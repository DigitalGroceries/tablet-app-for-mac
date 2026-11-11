using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using SkiaSharp;

namespace OpenTabletArtist.Services;

/// <summary>
/// #7 / #18 (Brice, PTK-640): shows what each Intuos4 ExpressKey does on the small screen beside it, as Wacom's
/// driver does. The words come from the key's binding ("Shift Cmd" / "Z", "Right click"); a key that does nothing
/// stays dark. Per-app and per-pen switches change the words too, because they change the bindings.
/// <para>
/// Like <see cref="Intuos4StatusLed"/>: nothing is sent while nothing changed, so drawing never sends anything.
/// Only keys whose picture changed are sent again. If the tablet refuses, it is logged once and we stop until
/// the tablet is plugged in again or the app restarts. Built from the Linux driver; not tested on real hardware here.
/// </para>
/// </summary>
public sealed class Intuos4OledLabels : IDisposable
{
    public const string SettingKey = "intuos4.oledLabels";

    private readonly IDaemonCapabilities _daemon;
    private readonly IConnectionState _connection;
    private readonly Func<string, Profile?> _profileFor;
    private readonly Func<IReadOnlyList<string>, byte[]> _render;
    private IReadOnlyList<(int Vid, int Pid, string Iface, int Count)>? _targets;
    private readonly Dictionary<string, byte[]?[]> _shown = new();
    private bool _failed;
    private bool _busy;

    public Intuos4OledLabels(IDaemonCapabilities daemon, IConnectionState connection, Func<string, Profile?> profileFor,
        Func<IReadOnlyList<string>, byte[]>? render = null)
    {
        _daemon = daemon;
        _connection = connection;
        _profileFor = profileFor;
        _render = render ?? Render;
        _daemon.TabletsChanged += OnTabletsChanged;
        _connection.PropertyChanged += OnConnectionChanged;
    }

    /// <summary>Whether the key screens show what each key does. Default on.</summary>
    public static bool Enabled
    {
        get => AppSettings.Get(SettingKey) != "false";
        set => AppSettings.Set(SettingKey, value ? "true" : "false");
    }

    private void OnTabletsChanged() => Forget();

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IConnectionState.IsConnected)) Forget();
    }

    /// <summary>A replugged tablet or a new daemon starts with blank screens and a fresh chance.</summary>
    private void Forget()
    {
        _targets = null;
        _shown.Clear();
        _failed = false;
    }

    /// <summary>Called often (about once a second) and after the setting changes: sends the keys whose words changed.</summary>
    public async Task RefreshAsync()
    {
        if (_failed || _busy || !_connection.IsConnected) return;
        _busy = true;
        try
        {
            _targets ??= PickTargets(await _daemon.GetDevicesAsync());
            foreach (var t in _targets)
            {
                var name = Intuos4Oled.NameOf(t.Pid);
                if (name == null) continue;
                var images = Compose(_profileFor(name), Enabled, _render);
                var key = $"{t.Vid}:{t.Pid}:{t.Iface}";
                if (!_shown.TryGetValue(key, out var shown)) _shown[key] = shown = new byte[]?[Intuos4Oled.KeyCount];
                var changed = Enumerable.Range(0, Intuos4Oled.KeyCount)
                    .Where(k => shown[k] == null || !shown[k]!.AsSpan().SequenceEqual(images[k])).ToList();
                if (changed.Count == 0) continue;
                var reports = changed.SelectMany(k => Intuos4Oled.BuildReports(k, images[k])).ToArray();
                if (await _daemon.SendFeatureSequenceAsync(t.Vid, t.Pid, t.Iface, reports))
                {
                    foreach (var k in changed) shown[k] = images[k];
                    AppLog.Info($"Intuos4 key screens: {changed.Count} updated on {name}.");
                }
                else if (_connection.IsConnected)
                {
                    _failed = true;
                    AppLog.Warn("Intuos4 key screens: the tablet didn't take the pictures; not trying again until it is plugged in again or the app restarts.");
                    return;
                }
            }
        }
        catch (Exception ex) when (!_connection.IsConnected)
        {
            AppLog.Info($"Intuos4 key screens: connection dropped mid-send ({ex.Message}); will retry on reconnect.");
        }
        catch (Exception ex)
        {
            _failed = true;
            AppLog.Warn("Intuos4 key screens: sending failed; not trying again until the tablet is plugged in again or the app restarts.", ex);
        }
        finally { _busy = false; }
    }

    /// <summary>The Intuos4 models with screens, each with its lowest feature-report interface.</summary>
    public static IReadOnlyList<(int Vid, int Pid, string Iface, int Count)> PickTargets(JArray devices) =>
        Intuos4StatusLed.PickInterfaces(devices).Where(t => Intuos4Oled.HasOleds(t.Vid, t.Pid)).ToList();

    /// <summary>The eight pictures for a profile: the words of each key, upside down for left-handed use; all dark
    /// when the setting is off or there is no profile.</summary>
    public static byte[][] Compose(Profile? profile, bool enabled, Func<IReadOnlyList<string>, byte[]> render)
    {
        var images = new byte[Intuos4Oled.KeyCount][];
        var labels = enabled && profile != null
            ? Intuos4Oled.KeyLabels(profile.BindingSettings?.AuxButtons)
            : null;
        bool flip = profile?.AbsoluteModeSettings?.Tablet is { } area && Intuos4Oled.IsUpsideDown(area.Rotation);
        for (int k = 0; k < Intuos4Oled.KeyCount; k++)
        {
            var img = labels == null || labels[k].Count == 0 ? Intuos4Oled.Blank() : render(labels[k]);
            images[k] = flip ? Intuos4Oled.Rotate180(img) : img;
        }
        return images;
    }

    // ---- Drawing the words ----

    private const int MaxTextWidth = Intuos4Oled.Width - 2;
    private static readonly float[] Sizes = { 20, 18, 16, 15, 14, 13, 12, 11, 10, 9 };

    /// <summary>Draws the lines white on black, centred, as large as they fit (long names wrap onto more lines),
    /// and returns the plain 1024-byte image.</summary>
    public static byte[] Render(IReadOnlyList<string> lines)
    {
        var clean = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
        if (clean.Count == 0) return Intuos4Oled.Blank();

        var typeface = PickTypeface(string.Concat(clean));
        using var bmp = new SKBitmap(new SKImageInfo(Intuos4Oled.Width, Intuos4Oled.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.Clear(SKColors.Black);
            List<string>? laid = null;
            SKFont? font = null;
            foreach (var size in Sizes)
            {
                font?.Dispose();
                font = new SKFont(typeface, size) { Edging = SKFontEdging.Antialias, Hinting = SKFontHinting.Full };
                laid = Wrap(clean, font, out var allFit);
                if (allFit && laid.Count * LineHeight(font) <= Intuos4Oled.Height) break;
                laid = null;
            }
            // Even the smallest size is too big: keep the lines that fit and shorten long words.
            if (laid == null)
            {
                laid = Wrap(clean, font!, out _).Take(Math.Max(1, (int)(Intuos4Oled.Height / LineHeight(font!)))).ToList();
                laid = laid.Select(l => Shorten(l, font!)).ToList();
            }
            float lh = LineHeight(font!);
            float top = (Intuos4Oled.Height - laid.Count * lh) / 2f;
            for (int i = 0; i < laid.Count; i++)
            {
                float w = font!.MeasureText(laid[i]);
                float x = MathF.Round((Intuos4Oled.Width - w) / 2f);
                float baseline = MathF.Round(top + i * lh - font.Metrics.Ascent);
                canvas.DrawText(laid[i], x, baseline, SKTextAlign.Left, font, paint);
            }
            font?.Dispose();
        }

        var grey = new byte[Intuos4Oled.Width * Intuos4Oled.Height];
        var px = bmp.Bytes;   // RGBA
        for (int i = 0; i < grey.Length; i++) grey[i] = px[i * 4];
        return Intuos4Oled.Pack(grey);
    }

    private static float LineHeight(SKFont font) => MathF.Ceiling(font.Metrics.Descent - font.Metrics.Ascent);

    /// <summary>Each line split at spaces so no line is wider than the screen; <paramref name="allFit"/> is false
    /// when a single word is still too wide.</summary>
    private static List<string> Wrap(IEnumerable<string> lines, SKFont font, out bool allFit)
    {
        allFit = true;
        var result = new List<string>();
        foreach (var line in lines)
        {
            var current = "";
            foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var tryLine = current.Length == 0 ? word : current + " " + word;
                if (font.MeasureText(tryLine) <= MaxTextWidth) { current = tryLine; continue; }
                if (current.Length > 0) result.Add(current);
                current = word;
                if (font.MeasureText(word) > MaxTextWidth) allFit = false;
            }
            if (current.Length > 0) result.Add(current);
        }
        return result;
    }

    private static string Shorten(string line, SKFont font)
    {
        if (font.MeasureText(line) <= MaxTextWidth) return line;
        var s = line;
        while (s.Length > 1 && font.MeasureText(s + "…") > MaxTextWidth) s = s[..^1];
        return s + "…";
    }

    /// <summary>A plain sans font; for letters it lacks (e.g. Japanese key names) the system font that has them.</summary>
    private static SKTypeface PickTypeface(string text)
    {
        var tf = SKTypeface.FromFamilyName(OperatingSystem.IsMacOS() ? "Helvetica Neue" : "DejaVu Sans") ?? SKTypeface.Default;
        using (var probe = new SKFont(tf))
            if (probe.ContainsGlyphs(text)) return tf;
        foreach (var rune in text.EnumerateRunes())
        {
            using var probe = new SKFont(tf);
            if (probe.ContainsGlyphs(rune.ToString())) continue;
            var match = SKFontManager.Default.MatchCharacter(rune.Value);
            if (match != null) return match;
            break;
        }
        return tf;
    }

    public void Dispose()
    {
        _daemon.TabletsChanged -= OnTabletsChanged;
        _connection.PropertyChanged -= OnConnectionChanged;
    }
}
