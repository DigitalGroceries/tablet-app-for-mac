using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace OpenTabletArtist.Services;

/// <summary>
/// R12: nothing on screen brighter than #E4E4E4, in every theme. The app's own palettes already keep
/// to that, but Fluent's dark defaults (text, glyphs, switch knobs, scroll arrows, check marks) are pure
/// white, and any key a skin does not re-point shows through. After each theme/skin refresh this resolves
/// every Fluent and app resource for the current theme and shadows any brighter colour with a clamped one
/// (each channel capped at #E4, alpha kept). Its own shadows are removed first, so a theme switch starts clean
/// and a light theme's dark text is never touched.
/// </summary>
public static class NoNearWhite
{
    public const byte Max = 0xE4;
    private static readonly Dictionary<object, object> Applied = new();

    public static bool IsTooBright(Color c) => c.A > 0x40 && c.R > Max && c.G > Max && c.B > Max;

    public static Color Clamp(Color c) =>
        Color.FromArgb(c.A, System.Math.Min(c.R, Max), System.Math.Min(c.G, Max), System.Math.Min(c.B, Max));

    public static void Apply(Application? app = null)
    {
        app ??= Application.Current;
        if (app is null) return;
        foreach (var (k, v) in Applied)
            if (app.Resources.TryGetValue(k, out var cur) && ReferenceEquals(cur, v)) app.Resources.Remove(k);
        Applied.Clear();

        var keys = new HashSet<object>();
        Collect(app.Resources, keys);
        foreach (var s in app.Styles) Collect(s, keys);
        var variant = app.ActualThemeVariant;
        foreach (var k in keys)
        {
            if (!app.TryGetResource(k, variant, out var v)) continue;
            object? clamped = v switch
            {
                Color c when IsTooBright(c) => Clamp(c),
                ISolidColorBrush b when IsTooBright(b.Color) => new SolidColorBrush(Clamp(b.Color), b.Opacity),
                _ => null,
            };
            if (clamped is null) continue;
            app.Resources[k] = clamped;
            Applied[k] = clamped;
        }
    }

    private static void Collect(object? node, HashSet<object> keys)
    {
        switch (node)
        {
            case ResourceDictionary rd:
                foreach (var k in rd.Keys) keys.Add(k);
                foreach (var m in rd.MergedDictionaries) Collect(m, keys);
                foreach (var t in rd.ThemeDictionaries.Values) Collect(t, keys);
                break;
            case IResourceDictionary d:
                foreach (var k in d.Keys) keys.Add(k);
                foreach (var m in d.MergedDictionaries) Collect(m, keys);
                foreach (var t in d.ThemeDictionaries.Values) Collect(t, keys);
                break;
            case IThemeVariantProvider p when p is IResourceProvider rp:
                Collect(rp as IResourceDictionary, keys);
                break;
            case Styles ss:
                Collect(ss.Resources, keys);
                foreach (var c in ss) Collect(c, keys);
                break;
            case StyleBase sb:
                Collect(sb.Resources, keys);
                foreach (var c in sb.Children) Collect(c, keys);
                break;
            case IResourceHost h:
                break;
        }
        if (node is Avalonia.Styling.IStyle st && node is not Styles && node is not StyleBase && node is IResourceNode)
        {
            var res = node.GetType().GetProperty("Resources")?.GetValue(node);
            if (res is IResourceDictionary r2) Collect(r2, keys);
        }
    }
}
