using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>Version 7: update prompts. Version 8: localised. Version 12: ready-to-install after auto-download.
/// Version 14: a choice between the stable version and a BETA (not tested yet), and going back to stable.</summary>
public static class UpdatePromptWindow
{
    public static Task<bool> AskAsync(string label) => ShowAsync(
        Localizer.T("update.prompt_title"),
        Localizer.Tf("update.prompt_heading", ("version", label)),
        Localizer.T("update.prompt_body"),
        Localizer.T("update.now"),
        Localizer.T("update.not_now"));

    /// <summary>After an automatic download: Version N is ready for installation — wait for OK.</summary>
    public static Task<bool> AskReadyAsync(string label) => ShowAsync(
        Localizer.T("update.ready_title"),
        Localizer.Tf("update.ready_heading", ("version", label)),
        Localizer.T("update.ready_body"),
        Localizer.T("app.ok"),
        Localizer.T("update.not_now"));

    /// <summary>Version 14: before installing a BETA.</summary>
    public static Task<bool> AskBetaAsync(string label) => ShowAsync(
        Localizer.T("update.beta_title"),
        Localizer.Tf("update.beta_heading", ("beta", label)),
        Localizer.T("update.beta_body"),
        Localizer.T("update.beta_install"),
        Localizer.T("update.not_now"));

    /// <summary>Version 14: before going back from a BETA to the stable version.</summary>
    public static Task<bool> AskGoBackAsync(string label) => ShowAsync(
        Localizer.T("update.go_back_title"),
        Localizer.Tf("update.go_back_heading", ("version", label)),
        Localizer.T("update.go_back_body"),
        Localizer.T("update.go_back_install"),
        Localizer.T("update.not_now"));

    /// <summary>Version 14: the choice window (pen menu). One button per offer, plus Not now.
    /// Returns the chosen release, or null.</summary>
    public static Task<Updater.Release?> ChooseAsync(Updater.UpdateChoices c)
    {
        var done = new TaskCompletionSource<Updater.Release?>();
        var w = NewWindow(Localizer.T("update.choose_title"));
        // Options stacked one per row (long localised labels fit), most usual first, Not now last.
        var buttons = new StackPanel { Spacing = 8 };
        var lines = new StackPanel { Spacing = 8 };
        void Offer(Updater.Release r, string buttonText, string line)
        {
            var b = new Button { Content = buttonText, Padding = new Thickness(16, 8), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Click += (_, _) => { done.TrySetResult(r); w.Close(); };
            buttons.Children.Add(b);
            lines.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#C8C8C8")) });
        }
        if (c.StableUpgrade is { } stable)
            Offer(stable, Localizer.Tf("update.install_stable", ("version", stable.Label)), Localizer.Tf("update.choose_stable_line", ("version", stable.Label)));
        if (c.Beta is { } beta)
            Offer(beta, Localizer.Tf("update.try_beta", ("beta", beta.Label)), Localizer.Tf("update.choose_beta_line", ("beta", beta.Label)));
        if (c.GoBack is { } back)
            Offer(back, Localizer.Tf("update.go_back", ("version", back.Label)), Localizer.Tf("update.choose_go_back_line", ("version", back.Label)));
        var no = new Button { Content = Localizer.T("update.not_now"), Padding = new Thickness(16, 8), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center };
        no.Click += (_, _) => { done.TrySetResult(null); w.Close(); };
        buttons.Children.Add(no);
        w.Closed += (_, _) => done.TrySetResult(null);
        w.Content = new StackPanel { Margin = new Thickness(24), Spacing = 14, Children = {
            Heading(Localizer.T("update.choose_heading")), lines, buttons } };
        w.Show();
        return done.Task;
    }

    static Window NewWindow(string title) => new()
    {
        Title = title, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark
    };

    static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#E4E4E4")),
        TextWrapping = TextWrapping.Wrap
    };

    static Task<bool> ShowAsync(string title, string heading, string body, string yesLabel, string noLabel)
    {
        var done = new TaskCompletionSource<bool>();
        var w = NewWindow(title);
        var yes = new Button { Content = yesLabel, Padding = new Thickness(16, 8) };
        var no = new Button { Content = noLabel, Padding = new Thickness(16, 8) };
        yes.Click += (_, _) => { done.TrySetResult(true); w.Close(); };
        no.Click += (_, _) => { done.TrySetResult(false); w.Close(); };
        w.Closed += (_, _) => done.TrySetResult(false);
        w.Content = new StackPanel { Margin = new Thickness(24), Spacing = 14, Children = {
            Heading(heading),
            new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#C8C8C8")) },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } } } };
        w.Show();
        return done.Task;
    }
}
