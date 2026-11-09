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
        if (SaveAsDefaultButton != null)
        {
            SaveAsDefaultButton.Content = Localizer.T("save.default.button");
            ToolTip.SetTip(SaveAsDefaultButton, Localizer.T("save.default.tip"));
        }
        if (RevertButton != null) RevertButton.Content = Localizer.T("save.revert");
        if (RevertToDefaultButton != null)
        {
            RevertToDefaultButton.Content = Localizer.T("save.default.revert");
            ToolTip.SetTip(RevertToDefaultButton, Localizer.T("save.default.revert_tip"));
        }
        if (RestoreFactoryButton != null)
        {
            RestoreFactoryButton.Content = Localizer.T("save.factory.button");
            ToolTip.SetTip(RestoreFactoryButton, Localizer.T("save.factory.tip"));
        }
    }
}
