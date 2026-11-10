using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Services;

/// <summary>
/// TASKS #28: the per-key On/Off of the ExpressKeys page. An Off key keeps its binding but does nothing.
///
/// How it is stored (the same way the "All buttons" switch keeps its bindings): the table that goes to the
/// driver (All's profile, an application's table, an extra pen's table) holds <b>no binding</b> for an Off key,
/// and the binding it had is kept here, in the app settings, under one key per table:
/// <c>AuxKeyOff:{tablet}</c> (All), <c>AuxKeyOff:{tablet}:app:{bundleId}</c> (an application),
/// <c>AuxKeyOff:{tablet}:pen:{penKey}</c> (an extra pen). The value is a JSON object
/// <c>{ "3": &lt;binding&gt; | null }</c>: each listed button number is Off, and its value is the binding that comes
/// back when the key is turned On (null = it had none). A key that is not listed is On.
/// A key works only when "All buttons" is On <i>and</i> the key is On (<see cref="Works"/>).
/// </summary>
public static class AuxKeyOffStore
{
    public static string ScopeKey(string tablet, string? appBundleId = null, string? penKey = null) =>
        !string.IsNullOrEmpty(penKey) ? $"AuxKeyOff:{tablet}:pen:{penKey}"
        : !string.IsNullOrEmpty(appBundleId) ? $"AuxKeyOff:{tablet}:app:{appBundleId!.ToLowerInvariant()}"
        : $"AuxKeyOff:{tablet}";

    /// <summary>A key works only when All buttons is On and the key itself is On.</summary>
    public static bool Works(bool allButtonsOn, bool keyOn) => allButtonsOn && keyOn;

    /// <summary>The Off keys of one table: button number (1-based) → the binding kept for it (null = none).</summary>
    public static Dictionary<int, PluginSettingStore?> Load(string scopeKey)
    {
        var raw = AppSettings.Get(scopeKey);
        if (string.IsNullOrWhiteSpace(raw)) return new();
        try
        {
            return JsonConvert.DeserializeObject<Dictionary<int, PluginSettingStore?>>(raw) ?? new();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Couldn't read the Off keys ({scopeKey}).", ex);
            return new();
        }
    }

    public static void Save(string scopeKey, Dictionary<int, PluginSettingStore?> off)
    {
        if (off.Count == 0) { if (!string.IsNullOrEmpty(AppSettings.Get(scopeKey))) AppSettings.Remove(scopeKey); return; }
        AppSettings.Set(scopeKey, JsonConvert.SerializeObject(off));
    }

    /// <summary>A new application starts with All's keys; an Off key stays Off there, with the same kept binding.</summary>
    public static void CopyScope(string fromScopeKey, string toScopeKey)
    {
        var raw = AppSettings.Get(fromScopeKey);
        if (string.IsNullOrWhiteSpace(raw)) return;
        AppSettings.Set(toScopeKey, raw);
    }

    public static bool IsOn(Dictionary<int, PluginSettingStore?> off, int button) => !off.ContainsKey(button);

    /// <summary>Turn one key Off: its binding moves from <paramref name="aux"/> (the table the driver gets) to the
    /// kept set, and the driver's table is left without a binding for it. Returns true when something changed.</summary>
    public static bool TurnOff(PluginSettingStoreCollection aux, Dictionary<int, PluginSettingStore?> off, int button)
    {
        if (button < 1 || button > aux.Count || off.ContainsKey(button)) return false;
        off[button] = aux[button - 1];
        aux[button - 1] = null!;
        return true;
    }

    /// <summary>Turn one key On again: its kept binding goes back into <paramref name="aux"/>.</summary>
    public static bool TurnOn(PluginSettingStoreCollection aux, Dictionary<int, PluginSettingStore?> off, int button)
    {
        if (button < 1 || button > aux.Count || !off.TryGetValue(button, out var kept)) return false;
        aux[button - 1] = kept!;
        off.Remove(button);
        return true;
    }

    /// <summary>Set a key's binding. An Off key only changes what is kept (the driver still gets nothing for it).</summary>
    public static void SetBinding(PluginSettingStoreCollection aux, Dictionary<int, PluginSettingStore?> off, int button,
        PluginSettingStore? binding)
    {
        if (button < 1 || button > aux.Count) return;
        if (off.ContainsKey(button)) off[button] = binding;
        else aux[button - 1] = binding!;
    }

    /// <summary>The binding to show for a key: the kept one when it is Off, else the table's.</summary>
    public static PluginSettingStore? Shown(PluginSettingStoreCollection aux, Dictionary<int, PluginSettingStore?> off, int button) =>
        off.TryGetValue(button, out var kept) ? kept : (button >= 1 && button <= aux.Count ? aux[button - 1] : null);

    /// <summary>"Default": every key's action is cleared, Off keys included (they stay Off).</summary>
    public static void ClearAll(PluginSettingStoreCollection aux, Dictionary<int, PluginSettingStore?> off)
    {
        for (int i = 0; i < aux.Count; i++) aux[i] = null!;
        foreach (var k in new List<int>(off.Keys)) off[k] = null;
    }

    /// <summary>What the driver gets for a table: nothing at all while All buttons is Off, else the table
    /// (whose Off keys already hold nothing). Applied to a copy, for tables that are not All's own profile.</summary>
    public static void ApplyAllButtons(PluginSettingStoreCollection aux, bool allButtonsOn)
    {
        if (allButtonsOn) return;
        for (int i = 0; i < aux.Count; i++) aux[i] = null!;
    }
}
