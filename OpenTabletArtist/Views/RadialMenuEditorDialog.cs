using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// The radial menu editor sheet (TASKS #42): the ring on the left (click a slice), its settings on the right,
/// menu size and Reset at the bottom. Opens inside the main window like every other dialog (Version 19).
/// </summary>
public sealed class RadialMenuEditorDialog : Window
{
    private readonly TaskCompletionSource<string?> _result = new();

    public RadialMenuEditorDialog() : this(new RadialMenuEditorViewModel(null, "Radial menu")) { }   // designer

    public RadialMenuEditorDialog(RadialMenuEditorViewModel vm)
    {
        DataContext = vm;
        Title = vm.Heading;
        Width = 880;
        Height = 600;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("BgBaseBrush");
        Content = Build(vm);
        vm.CloseRequested += () => { _result.TrySetResult(vm.Result); Close(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { vm.CancelCommand.Execute(null); e.Handled = true; } };
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _result.TrySetResult((DataContext as RadialMenuEditorViewModel)?.Result);
    }

    /// <summary>Open the editor over <paramref name="owner"/>; returns the new menu (JSON) or null on Cancel.</summary>
    public static async Task<string?> ShowAsync(Window owner, string menuJson, string title)
    {
        var vm = new RadialMenuEditorViewModel(menuJson, title);
        var dialog = new RadialMenuEditorDialog(vm);
        vm.PickKeystroke = initial => BindingEditorDialog.ShowAsync(owner, initial, vm.Heading + " · " + vm.SliceCaption);
        vm.PickApp = () => PickAppAsync(owner);
        await OpenTabletArtist.Helpers.InWindowSheet.ShowDialogAsync(dialog, owner);
        return await dialog._result.Task;
    }

    private static async Task<string?> PickAppAsync(Window owner)
    {
        var top = TopLevel.GetTopLevel(owner);
        if (top?.StorageProvider == null) return null;
        IStorageFolder? start = null;
        try { start = await top.StorageProvider.TryGetFolderFromPathAsync("/Applications"); } catch (Exception) { }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Services.Localizer.Te("radial.choose_app", "Choose app…"),
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType("Apps") { Patterns = new[] { "*.app" } } },
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private static Control Build(RadialMenuEditorViewModel vm)
    {
        TextBlock Caption(string text) => new()
        {
            Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0),
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush"),
        };

        // Heading
        var heading = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock
        {
            Text = vm.Heading + " · " + vm.Title, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimaryBrush"),
        });
        heading.Children.Add(new TextBlock
        {
            Text = vm.HelpText, TextWrapping = TextWrapping.Wrap,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush"),
        });
        DockPanel.SetDock(heading, Dock.Top);

        // Left: where we are, the ring, Back
        var pie = new RadialMenuPie { Width = 320, Height = 320, Interactive = true, Cursor = new Cursor(StandardCursorType.Hand) };
        pie[!RadialMenuPie.LabelsProperty] = new Binding(nameof(vm.Labels));
        pie[!RadialMenuPie.SetFlagsProperty] = new Binding(nameof(vm.SetFlags));
        pie[!RadialMenuPie.SubmenuFlagsProperty] = new Binding(nameof(vm.SubmenuFlags));
        pie[!RadialMenuPie.SelectedProperty] = new Binding(nameof(vm.SelectedSlice));
        pie.SliceClicked += vm.SelectSlice;
        var where = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
        where[!TextBlock.TextProperty] = new Binding(nameof(vm.Where));
        var back = new Button { Content = vm.BackLabel, Command = vm.BackCommand, Padding = new Thickness(12, 6) };
        back[!Visual.IsVisibleProperty] = new Binding(nameof(vm.InSubmenu));
        var left = new StackPanel { Spacing = 10, Width = 340 };
        left.Children.Add(where);
        left.Children.Add(pie);
        left.Children.Add(back);

        // Right: the selected slice
        var right = new StackPanel { Spacing = 6, Margin = new Thickness(24, 0, 0, 0), MinWidth = 380 };
        var sliceCaption = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        sliceCaption[!TextBlock.TextProperty] = new Binding(nameof(vm.SliceCaption));
        right.Children.Add(sliceCaption);

        right.Children.Add(Caption(vm.LabelCaption));
        var label = new TextBox { MaxLength = 40, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        label[!TextBox.TextProperty] = new Binding(nameof(vm.SliceLabel)) { Mode = BindingMode.TwoWay };
        right.Children.Add(label);

        right.Children.Add(Caption(vm.ActionCaption));
        var action = new ComboBox { ItemsSource = vm.ActionNames, MinWidth = 280 };
        action[!Avalonia.Controls.Primitives.SelectingItemsControl.SelectedIndexProperty] = new Binding(nameof(vm.ActionIndex)) { Mode = BindingMode.TwoWay };
        right.Children.Add(action);

        // Keystroke
        var keysRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };
        var keysText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        keysText[!TextBlock.TextProperty] = new Binding(nameof(vm.KeysText));
        keysRow.Children.Add(keysText);
        keysRow.Children.Add(new Button { Content = vm.SetKeysLabel, Command = vm.SetKeysCommand, Padding = new Thickness(12, 6) });
        keysRow[!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsKeys));
        right.Children.Add(keysRow);

        // Mouse click
        var click = new ComboBox { ItemsSource = vm.ClickNames, MinWidth = 200, Margin = new Thickness(0, 6, 0, 0) };
        click[!Avalonia.Controls.Primitives.SelectingItemsControl.SelectedIndexProperty] = new Binding(nameof(vm.ClickIndex)) { Mode = BindingMode.TwoWay };
        click[!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsClick));
        right.Children.Add(click);

        // Submenu
        var sub = new Button { Content = vm.EditSubmenuLabel, Command = vm.EditSubmenuCommand, Padding = new Thickness(12, 6), Margin = new Thickness(0, 6, 0, 0) };
        sub[!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsSubmenu));
        right.Children.Add(sub);

        // Open an app
        var appRow = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        var appBox = new TextBox { Watermark = "/Applications/…app" };
        appBox[!TextBox.TextProperty] = new Binding(nameof(vm.AppText)) { Mode = BindingMode.TwoWay };
        appRow.Children.Add(appBox);
        appRow.Children.Add(new Button { Content = vm.ChooseAppLabel, Command = vm.ChooseAppCommand, Padding = new Thickness(12, 6) });
        appRow[!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsOpen));
        right.Children.Add(appRow);

        right.Children.Add(new Button
        {
            Content = vm.ClearSliceLabel, Command = vm.ClearSliceCommand, Padding = new Thickness(12, 6), Margin = new Thickness(0, 14, 0, 0),
        });

        var body = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(left);
        body.Children.Add(right);

        // Footer: size, reset, save / cancel
        var size = new Slider { Minimum = 1, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true, MinWidth = 220 };
        size[!Avalonia.Controls.Primitives.RangeBase.ValueProperty] = new Binding(nameof(vm.Size)) { Mode = BindingMode.TwoWay };
        Avalonia.Automation.AutomationProperties.SetName(size, vm.SizeCaption);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 14, 0, 0) };
        void Put(Control c, int col) { Grid.SetColumn(c, col); footer.Children.Add(c); }
        Put(new TextBlock { Text = vm.SizeCaption, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, 0);
        Put(new TextBlock { Text = vm.SizeSmall, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, 1);
        Put(size, 2);
        Put(new TextBlock { Text = vm.SizeLarge, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 16, 0) }, 3);
        var reset = new Button { Content = vm.ResetLabel, Command = vm.ResetCommand, Padding = new Thickness(12, 6), HorizontalAlignment = HorizontalAlignment.Left };
        Put(reset, 4);
        var save = new Button { Content = vm.SaveLabel, Command = vm.SaveCommand, Padding = new Thickness(22, 8), Margin = new Thickness(0, 0, 8, 0) };
        if (Application.Current?.TryFindResource("AccentButton", out var accent) == true && accent is Avalonia.Styling.ControlTheme t) save.Theme = t;
        Put(save, 5);
        Put(new Button { Content = vm.CancelLabel, Command = vm.CancelCommand, Padding = new Thickness(16, 8) }, 6);
        var footerWrap = new StackPanel();
        footerWrap.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 16, 0, 0), [!Border.BackgroundProperty] = new DynamicResourceExtension("DividerBrush") });
        footerWrap.Children.Add(footer);
        DockPanel.SetDock(footerWrap, Dock.Bottom);

        var root = new DockPanel { Margin = new Thickness(24) };
        root.Children.Add(heading);
        root.Children.Add(footerWrap);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        return root;
    }
}
