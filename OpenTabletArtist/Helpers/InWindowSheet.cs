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
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;

    static Panel? Host(MainWindow? mw) => mw?.FindControl<Panel>(HostName);

    /// <summary>True when a sheet is open in the main window.</summary>
    public static bool IsOpen => _stack.Count > 0;

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
        if (!mw.IsVisible) mw.Show();
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
        };
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
            if (copy.Handled) e.Handled = true;
        }, RoutingStrategies.Tunnel);

        bool closedFired = false;
        dialog.Closed += (_, _) => closedFired = true;
        dialog.Closing += (_, e) =>
        {
            if (e.Cancel) return;
            Dispatcher.UIThread.Post(() =>
            {
                Detach(host, sheet);
                if (!closedFired) Invoke(dialog, "OnClosed", EventArgs.Empty);
                done.TrySetResult();
            });
        };

        if (_stack.TryPeek(out var below)) below.IsVisible = false;
        _stack.Push(sheet);
        host.Children.Add(sheet);
        host.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            Invoke(dialog, "OnOpened", EventArgs.Empty);
            var first = sheet.GetVisualDescendantsOfType<Button>().FirstOrDefault(b => b.IsDefault) as Control;
            first?.Focus();
        }, DispatcherPriority.Loaded);
        return done.Task;
    }

    static void Detach(Panel host, Control sheet)
    {
        host.Children.Remove(sheet);
        var rest = _stack.Where(s => !ReferenceEquals(s, sheet)).Reverse().ToList();
        _stack.Clear();
        foreach (var s in rest) _stack.Push(s);
        if (_stack.TryPeek(out var top)) top.IsVisible = true;
        host.IsVisible = host.Children.Count > 0;
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
