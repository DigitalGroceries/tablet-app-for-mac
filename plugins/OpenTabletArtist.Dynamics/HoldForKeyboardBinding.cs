using System;
using System.IO;
using System.Threading;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Keyboard;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletArtist.Dynamics;

/// <summary>
/// V29 #160 (Zak 20 Nov ~09:05-09:24: pen button 1 should open the floating keyboard; "what about long press instead of click?";
/// "let them have the choice to be able to drag while holding the button or open a keyboard while holding the button"). A quick press
/// clicks <see cref="Button"/> (right click by default) the moment the button is let go; holding it for <see cref="HoldMs"/> shows or
/// hides the floating keyboard instead, with no click. A right click can't be taken back, so the click waits for the release rather
/// than for a double-press window. Users who want to hold the right button down to drag pick the plain right-click binding instead.
/// </summary>
[PluginName(PLUGIN_NAME)]
public class HoldForKeyboardBinding : IStateBinding
{
    public const string PLUGIN_NAME = "Tablet App for Mac - Click, hold for keyboard";

    [Resolved] public IMouseButtonHandler? Pointer { set; get; }
    [Resolved] public IVirtualKeyboard? Keyboard { set; get; }

    [Property("Button")] public string? Button { set; get; } = "Right";
    [Property("HoldMs")] public int HoldMs { set; get; } = DefaultHoldMs;

    public const int DefaultHoldMs = 500;

    /// <summary>Writes the toggle request; tests replace it.</summary>
    public static Action? ToggleRunner { get; set; }
    public static string ToggleRequestPath { get; set; } = FloatingKeyboardFiles.ToggleRequest;

    private readonly object _gate = new();
    private Timer? _timer;
    private bool _down, _held;
    private long _press;

    public void Press(TabletReference tablet, IDeviceReport report)
    {
        lock (_gate)
        {
            if (_down) return;
            _down = true;
            _held = false;
            long press = ++_press;
            _timer?.Dispose();
            _timer = new Timer(_ => OnHeld(press), null, Math.Clamp(HoldMs, 150, 3000), Timeout.Infinite);
        }
    }

    public void Release(TabletReference tablet, IDeviceReport report)
    {
        bool click;
        lock (_gate)
        {
            if (!_down) return;
            _down = false;
            _timer?.Dispose();
            _timer = null;
            click = !_held;
        }
        if (!click) return;
        if (!Enum.TryParse<MouseButton>(Button ?? "Right", true, out var b)) b = MouseButton.Right;
        Pointer?.MouseDown(b);
        Pointer?.MouseUp(b);
    }

    private void OnHeld(long press)
    {
        lock (_gate)
        {
            if (!_down || _held || press != _press) return;
            _held = true;
        }
        try
        {
            FloatingKeyboardKeys.Remember(Keyboard);
            FloatingKeyboardKeys.Start();
            (ToggleRunner ?? WriteToggleRequest)();
        }
        catch (Exception e)
        {
            Log.Write(nameof(HoldForKeyboardBinding), $"Could not ask the app to show the keyboard: {e.Message}", LogLevel.Warning);
        }
    }

    private static void WriteToggleRequest()
    {
        var dir = Path.GetDirectoryName(ToggleRequestPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = ToggleRequestPath + ".tmp";
        File.WriteAllText(tmp, DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.Move(tmp, ToggleRequestPath, overwrite: true);
    }

    public override string ToString() => "Click, hold for keyboard";
}
