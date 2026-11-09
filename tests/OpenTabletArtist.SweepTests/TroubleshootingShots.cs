using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>#25: headless shot of Settings › Troubleshooting box.</summary>
public class TroubleshootingShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("OTA_SHOTS") ?? "/workspace";

    private sealed class Noop : ICommand
    {
        public event System.EventHandler? CanExecuteChanged;
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) { }
    }

    /// <summary>Inert stand-in so the shot does not need a live AppSession / daemon.</summary>
    private sealed class ShotState
    {
        public string Title => "Troubleshooting";
        public string Intro =>
            "Quick fixes when the tablet stops responding, a setting will not stick, or you need to send a report.";
        public string RestartTitle => "Restart driver";
        public string RestartBody =>
            "Stops the tablet driver and starts it again. Use this when the pen stops moving or a tablet is not detected.";
        public string RestartButton => "Restart driver";
        public string DiagTitle => "Diagnostic report";
        public string DiagBody =>
            "Saves a zip on your Desktop with logs, settings and tablet info you can attach when asking for help.";
        public string DiagButton => "Create diagnostic file";
        public string DiagReveal => "Show in Finder";
        public string PermTitle => "Reset permissions";
        public string PermBody =>
            "Clears this app’s Input Monitoring and Accessibility entries so macOS will ask again. macOS only.";
        public string PermButton => "Reset permissions";
        public bool ShowPermissionReset => true;
        public bool HasDiagnosticFile => false;
        public string DiagnosticStatus => "";
        public string DiagnosticFilePath => "";
        public string PermissionsResetStatus => "";
        public ICommand RestartDaemonCommand { get; } = new Noop();
        public ICommand CreateDiagnosticFileCommand { get; } = new Noop();
        public ICommand RevealDiagnosticFileCommand { get; } = new Noop();
        public ICommand ResetPermissionsCommand { get; } = new Noop();
    }

    [AvaloniaFact]
    public void TroubleshootingBox_Screenshot()
    {
        _ = new ThemeViewModel();
        System.IO.Directory.CreateDirectory(Dir);
        var view = new TroubleshootingView { DataContext = new ShotState() };
        var w = new Window
        {
            Content = new Border { Padding = new Thickness(24), Child = view },
            Width = 900,
            Height = 720,
            Title = "Settings › Troubleshooting",
        };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var path = $"{Dir}/troubleshooting-box.png";
        w.CaptureRenderedFrame()!.Save(path);
        w.Close();
        Assert.True(System.IO.File.Exists(path));
    }
}
