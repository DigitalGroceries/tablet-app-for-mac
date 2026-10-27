using System.Collections.ObjectModel;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

public enum PenSwitchBindingMode
{
    Auto,
    Legacy,
    Other
}

public enum PenSwitchKind
{
    Tip,
    Eraser,
    PenButton
}

/// <summary>Detects and applies Auto (Adaptive), Legacy (Windows Ink), and Other pen-switch bindings.</summary>
public static class PenSwitchBinding
{
    public const string AdaptiveBindingPath = "OpenTabletDriver.Desktop.Binding.AdaptiveBinding";
    public const string WindowsInkButtonHandlerPath = "VoiDPlugins.OutputMode.WindowsInkButtonHandler";
    public const string MouseBindingPath = "OpenTabletDriver.Desktop.Binding.MouseBinding";
    /// <summary>Version 1: a disabled tip or eraser. Clicks only in this app's own window (plugin binding).</summary>
    public const string OwnWindowBindingPath = "OpenTabletArtist.Dynamics.OwnWindowClickBinding";

    /// <summary>True when this pen end is switched off: no binding, or the this-app-only click.</summary>
    public static bool IsOff(PluginSettingStore? store) => store?.Path == null || store.Path == OwnWindowBindingPath;

    /// <summary>The binding a disabled end gets: left click in this app's window only.</summary>
    public static PluginSettingStore MakeOwnWindowBinding()
    {
        var store = CreateMinimalStore(OwnWindowBindingPath);
        SetSetting(store, "Button", "Left");
        return store;
    }

    private static readonly Dictionary<string, string> KnownPluginNames = new(StringComparer.Ordinal)
    {
        [AdaptiveBindingPath] = "Adaptive Binding",
        [WindowsInkButtonHandlerPath] = "Windows Ink",
        [MouseBindingPath] = "Mouse Button Binding",
        [OwnWindowBindingPath] = "Off (clicks in this app only)",
        [PenScrollBinding.BindingTypeName] = "Pen scroll",
    };

    public static PenSwitchBindingMode DetectMode(PluginSettingStore? store, PenSwitchKind kind, int penButtonIndex = 1)
    {
        if (store?.Path == null) return PenSwitchBindingMode.Other;
        if (IsAdaptive(store, kind, penButtonIndex)) return PenSwitchBindingMode.Auto;
        if (IsLegacy(store, kind)) return PenSwitchBindingMode.Legacy;
        return PenSwitchBindingMode.Other;
    }

    public static string GetDisplayLabel(
        PluginSettingStore? store,
        PenSwitchKind kind,
        int penButtonIndex = 1,
        Func<string?, string?>? friendlyName = null)
    {
        var mode = DetectMode(store, kind, penButtonIndex);
        return mode switch
        {
            PenSwitchBindingMode.Auto => kind switch
            {
                PenSwitchKind.Tip => "Adaptive Binding (Tip)",
                PenSwitchKind.Eraser => "Adaptive Binding (Eraser)",
                PenSwitchKind.PenButton => $"Adaptive Binding (Button {penButtonIndex})",
                _ => "Adaptive Binding"
            },
            PenSwitchBindingMode.Legacy => kind switch
            {
                PenSwitchKind.Tip => OperatingSystem.IsWindows() ? "Windows Ink, Pen Tip" : "Legacy binding, Pen tip",
                PenSwitchKind.Eraser => OperatingSystem.IsWindows() ? "Windows Ink, Eraser" : "Legacy binding, Eraser",
                PenSwitchKind.PenButton => OperatingSystem.IsWindows() ? $"Windows Ink, Button {penButtonIndex}" : $"Legacy binding, Button {penButtonIndex}",
                _ => OperatingSystem.IsWindows() ? "Windows Ink" : "Legacy binding"
            },
            _ => store == null ? "None" : FormatOtherBindingName(store, friendlyName)
        };
    }

    /// <summary>A short, plain-language name for what a switch is currently bound to — for the "Currently: …"
    /// line on the simplified pen-input cards (#pen-inputs-simplify). Avoids the internal plugin jargon that
    /// <see cref="GetDisplayLabel"/> exposes: the recommended state reads "Normal", legacy reads "Windows Ink
    /// (legacy)", and the common custom bindings map to friendly names, falling back to the plugin's friendly
    /// name for anything unrecognised.</summary>
    public static string GetPlainCurrentLabel(
        PluginSettingStore? store,
        PenSwitchKind kind,
        int penButtonIndex = 1,
        Func<string?, string?>? friendlyName = null)
    {
        switch (DetectMode(store, kind, penButtonIndex))
        {
            case PenSwitchBindingMode.Auto: return "Normal";
            case PenSwitchBindingMode.Legacy: return OperatingSystem.IsWindows() ? "Windows Ink (legacy)" : "Legacy binding";
        }

        var path = store?.Path;
        if (path == null) return "None";
        if (path == MouseBindingPath) return "Mouse button";
        if (path == OwnWindowBindingPath) return "Off (clicks in this app only)";
        if (path.EndsWith("MouseScrollBinding", StringComparison.Ordinal)) return "Mouse scroll";
        if (path == PenScrollBinding.BindingTypeName) return "Move pen to scroll";
        if (path.EndsWith("MultiKeyBinding", StringComparison.Ordinal)
            || path.EndsWith("KeyBinding", StringComparison.Ordinal)) return "Keyboard key";
        if (path.EndsWith("PresetBinding", StringComparison.Ordinal)) return "Preset";
        return FormatOtherBindingName(store!, friendlyName);
    }

    public static PluginSettingStore MakeAdaptiveBinding(PenSwitchKind kind, int penButtonIndex = 1)
    {
        var value = kind switch
        {
            PenSwitchKind.Tip => "Tip",
            PenSwitchKind.Eraser => "Eraser",
            PenSwitchKind.PenButton => $"Button {penButtonIndex}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var store = new PluginSettingStore(typeof(AdaptiveBinding), true);
        var bindingSetting = store.Settings.FirstOrDefault(s => s.Property == "Binding");
        if (bindingSetting != null)
            bindingSetting.SetValue(value);
        else
            store.Settings.Add(new PluginSetting("Binding", value));
        return store;
    }

    public static PluginSettingStore MakeLegacyBinding(PenSwitchKind kind)
    {
        var store = PluginSettingStore.FromPath(WindowsInkButtonHandlerPath)
            ?? CreateMinimalStore(WindowsInkButtonHandlerPath);
        SetSetting(store, "Button", LegacyButtonValue(kind));
        return store;
    }

    private static bool IsAdaptive(PluginSettingStore store, PenSwitchKind kind, int penButtonIndex)
    {
        if (!store.Path.Equals(AdaptiveBindingPath, StringComparison.Ordinal)) return false;
        var binding = GetSettingValue(store, "Binding");
        return kind switch
        {
            PenSwitchKind.Tip => binding == "Tip",
            PenSwitchKind.Eraser => binding == "Eraser",
            PenSwitchKind.PenButton => binding == $"Button {penButtonIndex}",
            _ => false
        };
    }

    private static bool IsLegacy(PluginSettingStore store, PenSwitchKind kind)
    {
        if (!store.Path.Equals(WindowsInkButtonHandlerPath, StringComparison.Ordinal)) return false;
        var button = GetSettingValue(store, "Button");
        return kind switch
        {
            PenSwitchKind.Tip => button == "Pen Tip",
            PenSwitchKind.Eraser => button is "Eraser (Hold)" or "Eraser (Toggle)",
            PenSwitchKind.PenButton => button == "Pen Button",
            _ => false
        };
    }

    private static string LegacyButtonValue(PenSwitchKind kind) => kind switch
    {
        PenSwitchKind.Tip => "Pen Tip",
        PenSwitchKind.Eraser => "Eraser (Hold)",
        PenSwitchKind.PenButton => "Pen Button",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Friendly label for a binding shown read-only (e.g. ExpressKeys).</summary>
    public static string GetBindingLabel(PluginSettingStore? store, Func<string?, string?>? friendlyName = null)
    {
        if (store?.Path == null) return "None";
        return FormatOtherBindingName(store, friendlyName);
    }

    private static string FormatOtherBindingName(PluginSettingStore store, Func<string?, string?>? friendlyName)
    {
        var name = friendlyName?.Invoke(store.Path)
            ?? KnownPluginNames.GetValueOrDefault(store.Path)
            ?? store.Path.Split('.').LastOrDefault()
            ?? store.Path;

        var button = GetSettingValue(store, "Button");
        if (!string.IsNullOrEmpty(button))
            return $"{name}, {button}";

        var binding = GetSettingValue(store, "Binding");
        if (!string.IsNullOrEmpty(binding))
            return $"{name} ({binding})";

        return name;
    }

    private static string? GetSettingValue(PluginSettingStore store, string property) =>
        store.Settings.FirstOrDefault(s => s.Property == property)?.Value?.ToString();

    private static void SetSetting(PluginSettingStore store, string property, string value)
    {
        var setting = store.Settings.FirstOrDefault(s => s.Property == property);
        if (setting != null)
            setting.SetValue(value);
        else
            store.Settings.Add(new PluginSetting(property, value));
    }

    private static PluginSettingStore CreateMinimalStore(string path)
    {
        var json = $$"""{"Path":"{{path}}","Enable":true,"Settings":[]}""";
        var store = JsonConvert.DeserializeObject<PluginSettingStore>(json)!;
        store.Settings ??= new ObservableCollection<PluginSetting>();
        return store;
    }
}
