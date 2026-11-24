using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenTabletArtist.Views;

/// <summary>V30 build 4 (Zak 24 Nov 2026: "When the user turns the switch on the app should ask them what buttons should open this
/// menu"): a small dialog with a question, a line under it and one button per choice ("Pen button 1", "Pen button 2"...), plus Cancel.
/// The result is the index of the chosen button, or null for Cancel or closing the window.</summary>
public sealed class MenuButtonChoiceDialog : AppWindow
{
    private readonly TaskCompletionSource<int?> _result = new();

    public IReadOnlyList<Button> ChoiceButtons { get; }
    public Button CancelButton { get; }

    public MenuButtonChoiceDialog(string title, string body, IReadOnlyList<string> choices)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var buttons = new List<Button>();
        var row = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        for (int i = 0; i < choices.Count; i++)
        {
            int index = i;
            var b = new Button { Content = choices[i], MinWidth = 130, Margin = new Thickness(0, 0, 10, 10), HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Click += (_, _) => { _result.TrySetResult(index); Close(); };
            buttons.Add(b);
            row.Children.Add(b);
        }
        ChoiceButtons = buttons;
        CancelButton = new Button { Content = Services.Localizer.Te("app.cancel", "Cancel"), MinWidth = 100, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        CancelButton.Click += (_, _) => { _result.TrySetResult(null); Close(); };

        Content = new Border
        {
            Padding = new Thickness(22, 18, 22, 16),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 17, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = body, FontSize = 14, TextWrapping = TextWrapping.Wrap },
                    row,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { CancelButton } },
                },
            },
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _result.TrySetResult(null);
    }

    /// <summary>The chosen index, or null when the user cancels.</summary>
    public Task<int?> Result => _result.Task;

    public static async Task<int?> ShowAsync(Window owner, string title, string body, IReadOnlyList<string> choices)
    {
        var dialog = new MenuButtonChoiceDialog(title, body, choices);
        await OpenTabletArtist.Helpers.InWindowSheet.ShowDialogAsync(dialog, owner);
        return await dialog._result.Task;
    }
}
