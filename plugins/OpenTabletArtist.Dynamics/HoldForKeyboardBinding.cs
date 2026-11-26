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
/// V29 #160 (Digital Groceries 20 Nov ~09:05-09:24: pen button 1 should open the floating keyboard; "what about long press instead of click?";
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
        // Digital Groceries 20 Nov (build 8): "No right click when Button 1 set to open the keyboard". The Mac pointer only posts a button when its
        // state differs at Flush (MacOSVirtualMouse.Flush compares the buttons with the last posted ones), so a down and an up in the
        // same report cancelled out and no click was posted (the recording showed 0 right clicks from the driver). The down is flushed
        // first, as RadialMenu.cs and PenScroll do; the up goes out with this report's own flush.
        var p = Pointer;
        if (p == null) { Log.Write(nameof(HoldForKeyboardBinding), "No pointer to click with.", LogLevel.Warning); return; }
        p.MouseDown(b);
        if (p is ISynchronousPointer sync) sync.Flush();
        p.MouseUp(b);
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
