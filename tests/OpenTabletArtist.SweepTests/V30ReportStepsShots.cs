using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>V30 #200 screenshots: the "Report saved" sheet with its 3 numbered steps, in light and dark and in Bulgarian
/// (agent K's language). Written to $OTA_SHOTS_DIR (default /workspace/ota-200-shots).</summary>
public class V30ReportStepsShots
{
    static string Dir => Environment.GetEnvironmentVariable("OTA_SHOTS_DIR") ?? Sweep.ShotsRoot + "/ota-200-shots";
    static (List<string>, IReadOnlyList<string>, string?) Inputs() => (new List<string>(), Array.Empty<string>(), null);

    static async System.Threading.Tasks.Task Shot(string name, string lang, bool dark)
    {
        _ = new ThemeViewModel();
        OpenTabletArtist.Services.ThemeService.Apply(dark ? OpenTabletArtist.Services.ThemeService.Wacom : OpenTabletArtist.Services.ThemeService.Light);
        OpenTabletArtist.Services.Localizer.SetPreference(lang);
        var mw = new MainWindow { Width = 1100, Height = 860 };
        mw.Show();
        Dispatcher.UIThread.RunJobs();
        InWindowSheet.MainOverride = () => mw;
        try
        {
            var vm = new ReportViewModel(Inputs, null, "test") { Email = "k@example.com" };
            ReportWindow.Open(vm);
            Dispatcher.UIThread.RunJobs();
            var folder = Path.Combine(Path.GetTempPath(), "Desktop");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, "Tablet App for Mac report 2026-11-23 15-30.zip");
            vm.PickSaveFile = _ => System.Threading.Tasks.Task.FromResult<string?>(target);
            await vm.SendFileCommand.ExecuteAsync(null);
            for (int i = 0; i < 8; i++) { Dispatcher.UIThread.RunJobs(); mw.UpdateLayout(); }
            Directory.CreateDirectory(Dir);
            mw.CaptureRenderedFrame()!.Save(Path.Combine(Dir, name + ".png"));
            vm.DoneCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }
        finally { InWindowSheet.MainOverride = null; mw.Close(); OpenTabletArtist.Services.Localizer.SetPreference("en"); }
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Screenshots()
    {
        await Shot("report-saved-steps-en-dark", "en", true);
        await Shot("report-saved-steps-bg-dark", "bg", true);
    }
}
