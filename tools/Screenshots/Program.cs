using AndroidTVDebloater;
using AndroidTVDebloater.Localization;
using AndroidTVDebloater.Models;
using AndroidTVDebloater.Services;
using AndroidTVDebloater.ViewModels;
using AndroidTVDebloater.Views;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;

// Usage: dotnet run --project tools/Screenshots -- <output folder>
var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "docs/screenshots");
Directory.CreateDirectory(outDir);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

// A made-up TV with a typical mix of bloat.
var tv = new TvDevice
{
    Serial = "192.168.1.42:5555",
    Manufacturer = "Example", Brand = "Example", Model = "Smart TV 4K", SerialNo = "DEMO",
    AndroidVersion = "11", Sdk = 30, RamMb = 2875, Network = NetworkKind.Ethernet,
    HomePackage = "com.example.launcher", SetupComplete = true,
    ScreensaverComponent = "com.google.android.backdrop/.Backdrop",
    Enabled =
    [
        "com.google.android.feedback", "com.google.android.tvrecommendations", "tv.samba.ssm",
        "android.autoinstalls.config.example.device", "com.google.android.play.games",
        "com.google.android.backdrop", "com.google.android.tungsten.setupwraith", "com.android.printspooler",
        "com.google.android.marvin.talkback", "com.google.android.katniss", "com.google.android.apps.mediashell",
        "com.netflix.ninja", "com.google.android.youtube.tv", "com.amazon.amazonvideo.livingroom",
        "com.android.dreams.basic", "com.example.launcher", "org.example.speedtest",
        "com.android.vending", "com.spocky.projengmenu",
    ],
    Disabled = ["com.example.oldapp"],
    ThirdParty = ["org.example.speedtest", "com.netflix.ninja"],
};

void Shot(string name, ThemeVariant theme, string lang, Func<MainViewModel, PageViewModel> page, int height = 800)
{
    Application.Current!.RequestedThemeVariant = theme;
    Loc.Instance.Language = lang;
    var main = new MainViewModel(new AdbService()) { Device = tv };
    main.CurrentPage = page(main);
    var w = new MainWindow { DataContext = main, Width = 1140, Height = height };
    w.Show();
    Dispatcher.UIThread.RunJobs();
    var frame = w.CaptureRenderedFrame();
    frame?.Save(Path.Combine(outDir, name + ".png"));
    w.Close();
    Console.WriteLine(name);
}

Shot("welcome-light-en", ThemeVariant.Light, "en", m => new WelcomeViewModel(m), 980);
Shot("welcome-dark-fr", ThemeVariant.Dark, "fr", m => new WelcomeViewModel(m), 980);
Shot("apply-light-fr", ThemeVariant.Light, "fr", m =>
{
    var a = new ApplyViewModel(m, tv, [], false) { AppsOff = 12, SettingsChanged = 5, BackupPath = @"C:\Users\vous\AppData\Local\AndroidTVDebloater\backups\Example" };
    a.HomeScreen = new HomeScreenViewModel(m, tv);
    a.IsFinished = true;
    return a;
}, 1500);
Shot("connect-light-en", ThemeVariant.Light, "en", m =>
{
    var c = new ConnectViewModel(m);
    c.Recent.Add(new FoundTv("192.168.1.42") { Name = "Example Smart TV 4K" });
    c.Found.Add(new FoundTv("192.168.1.42") { Name = "android-livingroom.lan" });
    c.Found.Add(new FoundTv("192.168.1.57"));
    return c;
});
Shot("connect-waiting-fr", ThemeVariant.Light, "fr", m => new ConnectViewModel(m) { State = ConnectState.WaitingForApproval });
Shot("choose-light-en", ThemeVariant.Light, "en", m => new ChooseViewModel(m, tv), 1500);
Shot("choose-dark-fr", ThemeVariant.Dark, "fr", m => new ChooseViewModel(m, tv), 1500);
Shot("choose-full-light-en", ThemeVariant.Light, "en", m => new ChooseViewModel(m, tv), 3400);
Shot("apply-dark-en", ThemeVariant.Dark, "en", m =>
{
    var a = new ApplyViewModel(m, tv, [], false);
    foreach (var (text, state, detail) in new[]
             {
                 ("Saving a backup on this PC", StepState.Done, ""),
                 ("Google Feedback", StepState.Done, ""),
                 ("Samba TV", StepState.Done, ""),
                 ("Google recommendations", StepState.Done, ""),
                 ("Block ads and trackers (DNS)", StepState.Done, "dns.adguard-dns.com"),
                 ("Restarting the TV", StepState.Done, ""),
                 ("Checking home screen and internet", StepState.Done, "Home screen opens, internet and name lookups work."),
             })
        a.Items.Add(new StepItem(text) { State = state, Detail = detail });
    a.BackupPath = @"C:\Users\you\AppData\Local\AndroidTVDebloater\backups\Example_Smart_TV_4K-DEMO";
    a.AppsOff = 3;
    a.SettingsChanged = 1;
    a.HomeScreen = new HomeScreenViewModel(m, tv);
    a.IsFinished = true;
    return a;
}, 1500);
Shot("apply-working-light-fr", ThemeVariant.Light, "fr", m =>
{
    var a = new ApplyViewModel(m, tv, [], false);
    a.Items.Add(new StepItem("Enregistrement d'une sauvegarde sur ce PC") { State = StepState.Done });
    a.Items.Add(new StepItem("Commentaires Google") { State = StepState.Done });
    a.Items.Add(new StepItem("Samba TV") { State = StepState.Running });
    a.Items.Add(new StepItem("Recommandations Google"));
    a.Items.Add(new StepItem("Redémarrage du téléviseur"));
    a.Progress = 40;
    return a;
});
Shot("restore-light-en", ThemeVariant.Light, "en", m => new RestoreViewModel(m, tv));

// App icon: the sidebar logo at Windows icon sizes (packed into Assets/app.ico by tools/make-ico.py).
foreach (var size in new[] { 16, 24, 32, 48, 64, 256 })
{
    var radius = size * 0.22;
    var logo = new Avalonia.Controls.Border
    {
        Width = size, Height = size,
        CornerRadius = new CornerRadius(radius),
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2459D6")),
        Child = new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M3 5 H21 V16 H3 Z M8 20 H16 M12 16 V20 M9 9.5 L11 11.5 L15 7.5"),
            Stroke = Avalonia.Media.Brushes.White,
            StrokeThickness = Math.Max(1.6, size * 0.055),
            StrokeLineCap = Avalonia.Media.PenLineCap.Round,
            StrokeJoin = Avalonia.Media.PenLineJoin.Round,
            Stretch = Avalonia.Media.Stretch.Uniform,
            Width = size * 0.6, Height = size * 0.6,
        },
    };
    logo.Measure(new Size(size, size));
    logo.Arrange(new Rect(0, 0, size, size));
    using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(size, size));
    bmp.Render(logo);
    bmp.Save(Path.Combine(outDir, $"icon-{size}.png"));
}
Console.WriteLine("icons");
