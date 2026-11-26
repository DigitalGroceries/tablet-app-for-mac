using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>TASKS #81 (a): "Record the problem" in the report sheet.</summary>
public class ReportRecordTests
{
    static (List<string>, IReadOnlyList<string>, string?) Inputs() => (new List<string>(), Array.Empty<string>(), null);

    private sealed class Daemon : IDaemonDebugSession
    {
        public event Action<JObject>? DeviceReport;
        public List<bool> Debug { get; } = new();
        public Task SetTabletDebugAsync(bool enabled) { Debug.Add(enabled); return Task.CompletedTask; }
        public void Emit() => DeviceReport?.Invoke(new JObject { ["Path"] = "X.PenReport", ["Data"] = new JObject { ["Pressure"] = 1 } });
    }

    static (MainWindow Mw, Border Sheet) OpenSheet(ReportViewModel vm)
    {
        var mw = new MainWindow { Width = 900, Height = 700 };
        mw.Show();
        Dispatcher.UIThread.RunJobs();
        InWindowSheet.MainOverride = () => mw;
        ReportWindow.Open(vm);
        Dispatcher.UIThread.RunJobs();
        return (mw, mw.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Sheet"));
    }

    static T Named<T>(Border sheet, string name) where T : Control =>
        sheet.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [AvaloniaFact]
    public async Task Record_button_starts_and_stops_and_shows_the_count()
    {
        var d = new Daemon();
        var vm = new ReportViewModel(Inputs, null, "test", recorder: new ProblemRecorder(d));
        var (mw, sheet) = OpenSheet(vm);
        try
        {
            var record = Named<Button>(sheet, "ReportRecord");
            Assert.True(record.IsVisible);
            Assert.Equal("Record the problem", record.Content);
            Assert.True(Named<TextBlock>(sheet, "ReportRecordHint").IsVisible);

            await vm.ToggleRecordCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsRecording);
            Assert.Equal("Stop recording", record.Content);
            d.Emit();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Recording… messages from the tablet so far: 1", Named<TextBlock>(sheet, "ReportRecordStatus").Text);

            await vm.ToggleRecordCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsRecording);
            Assert.Equal("Record the problem", record.Content);
            Assert.Equal("Recording added to the report: 1 messages from the tablet.", Named<TextBlock>(sheet, "ReportRecordStatus").Text);
            Assert.Equal(new[] { true, false }, d.Debug);
        }
        finally
        {
            vm.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            InWindowSheet.MainOverride = null;
            mw.Close();
        }
    }

    /// <summary>V29 #167 (Digital Groceries 20 Nov ~09:46: "I cant record report file because when recording is happening the radial menu settings are
    /// not available because the app is held in recording mode"): while recording, the report steps aside so the whole app can be
    /// used, and a bar at the bottom of the window shows the recording and stops it; stopping brings the report back.</summary>
    [AvaloniaFact]
    public void While_recording_the_report_steps_aside_and_a_bar_stops_it()
    {
        var d = new Daemon();
        var vm = new ReportViewModel(Inputs, null, "test", recorder: new ProblemRecorder(d));
        var (mw, sheet) = OpenSheet(vm);
        try
        {
            var host = mw.FindControl<Panel>(InWindowSheet.HostName)!;
            Assert.True(host.IsVisible);
            vm.ToggleRecordCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsRecording);
            Assert.False(sheet.IsVisible);
            Assert.False(host.IsVisible);                         // the page behind can be used
            var bar = InWindowSheet.Bar;
            Assert.NotNull(bar);
            Assert.True(bar!.IsVisible);
            d.Emit();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(bar.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Recording… messages from the tablet so far: 1");

            var stop = bar.GetVisualDescendants().OfType<Button>().First(b => b.Name == "RecordingBarStop");
            Assert.Equal("Stop recording", stop.Content);
            stop.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 50 && (vm.IsRecording || InWindowSheet.Bar != null); i++) { System.Threading.Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
            Assert.False(vm.IsRecording);
            Assert.True(sheet.IsVisible);
            Assert.True(host.IsVisible);
            Assert.Null(InWindowSheet.Bar);
        }
        finally
        {
            vm.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            InWindowSheet.MainOverride = null;
            mw.Close();
        }
    }

    [AvaloniaFact]
    public void Cancel_while_recording_turns_the_tablet_stream_off()
    {
        var d = new Daemon();
        var vm = new ReportViewModel(Inputs, null, "test", recorder: new ProblemRecorder(d));
        var (mw, _) = OpenSheet(vm);
        try
        {
            vm.ToggleRecordCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsRecording);
            vm.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsRecording);
            Assert.Equal(new[] { true, false }, d.Debug);
        }
        finally
        {
            InWindowSheet.MainOverride = null;
            mw.Close();
        }
    }

    [AvaloniaFact]
    public void Without_a_recorder_the_record_row_is_hidden()
    {
        var vm = new ReportViewModel(Inputs, null, "test");
        var (mw, sheet) = OpenSheet(vm);
        try
        {
            Assert.False(vm.CanRecord);
            Assert.False(Named<Button>(sheet, "ReportRecord").IsVisible);
            Assert.False(Named<TextBlock>(sheet, "ReportRecordHint").IsVisible);
            Assert.Equal("", vm.RecordStatus);
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
    public void Every_language_has_the_record_texts()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "OpenTabletArtist", "Assets", "i18n");
        foreach (var lang in new[] { "bg", "de", "en", "es", "fr", "ja", "ru", "zh-CN" })
        {
            var json = JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, lang + ".json")));
            foreach (var key in new[] { "report.record.start", "report.record.stop", "report.record.hint", "report.record.recording", "report.record.done", "report.record.limit" })
                Assert.False(string.IsNullOrWhiteSpace(json[key]?.ToString()), $"{lang}: {key}");
            Assert.Contains("{n}", json["report.record.recording"]!.ToString());
            Assert.Contains("{n}", json["report.record.done"]!.ToString());
        }
    }
}
