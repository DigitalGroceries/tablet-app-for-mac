using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>One Wacom-style Touch Ring mode: a name (shown on screen when the mode changes) and the key
/// combo each physical ring direction sends. A mode that is off is skipped by the ring button.</summary>
public sealed record RingMode(string Name, AuxCombo Clockwise, AuxCombo CounterClockwise, bool Enabled = true,
    float StepDegrees = 0, int ScrollAmount = 0, int RotateSensitivity = 0, int SizeSensitivity = 0,
    int HardnessSensitivity = 0)
{
    /// <summary>Build K (EXPERIMENTAL): when not 0, this mode changes Photoshop's brush size (or hardness) smoothly
    /// with Control+Option+drag — slider 2–18, default 10 = 1 pt of drag per ring degree.</summary>
    public const int MinBrushSensitivity = MinSpeed, MaxBrushSensitivity = MaxSpeed, DefaultBrushSensitivity = DefaultSpeed;
    [JsonIgnore] public bool UsesDragSize => SizeSensitivity != 0;
    [JsonIgnore] public bool UsesDragHardness => HardnessSensitivity != 0;
    [JsonIgnore] public bool UsesDragGesture => UsesDragRotate || UsesDragSize || UsesDragHardness;

    /// <summary>Build J (EXPERIMENTAL): when not 0, this mode rotates Photoshop's canvas smoothly by holding R
    /// and dragging — canvas degrees per 100° of ring (100 = 1:1). Takes precedence over scroll and keys.</summary>
    // Build K: J's slider minimum (10, which the user found perfect) is now the default, in the middle of 2–18.
    public const int MinRotateSensitivity = MinSpeed, MaxRotateSensitivity = MaxSpeed, DefaultRotateSensitivity = DefaultSpeed;

    // ---- Build K: one "Speed" (1–100, higher = always faster) for every mode -------------------------------
    // Smooth rotate / size / hardness store the Speed itself; the plugin gets Speed / 5 (so 50 = J's slider
    // minimum, the setting the user found perfect = canvas 10 % of ring travel; brush 1 pt of drag per ring degree).
    // Smooth zoom stores scroll px = Speed × 0.4 (50 = build I's 20 px). Keys store degrees per press, mapped
    // logarithmically: Speed 100 = 5° (every ring step), 50 ≈ 31°, 1 = 180°.
    public const int MinSpeed = 1, MaxSpeed = 100, DefaultSpeed = 50;
    public const float SpeedToGain = 5f;

    public static float GainOf(int speed) => speed / SpeedToGain;
    public static int SpeedOfGain(double gain) => gain == 0 ? 0
        : Math.Sign(gain) * Math.Clamp((int)Math.Round(Math.Abs(gain) * SpeedToGain), MinSpeed, MaxSpeed);
    // Builds I–16 BETA 1 stored smooth zoom as scroll "pixels" per ring step (Speed × 0.4, 10 px = one wheel line):
    // even Speed 1 sent a line every 10° of ring, far too fast, and Speeds 1–3 all became 1 px (shown as 2).
    // Version 16 BETA 2: ScrollAmount holds the Speed itself (sign = direction) and the zoom sends one wheel line
    // per DegreesOfSpeed(Speed) of ring, the same scale as keys. These two convert saved pixel values.
    public static int LegacyScrollOfSpeed(int speed) => Math.Max(1, (int)Math.Round(Math.Clamp(speed, MinSpeed, MaxSpeed) * 0.4));
    public static int SpeedOfLegacyScroll(int pixels) => pixels == 0 ? 0
        : Math.Sign(pixels) * Math.Clamp((int)Math.Round(Math.Abs(pixels) / 0.4), MinSpeed, MaxSpeed);

    private const double FastDeg = 5, SlowDeg = 180;
    public static float DegreesOfSpeed(double speed)
    {
        var t = (Math.Clamp(speed, MinSpeed, MaxSpeed) - MinSpeed) / (MaxSpeed - MinSpeed);
        return (float)Math.Round(SlowDeg * Math.Pow(FastDeg / SlowDeg, t), 1);
    }
    public static int SpeedOfDegrees(float degrees)
    {
        if (degrees <= FastDeg) return MaxSpeed;   // 0 = every ring step = fastest
        var t = Math.Log(Math.Min(degrees, SlowDeg) / SlowDeg) / Math.Log(FastDeg / SlowDeg);
        return Math.Clamp((int)Math.Round(MinSpeed + t * (MaxSpeed - MinSpeed)), MinSpeed, MaxSpeed);
    }
    [JsonIgnore] public bool UsesDragRotate => RotateSensitivity != 0;

    /// <summary>Build I: when not 0, this mode sends Option+scroll (smooth zoom in Photoshop and Affinity) instead of
    /// keys. Version 16 BETA 2: the value is the zoom's Speed (1–100; one wheel line per DegreesOfSpeed of ring),
    /// clockwise positive. Negative reverses the direction.</summary>
    public const int MaxScrollAmount = MaxSpeed;
    public const int DefaultScrollAmount = DefaultSpeed;
    [JsonIgnore] public bool UsesScroll => ScrollAmount != 0;

    /// <summary>Build H: degrees of ring travel per key press for this mode (0 = every ring step). The ring
    /// itself reports in fixed steps (5° on the PTK-440), so a step is rounded to whole ring steps on average.</summary>
    public const float MaxStepDegrees = 180;   // build I: was 90

    /// <summary>Has at least one direction mapped.</summary>
    [JsonIgnore] public bool IsMapped => UsesDragGesture || UsesScroll || Clockwise.IsBound || CounterClockwise.IsBound;

    /// <summary>Version 16 BETA 2: the Speed of a smooth action (rotate, size, hardness or zoom); 0 for keys.</summary>
    [JsonIgnore] public int SmoothSpeed => Math.Abs(UsesDragRotate ? RotateSensitivity : UsesDragSize ? SizeSensitivity
        : UsesDragHardness ? HardnessSensitivity : ScrollAmount);

    /// <summary>Version 16 BETA 2: degrees of ring per action that the ring plugin gets. Keys: the mode's own step.
    /// Smooth actions: their Speed on the keys scale, used for the zoom's wheel lines, Affinity's rotate lines and
    /// the keys sent when the smooth action can't run (another app in front), so the Speed works everywhere.</summary>
    [JsonIgnore] public float WrittenStepDegrees => SmoothSpeed != 0 ? DegreesOfSpeed(SmoothSpeed) : StepDegrees;
}

/// <summary>
/// Reads and writes Wacom-style Touch Ring modes as OpenTabletDriver bindings from the bundled
/// OpenTabletArtist.Dynamics plugin: the ring button gets "Ring Mode: Next Mode" and each ring direction
/// gets "Ring Mode: Turn" holding one key string per mode. Only modes that are on and mapped are written,
/// in order, so the ring button never lands on an empty mode. The full list (names, modes that are off)
/// is kept by the app beside the profile, see <see cref="Serialize"/>.
/// </summary>
public static class RingModeBinding
{
    // Plugin type names — hardcoded because the app doesn't reference the plugin assembly; the
    // PluginTypeNameGuardTests keep them in step with the plugin.
    public const string NextTypeName = "OpenTabletArtist.Dynamics.RingModeNextBinding";
    public const string TurnTypeName = "OpenTabletArtist.Dynamics.RingModeTurnBinding";
    public const string ModeCountProperty = "ModeCount";
    public const string ModeNamesProperty = "ModeNames";
    public const int MaxModes = 4;
    public const string RingStepProperty = "RingStep";
    /// <summary>The PTK-440 ring reports 72 positions per turn: 5° is the finest step there is.</summary>
    public const float HardwareRingStepDegrees = 5f;

    /// <summary>Version 16 BETA 2: written into both turn bindings. 2 = physical clockwise is OpenTabletDriver's
    /// ClockwiseRotation (OTD fires it for a rising ring position, which is a clockwise turn on Wacom rings; the
    /// Intuos Pro 2017 ring is mirrored by its report parser) and smooth zoom's value is a Speed. Missing = written
    /// by Version 16 BETA 1 or earlier: clockwise was stored in CounterClockwiseRotation (so every mode ran
    /// backwards on the PTK-440) and zoom held scroll pixels. Such bindings are read the old way and rewritten.</summary>
    public const string DirectionVersionProperty = "DirectionVersion";
    public const int CurrentDirectionVersion = 2;

    /// <summary>Wheel-line units of the plugin's Option+scroll (MacScroll.PixelsPerLine): one line per action.</summary>
    public const int ScrollPixelsPerLine = 10;
    public static string ModeStepProperty(int index) => $"Mode{index + 1}Step";

    public static string ModeKeysProperty(int index) => $"Mode{index + 1}Keys";
    public static string ModeScrollProperty(int index) => $"Mode{index + 1}Scroll";
    public static string ModeRotateProperty(int index) => $"Mode{index + 1}Rotate";
    public static string ModeSizeProperty(int index) => $"Mode{index + 1}Size";
    public static string ModeHardnessProperty(int index) => $"Mode{index + 1}Hardness";

    private static int ClampBrush(int v) => v == 0 ? 0
        : Math.Sign(v) * Math.Clamp(Math.Abs(v), RingMode.MinBrushSensitivity, RingMode.MaxBrushSensitivity);
    /// <summary>Build J: the pen gate filter that holds back pen reports during a smooth-rotate gesture.</summary>
    public const string GateTypeName = "OpenTabletArtist.Dynamics.RingGestureGate";

    /// <summary>Wacom-like starting point for Photoshop (build G): Rotate, Zoom, Brush size, Brush hardness.
    /// Rotate uses Photoshop's hidden Option+F13 (clockwise) / Option+F14 (counter-clockwise) view rotation —
    /// the keys Wacom's own driver sends — which needs F13/F14 on the macOS virtual keyboard (OTD patch in G).
    /// When modes are first switched on, the ring's current plain keys are kept: if they already match one of
    /// these modes nothing changes, otherwise they replace the last mode, so nothing the user had is lost.</summary>
    public static IReadOnlyList<RingMode> Defaults { get; } = new List<RingMode>
    {
        new("Rotate", Key("F13", alt: true), Key("F14", alt: true), StepDegrees: 10),
        new("Zoom", Key("Equal", cmd: true), Key("Minus", cmd: true), StepDegrees: 30),
        new("Brush size", Key("RightBracket"), Key("LeftBracket"), StepDegrees: 8),
        // Build K: hardness keys jump 25 % a press, so slow by default (60° of ring per press ≈ Speed 31).
        new("Brush hardness", Key("RightBracket", shift: true), Key("LeftBracket", shift: true), StepDegrees: 60),
    };

    private static AuxCombo Key(string key, bool cmd = false, bool alt = false, bool shift = false) =>
        new(false, shift, alt, key, cmd);

    /// <summary>True when this wheel's rotation is driven by ring modes.</summary>
    public static bool IsActive(WheelBindingSettings? wheel) =>
        wheel != null && (wheel.ClockwiseRotation?.Path == TurnTypeName || wheel.CounterClockwiseRotation?.Path == TurnTypeName);

    /// <summary>The modes that will actually be written (on and mapped), in order.</summary>
    public static IReadOnlyList<RingMode> ActiveModes(IEnumerable<RingMode> modes) =>
        modes.Where(m => m.Enabled && m.IsMapped).Take(MaxModes).ToList();

    /// <summary>True when this wheel's ring-mode bindings were written by Version 16 BETA 1 or earlier (reversed
    /// directions, zoom in pixels): <see cref="Read"/> reads them the old way and the host rewrites them.</summary>
    public static bool IsLegacy(WheelBindingSettings? wheel) =>
        IsActive(wheel) && DirectionVersionOf(wheel!) < CurrentDirectionVersion;

    private static int DirectionVersionOf(WheelBindingSettings wheel)
    {
        foreach (var store in new[] { wheel.ClockwiseRotation, wheel.CounterClockwiseRotation })
        {
            if (store?.Path != TurnTypeName) continue;
            var v = store.Settings?.FirstOrDefault(s => s.Property == DirectionVersionProperty)?.Value;
            if (v != null && int.TryParse(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
                return n;
        }
        return 0;
    }

    /// <summary>
    /// Write <paramref name="modes"/> into the wheel: the ring button (button 0) steps the modes, and each
    /// direction sends the active mode's keys. Physical clockwise is OTD's ClockwiseRotation (Version 16 BETA 2;
    /// see <see cref="DirectionVersionProperty"/>). Returns false (and changes nothing) when the wheel has no
    /// button to switch modes with or no mode is mapped.
    /// </summary>
    public static bool Apply(WheelBindingSettings wheel, IEnumerable<RingMode> modes)
    {
        var active = ActiveModes(modes);
        if (active.Count == 0 || wheel.WheelButtons.Count == 0) return false;

        // Each direction's "ring step" is the wheel's own activation threshold for that OTD direction: how far
        // the ring turns per call the plugin gets. The plugin then acts once per mode step (build H); smooth
        // actions get their Speed as a step (Version 16 BETA 2, RingMode.WrittenStepDegrees).
        float otdCwTick = wheel.ClockwiseActivationThreshold, otdCcwTick = wheel.CounterClockwiseActivationThreshold;
        var steps = active.Select(m => m.WrittenStepDegrees).ToList();
        // Smooth zoom: one wheel line per step; only the sign (direction) travels in the scroll value.
        var scrolls = active.Select(m => Math.Sign(m.ScrollAmount) * ScrollPixelsPerLine).ToList();
        var rotates = active.Select(m => m.UsesDragRotate ? RingMode.GainOf(ClampBrush(m.RotateSensitivity)) : 0f).ToList();
        // Build K: one smooth action per mode — rotate wins, then size, then hardness.
        var sizes = active.Select(m => m.UsesDragRotate ? 0f : RingMode.GainOf(ClampBrush(m.SizeSensitivity))).ToList();
        var hards = active.Select(m => m.UsesDragRotate || m.UsesDragSize ? 0f : RingMode.GainOf(ClampBrush(m.HardnessSensitivity))).ToList();
        wheel.ClockwiseRotation = MakeTurn(active.Select(m => m.Clockwise), steps, otdCwTick, scrolls, rotates, sizes, hards);
        wheel.CounterClockwiseRotation = MakeTurn(active.Select(m => m.CounterClockwise), steps, otdCcwTick,
            scrolls.Select(v => -v).ToList(), rotates.Select(v => -v).ToList(), sizes.Select(v => -v).ToList(),
            hards.Select(v => -v).ToList());
        wheel.WheelButtons[0] = MakeNext(active.Count, active.Select(m => m.Name));
        return true;
    }

    /// <summary>True when <see cref="Apply"/> would write something: the wheel has a ring button and at least
    /// one mode is on and has a key. (Build F: the switch only turns on when this is true.)</summary>
    public static bool CanApply(WheelBindingSettings? wheel, IEnumerable<RingMode> modes) =>
        wheel != null && wheel.WheelButtons.Count > 0 && ActiveModes(modes).Count > 0;

    /// <summary>
    /// Turn ring modes off on <paramref name="wheel"/>. With a backup (the plain bindings saved when modes were
    /// turned on) those are put back. Without one, only the slots that hold a ring-mode binding are cleared;
    /// anything else (e.g. a ring button bound by hand in OpenTabletDriver) is left alone. (Build F.)
    /// </summary>
    public static void Remove(WheelBindingSettings wheel, bool haveBackup,
        PluginSettingStore? clockwise, PluginSettingStore? counterClockwise, PluginSettingStore? button)
    {
        if (haveBackup)
        {
            wheel.ClockwiseRotation = clockwise;
            wheel.CounterClockwiseRotation = counterClockwise;
            if (wheel.WheelButtons.Count > 0) wheel.WheelButtons[0] = button!;
            return;
        }
        if (wheel.ClockwiseRotation?.Path == TurnTypeName) wheel.ClockwiseRotation = null;
        if (wheel.CounterClockwiseRotation?.Path == TurnTypeName) wheel.CounterClockwiseRotation = null;
        if (wheel.WheelButtons.Count > 0 && wheel.WheelButtons[0]?.Path == NextTypeName) wheel.WheelButtons[0] = null!;
    }

    /// <summary>Read the modes back from a wheel written by <see cref="Apply"/> (or set up by hand in
    /// OpenTabletDriver). Names come from the ring button's binding, else "Mode N".</summary>
    public static IReadOnlyList<RingMode> Read(WheelBindingSettings wheel)
    {
        // Version 16 BETA 2: bindings written by older versions kept physical clockwise in OTD's
        // CounterClockwiseRotation and zoom as scroll pixels; read those the way they were written.
        bool legacy = DirectionVersionOf(wheel) < CurrentDirectionVersion;
        bool physicalClockwiseIsOtdCounterClockwise = legacy;
        var otdCw = ReadTurn(wheel.ClockwiseRotation);
        var otdCcw = ReadTurn(wheel.CounterClockwiseRotation);
        // Steps are written identically to both directions; take whichever turn binding is there.
        var steps = ReadSteps(physicalClockwiseIsOtdCounterClockwise ? wheel.CounterClockwiseRotation : wheel.ClockwiseRotation)
                    ?? ReadSteps(physicalClockwiseIsOtdCounterClockwise ? wheel.ClockwiseRotation : wheel.CounterClockwiseRotation);
        var physCwStore = physicalClockwiseIsOtdCounterClockwise ? wheel.CounterClockwiseRotation : wheel.ClockwiseRotation;
        var scroll = ReadScrolls(physCwStore);
        for (int i = 0; i < scroll.Length; i++)
            scroll[i] = scroll[i] == 0 ? 0
                : legacy ? RingMode.SpeedOfLegacyScroll(scroll[i])
                : Math.Sign(scroll[i]) * RingMode.SpeedOfDegrees(steps?[i] ?? RingMode.DegreesOfSpeed(RingMode.DefaultSpeed));
        var rotate = ReadFloats(physCwStore, ModeRotateProperty).Select(RingMode.SpeedOfGain).ToArray();
        var size = ReadFloats(physCwStore, ModeSizeProperty).Select(RingMode.SpeedOfGain).ToArray();
        var hard = ReadFloats(physCwStore, ModeHardnessProperty).Select(RingMode.SpeedOfGain).ToArray();
        var physCw = physicalClockwiseIsOtdCounterClockwise ? otdCcw : otdCw;
        var physCcw = physicalClockwiseIsOtdCounterClockwise ? otdCw : otdCcw;
        int count = MaxModes;
        string[] names = [];
        if (wheel.WheelButtons.Count > 0 && wheel.WheelButtons[0]?.Path == NextTypeName)
        {
            count = ReadModeCount(wheel.WheelButtons[0]);
            names = (wheel.WheelButtons[0].Settings?.FirstOrDefault(s => s.Property == ModeNamesProperty)?.Value?.ToString() ?? "")
                .Split('|', StringSplitOptions.TrimEntries);
        }
        var list = new List<RingMode>();
        for (int i = 0; i < count; i++)
        {
            var name = i < names.Length && names[i].Length > 0 ? names[i] : $"Mode {i + 1}";
            // Bindings written before build H carry no steps: use the matching default's.
            var step = steps?[i] ?? DefaultStepFor(physCw[i], physCcw[i]);
            var mode = new RingMode(name, physCw[i], physCcw[i], StepDegrees: step, ScrollAmount: scroll[i], RotateSensitivity: rotate[i],
                SizeSensitivity: size[i], HardnessSensitivity: hard[i]);
            if (mode.IsMapped) list.Add(mode);
        }
        return list;
    }

    /// <summary>
    /// The full editable list for a wheel: the app's saved list when it still matches the profile,
    /// otherwise what the profile holds, padded to four modes. When modes are off, the defaults with mode 1
    /// seeded from the wheel's current plain directions.
    /// </summary>
    public static List<RingMode> Load(WheelBindingSettings wheel, string? saved,
        AuxCombo? currentClockwise, AuxCombo? currentCounterClockwise)
    {
        var savedModes = Deserialize(saved);
        if (IsActive(wheel))
        {
            // Build K: the saved list is migrated (seeded "Ring" → hardness keys), so compare against the
            // migrated live modes; the host then rewrites the profile once (see SameKeys use there).
            var live = Read(wheel).Select(MigrateSeededRing).ToList();
            if (savedModes != null && SameKeys(ActiveModes(savedModes), live)) return Pad(savedModes);
            return Pad(live);
        }
        if (savedModes != null) return Pad(savedModes);

        var list = Defaults.ToList();
        var cw = currentClockwise ?? AuxCombo.Unbound;
        var ccw = currentCounterClockwise ?? AuxCombo.Unbound;
        // Build H: OTA swaps CW/CCW for this ring internally, so the ring's keys can arrive reversed; a match in
        // either direction (or a single bound direction that one mode already sends) counts — never seed a
        // duplicate of a default mode.
        // Build K: the ring's old plain keys are no longer seeded over mode 4 (G–J turned it into a "Ring" mode
        // sending [ / ] — Brush size again, reversed). The four defaults stand as they are; Mode 4 = Brush hardness.
        _ = cw; _ = ccw;
        return list;
    }

    /// <summary>True when <paramref name="mode"/> already sends the keys <paramref name="cw"/> / <paramref name="ccw"/>,
    /// in either direction. An unbound direction matches anything the mode sends.</summary>
    public static bool SendsTheSame(RingMode mode, AuxCombo cw, AuxCombo ccw)
    {
        bool Same(AuxCombo a, AuxCombo b) => !a.IsBound || a == b;
        return (Same(cw, mode.Clockwise) && Same(ccw, mode.CounterClockwise))
            || (Same(cw, mode.CounterClockwise) && Same(ccw, mode.Clockwise));
    }

    /// <summary>The default step for a mode saved before build H (no step stored): the step of the default mode
    /// sending the same keys (either direction), else 0 = every ring step, as before.</summary>
    public static float DefaultStepFor(AuxCombo cw, AuxCombo ccw) =>
        (cw.IsBound || ccw.IsBound) && Defaults.FirstOrDefault(d => SendsTheSame(d, cw, ccw)) is { } d ? d.StepDegrees : 0;

    /// <summary>True when the profile's ring-mode bindings <paramref name="b"/> (as <see cref="Read"/> gives them)
    /// already carry the step sizes that <paramref name="a"/> would write.</summary>
    public static bool SameSteps(IReadOnlyList<RingMode> a, IReadOnlyList<RingMode> b) =>
        a.Count == b.Count && a.Zip(b).All(p => Math.Abs(p.First.WrittenStepDegrees - p.Second.StepDegrees) < 0.01f);

    public static string Serialize(IEnumerable<RingMode> modes) =>
        JsonConvert.SerializeObject(modes.Select(m => new SavedMode
        {
            Name = m.Name,
            Clockwise = AuxKeyBinding.FormatKeys(m.Clockwise),
            CounterClockwise = AuxKeyBinding.FormatKeys(m.CounterClockwise),
            Enabled = m.Enabled,
            Step = m.StepDegrees,
            // Version 16 BETA 2: the zoom Speed; Scroll keeps the old pixel value for versions before it.
            ScrollSpeed = m.ScrollAmount == 0 ? null : m.ScrollAmount,
            Scroll = m.ScrollAmount == 0 ? null : Math.Sign(m.ScrollAmount) * RingMode.LegacyScrollOfSpeed(Math.Abs(m.ScrollAmount)),
            RotateSpeed = m.RotateSensitivity == 0 ? null : m.RotateSensitivity,
            SizeSpeed = m.SizeSensitivity == 0 ? null : m.SizeSensitivity,
            HardnessSpeed = m.HardnessSensitivity == 0 ? null : m.HardnessSensitivity,
        }).ToList());

    /// <summary>Build K: a mode saved by builds G–J as the seeded "Ring" mode (or renamed to hardness) whose keys
    /// are plain ] / [ in either direction is moved to the Brush hardness keys: Shift+] clockwise, Shift+[
    /// counter-clockwise. Anything else is left alone.</summary>
    public static RingMode MigrateSeededRing(RingMode m)
    {
        var name = m.Name.Trim();
        bool ringish = name.Equals("Ring", StringComparison.OrdinalIgnoreCase)
                       || name.Contains("hardness", StringComparison.OrdinalIgnoreCase);
        var size = Defaults[2];
        if (!ringish || !SendsTheSame(size, m.Clockwise, m.CounterClockwise) || !m.Clockwise.IsBound || !m.CounterClockwise.IsBound)
            return m;
        var hard = Defaults[3];
        return m with
        {
            Name = name.Equals("Ring", StringComparison.OrdinalIgnoreCase) ? hard.Name : m.Name,
            Clockwise = hard.Clockwise,
            CounterClockwise = hard.CounterClockwise,
            StepDegrees = hard.StepDegrees,
        };
    }

    public static List<RingMode>? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var saved = JsonConvert.DeserializeObject<List<SavedMode>>(json);
            return saved?.Select(s =>
            {
                var cw = AuxKeyBinding.ParseKeys(s.Clockwise) ?? AuxCombo.Unbound;
                var ccw = AuxKeyBinding.ParseKeys(s.CounterClockwise) ?? AuxCombo.Unbound;
                // A list saved before build H has no step: use the matching default's (Zoom 30°, …).
                var step = s.Step is { } v ? Math.Clamp(v, 0, RingMode.MaxStepDegrees) : DefaultStepFor(cw, ccw);
                var scroll = s.ScrollSpeed is { } zs ? Math.Sign(zs) * Math.Clamp(Math.Abs(zs), RingMode.MinSpeed, RingMode.MaxSpeed)
                    : RingMode.SpeedOfLegacyScroll(s.Scroll ?? 0);   // saved before Version 16 BETA 2: pixels
                // Build K: J saved "Rotate" as its gain (10 = J's minimum) → Speed = gain × 5 (10 → 50, the middle).
                var rotate = s.RotateSpeed is { } rs ? ClampBrush(rs) : s.Rotate is { } r && r != 0 ? RingMode.SpeedOfGain(r) : 0;
                int size = rotate != 0 ? 0 : ClampBrush(s.SizeSpeed ?? 0);
                int hard = rotate != 0 || size != 0 ? 0 : ClampBrush(s.HardnessSpeed ?? 0);
                return MigrateSeededRing(new RingMode(string.IsNullOrWhiteSpace(s.Name) ? "Mode" : s.Name!, cw, ccw, s.Enabled,
                    step, rotate != 0 || size != 0 || hard != 0 ? 0 : scroll, rotate, size, hard));
            }).Take(MaxModes).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class SavedMode
    {
        public string? Name { get; set; }
        public string? Clockwise { get; set; }
        public string? CounterClockwise { get; set; }
        public bool Enabled { get; set; } = true;
        public float? Step { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public int? Scroll { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public int? ScrollSpeed { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public int? Rotate { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public int? RotateSpeed { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public int? SizeSpeed { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public int? HardnessSpeed { get; set; }
    }

    private static List<RingMode> Pad(List<RingMode> modes)
    {
        var list = modes.Take(MaxModes).ToList();
        for (int i = list.Count; i < MaxModes; i++)
            list.Add(new RingMode($"Mode {i + 1}", AuxCombo.Unbound, AuxCombo.Unbound, Enabled: false));
        return list;
    }

    public static bool SameKeys(IReadOnlyList<RingMode> a, IReadOnlyList<RingMode> b) =>
        a.Count == b.Count && a.Zip(b).All(p =>
            AuxKeyBinding.FormatKeys(p.First.Clockwise) == AuxKeyBinding.FormatKeys(p.Second.Clockwise) &&
            AuxKeyBinding.FormatKeys(p.First.CounterClockwise) == AuxKeyBinding.FormatKeys(p.Second.CounterClockwise) &&
            p.First.ScrollAmount == p.Second.ScrollAmount &&
            p.First.RotateSensitivity == p.Second.RotateSensitivity &&
            p.First.SizeSensitivity == p.Second.SizeSensitivity &&
            p.First.HardnessSensitivity == p.Second.HardnessSensitivity);

    private static PluginSettingStore MakeTurn(IEnumerable<AuxCombo> perMode, IReadOnlyList<float> steps, float ringStep,
        IReadOnlyList<int> scrolls, IReadOnlyList<float> rotates, IReadOnlyList<float> sizes, IReadOnlyList<float> hards)
    {
        var store = NewStore(TurnTypeName);
        int i = 0;
        foreach (var combo in perMode)
            store.Settings.Add(new PluginSetting(ModeKeysProperty(i++), AuxKeyBinding.FormatKeys(combo)));
        for (; i < MaxModes; i++)
            store.Settings.Add(new PluginSetting(ModeKeysProperty(i), ""));
        store.Settings.Add(new PluginSetting(RingStepProperty, ringStep > 0 ? ringStep : HardwareRingStepDegrees));
        for (i = 0; i < MaxModes; i++)
            store.Settings.Add(new PluginSetting(ModeStepProperty(i), i < steps.Count ? Math.Clamp(steps[i], 0f, RingMode.MaxStepDegrees) : 0f));
        for (i = 0; i < MaxModes; i++)
            store.Settings.Add(new PluginSetting(ModeScrollProperty(i), i < scrolls.Count ? scrolls[i] : 0));
        for (i = 0; i < MaxModes; i++)
            store.Settings.Add(new PluginSetting(ModeRotateProperty(i), i < rotates.Count ? rotates[i] : 0f));
        for (i = 0; i < MaxModes; i++)
            store.Settings.Add(new PluginSetting(ModeSizeProperty(i), i < sizes.Count ? sizes[i] : 0f));
        for (i = 0; i < MaxModes; i++)
            store.Settings.Add(new PluginSetting(ModeHardnessProperty(i), i < hards.Count ? hards[i] : 0f));
        store.Settings.Add(new PluginSetting(DirectionVersionProperty, CurrentDirectionVersion));
        return store;
    }

    private static double[] ReadFloats(PluginSettingStore? store, Func<int, string> property)
    {
        var result = new double[MaxModes];
        if (store?.Path != TurnTypeName) return result;
        for (int i = 0; i < MaxModes; i++)
        {
            var v = store.Settings?.FirstOrDefault(s => s.Property == property(i))?.Value;
            result[i] = v != null && double.TryParse(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        }
        return result;
    }

    private static int[] ReadInts(PluginSettingStore? store, Func<int, string> property)
    {
        var result = new int[MaxModes];
        if (store?.Path != TurnTypeName) return result;
        for (int i = 0; i < MaxModes; i++)
        {
            var v = Invariant(store.Settings?.FirstOrDefault(s => s.Property == property(i))?.Value);
            result[i] = v != null && int.TryParse(v, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        }
        return result;
    }

    /// <summary>True when any ring-mode wheel of the profile has a smooth-rotate mode written.</summary>
    public static bool UsesSmoothRotate(Profile profile) => UsesSmoothRotate(profile.BindingSettings);

    public static bool UsesSmoothRotate(BindingSettings? bindings) =>
        bindings?.WheelBindings?.Any(ws => ws != null && IsActive(ws)
            && new Func<int, string>[] { ModeRotateProperty, ModeSizeProperty, ModeHardnessProperty }.Any(prop =>
                ReadFloats(ws.ClockwiseRotation, prop).Any(v => v != 0)
                || ReadFloats(ws.CounterClockwiseRotation, prop).Any(v => v != 0))) == true;

    /// <summary>Build J: the pen gate filter is in the profile (enabled) exactly when a smooth-rotate mode is
    /// written; otherwise our gate store is removed. Never touches other filters. Version 16 BETA 2:
    /// <paramref name="alsoUsedBy"/> are the tablet's per-app tables, which the app puts on the driver while
    /// their app is in front with the profile's filters unchanged, so a smooth mode there needs the gate too.</summary>
    public static void SyncGate(Profile profile, IEnumerable<BindingSettings>? alsoUsedBy = null)
    {
        if (profile.Filters == null) return;
        var existing = profile.Filters.Where(f => f?.Path == GateTypeName).ToList();
        if (UsesSmoothRotate(profile) || alsoUsedBy?.Any(UsesSmoothRotate) == true)
        {
            if (existing.Count == 0) { var gate = NewStore(GateTypeName); gate.Enable = true; profile.Filters.Add(gate); }
            foreach (var f in existing) f.Enable = true;
            foreach (var extra in existing.Skip(1)) profile.Filters.Remove(extra);
        }
        else
        {
            foreach (var f in existing) profile.Filters.Remove(f);
        }
    }

    /// <summary>Per-mode scroll amounts of a turn binding (0 for keys, or for bindings before build I).</summary>
    private static int[] ReadScrolls(PluginSettingStore? store)
    {
        var result = new int[MaxModes];
        if (store?.Path != TurnTypeName) return result;
        for (int i = 0; i < MaxModes; i++)
        {
            var v = Invariant(store.Settings?.FirstOrDefault(s => s.Property == ModeScrollProperty(i))?.Value);
            result[i] = v != null && int.TryParse(v, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        }
        return result;
    }

    /// <summary>Version 16 BETA 2: a setting's number as text in the invariant culture. JValue.ToString() follows
    /// the current culture, so in Bulgarian, German or Russian 30.5 became "30,5" and read back as 0.</summary>
    private static string? Invariant(object? value) =>
        value == null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

    private static float[]? ReadSteps(PluginSettingStore? store)
    {
        if (store?.Path != TurnTypeName) return null;
        if (store.Settings?.Any(s => s.Property == ModeStepProperty(0)) != true) return null;   // pre-H binding
        var result = new float[MaxModes];
        for (int i = 0; i < MaxModes; i++)
        {
            var v = Invariant(store.Settings?.FirstOrDefault(s => s.Property == ModeStepProperty(i))?.Value);
            result[i] = v != null && float.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;
        }
        return result;
    }

    private static PluginSettingStore MakeNext(int count, IEnumerable<string> names)
    {
        var store = NewStore(NextTypeName);
        store.Settings.Add(new PluginSetting(ModeCountProperty, Math.Clamp(count, 1, MaxModes)));
        store.Settings.Add(new PluginSetting(ModeNamesProperty,
            string.Join("|", names.Select(n => n.Replace('|', '/').Trim()))));
        return store;
    }

    private static AuxCombo[] ReadTurn(PluginSettingStore? store)
    {
        var result = Enumerable.Repeat(AuxCombo.Unbound, MaxModes).ToArray();
        if (store?.Path != TurnTypeName) return result;
        for (int i = 0; i < MaxModes; i++)
        {
            var keys = store.Settings?.FirstOrDefault(s => s.Property == ModeKeysProperty(i))?.Value?.ToString();
            result[i] = AuxKeyBinding.ParseKeys(keys) ?? AuxCombo.Unbound;
        }
        return result;
    }

    private static int ReadModeCount(PluginSettingStore store)
    {
        var v = Invariant(store.Settings?.FirstOrDefault(s => s.Property == ModeCountProperty)?.Value);
        return v != null && int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, 1, MaxModes) : MaxModes;
    }

    private static PluginSettingStore NewStore(string path) => AuxKeyBinding.NewStore(path);
}
