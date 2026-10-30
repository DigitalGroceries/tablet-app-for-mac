using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenTabletArtist.Views;

/// <summary>Build P: the one-time "start automatically or manually?" question, in a plain Wacom-like dialog
/// (grey panel, icon left, text, buttons bottom right, default button last). Original drawing, no Wacom art.</summary>
public sealed class AutoStartDialog : AppWindow
{
    public const string Question = "Start Tablet App for Mac automatically when you log in?";
    public const string Explanation = "Your tablet only works while Tablet App for Mac is running. If you choose Manually, open Tablet App for Mac each time before using your tablet.";

    /// <summary>True = Start automatically, false = Manually, null = closed without answering.</summary>
    public bool? Result { get; private set; }

    public Button AutoButton { get; }
    public Button ManualButton { get; }

    public AutoStartDialog()
    {
        Title = Services.Localizer.T("app.name");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var icon = new Canvas
        {
            Width = 48, Height = 48,
            Children =
            {
                new Rectangle { Width = 48, Height = 34, RadiusX = 5, RadiusY = 5, [Canvas.TopProperty] = 7.0, Fill = new SolidColorBrush(Color.Parse("#3A3A3A")) },
                new Rectangle { Width = 28, Height = 24, [Canvas.LeftProperty] = 15.0, [Canvas.TopProperty] = 12.0, Fill = new SolidColorBrush(Color.Parse("#6A6A6A")) },
                new Ellipse { Width = 8, Height = 8, [Canvas.LeftProperty] = 4.0, [Canvas.TopProperty] = 20.0, Stroke = OpenTabletArtist.Services.UiColors.SoftWhiteBrush, StrokeThickness = 1.5 },
            },
        };

        ManualButton = new Button { Content = Services.Localizer.T("autostart.no"), MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
        AutoButton = new Button { Content = Services.Localizer.T("autostart.yes"), MinWidth = 150, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        AutoButton.Classes.Add("accent");
        ManualButton.Click += (_, _) => { Result = false; Close(); };
        AutoButton.Click += (_, _) => { Result = true; Close(); };

        Content = new Border
        {
            Padding = new Thickness(20, 18, 20, 16),
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("Auto,16,*"),
                        Children =
                        {
                            icon,
                            new StackPanel
                            {
                                [Grid.ColumnProperty] = 2, Spacing = 8,
                                Children =
                                {
                                    new TextBlock { Text = Services.Localizer.T("autostart.title"), FontWeight = FontWeight.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap },
                                    new TextBlock { Text = Services.Localizer.T("autostart.body"), FontSize = 13, TextWrapping = TextWrapping.Wrap },
                                    new TextBlock { Text = Services.Localizer.T("autostart.later_hint"), FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
                                },
                            },
                        },
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10,
                        Children = { ManualButton, AutoButton },
                    },
                },
            },
        };
        Opened += (_, _) => AutoButton.Focus();
    }

    /// <summary>Ask once (if needed) and apply the answer; shows the fallback message if macOS refuses.</summary>
    public static async Task AskIfNeededAsync(Services.AutoStartCoordinator coordinator, Window? owner)
    {
        if (!coordinator.ShouldPrompt) return;
        var dlg = new AutoStartDialog();
        await OpenTabletArtist.Helpers.InWindowSheet.ShowDialogAsync(dlg, owner);
        if (dlg.Result is not { } automatic) return;   // closed without answering: ask again next launch
        if (coordinator.Choose(automatic) is { } message)
        {
            await Helpers.Dialogs.ShowMessageAsync(Services.Localizer.T("startup.section"), message);
            coordinator.Service.OpenLoginItemsSettings();
        }
    }
}
