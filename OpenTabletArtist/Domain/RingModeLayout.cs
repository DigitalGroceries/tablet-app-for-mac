using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>V29 #158: what the Touch Ring's centre button does. <see cref="NextMode"/> (the factory choice) steps through the
/// modes; anything else (<see cref="Binding"/>: Do nothing, a keyboard shortcut, a click, a media key) means nothing can switch
/// modes, so the ring always uses mode 1. <see cref="Other"/> keeps a centre binding this app can't show (set up in
/// OpenTabletDriver) until the user picks something else.</summary>
public sealed record RingCentre(bool NextMode, AuxBinding Binding, PluginSettingStore? Other = null)
{
    public static RingCentre Next { get; } = new(true, AuxBinding.Unbound);
    public static RingCentre Nothing { get; } = new(false, AuxBinding.Unbound);
}

public static partial class RingModeBinding
{
    /// <summary>V29 #158: written as false into both turn bindings when the centre button doesn't switch modes.</summary>
    public const string ModesSwitchProperty = "ModesSwitch";

    public static bool CentreIsNext(WheelBindingSettings? wheel) =>
        wheel != null && wheel.WheelButtons.Count > 0 && wheel.WheelButtons[0]?.Path == NextTypeName;

    /// <summary>What the centre button of <paramref name="wheel"/> does now.</summary>
    public static RingCentre ReadCentre(WheelBindingSettings? wheel)
    {
        if (wheel == null || wheel.WheelButtons.Count == 0 || wheel.WheelButtons[0] is not { } store) return RingCentre.Nothing;
        if (store.Path == NextTypeName) return RingCentre.Next;
        return AuxKeyBinding.ReadBinding(store) is { } binding
            ? new RingCentre(false, binding)
            : new RingCentre(false, AuxBinding.Unbound, store);
    }

    /// <summary>V29 #190 (agent K's PTH-460, 23 Nov 2026: "the centre button does nothing"): a ring nobody ever set up has
    /// nothing on either turn and nothing on its centre button. Such a ring does nothing at all, so the app gives it the
    /// factory modes once (see TabletDetailViewModel.GiveUnsetRingFactoryModesAsync).</summary>
    public static bool IsUnsetRing(WheelBindingSettings? wheel) =>
        wheel != null && wheel.WheelButtons.Count > 0 && wheel.WheelButtons[0] == null
        && wheel.ClockwiseRotation == null && wheel.CounterClockwiseRotation == null;

    /// <summary>V29 #158 (Digital Groceries 20 Nov: one switch, one menu): the ring is shown as its modes, with no plain Clockwise /
    /// Anticlockwise rows: its turns are driven by modes, its centre button switches modes, or its turns do nothing.
    /// A ring whose plain rows hold something modes can't show (set up in OpenTabletDriver) keeps the plain rows.</summary>
    public static bool IsModesLayout(WheelBindingSettings? wheel) =>
        wheel != null && (IsActive(wheel) || CentreIsNext(wheel)
                          || (wheel.ClockwiseRotation == null && wheel.CounterClockwiseRotation == null));

    /// <summary>
    /// V29 #158: write the modes and the centre button. With <see cref="RingCentre.NextMode"/> the centre button steps through
    /// the modes that are on and do something (as <see cref="Apply"/>); with nothing to step through, the ring does nothing.
    /// With any other centre choice only mode 1 is used (the turns are written with ModesSwitch off) and the centre button gets
    /// that choice. Always writes; false only when <paramref name="wheel"/> has no binding slots at all.
    /// </summary>
    public static bool ApplyLayout(WheelBindingSettings wheel, IEnumerable<RingMode> modes, RingCentre centre)
    {
        var list = modes.Take(MaxModes).ToList();
        bool hasButton = wheel.WheelButtons.Count > 0;
        if (centre.NextMode && hasButton)
        {
            if (ActiveModes(list).Count > 0) return Apply(wheel, list);
            wheel.ClockwiseRotation = null;
            wheel.CounterClockwiseRotation = null;
            wheel.WheelButtons[0] = MakeNext(1, list.Take(1).Select(m => m.Name));
            return true;
        }
        var first = list.FirstOrDefault();
        if (first != null && first.Enabled && first.IsMapped) WriteTurns(wheel, new[] { first }, modesSwitch: false);
        else
        {
            wheel.ClockwiseRotation = null;
            wheel.CounterClockwiseRotation = null;
        }
        if (hasButton) wheel.WheelButtons[0] = centre.Other ?? AuxKeyBinding.MakeBinding(centre.Binding)!;
        return true;
    }

    /// <summary>
    /// V29 #158: the full list of four modes for a ring shown as modes (<see cref="IsModesLayout"/>), matching what the profile
    /// does: the saved list where it agrees, with mode 1 (centre button not switching modes) or every mode (switching, but
    /// nothing written) shown as Do nothing (off) when the ring does nothing.
    /// </summary>
    public static List<RingMode> LoadLayout(WheelBindingSettings wheel, string? saved)
    {
        if (CentreIsNext(wheel))
        {
            if (IsActive(wheel)) return Load(wheel, saved, null, null);
            return Pad(Deserialize(saved) ?? Defaults.ToList()).Select(m => m with { Enabled = false }).ToList();
        }
        var list = Pad(Deserialize(saved) ?? Defaults.ToList());
        var live = IsActive(wheel) ? Read(wheel).Select(MigrateSeededRing).Take(1).ToList() : new List<RingMode>();
        if (live.Count == 0) list[0] = list[0] with { Enabled = false };
        else if (!(list[0].Enabled && list[0].IsMapped && SameKeys(new[] { list[0] }, live)))
            list[0] = live[0] with { Name = list[0].Name, Enabled = true };
        return list;
    }

    /// <summary>
    /// V29 #158 ("plain-ring users move into mode 1"): a ring still set up with plain Clockwise / Anticlockwise rows becomes
    /// mode 1, sending the same keys at the same pace (one press per ring step, as the plain rows did); its centre button
    /// keeps what it did, so nothing changes for the user. False when a turn holds something a mode can't send (a click, a
    /// scroll, a binding set up in OpenTabletDriver): that ring keeps its plain rows.
    /// </summary>
    public static bool TryPlainAsMode(WheelBindingSettings wheel, out RingMode mode)
    {
        mode = Defaults[0] with { Enabled = false };
        var cw = wheel.ClockwiseRotation;
        var ccw = wheel.CounterClockwiseRotation;
        AuxCombo? cwKeys = cw == null ? AuxCombo.Unbound : AuxKeyBinding.ReadCombo(cw);
        AuxCombo? ccwKeys = ccw == null ? AuxCombo.Unbound : AuxKeyBinding.ReadCombo(ccw);
        if (cwKeys == null || ccwKeys == null) return false;
        if (!cwKeys.IsBound && !ccwKeys.IsBound) return true;   // nothing on the turns: mode 1 does nothing
        var name = Defaults.FirstOrDefault(d => d.Clockwise == cwKeys && d.CounterClockwise == ccwKeys)?.Name ?? "Mode 1";
        mode = new RingMode(name, cwKeys, ccwKeys, Enabled: true, StepDegrees: 0);
        return true;
    }

    /// <summary>The four modes after moving a plain ring into mode 1: the saved list (or the defaults) with mode 1 replaced.</summary>
    public static List<RingMode> WithModeOne(string? saved, RingMode modeOne)
    {
        var list = Pad(Deserialize(saved) ?? Defaults.ToList());
        list[0] = modeOne;
        return list;
    }
}
