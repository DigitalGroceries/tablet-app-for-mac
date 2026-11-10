using System;
using System.Threading;

namespace OpenTabletArtist.Services;

/// <summary>
/// The big on-screen "it worked" message for buttons that do something (Save → "Settings saved"). It is the
/// ring-mode popup (<see cref="RingModeHud"/>): same window, size, position, opacity and fade, click-through,
/// never takes focus. Call it ONLY after the action succeeded; on failure leave the existing error UI alone.
/// Not for navigation, dialogs/pickers, switches whose state is already visible, or Cancel/Close.
/// Safe to call from any thread (the display hops to the UI thread itself) and a no-op before the popup
/// exists (tests, start-up) apart from the <see cref="Recorder"/> hook.
/// </summary>
public static class ActionConfirmation
{
    /// <summary>The header line above the message: the app's name, like "TOUCH RING" above a ring mode.</summary>
    internal static string HeaderText() => Localizer.Te("app.name", "Tablet App for Mac").ToUpperInvariant();

    private static volatile RingModeHud? _hud;

    /// <summary>Wired once by App at start-up.</summary>
    public static void Attach(RingModeHud? hud) => _hud = hud;

    // Tests only: sees every confirmation key raised by the current async flow (AsyncLocal, so parallel
    // tests don't see each other's).
    private static readonly AsyncLocal<Action<string>?> _recorder = new();
    internal static Action<string>? Recorder { get => _recorder.Value; set => _recorder.Value = value; }

    /// <summary>Show the localised text for <paramref name="key"/> (<c>confirm.*</c>); <paramref name="english"/>
    /// is the fallback when the string tables aren't loaded.</summary>
    public static void Show(string key, string english)
    {
        try
        {
            Recorder?.Invoke(key);
            var hud = _hud;
            if (hud == null) return;
            hud.ShowConfirmation(HeaderText(), Localizer.Te(key, english));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Confirmation popup failed", ex);
        }
    }
}
