using Avalonia.Controls;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

public partial class SettingsFooterView : UserControl
{
    public SettingsFooterView()
    {
        InitializeComponent();
        ApplyLanguage();
        Localizer.LanguageChanged += ApplyLanguage;
        DetachedFromLogicalTree += (_, _) => Localizer.LanguageChanged -= ApplyLanguage;
    }

    void ApplyLanguage()
    {
        if (SaveButton != null) SaveButton.Content = Localizer.T("save.button");
        if (RevertButton != null) RevertButton.Content = Localizer.T("save.revert");
    }
}
