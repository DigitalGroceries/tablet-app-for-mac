using System.Threading.Tasks;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

public partial class TabletDetailViewModel
{
    /// <summary>The radial menu's pencil (TASKS #54): give the radial-menu buttons of the table being edited (All, or the
    /// selected application / pen) that hold <paramref name="oldJson"/> the new menu, and save it at once. False when
    /// no button of this table holds that menu, or it couldn't be applied.</summary>
    internal async Task<bool> ReplaceRadialMenuAsync(string oldJson, string newJson)
    {
        if (!RadialMenuBinding.AnyUses(WorkingBindings(), oldJson)) return false;
        return await ApplyBindingMutationAsync(b => RadialMenuBinding.ReplaceMenu(b, oldJson, newJson));
    }
}
