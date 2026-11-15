using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>
/// TASKS #76: pick the tablet model to set up before it is plugged in (or a sample tablet). The list is the built-in
/// catalog of tablets the driver supports, searchable by model number or brand.
/// </summary>
internal static class ChooseTabletDialog
{
    /// <summary>Shows the picker. Returns the chosen tablet name, or null when cancelled.</summary>
    public static async Task<string?> ShowAsync(Window owner)
    {
        string? chosen = null;
        var all = SupportedTabletsCatalog.All;

        var search = new TextBox { Watermark = Localizer.Te("choose.search", "Search, for example PTK-440 or Wacom") };
        var list = new ListBox { Height = 300, MinWidth = 440, SelectionMode = SelectionMode.Single };
        void Reload()
        {
            var items = Filter(all.Select(t => (t.Name, t.ActiveArea)), search.Text)
                .Select(t => new ListBoxItem
                {
                    Content = string.IsNullOrWhiteSpace(t.Area) ? t.Name : $"{t.Name}   ({t.Area})",
                    Tag = t.Name,
                }).ToList();
            list.ItemsSource = items;
            if (items.Count > 0) list.SelectedIndex = 0;
        }
        Reload();
        search.TextChanged += (_, _) => Reload();

        var sample = new Button { Content = Localizer.Te("choose.sample", "Use a sample tablet"), Padding = new Thickness(14, 6) };
        var cancel = new Button { Content = Localizer.Te("app.cancel", "Cancel"), Padding = new Thickness(14, 6) };
        var setUp = new Button { Content = Localizer.Te("choose.setup", "Set up this tablet"), Padding = new Thickness(14, 6) };

        var dialog = new AppWindow
        {
            Title = Localizer.Te("choose.title", "Choose your tablet"),
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = Localizer.Te("choose.body",
                            "Set up your tablet now, even before you plug it in. Your settings are used as soon as it is connected. "
                            + "The model number is on the label on the back of the tablet (for example PTK-440)."),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    search,
                    list,
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                        Children =
                        {
                            sample,
                            new StackPanel
                            {
                                [Grid.ColumnProperty] = 2,
                                Orientation = Orientation.Horizontal,
                                Spacing = 8,
                                Children = { cancel, setUp },
                            },
                        },
                    },
                },
            },
        };

        sample.Click += (_, _) => { chosen = OfflineTablet.SampleTablet; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        setUp.Click += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: string name }) { chosen = name; dialog.Close(); }
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: string name }) { chosen = name; dialog.Close(); }
        };

        await InWindowSheet.ShowDialogAsync(dialog, owner);
        return chosen;
    }

    /// <summary>Pure: the tablets whose name contains every word typed (any order, any case), so "ptk 440",
    /// "440" and "wacom ptk" all find the PTK-440. Unit-tested.</summary>
    internal static IEnumerable<(string Name, string Area)> Filter(IEnumerable<(string Name, string Area)> tablets, string? query)
    {
        var words = (query ?? "").Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return tablets;
        return tablets.Where(t =>
        {
            var compact = t.Name.Replace("-", "").Replace(" ", "");
            return words.All(w => t.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                               || compact.Contains(w, StringComparison.OrdinalIgnoreCase));
        });
    }
}
