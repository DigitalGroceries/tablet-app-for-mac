using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// Wacom-style Touch Ring modes. The ring button (bound to <see cref="RingModeNextBinding"/>) steps
/// through up to four modes; each ring direction (bound to <see cref="RingModeTurnBinding"/>) sends the
/// keys set for the current mode — e.g. mode 1 = brush size ([ / ]), mode 2 = zoom (⌘- / ⌘=),
/// mode 3 = layer up/down (⌥] / ⌥[). Both bindings live in this assembly so they share one mode.
///
/// <para>The current mode is written to <see cref="StateFilePath"/> ("mode count" on the first line,
/// 1-based, and the mode's name on the second) so the app can show a Wacom-like on-screen display when
/// the mode changes.</para>
/// </summary>
public static class RingModeState
{
    public const int MaxModes = 4;

    private static readonly object Gate = new();
    private static int _mode;       // 0-based
    private static int _count = MaxModes;
    private static string[] _names = [];

    /// <summary>Raised after the mode changes: (1-based mode, mode count).</summary>
    public static event Action<int, int>? Changed;

    /// <summary>The current mode, 0-based, always within the configured count.</summary>
    public static int Current
    {
        get { lock (Gate) return _mode; }
    }

    public static int Count
    {
        get { lock (Gate) return _count; }
    }

    /// <summary>Per-user temp folder; the daemon and the app run as the same user, so both see it.</summary>
    public static string StateFilePath { get; set; } =
        Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "ring-mode");

    public static void SetCount(int count)
    {
        int clamped = Math.Clamp(count, 1, MaxModes);
        lock (Gate)
        {
            _count = clamped;
            if (_mode >= _count) _mode = 0;
        }
    }

    /// <summary>Mode names for the on-screen display, "|"-separated in mode order.</summary>
    public static void SetNames(string? names)
    {
        var list = (names ?? "").Split('|', StringSplitOptions.TrimEntries);
        lock (Gate) _names = list;
    }

    public static string NameOf(int mode)
    {
        lock (Gate)
            return mode >= 0 && mode < _names.Length && _names[mode].Length > 0 ? _names[mode] : $"Mode {mode + 1}";
    }

    /// <summary>Advance to the next mode (wrapping), publish it, and return it 0-based.</summary>
    public static int Next()
    {
        int mode, count;
        lock (Gate)
        {
            _mode = (_mode + 1) % _count;
            mode = _mode;
            count = _count;
        }
        Publish(mode, count, NameOf(mode));
        return mode;
    }

    /// <summary>Back to mode 1 (tests, and a fresh settings apply).</summary>
    public static void Reset()
    {
        lock (Gate) _mode = 0;
    }

    private static void Publish(int mode, int count, string name)
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // Write then rename, so a reader never sees half a file.
            var tmp = StateFilePath + ".tmp";
            File.WriteAllText(tmp, $"{mode + 1} {count}\n{name.Replace('\n', ' ')}\n");
            File.Move(tmp, StateFilePath, overwrite: true);
        }
        catch (Exception e)
        {
            Log.Write(nameof(RingModeState), $"Could not write the ring mode file: {e.Message}", LogLevel.Debug);
        }
        Changed?.Invoke(mode + 1, count);
    }
}

/// <summary>Ring button: go to the next Touch Ring mode.</summary>
[PluginName(PLUGIN_NAME)]
public class RingModeNextBinding : IStateBinding
{
    public const string PLUGIN_NAME = "OpenTabletArtist - Ring Mode: Next Mode";

    private int _modeCount = RingModeState.MaxModes;

    [Property("Number of modes"), DefaultPropertyValue(4),
     ToolTip("How many Touch Ring modes the ring button steps through (1-4).")]
    public int ModeCount
    {
        get => _modeCount;
        set
        {
            _modeCount = Math.Clamp(value, 1, RingModeState.MaxModes);
            RingModeState.SetCount(_modeCount);
        }
    }

    [Property("Mode names"), PropertyUnvalidatedStringPlaceholder("e.g. Brush size|Zoom|Layers"),
     ToolTip("Names shown on screen when the mode changes, separated by |.")]
    public string? ModeNames
    {
        get => _modeNames;
        set
        {
            _modeNames = value;
            RingModeState.SetNames(value);
        }
    }

    private string? _modeNames;

    public void Press(TabletReference tablet, IDeviceReport report) => RingModeState.Next();

    public void Release(TabletReference tablet, IDeviceReport report) { }

    public override string ToString() => $"{PLUGIN_NAME} ({ModeCount})";
}

/// <summary>
/// One ring direction: sends the keys set for the current Touch Ring mode. Keys use the same format as
/// OpenTabletDriver's Multi-Key Binding, e.g. "RightBracket" or "Application+Equal" (Application = ⌘ on
/// macOS). An empty mode does nothing in this direction.
/// </summary>
[PluginName(PLUGIN_NAME)]
public class RingModeTurnBinding : IStateBinding
{
    public const string PLUGIN_NAME = "OpenTabletArtist - Ring Mode: Turn";
    private const char KeysSplitter = '+';

    private readonly string?[] _modeKeys = new string?[RingModeState.MaxModes];
    [Resolved]
    public IVirtualKeyboard? Keyboard { set; get; }

    [Property("Mode 1 keys"), PropertyUnvalidatedStringPlaceholder("e.g. RightBracket")]
    public string? Mode1Keys { get => _modeKeys[0]; set => _modeKeys[0] = value; }

    [Property("Mode 2 keys"), PropertyUnvalidatedStringPlaceholder("e.g. Application+Equal")]
    public string? Mode2Keys { get => _modeKeys[1]; set => _modeKeys[1] = value; }

    [Property("Mode 3 keys"), PropertyUnvalidatedStringPlaceholder("e.g. Alt+RightBracket")]
    public string? Mode3Keys { get => _modeKeys[2]; set => _modeKeys[2] = value; }

    [Property("Mode 4 keys"), PropertyUnvalidatedStringPlaceholder("e.g. Shift+RightBracket")]
    public string? Mode4Keys { get => _modeKeys[3]; set => _modeKeys[3] = value; }

    // Build H: per-mode step size. OpenTabletDriver calls Press once per wheel activation threshold (the
    // wheel's own setting, written here as RingStep); each mode sends its keys only once per StepN degrees of
    // ring travel, so e.g. zoom can be gentler than brush size. 0 (or a step no bigger than RingStep) = a key
    // on every activation, which is how builds E–G behaved.
    private readonly RingStepCounter _steps = new(RingModeState.MaxModes);

    [Property("Ring step"), DefaultPropertyValue(0f), Unit("°"),
     ToolTip("Degrees of ring travel per activation — the wheel's activation threshold. Set by OpenTabletArtist.")]
    public float RingStep { get => _steps.RingStep; set => _steps.RingStep = value; }

    [Property("Mode 1 step"), DefaultPropertyValue(0f), Unit("°"), ToolTip("Degrees of ring travel per key press in mode 1 (0 = every step).")]
    public float Mode1Step { get => _steps[0]; set => _steps[0] = value; }

    [Property("Mode 2 step"), DefaultPropertyValue(0f), Unit("°"), ToolTip("Degrees of ring travel per key press in mode 2 (0 = every step).")]
    public float Mode2Step { get => _steps[1]; set => _steps[1] = value; }

    [Property("Mode 3 step"), DefaultPropertyValue(0f), Unit("°"), ToolTip("Degrees of ring travel per key press in mode 3 (0 = every step).")]
    public float Mode3Step { get => _steps[2]; set => _steps[2] = value; }

    [Property("Mode 4 step"), DefaultPropertyValue(0f), Unit("°"), ToolTip("Degrees of ring travel per key press in mode 4 (0 = every step).")]
    public float Mode4Step { get => _steps[3]; set => _steps[3] = value; }

    // Build I: per-mode Option+scroll (Photoshop's smooth zoom) instead of keys: pixels per step, signed per
    // direction by the app. 0 = this mode sends keys.
    private readonly int[] _modeScroll = new int[RingModeState.MaxModes];

    [Property("Mode 1 Scroll"), DefaultPropertyValue(0), Unit("px")]
    public int Mode1Scroll { get => _modeScroll[0]; set => _modeScroll[0] = value; }

    [Property("Mode 2 Scroll"), DefaultPropertyValue(0), Unit("px")]
    public int Mode2Scroll { get => _modeScroll[1]; set => _modeScroll[1] = value; }

    [Property("Mode 3 Scroll"), DefaultPropertyValue(0), Unit("px")]
    public int Mode3Scroll { get => _modeScroll[2]; set => _modeScroll[2] = value; }

    [Property("Mode 4 Scroll"), DefaultPropertyValue(0), Unit("px")]
    public int Mode4Scroll { get => _modeScroll[3]; set => _modeScroll[3] = value; }

    // Build J (EXPERIMENTAL): per-mode smooth rotate (hold R + drag in Photoshop): canvas degrees per 100°
    // of ring, signed per direction by the app. 0 = off.
    private readonly float[] _modeRotate = new float[RingModeState.MaxModes];

    [Property("Mode 1 Rotate"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode1Rotate { get => _modeRotate[0]; set => _modeRotate[0] = value; }

    [Property("Mode 2 Rotate"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode2Rotate { get => _modeRotate[1]; set => _modeRotate[1] = value; }

    [Property("Mode 3 Rotate"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode3Rotate { get => _modeRotate[2]; set => _modeRotate[2] = value; }

    [Property("Mode 4 Rotate"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode4Rotate { get => _modeRotate[3]; set => _modeRotate[3] = value; }

    // Build K: per-mode smooth brush size / hardness (Control+Option+drag in Photoshop): drag points per 10° of ring (10 = 1 pt per ring degree)
    // of ring, signed per direction by the app. 0 = off.
    private readonly float[] _modeSize = new float[RingModeState.MaxModes];
    private readonly float[] _modeHardness = new float[RingModeState.MaxModes];

    [Property("Mode 1 Size"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode1Size { get => _modeSize[0]; set => _modeSize[0] = value; }
    [Property("Mode 2 Size"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode2Size { get => _modeSize[1]; set => _modeSize[1] = value; }
    [Property("Mode 3 Size"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode3Size { get => _modeSize[2]; set => _modeSize[2] = value; }
    [Property("Mode 4 Size"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode4Size { get => _modeSize[3]; set => _modeSize[3] = value; }

    [Property("Mode 1 Hardness"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode1Hardness { get => _modeHardness[0]; set => _modeHardness[0] = value; }
    [Property("Mode 2 Hardness"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode2Hardness { get => _modeHardness[1]; set => _modeHardness[1] = value; }
    [Property("Mode 3 Hardness"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode3Hardness { get => _modeHardness[2]; set => _modeHardness[2] = value; }
    [Property("Mode 4 Hardness"), DefaultPropertyValue(0f), Unit("%")]
    public float Mode4Hardness { get => _modeHardness[3]; set => _modeHardness[3] = value; }

    /// <summary>Count one activation in <paramref name="mode"/>; true = send that mode's keys now.</summary>
    public bool CountStep(int mode) => _steps.Count(mode);

    /// <summary>The keys this binding would send in <paramref name="mode"/> (0-based); empty = nothing.</summary>
    public string[] KeysForMode(int mode)
    {
        if (mode < 0 || mode >= RingModeState.MaxModes) return [];
        return ParseKeys(_modeKeys[mode], Keyboard);
    }

    // Build R: test seams. Gesture = smooth Photoshop gesture (false = refused); FrontApp says which app's window is
    // under the cursor (Version 16 BETA 2: Photoshop or Affinity); Scroll posts Option+scroll (zoom in both),
    // CommandScroll posts ⌘+scroll (Affinity's canvas rotation). When a gesture is refused or another app is in
    // front, the mode's keys are sent.
    public static Func<GestureKind, double, bool> Gesture { get; set; } = DragRotate.Turn;
    public static Func<FrontApp> FrontApp { get; set; } = DragRotate.FrontAppUnderCursor;
    public static Action<int> Scroll { get; set; } = MacScroll.PostWithOption;
    public static Action<int> CommandScroll { get; set; } = MacScroll.PostWithCommand;

    /// <summary>Version 16 BETA 2: written by the app (2 = physical clockwise is OTD's clockwise binding, zoom
    /// value is a direction only). Not used here; the app reads it back to tell old bindings apart.</summary>
    [Property("Direction version"), DefaultPropertyValue(0)]
    public int DirectionVersion { get; set; }

    private readonly KeyPulse _pulse = new();
    /// <summary>Build R: how long a ring key is held before it is let go (OTD releases instantly otherwise).</summary>
    public int KeyHoldMs { get => _pulse.HoldMs; set => _pulse.HoldMs = value; }
    internal KeyPulse Pulse => _pulse;

    public void Press(TabletReference tablet, IDeviceReport report)
    {
        int mode = RingModeState.Current;
        bool valid = mode >= 0 && mode < RingModeState.MaxModes;
        double ring = RingStep > 0 ? RingStep : 5;
        if (valid && _modeRotate[mode] != 0)
        {
            // Smooth rotate in Photoshop: every ring position turns the canvas by (ring degrees × sensitivity %).
            if (Gesture(GestureKind.Rotate, ring * _modeRotate[mode] / 100.0)) return;
            // Version 16 BETA 2: Affinity rotates its view with ⌘ + scroll wheel (when "Enable canvas rotation with
            // ⌘+scroll wheel" is on in its Settings › Tools): one wheel line per mode step.
            if (FrontApp() == Dynamics.FrontApp.Affinity)
            {
                if (CountStep(mode)) CommandScroll(Math.Sign(_modeRotate[mode]) * MacScroll.PixelsPerLine);
                return;
            }
        }
        else if (valid && (_modeSize[mode] != 0 || _modeHardness[mode] != 0))
        {
            // Build K: smooth brush size / hardness (Photoshop only).
            bool acted = _modeSize[mode] != 0
                ? Gesture(GestureKind.BrushSize, ring * _modeSize[mode] / 10.0)
                : Gesture(GestureKind.BrushHardness, ring * _modeHardness[mode] / 10.0);
            if (acted) return;
        }
        else if (valid && _modeScroll[mode] != 0 && FrontApp() is Dynamics.FrontApp.Photoshop or Dynamics.FrontApp.Affinity)
        {
            // Smooth Option+scroll zoom in Photoshop and Affinity (Version 16 BETA 2: one wheel line per mode step,
            // so the app's Speed sets it; before, every ring step sent lines); other apps get the mode's keys.
            if (CountStep(mode)) Scroll(Math.Sign(_modeScroll[mode]) * MacScroll.PixelsPerLine);
            return;
        }
        var keys = KeysForMode(mode);
        if (keys.Length == 0 || Keyboard == null) return;
        if (!CountStep(mode)) return;
        _pulse.Press(Keyboard, keys);   // key down now, up ~18 ms later on a timer
    }

    /// <summary>OTD calls this straight after Press; the timed pulse does the release instead.</summary>
    public void Release(TabletReference tablet, IDeviceReport report) { }

    public static string[] ParseKeys(string? str, IVirtualKeyboard? keyboard)
    {
        if (string.IsNullOrWhiteSpace(str) || keyboard == null) return [];
        var keys = str.Split(KeysSplitter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var supported = keyboard.SupportedKeys as ICollection<string> ?? keyboard.SupportedKeys.ToHashSet();
        return keys.Length > 0 && keys.All(supported.Contains) ? keys : [];
    }

    public override string ToString() =>
        $"{PLUGIN_NAME}: {string.Join(" | ", _modeKeys.Select(k => string.IsNullOrWhiteSpace(k) ? "-" : k))}";
}

/// <summary>Build R: presses a key combo and lets go of it on a timer (default 18 ms) without blocking the
/// caller. A new Press first releases any combo still held. Release order is reversed so modifiers
/// (e.g. Cmd for zoom) stay down until the main key is up. Thread-safe.</summary>
public sealed class KeyPulse
{
    private readonly object _lock = new();
    private System.Threading.Timer? _timer;
    private string[]? _held;
    private IVirtualKeyboard? _kb;
    private long _gen;
    public int HoldMs { get; set; } = 18;
    public bool Holding { get { lock (_lock) return _held != null; } }

    public void Press(IVirtualKeyboard kb, string[] keys)
    {
        lock (_lock)
        {
            ReleaseLocked();
            foreach (var k in keys) kb.Press(k);
            _held = keys; _kb = kb;
            long gen = ++_gen;
            int ms = Math.Max(0, HoldMs);
            if (ms == 0) { ReleaseLocked(); return; }
            _timer = new System.Threading.Timer(_ => { lock (_lock) { if (gen == _gen) ReleaseLocked(); } }, null, ms, System.Threading.Timeout.Infinite);
        }
    }

    /// <summary>Let go now of anything held.</summary>
    public void ReleaseNow() { lock (_lock) ReleaseLocked(); }

    private void ReleaseLocked()
    {
        _timer?.Dispose(); _timer = null;
        if (_held == null || _kb == null) return;
        var held = _held; _held = null;
        for (int i = held.Length - 1; i >= 0; i--)
            try { _kb.Release(held[i]); } catch (Exception) { /* keep releasing the rest */ }
    }
}
