using AndroidTVDebloater.Services;
using AndroidTVDebloater.ViewModels;
using AndroidTVDebloater.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AndroidTVDebloater;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            UpdateService.CleanUpAfterUpdate();
            var main = new MainViewModel(new AdbService());
            desktop.MainWindow = new MainWindow { DataContext = main };
            _ = main.Updater.CheckInBackgroundAsync();
            // Leave no adb server running in the background once the app is closed.
            desktop.Exit += (_, _) => main.Adb.KillServer();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
