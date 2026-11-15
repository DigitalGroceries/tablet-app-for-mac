using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>The shared "no tablet yet" empty-state hero for the Tablet + Pen pages (#580). Only the
/// <see cref="Description"/> line differs between them; without one it says how to set a tablet up before it is
/// plugged in (TASKS #76).</summary>
public partial class NoTabletHero : UserControl
{
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<NoTabletHero, string?>(nameof(Description));

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public NoTabletHero()
    {
        InitializeComponent();
        ApplyLanguage();
        Localizer.LanguageChanged += ApplyLanguage;
        DetachedFromLogicalTree += (_, _) => Localizer.LanguageChanged -= ApplyLanguage;
    }

    private void ApplyLanguage()
    {
        Localizer.EnsureLoaded();
        HeroTitle.Text = Localizer.Te("notablet.title", "No tablet yet");
        ChooseButton.Content = Localizer.Te("notablet.choose", "Choose your tablet");
        if (string.IsNullOrEmpty(Description) || _ownDescription)
        {
            _ownDescription = true;
            Description = Localizer.Te("notablet.body",
                "Plug in your tablet and it shows up here. Don't have it with you? Choose your model and set everything up now. "
                + "Your settings are used as soon as the tablet is connected.");
        }
    }

    /// <summary>True when the description is this hero's own (translated) default, so a language change updates it.</summary>
    private bool _ownDescription;
}
