using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>
/// V29 #142 (Zak ~20:05: the palette's pencil changes a swatch to your own colour): a small sheet with the app's colour picker (hue
/// wheel, curated swatches and a hex field) for one swatch. Save returns "#RRGGBB"; Cancel returns null.
/// </summary>
public sealed class PaletteColourDialog : Window
{
    private readonly TaskCompletionSource<string?> _result = new();
    private readonly OpenTabletArtist.Controls.ColorPicker _picker;

    private PaletteColourDialog(string hex, string swatchName)
    {
        Title = Localizer.Te("palette.change_title", "Change this colour");
        Width = 380; SizeToContent = SizeToContent.Height; CanResize = false;
        _picker = new OpenTabletArtist.Controls.ColorPicker { Color = Color.Parse(hex) };
        var save = new Button { Content = Localizer.Te("save.button", "Save"), Padding = new Thickness(16, 6) };
        var cancel = new Button { Content = Localizer.Te("app.cancel", "Cancel"), Padding = new Thickness(16, 6) };
        save.Click += (_, _) => { _result.TrySetResult(Hex(_picker.Color)); Close(); };
        cancel.Click += (_, _) => { _result.TrySetResult(null); Close(); };
        Closed += (_, _) => _result.TrySetResult(null);
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 14,
            Children =
            {
                // V30 build 5: no heading of its own; the window's title bar already says "Change this colour" (it said it twice)
                new TextBlock { Text = swatchName, Opacity = 0.8 },
                // V30 build 6 (Zak's build 5 screenshot 24 Nov 21:22: the picker's drop-down opened over Cancel): the colour box and
                // Cancel / Save on one row, so the drop-down opens below them and covers neither.
                PickerRow(_picker, cancel, save),
            },
        };
    }

    private static Grid PickerRow(Control picker, Button cancel, Button save)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        picker.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(picker);
        row.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            [Grid.ColumnProperty] = 2, Children = { cancel, save },
        });
        return row;
    }

    internal static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static async Task<string?> ShowAsync(Window owner, string hex, string swatchName)
    {
        var dialog = new PaletteColourDialog(hex, swatchName);
        await OpenTabletArtist.Helpers.InWindowSheet.ShowDialogAsync(dialog, owner);
        return await dialog._result.Task;
    }
}
