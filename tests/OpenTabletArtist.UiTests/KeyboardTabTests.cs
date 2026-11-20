using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V29 #177 (Zak 20 Nov 18:52: "Should we move the Floating keyboard menu to its own tab / button in between the
/// Eraser and Mapping buttons so all 4 tabs have menu of somewhat the same size?" ... "Do it"): the floating keyboard's
/// settings live on their own Keyboard tab, and the Pen tab keeps a link to it under pen button 1.</summary>
public class KeyboardTabTests
{
    private static (TabletDetailViewModel, TabletDetailView, Window) Open(string tab)
    {
        _ = new ThemeViewModel();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings);
        vm.PenButton1Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, null, false, (_, _, _) => Task.CompletedTask);
        vm.PenButton2Row = new PenSwitchRowViewModel(PenSwitchKind.PenButton, 2, null, false, (_, _, _) => Task.CompletedTask);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1100 }; w.Show();
        view.FindControl<RadioButton>(tab)!.IsChecked = true;
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        return (vm, view, w);
    }

    // Logical parents: a hidden ScrollViewer may never build its template, so its content has no visual parents to check.
    private static bool ShownOnScreen(Control c) => c.IsVisible && c.GetLogicalAncestors().OfType<Control>().All(a => a.IsVisible);

    [AvaloniaFact]
    public void KeyboardTab_SitsBetweenEraserAndMapping()
    {
        var (_, view, w) = Open("PenTab");
        try
        {
            var keyboard = view.FindControl<RadioButton>("KeyboardTab")!;
            var row = (Panel)keyboard.Parent!;
            // Mapping is hidden in this test (no tablet area), so check the order of the four pen tabs as written.
            var pens = new[] { "PenTab", "EraserTab", "KeyboardTab", "MappingTab" };
            var order = row.Children.OfType<RadioButton>().Select(r => r.Name).Where(n => pens.Contains(n)).ToList();
            Assert.Equal(new[] { "PenTab", "EraserTab", "KeyboardTab", "MappingTab" }, order);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void FloatingKeyboardSettings_AreOnTheKeyboardTab_NotOnThePenTab()
    {
        var (_, view, w) = Open("PenTab");
        try
        {
            var box = view.FindControl<Border>("FloatingKeyboardBox")!;
            Assert.False(ShownOnScreen(box));
            view.FindControl<RadioButton>("KeyboardTab")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
            Assert.True(ShownOnScreen(box));
            Assert.True(ShownOnScreen(view.FindControl<Slider>("KeyboardTransparencySlider")!));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void KeyboardSettingsLink_UnderPenButton1_OpensTheKeyboardTab()
    {
        var (_, view, w) = Open("PenTab");
        try
        {
            var link = view.FindControl<Button>("KeyboardSettingsLink")!;
            Assert.True(ShownOnScreen(link));
            link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
            Assert.True(view.FindControl<RadioButton>("KeyboardTab")!.IsChecked);
            Assert.True(ShownOnScreen(view.FindControl<Border>("FloatingKeyboardBox")!));
        }
        finally { w.Close(); }
    }

    /// <summary>Grok 18:54 point 3: a "Show or hide the keyboard" slice in the menu editor points to the Keyboard tab.</summary>
    [AvaloniaFact]
    public void MenuEditor_KeyboardSlice_PointsToTheKeyboardTab()
    {
        _ = new ThemeViewModel();
        var vm = new RadialMenuEditorViewModel(null, "Pen button 1");
        vm.SelectSlice(3);
        var window = new RadialMenuEditorDialog(vm);
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        try
        {
            var note = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "KeyboardTabNote");
            vm.ActionIndex = RadialMenuEditorViewModel.ActKeys;
            Dispatcher.UIThread.RunJobs();
            Assert.False(note.IsVisible);
            vm.ActionIndex = RadialMenuEditorViewModel.ActKeyboard;
            Dispatcher.UIThread.RunJobs();
            Assert.True(note.IsVisible);
            Assert.Contains("Keyboard tab", note.Text);
        }
        finally { window.Close(); }
    }

    /// <summary>Zak 20 Nov ~20:05 ("if 'Keyboard settings...' is clickable it needs to be a button") and ~20:07 (the tick
    /// next to "Default setting" looks like a checkbox): a real button, and no tick on any pen card.</summary>
    [AvaloniaFact]
    public void KeyboardSettings_IsARealButton_AndNoPenCardHasATick()
    {
        var (_, view, w) = Open("PenTab");
        try
        {
            var link = view.FindControl<Button>("KeyboardSettingsLink")!;
            Assert.True(link.Width >= 200 && link.FontSize >= 17);
            var cardXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "OpenTabletArtist/Views/PenSwitchCard.axaml"));
            Assert.DoesNotContain("IconCheckCircle", cardXaml);
        }
        finally { w.Close(); }
    }

    /// <summary>Zak 20 Nov ~20:10: the radial menu editor says "Radial menu" once (its window title), not three times.</summary>
    [AvaloniaFact]
    public void MenuEditor_SaysRadialMenuOnce()
    {
        _ = new ThemeViewModel();
        var vm = new RadialMenuEditorViewModel(null, "Radial menu");
        var window = new RadialMenuEditorDialog(vm);
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        try
        {
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            Assert.DoesNotContain(texts, t => t.Contains("Radial menu", System.StringComparison.OrdinalIgnoreCase));
        }
        finally { window.Close(); }
    }

    private static string RepoRoot()
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "AGENTS.md"))) d = d.Parent;
        return d!.FullName;
    }
}
