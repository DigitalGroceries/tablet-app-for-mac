using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>Pick running apps or browse for .app bundles to add Application-row profiles.
/// Version 14: several at once — hold Command to select more than one, in the list and in the Finder browser.</summary>
internal static class AddApplicationDialog
{
    public static async Task<IReadOnlyList<MacAppCandidate>> ShowAsync(Window owner)
    {
        var chosen = new List<MacAppCandidate>();
        var list = new ListBox
        {
            Height = 280,
            MinWidth = 420,
            // Command-click (Shift-click for a range) picks several apps; macOS's usual list behaviour.
            SelectionMode = SelectionMode.Multiple,
        };
        void Reload()
        {
            var items = MacFrontmostApp.RunningApps()
                .Select(a => new ListBoxItem
                {
                    Content = string.IsNullOrEmpty(a.AppPath)
                        ? $"{a.DisplayName}  ({a.BundleId})"
                        : $"{a.DisplayName}  ({a.BundleId})",
                    Tag = a,
                }).ToList();
            list.ItemsSource = items;
            if (items.Count > 0) list.SelectedIndex = 0;
        }
        Reload();

        var browse = new Button { Content = Localizer.T("tablet.app.browse"), Padding = new Thickness(14, 6) };
        var cancel = new Button { Content = Localizer.T("app.cancel"), Padding = new Thickness(14, 6) };
        var add = new Button { Content = Localizer.T("tablet.app.add"), Padding = new Thickness(14, 6) };

        var dialog = new AppWindow
        {
            Title = Localizer.T("tablet.app.add_title"),
            Width = 480,
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
                        Text = Localizer.T("tablet.app.add_body"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    list,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { browse, cancel, add },
                    },
                },
            },
        };

        browse.Click += async (_, _) =>
        {
            var storage = owner.StorageProvider;
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Localizer.T("tablet.app.browse_title"),
                AllowMultiple = true,
            });
            var paths = folders.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).ToList();
            if (paths.Count == 0) return;
            // Users often pick the .app itself (treated as a folder on macOS).
            var picked = Pick(paths, MacFrontmostApp.FromAppPath);
            if (picked.Count == 0)
            {
                await Dialogs.ShowMessageAsync(Localizer.T("tablet.app.add_title"),
                    Localizer.T("tablet.app.browse_invalid"), dialog);
                return;
            }
            chosen.AddRange(picked);
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        add.Click += (_, _) =>
        {
            var selected = (list.SelectedItems?.OfType<ListBoxItem>() ?? Enumerable.Empty<ListBoxItem>())
                .Select(i => i.Tag).OfType<MacAppCandidate>().ToList();
            if (selected.Count == 0) return;
            chosen.AddRange(Distinct(selected));
            dialog.Close();
        };

        await dialog.ShowDialog(owner);
        return chosen;
    }

    /// <summary>Turns picked .app paths into candidates, skipping anything that isn't an app and duplicates.
    /// Pure apart from <paramref name="fromPath"/>; unit-tested.</summary>
    internal static IReadOnlyList<MacAppCandidate> Pick(IEnumerable<string> paths, Func<string, MacAppCandidate?> fromPath)
        => Distinct(paths.Select(fromPath).OfType<MacAppCandidate>());

    /// <summary>One entry per bundle id, first one wins (case-insensitive, like the profile store).</summary>
    internal static IReadOnlyList<MacAppCandidate> Distinct(IEnumerable<MacAppCandidate> candidates)
        => candidates.Where(c => !string.IsNullOrWhiteSpace(c.BundleId))
            .GroupBy(c => c.BundleId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).ToList();
}
