using System;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;

namespace OpenTabletArtist;

/// <summary>Version 29: Home's "Setting search" — the box's width, and opening a chosen place: its page,
/// tab and sub-tab, then the words, marked for a moment.</summary>
public partial class MainWindow
{
    /// <summary>How long the chosen words stay marked.</summary>
    internal static readonly TimeSpan MarkTime = TimeSpan.FromSeconds(1.8);

    private SettingSearchViewModel? _searchSub;

    private void WireSearch(MainViewModel? vm)
    {
        if (_searchSub != null) _searchSub.PlaceChosen -= OnSearchPlaceChosen;
        _searchSub = vm?.Search;
        if (_searchSub != null) _searchSub.PlaceChosen += OnSearchPlaceChosen;
    }

    /// <summary>The box runs from Home's left edge to Advanced's right edge.</summary>
    private void FitSearchToTabs()
    {
        double left = double.MaxValue, right = double.MinValue;
        foreach (var tab in NavTabs.GetVisualDescendants().OfType<RadioButton>().Where(r => r.IsVisible))
        {
            if (tab.TranslatePoint(default, NavTabs) is not { } at) continue;
            left = Math.Min(left, at.X);
            right = Math.Max(right, at.X + tab.Bounds.Width);
        }
        if (right <= left) return;
        var width = right - left;
        if (double.IsNaN(SettingSearchRow.Width) || Math.Abs(SettingSearchRow.Width - width) > 0.5
            || Math.Abs(SettingSearchRow.Margin.Left - left) > 0.5)
        {
            SettingSearchRow.Width = width;
            SettingSearchRow.Margin = new Thickness(left, 6, 0, 4);
        }
    }

    private void OnSearchPlaceChosen(SettingSearchEntry entry) => OpenSearchPlace(entry);

    /// <summary>Open the page, tab and sub-tab of an index entry, then mark its words.</summary>
    internal void OpenSearchPlace(SettingSearchEntry entry)
    {
        if (DataContext is not MainViewModel vm) return;
        if (!SettingSearchRoute.TryParse(entry.Route, out var route) || !vm.OpenSearchPage(route))
        {
            AppLog.Warn($"Setting search: unknown place '{entry.Route}' for '{entry.Text}'");
            return;
        }
        // The page is built on the next layout pass; open its sub-tab after that, then mark the words.
        Dispatcher.UIThread.Post(() =>
        {
            var opened = OpenSubPlace(route);
            Dispatcher.UIThread.Post(() => MarkWords(entry, opened), DispatcherPriority.Background);
        }, DispatcherPriority.Loaded);
    }

    private bool OpenSubPlace(SettingSearchRoute route)
    {
        UpdateLayout();   // the new page's controls exist only after a layout pass
        var content = PageContent();
        if (content is null) return false;
        if (route.Page == "tablet")
        {
            var view = content.GetVisualDescendants().OfType<TabletDetailView>().FirstOrDefault(v => v.IsEffectivelyVisible);
            if (view is null)
            {
                AppLog.Info("Setting search: the Tablet page has no tablet to show, so it opened as it is.");
                return false;
            }
            return view.OpenSearchPlace(route.Tab!, route.Sub!, route.Panel);
        }
        if (route.Sub is null) return true;
        var sub = content.GetVisualDescendants().OfType<RadioButton>().FirstOrDefault(r => r.Name == route.Sub);
        if (sub is null) return false;
        sub.IsChecked = true;
        return true;
    }

    private Control? PageContent() =>
        this.GetVisualDescendants().OfType<TransitioningContentControl>().FirstOrDefault();

    /// <summary>Scroll the words into view and draw a frame round them that goes after <see cref="MarkTime"/>.
    /// Nothing is marked (and the log says so) when the words aren't on the page now, for example a card that
    /// only shows in another state.</summary>
    private void MarkWords(SettingSearchEntry entry, bool opened, int tries = 3)
    {
        UpdateLayout();
        var content = PageContent();
        var words = content?.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.IsEffectivelyVisible && Same(Shown(t), entry.Text));
        if (words is null && tries > 0)
        {
            // A card that fills in a moment later (it reads a file or asks the driver first).
            DispatcherTimer.RunOnce(() => MarkWords(entry, opened, tries - 1), TimeSpan.FromMilliseconds(250));
            return;
        }
        if (words is null)
        {
            AppLog.Info($"Setting search: opened {entry.Route} (sub-place {(opened ? "open" : "not found")}); '{entry.Text}' isn't shown there now.");
            return;
        }
        var target = words.GetVisualAncestors().OfType<Control>().Take(4)
            .FirstOrDefault(a => a is Button or ToggleButton or ToggleSwitch or CheckBox) ?? (Control)words;
        target.BringIntoView();
        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is null) return;
        var accent = this.TryFindResource("AccentBrush", ActualThemeVariant, out var b) && b is ISolidColorBrush brush
            ? brush.Color : Colors.Orange;
        var mark = new Border
        {
            Name = "SearchMark",
            BorderBrush = new SolidColorBrush(accent),
            Background = new SolidColorBrush(accent, 0.18),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(-6, -3),
            IsHitTestVisible = false,
        };
        AdornerLayer.SetAdornedElement(mark, target);
        AdornerLayer.SetIsClipEnabled(mark, false);   // the frame sits just outside the words
        layer.Children.Add(mark);
        DispatcherTimer.RunOnce(() => layer.Children.Remove(mark), MarkTime);
    }

    /// <summary>The words a TextBlock shows, the way the index walk reads them.</summary>
    internal static string Shown(TextBlock t)
    {
        var raw = t.Text ?? string.Concat(t.Inlines?.OfType<Run>().Select(r => r.Text) ?? Array.Empty<string>());
        return Regex.Replace(raw, @"\s+", " ").Trim().TrimEnd(':', '…').Trim();
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
}
