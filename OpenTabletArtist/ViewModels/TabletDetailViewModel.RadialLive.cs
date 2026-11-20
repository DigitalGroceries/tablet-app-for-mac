using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

public partial class TabletDetailViewModel
{
    /// <summary>The radial menu's pencil (TASKS #54) and the Pen / Eraser page sliders: replace the menu
    /// <paramref name="oldJson"/> with <paramref name="newJson"/> and save it at once. V29 #170: the tablet has ONE menu,
    /// so every table (All, each application's, each pen's) gets it, whichever table is on screen. False when no table
    /// of this tablet holds that menu, or it couldn't be applied.</summary>
    internal async Task<bool> ReplaceRadialMenuAsync(string oldJson, string newJson)
    {
        var tablet = _profile.Tablet ?? "";
        bool here = RadialMenuBinding.AnyUses(WorkingBindings(), oldJson);
        if (!here && !RadialMenuShare.AnyTableUses(tablet, EditorAllBindings(), oldJson)) return false;
        if (!here) return await ShareRadialMenuAsync(newJson, oldJson);   // it updates an open menu too
        var ok = await ApplyBindingMutationAsync(b => RadialMenuBinding.ReplaceMenu(b, oldJson, newJson));   // shares it too
        // V29 #161: a menu open on screen that still shows the old slices gets the new ones at once.
        if (ok) RadialMenuOverlay.ReplaceLive(AuxBinding.RadialMenu(oldJson), AuxBinding.RadialMenu(newJson));
        return ok;
    }
}
