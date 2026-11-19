using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace OpenTabletArtist.Services;

/// <summary>
/// V29 #142 (Zak 19 Nov 19:34: "You decide what may work better"): what a tap on the colour palette does with its colour. In
/// Photoshop the colour becomes the foreground (brush) colour at once, through Photoshop's own scripting (macOS asks the user once
/// to let Tablet App for Mac control Photoshop). In every app the colour's code ("#RRGGBB") is copied, so it can be pasted into
/// any colour box; and the palette stays on screen, so an app's own eyedropper can pick from it (Photoshop's Eyedropper can sample
/// anywhere on screen when dragged out of the picture).
/// </summary>
public static class ColourPick
{
    /// <summary>The bundle id of the app in front (tests replace it).</summary>
    public static Func<string?> FrontmostBundleId { get; set; } = () => OperatingSystem.IsMacOS() ? MacFrontmostApp.FrontmostBundleId() : null;
    /// <summary>Runs an AppleScript (tests replace it). Default: /usr/bin/osascript in the background, failures logged.</summary>
    public static Action<string> RunAppleScript { get; set; } = RunOsaScript;

    public const string PhotoshopBundle = "com.adobe.Photoshop";
    private static readonly Regex HexColour = new("^#[0-9A-Fa-f]{6}$");

    /// <summary>The AppleScript that makes <paramref name="hex"/> Photoshop's foreground colour.</summary>
    public static string PhotoshopScript(string bundleId, string hex) =>
        $"tell application id \"{bundleId}\" to do javascript \"var c = new SolidColor(); c.rgb.hexValue = '{hex.TrimStart('#').ToUpperInvariant()}'; app.foregroundColor = c;\"";

    /// <summary>Use the tapped colour. Returns the short text shown next to the palette (in the app's language).</summary>
    public static string Apply(string? hex, string? label, TopLevel? top)
    {
        if (hex == null || !HexColour.IsMatch(hex)) return Localizer.Te("radial.done.failed_plain", "Did not work");
        hex = hex.ToUpperInvariant();
        try { if (top?.Clipboard is { } clipboard) _ = clipboard.SetTextAsync(hex); }
        catch (Exception e) { AppLog.Warn("Colour palette: couldn't copy the colour", e); }
        var front = FrontmostBundleId();
        if (front != null && front.StartsWith(PhotoshopBundle, StringComparison.OrdinalIgnoreCase))
        {
            RunAppleScript(PhotoshopScript(front, hex));
            AppLog.Info($"Colour palette: {hex} ({label}) sent to Photoshop as the foreground colour, and copied.");
            return Localizer.Te("palette.set_photoshop", "{colour} set in Photoshop").Replace("{colour}", hex, StringComparison.Ordinal);
        }
        AppLog.Info($"Colour palette: {hex} ({label}) copied; the app in front ({front ?? "unknown"}) takes colours by its eyedropper or a paste.");
        return Localizer.Te("palette.copied", "{colour} copied").Replace("{colour}", hex, StringComparison.Ordinal);
    }

    private static void RunOsaScript(string script)
    {
        if (!OperatingSystem.IsMacOS()) return;
        Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(script);
                using var p = Process.Start(psi);
                if (p == null) return;
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit(10_000);
                if (p.ExitCode != 0)
                    AppLog.Warn($"Colour palette: Photoshop didn't take the colour (osascript {p.ExitCode}): {err.Trim()}"
                        + (err.Contains("-1743") ? " - allow Tablet App for Mac to control Photoshop in System Settings › Privacy & Security › Automation." : ""));
            }
            catch (Exception e) { AppLog.Warn("Colour palette: couldn't run the Photoshop script", e); }
        });
    }
}
