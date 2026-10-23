using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenTabletArtist.Controls;

/// <summary>
/// Version 1: no text box or drop-down is wider than the longest entry it holds. Set on every ComboBox
/// and single-line TextBox by the app style (Themes/Styles.axaml). A drop-down fits its longest choice; a text
/// box fits its text or placeholder, whichever is longer, and grows as you type. A control with its own Width,
/// or the class <c>fullWidth</c>, is left alone.
/// </summary>
public static class FitWidth
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(FitWidth));
    public static bool GetEnabled(Control c) => c.GetValue(EnabledProperty);
    public static void SetEnabled(Control c, bool v) => c.SetValue(EnabledProperty, v);

    /// <summary>Smallest width for any fitted box, so a one-digit value is still easy to click.</summary>
    public const double MinFit = 48;

    static FitWidth()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.NewValue is not true) return;
            c.AttachedToVisualTree += (_, _) => Apply(c);
            c.PropertyChanged += (_, pe) =>
            {
                if (pe.Property == ItemsControl.ItemsSourceProperty || pe.Property == TextBox.TextProperty
                    || pe.Property == TextBox.PlaceholderTextProperty || pe.Property == TemplatedControl.FontSizeProperty)
                    Apply(c);
            };
            if (c is ComboBox cb) cb.Items.CollectionChanged += (_, _) => Apply(c);
            Apply(c);
        });
    }

    private static bool Skip(Control c) =>
        c.Classes.Contains("fullWidth") || (c.IsSet(Layoutable.WidthProperty) && !_owned.TryGetValue(c, out _))
        || c is TextBox { AcceptsReturn: true };

    // Widths this behaviour set (so a later Apply may change them again).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> _owned = new();

    public static void Apply(Control c)
    {
        if (Skip(c)) return;
        double w = c switch
        {
            ComboBox cb => Measure(cb, Texts(cb)) + cb.Padding.Left + cb.Padding.Right + 40,
            TextBox tb => Measure(tb, new[] { tb.Text ?? "", tb.PlaceholderText ?? "" }) + tb.Padding.Left + tb.Padding.Right + 12,
            _ => double.NaN,
        };
        if (double.IsNaN(w)) return;
        _owned.AddOrUpdate(c, true);
        c.Width = Math.Ceiling(Math.Max(MinFit, w));
        if (c.HorizontalAlignment == HorizontalAlignment.Stretch) c.HorizontalAlignment = HorizontalAlignment.Left;
    }

    /// <summary>The text each choice shows: a string, a ComboBoxItem's content, or a Name/Label/Title-style property.</summary>
    public static string[] Texts(ComboBox cb)
    {
        IEnumerable items = (IEnumerable?)cb.ItemsSource ?? cb.Items;
        return items.Cast<object?>().Select(Display).ToArray();
    }

    public static string Display(object? item) => item switch
    {
        null => "",
        string s => s,
        ComboBoxItem { Content: var content } => Display(content),
        ContentControl { Content: string s } => s,
        TextBlock t => t.Text ?? "",
        _ => item.GetType().GetProperties()
                 .FirstOrDefault(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0
                                      && p.Name is "Name" or "Label" or "Title" or "DisplayName" or "Text" or "Header")
                 ?.GetValue(item) as string ?? item.ToString() ?? "",
    };

    private static double Measure(TemplatedControl c, System.Collections.Generic.IEnumerable<string> texts)
    {
        var face = new Typeface(c.FontFamily, c.FontStyle, c.FontWeight);
        double w = 0;
        foreach (var t in texts)
        {
            if (string.IsNullOrEmpty(t)) continue;
            try
            {
                var ft = new FormattedText(t, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, c.FontSize, null);
                w = Math.Max(w, ft.WidthIncludingTrailingWhitespace);
            }
            catch (Exception) { w = Math.Max(w, t.Length * c.FontSize * 0.6); }
        }
        return w;
    }
}
