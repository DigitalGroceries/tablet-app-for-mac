using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpenTabletArtist.Services;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>#22 headless shots (real Skia) of the footer button row and the factory confirm dialog.</summary>
public class SaveDefaultButtonsShots
{
    private sealed class Recording : System.Windows.Input.ICommand
    {
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) { }
    }

    private sealed class FooterState
    {
        public SessionState SettingsSession { get; } = new();
        public ConnectionState Connection { get; } = new();
        public Recording SaveSettingsCommand { get; } = new();
        public Recording ReloadSettingsCommand { get; } = new();
        public Recording RevertSettingsCommand { get; } = new();
        public Recording SaveAsDefaultCommand { get; } = new();
        public Recording RevertToDefaultCommand { get; } = new();
        public Recording RestoreFactorySettingsCommand { get; } = new();
        public bool CanRevertToDefault => true;
    }

    private sealed class SessionState
    {
        public bool CanEditSettings => true;
        public bool SettingsBusy => false;
        public bool SettingsPaused => false;
        public string SaveStatusText => "Unsaved changes";
        public bool HasUnsavedChanges => true;
    }

    private sealed class ConnectionState
    {
        public string DiscardedChangeNotice => "";
        public bool HasDiscardedChangeNotice => false;
        public string SettingsRefreshedNotice => "";
        public bool HasSettingsRefreshedNotice => false;
        public bool HasSettingsNotice => false;
        public Recording DismissSettingsNoticesCommand { get; } = new();
    }

    private static void Pump() { for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs(); }

    private static void SaveFrame(Window window, string path)
    {
        Pump();
        window.UpdateLayout();
        Pump();
        var bmp = window.CaptureRenderedFrame();
        Assert.NotNull(bmp);
        if (File.Exists(path)) File.Delete(path);
        bmp!.Save(path);
        Assert.True(new FileInfo(path).Length > 500, path + " too small");
    }

    [AvaloniaFact]
    public void ButtonRow_Screenshot()
    {
        var footer = new SettingsFooterView();
        var window = new Window
        {
            Content = footer,
            DataContext = new FooterState(),
            Width = 1200,
            Height = 140,
            Title = "Save default buttons",
            Background = Brushes.White,
        };
        window.Show();
        Pump();
        window.Measure(new Size(1200, 140));
        window.Arrange(new Rect(0, 0, 1200, 140));
        Pump();
        SaveFrame(window, "/workspace/save-default-buttons.png");
        window.Close();
    }

    [AvaloniaFact]
    public void FactoryConfirm_Screenshot()
    {
        var panel = new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Width = 420,
                Children =
                {
                    new TextBlock
                    {
                        Text = Localizer.Te("save.factory.confirm_title", "Restore factory settings?"),
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 0, 12),
                    },
                    new TextBlock
                    {
                        Text = Localizer.Te("save.factory.confirm_body",
                            "This replaces your current settings with the original factory defaults and saves them. "
                            + "Your own saved default (if any) is kept, so you can still use Revert to default afterwards. "
                            + "Unsaved edits will be lost."),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 20),
                        FontSize = 13,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            new Button { Content = "Yes", Padding = new Thickness(24, 8), FontSize = 13 },
                            new Button { Content = "No", Padding = new Thickness(24, 8), FontSize = 13 },
                        }
                    }
                }
            }
        };
        var window = new Window
        {
            Content = panel,
            Width = 480,
            Height = 300,
            Background = Brushes.White,
            Title = Localizer.Te("save.factory.confirm_title", "Restore factory settings?"),
        };
        window.Show();
        Pump();
        window.Measure(new Size(480, 300));
        window.Arrange(new Rect(0, 0, 480, 300));
        Pump();
        SaveFrame(window, "/workspace/factory-confirm.png");
        window.Close();
    }
}
