using Avalonia.Controls;
using Avalonia.LogicalTree;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

public partial class PluginsView : UserControl
{
    public PluginsView()
    {
        InitializeComponent();
        ApplyLanguage();
        Localizer.LanguageChanged += ApplyLanguage;
        DetachedFromLogicalTree += (_, _) => Localizer.LanguageChanged -= ApplyLanguage;
    }

    void ApplyLanguage()
    {
        Localizer.EnsureLoaded();
        if (LblInstalledPlugins != null) LblInstalledPlugins.Text = Localizer.T("plugins.installed");
        if (MenuRescan != null) MenuRescan.Header = Localizer.T("plugins.rescan");
        if (MenuOpenFolder != null) MenuOpenFolder.Header = Localizer.T("plugins.open_folder");
    }
}
