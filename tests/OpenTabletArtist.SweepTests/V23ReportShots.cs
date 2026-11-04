using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>Version 23 (owner's request, 4 Nov 2026): pictures of the report sheet with "Send with file", before and
/// after saving, for checking the texts and for the help.</summary>
public class V23ReportShots
{
    static string Dir => Environment.GetEnvironmentVariable("SHOTS_DIR") ?? "/workspace/otaE/v23report/shots";
    static (List<string>, IReadOnlyList<string>, string?) Inputs() => (new List<string> { "Wacom PTK-440" }, Array.Empty<string>(), "0.6.8");

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("bg")]
    public async Task Report_sheet_before_and_after_saving(string lang)
    {
        Localizer.SetPreference(lang);
        var mw = new MainWindow { Width = 1000, Height = 760 };
        mw.Show();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            var vm = new ReportViewModel(Inputs, null, "shots");
            var w = new ReportWindow(vm);
            InWindowSheet.Show(w);
            Dispatcher.UIThread.RunJobs(); mw.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Directory.CreateDirectory(Dir);
            mw.CaptureRenderedFrame()!.Save($"{Dir}/report-{lang}-1-form.png");

            var target = Path.Combine(Directory.CreateTempSubdirectory().FullName, "TabletApp-diagnostics-2026-11-04-2000.zip");
            vm.PickSaveFile = _ => Task.FromResult<string?>(target);
            vm.Reveal = _ => { };
            await vm.SendFileCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs(); mw.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            mw.CaptureRenderedFrame()!.Save($"{Dir}/report-{lang}-2-saved.png");
            vm.DoneCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); Localizer.SetPreference("en"); }
    }
}
