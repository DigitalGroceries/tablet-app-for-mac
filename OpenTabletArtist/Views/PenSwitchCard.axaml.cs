using Avalonia.Controls;
using Avalonia.LogicalTree;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>One switch callout in the pen diagram (#pen-switch-diagram). DataContext is a
/// <see cref="ViewModels.PenSwitchRowViewModel"/> bound to a slot by the diagram.</summary>
public partial class PenSwitchCard : UserControl
{
    public PenSwitchCard()
    {
        InitializeComponent();
        ApplyLanguage();
        Localizer.LanguageChanged += ApplyLanguage;
        DetachedFromLogicalTree += (_, _) => Localizer.LanguageChanged -= ApplyLanguage;
    }

    void ApplyLanguage()
    {
        Localizer.EnsureLoaded();
        if (BtnChange != null) BtnChange.Content = Localizer.T("tablet.pen.change");
        if (BtnDefault != null) BtnDefault.Content = Localizer.T("tablet.pen.default");
        if (LblDefaultSetting != null) LblDefaultSetting.Text = Localizer.T("tablet.pen.default_setting");
    }
}
