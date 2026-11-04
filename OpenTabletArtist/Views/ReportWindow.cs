using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Version 22 BETA 3 (owner's request, 2 Nov 2026): the "Report a problem or send a recommendation" sheet. The same sheet opens
/// from the app window (Home › Feedback, the tablet page's bottom bar), the pen icon menu and the app menu
/// (top left). It shows inside the main window like every other dialog (Version 19: no popup windows).
/// </summary>
public sealed class ReportWindow : Window
{
    static ReportWindow? _open;
    static bool _opening;

    /// <summary>True while the report sheet is open.</summary>
    public static bool IsOpen => _open != null;

    /// <summary>How long to wait after showing the window before its picture is taken (a hidden window needs a
    /// moment to draw). Tests set it to zero.</summary>
    internal static TimeSpan PictureDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Opens the report from <paramref name="from"/> ("Home", "tablet page", "pen menu", "app menu"):
    /// brings the main window forward, takes its picture, then shows the sheet in it.</summary>
    public static async Task OpenAsync(string from)
    {
        if (_open != null || _opening) return;
        var mw = AppMenu.Window
                 ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
        if (mw?.DataContext is not MainViewModel vm) return;
        _opening = true;
        try
        {
            mw.BringToFront(from + " Report a problem", byUser: true);
            if (PictureDelay > TimeSpan.Zero) await Task.Delay(PictureDelay);
            // The picture is taken before the sheet covers the page, so it shows what the user was looking at.
            var picture = mw.CaptureReportPicture();
            Open(new ReportViewModel(vm.Diagnostics.ReportInputs, picture, from));
        }
        finally { _opening = false; }
    }

    /// <summary>Shows the sheet for <paramref name="vm"/> (tests call this directly).</summary>
    internal static ReportWindow Open(ReportViewModel vm)
    {
        var w = new ReportWindow(vm);
        _open = w;
        w.Closed += (_, _) => { if (ReferenceEquals(_open, w)) _open = null; };
        AppLog.Info($"Report: opened from {vm.From}.");
        InWindowSheet.Show(w);
        return w;
    }

    public ReportViewModel Model { get; }

    public ReportWindow(ReportViewModel vm)
    {
        Model = vm;
        Title = vm.Title;
        Width = 560; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var problem = new RadioButton { Name = "ReportProblem", Content = vm.ProblemLabel, GroupName = "ReportKind", IsChecked = !vm.IsRecommendation };
        var idea = new RadioButton { Name = "ReportRecommendation", Content = vm.RecommendationLabel, GroupName = "ReportKind", IsChecked = vm.IsRecommendation };
        idea.IsCheckedChanged += (_, _) => vm.IsRecommendation = idea.IsChecked == true;

        var text = new TextBox
        {
            Name = "ReportText", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 130,
            PlaceholderText = vm.DescriptionHint, Text = vm.Description,
        };
        // The Text property itself (TextChanged is raised later, after the dispatcher runs).
        text.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) vm.Description = text.Text ?? ""; };
        Avalonia.Automation.AutomationProperties.SetName(text, vm.DescriptionLabel);

        var body = new StackPanel { Margin = new Thickness(24, 8, 24, 20), Spacing = 12 };
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { problem, idea } });
        body.Children.Add(new TextBlock { Text = vm.DescriptionLabel, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }.Fg());
        body.Children.Add(text);

        if (vm.HasWindowPicture)
        {
            var include = new CheckBox { Name = "ReportWindowPicture", Content = vm.WindowPictureLabel, IsChecked = vm.IncludeWindowPicture };
            include.IsCheckedChanged += (_, _) => vm.IncludeWindowPicture = include.IsChecked == true;
            var thumb = new Border
            {
                CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Left, MaxHeight = 150, MaxWidth = 260,
                Child = new Image { Source = LoadThumb(vm.WindowPicturePath!), Stretch = Stretch.Uniform },
            };
            thumb.Use(Border.BorderBrushProperty, "GlassBorderBrush");
            body.Children.Add(new StackPanel { Spacing = 6, Children = { thumb, include } });
        }

        var add = new Button { Name = "ReportAddScreenshots", Content = vm.AddScreenshotsLabel, Command = vm.AddScreenshotsCommand };
        var count = new TextBlock { Name = "ReportPictures", Text = vm.PicturesText, VerticalAlignment = VerticalAlignment.Center }.Fg("TextSecondaryBrush");
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { add, count } });

        body.Children.Add(new TextBlock { Text = vm.Note, FontSize = 12, TextWrapping = TextWrapping.Wrap }.Fg("TextSecondaryBrush"));
        var status = new TextBlock { Name = "ReportStatus", Text = vm.Status, TextWrapping = TextWrapping.Wrap, IsVisible = vm.Status.Length > 0 }.Fg();
        body.Children.Add(status);

        var cancel = new Button { Name = "ReportCancel", Content = vm.CancelLabel, Padding = new Thickness(16, 8), Command = vm.CancelCommand };
        // Owner's request: feedback with or without a GitHub account. The file needs no account, so it comes first.
        var file = new Button { Name = "ReportSendFile", Content = vm.SendFileLabel, Padding = new Thickness(16, 8), Command = vm.SendFileCommand };
        var send = new Button { Name = "ReportSend", Content = vm.SendGitHubLabel, Padding = new Thickness(16, 8), Command = vm.SendCommand };
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0), Children = { cancel, file, send },
        });

        // Version 23 (owner's request, 4 Nov 2026): after "Send with file" the sheet says where the file is and where to
        // email it, instead of closing with no word. The text can be selected, so the address can be copied.
        var savedText = new SelectableTextBlock { Name = "ReportSavedText", TextWrapping = TextWrapping.Wrap }.Fg();
        var showFile = new Button { Name = "ReportShowFile", Content = vm.ShowFileLabel, Padding = new Thickness(16, 8), Command = vm.ShowFileCommand };
        var done = new Button { Name = "ReportDone", Content = vm.CloseLabel, Padding = new Thickness(16, 8), Command = vm.DoneCommand };
        var saved = new StackPanel
        {
            Name = "ReportSaved", Margin = new Thickness(24, 8, 24, 20), Spacing = 12, IsVisible = false,
            Children =
            {
                new TextBlock { Text = vm.SavedTitle, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }.Fg(),
                savedText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 4, 0, 0), Children = { showFile, done },
                },
            },
        };
        void ShowSaved()
        {
            body.IsVisible = !vm.IsSaved;
            saved.IsVisible = vm.IsSaved;
            savedText.Text = vm.SavedMessage;
            if (vm.IsSaved) done.Focus();
        }
        Content = new Panel { Children = { body, saved } };

        vm.PickImages = () => PickAsync(body);
        vm.PickSaveFile = name => PickSaveAsync(body, vm.SaveDialogTitle, name);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReportViewModel.PicturesText)) count.Text = vm.PicturesText;
            if (e.PropertyName == nameof(ReportViewModel.Status)) { status.Text = vm.Status; status.IsVisible = vm.Status.Length > 0; }
            if (e.PropertyName == nameof(ReportViewModel.SavedPath)) ShowSaved();
        };
        vm.Finished += _ => Close();
        Opened += (_, _) => text.Focus();
    }

    static Bitmap? LoadThumb(string path)
    {
        try { return new Bitmap(path); }
        catch (Exception ex) { AppLog.Debug("Report: couldn't show the window picture.", ex); return null; }
    }

    static readonly FilePickerFileType ZipType =
        new("Zip archive") { Patterns = new[] { "*.zip" }, AppleUniformTypeIdentifiers = new[] { "public.zip-archive" } };

    /// <summary>The macOS save panel, starting on the Desktop with the suggested name. Null when the user closes it.</summary>
    static async Task<string?> PickSaveAsync(Control anchor, string title, string suggestedName)
    {
        var top = TopLevel.GetTopLevel(anchor) ?? AppMenu.Window;
        if (top == null) return null;
        var desktop = await top.StorageProvider.TryGetFolderFromPathAsync(DiagnosticBundle.DefaultFolder());
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = desktop,
            DefaultExtension = "zip",
            FileTypeChoices = new[] { ZipType },
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    static async Task<IReadOnlyList<string>> PickAsync(Control anchor)
    {
        var top = TopLevel.GetTopLevel(anchor) ?? AppMenu.Window;
        if (top == null) return new List<string>();
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
        });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
    }
}
