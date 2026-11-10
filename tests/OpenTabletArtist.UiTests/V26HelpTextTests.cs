using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Zak 9 Nov: About ExpressKeys and the Touch ring notes in plain English; About ExpressKeys uses the
/// empty right half of the page instead of a squeezed 300 px column.</summary>
public class V26HelpTextTests
{
    private static (TabletDetailView, Window) OpenKeys(double width)
    {
        _ = new ThemeViewModel();
        var profile = TestPens.Arm(new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) });
        profile.BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in new[] { "B", "E", "Space", "Control" }) profile.BindingSettings.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding(k));
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings);
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = width, Height = 900 }; w.Show();
        view.FindControl<RadioButton>("ButtonsTab")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
        return (view, w);
    }

    [AvaloniaFact]
    public void AboutExpressKeys_SaysItPlainly()
    {
        var (view, w) = OpenKeys(1052);
        var about = view.FindControl<Control>("AboutExpressKeys")!;
        Assert.True(about.IsEffectivelyVisible);
        var texts = about.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("ExpressKeys are the buttons on your tablet (not the ones on your pen).", texts);
        Assert.Contains("Click ••• next to a key to choose what it does: a keyboard shortcut, a mouse click or scrolling.", texts);
        Assert.Contains("Press a key on your tablet to see which row it is.", texts);
        Assert.Contains("Use the All buttons switch to stop every key working, for example if your hand keeps pressing one by accident. Your settings are kept, and the keys work again when you turn it back On.", texts);
        Assert.Contains("To turn off just one key, click ••• next to it and choose Turn off. A key works only when All buttons and the key are both On.", texts);
        Assert.Contains("Your key settings are saved for this tablet. Each application can have its own: pick it in the Application row above.", texts);
        w.Close();
    }

    [AvaloniaFact]
    public void AboutExpressKeys_SitsBesideTheList_AndUsesTheRoom()
    {
        var (view, w) = OpenKeys(1052);   // the default 1100 px window less the main window's side padding
        var list = view.FindControl<Control>("ExpressKeysList")!;
        var about = view.FindControl<Control>("AboutExpressKeys")!;
        var lp = list.TranslatePoint(new Point(0, 0), w)!.Value;
        var ap = about.TranslatePoint(new Point(0, 0), w)!.Value;
        Assert.True(ap.X >= lp.X + list.Bounds.Width, $"About at x={ap.X}, list ends at {lp.X + list.Bounds.Width}");
        Assert.True(System.Math.Abs(ap.Y - lp.Y) < 2, "About starts level with the list");
        Assert.True(about.Bounds.Width > 330, $"About is {about.Bounds.Width} wide");
        Assert.True(about.Bounds.Width <= 520.5, "line length stays readable");
        w.Close();
    }

    [AvaloniaFact]
    public void AboutExpressKeys_GoesUnderTheList_WhenTheWindowIsNarrow()
    {
        var (view, w) = OpenKeys(800);
        var list = view.FindControl<Control>("ExpressKeysList")!;
        var about = view.FindControl<Control>("AboutExpressKeys")!;
        var lp = list.TranslatePoint(new Point(0, 0), w)!.Value;
        var ap = about.TranslatePoint(new Point(0, 0), w)!.Value;
        Assert.True(ap.Y >= lp.Y + list.Bounds.Height, "About under the list");
        Assert.True(System.Math.Abs(ap.X - lp.X) < 2, "aligned with the list");
        Assert.True(about.Bounds.Width > 400, $"About is {about.Bounds.Width} wide");
        // and back beside it when the window grows again
        w.Width = 1300; Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
        ap = about.TranslatePoint(new Point(0, 0), w)!.Value;
        lp = list.TranslatePoint(new Point(0, 0), w)!.Value;
        Assert.True(ap.X >= lp.X + list.Bounds.Width);
        w.Close();
    }

    private static List<RingModeRow> SmoothRows()
    {
        var d = RingModeBinding.Defaults;
        var modes = new List<RingMode> { d[0] with { RotateSensitivity = 50 }, d[1] with { ScrollAmount = 50 }, d[2] with { SizeSensitivity = 50 }, d[3] with { HardnessSensitivity = 50 } };
        return new RingModesEditor(0, modes, true, true, true, null, null, _ => Task.CompletedTask).Rows.ToList();
    }

    [AvaloniaFact]
    public void TouchRingNotes_ArePlain_WithASeparateSetupLine()
    {
        var rows = SmoothRows();
        Assert.Equal(new[] { RingAction.SmoothRotate, RingAction.SmoothZoom, RingAction.SmoothSize, RingAction.SmoothHardness }, rows.Select(r => r.Action));
        foreach (var r in rows)
        {
            Assert.StartsWith("Turn the ring to ", r.ModeNote);
            Assert.DoesNotContain("Held keys", r.ModeNote);
            Assert.DoesNotContain("Photoshop:", r.ModeNote);
            Assert.True(r.HasModeSetup);
            Assert.StartsWith("Setup: ", r.ModeSetup);
        }
        Assert.Contains("Enable canvas rotation with ⌘+scroll wheel", rows[0].ModeSetup);
        Assert.Contains("Zoom with Scroll Wheel", rows[1].ModeSetup);
        Assert.Contains("Use Graphics Processor", rows[2].ModeSetup);
        Assert.Contains("Vary Round Brush Hardness based on HUD vertical movement", rows[3].ModeSetup);
        Assert.Contains("Photoshop and Affinity", rows[0].ModeNote);
        Assert.Contains("Photoshop only", rows[3].ModeNote);

        var keys = new RingModesEditor(0, RingModeBinding.Defaults, true, true, true, null, null, _ => Task.CompletedTask).Rows[0];
        Assert.Equal("Turning the ring sends the keys below, once every 10°.", keys.ModeNote);
        Assert.False(keys.HasModeSetup);
    }

    [AvaloniaFact]
    public void TouchRingEditor_ShowsTheNoteAndSetupLines()
    {
        var row = SmoothRows()[0];
        var editor = new RingStepEditor { DataContext = row };
        var w = new Window { Content = editor, Width = 700, Height = 300 }; w.Show();
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
        var texts = editor.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        Assert.Contains(row.ModeNote, texts);
        Assert.Contains(row.ModeSetup, texts);
        Assert.DoesNotContain(texts, t => t.StartsWith("Experimental. Photoshop"));
        w.Close();
    }

    static string I18nDir()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "OpenTabletArtist", "Assets", "i18n"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "OpenTabletArtist", "Assets", "i18n");
    }

    [Fact]
    public void EveryLocale_HasTheNewTexts_AndNoneOfTheOldOnes()
    {
        string[] keys =
        {
            "tablet.expresskeys.body", "tablet.expresskeys.choose_hint", "tablet.expresskeys.press_hint", "tablet.expresskeys.enable_note",
            "tablet.expresskeys.per_app", "tablet.expresskeys.key_note", "tablet.ring.about_turns", "tablet.ring.about_dial", "tablet.ring.about_enable", "tablet.ring.about_enable_all",
            "tablet.ring.note.rotate", "tablet.ring.note.zoom", "tablet.ring.note.size", "tablet.ring.note.hardness", "tablet.ring.note.keys",
            "tablet.ring.setup.rotate", "tablet.ring.setup.zoom", "tablet.ring.setup.size", "tablet.ring.setup.hardness",
        };
        var dir = I18nDir();
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(dir, "en.json")))!;
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            var raw = File.ReadAllText(f);
            var t = JsonSerializer.Deserialize<Dictionary<string, string>>(raw)!;
            foreach (var k in keys)
            {
                Assert.True(t.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v), $"{Path.GetFileName(f)} lacks {k}");
                if (!f.EndsWith("en.json")) Assert.NotEqual(en[k], v);
            }
            Assert.Contains("{degrees}", t["tablet.ring.note.keys"]);
            if (f.EndsWith("es.json"))   // the rest of es.json says "tableta" (feminine) and quotes labels with «»
                foreach (var k in keys)
                {
                    Assert.DoesNotMatch(@"\btablet\b", t[k]);
                    Assert.DoesNotContain("Desactiva Activar", t[k]);
                }
            // the old wording is gone (en source phrases and the old Bulgarian one)
            Assert.DoesNotContain("The keys on the tablet itself", raw);
            Assert.DoesNotContain("for when one sits where your hand rests", raw);
            Assert.DoesNotContain("Клавишите на самия таблет", raw);
        }
    }

    [AvaloniaFact]
    public void TouchRingTable_StartsBelowTheAboutText()
    {
        foreach (var width in new[] { 800.0, 1052.0, 1400.0 })
        {
            _ = new ThemeViewModel();
            var profile = TestPens.Arm(new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) });
            var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
            var vm = new TabletDetailViewModel(profile, settings);
            vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
            vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
            ButtonBinding Row(string l) => new(0, AuxBinding.Unbound, isOtherBinding: false, otherLabel: "", canEdit: true, applyBinding: null, label: l);
            var d = RingModeBinding.Defaults;
            var ed = new RingModesEditor(0, d, true, true, true, null, null, _ => Task.CompletedTask);
            vm.Wheels = new List<WheelEditor> { new WheelEditor(0, "Wheel", false, Row("Clockwise"), Row("Anticlockwise"),
                new List<ButtonBinding> { Row("Centre button") }, 30, 30, 5, null) { RingModes = ed } };
            var view = new TabletDetailView { DataContext = vm };
            var w = new Window { Content = view, Width = width, Height = 1200 }; w.Show();
            view.FindControl<RadioButton>("WheelsTab")!.IsChecked = true;
            for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
            var about = view.FindControl<Control>("AboutTouchRing")!;
            var diagram = view.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "RingDiagram" && g.IsEffectivelyVisible);
            var aboutBottom = about.TranslatePoint(new Point(0, about.Bounds.Height), w)!.Value.Y;
            var diagramTop = diagram.TranslatePoint(new Point(0, 0), w)!.Value.Y;
            Assert.True(diagramTop >= aboutBottom, $"{width}: table at {diagramTop}, About ends at {aboutBottom}");
            w.Close();
        }
    }
}
