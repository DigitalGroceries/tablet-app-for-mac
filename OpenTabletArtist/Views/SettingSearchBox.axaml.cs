using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>Version 29: Home's "Setting search" box. Keys: Enter opens the best match, Down moves into the
/// list (Up from its first line comes back to the box), Escape empties the box.</summary>
public partial class SettingSearchBox : UserControl
{
    public SettingSearchBox()
    {
        InitializeComponent();
        SearchBox.AddHandler(KeyDownEvent, OnBoxKeyDown, RoutingStrategies.Tunnel);
        ResultsList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
    }

    private SettingSearchViewModel? Vm => DataContext as SettingSearchViewModel;

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        switch (e.Key)
        {
            case Key.Enter:
                vm.OpenFirst();
                e.Handled = true;
                break;
            case Key.Escape:
                vm.Clear();
                e.Handled = true;
                break;
            case Key.Down when vm.IsOpen:
                ResultButtons().FirstOrDefault()?.Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        var buttons = ResultButtons().ToList();
        var at = buttons.FindIndex(b => b.IsFocused);
        switch (e.Key)
        {
            case Key.Down when at >= 0 && at < buttons.Count - 1:
                buttons[at + 1].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Up when at > 0:
                buttons[at - 1].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Up when at == 0:
            case Key.Escape:
                SearchBox.Focus(NavigationMethod.Directional);
                if (e.Key == Key.Escape) Vm?.Clear();
                e.Handled = true;
                break;
        }
    }

    private System.Collections.Generic.IEnumerable<Button> ResultButtons() =>
        ResultsList.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible);
}
