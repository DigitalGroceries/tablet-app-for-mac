using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class DiagnosticFileView : UserControl
{
    public DiagnosticFileView() => InitializeComponent();

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DiagnosticsViewModel vm) vm.PickImages = PickAsync;
    }

    private async Task<IReadOnlyList<string>> PickAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return new List<string>();
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
    }
}
