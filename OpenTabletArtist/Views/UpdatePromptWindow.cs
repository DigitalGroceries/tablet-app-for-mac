using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>Version 7: update prompts. Version 8: localised. Version 12: ready-to-install after auto-download.</summary>
public static class UpdatePromptWindow
{
    public static Task<bool> AskAsync(int version) => ShowAsync(
        Localizer.T("update.prompt_title"),
        Localizer.Tf("update.prompt_heading", ("version", version)),
        Localizer.T("update.prompt_body"),
        Localizer.T("update.now"),
        Localizer.T("update.not_now"));

    /// <summary>After an automatic download: Version N is ready for installation — wait for OK.</summary>
    public static Task<bool> AskReadyAsync(int version) => ShowAsync(
        Localizer.T("update.ready_title"),
        Localizer.Tf("update.ready_heading", ("version", version)),
        Localizer.T("update.ready_body"),
        Localizer.T("app.ok"),
        Localizer.T("update.not_now"));

    static Task<bool> ShowAsync(string title, string heading, string body, string yesLabel, string noLabel)
    {
        var done = new TaskCompletionSource<bool>();
        var w = new Window { Title = title, Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        var yes = new Button { Content = yesLabel, Padding = new Thickness(16, 8) };
        var no = new Button { Content = noLabel, Padding = new Thickness(16, 8) };
        yes.Click += (_, _) => { done.TrySetResult(true); w.Close(); };
        no.Click += (_, _) => { done.TrySetResult(false); w.Close(); };
        w.Closed += (_, _) => done.TrySetResult(false);
        w.Content = new StackPanel { Margin = new Thickness(24), Spacing = 14, Children = {
            new TextBlock { Text = heading, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#E4E4E4")), TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#C8C8C8")) },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } } } };
        w.Show();
        return done.Task;
    }
}
