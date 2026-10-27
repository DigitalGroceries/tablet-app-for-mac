using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 16 BETA 2: Zak's ring test (27 Oct 2026). Every ring mode ran backwards on the PTK-440 (the
/// app stored "Clockwise" in OTD's CounterClockwiseRotation), zoom was far too fast and stuck at Speed 2 or more,
/// smooth actions in other apps ignored the Speed, and with an app selected in the Application row no ring-mode
/// change stayed (they were written to All while the page showed the app's table).</summary>
[Collection(DisplayEnumeratorCollection.Name)]
public class RingFixV16Beta2Tests
{
    private static AuxCombo K(string key, bool cmd = false) => new(false, false, false, key, cmd);

    private static WheelBindingSettings Wheel() => new()
    {
        WheelButtons = new PluginSettingStoreCollection(new[] { (PluginSettingStore)null! }),
        ClockwiseActivationThreshold = 5, CounterClockwiseActivationThreshold = 5,
    };

    private static string? Setting(PluginSettingStore? s, string p) => s?.Settings.FirstOrDefault(x => x.Property == p)?.Value?.ToString();

    /// <summary>A wheel as Version 16 BETA 1 wrote it: physical clockwise in OTD's CounterClockwiseRotation, zoom in
    /// pixels, no DirectionVersion.</summary>
    private static WheelBindingSettings LegacyWheel()
    {
        var w = Wheel();
        Assert.True(RingModeBinding.Apply(w, new[]
        {
            new RingMode("Brush size", K("RightBracket"), K("LeftBracket"), StepDegrees: 8),
            new RingMode("Zoom", K("Equal", cmd: true), K("Minus", cmd: true), StepDegrees: 30, ScrollAmount: 12),
        }));
        (w.ClockwiseRotation, w.CounterClockwiseRotation) = (w.CounterClockwiseRotation, w.ClockwiseRotation);
        foreach (var s in new[] { w.ClockwiseRotation!, w.CounterClockwiseRotation! })
        {
            s.Settings.Remove(s.Settings.First(x => x.Property == RingModeBinding.DirectionVersionProperty));
            var zoom = s.Settings.First(x => x.Property == "Mode2Scroll");
            zoom.Value = Math.Sign(Convert.ToInt32(zoom.Value)) * 5;   // BETA 1: Speed 12 = 5 px
            s.Settings.First(x => x.Property == "Mode2Step").Value = 30f;
        }
        return w;
    }

    [Fact]
    public void Clockwise_IsOtdsClockwise_ForKeysAndSmoothActions()
    {
        var w = Wheel();
        RingModeBinding.Apply(w, new[]
        {
            new RingMode("Zoom", K("Equal", cmd: true), K("Minus", cmd: true), ScrollAmount: 50),
            new RingMode("Size", K("RightBracket"), K("LeftBracket"), SizeSensitivity: 50),
        });
        Assert.Equal("Application+Equal", Setting(w.ClockwiseRotation, "Mode1Keys"));   // zoom in = clockwise
        Assert.Equal("10", Setting(w.ClockwiseRotation, "Mode1Scroll"));                 // scroll up = zoom in
        Assert.Equal("RightBracket", Setting(w.ClockwiseRotation, "Mode2Keys"));         // bigger brush = clockwise
        Assert.True(Convert.ToSingle(w.ClockwiseRotation!.Settings.First(s => s.Property == "Mode2Size").Value,
            System.Globalization.CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public void LegacyBindings_AreReadTheOldWay_ThenRewrittenTheRightWayRound()
    {
        var w = LegacyWheel();
        Assert.True(RingModeBinding.IsLegacy(w));
        var read = RingModeBinding.Read(w);
        Assert.Equal("RightBracket", AuxKeyBinding.FormatKeys(read[0].Clockwise));   // the user's list, not reversed
        Assert.Equal(12, read[1].ScrollAmount);                                        // 5 px was Speed 12
        Assert.True(RingModeBinding.Apply(w, read));
        Assert.False(RingModeBinding.IsLegacy(w));
        Assert.Equal("RightBracket", Setting(w.ClockwiseRotation, "Mode1Keys"));
        Assert.Equal(12, RingModeBinding.Read(w)[1].ScrollAmount);
    }

    [Fact]
    public void SavedList_FromBeta1_KeepsItsZoomSpeed_AndOlderVersionsCanStillReadIt()
    {
        var beta1 = "[{\"Name\":\"Zoom\",\"Clockwise\":\"Application+Equal\",\"CounterClockwise\":\"Application+Minus\",\"Enabled\":true,\"Step\":30,\"Scroll\":5}]";
        Assert.Equal(12, RingModeBinding.Deserialize(beta1)![0].ScrollAmount);
        var json = RingModeBinding.Serialize(new[] { new RingMode("Zoom", K("Equal", cmd: true), K("Minus", cmd: true), ScrollAmount: 1) });
        Assert.Contains("\"ScrollSpeed\":1", json);
        Assert.Contains("\"Scroll\":1", json);   // what Version 15 reads (it loses only the finer speed)
        Assert.Equal(1, RingModeBinding.Deserialize(json)![0].ScrollAmount);   // BETA 1 showed Speed 1 as 2
    }

    [Theory]
    [InlineData(1, 180f)]
    [InlineData(12, 120.9f)]
    [InlineData(50, 30.5f)]
    [InlineData(100, 5f)]
    public void SmoothActions_GetTheirSpeedAsAStep_SoFallbackKeysAndZoomLinesFollowIt(int speed, float degrees)
    {
        foreach (var m in new[]
                 {
                     new RingMode("Z", K("Equal", cmd: true), K("Minus", cmd: true), StepDegrees: 8, ScrollAmount: speed),
                     new RingMode("S", K("RightBracket"), K("LeftBracket"), StepDegrees: 8, SizeSensitivity: speed),
                     new RingMode("R", K("F13"), K("F14"), StepDegrees: 8, RotateSensitivity: speed),
                 })
        {
            var w = Wheel();
            RingModeBinding.Apply(w, new[] { m });
            Assert.Equal(degrees, Convert.ToSingle(w.ClockwiseRotation!.Settings.First(s => s.Property == "Mode1Step").Value,
                System.Globalization.CultureInfo.InvariantCulture), 1);
            Assert.True(RingModeBinding.SameSteps(new[] { m }, RingModeBinding.Read(w)));   // no rewrite loop
            Assert.True(RingModeBinding.SameKeys(new[] { m }, RingModeBinding.Read(w)));
        }
    }

    [Theory]
    [InlineData("bg-BG")]
    [InlineData("de-DE")]
    [InlineData("ru-RU")]
    public void CommaDecimalLanguages_ReadStepsAndZoomSpeedBack(string culture)
    {
        var was = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            var w = Wheel();
            var modes = new[]
            {
                new RingMode("Z", K("Equal", cmd: true), K("Minus", cmd: true), ScrollAmount: 50),
                new RingMode("B", K("RightBracket"), K("LeftBracket"), StepDegrees: 7.5f),
            };
            RingModeBinding.Apply(w, modes);
            var back = RingModeBinding.Read(w);
            Assert.Equal(50, back[0].ScrollAmount);
            Assert.Equal(7.5f, back[1].StepDegrees);
            Assert.True(RingModeBinding.SameKeys(modes, back));
            Assert.True(RingModeBinding.SameSteps(modes, back));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = was; }
    }

    [Fact]
    public void KeysModes_KeepTheirOwnStep()
    {
        var w = Wheel();
        RingModeBinding.Apply(w, new[] { new RingMode("B", K("RightBracket"), K("LeftBracket"), StepDegrees: 8) });
        Assert.Equal("8", Setting(w.ClockwiseRotation, "Mode1Step"));
    }

    [Fact]
    public void PenGate_IsAdded_WhenOnlyAnAppsTableHasASmoothMode()
    {
        var p = new Profile { Tablet = "x", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        var app = new BindingSettings();
        var w = Wheel();
        RingModeBinding.Apply(w, new[] { new RingMode("R", AuxCombo.Unbound, AuxCombo.Unbound, RotateSensitivity: 50) });
        app.WheelBindings.Add(w);
        RingModeBinding.SyncGate(p);
        Assert.DoesNotContain(p.Filters, f => f?.Path == RingModeBinding.GateTypeName);
        RingModeBinding.SyncGate(p, new[] { app });
        Assert.Contains(p.Filters, f => f?.Path == RingModeBinding.GateTypeName && f.Enable);
    }

    // ---- the Application row: ring-mode changes go to the selected app's table ----

    private static (TabletDetailViewModel Vm, Settings Settings, string Tablet) PageWithApp(out string bundle)
    {
        var tablet = "TestTablet-RingV16b2-" + Guid.NewGuid().ToString("N")[..8];
        bundle = "com.example.Paint";
        var w = Wheel();
        RingModeBinding.Apply(w, RingModeBinding.Defaults);
        var profile = new Profile { Tablet = tablet, OutputMode = null! };
        profile.BindingSettings.WheelBindings.Add(w);
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        PerAppBindingStore.Add(tablet, bundle, "Paint", "", profile.BindingSettings);
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        return (vm, settings, tablet);
    }

    [Fact]
    public async Task WithAnAppSelected_TurningAModeOff_Stays_AndLandsInThatAppsTable()
    {
        var (vm, settings, tablet) = PageWithApp(out var bundle);
        vm.SelectAppProfileCommand.Execute(bundle);
        var row = vm.Wheels[0].RingModes!.Rows[1];   // Zoom
        Assert.True(row.Enabled);
        row.Enabled = false;
        await Task.Delay(50);

        Assert.False(vm.Wheels[0].RingModes!.Rows[1].Enabled);   // the rebuilt page keeps it (it used to snap back)
        var appWheel = PerAppBindingStore.Find(tablet, bundle)!.Bindings.WheelBindings[0];
        Assert.Equal("3", Setting(appWheel.WheelButtons[0], RingModeBinding.ModeCountProperty));
        var allWheel = settings.Profiles[0].BindingSettings.WheelBindings[0];
        Assert.Equal("4", Setting(allWheel.WheelButtons[0], RingModeBinding.ModeCountProperty));   // All untouched
    }

    [Fact]
    public async Task WithAnAppSelected_ChangingTheFunctionAndSpeed_Stays()
    {
        var (vm, _, tablet) = PageWithApp(out var bundle);
        vm.SelectAppProfileCommand.Execute(bundle);
        var row = vm.Wheels[0].RingModes!.Rows[1];
        row.ActionIndex = (int)RingAction.SmoothZoom;
        await Task.Delay(50);
        row = vm.Wheels[0].RingModes!.Rows[1];
        Assert.Equal(RingAction.SmoothZoom, row.Action);
        row.CommitSpeed(1);
        await Task.Delay(50);
        row = vm.Wheels[0].RingModes!.Rows[1];
        Assert.Equal(1, row.Speed);   // BETA 1: Zoom could not go below 2
        var appWheel = PerAppBindingStore.Find(tablet, bundle)!.Bindings.WheelBindings[0];
        Assert.Equal(1, RingModeBinding.Read(appWheel)[1].ScrollAmount);
    }

    [Fact]
    public void LegacyAppTables_AreRewritten_WhenTheAppsAreLoaded()
    {
        var tablet = "TestTablet-RingV16b2-" + Guid.NewGuid().ToString("N")[..8];
        var all = new BindingSettings();
        all.WheelBindings.Add(LegacyWheel());
        PerAppBindingStore.Add(tablet, "com.example.Old", "Old", "", all);
        var profile = new Profile { Tablet = tablet, OutputMode = null! };
        profile.BindingSettings.WheelBindings.Add(Wheel());
        var settings = new Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        vm.LoadAppProfiles();
        var w = PerAppBindingStore.Find(tablet, "com.example.Old")!.Bindings.WheelBindings[0];
        Assert.False(RingModeBinding.IsLegacy(w));
        Assert.Equal("RightBracket", Setting(w.ClockwiseRotation, "Mode1Keys"));
    }
}
