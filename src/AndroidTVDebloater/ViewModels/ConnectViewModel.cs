using System.Collections.ObjectModel;
using System.Net;
using AndroidTVDebloater.Localization;
using AndroidTVDebloater.Models;
using AndroidTVDebloater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVDebloater.ViewModels;

public enum ConnectState { Idle, Connecting, WaitingForApproval, Reading, Failed }

public sealed partial class FoundTv(string address) : ObservableObject
{
    public string Address { get; } = address;
    [ObservableProperty] private string _name = "";
    public bool HasName => Name.Length > 0;
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(HasName));
}

public sealed partial class ConnectViewModel : PageViewModel
{
    public override int StepIndex => 1;

    public ObservableCollection<FoundTv> Found { get; } = [];
    public List<FoundTv> Recent { get; }
    public bool HasRecent => Recent.Count > 0;

    private readonly Dictionary<string, string> _recentNames;

    public ConnectViewModel(MainViewModel main) : base(main)
    {
        var recent = RecentTvs.Load();
        Recent = recent.Select(r => new FoundTv(DisplayAddress(r.Address)) { Name = r.Name }).ToList();
        _recentNames = recent.GroupBy(r => r.Address).ToDictionary(g => g.Key, g => g.First().Name);
    }

    /// <summary>"192.168.1.20:5555" → "192.168.1.20"; other ports are kept.</summary>
    private static string DisplayAddress(string serial) =>
        serial.EndsWith(":" + NetworkScanner.AdbPort) ? serial[..serial.LastIndexOf(':')] : serial;

    [ObservableProperty] private string _address = "";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _scanDone;
    [ObservableProperty] private double _scanProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsWaiting), nameof(HasError), nameof(StatusText), nameof(ShowStatus))]
    private ConnectState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _errorDetail = "";

    [ObservableProperty] private string _pairAddress = "";
    [ObservableProperty] private string _pairCode = "";
    [ObservableProperty] private string _pairResult = "";

    private CancellationTokenSource? _cts;

    public bool IsBusy => State is ConnectState.Connecting or ConnectState.WaitingForApproval or ConnectState.Reading;
    public bool IsWaiting => State == ConnectState.WaitingForApproval;
    public bool HasError => State == ConnectState.Failed;
    public bool ShowStatus => State != ConnectState.Idle && State != ConnectState.WaitingForApproval;
    public bool NothingFound => ScanDone && Found.Count == 0;

    public string StatusText => State switch
    {
        ConnectState.Connecting => Loc.Get("Connect_Connecting"),
        ConnectState.Reading => Loc.Get("Connect_Reading"),
        ConnectState.Failed => ErrorDetail,
        _ => "",
    };

    public override void OnLanguageChanged() => OnPropertyChanged(nameof(StatusText));

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        ScanDone = false;
        Found.Clear();
        OnPropertyChanged(nameof(NothingFound));
        try
        {
            var ips = await NetworkScanner.ScanAsync(new Progress<double>(p => ScanProgress = p * 100));
            foreach (var ip in ips)
            {
                var tv = new FoundTv(ip);
                Found.Add(tv);
                if (_recentNames.TryGetValue($"{ip}:{NetworkScanner.AdbPort}", out var known)) tv.Name = known;
                else _ = ResolveNameAsync(tv);
            }
        }
        finally
        {
            IsScanning = false;
            ScanDone = true;
            OnPropertyChanged(nameof(NothingFound));
        }
    }

    private static async Task ResolveNameAsync(FoundTv tv)
    {
        try
        {
            var lookup = Dns.GetHostEntryAsync(tv.Address);
            if (await Task.WhenAny(lookup, Task.Delay(2000)) == lookup && lookup.Result.HostName != tv.Address)
                tv.Name = lookup.Result.HostName;
        }
        catch { /* no name, the address is enough */ }
    }

    [RelayCommand]
    private Task ConnectFoundAsync(FoundTv tv) => ConnectToAsync(tv.Address);

    [RelayCommand]
    private Task ConnectManualAsync() => ConnectToAsync(Address);

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        State = ConnectState.Idle;
    }

    private async Task ConnectToAsync(string input)
    {
        var target = Normalize(input);
        if (target is null)
        {
            Fail(Loc.Get("Connect_BadAddress"));
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        State = ConnectState.Connecting;
        try
        {
            var r = await Main.Adb.RunAsync(["connect", target], 20000, ct);
            if (!r.All.Contains("connected to"))
            {
                Fail(Loc.Format("Connect_Refused", target) + "\n" + r.All);
                return;
            }

            // Wait for "Allow USB debugging?" to be accepted on the TV (up to 3 minutes).
            var deadline = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                var state = await Main.Adb.GetStateAsync(target);
                if (state == "device") break;
                if (state == "unauthorized") State = ConnectState.WaitingForApproval;
                if (state is null or "offline")
                    await Main.Adb.RunAsync(["connect", target], 15000, ct);
                await Task.Delay(1500, ct);
            }
            if (await Main.Adb.GetStateAsync(target) != "device")
            {
                Fail(Loc.Get("Connect_ApprovalTimeout"));
                return;
            }

            State = ConnectState.Reading;
            var device = await TvDevice.LoadAsync(Main.Adb, target);
            RecentTvs.Remember(target, device.DisplayName);
            Main.Device = device;
            await Main.GoChooseAsync();
        }
        catch (OperationCanceledException) { State = ConnectState.Idle; }
        catch (Exception ex) { Fail(ex.Message); }
    }

    [RelayCommand]
    private async Task PairAsync()
    {
        PairResult = "";
        if (Normalize(PairAddress, requirePort: true) is not { } target || PairCode.Trim().Length < 6)
        {
            PairResult = Loc.Get("Connect_PairBadInput");
            return;
        }
        var r = await Main.Adb.RunAsync(["pair", target, PairCode.Trim()], 30000);
        PairResult = r.All.Contains("Successfully paired")
            ? Loc.Get("Connect_PairOk")
            : Loc.Get("Connect_PairFailed") + "\n" + r.All;
    }

    private void Fail(string message)
    {
        ErrorDetail = message;
        State = ConnectState.Failed;
    }

    /// <summary>"192.168.1.20" → "192.168.1.20:5555". Accepts a comma typed instead of a dot.</summary>
    public static string? Normalize(string input, bool requirePort = false)
    {
        var s = input.Trim().Replace(',', '.').Replace(" ", "");
        if (s.Length == 0) return null;
        var host = s;
        var port = NetworkScanner.AdbPort;
        var colon = s.LastIndexOf(':');
        if (colon > 0)
        {
            host = s[..colon];
            if (!int.TryParse(s[(colon + 1)..], out port) || port is < 1 or > 65535) return null;
        }
        else if (requirePort) return null;
        return IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? $"{ip}:{port}"
            : null;
    }
}
