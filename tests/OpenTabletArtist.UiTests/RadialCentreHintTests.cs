using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>v27 (Zak 21:56): clicking a centre icon (X, pencil, circular arrow, hand) on the editor ring or the
/// Pen page picture shows a short line under the picture saying what that button does in the real menu. The
/// click does not select or edit a slice.</summary>
public class RadialCentreHintTests
{
    private static Point QuarterPoint(RadialMenuPie pie, int code)
    {
        var c = new Point(pie.Bounds.Width / 2, pie.Bounds.Height / 2);
        double outer = Math.Max(10, Math.Min(pie.Bounds.Width, pie.Bounds.Height) / 2 - 2);
        double d = outer * RadialMenuGeometry.InnerRatio * 0.45;
        return code switch
        {
            RadialMenuGeometry.Centre => new Point(c.X - d, c.Y - d),
            RadialMenuGeometry.Edit => new Point(c.X + d, c.Y - d),
            RadialMenuGeometry.Restore => new Point(c.X - d, c.Y + d),
            RadialMenuGeometry.Hand => new Point(c.X + d, c.Y + d),
            _ => c,
        };
    }

    private static Point SlicePoint(RadialMenuPie pie, int slice)
    {
        var c = new Point(pie.Bounds.Width / 2, pie.Bounds.Height / 2);
        double outer = Math.Max(10, Math.Min(pie.Bounds.Width, pie.Bounds.Height) / 2 - 2);
        double mid = outer * (RadialMenuGeometry.InnerRatio + 1) / 2;
        double deg = slice * 45.0;
        double rad = deg * Math.PI / 180;
        return new Point(c.X + mid * Math.Sin(rad), c.Y - mid * Math.Cos(rad));
    }

    private static void Settle(Window w)
    {
        for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
    }

    [AvaloniaFact]
    public void Editor_ClickingEachCentreIcon_ShowsTheHintUnderTheRing_Logs_AndDoesNotSelectASlice()
    {
        _ = new ThemeViewModel();
        var vm = new RadialMenuEditorViewModel(null, "Pen button 1");
        vm.SelectSlice(3);
        var window = new RadialMenuEditorDialog(vm);
        window.Show(); Settle(window);
        try
        {
            var pie = window.GetVisualDescendants().OfType<RadialMenuPie>().First();
            var hint = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "CentreHint");
            Assert.False(hint.IsVisible);
            Assert.False(vm.HasCentreHint);

            var cases = new (int Code, string Text, string LogName)[]
            {
                (RadialMenuGeometry.Centre, "Closes the menu.", "X"),
                (RadialMenuGeometry.Edit, "Turns on edit mode: then tap a slice to choose what it does.", "pencil"),
                (RadialMenuGeometry.Restore, "Asks whether to restore the 8 default slices.", "circular arrow"),
                (RadialMenuGeometry.Hand, "Drag to move the menu.", "hand"),
            };
            foreach (var (code, text, logName) in cases)
            {
                var at = QuarterPoint(pie, code);
                Assert.Equal(code, pie.CentreAt(at));
                var lines = new List<string>();
                void H(string l) => lines.Add(l);
                AppLog.LineWritten += H;
                try
                {
                    Assert.True(pie.HandleClick(at));
                    Settle(window);
                }
                finally { AppLog.LineWritten -= H; }
                Assert.Equal(3, vm.SelectedSlice);            // still the slice chosen first; the icon is not a slice
                Assert.Equal(text, vm.CentreHint);
                Assert.True(hint.IsVisible);
                Assert.Equal(text, hint.Text);
                // the line sits under the ring
                var pieBottom = pie.TranslatePoint(new Point(0, pie.Bounds.Height), window)!.Value.Y;
                var hintTop = hint.TranslatePoint(new Point(0, 0), window)!.Value.Y;
                Assert.True(hintTop + 0.5 >= pieBottom, $"hint top {hintTop} is not under the ring bottom {pieBottom}");
                Assert.Contains(lines, l => l.Contains($"the {logName} in the picture was clicked") && l.Contains(text));
            }

            // a slice click still selects that slice
            Assert.True(pie.HandleClick(SlicePoint(pie, 0)));
            Settle(window);
            Assert.Equal(0, vm.SelectedSlice);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Editor_CentreClickDoesNotFireSliceClicked()
    {
        _ = new ThemeViewModel();
        var pie = new RadialMenuPie { Width = 300, Height = 300, Interactive = true };
        var w = new Window { Width = 300, Height = 300, Content = pie };
        w.Show(); Settle(w);
        try
        {
            int? slice = null; int? centre = null;
            pie.SliceClicked += i => slice = i;
            pie.CentreClicked += i => centre = i;
            pie.HandleClick(QuarterPoint(pie, RadialMenuGeometry.Centre));
            Assert.Equal(RadialMenuGeometry.Centre, centre);
            Assert.Null(slice);
        }
        finally { w.Close(); }
    }
}
