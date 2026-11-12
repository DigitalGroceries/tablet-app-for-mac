using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ProfileFilterMaintenanceTests
{
    // Build a filter store at a given type path the same way the *Profile writers do (the app can't
    // reference the plugin type, so it deserializes an empty store and sets the path).
    private static PluginSettingStore StoreAt(string path)
    {
        var store = JsonConvert.DeserializeObject<PluginSettingStore>("{}")!;
        store.Path = path;
        return store;
    }

    private static Settings WithFilters(params string[] filterPaths)
    {
        var profile = new Profile { OutputMode = new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)), Tablet = "T" };
        foreach (var p in filterPaths)
            profile.Filters.Add(StoreAt(p));
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static string[] Paths(Settings s) =>
        s.Profiles.First().Filters.Select(f => f.Path).ToArray();

    // The reported bug (#…): a pre-rename OtdArtist.* DynamicsFilter lingers beside the current
    // OpenTabletArtist one, so the Filters tab shows "DynamicsFilter" twice. Cleanup drops the orphan.
    [Fact]
    public void RemovesPreRenameOrphan_KeepsCurrent()
    {
        var settings = WithFilters(
            "OtdArtist.Dynamics.DynamicsFilter",          // stale orphan from before the rename
            PressureCurveProfile.FilterTypeName);          // current

        Assert.True(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(new[] { PressureCurveProfile.FilterTypeName }, Paths(settings));
    }

    [Fact]
    public void RemovesAllKnownLegacyNamespaces_AndLegacyClassName()
    {
        var settings = WithFilters(
            "OtdArtist.Dynamics.DynamicsFilter",
            "OtdWindowsHelper.Dynamics.DynamicsFilter",     // real-world earliest name (mixed case)
            "OtdWindowsHelper.Dynamics.CalibrationFilter",  // its calibration sibling
            "OtdArtist.PressureCurve.PressureCurveFilter",  // even-older class name
            PressureCurveProfile.FilterTypeName,
            HoverProfile.FilterTypeName);

        Assert.True(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(
            new[] { PressureCurveProfile.FilterTypeName, HoverProfile.FilterTypeName },
            Paths(settings));
    }

    // The orphan that shipped: an OtdWindowsHelper DynamicsFilter sitting beside the current one,
    // matched case-insensitively (the namespace appears as both "Otd..." and "OTD..." in the wild).
    [Theory]
    [InlineData("OtdWindowsHelper.Dynamics.DynamicsFilter")]
    [InlineData("OTDWindowsHelper.Dynamics.DynamicsFilter")]
    [InlineData("otdwindowshelper.dynamics.DynamicsFilter")]
    public void RemovesEarliestNamespaceOrphan_RegardlessOfCase(string legacyPath)
    {
        var settings = WithFilters(legacyPath, PressureCurveProfile.FilterTypeName);

        Assert.True(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(new[] { PressureCurveProfile.FilterTypeName }, Paths(settings));
    }

    // An exact duplicate of a current store (somehow written twice) collapses to one.
    [Fact]
    public void CollapsesExactDuplicateOfCurrentStore()
    {
        var settings = WithFilters(
            PressureCurveProfile.FilterTypeName,
            PressureCurveProfile.FilterTypeName);

        Assert.True(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(new[] { PressureCurveProfile.FilterTypeName }, Paths(settings));
    }

    // A clean profile is left untouched and reports no change (so we don't needlessly re-save).
    [Fact]
    public void NoChange_WhenOnlyCurrentFilters()
    {
        var settings = WithFilters(
            PressureCurveProfile.FilterTypeName,
            HoverProfile.FilterTypeName,
            CalibrationProfile.FilterTypeName);

        Assert.False(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(3, settings.Profiles.First().Filters.Count);
    }

    // Third-party filters — even one that coincidentally ends in a name we use — are never removed.
    [Fact]
    public void LeavesThirdPartyFiltersAlone()
    {
        var settings = WithFilters(
            "SomeVendor.Cool.DynamicsFilter", // same class name, but not our namespace → keep
            "OpenTabletDriver.Filters.Noise.NoiseReduction",
            PressureCurveProfile.FilterTypeName);

        Assert.False(ProfileFilterMaintenance.CleanLegacyFilters(settings));
        Assert.Equal(3, settings.Profiles.First().Filters.Count);
    }

    [Fact]
    public void HandlesNullSettings()
    {
        Assert.False(ProfileFilterMaintenance.CleanLegacyFilters(null));
    }

    [Theory]
    [InlineData(null, ProfileFilterMaintenance.FilterOrigin.Unknown)]
    [InlineData("", ProfileFilterMaintenance.FilterOrigin.Unknown)]
    [InlineData("OpenTabletArtist.Dynamics.DynamicsFilter", ProfileFilterMaintenance.FilterOrigin.Current)]
    [InlineData("OtdWindowsHelper.Dynamics.DynamicsFilter", ProfileFilterMaintenance.FilterOrigin.Legacy)]
    [InlineData("OtdArtist.PressureCurve.PressureCurveFilter", ProfileFilterMaintenance.FilterOrigin.Legacy)]
    [InlineData("OpenTabletDriver.Filters.Noise.NoiseReduction", ProfileFilterMaintenance.FilterOrigin.Unknown)]
    public void Classify_TagsCurrentLegacyAndThirdParty(string? path, ProfileFilterMaintenance.FilterOrigin expected)
    {
        Assert.Equal(expected, ProfileFilterMaintenance.Classify(path));
    }

    // #465: non-approved (third-party / driver-built-in) filters are disabled — but left in place —
    // while our approved filters stay enabled.
    [Fact]
    public void DisableUnapproved_DisablesThirdPartyFilters_LeavesApprovedEnabled()
    {
        var settings = WithFilters(
            PressureCurveProfile.FilterTypeName,               // approved (Pen Dynamics)
            CalibrationProfile.FilterTypeName,                 // approved
            "OpenTabletDriver.Filters.Noise.NoiseReduction");  // third-party → Unknown
        foreach (var f in settings.Profiles.First().Filters) f.Enable = true;

        var changed = ProfileFilterMaintenance.DisableUnapprovedFilters(settings);

        var filters = settings.Profiles.First().Filters;
        Assert.True(changed);
        Assert.Equal(3, filters.Count); // disabled, not removed
        Assert.True(filters.First(f => f.Path == PressureCurveProfile.FilterTypeName).Enable);
        Assert.True(filters.First(f => f.Path == CalibrationProfile.FilterTypeName).Enable);
        Assert.False(filters.First(f => f.Path == "OpenTabletDriver.Filters.Noise.NoiseReduction").Enable);
    }

    [Fact]
    public void DisableUnapproved_NoChange_WhenOnlyApprovedFiltersEnabled()
    {
        var settings = WithFilters(PressureCurveProfile.FilterTypeName, CalibrationProfile.FilterTypeName);
        foreach (var f in settings.Profiles.First().Filters) f.Enable = true;

        Assert.False(ProfileFilterMaintenance.DisableUnapprovedFilters(settings));
        Assert.All(settings.Profiles.First().Filters, f => Assert.True(f.Enable));
    }

    [Fact]
    public void DisableUnapproved_LeavesAnAlreadyDisabledThirdPartyFilter_Untouched()
    {
        var settings = WithFilters("OpenTabletDriver.Filters.Noise.NoiseReduction");
        settings.Profiles.First().Filters.First().Enable = false; // already off

        Assert.False(ProfileFilterMaintenance.DisableUnapprovedFilters(settings)); // nothing to change
    }

    // ── TASKS #17 (Kert): other plugins' filters the user turned on in the Filters tab ──

    private const string Antichatter = "TabletDriverFilters.Devocub.Antichatter";

    /// <summary>Runs <paramref name="body"/> with UserFilterChoices kept in memory, then puts the real store back.</summary>
    private static void WithChoices(string? initial, Action<Func<string?>> body)
    {
        var (read, write) = (UserFilterChoices.Read, UserFilterChoices.Write);
        string? value = initial;
        UserFilterChoices.Read = () => value;
        UserFilterChoices.Write = v => value = v;
        try { body(() => value); }
        finally { (UserFilterChoices.Read, UserFilterChoices.Write) = (read, write); }
    }

    [Fact]
    public void UserFilterChoices_SetOnAndOff_RoundTrips()
    {
        WithChoices(null, stored =>
        {
            Assert.False(UserFilterChoices.IsAllowed(Antichatter));
            UserFilterChoices.Set(Antichatter, true);
            UserFilterChoices.Set("Other.Plugin.Filter", true);
            Assert.True(UserFilterChoices.IsAllowed(Antichatter));
            Assert.Equal("Other.Plugin.Filter|" + Antichatter, stored());
            UserFilterChoices.Set(Antichatter, false);
            Assert.False(UserFilterChoices.IsAllowed(Antichatter));
            Assert.Equal("Other.Plugin.Filter", stored());
            Assert.False(UserFilterChoices.IsAllowed(null));
            Assert.False(UserFilterChoices.IsAllowed(""));
        });
    }

    [Fact]
    public void DisableUnapproved_KeepsAFilterTheUserTurnedOn_ButStillTurnsOffOthers()
    {
        WithChoices(Antichatter, _ =>
        {
            var settings = WithFilters(PressureCurveProfile.FilterTypeName, Antichatter,
                "OpenTabletDriver.Filters.Noise.NoiseReduction");
            foreach (var f in settings.Profiles.First().Filters) f.Enable = true;

            Assert.True(ProfileFilterMaintenance.DisableUnapprovedFilters(settings));
            var filters = settings.Profiles.First().Filters;
            Assert.True(filters.First(f => f.Path == Antichatter).Enable);
            Assert.False(filters.First(f => f.Path == "OpenTabletDriver.Filters.Noise.NoiseReduction").Enable);
            Assert.True(filters.First(f => f.Path == PressureCurveProfile.FilterTypeName).Enable);
        });
    }

    [Fact]
    public void FilterCard_ThirdParty_HasASwitchThatReportsTheChange()
    {
        bool? got = null;
        var card = new OpenTabletArtist.ViewModels.FilterCardViewModel("Antichatter", Antichatter, enabled: false,
            ProfileFilterMaintenance.Classify(Antichatter), toggle: on => got = on);
        Assert.True(card.IsThirdParty);
        Assert.True(card.CanToggle);
        Assert.False(card.ShowStatus);

        card.Enabled = true;
        Assert.True(card.Enabled);
        Assert.True(got);
        got = null;
        card.Enabled = true;   // same value: nothing to do
        Assert.Null(got);
    }

    [Fact]
    public void FilterCard_OwnFilter_HasNoSwitch_AndIgnoresWrites()
    {
        bool called = false;
        var card = new OpenTabletArtist.ViewModels.FilterCardViewModel("Pen Dynamics", PressureCurveProfile.FilterTypeName,
            enabled: true, ProfileFilterMaintenance.Classify(PressureCurveProfile.FilterTypeName), toggle: _ => called = true);
        Assert.False(card.IsThirdParty);
        Assert.False(card.CanToggle);
        Assert.True(card.ShowStatus);
        card.Enabled = false;
        Assert.True(card.Enabled);
        Assert.False(called);
    }
}
