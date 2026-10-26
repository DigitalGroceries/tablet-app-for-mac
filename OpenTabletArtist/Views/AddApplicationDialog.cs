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

/// <summary>Pick a running app or browse for a .app to add an Application-row profile.</summary>
internal static class AddApplicationDialog
{
    public static async Task<MacAppCandidate?> ShowAsync(Window owner)
    {
        MacAppCandidate? chosen = null;
        var list = new ListBox
        {
            Height = 280,
            MinWidth = 420,
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
                AllowMultiple = false,
            });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            // Users often pick the .app itself (treated as a folder on macOS).
            var candidate = MacFrontmostApp.FromAppPath(path);
            if (candidate is null)
            {
                await Dialogs.ShowMessageAsync(Localizer.T("tablet.app.add_title"),
                    Localizer.T("tablet.app.browse_invalid"), dialog);
                return;
            }
            chosen = candidate;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        add.Click += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: MacAppCandidate c })
            {
                chosen = c;
                dialog.Close();
            }
        };

        await dialog.ShowDialog(owner);
        return chosen;
    }
}
