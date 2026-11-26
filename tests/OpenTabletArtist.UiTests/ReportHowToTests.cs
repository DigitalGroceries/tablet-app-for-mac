using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>"How to report" (Digital Groceries, 17 Nov 2026): a short guide at the top of the report window.</summary>
public class ReportHowToTests
{
    static (List<string>, IReadOnlyList<string>, string?) Inputs() => (new List<string>(), Array.Empty<string>(), null);

    [AvaloniaFact]
    public void The_guide_is_hidden_until_its_button_is_clicked_and_lists_six_steps()
    {
        var vm = new ReportViewModel(Inputs, null, "test");
        var mw = new MainWindow { Width = 900, Height = 700 };
        mw.Show();
        Dispatcher.UIThread.RunJobs();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            ReportWindow.Open(vm);
            Dispatcher.UIThread.RunJobs();
            var sheet = mw.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Sheet");
            var button = sheet.GetVisualDescendants().OfType<Button>().First(c => c.Name == "ReportHowTo");
            var steps = sheet.GetVisualDescendants().OfType<StackPanel>().First(c => c.Name == "ReportHowToSteps");
            Assert.Equal("How to report", button.Content);
            Assert.False(steps.IsVisible);

            button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(steps.IsVisible);
            var lines = steps.Children.OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Equal(6, lines.Count);
            Assert.Equal("1. Click Record the problem.", lines[0]);
            Assert.Contains("mouse first, then with the pen", lines[2]);
            Assert.StartsWith("6. Click Send with file", lines[5]);

            button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(steps.IsVisible);
        }
        finally
        {
            vm.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            InWindowSheet.MainOverride = null;
            mw.Close();
        }
    }

    [Fact]
    public void Every_language_has_the_guide()
    {
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var dir = System.IO.Path.Combine(root, "OpenTabletArtist", "Assets", "i18n");
        foreach (var f in System.IO.Directory.GetFiles(dir, "*.json"))
        {
            var d = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(f));
            foreach (var k in new[] { "report.howto.button", "report.howto.1", "report.howto.2", "report.howto.3", "report.howto.4", "report.howto.5", "report.howto.6" })
                Assert.False(string.IsNullOrWhiteSpace(d[k]?.ToString()), $"{System.IO.Path.GetFileName(f)} misses {k}");
        }
    }
}
