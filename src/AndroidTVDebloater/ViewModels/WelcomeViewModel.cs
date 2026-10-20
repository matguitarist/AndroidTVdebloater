using AndroidTVDebloater.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVDebloater.ViewModels;

public sealed partial class WelcomeViewModel(MainViewModel main) : PageViewModel(main)
{
    public override int StepIndex => 0;

    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _error = "";

    public bool ToolsReady => Main.Adb.IsInstalled;
    public string ToolsStatus => Loc.Get(ToolsReady ? "Welcome_ToolsReady" : "Welcome_ToolsMissing");

    public override void OnLanguageChanged() => OnPropertyChanged(nameof(ToolsStatus));

    [RelayCommand]
    private async Task ContinueAsync()
    {
        Error = "";
        if (!Main.Adb.IsInstalled)
        {
            IsDownloading = true;
            try
            {
                await Main.Adb.EnsureInstalledAsync(new Progress<double>(p => DownloadProgress = p * 100));
            }
            catch (Exception ex)
            {
                Error = Loc.Format("Welcome_DownloadFailed", ex.Message);
                return;
            }
            finally
            {
                IsDownloading = false;
                OnPropertyChanged(nameof(ToolsReady));
                OnPropertyChanged(nameof(ToolsStatus));
            }
        }
        Main.GoConnect();
    }
}
