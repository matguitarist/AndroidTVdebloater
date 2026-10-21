using AndroidTVDebloater.Localization;
using AndroidTVDebloater.Models;
using AndroidTVDebloater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVDebloater.ViewModels;

public sealed partial class LauncherOption : ObservableObject
{
    public required string Package { get; init; }
    public required string Name { get; init; }
    public required LocText DescriptionText { get; init; }
    public string Description => DescriptionText.ToString();
    /// <summary>"owner/name" when the .apk is also published on GitHub, so it can be installed without the Play Store.</summary>
    public string? GitHubRepo { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall), nameof(CanMakeHome), nameof(CanInstallFromGitHub))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMakeHome))]
    private bool _isCurrent;

    public bool CanInstall => !IsInstalled;
    public bool CanInstallFromGitHub => !IsInstalled && GitHubRepo is not null;
    public bool CanMakeHome => IsInstalled && !IsCurrent;

    public void RefreshTexts() => OnPropertyChanged(nameof(Description));
}

/// <summary>
/// Suggests a home screen without ads, opens it in the Play Store on the TV (or installs it from
/// GitHub), and makes it the default. The original home screen is remembered on this PC so it can be put back.
/// Nothing here runs adb until the user clicks.
/// </summary>
public sealed partial class HomeScreenViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly TvDevice _device;

    public List<LauncherOption> Launchers { get; } =
    [
        new()
        {
            Package = "com.spocky.projengmenu", Name = "Projectivy Launcher",
            DescriptionText = new("Clean and very customisable, no ads. It can take over the Home button, even on Google TV.",
                                  "Épuré et très personnalisable, sans publicité. Il peut prendre le contrôle du bouton Accueil, même sur Google TV."),
        },
        new()
        {
            Package = "me.efesser.flauncher", Name = "FLauncher",
            DescriptionText = new("Simple and open source: just your apps, in rows you arrange. No ads, no tracking.",
                                  "Simple et open source : juste vos applications, en rangées que vous organisez. Sans publicité ni pistage."),
        },
        new()
        {
            Package = "com.overdevs.at4k", Name = "AT4K Launcher", GitHubRepo = "avadhesh18/at4k",
            DescriptionText = new("Simple and clean, inspired by Apple TV: your favourite apps with what to watch next. No ads, no tracking.",
                                  "Simple et épuré, inspiré de l'Apple TV : vos applications favorites et ce qu'il y a à regarder ensuite. Sans publicité ni pistage."),
        },
    ];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestoreOriginal))]
    private string _currentHome;

    /// <summary>The TV's own home screen, saved before the first change.</summary>
    private string _originalHome;

    public bool HasPlayStore { get; }
    public bool IsGoogleTv { get; }
    public bool CanRestoreOriginal => _originalHome.Length > 0 && CurrentHome != _originalHome;

    private string OriginalFile => Path.Combine(Backup.FolderFor(_device), "original-home.txt");

    public HomeScreenViewModel(MainViewModel main, TvDevice device)
    {
        _main = main;
        _device = device;
        _currentHome = device.HomePackage;
        HasPlayStore = device.Enabled.Contains("com.android.vending");
        IsGoogleTv = device.IsInstalled("com.google.android.apps.tv.launcherx");
        try { _originalHome = File.Exists(OriginalFile) ? File.ReadAllText(OriginalFile).Trim() : ""; }
        catch { _originalHome = ""; }
        UpdateStates(device.Enabled);
    }

    public void RefreshTexts()
    {
        foreach (var l in Launchers) l.RefreshTexts();
    }

    private void UpdateStates(ICollection<string> installed)
    {
        foreach (var l in Launchers)
        {
            l.IsInstalled = installed.Contains(l.Package);
            l.IsCurrent = l.Package == CurrentHome;
        }
    }

    [RelayCommand]
    private Task OpenStoreAsync(LauncherOption l) => Busy(async () =>
    {
        var r = await _main.Adb.ShellAsync(_device.Serial,
            $"am start -a android.intent.action.VIEW -d market://details?id={l.Package}");
        return r.All.Contains("Error", StringComparison.OrdinalIgnoreCase)
            ? Loc.Get("Home_StoreFailed")
            : Loc.Format("Home_StoreOpened", l.Name);
    });

    [RelayCommand]
    private Task InstallFromGitHubAsync(LauncherOption l) => Busy(async () =>
    {
        Status = Loc.Format("Home_Downloading", l.Name);
        string apk;
        try { apk = await GitHubApk.DownloadLatestAsync(l.GitHubRepo!); }
        catch (Exception ex) { return Loc.Format("Home_DownloadFailed", l.Name, ex.Message); }
        try
        {
            Status = Loc.Format("Install_Installing", l.Name);
            var r = await _main.Adb.RunAsync(["-s", _device.Serial, "install", "-r", apk], 10 * 60 * 1000);
            await RefreshAsync();
            return r.All.Contains("Success")
                ? Loc.Format("Home_GitHubDone", l.Name)
                : Loc.Format("Install_Failed", r.All.Split('\n').LastOrDefault(x => x.Contains("Failure") || x.Contains("INSTALL_")) ?? r.All);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(apk)!, true); } catch { }
        }
    });

    [RelayCommand]
    private Task CheckAgainAsync() => Busy(async () =>
    {
        await RefreshAsync();
        return "";
    });

    [RelayCommand]
    private Task MakeHomeAsync(LauncherOption l) => Busy(async () =>
    {
        // "android" is the "choose an app" dialog, not a home screen worth going back to.
        if (_originalHome.Length == 0 && CurrentHome.Length > 0 && CurrentHome != "android")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OriginalFile)!);
            await File.WriteAllTextAsync(OriginalFile, CurrentHome);
            _originalHome = CurrentHome;
        }
        await _main.Adb.ShellAsync(_device.Serial, $"cmd package set-home-activity {l.Package}");
        await RefreshAsync();
        return CurrentHome == l.Package
            ? Loc.Format(IsGoogleTv ? "Home_SetOkGoogleTv" : "Home_SetOk", l.Name)
            : Loc.Format("Home_SetFailed", l.Name);
    });

    [RelayCommand]
    private Task RestoreOriginalAsync() => Busy(async () =>
    {
        await _main.Adb.ShellAsync(_device.Serial, $"cmd package set-home-activity {_originalHome}");
        await RefreshAsync();
        return CurrentHome == _originalHome ? Loc.Get("Home_RestoredOk") : Loc.Get("Home_RestoreFailed");
    });

    private async Task RefreshAsync()
    {
        var serial = _device.Serial;
        var home = await _main.Adb.ShellAsync(serial,
            "cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.HOME");
        var line = home.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        CurrentHome = line.Contains('/') ? line[..line.IndexOf('/')].Trim() : "";

        var installed = new HashSet<string>();
        foreach (var l in Launchers)
        {
            var r = await _main.Adb.ShellAsync(serial, $"pm list packages -e {l.Package}");
            if (r.Output.Split('\n').Any(x => x.Trim() == "package:" + l.Package)) installed.Add(l.Package);
        }
        UpdateStates(installed);
        OnPropertyChanged(nameof(CanRestoreOriginal));
    }

    private async Task Busy(Func<Task<string>> work)
    {
        IsBusy = true;
        Status = "";
        try { Status = await work(); }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }
}
