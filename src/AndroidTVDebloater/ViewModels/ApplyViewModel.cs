using System.Collections.ObjectModel;
using System.Diagnostics;
using AndroidTVDebloater.Localization;
using AndroidTVDebloater.Models;
using AndroidTVDebloater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVDebloater.ViewModels;

public enum StepState { Pending, Running, Done, Failed, Skipped }

public sealed partial class StepItem(string text) : ObservableObject
{
    [ObservableProperty] private string _text = text;
    [ObservableProperty] private string _detail = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon), nameof(IsRunning), nameof(IsDone), nameof(IsFailed), nameof(IsPending), nameof(HasDetail))]
    private StepState _state;

    public bool IsRunning => State == StepState.Running;
    public bool IsDone => State == StepState.Done;
    public bool IsFailed => State is StepState.Failed or StepState.Skipped;
    public bool IsPending => State == StepState.Pending;
    public bool HasDetail => Detail.Length > 0;
    public string Icon => State switch { StepState.Done => "✓", StepState.Failed => "✕", StepState.Skipped => "–", _ => "" };
    partial void OnDetailChanged(string value) => OnPropertyChanged(nameof(HasDetail));
}

public sealed partial class ApplyViewModel(MainViewModel main, TvDevice device, List<Tweak> selected, bool reboot)
    : PageViewModel(main)
{
    public override int StepIndex => IsFinished ? 4 : 3;

    public ObservableCollection<StepItem> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle))]
    private bool _isFinished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle))]
    private int _problemCount;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _backupPath = "";
    [ObservableProperty] private int _appsOff;
    [ObservableProperty] private int _settingsChanged;
    [ObservableProperty] private HomeScreenViewModel? _homeScreen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StillConnected), nameof(ShowHomeScreen))]
    private bool _debugOff;

    [ObservableProperty] private bool _isTurningOffDebug;
    [ObservableProperty] private string _debugStatus = "";

    public bool HasProblems => ProblemCount > 0;
    public bool StillConnected => !DebugOff;
    public bool ShowHomeScreen => HomeScreen is { HasPlayStore: true } && !DebugOff;
    partial void OnHomeScreenChanged(HomeScreenViewModel? value) => OnPropertyChanged(nameof(ShowHomeScreen));

    public string CheckValue => ProblemCount == 0 ? "OK" : ProblemCount.ToString();
    public string CheckLabel => Loc.Get(ProblemCount > 0 ? "Apply_StatProblems" : reboot ? "Apply_StatChecks" : "Apply_StatChecksNoReboot");
    partial void OnProblemCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(CheckValue));
        OnPropertyChanged(nameof(CheckLabel));
    }

    public string Title => !IsFinished ? Loc.Get("Apply_Working")
        : ProblemCount == 0 ? Loc.Get("Apply_DoneTitle") : Loc.Get("Apply_DoneWithProblems");

    public string Subtitle => !IsFinished ? Loc.Get("Apply_WorkingSub")
        : ProblemCount == 0 ? Loc.Get("Apply_DoneSub") : Loc.Format("Apply_ProblemsSub", ProblemCount);

    public override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CheckLabel));
        HomeScreen?.RefreshTexts();
    }

    private TvDevice _device = device;

    public async Task RunAsync()
    {
        var backupStep = Add(Loc.Get("Apply_Backup"));
        var tweakSteps = selected.Select(t => (tweak: t, step: Add(t.Title))).ToList();
        var rebootStep = reboot ? Add(Loc.Get("Apply_Reboot")) : null;
        var checkStep = Add(Loc.Get("Apply_Check"));
        var total = Items.Count;
        var done = 0;
        void Tick() => Progress = 100.0 * ++done / total;

        // 1. Backup
        backupStep.State = StepState.Running;
        var backup = Backup.Start(_device);
        try
        {
            backup.Save();
            await SaveSnapshotAsync(backup);
            BackupPath = Path.GetDirectoryName(backup.FilePath)!;
            backupStep.State = StepState.Done;
        }
        catch (Exception ex)
        {
            // No backup, no changes.
            backupStep.Detail = ex.Message;
            backupStep.State = StepState.Failed;
            foreach (var (_, step) in tweakSteps) step.State = StepState.Skipped;
            ProblemCount = 1;
            IsFinished = true;
            return;
        }
        Tick();

        // 2. Changes, packages first so the screensaver fix below can see what went.
        var ctx = new ApplyContext(Main.Adb, _device, backup);
        var kept = selected.SelectMany(t => t.KeptPackages).ToHashSet();
        foreach (var (tweak, step) in tweakSteps.OrderBy(x => x.tweak is PackageTweak ? 0 : 1))
        {
            if (tweak is PackageTweak p && kept.Contains(p.Package))
            {
                // A choice in the same run needs this app (e.g. the screensaver): leave it on.
                step.Detail = Loc.Get("Apply_Kept");
                step.State = StepState.Skipped;
                Tick();
                continue;
            }
            step.State = StepState.Running;
            try
            {
                var r = await tweak.ApplyAsync(ctx);
                step.Detail = r.Detail;
                step.State = r.Success ? StepState.Done : StepState.Failed;
            }
            catch (Exception ex)
            {
                step.Detail = ex.Message;
                step.State = StepState.Failed;
            }
            if (step.State == StepState.Failed) ProblemCount++;
            else if (tweak is PackageTweak) AppsOff++;
            else if (tweak is not (TrimCachesTweak or OptimizeAppsTweak)) SettingsChanged++;
            backup.Save();
            Tick();
        }

        // If the active screensaver's app was just turned off, switch to the simple one.
        var dreamPkg = _device.ScreensaverComponent.Split('/')[0];
        if (dreamPkg.Length > 0 && backup.DisabledPackages.Contains(dreamPkg) && _device.IsInstalled("com.android.dreams.basic"))
        {
            await SettingsIO.ChangeAsync(ctx, "secure", "screensaver_components", Catalog.BasicScreensaver);
            backup.Save();
        }

        // 3. Restart and wait for the TV to come back.
        if (rebootStep is not null)
        {
            rebootStep.State = StepState.Running;
            var back = await RebootAndWaitAsync(rebootStep);
            rebootStep.State = back ? StepState.Done : StepState.Failed;
            if (!back) ProblemCount++;
            Tick();
        }

        // 4. Check the essentials: home screen, internet, name lookups.
        checkStep.State = StepState.Running;
        var problems = await CheckAsync();
        checkStep.Detail = problems.Count == 0 ? Loc.Get("Apply_CheckOk") : string.Join("\n", problems);
        checkStep.State = problems.Count == 0 ? StepState.Done : StepState.Failed;
        if (problems.Count > 0) ProblemCount++;
        Tick();

        IsFinished = true;
        Main.RefreshSteps();
        try
        {
            Main.Device = _device = await TvDevice.LoadAsync(Main.Adb, _device.Serial);
            HomeScreen = new HomeScreenViewModel(Main, _device);
        }
        catch { /* the TV went away; the summary above still stands */ }
    }

    private StepItem Add(string text)
    {
        var s = new StepItem(text);
        Items.Add(s);
        return s;
    }

    /// <summary>Raw lists of packages and settings next to the change file, for reference.</summary>
    private async Task SaveSnapshotAsync(Backup backup)
    {
        var folder = Path.GetDirectoryName(backup.FilePath)!;
        var stamp = backup.Created.ToString("yyyyMMdd-HHmmss");
        var parts = new List<string>();
        foreach (var (title, cmd) in new[]
                 {
                     ("packages (all)", "pm list packages -f"),
                     ("packages (disabled)", "pm list packages -d"),
                     ("settings global", "settings list global"),
                     ("settings secure", "settings list secure"),
                     ("settings system", "settings list system"),
                 })
        {
            var r = await Main.Adb.ShellAsync(_device.Serial, cmd, 30000);
            parts.Add($"===== {title} =====\n{r.Output}");
        }
        await File.WriteAllTextAsync(Path.Combine(folder, $"snapshot-{stamp}.txt"), string.Join("\n", parts));
    }

    private async Task<bool> RebootAndWaitAsync(StepItem step)
    {
        var serial = _device.Serial;
        await Main.Adb.RunAsync(["-s", serial, "reboot"], 15000);
        await Task.Delay(8000);
        var deadline = DateTime.UtcNow.AddMinutes(4);
        while (DateTime.UtcNow < deadline)
        {
            if (serial.Contains(':')) await Main.Adb.RunAsync(["connect", serial], 10000);
            if (await Main.Adb.GetStateAsync(serial) == "device")
            {
                var boot = await Main.Adb.ShellAsync(serial, "getprop sys.boot_completed", 8000);
                if (boot.Output.Trim() == "1")
                {
                    step.Detail = Loc.Get("Apply_RebootSettle");
                    await Task.Delay(20000); // let apps and the network settle
                    step.Detail = "";
                    return true;
                }
            }
            await Task.Delay(3000);
        }
        step.Detail = Loc.Get("Apply_RebootTimeout");
        return false;
    }

    private async Task<List<string>> CheckAsync()
    {
        var serial = _device.Serial;
        var problems = new List<string>();

        var home = await Main.Adb.ShellAsync(serial,
            "cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.HOME");
        if (!home.Output.Contains('/')) problems.Add(Loc.Get("Apply_CheckNoHome"));

        var ip = await Main.Adb.ShellAsync(serial, "ping -c 1 -W 3 8.8.8.8", 10000);
        if (!ip.Output.Contains(" 0% packet loss")) problems.Add(Loc.Get("Apply_CheckNoInternet"));
        else
        {
            var dns = await Main.Adb.ShellAsync(serial, "ping -c 1 -W 3 connectivitycheck.gstatic.com", 10000);
            if (!dns.Output.Contains("PING ")) problems.Add(Loc.Get("Apply_CheckNoDns"));
        }
        return problems;
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        if (BackupPath.Length == 0) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open",
                ArgumentList = { BackupPath },
                UseShellExecute = false,
            });
        }
        catch { }
    }

    [RelayCommand]
    private Task BackToChoicesAsync() => Main.GoChooseAsync(reload: true);

    [RelayCommand]
    private Task StartOverAsync() => Main.StartOverAsync();

    /// <summary>
    /// Turns USB and network debugging off from here. adbd stops as soon as the setting is
    /// written, so success is "the TV no longer answers", not the command's output.
    /// </summary>
    [RelayCommand]
    private async Task TurnOffDebuggingAsync()
    {
        IsTurningOffDebug = true;
        DebugStatus = "";
        var serial = _device.Serial;
        try
        {
            await Main.Adb.ShellAsync(serial, "settings put global adb_wifi_enabled 0; settings put global adb_enabled 0", 8000);
            await Task.Delay(3000);
            if (serial.Contains(':'))
            {
                await Main.Adb.RunAsync(["disconnect", serial], 5000);
                await Main.Adb.RunAsync(["connect", serial], 8000);
            }
            DebugOff = await Main.Adb.GetStateAsync(serial) != "device";
            if (DebugOff)
            {
                if (serial.Contains(':')) await Main.Adb.RunAsync(["disconnect", serial], 5000);
                Main.Device = null;
            }
            DebugStatus = Loc.Get(DebugOff ? "Apply_DebugOffDone" : "Apply_DebugOffFailed");
        }
        catch (Exception ex) { DebugStatus = ex.Message; }
        finally { IsTurningOffDebug = false; }
    }
}
