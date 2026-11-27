using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Version 31 (Digital Groceries' test screenshot, 27 Nov 2026): on the keyboard of the Change… window the
/// names PrtSc, ScrLk, Pause, Home, PgUp and PgDn were cut off ("PrtS", "Hom", "Paus"). No key name may be wider
/// than its key.</summary>
public class KeyNamesFitTests
{
    [AvaloniaFact]
    public void Every_key_name_fits_on_its_key()
    {
        var vm = new BindingEditorViewModel(AuxBinding.Unbound, "Button 1");
        var dialog = new BindingEditorDialog(vm);
        dialog.Show();
        for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); }

        var keys = dialog.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is KeyCap { IsSpacer: false }).ToList();
        Assert.True(keys.Count > 80, $"only {keys.Count} keys found");
        foreach (var name in new[] { "PrtSc", "ScrLk", "Pause", "Home", "PgUp", "PgDn", "Num" })
            Assert.Contains(keys, b => (b.DataContext as KeyCap)!.Display == name);
        var cut = keys.Where(b =>
        {
            var text = b.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
            if (text == null) return false;
            text.Measure(Size.Infinity);
            return text.DesiredSize.Width > b.Bounds.Width - 3;      // 1.5 pt outline each side
        }).Select(b => (b.DataContext as KeyCap)!.Display + $" (font {b.FontSize}, text {b.GetVisualDescendants().OfType<TextBlock>().First().DesiredSize.Width:0.0}, key {b.Bounds.Width:0.0})").ToList();
        Assert.True(cut.Count == 0, "cut off: " + string.Join(", ", cut));
        dialog.Close();
    }
}
