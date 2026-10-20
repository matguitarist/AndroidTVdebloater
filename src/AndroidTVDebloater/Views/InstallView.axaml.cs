using AndroidTVDebloater.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace AndroidTVDebloater.Views;

public partial class InstallView : UserControl
{
    public InstallView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var path = e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath();
            if (path is not null && DataContext is InstallViewModel vm) await vm.InstallAsync(path);
        });
    }
}
