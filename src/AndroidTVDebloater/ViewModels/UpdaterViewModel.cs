using AndroidTVDebloater.Localization;
using AndroidTVDebloater.Services;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVDebloater.ViewModels;

public enum UpdateDialog { None, Available, UpToDate, Failed }

/// <summary>Checks for a new version at start-up (unless turned off) or on request, and installs it.</summary>
public sealed partial class UpdaterViewModel(MainViewModel main) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(ShowAvailable), nameof(ShowUpToDate), nameof(ShowFailed))]
    private UpdateDialog _dialog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(AvailableText), nameof(SidebarText), nameof(Notes), nameof(HasNotes))]
    private UpdateInfo? _update;

    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _error = "";

    public bool IsOpen => Dialog != UpdateDialog.None;
    public bool ShowAvailable => Dialog == UpdateDialog.Available;
    public bool ShowUpToDate => Dialog == UpdateDialog.UpToDate;
    public bool ShowFailed => Dialog == UpdateDialog.Failed;
    public bool HasUpdate => Update is not null;
    public bool CanInstallInPlace { get; } = UpdateService.CanInstallInPlace;
    public bool OpensBrowser => !CanInstallInPlace;

    public string CurrentVersion => UpdateService.Current.ToString();
    public string AvailableText => Update is null ? "" : Loc.Format("Update_Text", Update.Version, CurrentVersion);
    public string SidebarText => Update is null ? Loc.Get("Side_CheckUpdates") : Loc.Format("Side_UpdateAvailable", Update.Version);
    public string Notes => Update is { Notes.Length: > 0 } u ? u.Notes : Loc.Get("Update_NoNotes");
    public bool HasNotes => Update is not null;
    public string UpToDateText => Loc.Format("Update_UpToDate", CurrentVersion);
    public string InstallText => Loc.Get(CanInstallInPlace ? "Update_Install" : "Update_Download");

    public bool AutoCheck
    {
        get => AppSettings.Current.CheckForUpdates;
        set
        {
            AppSettings.Current.CheckForUpdates = value;
            AppSettings.Current.Save();
            OnPropertyChanged();
        }
    }

    public void OnLanguageChanged()
    {
        foreach (var name in new[] { nameof(AvailableText), nameof(SidebarText), nameof(Notes), nameof(UpToDateText), nameof(InstallText) })
            OnPropertyChanged(name);
    }

    /// <summary>Quiet check at start-up: only speaks up when there is something new and not skipped.</summary>
    public async Task CheckInBackgroundAsync()
    {
        if (!AppSettings.Current.CheckForUpdates) return;
        try
        {
            await Task.Delay(1500);
            var u = await UpdateService.CheckAsync();
            if (u is null) return;
            Update = u;
            if (AppSettings.Current.SkippedVersion != u.Version.ToString() && !main.IsBusy)
                Dialog = UpdateDialog.Available;
        }
        catch { /* offline: try again next time */ }
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (Update is not null) { Dialog = UpdateDialog.Available; return; }
        IsChecking = true;
        Error = "";
        try
        {
            Update = await UpdateService.CheckAsync();
            Dialog = Update is null ? UpdateDialog.UpToDate : UpdateDialog.Available;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Dialog = UpdateDialog.Failed;
        }
        finally { IsChecking = false; }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (Update is null) return;
        if (!CanInstallInPlace)
        {
            UpdateService.OpenInBrowser(Update.PageUrl);
            Dialog = UpdateDialog.None;
            return;
        }
        if (main.IsBusy)
        {
            Error = Loc.Get("Update_Busy");
            return;
        }
        IsDownloading = true;
        Error = "";
        try
        {
            await UpdateService.InstallAsync(Update, new Progress<double>(p => Progress = p * 100));
            // The new copy is starting; close this one (which also stops adb).
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (InvalidDataException)
        {
            Error = Loc.Get("Update_ChecksumFailed");
        }
        catch (Exception ex)
        {
            Error = Loc.Format("Update_InstallFailed", ex.Message);
        }
        finally { IsDownloading = false; }
    }

    [RelayCommand]
    private void Later()
    {
        Error = "";
        Dialog = UpdateDialog.None;
    }

    [RelayCommand]
    private void Skip()
    {
        if (Update is not null)
        {
            AppSettings.Current.SkippedVersion = Update.Version.ToString();
            AppSettings.Current.Save();
        }
        Later();
    }

    [RelayCommand]
    private void OpenReleasePage() => UpdateService.OpenInBrowser(Update?.PageUrl ?? UpdateService.ReleasesPage);
}
