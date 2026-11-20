using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OpenTabletArtist.Helpers;

/// <summary>Version 19 (Zak's rule: no popup windows). Shows what used to be a separate dialog window as a
/// sheet inside the main window. The dialog object is still built as before (so its code, bindings, results and
/// Close() calls are unchanged); only its content is moved into the main window's <c>SheetHost</c> layer.
/// Falls back to a normal window when there is no main window to host it (tests, very early start).</summary>
public static class InWindowSheet
{
    public const string HostName = "SheetHost";
    static readonly Stack<Control> _stack = new();

    /// <summary>Tests: the main window to host sheets in (the app uses the desktop lifetime's main window).</summary>
    internal static Func<MainWindow?>? MainOverride;

    static MainWindow? Main() => MainOverride?.Invoke() ??
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow
        // Item 28: after a quiet login start desktop.MainWindow is still unset, but the window exists. Use it,
        // so setup and the manual open inside it rather than as their own windows.
        ?? AppMenu.Window;

    static Panel? Host(MainWindow? mw) => mw?.FindControl<Panel>(HostName);

    /// <summary>True when a sheet is open in the main window.</summary>
    public static bool IsOpen => _stack.Count > 0;

    // ── V29 #167 (Zak 20 Nov ~09:46: "I cant record report file because when recording is happening the radial menu settings are
    // not available because the app is held in recording mode"): while Record the problem runs, the report sheet steps aside so
    // the whole app can be used, and a small bar at the bottom of the window says it is recording and has Stop recording. ──
    static readonly Dictionary<Window, (Panel Host, Control Sheet)> _sheets = new();
    static readonly HashSet<Control> _aside = new();
    static Border? _bar;
    static TextBlock? _barText;

    /// <summary>Hide (<paramref name="aside"/> true) or show again the sheet that holds <paramref name="dialog"/>, without closing it.
    /// While it is aside the page behind can be used. False when the dialog isn't a sheet (it is its own window then).</summary>
    public static bool StepAside(Window dialog, bool aside)
    {
        if (!_sheets.TryGetValue(dialog, out var s)) return false;
        if (aside) _aside.Add(s.Sheet); else _aside.Remove(s.Sheet);
        bool onTop = _stack.TryPeek(out var top) && ReferenceEquals(top, s.Sheet);
        if (onTop) s.Sheet.IsVisible = !aside;
        s.Host.IsVisible = _stack.Any(x => !_aside.Contains(x));
        return true;
    }

    /// <summary>Show (or update) the recording bar at the bottom of the main window; <paramref name="stop"/> runs on its button.</summary>
    public static void ShowBar(string text, string stopLabel, Action stop)
    {
        var mw = Main();
        if (mw?.FindControl<Panel>(HostName)?.Parent is not Panel root) return;
        if (_bar == null)
        {
            _barText = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 }.Fg("TextPrimaryBrush");
            var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.Parse("#E5484D")), VerticalAlignment = VerticalAlignment.Center };
            var button = new Button { Name = "RecordingBarStop", Content = stopLabel, Padding = new Thickness(12, 6), VerticalAlignment = VerticalAlignment.Center };
            button.Click += (_, _) => stop();
            _bar = new Border
            {
                Name = "RecordingBar", ZIndex = 1001, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16),
                BorderThickness = new Thickness(1), BoxShadow = BoxShadows.Parse("0 4 16 0 #80000000"),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { dot, _barText, button } },
            };
            _bar.Use(Border.BackgroundProperty, "BgElevatedBrush").Use(Border.BorderBrushProperty, "GlassBorderBrush");
            root.Children.Add(_bar);
        }
        else if (_barText != null) _barText.Text = text;
    }

    public static void HideBar()
    {
        if (_bar?.Parent is Panel p) p.Children.Remove(_bar);
        _bar = null;
        _barText = null;
    }

    /// <summary>The recording bar (tests).</summary>
    internal static Border? Bar => _bar;

    /// <summary>Modal: completes when the dialog closes (its Close() is called and not cancelled).</summary>
    public static Task ShowDialogAsync(Window dialog, Window? owner = null)
    {
        var mw = Main();
        var host = Host(mw);
        if (mw is null || host is null || ReferenceEquals(dialog, mw))
            return owner is { IsVisible: true } ? dialog.ShowDialog(owner) : ShowWindowAsync(dialog);
        return Attach(dialog, mw, host);
    }

    /// <summary>Non-modal: shows the dialog in the main window and returns at once.</summary>
    public static void Show(Window dialog) => _ = ShowDialogAsync(dialog);

    static Task ShowWindowAsync(Window w)
    {
        var tcs = new TaskCompletionSource();
        w.Closed += (_, _) => tcs.TrySetResult();
        w.Show();
        return tcs.Task;
    }

    static Task Attach(Window dialog, MainWindow mw, Panel host)
    {
        var done = new TaskCompletionSource();
        // Item 9 (#17): remember what had keyboard focus on the page, to give it back when the last sheet closes.
        var focusBefore = _stack.Count == 0 ? mw.FocusManager?.GetFocusedElement() as Control : null;
        // Item 28: BringToFront (not a bare Show) so a hidden window after a quiet login gets its Dock icon and
        // becomes desktop.MainWindow too.
        if (!mw.IsVisible) mw.BringToFront("in-window sheet opened", byUser: true);
        mw.Activate();

        var content = dialog.Content as Control ?? new TextBlock { Text = dialog.Content?.ToString() ?? "" };
        var dataContext = dialog.DataContext;
        dialog.Content = null;
        if (dataContext != null && content.DataContext == null) content.DataContext = dataContext;

        var close = new Button { Content = "×", FontSize = 18, Padding = new Thickness(10, 0), Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => dialog.Close();
        close.Fg("TextSecondaryBrush");
        var title = new TextBlock { Text = dialog.Title ?? "", FontSize = 15, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }.Fg("TextPrimaryBrush");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 10, 8, 0) };
        header.Children.Add(title); Grid.SetColumn(close, 1); header.Children.Add(close);

        var body = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        double hostH = Math.Max(300, (mw.Bounds.Height > 0 ? mw.Bounds.Height : 720) - 48);
        bool fixedHeight = !double.IsNaN(dialog.Height) && dialog.SizeToContent is SizeToContent.Manual or SizeToContent.Width;
        body.Children.Add(fixedHeight ? content : new ScrollViewer { Content = content, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });

        var sheet = new Border
        {
            Name = "Sheet",
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), BoxShadow = BoxShadows.Parse("0 8 32 0 #80000000"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MaxHeight = hostH, MaxWidth = Math.Max(400, (mw.Bounds.Width > 0 ? mw.Bounds.Width : 1100) - 48),
            Child = body,
            Focusable = true,
        };
        // Item 9 (#17): Tab stays inside the sheet instead of walking into the page behind it.
        KeyboardNavigation.SetTabNavigation(sheet, KeyboardNavigationMode.Cycle);
        // Always the app theme (never the dialog window's own background, which follows macOS, not the app).
        sheet.Use(Border.BackgroundProperty, "BgElevatedBrush").Use(Border.BorderBrushProperty, "GlassBorderBrush");
        sheet.Use(Avalonia.Controls.Documents.TextElement.ForegroundProperty, "TextPrimaryBrush");
        if (!double.IsNaN(dialog.Width)) sheet.Width = dialog.Width;
        else if (dialog.MinWidth > 0) sheet.MinWidth = dialog.MinWidth;
        if (fixedHeight) sheet.Height = Math.Min(dialog.Height, hostH);

        // Carry the window's own resources, styles and key bindings over to the sheet.
        foreach (var kv in dialog.Resources.ToList()) sheet.Resources[kv.Key] = kv.Value;
        foreach (var md in dialog.Resources.MergedDictionaries.ToList()) sheet.Resources.MergedDictionaries.Add(md);
        var styles = dialog.Styles.ToList(); dialog.Styles.Clear(); sheet.Styles.AddRange(styles);
        var keys = dialog.KeyBindings.ToList(); dialog.KeyBindings.Clear(); sheet.KeyBindings.AddRange(keys);

        // Keys typed in the sheet reach the dialog's own key handlers (Escape, hotkey capture, …).
        sheet.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Handled) return;
            var copy = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = e.Key, KeyModifiers = e.KeyModifiers,
                PhysicalKey = e.PhysicalKey, KeySymbol = e.KeySymbol, Source = dialog };
            dialog.RaiseEvent(copy);
            if (copy.Handled) { e.Handled = true; return; }
            // Item 10 (#17): ⌘S belongs to the page behind (Save settings); it must not save while a sheet is open.
            if (IsPageSaveGesture(e.Key, e.KeyModifiers) && !sheet.KeyBindings.Any(k => k.Gesture?.Matches(e) == true))
                e.Handled = true;
        }, RoutingStrategies.Tunnel);

        bool closedFired = false;
        dialog.Closed += (_, _) => closedFired = true;
        dialog.Closing += (_, e) =>
        {
            if (e.Cancel) return;
            Dispatcher.UIThread.Post(() =>
            {
                _sheets.Remove(dialog);
                Detach(host, sheet);
                RestoreFocus(focusBefore);
                if (!closedFired) Invoke(dialog, "OnClosed", EventArgs.Empty);
                done.TrySetResult();
            });
        };

        if (_stack.TryPeek(out var below)) below.IsVisible = false;
        _stack.Push(sheet);
        host.IsVisible = true;   // V29 #167: a new sheet shows even while the report is aside
        _sheets[dialog] = (host, sheet);
        dialog.Closed += (_, _) => _sheets.Remove(dialog);
        host.Children.Add(sheet);
        host.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            Invoke(dialog, "OnOpened", EventArgs.Empty);
            FocusInitial(sheet);
        }, DispatcherPriority.Loaded);
        return done.Task;
    }

    /// <summary>The ⌘S gesture the main window binds to Save settings.</summary>
    internal static bool IsPageSaveGesture(Key key, KeyModifiers mods) => key == Key.S && mods == KeyModifiers.Meta;

    /// <summary>Item 9 (#17): keyboard focus moves into the sheet: its default button, else the first text box,
    /// else the first control that takes focus, else the sheet itself, so keys never land on the page behind.</summary>
    internal static void FocusInitial(Control sheet)
    {
        var all = sheet.GetVisualDescendantsOfType<Control>()
            .Where(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled).ToList();
        Control? target = all.OfType<Button>().FirstOrDefault(b => b.IsDefault)
            ?? all.OfType<TextBox>().FirstOrDefault() as Control
            ?? all.FirstOrDefault(c => c is not ScrollViewer)
            ?? sheet;
        if (!target.Focus()) sheet.Focus();
    }

    /// <summary>When a sheet closes: focus the sheet below it, or (last sheet) what had focus on the page.</summary>
    static void RestoreFocus(Control? before)
    {
        if (_stack.TryPeek(out var top)) { FocusInitial(top); return; }
        if (before is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } && TopLevel.GetTopLevel(before) != null) before.Focus();
    }

    static void Detach(Panel host, Control sheet)
    {
        host.Children.Remove(sheet);
        var rest = _stack.Where(s => !ReferenceEquals(s, sheet)).Reverse().ToList();
        _stack.Clear();
        foreach (var s in rest) _stack.Push(s);
        _aside.Remove(sheet);
        // V29 #167: a sheet that stepped aside (the report while recording) stays hidden when one opened over it closes.
        if (_stack.TryPeek(out var top)) top.IsVisible = !_aside.Contains(top);
        host.IsVisible = _stack.Any(x => !_aside.Contains(x));
    }

    static void Invoke(Window w, string method, EventArgs e)
    {
        try
        {
            typeof(Window).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic, new[] { typeof(EventArgs) })?.Invoke(w, new object[] { e });
        }
        catch (Exception ex) { Services.AppLog.Warn($"Sheet: {method} failed.", ex); }
    }
}

static class VisualExt
{
    public static IEnumerable<T> GetVisualDescendantsOfType<T>(this Avalonia.Visual v) where T : Avalonia.Visual
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v).OfType<T>();
}
