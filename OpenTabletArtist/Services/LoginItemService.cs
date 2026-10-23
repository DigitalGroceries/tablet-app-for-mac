using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenTabletArtist.Services;

/// <summary>Build P: the state of "start at login" as macOS reports it (mirrors SMAppServiceStatus).</summary>
public enum LoginItemStatus { NotRegistered = 0, Enabled = 1, RequiresApproval = 2, NotFound = 3, Unsupported = -1 }

/// <summary>Build P: start-at-login. On macOS this is <c>SMAppService.mainAppService</c> (macOS 13+). Abstracted so the
/// prompt-once and settings-sync logic can be tested with a fake.</summary>
public interface ILoginItemService
{
    bool IsSupported { get; }
    LoginItemStatus Status { get; }
    /// <summary>Register; false + a plain-English reason on failure (e.g. ad-hoc signing refused).</summary>
    bool TryEnable(out string? error);
    bool TryDisable(out string? error);
    /// <summary>Open System Settings › General › Login Items.</summary>
    void OpenLoginItemsSettings();
}

/// <summary>+[SMAppService mainAppService] through the Objective-C runtime (no Swift helper needed). Every call is guarded:
/// anything missing or failing reports an error instead of throwing. UNVERIFIED on a real Mac, especially with
/// ad-hoc signing (no Team ID).</summary>
public sealed class MacLoginItemService : ILoginItemService
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr r, IntPtr s);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern long SendLong(IntPtr r, IntPtr s);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBoolErr(IntPtr r, IntPtr s, out IntPtr error);
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern IntPtr dlopen(string path, int mode);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendStr(IntPtr r, IntPtr s, [MarshalAs(UnmanagedType.LPUTF8Str)] string a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr r, IntPtr s, IntPtr a);

    /// <summary>Version 1: the login item is a launch agent inside the bundle that starts the app with
    /// <c>--background</c>, so a login start shows only the menu-bar icon. The install script writes it to
    /// Contents/Library/LaunchAgents before signing. Without it the old mainApp item (no arguments) is used.</summary>
    public const string AgentPlistName = "com.freetabletapp.mac.login.plist";

    public static string AgentPlistPath =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "Library", "LaunchAgents", AgentPlistName));

    private static bool HasAgent => System.IO.File.Exists(AgentPlistPath);

    /// <summary>+[SMAppService agentServiceWithPlistName:], or zero.</summary>
    private static IntPtr AgentService()
    {
        if (MainApp() == IntPtr.Zero) return IntPtr.Zero;   // loads ServiceManagement, checks macOS 13+
        var cls = objc_getClass("SMAppService");
        if (cls == IntPtr.Zero || !ObjCGuard.RespondsTo(cls, ObjCSelectors.AgentServiceWithPlistName)) return IntPtr.Zero;
        var name = SendStr(objc_getClass("NSString"), sel_registerName(ObjCSelectors.StringWithUtf8String), AgentPlistName);
        return name == IntPtr.Zero ? IntPtr.Zero : SendPtr(cls, sel_registerName(ObjCSelectors.AgentServiceWithPlistName), name);
    }

    /// <summary>The service this app uses for start at login: the agent when the bundle has it.</summary>
    private static IntPtr Service() => HasAgent && AgentService() is var a && a != IntPtr.Zero ? a : MainApp();

    private static bool _loaded;
    private static IntPtr MainApp()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(13)) return IntPtr.Zero;
        if (!_loaded)
        {
            dlopen("/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement", 1);
            _loaded = true;
        }
        return ResolveMainApp(objc_getClass, ObjCGuard.RespondsTo, (r, sel) => Send(r, sel_registerName(sel)));
    }

    /// <summary>Build Q: +[SMAppService mainAppService] (ObjC name; "mainApp" is Swift-only and crashed build P),
    /// sent only when the class exists and responds. Seams for tests.</summary>
    public static IntPtr ResolveMainApp(Func<string, IntPtr> getClass, Func<IntPtr, string, bool> respondsTo,
        Func<IntPtr, string, IntPtr> send)
    {
        var cls = getClass("SMAppService");
        if (cls == IntPtr.Zero || !respondsTo(cls, ObjCSelectors.MainAppService)) return IntPtr.Zero;
        return send(cls, ObjCSelectors.MainAppService);
    }

    private bool? _supported;
    /// <summary>Build Q: supported only when SMAppService really answers mainAppService and status; otherwise the
    /// start-at-login choice is shown as unavailable instead of crashing.</summary>
    public bool IsSupported => _supported ??= OperatingSystem.IsMacOS() && OperatingSystem.IsMacOSVersionAtLeast(13) && SafeProbe();

    private static bool SafeProbe()
    {
        try { var app = MainApp(); return app != IntPtr.Zero && ObjCGuard.RespondsTo(app, ObjCSelectors.Status); }
        catch (Exception ex) { AppLog.Warn("Start at login unavailable.", ex); return false; }
    }

    public LoginItemStatus Status
    {
        get
        {
            try
            {
                var app = Service();
                if (app == IntPtr.Zero || !ObjCGuard.RespondsTo(app, ObjCSelectors.Status)) return LoginItemStatus.Unsupported;
                return (LoginItemStatus)SendLong(app, sel_registerName(ObjCSelectors.Status));
            }
            catch (Exception ex) { AppLog.Warn("Couldn't read the login-item status.", ex); return LoginItemStatus.Unsupported; }
        }
    }

    public bool TryEnable(out string? error)
    {
        var ok = Call(Service(), ObjCSelectors.Register, out error);
        // Version 1: the agent replaces the older mainApp item, which started the app with its window open.
        if (ok && HasAgent) RemoveMainAppItem();
        return ok;
    }

    public bool TryDisable(out string? error)
    {
        var ok = Call(Service(), ObjCSelectors.Unregister, out error);
        if (HasAgent) RemoveMainAppItem();
        return ok;
    }

    /// <summary>Unregister the older mainApp login item if it is still there (best effort).</summary>
    public static void RemoveMainAppItem()
    {
        try
        {
            var app = MainApp();
            if (app == IntPtr.Zero || !ObjCGuard.RespondsTo(app, ObjCSelectors.Status)) return;
            if ((LoginItemStatus)SendLong(app, sel_registerName(ObjCSelectors.Status)) is LoginItemStatus.Enabled or LoginItemStatus.RequiresApproval)
                Call(app, ObjCSelectors.Unregister, out _);
        }
        catch (Exception ex) { AppLog.Warn("Couldn't remove the older login item.", ex); }
    }

    /// <summary>Version 1: move an existing start-at-login choice from the mainApp item to the agent once.</summary>
    public static void MigrateToAgent()
    {
        if (!OperatingSystem.IsMacOS() || !HasAgent) return;
        try
        {
            var app = MainApp();
            if (app == IntPtr.Zero || !ObjCGuard.RespondsTo(app, ObjCSelectors.Status)) return;
            if ((LoginItemStatus)SendLong(app, sel_registerName(ObjCSelectors.Status)) is not (LoginItemStatus.Enabled or LoginItemStatus.RequiresApproval)) return;
            if (Call(AgentService(), ObjCSelectors.Register, out var err)) Call(app, ObjCSelectors.Unregister, out _);
            else AppLog.Warn("Couldn't move start at login to the launch agent: " + err);
        }
        catch (Exception ex) { AppLog.Warn("Couldn't move start at login to the launch agent.", ex); }
    }

    private static bool Call(IntPtr app, string selector, out string? error)
    {
        error = null;
        try
        {
            if (app == IntPtr.Zero || !ObjCGuard.RespondsTo(app, selector))
            { error = "Start at login isn't available on this Mac."; return false; }
            if (SendBoolErr(app, sel_registerName(selector), out var err)) return true;
            error = Describe(err);
            return false;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static string Describe(IntPtr nsError)
    {
        if (nsError == IntPtr.Zero) return "macOS refused without saying why.";
        if (!ObjCGuard.RespondsTo(nsError, ObjCSelectors.LocalizedDescription)) return "macOS refused without saying why.";
        var desc = Send(nsError, sel_registerName(ObjCSelectors.LocalizedDescription));
        var utf8 = desc != IntPtr.Zero && ObjCGuard.RespondsTo(desc, ObjCSelectors.Utf8String)
            ? Send(desc, sel_registerName(ObjCSelectors.Utf8String)) : IntPtr.Zero;
        return utf8 == IntPtr.Zero ? "macOS refused without saying why." : Marshal.PtrToStringUTF8(utf8) ?? "";
    }

    public void OpenLoginItemsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("/usr/bin/open", "x-apple.systempreferences:com.apple.LoginItems-Settings.extension") { UseShellExecute = false });
        }
        catch (Exception ex) { AppLog.Warn("Couldn't open Login Items settings.", ex); }
    }
}

/// <summary>Platforms without this mechanism (Windows keeps its own Run-key toggle, #360).</summary>
public sealed class NoLoginItemService : ILoginItemService
{
    public bool IsSupported => false;
    public LoginItemStatus Status => LoginItemStatus.Unsupported;
    public bool TryEnable(out string? error) { error = "Not available on this platform."; return false; }
    public bool TryDisable(out string? error) { error = null; return true; }
    public void OpenLoginItemsSettings() { }
}

/// <summary>Build P: the "start automatically or manually?" choice — asked once (new and upgrading users alike),
/// remembered, and kept in step with the real login item (the user can remove it in System Settings).</summary>
public sealed class AutoStartCoordinator
{
    public const string PromptedKey = "AutoStartPrompted";
    public const string ChoiceKey = "AutoStartChoice";   // "auto" | "manual"
    public const string FallbackMessage =
        "macOS didn't let Tablet App for Mac add itself as a login item ({0}). You can add it yourself: open System Settings › General › Login Items, click +, and choose Tablet App for Mac.";

    private readonly ILoginItemService _svc;
    private readonly Func<string, string?> _get;
    private readonly Action<string, string> _set;

    public AutoStartCoordinator(ILoginItemService svc, Func<string, string?> get, Action<string, string> set)
    { _svc = svc; _get = get; _set = set; }

    public static AutoStartCoordinator Default { get; } = new(
        OperatingSystem.IsMacOS() ? new MacLoginItemService() : new NoLoginItemService(), AppSettings.Get, AppSettings.Set);

    public ILoginItemService Service => _svc;
    public bool IsSupported => _svc.IsSupported;

    /// <summary>True until the user has answered once. Upgrading users have no answer yet, so they're asked too.</summary>
    public bool ShouldPrompt => _svc.IsSupported && _get(PromptedKey) != "true";

    /// <summary>The real state; "requires approval" counts as on (the item exists, macOS is waiting for the user).</summary>
    public bool IsEnabled => _svc.Status is LoginItemStatus.Enabled or LoginItemStatus.RequiresApproval;

    public bool RequiresApproval => _svc.Status == LoginItemStatus.RequiresApproval;

    /// <summary>Apply a choice (from the prompt or the toggle). Returns null on success, else a message to show.</summary>
    public string? Choose(bool automatic)
    {
        _set(PromptedKey, "true");
        _set(ChoiceKey, automatic ? "auto" : "manual");
        string? error;
        bool ok = automatic ? _svc.TryEnable(out error) : _svc.TryDisable(out error);
        if (ok) return null;
        return automatic ? string.Format(FallbackMessage, error) : $"Couldn't remove the login item ({error}). Remove it in System Settings › General › Login Items.";
    }

    /// <summary>Build R: after the rename, carry the old app's start-at-login choice over once. "auto" registers
    /// the new app (the old OpenTabletArtist login item can't be removed from here; INSTALL says how); "manual"
    /// is remembered. Returns true when it acted, so the first-launch question isn't asked again.</summary>
    public bool ApplyLegacyChoice()
    {
        if (_get(PromptedKey) == "true") return false;
        var legacy = _get(SettingsMigration.LegacyAutoStartKey);
        if (legacy is not ("auto" or "manual")) return false;
        _set(SettingsMigration.LegacyAutoStartKey, "applied");
        if (!_svc.IsSupported) return false;
        var msg = Choose(legacy == "auto");
        if (msg != null) AppLog.Warn("Carrying start at login over from OpenTabletArtist failed: " + msg);
        return true;
    }

    /// <summary>Re-read the real state (call whenever Settings opens). Keeps the remembered choice in step.</summary>
    public bool Sync()
    {
        var on = IsEnabled;
        if (_get(PromptedKey) == "true") _set(ChoiceKey, on ? "auto" : "manual");
        return on;
    }

    /// <summary>macOS can't pass --background to a mainApp login item, so a launch counts as "at login" when
    /// automatic start is on and the user's login session (loginwindow) began under two minutes ago.
    /// Heuristic — UNVERIFIED on a real Mac.</summary>
    public bool IsLikelyLoginLaunch(Func<TimeSpan?> sessionAge) =>
        _svc.IsSupported && _get(ChoiceKey) == "auto" && sessionAge() is { } age && age < TimeSpan.FromMinutes(2);

    /// <summary>Age of this user's loginwindow process (ps etime), or null.</summary>
    public static TimeSpan? LoginSessionAge()
    {
        try
        {
            var psi = new ProcessStartInfo("/bin/ps") { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var a in new[] { "-x", "-U", Environment.UserName, "-o", "etime=,comm=" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                if (!t.EndsWith("loginwindow", StringComparison.Ordinal)) continue;
                return ParseEtime(t.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
            }
        }
        catch (Exception ex) { AppLog.Warn("Couldn't read the login session age.", ex); }
        return null;
    }

    /// <summary>ps etime: [[dd-]hh:]mm:ss</summary>
    public static TimeSpan? ParseEtime(string s)
    {
        int days = 0;
        var dash = s.IndexOf('-');
        if (dash > 0) { if (!int.TryParse(s[..dash], out days)) return null; s = s[(dash + 1)..]; }
        var parts = s.Split(':');
        int h = 0, m, sec;
        if (parts.Length == 3) { if (!int.TryParse(parts[0], out h)) return null; parts = parts[1..]; }
        if (parts.Length != 2 || !int.TryParse(parts[0], out m) || !int.TryParse(parts[1], out sec)) return null;
        return new TimeSpan(days, h, m, sec);
    }
}
