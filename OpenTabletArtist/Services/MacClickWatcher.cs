using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace OpenTabletArtist.Services;

/// <summary>
/// macOS side of the click recording (TASKS #85): a listen-only event tap at the session level sees every
/// mouse and tablet event on its way to the apps, real mouse clicks and the clicks the pen's driver posts
/// alike, so the two can be compared field by field. A poller notes the front app (NSWorkspace) and the
/// front window (CGWindowList) every 20 ms for 3 s after each click, so the report shows how long each
/// click took to bring its window and app forward. Listen-only: it never changes or holds back an event.
/// Fails open: if macOS refuses the tap, the recording says so and everything else works as before.
/// </summary>
public sealed class MacClickWatcher : IClickWatcher
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    private const uint SessionEventTap = 1, HeadInsert = 0, ListenOnly = 1;
    private const uint TapDisabledByTimeout = 0xFFFFFFFE, TapDisabledByUserInput = 0xFFFFFFFF;
    private const uint OnScreenOnly = 1, IncludingWindow = 8, ExcludeDesktop = 16;
    private const uint Utf8 = 0x08000100;
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(20);

    private static readonly ulong Mask = Bits(1, 2, 3, 4, 5, 6, 7, 23, 24, 25, 26, 27);

    [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CGRect { public double X, Y, W, H; }

    [DllImport(CoreGraphics)]
    private static extern unsafe IntPtr CGEventTapCreate(uint tap, uint place, uint options, ulong events,
        delegate* unmanaged[Cdecl]<IntPtr, uint, IntPtr, IntPtr, IntPtr> callback, IntPtr userInfo);
    [DllImport(CoreGraphics)] private static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);
    [DllImport(CoreGraphics)] private static extern long CGEventGetIntegerValueField(IntPtr ev, uint field);
    [DllImport(CoreGraphics)] private static extern double CGEventGetDoubleValueField(IntPtr ev, uint field);
    [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr ev);
    [DllImport(CoreGraphics)] private static extern ulong CGEventGetFlags(IntPtr ev);
    [DllImport(CoreGraphics)] private static extern ulong CGEventGetTimestamp(IntPtr ev);
    [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreFoundation)] private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr alloc, IntPtr port, nint order);
    [DllImport(CoreFoundation)] private static extern void CFMachPortInvalidate(IntPtr port);
    [DllImport(CoreFoundation)] private static extern IntPtr CFRunLoopGetCurrent();
    [DllImport(CoreFoundation)] private static extern void CFRunLoopAddSource(IntPtr loop, IntPtr source, IntPtr mode);
    [DllImport(CoreFoundation)] private static extern int CFRunLoopRunInMode(IntPtr mode, double seconds, [MarshalAs(UnmanagedType.I1)] bool returnAfterSourceHandled);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);
    [DllImport(CoreFoundation)] private static extern nint CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(IntPtr number, int type, out int value);
    [DllImport(CoreFoundation, EntryPoint = "CFNumberGetValue")] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetDouble(IntPtr number, int type, out double value);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string s, uint encoding);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr str, byte[] buffer, nint size, uint encoding);

    [DllImport(ObjC)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern int SendInt(IntPtr obj, IntPtr sel);

    [DllImport(LibSystem)] private static extern int proc_name(int pid, byte[] buffer, uint size);

    private static volatile MacClickWatcher? _current;

    private readonly ClickLog _log = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private Func<TimeSpan> _now = () => TimeSpan.Zero;
    private IntPtr _tap;
    private volatile bool _stop;
    private Thread? _thread;
    private Task? _poller;
    private bool _started;
    private IntPtr _kPid, _kName, _kLayer, _kAlpha, _kNumber;

    private static ulong Bits(params int[] types)
    {
        ulong m = 0;
        foreach (var t in types) m |= 1UL << t;
        return m;
    }

    public void Start(Func<TimeSpan> now)
    {
        if (!OperatingSystem.IsMacOS() || _started) return;
        _started = true;
        _now = now;
        _stop = false;
        try
        {
            NativeLibrary.Load(AppKit);
            _kPid = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", Utf8);
            _kName = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerName", Utf8);
            _kLayer = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", Utf8);
            _kAlpha = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowAlpha", Utf8);
            _kNumber = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowNumber", Utf8);
        }
        catch (Exception ex)
        {
            _log.Problem("Couldn't load the macOS parts needed to watch the mouse: " + ex.Message);
            return;
        }
        _current = this;
        _thread = new Thread(RunTap) { IsBackground = true, Name = "Record the problem: mouse" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
        _poller = Task.Run(PollAsync);
        AppLog.Info("Report: watching mouse and click events for the recording (listen only).");
    }

    public string? Stop()
    {
        if (!_started) return null;
        _started = false;
        _stop = true;
        try { _thread?.Join(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        try { _poller?.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        if (_current == this) _current = null;
        NameWhatWasSeen();
        foreach (var k in new[] { _kPid, _kName, _kLayer, _kAlpha, _kNumber })
            if (k != IntPtr.Zero) CFRelease(k);
        _kPid = _kName = _kLayer = _kAlpha = _kNumber = IntPtr.Zero;
        AppLog.Info($"Report: stopped watching mouse and click events ({_log.ClickCount} clicks).");
        return _log.Format();
    }

    private unsafe void RunTap()
    {
        IntPtr source = IntPtr.Zero;
        try
        {
            var mode = DefaultMode();
            _tap = CGEventTapCreate(SessionEventTap, HeadInsert, ListenOnly, Mask, &OnEvent, IntPtr.Zero);
            if (_tap == IntPtr.Zero)
            {
                _log.Problem("macOS didn't let the app watch the mouse (event tap refused; Input Monitoring?). Only the tablet reports are recorded.");
                return;
            }
            source = CFMachPortCreateRunLoopSource(IntPtr.Zero, _tap, 0);
            CFRunLoopAddSource(CFRunLoopGetCurrent(), source, mode);
            CGEventTapEnable(_tap, true);
            _log.Note("Watching: a listen-only event tap at the session level (sees the mouse and the pen's posted clicks).");
            _ready.Set();
            while (!_stop) CFRunLoopRunInMode(mode, 0.2, false);
        }
        catch (Exception ex)
        {
            _log.Problem("Watching the mouse stopped with an error: " + ex.Message);
        }
        finally
        {
            _ready.Set();
            if (_tap != IntPtr.Zero)
            {
                try { CGEventTapEnable(_tap, false); CFMachPortInvalidate(_tap); } catch (Exception) { }
            }
            if (source != IntPtr.Zero) CFRelease(source);
            if (_tap != IntPtr.Zero) CFRelease(_tap);
            _tap = IntPtr.Zero;
        }
    }

    private static IntPtr DefaultMode()
    {
        var cf = NativeLibrary.Load(CoreFoundation);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(cf, "kCFRunLoopDefaultMode"));
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr OnEvent(IntPtr proxy, uint type, IntPtr ev, IntPtr userInfo)
    {
        var w = _current;
        if (w == null) return ev;
        try
        {
            if (type is TapDisabledByTimeout or TapDisabledByUserInput)
            {
                w._log.Note($"{w._now().TotalSeconds:0.000} s: macOS paused the watcher ({(type == TapDisabledByTimeout ? "timeout" : "user input")}); turned it back on.");
                if (w._tap != IntPtr.Zero) CGEventTapEnable(w._tap, true);
                return ev;
            }
            if (ev == IntPtr.Zero) return ev;
            var at = CGEventGetLocation(ev);
            w._log.Add(new MouseSample(
                w._now(), (int)type, at.X, at.Y,
                Button: CGEventGetIntegerValueField(ev, 3),
                ClickState: CGEventGetIntegerValueField(ev, 1),
                Subtype: CGEventGetIntegerValueField(ev, 7),
                EventNumber: CGEventGetIntegerValueField(ev, 0),
                Pressure: CGEventGetDoubleValueField(ev, 2),
                SourcePid: CGEventGetIntegerValueField(ev, 41),
                SourceStateId: CGEventGetIntegerValueField(ev, 45),
                SourceUserData: CGEventGetIntegerValueField(ev, 42),
                TargetPid: CGEventGetIntegerValueField(ev, 40),
                WindowUnder: CGEventGetIntegerValueField(ev, 91),
                WindowThatCanHandle: CGEventGetIntegerValueField(ev, 92),
                DeltaX: CGEventGetIntegerValueField(ev, 4),
                DeltaY: CGEventGetIntegerValueField(ev, 5),
                Flags: CGEventGetFlags(ev),
                Timestamp: CGEventGetTimestamp(ev)));
        }
        catch (Exception)
        {
            // Never let an exception cross into the event system; the event goes on unchanged.
        }
        return ev;
    }

    private async Task PollAsync()
    {
        try
        {
            await ReadFrontAsync();
            var tick = 0;
            while (!_stop)
            {
                await Task.Delay(PollEvery);
                var last = _log.LastClickAt;
                if (last is { } t && _now() - t <= ClickLog.FollowFor) await ReadFrontAsync();
                if (++tick % 5 == 0) NameWhatWasSeen();
            }
        }
        catch (Exception ex)
        {
            _log.Problem("Following the front app stopped with an error: " + ex.Message);
        }
    }

    private async Task ReadFrontAsync()
    {
        var at = _now();
        var (wPid, wName) = FrontWindow();
        if (wPid > 0) _log.Front(at, ClickLog.FrontKind.Window, wPid, wName);
        if (_stop) return;
        (int Pid, string? Name) app;
        try
        {
            app = Dispatcher.UIThread.CheckAccess()
                ? FrontApp()
                : await Dispatcher.UIThread.InvokeAsync(FrontApp).GetTask().WaitAsync(TimeSpan.FromMilliseconds(250));
        }
        catch (TimeoutException) { return; }
        if (app.Pid > 0) _log.Front(at, ClickLog.FrontKind.App, app.Pid, app.Name);
    }

    /// <summary>The active app (the one in the menu bar). Called on the UI thread.</summary>
    private static (int Pid, string? Name) FrontApp()
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var wsClass = ObjCGuard.Class("NSWorkspace");
            if (!ObjCGuard.RespondsTo(wsClass, ObjCSelectors.SharedWorkspace)) return (0, null);
            var ws = Send(wsClass, ObjCGuard.Sel(ObjCSelectors.SharedWorkspace));
            if (!ObjCGuard.RespondsTo(ws, ObjCSelectors.FrontmostApplication)) return (0, null);
            var app = Send(ws, ObjCGuard.Sel(ObjCSelectors.FrontmostApplication));
            if (!ObjCGuard.RespondsTo(app, ObjCSelectors.ProcessIdentifier)) return (0, null);
            var pid = SendInt(app, ObjCGuard.Sel(ObjCSelectors.ProcessIdentifier));
            var name = ObjCGuard.RespondsTo(app, "localizedName") ? Send(app, ObjCGuard.Sel("localizedName")) : IntPtr.Zero;
            var utf8 = ObjCGuard.RespondsTo(name, ObjCSelectors.Utf8String) ? Send(name, ObjCGuard.Sel(ObjCSelectors.Utf8String)) : IntPtr.Zero;
            return (pid, utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8));
        }
        finally { objc_autoreleasePoolPop(pool); }
    }

    /// <summary>The owner of the top normal window (layer 0) on screen.</summary>
    private (int Pid, string? Name) FrontWindow()
    {
        var list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktop, 0);
        if (list == IntPtr.Zero) return (0, null);
        try
        {
            var n = CFArrayGetCount(list);
            for (nint i = 0; i < n; i++)
            {
                var d = CFArrayGetValueAtIndex(list, i);
                if (d == IntPtr.Zero) continue;
                if (Int(d, _kLayer) != 0) continue;
                var alphaRef = CFDictionaryGetValue(d, _kAlpha);
                if (alphaRef != IntPtr.Zero && CFNumberGetDouble(alphaRef, 13, out var alpha) && alpha <= 0) continue;
                return (Int(d, _kPid), CFString(CFDictionaryGetValue(d, _kName)));
            }
        }
        finally { CFRelease(list); }
        return (0, null);
    }

    private void NameWhatWasSeen()
    {
        foreach (var id in _log.UnnamedWindows())
        {
            var list = CGWindowListCopyWindowInfo(IncludingWindow, (uint)id);
            if (list == IntPtr.Zero) { _log.NameWindow(id, 0, "not found", -1); continue; }
            try
            {
                var d = CFArrayGetCount(list) < 1 ? IntPtr.Zero : CFArrayGetValueAtIndex(list, 0);
                if (d == IntPtr.Zero || Int(d, _kNumber) != id) { _log.NameWindow(id, 0, "not on screen any more", -1); continue; }
                _log.NameWindow(id, Int(d, _kPid), CFString(CFDictionaryGetValue(d, _kName)), Int(d, _kLayer));
            }
            finally { CFRelease(list); }
        }
        foreach (var pid in _log.UnnamedProcesses())
        {
            var buf = new byte[256];
            var len = proc_name((int)pid, buf, (uint)buf.Length);
            _log.NameProcess(pid, len > 0 ? System.Text.Encoding.UTF8.GetString(buf, 0, len) : null);
        }
    }

    private static int Int(IntPtr dict, IntPtr key)
    {
        var r = CFDictionaryGetValue(dict, key);
        return r != IntPtr.Zero && CFNumberGetValue(r, 3, out var v) ? v : 0;
    }

    private static string? CFString(IntPtr s)
    {
        if (s == IntPtr.Zero) return null;
        var buf = new byte[256];
        if (!CFStringGetCString(s, buf, buf.Length, Utf8)) return null;
        var len = Array.IndexOf(buf, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buf, 0, len < 0 ? buf.Length : len);
    }
}
