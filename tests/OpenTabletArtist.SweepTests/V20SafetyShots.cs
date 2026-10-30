using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>Safety update (ideas from MockTab): screenshots of Settings › System backup card with an
/// import preview, and the diagnostic file card, in the dark and light app themes.</summary>
public class V20SafetyShots
{
    sealed class NoDebug : OtdInterop.IDaemonDebugSession
    {
        public event Action<JObject>? DeviceReport { add { } remove { } }
        public System.Threading.Tasks.Task SetTabletDebugAsync(bool enabled) => System.Threading.Tasks.Task.CompletedTask;
    }

    static string Dir => Environment.GetEnvironmentVariable("SHOTS_DIR") ?? "/workspace/otaE/v20safety/shots";

    [AvaloniaTheory]
    [InlineData("dark", 0)]
    [InlineData("light", 8)]
    public void BackupAndDiagnostics(string name, int step)
    {
        var before = LightnessService.Saved;
        LightnessService.Set(step);
        var tmp = Directory.CreateTempSubdirectory("ota-shot").FullName;
        var (appDir, otdDir) = (SettingsBundle.AppDataDir, SettingsBundle.OtdDataDir);
        try
        {
            var a = Path.Combine(tmp, "app"); var o = Path.Combine(tmp, "otd");
            Directory.CreateDirectory(a); Directory.CreateDirectory(Path.Combine(o, "Presets"));
            File.WriteAllText(Path.Combine(a, "settings.json"), "{\"ui.language\":\"en\",\"PerPenTables:Wacom PTK-440\":\"[]\"}");
            File.WriteAllText(Path.Combine(o, "settings.json"), "{\"Profiles\":[{\"Tablet\":\"Wacom PTK-440\",\"X\":1}]}");
            SettingsBundle.AppDataDir = () => a; SettingsBundle.OtdDataDir = () => o;
            var file = Path.Combine(tmp, "Intuos4PTK440_30Oct26.tabletsettings");
            SettingsBundle.Export(file, portable: true);
            File.WriteAllText(Path.Combine(a, "settings.json"), "{\"ui.language\":\"de\"}");
            File.WriteAllText(Path.Combine(o, "settings.json"), "{\"Profiles\":[{\"Tablet\":\"Wacom PTK-440\",\"X\":2}]}");
            File.WriteAllText(Path.Combine(o, "Presets", "Photoshop.json"), "{}");
            SettingsBundle.MakeBackup("v19-to-v20");

            var diag = new DiagnosticsViewModel(new NoDebug());
            diag.DiagnosticStatus = "Saved to:";
            diag.DiagnosticFilePath = "/Users/you/Desktop/TabletApp-diagnostics-2026-10-30-2130.zip";
            var vm = new BackupViewModel(() => "Wacom PTK-440") { Diagnostics = diag };
            vm.PreviewFile(file);

            var view = new ScrollViewer { Content = new BackupView { DataContext = vm } };
            var w = new Window { Content = view, Width = 900, Height = 1000 };
            _ = new ThemeViewModel();
            w.Show();
            Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Directory.CreateDirectory(Dir);
            w.CaptureRenderedFrame()!.Save($"{Dir}/settings-backup-preview-{name}.png");
            Assert.True(vm.HasPreview);
            Assert.True(vm.PreviewHasChanges);
            Assert.StartsWith("Intuos4PTK440_", vm.SuggestedFileName);
            w.Close();
        }
        finally
        {
            SettingsBundle.AppDataDir = appDir; SettingsBundle.OtdDataDir = otdDir;
            LightnessService.Set(before);
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
