using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// TASKS #212: where the step-by-step mapping of a tablet model is kept: the app settings, one entry per model
/// (<c>KeyOrder:{tablet}</c>). Per model, not per tablet: two tablets of one model have their keys in the same places,
/// and running the mapping again replaces the entry (team vote 23 Nov 2026).
/// </summary>
public static class KeyOrderStore
{
    public static string Key(string tablet) => $"KeyOrder:{tablet}";

    /// <summary>The saved mapping of a model, or null when there is none (or it cannot be read).</summary>
    public static KeyOrderMap? Load(string? tablet) =>
        string.IsNullOrEmpty(tablet) ? null : KeyOrderMap.TryParse(AppSettings.Get(Key(tablet)));

    /// <summary>Saves a mapping. One with no key recorded is not a mapping: it removes the entry instead, so a run
    /// the user stopped at once does not look like a finished one.</summary>
    public static void Save(string? tablet, KeyOrderMap map)
    {
        if (string.IsNullOrEmpty(tablet)) return;
        if (!map.HasAny) { Clear(tablet); return; }
        AppSettings.Set(Key(tablet), map.ToJson());
    }

    public static void Clear(string? tablet)
    {
        if (!string.IsNullOrEmpty(tablet)) AppSettings.Remove(Key(tablet));
    }

    public static bool Has(string? tablet) => Load(tablet) is { HasAny: true };
}
