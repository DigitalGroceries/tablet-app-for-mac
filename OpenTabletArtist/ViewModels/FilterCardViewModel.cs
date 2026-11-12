using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

// Moved out of TabletDetailViewModel (#751). Nothing changed but the file it lives in: these
// are top-level types that never touched the editor's fields, so the move is mechanical and
// the editor is that much smaller to read.
//
// FilterCardViewModel, one of the tablet editor's row/card types.

/// <summary>A single filter on a tablet's profile, shown as a card in the Filters tab.</summary>
public sealed class FilterCardViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private readonly Action<bool>? _toggle;
    private bool _enabled;

    public FilterCardViewModel(string title, string fullPath, bool enabled,
        ProfileFilterMaintenance.FilterOrigin origin, Action<bool>? toggle = null)
    {
        Title = title;
        FullPath = fullPath;
        _enabled = enabled;
        IsLegacy = origin == ProfileFilterMaintenance.FilterOrigin.Legacy;
        // TASKS #17: filters from other plugins get an Off / On switch; this app's own filters are set on
        // their own pages (Pen, Touch Ring, …) and only show their state here.
        IsThirdParty = origin == ProfileFilterMaintenance.FilterOrigin.Unknown;
        _toggle = IsThirdParty ? toggle : null;
    }

    /// <summary>Friendly label (e.g. "Pen Dynamics") or the raw type name for unknown filters.</summary>
    public string Title { get; }
    /// <summary>The filter's full type path (with namespace) — the subtitle. Showing the namespace is
    /// what makes a stale duplicate (old vs current namespace) visibly distinct rather than identical.</summary>
    public string FullPath { get; }
    public string StatusText => Enabled ? "Enabled" : "Disabled";
    /// <summary>True for a filter left over from an older app/plugin name — inert (the driver has no
    /// plugin for it) and normally cleaned on load, but flagged so a stray one stands out.</summary>
    public bool IsLegacy { get; }
    /// <summary>A filter from another OpenTabletDriver plugin (not this app's own).</summary>
    public bool IsThirdParty { get; }
    /// <summary>Shows the Off / On switch instead of the plain status.</summary>
    public bool CanToggle => _toggle != null;
    public bool ShowStatus => !CanToggle;
    public string OffText => Localizer.Te("tablet.switch.off", "Off");
    public string OnText => Localizer.Te("tablet.switch.on", "On");

    /// <summary>On / Off. For another plugin's filter, flipping it turns the filter on or off for real.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value || _toggle == null) return;
            _enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            _toggle(value);
        }
    }
}
