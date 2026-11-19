using System;
using System.IO;
using System.Threading;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Platform.Keyboard;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// V29 #141 (Zak 19 Nov 2026: "add the option where the user can add a floating keyboard to the radial pen menu so they can type
/// using the pen"). The app draws the keyboard; this presses its keys in the app in front, because the app itself can't. While the
/// app keeps <see cref="FloatingKeyboardFiles.OpenFlag"/>, the key file is looked at every <see cref="PollMs"/> ms and every new key
/// in it is pressed once, in order. Polling starts when the radial menu's Keyboard slice is used, or when the filter sees the
/// keyboard is already on screen (after the driver restarted), and stops soon after the keyboard is closed.
/// </summary>
public static class FloatingKeyboardKeys
{
    public static string OpenFlagPath { get; set; } = FloatingKeyboardFiles.OpenFlag;
    public static string CommandFilePath { get; set; } = FloatingKeyboardFiles.CommandFile;
    public static int PollMs { get; set; } = 30;
    /// <summary>After a start, how long to wait for the app to put the keyboard on screen before giving up.</summary>
    public static int StartGraceMs { get; set; } = 3000;
    /// <summary>Presses keys; tests replace it. Default: the driver's virtual keyboard.</summary>
    public static Action<string>? KeyRunner { get; set; }

    private static readonly object Gate = new();
    private static readonly KeyPulse Pulse = new() { HoldMs = 30 };
    private static IVirtualKeyboard? _keyboard;
    private static Timer? _timer;
    private static long _seen;
    private static long _startedAt;
    private static long _lastLook;

    public static bool Polling { get { lock (Gate) return _timer != null; } }

    /// <summary>The keyboard device keys are pressed on (any binding or filter that has one hands it over).</summary>
    public static void Remember(IVirtualKeyboard? keyboard)
    {
        if (keyboard != null) lock (Gate) _keyboard = keyboard;
    }

    /// <summary>Start watching the key file. Keys already in it are old and are not pressed.</summary>
    public static void Start()
    {
        lock (Gate)
        {
            _startedAt = Environment.TickCount64;
            if (_timer != null) return;
            _seen = LatestSeq(ReadFile());
            _timer = new Timer(_ => Poll(), null, PollMs, PollMs);
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Called by the filter on every report: at most once a second, start watching when the keyboard is on screen.</summary>
    public static void LookNow()
    {
        var now = Environment.TickCount64;
        lock (Gate)
        {
            if (_timer != null || now - _lastLook < 1000) return;
            _lastLook = now;
        }
        if (File.Exists(OpenFlagPath)) Start();
    }

    /// <summary>One look at the key file (the timer does this; tests call it).</summary>
    public static void Poll()
    {
        bool open = File.Exists(OpenFlagPath);
        lock (Gate)
        {
            if (!open && Environment.TickCount64 - _startedAt > StartGraceMs)
            {
                _timer?.Dispose();
                _timer = null;
                return;
            }
        }
        if (!open) return;
        foreach (var cmd in FloatingKeyboardFiles.ParseLines(ReadFile()))
        {
            lock (Gate)
            {
                if (cmd.Seq <= _seen) continue;
                _seen = cmd.Seq;
            }
            Press(cmd.Keys);
        }
    }

    private static void Press(string? keys)
    {
        if (string.IsNullOrWhiteSpace(keys)) return;
        try
        {
            if (KeyRunner != null) { KeyRunner(keys); return; }
            IVirtualKeyboard? kb;
            lock (Gate) kb = _keyboard;
            var parsed = RingModeBindingsKeys.Parse(keys, kb);
            if (kb != null && parsed.Length > 0) Pulse.Press(kb, parsed);
            else Log.Write("Floating keyboard", $"Key not pressed: '{keys}' (no keyboard device or unknown key).", LogLevel.Warning);
        }
        catch (Exception e)
        {
            Log.Write("Floating keyboard", $"Key '{keys}' failed: {e.Message}", LogLevel.Warning);
        }
    }

    private static long LatestSeq(string? text)
    {
        long max = 0;
        foreach (var c in FloatingKeyboardFiles.ParseLines(text)) max = Math.Max(max, c.Seq);
        return max;
    }

    private static string? ReadFile()
    {
        try { return File.Exists(CommandFilePath) ? File.ReadAllText(CommandFilePath) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Tests: forget everything.</summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            _timer?.Dispose();
            _timer = null;
            _seen = 0;
            _lastLook = 0;
            _keyboard = null;
        }
    }
}
