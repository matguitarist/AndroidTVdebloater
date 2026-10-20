using AndroidTVDebloater.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AndroidTVDebloater.Views;

public partial class RemoteView : UserControl
{
    public RemoteView()
    {
        InitializeComponent();
        // Keyboard as a remote, except while typing in the text box.
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Focus();
    }

    private async void OnKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not RemoteViewModel vm || e.Source is TextBox) return;
        var code = e.Key switch
        {
            Key.Up => "19", Key.Down => "20", Key.Left => "21", Key.Right => "22",
            Key.Enter or Key.Space => "23", Key.Back or Key.Escape => "4", Key.H => "3", Key.M => "82",
            _ => null,
        };
        if (code is null) return;
        e.Handled = true;
        await vm.KeyAsync(code);
    }
}
