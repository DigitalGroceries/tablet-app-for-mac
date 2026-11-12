using System;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.Services;

/// <summary>
/// The radial menu's pencil and circular arrow (TASKS #54). The driver asks (<see cref="RadialMenuOverlay.RadialRequest"/>);
/// this does the part only the app can: for "edit", opens the same slice editor as "Edit menu…" on the slice that was
/// tapped (the app comes forward; the menu stays open) and for "restore" (after the driver's Yes / No) puts the 8
/// default slices back. A changed menu is saved at once into every radial-menu button that held the old one, then
/// handed back to the driver so the open menu changes too. Whatever happens, the driver is answered, so the pen is
/// never left waiting.
/// </summary>
public static class RadialLiveEdit
{
    public static async Task HandleAsync(RadialMenuOverlay.RadialRequest request, string menuJson)
    {
        var window = AppMenu.Window;
        var vm = window?.DataContext as MainViewModel;
        var current = RadialMenuJson.Serialize(RadialMenuJson.Parse(menuJson));
        string? result = null;
        bool wentForward = false;
        try
        {
            if (window == null || vm == null)
            {
                AppLog.Warn("Radial menu: the app window isn't ready, so the menu wasn't changed.");
                return;
            }
            if (request.Kind == "restore")
            {
                var defaults = RadialMenuDefaults.Create(Localizer.Te);
                defaults.Size = RadialMenuJson.Parse(current).Size;
                result = RadialMenuJson.Serialize(defaults);
            }
            else
            {
                window.BringToFront("radial menu edit", byUser: true);
                wentForward = true;
                result = await RadialMenuEditorDialog.ShowAsync(window, current, Localizer.Te("radial.title", "Radial menu"),
                    request.Path, request.Slice);
            }
            if (result == null || RadialMenuJson.Serialize(RadialMenuJson.Parse(result)) == current)
            {
                result = null;   // cancelled or nothing changed
                return;
            }
            if (await vm.ReplaceRadialMenuAsync(current, result))
            {
                if (request.Kind == "restore") ActionConfirmation.Show("confirm.radial_restored", "Menu restored");
                else ActionConfirmation.Show("confirm.radial_saved", "Slice saved");
            }
            else
            {
                AppLog.Warn("Radial menu: no button of the tablet page being edited holds this menu, so the change wasn't saved.");
                result = null;
            }
        }
        finally
        {
            if (wentForward) MacDesktop.GiveFocusBack();   // back to the drawing app; the menu is still up
            RadialMenuOverlay.SendCommand(RadialMenuCommand.MenuChanged, menu: result ?? current);
        }
    }
}
