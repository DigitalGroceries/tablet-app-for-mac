using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Views;

public partial class StartupView : UserControl
{
    public StartupView() => InitializeComponent();

    // Build P: re-check the real login item every time the page is shown (it can be removed in System Settings).
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as ViewModels.StartupViewModel)?.RefreshLoginItem();
    }
}
