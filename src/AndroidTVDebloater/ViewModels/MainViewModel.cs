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
    /// <summary>A finished step the user can click to go back to.</summary>
    [ObservableProperty] private bool _canNavigate;
    public void Refresh() => OnPropertyChanged(nameof(Title));
}

public sealed partial class MainViewModel : ObservableObject
{
    public AdbService Adb { get; }
    public Loc Loc => Loc.Instance;
    public string Version => "v" + UpdateService.Current;
    public UpdaterViewModel Updater { get; }

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
        Updater = new UpdaterViewModel(this);
        RefreshSteps();
        Loc.Instance.LanguageChanged += () =>
        {
            foreach (var s in Steps) s.Refresh();
            CurrentPage.OnLanguageChanged();
            Updater.OnLanguageChanged();
        };
    }

    partial void OnCurrentPageChanged(PageViewModel value) => RefreshSteps();

    /// <summary>Changes are being made to the TV: don't let the user leave the page.</summary>
    public bool IsBusy => CurrentPage is ApplyViewModel { IsFinished: false } or RestoreViewModel { IsBusy: true };

    public void RefreshSteps()
    {
        foreach (var s in Steps)
        {
            s.IsActive = s.Number - 1 == CurrentPage.StepIndex;
            s.IsDone = s.Number - 1 < CurrentPage.StepIndex;
            s.CanNavigate = s.IsDone && !IsBusy && s.Number < 4 && (s.Number != 3 || HasDevice);
        }
    }

    [RelayCommand]
    private async Task GoToStepAsync(StepInfo step)
    {
        if (!step.CanNavigate) return;
        switch (step.Number)
        {
            case 1: CurrentPage = new WelcomeViewModel(this); break;
            case 2: GoConnect(); break;
            case 3:
                try { await GoChooseAsync(reload: true); }
                catch { GoConnect(); }
                break;
        }
    }

    /// <summary>Back to the first page with no TV connected, e.g. to clean another one.</summary>
    public async Task StartOverAsync()
    {
        if (Device is { } d && d.Serial.Contains(':'))
            await Adb.RunAsync(["disconnect", d.Serial], 5000);
        Device = null;
        CurrentPage = new WelcomeViewModel(this);
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

    partial void OnDeviceChanged(TvDevice? value)
    {
        GoRestoreCommand.NotifyCanExecuteChanged();
        RefreshSteps();
    }
}
