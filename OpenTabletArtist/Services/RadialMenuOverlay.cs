using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Shows the radial menu on screen (TASKS #42). The driver plugin (RadialMenuState, inside the daemon) writes
/// the menu's state to a small file whenever it opens, changes level or closes; this watches that file and
/// draws the ring centred where the pen was. The window never takes focus or clicks: the drawing app keeps
/// the keyboard, and the plugin itself reads where the pen tip went down. While open, the slice under the
/// pointer lights up (polled here, so it needs nothing from the driver).
/// Also owns the Radial menu On/Off switch (Settings › System): Off writes a flag file the plugin checks.
/// </summary>
public sealed class RadialMenuOverlay : IDisposable
{
    public const string SettingKey = "RadialMenu";

    /// <summary>Same files the plugin uses (RadialMenuState.StateFilePath / OffFlagPath).</summary>
    public static string StateFilePath { get; } = Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-menu");
    public static string OffFlagPath { get; } = Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "radial-menu-off");

    /// <summary>The Radial menu switch. On by default: nothing happens until a button is set to Radial menu.</summary>
    public static bool Enabled
    {
        get => AppSettings.Get(SettingKey) != "0";
        set
        {
            AppSettings.Set(SettingKey, value ? "1" : "0");
            SyncOffFlag();
        }
    }

    /// <summary>Make the plugin's flag file match the switch (it can't read the app's settings).</summary>
    public static void SyncOffFlag()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OffFlagPath)!);
            if (Enabled) { if (File.Exists(OffFlagPath)) File.Delete(OffFlagPath); }
            else File.WriteAllText(OffFlagPath, "off");
        }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu: couldn't update the off switch file", e);
        }
    }

    private FileSystemWatcher? _watcher;
    private Window? _window;
    private RadialMenuPie? _pie;
    private DispatcherTimer? _pointerTimer;
    private long _lastSeq = -1;
    private double _cx, _cy;
    private int _size = RadialMenuDef.DefaultSize;

    public void Start()
    {
        SyncOffFlag();
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath)!;
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(StateFilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _watcher.Changed += (_, _) => OnFileChanged();
            _watcher.Created += (_, _) => OnFileChanged();
            _watcher.Renamed += (_, _) => OnFileChanged();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu display unavailable", e);
        }
    }

    /// <summary>What the state file says (null when it can't be read).</summary>
    public sealed record MenuState(string State, long Seq, double X, double Y, int Size, int Depth,
        IReadOnlyList<string> Labels, IReadOnlyList<bool> Set, IReadOnlyList<bool> Submenu);

    public static MenuState? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var state = r.GetProperty("state").GetString() ?? "closed";
            long seq = r.TryGetProperty("seq", out var sq) ? sq.GetInt64() : 0;
            if (state != "open") return new MenuState(state, seq, 0, 0, RadialMenuDef.DefaultSize, 0,
                Array.Empty<string>(), Array.Empty<bool>(), Array.Empty<bool>());
            var labels = new List<string>(); var set = new List<bool>(); var sub = new List<bool>();
            if (r.TryGetProperty("slices", out var slices))
                foreach (var s in slices.EnumerateArray())
                {
                    labels.Add(s.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "");
                    set.Add(s.TryGetProperty("set", out var st) && st.GetBoolean());
                    sub.Add(s.TryGetProperty("action", out var a) && a.GetString() == RadialActions.Submenu);
                }
            return new MenuState(state, seq,
                r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble(),
                r.TryGetProperty("size", out var sz) ? sz.GetInt32() : RadialMenuDef.DefaultSize,
                r.TryGetProperty("depth", out var d) ? d.GetInt32() : 0,
                labels, set, sub);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnFileChanged()
    {
        string content;
        try { content = File.ReadAllText(StateFilePath); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        var st = Parse(content);
        if (st == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (st.Seq == _lastSeq) return;   // a watcher can fire twice for one write
            _lastSeq = st.Seq;
            if (st.State == "open" && Enabled) Show(st);
            else Hide();
        });
    }

    private void Show(MenuState st)
    {
        try
        {
            EnsureWindow();
            _cx = st.X; _cy = st.Y; _size = st.Size;
            double r = RadialMenuGeometry.OuterRadius(st.Size);
            double d = 2 * r + 4;
            _window!.Width = d;
            _window.Height = d;
            // On macOS Avalonia's window coordinates are global points with a top-left origin, the same
            // space as CGEventGetLocation, which is where the plugin took the centre from (see RingModeHud).
            _window.Position = new PixelPoint((int)Math.Round(st.X - d / 2), (int)Math.Round(st.Y - d / 2));
            _pie!.Labels = st.Labels;
            _pie.SetFlags = st.Set;
            _pie.SubmenuFlags = st.Submenu;
            _pie.Highlight = HighlightNow();
            if (!_window.IsVisible) _window.Show();
            if (_window.TryGetPlatformHandle() is Avalonia.Platform.IMacOSTopLevelPlatformHandle mac && mac.NSWindow != IntPtr.Zero)
                MacDesktop.MakeOverlay(mac.NSWindow);
            _pointerTimer!.Start();
            AppLog.Info($"Radial menu shown at {st.X:0},{st.Y:0} (size {st.Size}, level {st.Depth}).");
        }
        catch (Exception e)
        {
            AppLog.Warn("Radial menu display failed", e);
        }
    }

    private void Hide()
    {
        _pointerTimer?.Stop();
        if (_window?.IsVisible == true) _window.Hide();
    }

    private int HighlightNow()
    {
        if (MacDesktop.CursorLocation() is not { } p) return -3;
        return RadialMenuGeometry.SliceAt(p.X - _cx, p.Y - _cy, _size);
    }

    private void EnsureWindow()
    {
        if (_window != null) return;
        _pie = new RadialMenuPie { IsHitTestVisible = false };
        _window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            SizeToContent = SizeToContent.Manual,
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            CanResize = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Focusable = false,
            IsHitTestVisible = false,
            Content = _pie,
            Title = "Radial menu",
        };
        _pointerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _pointerTimer.Tick += (_, _) =>
        {
            if (_pie == null) return;
            var h = HighlightNow();
            if (h != _pie.Highlight) _pie.Highlight = h;
        };
    }

    /// <summary>The window once it exists (tests: screenshots).</summary>
    internal Window? MenuWindow => _window;

    /// <summary>Show a menu right now, on the UI thread (tests).</summary>
    internal void ShowNowForTest(MenuState st) => Show(st);

    public void Dispose()
    {
        _pointerTimer?.Stop();
        _watcher?.Dispose();
        _window?.Close();
    }
}
