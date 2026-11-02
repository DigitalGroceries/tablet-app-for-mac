using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenTabletArtist.Services;
using OpenTabletArtist.Helpers;

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
            lines.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap }.Fg("TextSecondaryBrush"));
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
        OpenTabletArtist.Helpers.InWindowSheet.Show(w);
        return done.Task;
    }

    /// <summary>Version 15 (Zak: "All versions stable or beta should allow the users to downgrade to an earlier
    /// version"): one "Go back to Version N" button per earlier stable version, plus Not now.
    /// Returns the chosen release, or null.</summary>
    public static Task<Updater.Release?> ChooseEarlierAsync(System.Collections.Generic.IReadOnlyList<Updater.Release> earlier)
    {
        var done = new TaskCompletionSource<Updater.Release?>();
        var w = NewWindow(Localizer.T("update.earlier_title"));
        var buttons = new StackPanel { Spacing = 8 };
        foreach (var r in earlier)
        {
            var b = new Button { Content = Localizer.Tf("update.go_back", ("version", r.Label)), Padding = new Thickness(16, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Click += (_, _) => { done.TrySetResult(r); w.Close(); };
            buttons.Children.Add(b);
        }
        var no = new Button { Content = Localizer.T("update.not_now"), Padding = new Thickness(16, 8), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center };
        no.Click += (_, _) => { done.TrySetResult(null); w.Close(); };
        buttons.Children.Add(no);
        w.Closed += (_, _) => done.TrySetResult(null);
        w.Content = new StackPanel { Margin = new Thickness(24), Spacing = 14, Children = {
            Heading(Localizer.T("update.earlier_heading")),
            new TextBlock { Text = Localizer.T("update.earlier_body"), TextWrapping = TextWrapping.Wrap }.Fg("TextSecondaryBrush"),
            buttons } };
        OpenTabletArtist.Helpers.InWindowSheet.Show(w);
        return done.Task;
    }

    /// <summary>Zak (2 Nov): while an app update downloads, a note in the window under the download status
    /// thanks everyone who donated. It closes by itself when the download ends.</summary>
    public const string DonorThanks = "We are grateful to all who donated and keep this project alive.";
    static Window? _downloadNote;

    public static void SyncDownloadNote(bool downloading)
    {
        if (downloading && _downloadNote == null)
        {
            var w = NewWindow(Localizer.T("update.downloading"));
            w.Content = new StackPanel { Margin = new Thickness(24), Spacing = 14, Children = {
                Heading(Localizer.T("update.downloading")),
                new ProgressBar { IsIndeterminate = true, Height = 4 },
                new TextBlock { Text = Localizer.Te("update.donor_thanks", DonorThanks), TextWrapping = TextWrapping.Wrap,
                    FontStyle = FontStyle.Italic }.Fg("TextSecondaryBrush") } };
            w.Closed += (_, _) => { if (ReferenceEquals(_downloadNote, w)) _downloadNote = null; };
            _downloadNote = w;
            OpenTabletArtist.Helpers.InWindowSheet.Show(w);
        }
        else if (!downloading && _downloadNote is { } open)
        {
            _downloadNote = null;
            open.Close();
        }
    }

    internal static bool DownloadNoteOpen => _downloadNote != null;

    /// <summary>Remembers the version that last ran, so the first run after an update can say thank you.</summary>
    public const string LastRunKey = "update.lastRunVersion";

    /// <summary>Zak (2 Nov): the download is done by the OLD app, which (before Version 21) has no thank-you
    /// note. So the new version also thanks donors on its first run after an update. Pure, unit-tested:
    /// true when a different version ran before, or (no record yet) when the app has been used before.</summary>
    public static bool ShouldThankAfterUpdate(string? lastRun, string current, bool usedBefore) =>
        lastRun == null ? usedBefore : lastRun != current;

    /// <summary>Shows the thank-you once after an update; records this version. Not on a quiet login start
    /// (then it waits for a normal start).</summary>
    public static void ThankAfterUpdateIfNeeded(bool quietStart)
    {
        try
        {
            var current = Updater.CurrentBeta > 0 ? $"{Updater.Current}-beta{Updater.CurrentBeta}" : Updater.Current.ToString();
            var last = AppSettings.Get(LastRunKey);
            var usedBefore = System.IO.File.Exists(System.IO.Path.Combine(AppPaths.LocalAppData, "settings.json"));
            if (!ShouldThankAfterUpdate(last, current, usedBefore)) { if (last == null) AppSettings.Set(LastRunKey, current); return; }
            if (quietStart) return;
            AppSettings.Set(LastRunKey, current);
            var label = Updater.CurrentBeta > 0 ? $"{Updater.Current} BETA {Updater.CurrentBeta}" : Updater.Current.ToString();
            ShowThanks(label);
        }
        catch (Exception ex) { AppLog.Warn("Thank-you after update couldn't be shown.", ex); }
    }

    internal static Window ShowThanks(string label)
    {
        var w = NewWindow(Localizer.Te("update.thanks_title", "Thank you"));
        var ok = new Button { Content = Localizer.Te("update.thanks_ok", "OK"), Padding = new Thickness(16, 8), IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => w.Close();
        w.Content = new StackPanel { Margin = new Thickness(24), Spacing = 14, Children = {
            Heading(Localizer.Tf("home.version", ("version", label))),
            new TextBlock { Text = Localizer.Te("update.donor_thanks", DonorThanks), TextWrapping = TextWrapping.Wrap,
                FontStyle = FontStyle.Italic }.Fg("TextSecondaryBrush"),
            ok } };
        OpenTabletArtist.Helpers.InWindowSheet.Show(w);
        return w;
    }

    /// <summary>Shows the note whenever <paramref name="state"/> downloads (any update path: Update now, Try BETA,
    /// Go back, pen menu, automatic).</summary>
    public static void WireDownloadNote(AppUpdateState state) =>
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AppUpdateState.IsDownloading)) return;
            var on = state.IsDownloading;
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) SyncDownloadNote(on);
            else Avalonia.Threading.Dispatcher.UIThread.Post(() => SyncDownloadNote(on));
        };

    static Window NewWindow(string title) => new()
    {
        Title = title, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterScreen
    };

    static TextBlock Heading(string text) => new TextBlock
    {
        Text = text, FontSize = 20, FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap
    }.Fg("TextPrimaryBrush");

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
            new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap }.Fg("TextSecondaryBrush"),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } } } };
        OpenTabletArtist.Helpers.InWindowSheet.Show(w);
        return done.Task;
    }
}
