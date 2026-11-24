using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.ViewModels;

public partial class TabletDetailViewModel
{
    // ── V30 build 4 (Zak 24 Nov 2026 ~15:44, on the light-look picture of Tablet › Pen): the radial menu and the colour palette
    // "should never be grayed out. Use different way to show that they are not setup. Add a label next to then, a swithch with On on
    // one side and Off on the other. When the user turns the switch on the app should ask them what buttons should open this menu."
    // ~15:48: "Put the On/Off switches in the top right corner of the radial and palette menu", "Or outside next to that corner".
    // On: the page asks which pen button opens the menu (ChooseMenuButton, set by the view) and that button gets the radial menu.
    // The colour palette opens when the radial menu's button is pressed twice (the plugin's double press), so switching the palette
    // on with a button that doesn't open the radial menu gives that button the radial menu too; the dialog says so. Off: the radial
    // menu's buttons go back to what they did before it was switched on here (kept in the app settings), or to their own default;
    // the palette's Off keeps the radial menu and stops the double press opening the palette (the binding's NoPalette).

    /// <summary>The page asks which pen button opens a menu: the title, a line under it and the choices; returns the index of the
    /// chosen one, or null on Cancel. Set by the view; tests set their own.</summary>
    public Func<string, string, IReadOnlyList<string>, Task<int?>>? ChooseMenuButton { get; set; }

    [ObservableProperty] private bool _radialSwitchOn;
    [ObservableProperty] private bool _paletteSwitchOn;
    private bool _menuSwitchQuiet;

    public string RadialSwitchLabel => Localizer.Te("radial_spot.switch", "Radial menu");
    public string PaletteSwitchLabel => Localizer.Te("palette_spot.switch", "Colour palette");
    /// <summary>V30 build 5: the palette picture's tooltip and name (it opens the palette window).</summary>
    public string PaletteSpotTip => Localizer.Te("palette_editor.tip", "Click to choose your own colours");
    public string MenuSwitchOffText => Localizer.Te("tablet.switch.off", "Off");
    public string MenuSwitchOnText => Localizer.Te("tablet.switch.on", "On");

    /// <summary>The switches work when the page can write settings and the pen has a button.</summary>
    public bool CanSwitchMenus => _applyAction != null && (WorkingBindings().PenButtons?.Count ?? 0) > 0;

    partial void OnRadialSwitchOnChanged(bool value)
    {
        if (_menuSwitchQuiet) return;
        _ = value ? RadialOnAsync() : RadialOffAsync();
    }

    partial void OnPaletteSwitchOnChanged(bool value)
    {
        if (_menuSwitchQuiet) return;
        _ = value ? PaletteOnAsync() : PaletteOffAsync();
    }

    /// <summary>Set both switches from the table without asking anything (RefreshRadialSpot calls it).</summary>
    private void SyncMenuSwitches(OpenTabletDriver.Desktop.Profiles.BindingSettings bindings)
    {
        var pens = bindings.PenButtons ?? new PluginSettingStoreCollection();
        _menuSwitchQuiet = true;
        try
        {
            RadialSwitchOn = pens.Any(RadialMenuBinding.Is);
            PaletteSwitchOn = pens.Any(RadialMenuBinding.OpensPalette);
        }
        finally { _menuSwitchQuiet = false; }
        OnPropertyChanged(nameof(CanSwitchMenus));
    }

    private string PreviousKey(int button) => $"MenuSwitchPrevious:{_profile.Tablet}|{SelectedAppBundleId}|{SelectedPenKey}|{button}";

    private IReadOnlyList<string> PenButtonChoices(bool twice)
    {
        int n = WorkingBindings().PenButtons?.Count ?? 0;
        return Enumerable.Range(1, n).Select(i => twice
            ? Localizer.Te("menu_switch.button_twice", "Pen button {number}, pressed twice").Replace("{number}", i.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            : Localizer.Te("menu_switch.button", "Pen button {number}").Replace("{number}", i.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)).ToList();
    }

    private async Task<int?> AskButtonAsync(string title, string body, bool twice)
    {
        var choices = PenButtonChoices(twice);
        if (choices.Count == 0) return null;
        if (ChooseMenuButton == null) { AppLog.Warn("Menu switch: the page can't ask which pen button to use (no dialog), so nothing was changed."); return null; }
        var pick = await ChooseMenuButton(title, body, choices);
        return pick is int i && i >= 0 && i < choices.Count ? i + 1 : null;
    }

    /// <summary>Remember what pen button <paramref name="button"/> did before a menu switch took it, so Off can give it back.</summary>
    private void RememberBefore(int button)
    {
        try
        {
            var store = WorkingBindings().PenButtons?.ElementAtOrDefault(button - 1);
            if (store == null || RadialMenuBinding.Is(store)) return;   // nothing worth keeping, or it already opens the menu
            AppSettings.Set(PreviousKey(button), JsonConvert.SerializeObject(store));
        }
        catch (Exception ex) { AppLog.Warn($"Menu switch: could not remember what pen button {button} did before.", ex); }
    }

    /// <summary>What pen button <paramref name="button"/> goes back to when the radial menu is switched off: what it did before the
    /// switch took it, else its own default.</summary>
    private PluginSettingStore BeforeOrDefault(int button)
    {
        try
        {
            if (AppSettings.Get(PreviousKey(button)) is { Length: > 0 } json
                && JsonConvert.DeserializeObject<PluginSettingStore>(json) is { } kept && !RadialMenuBinding.Is(kept))
                return kept;
        }
        catch (Exception ex) { AppLog.Warn($"Menu switch: what pen button {button} did before could not be read; it gets its default.", ex); }
        return PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton, button);
    }

    private void AfterMenuSwitch()
    {
        RefreshRadialSpot();
        RefreshPenSwitchRows();
    }

    private async Task RadialOnAsync()
    {
        // Let the switch's own change finish first: putting it back (Cancel, read-only page) from inside that change is
        // ignored by the two-way binding, so the switch would show On with nothing on.
        await Task.Yield();
        try
        {
            if (!CanSwitchMenus) { AfterMenuSwitch(); return; }
            var button = await AskButtonAsync(
                Localizer.Te("menu_switch.radial_title", "Which pen button opens the radial menu?"),
                Localizer.Te("menu_switch.radial_body", "Press that button and the radial menu opens where the pen is."), twice: false);
            if (button is not int n) { AppLog.Info("Radial menu switch: cancelled, it stays off."); AfterMenuSwitch(); return; }
            RememberBefore(n);
            AppLog.Info($"Radial menu switch: on, with pen button {n}.");
            await ApplyPenSwitchBindingAsync(PenSwitchKind.PenButton, n, RadialMenuBinding.Make(RadialPreview?.CurrentJson()));
        }
        catch (Exception ex) { AppLog.Warn("Radial menu switch: switching it on failed.", ex); }
        AfterMenuSwitch();
    }

    private async Task RadialOffAsync()
    {
        // Let the switch's own change finish first: putting it back (Cancel, read-only page) from inside that change is
        // ignored by the two-way binding, so the switch would show On with nothing on.
        await Task.Yield();
        try
        {
            if (!CanSwitchMenus) { AfterMenuSwitch(); return; }
            var pens = WorkingBindings().PenButtons;
            var openers = Enumerable.Range(1, pens?.Count ?? 0).Where(i => RadialMenuBinding.Is(pens![i - 1])).ToList();
            foreach (var n in openers)
            {
                await ApplyPenSwitchBindingAsync(PenSwitchKind.PenButton, n, BeforeOrDefault(n));
                AppSettings.Set(PreviousKey(n), "");
            }
            AppLog.Info($"Radial menu switch: off; pen button(s) {string.Join(", ", openers)} went back to what they did before.");
        }
        catch (Exception ex) { AppLog.Warn("Radial menu switch: switching it off failed.", ex); }
        AfterMenuSwitch();
    }

    private async Task PaletteOnAsync()
    {
        // Let the switch's own change finish first: putting it back (Cancel, read-only page) from inside that change is
        // ignored by the two-way binding, so the switch would show On with nothing on.
        await Task.Yield();
        try
        {
            if (!CanSwitchMenus) { AfterMenuSwitch(); return; }
            var button = await AskButtonAsync(
                Localizer.Te("menu_switch.palette_title", "Which pen button opens the colour palette?"),
                Localizer.Te("menu_switch.palette_body", "The colour palette opens when you press that button twice. Pressed once, the same button opens the radial menu."),
                twice: true);
            if (button is not int n) { AppLog.Info("Colour palette switch: cancelled, it stays off."); AfterMenuSwitch(); return; }
            var store = WorkingBindings().PenButtons?.ElementAtOrDefault(n - 1);
            if (RadialMenuBinding.Is(store))
                await ApplyPenSwitchBindingAsync(PenSwitchKind.PenButton, n, RadialMenuBinding.WithPalette(store!, true));
            else
            {
                RememberBefore(n);
                await ApplyPenSwitchBindingAsync(PenSwitchKind.PenButton, n, RadialMenuBinding.Make(RadialPreview?.CurrentJson()));
            }
            AppLog.Info($"Colour palette switch: on, pen button {n} pressed twice.");
        }
        catch (Exception ex) { AppLog.Warn("Colour palette switch: switching it on failed.", ex); }
        AfterMenuSwitch();
    }

    private async Task PaletteOffAsync()
    {
        // Let the switch's own change finish first: putting it back (Cancel, read-only page) from inside that change is
        // ignored by the two-way binding, so the switch would show On with nothing on.
        await Task.Yield();
        try
        {
            if (!CanSwitchMenus) { AfterMenuSwitch(); return; }
            var pens = WorkingBindings().PenButtons;
            for (int i = 1; i <= (pens?.Count ?? 0); i++)
                if (RadialMenuBinding.OpensPalette(pens![i - 1]))
                    await ApplyPenSwitchBindingAsync(PenSwitchKind.PenButton, i, RadialMenuBinding.WithPalette(pens[i - 1]!, false));
            AppLog.Info("Colour palette switch: off (pressing the radial menu's button twice no longer opens it).");
        }
        catch (Exception ex) { AppLog.Warn("Colour palette switch: switching it off failed.", ex); }
        AfterMenuSwitch();
    }
}
