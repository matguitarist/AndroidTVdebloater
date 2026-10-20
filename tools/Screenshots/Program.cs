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

// Usage: dotnet run --project tools/Screenshots -- <output folder> [--live <ip:port>]
// --live reads a real TV (read-only) and prints what the Choose page would offer.
var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "docs/screenshots");
if (args.Length >= 3 && args[1] == "--live")
{
    var adb = new AdbService();
    await adb.EnsureInstalledAsync(null);
    await adb.RunAsync(["connect", args[2]]);
    var dev = await TvDevice.LoadAsync(adb, args[2]);
    Console.WriteLine($"{dev.DisplayName}, Android {dev.AndroidVersion}, storage {dev.StorageFreeMb}/{dev.StorageTotalMb} MB, catalog v{Catalog.Version}");
    foreach (var g in Catalog.Build(dev))
        foreach (var t in g.Tweaks)
            Console.WriteLine($"  [{(t.IsSelected ? "x" : " ")}] {g.Id,-12} {t.Id,-50} {t.Description[..Math.Min(70, t.Description.Length)]}");
    var unknown = Catalog.UnknownSystemApps(dev);
    Console.WriteLine($"unknown: {unknown.Count}: {string.Join(", ", unknown)}");
    var (url, complete) = IssueReport.Build(dev, unknown, Catalog.Version);
    Console.WriteLine($"issue url {url.Length} chars, complete={complete}");
    return;
}
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
    c.Recent.Clear(); // never this PC's real TVs
    c.Recent.Add(new FoundTv("192.168.1.42") { Name = "Example Smart TV 4K" });
    c.Found.Add(new FoundTv("192.168.1.42") { Name = "android-livingroom.lan" });
    c.Found.Add(new FoundTv("192.168.1.57"));
    return c;
});
Shot("connect-waiting-fr", ThemeVariant.Light, "fr", m =>
{
    var c = new ConnectViewModel(m) { State = ConnectState.WaitingForApproval };
    c.Recent.Clear();
    return c;
});
// Choose pages use made-up "last choices", never the ones saved on this PC.
var demoChoices = new ChoiceSet
{
    Name = "Bedroom TV", Created = "2026-10-12T20:15:00-04:00",
    Choices = [new() { Id = "pkg:com.google.android.feedback" }, new() { Id = "animations", Option = "0.5" }, new() { Id = "wifiscan" }],
};
ChooseViewModel Choose(MainViewModel m, ChoiceSet? last) => new(m, tv) { LastChoices = last };
Shot("choose-light-en", ThemeVariant.Light, "en", m => Choose(m, demoChoices), 1500);
Shot("choose-dark-fr", ThemeVariant.Dark, "fr", m => Choose(m, null), 1500);
Shot("choose-full-light-en", ThemeVariant.Light, "en", m => Choose(m, null), 3400);
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

// Update dialog with made-up notes, and a real check against GitHub.
void UpdateShot(string name, ThemeVariant theme, string lang)
{
    Application.Current!.RequestedThemeVariant = theme;
    Loc.Instance.Language = lang;
    var main = new MainViewModel(new AdbService());
    main.Updater.Update = new UpdateInfo(new Version(1, 2, 0),
        "• Remembers the TVs you used before\n• Turns debugging off for you at the end\n• Suggests a home screen without ads",
        "", 0, "", "");
    main.Updater.Dialog = UpdateDialog.Available;
    var w = new MainWindow { DataContext = main, Width = 1140, Height = 800 };
    w.Show();
    Dispatcher.UIThread.RunJobs();
    w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, name + ".png"));
    w.Close();
    Console.WriteLine(name);
}
UpdateShot("update-light-en", ThemeVariant.Light, "en");
UpdateShot("update-dark-fr", ThemeVariant.Dark, "fr");
// Off the UI thread: awaiting on it here would wait for a dispatcher nobody is running.
Console.WriteLine($"current {UpdateService.Current}; check: {Task.Run(() => UpdateService.CheckAsync()).Result}");
Console.WriteLine($"check as 0.9.0: {Task.Run(() => UpdateService.CheckAsync(new Version(0, 9, 0))).Result}");

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

// macOS icon: the same logo on Apple's grid (824 px rounded square centred in 1024, with a soft shadow).
{
    const int size = 1024;
    var tile = new Avalonia.Controls.Border
    {
        Width = 824, Height = 824,
        CornerRadius = new CornerRadius(185),
        Background = new Avalonia.Media.LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new Avalonia.Media.GradientStop(Avalonia.Media.Color.Parse("#3B72F0"), 0),
                new Avalonia.Media.GradientStop(Avalonia.Media.Color.Parse("#1F4FC4"), 1),
            },
        },
        BoxShadow = Avalonia.Media.BoxShadows.Parse("0 10 24 0 #40000000"),
        Child = new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M3 5 H21 V16 H3 Z M8 20 H16 M12 16 V20 M9 9.5 L11 11.5 L15 7.5"),
            Stroke = Avalonia.Media.Brushes.White,
            // Stroke width is in pixels, not scaled with the shape.
            StrokeThickness = 42,
            StrokeLineCap = Avalonia.Media.PenLineCap.Round,
            StrokeJoin = Avalonia.Media.PenLineJoin.Round,
            Stretch = Avalonia.Media.Stretch.Uniform,
            Width = 500, Height = 500,
        },
    };
    var canvas = new Avalonia.Controls.Panel { Width = size, Height = size, Children = { tile } };
    tile.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
    tile.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    canvas.Measure(new Size(size, size));
    canvas.Arrange(new Rect(0, 0, size, size));
    using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(size, size));
    bmp.Render(canvas);
    bmp.Save(Path.Combine(outDir, "mac-icon-1024.png"));
    Console.WriteLine("mac icon");
}
