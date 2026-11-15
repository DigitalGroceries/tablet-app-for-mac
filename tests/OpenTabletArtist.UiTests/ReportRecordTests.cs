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
            Assert.Equal("Recording… tablet reports so far: 1", Named<TextBlock>(sheet, "ReportRecordStatus").Text);

            await vm.ToggleRecordCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsRecording);
            Assert.Equal("Record the problem", record.Content);
            Assert.Equal("Recording added to the report: 1 tablet reports.", Named<TextBlock>(sheet, "ReportRecordStatus").Text);
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
            foreach (var key in new[] { "report.record.start", "report.record.stop", "report.record.hint", "report.record.recording", "report.record.done" })
                Assert.False(string.IsNullOrWhiteSpace(json[key]?.ToString()), $"{lang}: {key}");
            Assert.Contains("{n}", json["report.record.recording"]!.ToString());
            Assert.Contains("{n}", json["report.record.done"]!.ToString());
        }
    }
}
