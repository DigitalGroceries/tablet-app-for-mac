using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>#22: the artist's own default settings file round-trips through the shared codec.</summary>
public class UserDefaultSettingsTests
{
    private readonly IPresetStore _store = new PresetStore(AppLogBridge.Instance);

    [Fact]
    public void Save_Then_TryLoad_RoundTripsSettings()
    {
        UserDefaultSettings.Delete();
        try
        {
            var settings = new Settings { LockUsableAreaDisplay = true, LockUsableAreaTablet = false };
            UserDefaultSettings.Save(settings, _store);
            Assert.True(UserDefaultSettings.Exists);
            Assert.True(UserDefaultSettings.TryLoad(_store, out var loaded));
            Assert.NotNull(loaded);
            Assert.True(SettingsCodec.Same(settings, loaded!));
            Assert.True(UserDefaultSettings.Matches(settings, _store));
            Assert.False(UserDefaultSettings.Matches(new Settings { LockUsableAreaDisplay = false }, _store));
        }
        finally
        {
            UserDefaultSettings.Delete();
        }
    }

    [Fact]
    public void Matches_IsFalse_WhenNoFile()
    {
        UserDefaultSettings.Delete();
        Assert.False(UserDefaultSettings.Exists);
        Assert.False(UserDefaultSettings.Matches(new Settings(), _store));
    }
}
