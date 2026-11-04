using System;
using System.Collections.Generic;
using System.IO;
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

/// <summary>Version 22 BETA 3 (owner's request, 2 Nov 2026): the report sheet that the app window, the pen menu and the app
/// menu all open.</summary>
public class ReportSheetTests
{
    static (List<string>, IReadOnlyList<string>, string?) Inputs() => (new List<string>(), Array.Empty<string>(), null);

    [AvaloniaFact]
    public void Sheet_opens_inside_the_window_with_the_picture_and_cancel_closes_it()
    {
        var mw = new MainWindow { Width = 900, Height = 700 };
        mw.Show();
        Dispatcher.UIThread.RunJobs();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            // The test platform draws nothing (headless drawing), so a real picture can't be checked here;
            // what can be checked is that an empty file is never offered.
            var taken = mw.CaptureReportPicture();
            if (taken != null) Assert.True(new FileInfo(taken).Length > 0);
            var picture = Path.Combine(Directory.CreateTempSubdirectory().FullName, "app-window.png");
            File.WriteAllBytes(picture, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            var vm = new ReportViewModel(Inputs, picture, "test");
            var w = ReportWindow.Open(vm);
            Dispatcher.UIThread.RunJobs();
            Assert.True(InWindowSheet.IsOpen);
            Assert.True(ReportWindow.IsOpen);

            var sheet = mw.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Sheet");
            var texts = sheet.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Report a problem or send a recommendation", texts);
            var named = sheet.GetVisualDescendants().OfType<Control>().Where(c => !string.IsNullOrEmpty(c.Name))
                .Select(c => c.Name).ToHashSet();
            foreach (var n in new[] { "ReportProblem", "ReportRecommendation", "ReportText", "ReportWindowPicture",
                         "ReportAddScreenshots", "ReportCancel", "ReportSendFile", "ReportSend" })
                Assert.Contains(n, named);

            var buttons = sheet.GetVisualDescendants().OfType<Button>().ToDictionary(b => b.Name ?? "", b => b.Content);
            Assert.Equal("Send with file", buttons["ReportSendFile"]);
            Assert.Equal("Send with GitHub account", buttons["ReportSend"]);

            // The tick box and the kind follow the view model.
            var box = sheet.GetVisualDescendants().OfType<CheckBox>().First(c => c.Name == "ReportWindowPicture");
            Assert.True(box.IsChecked);
            box.IsChecked = false;
            Assert.False(vm.IncludeWindowPicture);
            sheet.GetVisualDescendants().OfType<RadioButton>().First(c => c.Name == "ReportRecommendation").IsChecked = true;
            Assert.True(vm.IsRecommendation);
            sheet.GetVisualDescendants().OfType<TextBox>().First(c => c.Name == "ReportText").Text = "hello";
            Assert.Equal("hello", vm.Description);

            vm.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(InWindowSheet.IsOpen);
            Assert.False(ReportWindow.IsOpen);
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Send_with_file_shows_where_the_file_is_and_close_shuts_the_sheet()
    {
        // Version 23 (owner's request, 4 Nov 2026): the sheet no longer closes with no word after saving.
        var mw = new MainWindow { Width = 900, Height = 700 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            var vm = new ReportViewModel(Inputs, null, "test");
            ReportWindow.Open(vm);
            Dispatcher.UIThread.RunJobs();
            var target = Path.Combine(Directory.CreateTempSubdirectory().FullName, "my-report.zip");
            vm.PickSaveFile = _ => System.Threading.Tasks.Task.FromResult<string?>(target);   // stands in for the save panel
            vm.Reveal = _ => { };
            await vm.SendFileCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(File.Exists(target));
            Assert.True(ReportWindow.IsOpen);
            var sheet = mw.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Sheet");
            var saved = sheet.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "ReportSaved");
            Assert.True(saved.IsVisible);
            Assert.False(sheet.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ReportSendFile").IsEffectivelyVisible);
            var text = saved.GetVisualDescendants().OfType<SelectableTextBlock>().First(t => t.Name == "ReportSavedText").Text ?? "";
            Assert.Contains(target, text);
            Assert.Contains("organicmachine@protonmail.com", text);
            Assert.Contains(saved.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Report saved");
            var buttons = saved.GetVisualDescendants().OfType<Button>().ToDictionary(b => b.Name ?? "", b => b.Content);
            Assert.Equal("Show in Finder", buttons["ReportShowFile"]);
            Assert.Equal("Close", buttons["ReportDone"]);

            vm.DoneCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(ReportWindow.IsOpen);
            Assert.False(InWindowSheet.IsOpen);
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }

    [AvaloniaFact]
    public void Without_a_picture_there_is_no_tick_box()
    {
        var mw = new MainWindow { Width = 900, Height = 700 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            var vm = new ReportViewModel(Inputs, null, "test");
            ReportWindow.Open(vm);
            Dispatcher.UIThread.RunJobs();
            var sheet = mw.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Sheet");
            Assert.DoesNotContain(sheet.GetVisualDescendants().OfType<CheckBox>(), c => c.Name == "ReportWindowPicture");
            vm.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); }
    }

    [AvaloniaFact]
    public void Tablet_page_bottom_bar_has_the_report_button()
    {
        var view = new TabletDetailView { DataContext = new TabletDetailViewModel() };
        var w = new Window { Content = view, Width = 1100, Height = 800 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var bar = view.FindControl<StackPanel>("WacomBottomBar")!;
        var report = view.FindControl<Button>("BtnReport")!;
        Assert.Contains(report, bar.Children);
        Assert.Equal("Report a problem or send a recommendation…", report.Content);
        w.Close();
    }

    [AvaloniaFact]
    public void Home_feedback_link_opens_the_report_not_the_website()
    {
        var view = new AboutView { DataContext = new AboutViewModel() };
        var w = new Window { Content = view, Width = 900, Height = 600 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var link = view.FindControl<Button>("FeedbackLink")!;
        Assert.True(link.IsVisible);
        Assert.Same(((AboutViewModel)view.DataContext!).OpenReportCommand, link.Command);
        Assert.Null(link.CommandParameter);
        w.Close();
    }
}
