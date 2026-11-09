using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenTabletArtist.Services;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>#22 headless shots (real Skia) of the footer button row and the factory confirm dialog.
/// The confirm shot uses the same elevated dark sheet chrome as <c>InWindowSheet</c> (BgElevatedBrush +
/// TextPrimaryBrush), not a white page — an earlier white mock made light theme text unreadable.</summary>
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

    private static IBrush Brush(string key, IBrush fallback) =>
        Application.Current?.TryFindResource(key, out var v) == true && v is IBrush b ? b : fallback;

    private static ControlTheme? Theme(string key) =>
        Application.Current?.TryFindResource(key, out var v) == true ? v as ControlTheme : null;

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
            Background = Brush("BgBaseBrush", Brushes.Black),
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
        var yesBtn = new Button
        {
            Content = Localizer.Te("save.factory.confirm_action", "Restore Factory Settings"),
            Padding = new Thickness(24, 8),
            FontSize = 13,
        };
        var noBtn = new Button
        {
            Content = Localizer.Te("app.cancel", "Cancel"),
            Padding = new Thickness(24, 8),
            FontSize = 13,
            IsCancel = true,
            IsDefault = true,
        };
        if (Theme("AccentButton") is { } accent) yesBtn.Theme = accent;
        if (Theme("GhostButton") is { } ghost) noBtn.Theme = ghost;

        // Mirror InWindowSheet: elevated dark panel + primary text (not a white page).
        var sheet = new Border
        {
            Background = Brush("BgElevatedBrush", new SolidColorBrush(Color.Parse("#2A2A2A"))),
            BorderBrush = Brush("GlassBorderBrush", new SolidColorBrush(Color.Parse("#444444"))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24),
            Width = 420,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = Localizer.Te("save.factory.confirm_title", "Restore factory settings?"),
                        FontSize = 15,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brush("TextPrimaryBrush", Brushes.White),
                        Margin = new Thickness(0, 0, 0, 12),
                    },
                    new TextBlock
                    {
                        Text = Localizer.Te("save.factory.confirm_body",
                            "Your current settings will be replaced with the factory settings. "
                            + "Your own saved default is kept, so you can still use Revert to default afterwards."),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brush("TextPrimaryBrush", Brushes.White),
                        Margin = new Thickness(0, 0, 0, 20),
                        FontSize = 13,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { yesBtn, noBtn },
                    }
                }
            }
        };

        var window = new Window
        {
            Content = new Grid
            {
                Background = Brush("BgBaseBrush", Brushes.Black),
                Children = { sheet },
            },
            Width = 520,
            Height = 320,
            Title = Localizer.Te("save.factory.confirm_title", "Restore factory settings?"),
            Background = Brush("BgBaseBrush", Brushes.Black),
        };
        window.Show();
        Pump();
        window.Measure(new Size(520, 320));
        window.Arrange(new Rect(0, 0, 520, 320));
        Pump();
        SaveFrame(window, "/workspace/factory-confirm.png");
        window.Close();
    }
}
