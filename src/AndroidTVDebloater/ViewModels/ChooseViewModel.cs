using System.ComponentModel;
using AndroidTVDebloater.Localization;
using AndroidTVDebloater.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidTVDebloater.ViewModels;

public sealed partial class ChooseViewModel : PageViewModel
{
    public override int StepIndex => 2;

    public TvDevice Device { get; }
    public List<TweakGroup> Groups { get; }

    [ObservableProperty] private bool _rebootAfter = true;

    public ChooseViewModel(MainViewModel main, TvDevice device) : base(main)
    {
        Device = device;
        Groups = Catalog.Build(device);
        foreach (var t in AllTweaks) t.PropertyChanged += OnTweakChanged;
    }

    private IEnumerable<Tweak> AllTweaks => Groups.SelectMany(g => g.Tweaks);

    public int SelectedCount => AllTweaks.Count(t => t.IsSelected);
    public string SelectedText => Loc.Format(SelectedCount == 1 ? "Choose_SelectedOne" : "Choose_Selected", SelectedCount);
    public string ApplyText => Loc.Format("Choose_Apply", SelectedCount);
    public bool CanApply => SelectedCount > 0;

    public string AndroidText => Loc.Format("Choose_Android", Device.AndroidVersion);
    public string RamText => Loc.Format("Choose_Ram", (Device.RamMb / 1024.0).ToString("0.#"));
    public string NetworkText => Loc.Get(Device.Network switch
    {
        NetworkKind.Ethernet => "Choose_NetEthernet",
        NetworkKind.WiFi => "Choose_NetWifi",
        _ => "Choose_NetUnknown",
    });
    public string AlreadyOffText => Device.Disabled.Count == 1 ? Loc.Get("Choose_AlreadyOffOne") : Loc.Format("Choose_AlreadyOff", Device.Disabled.Count);
    public bool HasAlreadyOff => Device.Disabled.Count > 0;

    private void OnTweakChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Tweak.IsSelected)) return;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(ApplyText));
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    public override void OnLanguageChanged()
    {
        foreach (var g in Groups) g.RefreshTexts();
        foreach (var name in new[] { nameof(SelectedText), nameof(ApplyText), nameof(AndroidText), nameof(RamText), nameof(NetworkText), nameof(AlreadyOffText) })
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var t in AllTweaks) t.IsSelected = t.Recommended;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var t in AllTweaks) t.IsSelected = false;
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => Main.GoApply(AllTweaks.Where(t => t.IsSelected).ToList(), RebootAfter);

    [RelayCommand]
    private void ChangeTv() => Main.GoConnect();
}
