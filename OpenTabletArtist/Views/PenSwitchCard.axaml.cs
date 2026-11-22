using Avalonia.Controls;
using Avalonia.LogicalTree;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>One switch callout in the pen diagram (#pen-switch-diagram). DataContext is a
/// <see cref="ViewModels.PenSwitchRowViewModel"/> bound to a slot by the diagram.</summary>
public partial class PenSwitchCard : UserControl
{
    /// <summary>V29 #180: the heading in capitals (the Pen page's BUTTON 2 / BUTTON 1).</summary>
    public static readonly Avalonia.StyledProperty<bool> ShoutProperty =
        Avalonia.AvaloniaProperty.Register<PenSwitchCard, bool>(nameof(Shout));
    public bool Shout { get => GetValue(ShoutProperty); set => SetValue(ShoutProperty, value); }

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
        if (LblDefaultSetting != null)
        {
            LblDefaultSetting.Text = Localizer.T("tablet.pen.default_setting");
            ToolTip.SetTip(LblDefaultSetting, Localizer.Te("tablet.pen.default_setting_tip", "This input uses its default action. This is not a hardware test."));
        }
        // V29 (Grok's check of the build 18 renders, 22 Nov): the Fix button and the card's tooltips follow the language too
        if (BtnFix != null)
        {
            BtnFix.Content = Localizer.Te("tablet.pen.fix", "Fix");
            ToolTip.SetTip(BtnFix, Localizer.Te("tablet.pen.fix_tip", "Reset this input to its default action"));
        }
        if (BtnChange != null) ToolTip.SetTip(BtnChange, Localizer.Te("tablet.pen.change_tip", "Choose what this button or the eraser does"));
        if (BtnDefault != null) ToolTip.SetTip(BtnDefault, Localizer.Te("tablet.pen.default_tip", "Put this button back to its normal behaviour"));
    }
}
