using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
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
            Open(new ReportViewModel(vm.Diagnostics.ReportInputs, picture, from,
                () => UnsavedSettingsReport.Capture(vm.SettingsSession),
                new ProblemRecorder(vm.SettingsSession.Daemon,
                    clicks: OperatingSystem.IsMacOS() ? new MacClickWatcher() : null)));
        }
        finally { _opening = false; }
    }

    /// <summary>Shows the sheet for <paramref name="vm"/> (tests call this directly).</summary>
    internal static ReportWindow Open(ReportViewModel vm)
    {
        var w = new ReportWindow(vm);
        _open = w;
        w.Closed += (_, _) => { if (ReferenceEquals(_open, w)) _open = null; _ = vm.StopRecordingAsync(); };
        AppLog.Info($"Report: opened from {vm.From}.");
        InWindowSheet.Show(w);
        return w;
    }

    public ReportViewModel Model { get; }

    public ReportWindow(ReportViewModel vm)
    {
        Model = vm;
        Title = vm.Title;
        Width = 620; SizeToContent = SizeToContent.Height; CanResize = false;   // V30 #200: bigger (Zak 23 Nov)
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

        // Version 25 (Zak, 6 Nov 2026): optional email near the top, with Zak's own line. Send with file only.
        var email = new TextBox { Name = "ReportEmail", PlaceholderText = vm.EmailHint, Text = vm.Email, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        email.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) vm.Email = email.Text ?? ""; };
        Avalonia.Automation.AutomationProperties.SetName(email, vm.EmailLabel);

        var body = new StackPanel { Margin = new Thickness(24, 8, 24, 20), Spacing = 12 };
        // "How to report" (Zak, 17 Nov 2026): a short guide, shown and hidden by its button, at the top of the report.
        var howToButton = new Button { Name = "ReportHowTo", Content = vm.HowToLabel, Command = vm.ToggleHowToCommand, HorizontalAlignment = HorizontalAlignment.Left };
        var howToSteps = new StackPanel { Name = "ReportHowToSteps", Spacing = 4, IsVisible = vm.IsHowToOpen };
        foreach (var step in vm.HowToSteps)
            howToSteps.Children.Add(new TextBlock { Text = step, TextWrapping = TextWrapping.Wrap }.Fg());
        body.Children.Add(howToButton);
        body.Children.Add(howToSteps);
        body.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Name = "ReportEmailLabel", Text = vm.EmailLabel, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }.Fg(),
                new TextBlock { Name = "ReportEmailHelp", Text = vm.EmailHelp, TextWrapping = TextWrapping.Wrap }.Fg("TextSecondaryBrush"),
                email,
            },
        });
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

        var record = new Button { Name = "ReportRecord", Content = vm.RecordLabel, Command = vm.ToggleRecordCommand, IsVisible = vm.CanRecord };
        var recordStatus = new TextBlock { Name = "ReportRecordStatus", Text = vm.RecordStatus, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }.Fg("TextSecondaryBrush");
        var recordHint = new TextBlock { Name = "ReportRecordHint", Text = vm.RecordHint, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = vm.CanRecord }.Fg("TextSecondaryBrush");
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, IsVisible = vm.CanRecord, Children = { record, recordStatus } });
        body.Children.Add(recordHint);

        body.Children.Add(new TextBlock { Text = vm.Note, FontSize = 12, TextWrapping = TextWrapping.Wrap }.Fg("TextSecondaryBrush"));
        var status = new TextBlock { Name = "ReportStatus", Text = vm.Status, TextWrapping = TextWrapping.Wrap, IsVisible = vm.Status.Length > 0 }.Fg();
        body.Children.Add(status);

        // Version 29 (Zak, 17 Nov 2026): Send with file with the email box empty asks first, inside the sheet.
        vm.AskForEmail = true;
        var askAdd = new Button { Name = "ReportEmailAskAdd", Content = vm.AddEmailLabel, Padding = new Thickness(16, 8), Command = vm.AddEmailCommand };
        var askSend = new Button { Name = "ReportEmailAskSendWithout", Content = vm.SendWithoutEmailLabel, Padding = new Thickness(16, 8), Command = vm.SendWithoutEmailCommand };
        var ask = new StackPanel
        {
            Name = "ReportEmailAsk", Spacing = 8, IsVisible = vm.AskingEmail,
            Children =
            {
                new TextBlock { Name = "ReportEmailAskText", Text = vm.EmailQuestion, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }.Fg(),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { askAdd, askSend } },
            },
        };
        // V29 #156 (Zak 20 Nov, build 5: "Send with file report does not work if the email ... is not written"): the question
        // was added above the buttons, often out of sight in a long report, so pressing Send with file looked like it did
        // nothing. It now takes the buttons' place, framed, and is scrolled into view.
        var askFrame = new Border
        {
            Name = "ReportEmailAskFrame", Child = ask, IsVisible = vm.AskingEmail, Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2),
        };
        askFrame.Use(Border.BorderBrushProperty, "AccentBrush");
        body.Children.Add(askFrame);
        vm.FocusEmailRequested += () => email.Focus();

        var cancel = new Button { Name = "ReportCancel", Content = vm.CancelLabel, Padding = new Thickness(16, 8), Command = vm.CancelCommand };
        // Owner's request: feedback with or without a GitHub account. The file needs no account, so it comes first.
        var file = new Button { Name = "ReportSendFile", Content = vm.SendFileLabel, Padding = new Thickness(16, 8), Command = vm.SendFileCommand };
        var send = new Button { Name = "ReportSend", Content = vm.SendGitHubLabel, Padding = new Thickness(16, 8), Command = vm.SendCommand };
        var buttons = new StackPanel
        {
            Name = "ReportButtons", Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0), Children = { cancel, file, send }, IsVisible = !vm.AskingEmail,
        };
        body.Children.Add(buttons);

        // V30 #200 (Zak 23 Nov 2026: "make it simpler ... bigger ... the list of the steps numbered one under another";
        // team vote A1, A3, A5): after Send with file the sheet shows 3 big numbered steps. Step 1 has the big Open upload
        // page button, step 2 shows the file itself (it can be dragged onto the page), step 3 the name and email. The sheet
        // stays open until Done. Zak 23 Nov: the upload service is never named. Zak 23 Nov 17:42: Open upload page also shows
        // the file in Finder (A2), and "The page doesn't open?" shows the email address (A6).
        var openUpload = new Button
        {
            Name = "ReportOpenUpload", Content = vm.OpenUploadLabel, Padding = new Thickness(22, 10), FontSize = 15,
            FontWeight = FontWeight.SemiBold, Command = vm.OpenUploadCommand, HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (Application.Current?.TryFindResource("AccentButton", out var accent) == true && accent is Avalonia.Styling.ControlTheme accentTheme)
            openUpload.Theme = accentTheme;
        var fileName = new TextBlock { Name = "ReportFileName", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }.Fg();
        var fileFolder = new TextBlock { Name = "ReportFileFolder", FontSize = 12, TextWrapping = TextWrapping.Wrap }.Fg("TextSecondaryBrush");
        var fileTile = new Border
        {
            Name = "ReportFileTile", Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children = { FileIcon(), new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { fileName, fileFolder } } },
            },
        };
        fileTile.Use(Border.BorderBrushProperty, "GlassBorderBrush").Use(Border.BackgroundProperty, "BgSurfaceBrush");
        fileTile.PointerPressed += async (_, e) => await DragFileOutAsync(fileTile, e, vm.SavedPath);
        // V30 #200 A6: the upload page doesn't open (no internet, a blocked site) - email the file instead.
        var wontOpen = new Button
        {
            Name = "ReportPageWontOpen", Content = vm.PageWontOpenLabel, Command = vm.ShowEmailFallbackCommand, Classes = { "homeLink" },
            Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Left, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        wontOpen.Fg("TextSecondaryBrush");
        var copied = new TextBlock { Name = "ReportAddressCopied", Text = vm.AddressCopiedText, IsVisible = false,
            VerticalAlignment = VerticalAlignment.Center }.Fg("TextSecondaryBrush");
        var emailFallback = new StackPanel
        {
            Name = "ReportEmailFallback", Spacing = 6, IsVisible = false,
            Children =
            {
                new TextBlock { Name = "ReportEmailFallbackText", Text = vm.EmailFallbackText, TextWrapping = TextWrapping.Wrap }.Fg(),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10,
                    Children =
                    {
                        new SelectableTextBlock { Name = "ReportEmailAddress", Text = vm.FeedbackEmail, FontWeight = FontWeight.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center }.Fg(),
                        new Button { Name = "ReportCopyAddress", Content = vm.CopyAddressLabel, Command = vm.CopyAddressCommand,
                            Padding = new Thickness(12, 4) },
                        copied,
                    },
                },
            },
        };
        vm.CopyText = async t => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(t); };
        var done = new Button { Name = "ReportDone", Content = vm.DoneLabel, Padding = new Thickness(22, 10), FontSize = 15, Command = vm.DoneCommand };
        var savedAt = new SelectableTextBlock { Name = "ReportSavedAt", FontSize = 11, TextWrapping = TextWrapping.Wrap }.Fg("TextSecondaryBrush");
        // The whole message as one text for screen readers and the tests (not shown).
        var savedText = new SelectableTextBlock { Name = "ReportSavedText", IsVisible = false };
        var saved = new StackPanel
        {
            Name = "ReportSaved", Margin = new Thickness(24, 8, 24, 20), Spacing = 14, IsVisible = false,
            Children =
            {
                new TextBlock { Text = vm.SavedTitle, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }.Fg(),
                new TextBlock { Name = "ReportStepsIntro", Text = vm.StepsIntro, FontSize = 15, TextWrapping = TextWrapping.Wrap }.Fg(),
                Step("ReportStep1", 1, vm.Step1, openUpload, wontOpen, emailFallback),
                Step("ReportStep2", 2, vm.Step2, fileTile,
                    new TextBlock { Name = "ReportStep2Drag", Text = vm.Step2Drag, FontSize = 12, TextWrapping = TextWrapping.Wrap }.Fg("TextSecondaryBrush")),
                Step("ReportStep3", 3, vm.Step3,
                    new TextBlock { Name = "ReportStep3Note", Text = vm.Step3Note, FontSize = 12, TextWrapping = TextWrapping.Wrap }.Fg("TextSecondaryBrush")),
                new DockPanel
                {
                    Margin = new Thickness(0, 6, 0, 0),
                    Children =
                    {
                        Docked(done, Dock.Right),
                        new TextBlock { Name = "ReportDoneLine", Text = vm.DoneLine, FontSize = 15, FontWeight = FontWeight.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }.Fg(),
                    },
                },
                savedAt,
                savedText,
            },
        };
        void ShowSaved()
        {
            body.IsVisible = !vm.IsSaved;
            saved.IsVisible = vm.IsSaved;
            savedText.Text = vm.SavedMessage;
            fileName.Text = vm.SavedFileName;
            fileFolder.Text = vm.SavedFolderText;
            savedAt.Text = vm.SavedAtText;
            if (vm.IsSaved) openUpload.Focus();
        }
        Content = new Panel { Children = { body, saved } };

        vm.PickImages = () => PickAsync(body);
        vm.PickSaveFile = name => PickSaveAsync(body, vm.SaveDialogTitle, name);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReportViewModel.PicturesText)) count.Text = vm.PicturesText;
            if (e.PropertyName == nameof(ReportViewModel.IsHowToOpen)) howToSteps.IsVisible = vm.IsHowToOpen;
            if (e.PropertyName == nameof(ReportViewModel.RecordLabel)) record.Content = vm.RecordLabel;
            if (e.PropertyName == nameof(ReportViewModel.RecordStatus)) recordStatus.Text = vm.RecordStatus;
            if (e.PropertyName is nameof(ReportViewModel.IsRecording) or nameof(ReportViewModel.RecordStatus)) FollowRecording(vm);
            if (e.PropertyName == nameof(ReportViewModel.Status)) { status.Text = vm.Status; status.IsVisible = vm.Status.Length > 0; }
            if (e.PropertyName == nameof(ReportViewModel.SavedPath)) ShowSaved();
            if (e.PropertyName == nameof(ReportViewModel.UploadPageOpened) && vm.UploadPageOpened) done.Focus();
            if (e.PropertyName == nameof(ReportViewModel.EmailFallbackShown))
            {
                emailFallback.IsVisible = vm.EmailFallbackShown;
                wontOpen.IsVisible = !vm.EmailFallbackShown;
            }
            if (e.PropertyName == nameof(ReportViewModel.AddressCopied)) copied.IsVisible = vm.AddressCopied;
            if (e.PropertyName == nameof(ReportViewModel.AskingEmail))
            {
                ask.IsVisible = askFrame.IsVisible = vm.AskingEmail;
                buttons.IsVisible = !vm.AskingEmail;
                if (vm.AskingEmail)
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { askFrame.BringIntoView(); askAdd.Focus(); },
                        Avalonia.Threading.DispatcherPriority.Loaded);
            }
        };
        vm.Finished += _ => Close();
        Closed += (_, _) => { if (_aside) { _aside = false; InWindowSheet.HideBar(); } };
        Opened += (_, _) => text.Focus();
    }

    private bool _aside;

    /// <summary>V29 #167 (Zak 20 Nov ~09:46): while recording, the sheet steps aside so the app can be used (the radial menu settings,
    /// any page); a bar at the bottom of the window shows the recording and stops it. Stopping brings the report back.</summary>
    private void FollowRecording(ReportViewModel vm)
    {
        if (vm.IsRecording)
        {
            if (!_aside && InWindowSheet.StepAside(this, true))
            {
                _aside = true;
                AppLog.Info("Report: the report steps aside while recording, so the app can be used.");
            }
            if (_aside) InWindowSheet.ShowBar(vm.RecordStatus, vm.RecordLabel, () => vm.ToggleRecordCommand.Execute(null));
        }
        else if (_aside)
        {
            _aside = false;
            InWindowSheet.HideBar();
            InWindowSheet.StepAside(this, false);
        }
    }

    /// <summary>V30 #200: one numbered step - a big round number, the step's words, then what belongs to it.</summary>
    static Grid Step(string name, int n, string text, params Control[] extra)
    {
        var number = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), VerticalAlignment = VerticalAlignment.Top,
            // the theme's text-on-accent colour (rules R2/R6: no hard-coded white)
            Child = new TextBlock { Text = n.ToString(), FontSize = 17, FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                .Use(TextBlock.ForegroundProperty, "AccentButtonForegroundBrush"),
        };
        number.Use(Border.BackgroundProperty, "AccentBrush");
        var right = new StackPanel { Spacing = 8, Margin = new Thickness(14, 4, 0, 0) };
        right.Children.Add(new TextBlock { Name = name + "Text", Text = text, FontSize = 15, TextWrapping = TextWrapping.Wrap }.Fg());
        foreach (var c in extra) right.Children.Add(c);
        var g = new Grid { Name = name, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        g.Children.Add(number);
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return g;
    }

    static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }

    /// <summary>A plain file picture (a page with a folded corner and a zipper line) for the report file.</summary>
    static Control FileIcon()
    {
        var page = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 2,0 L 18,0 L 26,8 L 26,34 L 2,34 Z M 18,0 L 18,8 L 26,8"),
            StrokeThickness = 1.5, Width = 28, Height = 36, Stretch = Stretch.None,
        };
        page.Use(Avalonia.Controls.Shapes.Shape.StrokeProperty, "TextSecondaryBrush");
        page.Use(Avalonia.Controls.Shapes.Shape.FillProperty, "BgElevatedBrush");
        var zip = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 12,4 L 12,6 M 14,8 L 14,10 M 12,12 L 12,14 M 14,16 L 14,18 M 11,20 L 15,20 L 15,25 L 11,25 Z"),
            StrokeThickness = 1.5, Width = 28, Height = 36, Stretch = Stretch.None,
        };
        zip.Use(Avalonia.Controls.Shapes.Shape.StrokeProperty, "AccentBrush");
        return new Panel { Width = 28, Height = 36, VerticalAlignment = VerticalAlignment.Center, Children = { page, zip } };
    }

    /// <summary>V30 #200 (vote A5, Grok): the report file can be dragged from the sheet straight onto the upload page.</summary>
    static async Task DragFileOutAsync(Control from, Avalonia.Input.PointerPressedEventArgs e, string path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
        if (!e.GetCurrentPoint(from).Properties.IsLeftButtonPressed) return;
        try
        {
            var top = TopLevel.GetTopLevel(from);
            if (top == null) return;
            var file = await top.StorageProvider.TryGetFileFromPathAsync(new Uri(path));
            if (file == null) return;
            var data = new Avalonia.Input.DataTransfer();
            data.Add(Avalonia.Input.DataTransferItem.CreateFile(file));
            var result = await Avalonia.Input.DragDrop.DoDragDropAsync(e, data, Avalonia.Input.DragDropEffects.Copy);
            AppLog.Info($"Report: the report file was dragged out of the sheet ({result}).");
        }
        catch (Exception ex) { AppLog.Warn("Report: couldn't drag the report file out of the sheet.", ex); }
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
