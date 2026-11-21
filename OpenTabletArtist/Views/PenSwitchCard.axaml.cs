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
        if (LblDefaultSetting != null) LblDefaultSetting.Text = Localizer.T("tablet.pen.default_setting");
    }
}
