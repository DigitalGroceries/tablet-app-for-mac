using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Output;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #57 (Zak 17:27): the "Smoother fast curves" switch would not stay on. Cause: its filter
/// (OpenTabletArtist.Dynamics.InBetweenFilter) was missing from <see cref="ProfileFilterMaintenance"/>'s list of this
/// app's own filters, so the "only approved filters run" rule (#465, run on every send to the app's own daemon)
/// switched it off again as a foreign plugin filter, and the profile came back with Enable=false.</summary>
[Collection(DisplayEnumeratorCollection.Name)]
public class SmootherCurvesStaysOnTests
{
    private static Settings WithTablet()
    {
        var p = new Profile
        {
            Tablet = "Wacom PTK-440",
            OutputMode = new PluginSettingStore(typeof(AbsoluteMode)),
            Filters = new PluginSettingStoreCollection(),
            BindingSettings = new BindingSettings(),
        };
        return new Settings { Profiles = new ProfileCollection { p } };
    }

    [Fact]
    public void InBetweenFilter_IsOneOfOurOwnFilters()
    {
        Assert.Equal(ProfileFilterMaintenance.FilterOrigin.Current, ProfileFilterMaintenance.Classify(InBetweenProfile.FilterTypeName));
    }

    [Fact]
    public void EveryFilterThePluginShips_IsOneOfOurOwnFilters() => CheckPluginFilters();

    private static void CheckPluginFilters([CallerFilePath] string here = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
        var names = Directory.GetFiles(Path.Combine(root, "plugins", "OpenTabletArtist.Dynamics"), "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"public\s+(?:sealed\s+)?class\s+(\w*Filter|RingGestureGate)\b").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.Contains("InBetweenFilter", names);
        foreach (var n in names)
            Assert.Equal(ProfileFilterMaintenance.FilterOrigin.Current,
                ProfileFilterMaintenance.Classify("OpenTabletArtist.Dynamics." + n));
    }

    [Fact]
    public void SwitchOn_ThenTheAppsOwnSendRules_KeepsItOn()
    {
        var settings = WithTablet();
        InBetweenProfile.Write(settings, "Wacom PTK-440", on: true);
        Assert.True(InBetweenProfile.IsOn(settings, "Wacom PTK-440"));

        var sent = SettingsCodec.Clone(settings);                 // what the workspace sends and the page adopts back
        OtaSettingsPolicy.Prepare(sent, ownedDaemon: true);
        Assert.True(InBetweenProfile.IsOn(sent, "Wacom PTK-440"));

        var reloaded = SettingsCodec.Clone(sent);                 // a reload from the driver's file
        Assert.True(InBetweenProfile.IsOn(reloaded, "Wacom PTK-440"));
        Assert.False(ProfileFilterMaintenance.DisableUnapprovedFilters(reloaded));
        Assert.False(ProfileFilterMaintenance.CleanLegacyFilters(reloaded));
    }

    [Fact]
    public void ThePageSwitch_SubmitsOn_AndStaysOnAfterPrepareAndReload()
    {
        var settings = WithTablet();
        Settings? submitted = null;
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings,
            applyAction: s => { submitted = SettingsCodec.Clone(s); return Task.FromResult(SettingsApplyOutcome.Live); });
        vm.SmootherCurves = true;
        Assert.NotNull(submitted);
        Assert.True(InBetweenProfile.IsOn(submitted, "Wacom PTK-440"));
        OtaSettingsPolicy.Prepare(submitted!, ownedDaemon: true);
        Assert.True(InBetweenProfile.IsOn(SettingsCodec.Clone(submitted!), "Wacom PTK-440"));
        Assert.True(vm.SmootherCurves);
    }

    [Fact]
    public void AForeignFilter_IsStillSwitchedOff_AndTheReasonIsLogged()
    {
        var settings = WithTablet();
        var foreign = new PluginSettingStore("Some.Other.Plugin.WobbleFilter", new System.Collections.ObjectModel.ObservableCollection<PluginSetting>()) { Enable = true };
        settings.Profiles[0].Filters.Add(foreign);
        var lines = new System.Collections.Generic.List<string>();
        Action<string> h = l => { lock (lines) lines.Add(l); };
        AppLog.LineWritten += h;
        try { Assert.True(ProfileFilterMaintenance.DisableUnapprovedFilters(settings)); }
        finally { AppLog.LineWritten -= h; }
        Assert.False(foreign.Enable);
        Assert.Contains(lines, l => l.Contains("Filter switched off") && l.Contains("Some.Other.Plugin.WobbleFilter"));
    }

    [Fact]
    public void TheSwitchChange_IsLogged()
    {
        var settings = WithTablet();
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live));
        var lines = new System.Collections.Generic.List<string>();
        Action<string> h = l => { lock (lines) lines.Add(l); };
        AppLog.LineWritten += h;
        try { vm.SmootherCurves = true; }
        finally { AppLog.LineWritten -= h; }
        lock (lines) Assert.Contains(lines, l => l.Contains("Smoother fast curves switched on"));
    }
}
