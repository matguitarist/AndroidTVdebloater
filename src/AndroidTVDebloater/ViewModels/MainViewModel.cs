using System.Collections.ObjectModel;
using AndroidTVDebloater.Localization;
using AndroidTVDebloater.Models;
using AndroidTVDebloater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVDebloater.ViewModels;

public abstract class PageViewModel(MainViewModel main) : ObservableObject
{
    protected MainViewModel Main { get; } = main;
    public abstract int StepIndex { get; }
    public virtual void OnLanguageChanged() { }
}

public sealed partial class StepInfo(int number, string titleKey) : ObservableObject
{
    public int Number { get; } = number;
    public string TitleKey { get; } = titleKey;
    public string Title => Loc.Get(TitleKey);
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isDone;
    public void Refresh() => OnPropertyChanged(nameof(Title));
}

public sealed partial class MainViewModel : ObservableObject
{
    public AdbService Adb { get; }
    public Loc Loc => Loc.Instance;
    public string Version => "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    public ObservableCollection<StepInfo> Steps { get; } =
    [
        new(1, "Step_Prepare"), new(2, "Step_Connect"), new(3, "Step_Choose"), new(4, "Step_Apply"),
    ];

    [ObservableProperty] private PageViewModel _currentPage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDevice), nameof(DeviceName), nameof(DeviceAddress))]
    private TvDevice? _device;

    public bool HasDevice => Device is not null;
    public string DeviceName => Device?.DisplayName ?? "";
    public string DeviceAddress => Device?.IpAddress ?? "";

    public MainViewModel(AdbService adb)
    {
        Adb = adb;
        _currentPage = new WelcomeViewModel(this);
        RefreshSteps();
        Loc.Instance.LanguageChanged += () =>
        {
            foreach (var s in Steps) s.Refresh();
            CurrentPage.OnLanguageChanged();
        };
    }

    partial void OnCurrentPageChanged(PageViewModel value) => RefreshSteps();

    public void RefreshSteps()
    {
        foreach (var s in Steps)
        {
            s.IsActive = s.Number - 1 == CurrentPage.StepIndex;
            s.IsDone = s.Number - 1 < CurrentPage.StepIndex;
        }
    }

    public void GoConnect() => CurrentPage = new ConnectViewModel(this);

    public async Task GoChooseAsync(bool reload = false)
    {
        if (Device is null) { GoConnect(); return; }
        if (reload) Device = await TvDevice.LoadAsync(Adb, Device.Serial);
        CurrentPage = new ChooseViewModel(this, Device);
    }

    public void GoApply(List<Tweak> selected, bool reboot)
    {
        if (Device is null) return;
        var page = new ApplyViewModel(this, Device, selected, reboot);
        CurrentPage = page;
        _ = page.RunAsync();
    }

    [RelayCommand(CanExecute = nameof(HasDevice))]
    private void GoRestore()
    {
        if (Device is not null) CurrentPage = new RestoreViewModel(this, Device);
    }

    partial void OnDeviceChanged(TvDevice? value) => GoRestoreCommand.NotifyCanExecuteChanged();
}
