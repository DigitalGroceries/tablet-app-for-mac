using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>
/// V30 build 5 (Digital Groceries 24 Nov 2026 ~17:43: "In Tablet / Pen when you click on the radial menu, it opens a popup window to set it up. I want
/// the same for the colour pallet under the radial menu. The users should be able to click it and then go to fine tune it with the exaxt
/// colour palletes they want"): the colour palette, large. Click a colour to pick your own for it (the palette pencil's picker:
/// PaletteColourDialog - wheel, swatches, hex). Default colours puts every built-in colour back. Save returns the menu (JSON) with the
/// new colours; Cancel or closing the window returns null and changes nothing.
/// </summary>
public sealed class PaletteEditorDialog : AppWindow
{
    private readonly TaskCompletionSource<string?> _result = new();
    private readonly RadialMenuDef _menu;
    private readonly ColourPaletteWheel _wheel;
    private readonly int _size = RadialMenuDef.SizeForStep(RadialMenuDef.MaxSizeStep);

    /// <summary>Picks a colour for one swatch: its colour now and its name; returns "#RRGGBB" or null (Cancel). Tests replace it.</summary>
    public Func<string, string, Task<string?>>? PickColour { get; set; }

    public Button DefaultButton { get; }
    public Button SaveButton { get; }
    public Button CancelButton { get; }
    public ColourPaletteWheel Wheel => _wheel;

    public PaletteEditorDialog(string menuJson)
    {
        _menu = RadialMenuJson.Parse(menuJson);
        Title = Localizer.Te("palette.title", "Colour Palette Settings");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        double edge = 2 * RadialMenuGeometry.OuterRadius(_size) + 4;
        _wheel = new ColourPaletteWheel { Width = edge, Height = edge, MenuSize = _size, Menu = _menu, Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Center };
        _wheel.PointerMoved += (_, e) => _wheel.Highlight = SwatchAt(e.GetPosition(_wheel));
        _wheel.PointerExited += (_, _) => _wheel.Highlight = -3;
        _wheel.PointerPressed += OnWheelPressed;
        AutomationProperties.SetName(_wheel, Title);

        DefaultButton = new Button { Content = Localizer.Te("palette_editor.default", "Default colours"), Padding = new Thickness(16, 6) };
        DefaultButton.Click += (_, _) => { _menu.PaletteColours = null; Redraw(); AppLog.Info("Colour palette settings: every colour set back to the default (Save keeps it)."); };
        CancelButton = new Button { Content = Localizer.Te("app.cancel", "Cancel"), Padding = new Thickness(16, 6), IsCancel = true };
        CancelButton.Click += (_, _) => { _result.TrySetResult(null); Close(); };
        SaveButton = new Button { Content = Localizer.Te("save.button", "Save"), Padding = new Thickness(16, 6), IsDefault = true };
        SaveButton.Click += (_, _) => { _result.TrySetResult(RadialMenuJson.Serialize(_menu)); Close(); };

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        bottom.Children.Add(DefaultButton);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, [Grid.ColumnProperty] = 2, Children = { CancelButton, SaveButton } };
        bottom.Children.Add(right);

        Content = new Border
        {
            Padding = new Thickness(24, 20, 24, 18),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    // no heading of its own: the window's title bar already says "Colour Palette Settings" (it said it twice)
                    new TextBlock { Text = Localizer.Te("palette_editor.help", "Click a colour to replace it with one of your own."), FontSize = 14, TextWrapping = TextWrapping.Wrap },
                    // V31 (task list L7.23): embossed like the picture on the Pen page - a thin ring just outside the wheel
                    new Panel
                    {
                        Width = edge, Height = edge, HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            new EmbossRing { Name = "WheelRim", Margin = new Thickness(-EmbossRing.Gap), MenuSize = _size,
                                [!EmbossRing.BrushProperty] = new DynamicResourceExtension("EmbossRingBrush"),
                                [!EmbossRing.EdgeBrushProperty] = new DynamicResourceExtension("EmbossRingEdgeBrush") },
                            _wheel,
                        },
                    },
                    bottom,
                },
            },
        };
    }

    private int SwatchAt(Point p) => ColourPalette.SwatchAt(p.X - _wheel.Bounds.Width / 2, p.Y - _wheel.Bounds.Height / 2, _size);

    private async void OnWheelPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (!e.GetCurrentPoint(_wheel).Properties.IsLeftButtonPressed) return;
            int swatch = SwatchAt(e.GetPosition(_wheel));
            if (swatch < 0 || swatch >= ColourPalette.Count) return;   // the centre and outside do nothing here
            await ChangeAsync(swatch);
        }
        catch (Exception ex) { AppLog.Warn("Colour palette settings: changing a colour failed.", ex); }
    }

    /// <summary>Ask for a colour for <paramref name="swatch"/> and keep it in the window's copy of the menu (Save keeps it).</summary>
    internal async Task ChangeAsync(int swatch)
    {
        var name = ColourPalette.NameOf(swatch);
        var pick = PickColour ?? ((hex, n) => PaletteColourDialog.ShowAsync(this, hex, n));
        var hex = await pick(ColourPalette.HexFor(_menu, swatch), name);
        if (hex == null || !ColourPalette.IsHex(hex)) return;
        var colours = _menu.PaletteColours ?? new Dictionary<string, string>();
        colours[swatch.ToString(CultureInfo.InvariantCulture)] = hex.ToUpperInvariant();
        _menu.PaletteColours = colours;
        Redraw();
        AppLog.Info($"Colour palette settings: {name} set to {hex.ToUpperInvariant()} (Save keeps it).");
    }

    private void Redraw()
    {
        // The wheel redraws when its Menu changes; the same object with new colours needs a nudge.
        _wheel.Menu = null;
        _wheel.Menu = _menu;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _result.TrySetResult(null);
    }

    public Task<string?> Result => _result.Task;

    public static async Task<string?> ShowAsync(Window owner, string menuJson)
    {
        var dialog = new PaletteEditorDialog(menuJson);
        await OpenTabletArtist.Helpers.InWindowSheet.ShowDialogAsync(dialog, owner);
        return await dialog._result.Task;
    }
}
