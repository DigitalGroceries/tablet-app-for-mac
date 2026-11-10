using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The big "it worked" overlay (<see cref="ActionConfirmation"/>): every ACTION button asks for it with its own
/// <c>confirm.*</c> key after the action succeeded, and never after it failed or was declined. Navigation,
/// pickers, switches and Cancel/Close never ask. The overlay itself is the ring-mode popup; here the
/// <see cref="ActionConfirmation.Recorder"/> hook sees the keys.
/// </summary>
public class ActionConfirmationTests
{
    private static List<string> Record()
    {
        var shown = new List<string>();
        ActionConfirmation.Recorder = shown.Add;
        return shown;
    }

    // ── Presets ───────────────────────────────────────────────────────────────────────────────────
    private sealed class NoHotkeys : IProfileHotkeys
    {
        public HotkeyChord? GetChord(string snapshot) => null;
        public HotkeySetResult SetHotkey(string snapshot, HotkeyChord chord) => HotkeySetResult.Ok;
        public void ClearHotkey(string snapshot) { }
        public void Sync(IEnumerable<string> snapshotNames) { }
        public void RenameSnapshot(string oldName, string newName) { }
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"otdconfirm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static PresetsViewModel NewPresets(string dir, FakeSettingsCoordinator coordinator, FakeDialogService? dialogs = null)
    {
        var store = new PresetStore(NullOtdLog.Instance);
        return new PresetsViewModel(store, coordinator, new FakeDeviceData(), dialogs ?? new FakeDialogService(),
            new NoHotkeys(), new ProfileSwitchService(coordinator, store, () => dir))
        { PresetDirectory = dir };
    }

    [Fact]
    public async Task SavePreset_ConfirmsOnSuccess_NotWithoutSettings()
    {
        var dir = TempDir();
        try
        {
            var shown = Record();
            await NewPresets(dir, new FakeSettingsCoordinator()).SavePresetCommand.ExecuteAsync(null);
            Assert.Empty(shown);

            await NewPresets(dir, new FakeSettingsCoordinator { CurrentSettings = new Settings() }).SavePresetCommand.ExecuteAsync(null);
            Assert.Equal(["confirm.preset_saved"], shown);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task LoadPreset_ConfirmsOnlyWhenTheDriverTookIt()
    {
        var dir = TempDir();
        try
        {
            var coordinator = new FakeSettingsCoordinator { CurrentSettings = new Settings() };
            var vm = NewPresets(dir, coordinator);
            await vm.SavePresetCommand.ExecuteAsync(null);
            var shown = Record();

            await vm.LoadPresetCommand.ExecuteAsync("Preset");
            Assert.Equal(["confirm.preset_loaded"], shown);

            shown.Clear();
            await vm.LoadPresetCommand.ExecuteAsync("Nope");             // no such preset
            coordinator.DaemonAccepts = false;
            await vm.LoadPresetCommand.ExecuteAsync("Preset");           // the driver refused it
            Assert.Empty(shown);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task UpdateAndDuplicatePreset_Confirm()
    {
        var dir = TempDir();
        try
        {
            var vm = NewPresets(dir, new FakeSettingsCoordinator { CurrentSettings = new Settings() });
            await vm.SavePresetCommand.ExecuteAsync(null);
            var shown = Record();

            await vm.UpdatePresetCommand.ExecuteAsync("Preset");
            await vm.DuplicatePresetCommand.ExecuteAsync("Preset");
            await vm.DuplicatePresetCommand.ExecuteAsync("Missing");     // nothing to copy: silent

            Assert.Equal(["confirm.preset_updated", "confirm.preset_duplicated"], shown);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RenamePreset_ConfirmsOnlyWhenRenamed()
    {
        var dir = TempDir();
        try
        {
            var dialogs = new FakeDialogService();
            var vm = NewPresets(dir, new FakeSettingsCoordinator { CurrentSettings = new Settings() }, dialogs);
            await vm.SavePresetCommand.ExecuteAsync(null);
            await vm.SavePresetCommand.ExecuteAsync(null);               // Preset + Preset 2 (or similar)
            var other = Path.GetFileNameWithoutExtension(Directory.GetFiles(dir).First(f => !f.EndsWith("Preset.json")));
            var shown = Record();

            dialogs.InputResult = null;                                  // cancelled
            await vm.RenamePresetCommand.ExecuteAsync("Preset");
            dialogs.InputResult = other;                                 // name already taken
            await vm.RenamePresetCommand.ExecuteAsync("Preset");
            Assert.Empty(shown);

            dialogs.InputResult = "Brand new";
            await vm.RenamePresetCommand.ExecuteAsync("Preset");
            Assert.Equal(["confirm.preset_renamed"], shown);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task DeletePreset_ConfirmsOnlyAfterTheUserConfirmedAndItWasDeleted()
    {
        var dir = TempDir();
        try
        {
            var dialogs = new FakeDialogService();
            var vm = NewPresets(dir, new FakeSettingsCoordinator { CurrentSettings = new Settings() }, dialogs);
            await vm.SavePresetCommand.ExecuteAsync(null);
            var shown = Record();

            dialogs.ConfirmResult = false;                               // "Cancel" in the confirm dialog
            await vm.DeletePresetCommand.ExecuteAsync("Preset");
            Assert.Empty(shown);
            Assert.True(File.Exists(Path.Combine(dir, "Preset.json")));

            dialogs.ConfirmResult = true;
            await vm.DeletePresetCommand.ExecuteAsync("Preset");
            Assert.Equal(["confirm.preset_deleted"], shown);
            Assert.False(File.Exists(Path.Combine(dir, "Preset.json")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── Settings backup (Export / Import) ─────────────────────────────────────────────────────────
    [Fact]
    public async Task ApplyImport_ConfirmsWhenWritten_NotWhenItFailsOrIsCancelled()
    {
        var shown = Record();
        var failing = new BackupViewModel(null, reloadDriver: () => Task.CompletedTask, resolveUnsaved: () => Task.FromResult(true))
        { ApplyOverride = _ => throw new IOException("disk full"), Pending = new SettingsBundle.Bundle() };
        await failing.ApplyImportCommand.ExecuteAsync(null);

        var cancelled = new BackupViewModel(null, reloadDriver: () => Task.CompletedTask, resolveUnsaved: () => Task.FromResult(false))
        { ApplyOverride = _ => "x", Pending = new SettingsBundle.Bundle() };
        await cancelled.ApplyImportCommand.ExecuteAsync(null);
        Assert.Empty(shown);

        var ok = new BackupViewModel(null, reloadDriver: () => Task.CompletedTask, resolveUnsaved: () => Task.FromResult(true))
        { ApplyOverride = _ => "/tmp/safety.zip", Pending = new SettingsBundle.Bundle() };
        await ok.ApplyImportCommand.ExecuteAsync(null);
        Assert.Equal(["confirm.settings_imported"], shown);
    }

    [Fact]
    public void ExportTo_ConfirmsWhenTheFileIsWritten_NotWhenItCannotBe()
    {
        var shown = Record();
        var vm = new BackupViewModel(null);
        vm.ExportTo(Path.GetTempPath());                       // a folder, not a file: can't be written
        Assert.Empty(shown);

        var dir = TempDir();
        try
        {
            vm.ExportTo(Path.Combine(dir, "settings.otasettings"));
            Assert.Equal(["confirm.settings_exported"], shown);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── Driver start / stop / restart (Daemon page, Home "Fix", the pen menu — one shared command) ──
    private sealed class FakeLifecycle(string? exe) : IDaemonLifecycleService
    {
        public string? ExpectedExePath() => exe;
        public bool IsAppManaged(string? path) => path != null && exe != null;
        public bool HasBundledDaemon() => false;
        public string? FindExe() => exe;
        public bool IsRunning() => false;
        public string? Launch(string? executablePath = null) => null;
        public bool Stop(int processId) => true;
        public void StopAll() { }
        public string? PathOf(int processId) => null;
        public string? SingleRunningDaemonPath() => null;
    }

    private static AppSession NewSession() => new(FakeSession.Over(new FakeDaemonTransport()), new FakeLifecycle("fake-daemon.exe"))
    {
        DaemonOperationTimeout = TimeSpan.FromMilliseconds(150),   // nothing ever connects in a test
    };

    [Fact]
    public async Task StopDriver_ConfirmsWhenItStopped()
    {
        using var session = NewSession();
        var shown = Record();
        await session.StopDaemonCommand.ExecuteAsync(null);
        Assert.False(session.HasDaemonOperationError);
        Assert.Equal(["confirm.driver_stopped"], shown);
    }

    [Fact]
    public async Task StartAndRestartDriver_DoNotConfirmWhenTheDriverNeverCameOnline()
    {
        using var session = NewSession();
        var shown = Record();
        await session.StartDaemonCommand.ExecuteAsync(null);
        await session.RestartDaemonCommand.ExecuteAsync(null);
        Assert.True(session.HasDaemonOperationError);
        Assert.Empty(shown);
    }

    // ── Log: Clear ──────────────────────────────────────────────────────────────────────────────────
    private sealed class NoLog : IDaemonLogSource
    {
        public event Action<LogMessage>? LogReceived { add { } remove { } }
        public Task<List<LogMessage>> GetCurrentLogAsync() => Task.FromResult(new List<LogMessage>());
    }

    [Fact]
    public void ClearLog_Confirms()
    {
        var vm = new LogViewModel(new NoLog());
        var shown = Record();
        vm.ClearCommand.Execute(null);
        Assert.Equal(["confirm.log_cleared"], shown);
    }

    // ── Custom tablet configs: Delete ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task DeleteConfiguration_ConfirmsOnlyAfterTheUserConfirmedAndTheFileIsGone()
    {
        var dir = TempDir();
        try
        {
            var file = Path.Combine(dir, "ctl672.json");
            File.WriteAllText(file, "{\"Name\":\"Wacom CTL-672\"}");
            var dialogs = new FakeDialogService();
            var vm = new CustomTabletConfigsViewModel(dialogs, new FakeConfigurationsDirectoryProvider(dir));
            var shown = Record();

            dialogs.ConfirmResult = false;
            await vm.DeleteConfigurationCommand.ExecuteAsync(file);
            Assert.Empty(shown);
            Assert.True(File.Exists(file));

            dialogs.ConfirmResult = true;
            await vm.DeleteConfigurationCommand.ExecuteAsync(file);
            Assert.Equal(["confirm.config_deleted"], shown);
            Assert.False(File.Exists(file));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── The strings: every key used in code exists, translated, in all 8 locales ───────────────────
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenTabletArtist.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static readonly string[] Locales = ["en", "bg", "de", "es", "fr", "ja", "ru", "zh-CN"];

    private static IEnumerable<(string File, string Key, string English)> CallSites()
    {
        var app = Path.Combine(RepoRoot(), "OpenTabletArtist");
        var rx = new Regex("ActionConfirmation\\.Show\\(\"(confirm\\.[a-z_]+)\",\\s*\"([^\"]+)\"\\)");
        foreach (var f in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            foreach (Match m in rx.Matches(File.ReadAllText(f)))
                yield return (Path.GetFileName(f), m.Groups[1].Value, m.Groups[2].Value);
    }

    private static Dictionary<string, string> Table(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(RepoRoot(), "OpenTabletArtist", "Assets", "i18n", code + ".json")))!;

    [Fact]
    public void EveryConfirmKeyInCode_IsInAllEightLocales_WithTheSameEnglishFallback()
    {
        var sites = CallSites().ToList();
        Assert.True(sites.Count >= 30, $"found only {sites.Count} call sites");
        var tables = Locales.ToDictionary(l => l, Table);
        foreach (var (file, key, english) in sites)
        {
            foreach (var l in Locales)
                Assert.True(tables[l].TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v), $"{key} missing in {l} (used in {file})");
            Assert.Equal(tables["en"][key], english);   // the in-code fallback never drifts from en.json
        }
    }

    [Fact]
    public void ConfirmStrings_AreShort_AndTranslatedOutsideEnglish()
    {
        var en = Table("en");
        foreach (var l in Locales.Where(l => l != "en"))
        {
            var t = Table(l);
            foreach (var k in en.Keys.Where(k => k.StartsWith("confirm.")))
            {
                Assert.True(t.ContainsKey(k), $"{k} missing in {l}");
                Assert.True(t[k].Length <= 60, $"{l}:{k} is too long for the overlay");
                Assert.NotEqual(en[k], t[k]);   // really translated, not copied from English
            }
        }
    }

    [Fact]
    public void LocaleFilesStayInKeyParity()
    {
        var en = Table("en").Keys.Where(k => k.StartsWith("confirm.")).ToHashSet();
        foreach (var l in Locales)
            Assert.Equal(en, Table(l).Keys.Where(k => k.StartsWith("confirm.")).ToHashSet());
    }

    // ── Which button asks for which message (a map to keep honest; the commands that need a whole
    //    window to run — Save, Revert to saved, Reload — are checked here instead of by execution) ──
    [Theory]
    [InlineData("MainViewModel.cs", "confirm.settings_saved")]
    [InlineData("MainViewModel.cs", "confirm.settings_reverted")]
    [InlineData("MainViewModel.cs", "confirm.settings_reloaded")]
    [InlineData("DashboardViewModel.cs", "confirm.permissions_reset")]
    [InlineData("DashboardViewModel.cs", "confirm.fixed")]
    [InlineData("AboutViewModel.cs", "confirm.update_checked")]
    [InlineData("BackupViewModel.cs", "confirm.settings_exported")]
    [InlineData("DiagnosticsViewModel.cs", "confirm.diagnostic_created")]
    [InlineData("DiagnosticsViewModel.cs", "confirm.screenshots_added")]
    [InlineData("ReportViewModel.cs", "confirm.report_saved")]
    [InlineData("LogViewModel.cs", "confirm.log_cleared")]
    [InlineData("LogView.axaml.cs", "confirm.log_copied")]
    [InlineData("TestView.axaml.cs", "confirm.image_copied")]
    [InlineData("DynamicsView.axaml.cs", "confirm.image_copied")]
    [InlineData("ThemeView.axaml.cs", "confirm.copied")]
    [InlineData("ThemeViewModel.cs", "confirm.theme_reset")]
    [InlineData("ThemeViewModel.cs", "confirm.background_cleared")]
    [InlineData("GradientEditorViewModel.cs", "confirm.glows_reset")]
    [InlineData("StrokeRecordingViewModel.cs", "confirm.recording_saved")]
    [InlineData("CustomTabletConfigsViewModel.cs", "confirm.config_installed")]
    [InlineData("CustomTabletConfigsViewModel.cs", "confirm.config_deleted")]
    [InlineData("CalibrationViewModel.cs", "confirm.calibration_applied")]
    [InlineData("CalibrationViewModel.cs", "confirm.calibration_cleared")]
    [InlineData("TabletDetailViewModel.cs", "confirm.calibration_cleared")]
    [InlineData("TabletDetailViewModel.cs", "confirm.calibration_imported")]
    [InlineData("TabletDetailViewModel.cs", "confirm.calibration_exported")]
    [InlineData("TabletDetailViewModel.cs", "confirm.report_copied")]
    [InlineData("TabletDetailViewModel.cs", "confirm.keys_default")]
    [InlineData("TabletDetailViewModel.cs", "confirm.ring_default")]
    [InlineData("TabletDetailViewModel.cs", "confirm.double_click_reset")]
    [InlineData("TabletDetailViewModel.cs", "confirm.output_mode_fixed")]
    [InlineData("DesktopEntryViewModel.cs", "confirm.launcher_created")]
    [InlineData("DaemonViewModel.cs", "confirm.service_started")]
    public void TheButtonsCommandAsksForItsMessage(string file, string key)
        => Assert.Contains(CallSites(), s => s.File == file && s.Key == key);

    // ── Never from a button that only navigates, opens something or toggles ─────────────────────────
    [Fact]
    public void NavigationAndPickerCommandsDoNotConfirm()
    {
        var notAllowed = new[]
        {
            "NavigateCommand", "SelectTabCommand", "OpenUrlCommand", "OpenHelpCommand", "OpenReportCommand",
            "OpenSupportedTabletsCommand", "OpenDaemonPageCommand", "OpenFolderCommand", "OpenContainingFolderCommand",
            "CancelDialogCommand", "CancelReportCommand", "CancelImportCommand",
        };
        // those commands are plain methods named without the suffix; none of the call sites sits in one of them
        var app = Path.Combine(RepoRoot(), "OpenTabletArtist");
        foreach (var name in notAllowed.Select(n => n.Replace("Command", "")))
        {
            var rx = new Regex(@"void\s+" + name + @"\s*\([^)]*\)\s*(\{[^{}]*\}|=>[^;]*;)");
            foreach (var f in Directory.EnumerateFiles(Path.Combine(app, "ViewModels"), "*.cs"))
                foreach (Match m in rx.Matches(File.ReadAllText(f)))
                    Assert.DoesNotContain("ActionConfirmation", m.Value);
        }
    }
}
