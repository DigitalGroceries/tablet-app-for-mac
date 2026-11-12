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
    /// <summary>Control names the layout tests look for.</summary>
    public const string FooterName = "RadialEditorFooter", AreaName = "RadialEditorArea";
    /// <summary>The ring never shrinks below this (px); in a smaller window the area scrolls instead (TASKS #60).</summary>
    public const double MinPieDiameter = 220;

    private readonly TaskCompletionSource<string?> _result = new();

    public RadialMenuEditorDialog() : this(new RadialMenuEditorViewModel(null, "Radial menu")) { }   // designer

    public RadialMenuEditorDialog(RadialMenuEditorViewModel vm)
    {
        DataContext = vm;
        Title = vm.Heading;
        Width = 1000;
        Height = 830;
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
    public static Task<string?> ShowAsync(Window owner, string menuJson, string title) =>
        ShowAsync(owner, menuJson, title, null, -1);

    /// <summary>As above, opened on slice <paramref name="slice"/> of the ring reached by <paramref name="path"/>.</summary>
    public static async Task<string?> ShowAsync(Window owner, string menuJson, string title, System.Collections.Generic.IReadOnlyList<int>? path, int slice)
    {
        var vm = new RadialMenuEditorViewModel(menuJson, title);
        if (slice >= 0) vm.GoTo(path ?? Array.Empty<int>(), slice);
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
        var pie = new RadialMenuPie { Interactive = true, Cursor = new Cursor(StandardCursorType.Hand) };
        pie[!RadialMenuPie.LabelsProperty] = new Binding(nameof(vm.Labels));
        pie[!RadialMenuPie.SetFlagsProperty] = new Binding(nameof(vm.SetFlags));
        pie[!RadialMenuPie.SubmenuFlagsProperty] = new Binding(nameof(vm.SubmenuFlags));
        pie[!RadialMenuPie.SelectedProperty] = new Binding(nameof(vm.SelectedSlice));
        pie[!RadialMenuPie.TransparencyProperty] = new Binding(nameof(vm.Transparency));   // live preview of the transparency
        pie.SliceClicked += vm.SelectSlice;
        // TASKS #60: the ring has the size the slider says (live preview) but never more than the free space, and never less than
        // MinPieDiameter (smaller windows scroll instead). The old fixed 470 x 470 box was taller than the area in a 1100x720 window.
        var pieBox = new PieFit { Child = pie, MinDiameter = MinPieDiameter };
        pieBox[!PieFit.PreferredProperty] = new Binding(nameof(vm.PreviewDiameter));
        var where = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
        where[!TextBlock.TextProperty] = new Binding(nameof(vm.Where));
        var back = new Button { Content = vm.BackLabel, Command = vm.BackCommand, Padding = new Thickness(12, 6) };
        back[!Visual.IsVisibleProperty] = new Binding(nameof(vm.InSubmenu));
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10, MinWidth = MinPieDiameter };
        Grid.SetRow(where, 0); Grid.SetRow(pieBox, 1); Grid.SetRow(back, 2);
        left.Children.Add(where);
        left.Children.Add(pieBox);
        left.Children.Add(back);

        // Right: the selected slice
        var right = new StackPanel { Spacing = 6, Margin = new Thickness(24, 0, 0, 0), MinWidth = 380, VerticalAlignment = VerticalAlignment.Top };
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

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto") };
        Grid.SetColumn(right, 1);
        body.Children.Add(left);
        body.Children.Add(right);

        // Footer: size, reset, save / cancel
        var size = new Slider { Minimum = RadialMenuDef.MinSize, Maximum = RadialMenuDef.MaxSize, TickFrequency = 1, IsSnapToTickEnabled = true, MinWidth = 220 };
        size[!Avalonia.Controls.Primitives.RangeBase.ValueProperty] = new Binding(nameof(vm.Size)) { Mode = BindingMode.TwoWay };
        Avalonia.Automation.AutomationProperties.SetName(size, vm.SizeCaption);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 14, 0, 0), Name = FooterName };
        footer.RowDefinitions = new RowDefinitions("Auto,Auto");
        void Put(Control c, int col, int row = 0) { Grid.SetColumn(c, col); Grid.SetRow(c, row); footer.Children.Add(c); }
        Put(new TextBlock { Text = vm.SizeCaption, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, 0);
        Put(new TextBlock { Text = vm.SizeSmall, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, 1);
        Put(size, 2);
        Put(new TextBlock { Text = vm.SizeLarge, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) }, 3);
        var sizeNumber = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, MinWidth = 34, Margin = new Thickness(0, 0, 16, 0) };
        sizeNumber[!TextBlock.TextProperty] = new Binding(nameof(vm.SizeText));
        Put(sizeNumber, 4);
        // Menu transparency: 0 (solid) to 100 (clear), the ring on the left shows it while the slider moves.
        var transparency = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true, MinWidth = 220, Margin = new Thickness(0, 6, 0, 0) };
        transparency[!Avalonia.Controls.Primitives.RangeBase.ValueProperty] = new Binding(nameof(vm.Transparency)) { Mode = BindingMode.TwoWay };
        Avalonia.Automation.AutomationProperties.SetName(transparency, vm.TransparencyCaption);
        Put(new TextBlock { Text = vm.TransparencyCaption, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 10, 0) }, 0, 1);
        Put(new TextBlock { Text = vm.TransparencySolid, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 6, 0) }, 1, 1);
        Put(transparency, 2, 1);
        Put(new TextBlock { Text = vm.TransparencyClear, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 6, 10, 0) }, 3, 1);
        // The number is a box too (TASKS #59, Zak 18:21): type 0-100 and press Enter or leave the box; Up / Down change it by 1.
        var transparencyBox = new TextBox
        {
            Width = 52, MinWidth = 0, HorizontalContentAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold, MaxLength = 6, Margin = new Thickness(0, 6, 0, 0), Name = "TransparencyBox",
        };
        transparencyBox[!TextBox.TextProperty] = new Binding(nameof(vm.TransparencyInput)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus };
        Avalonia.Automation.AutomationProperties.SetName(transparencyBox, vm.TransparencyCaption + " value");
        transparencyBox.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return) { vm.TransparencyInput = transparencyBox.Text ?? ""; transparencyBox.Text = vm.TransparencyInput; transparencyBox.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Up) { vm.TransparencyInput = (vm.Transparency + 1).ToString(System.Globalization.CultureInfo.InvariantCulture); e.Handled = true; }
            else if (e.Key == Key.Down) { vm.TransparencyInput = (vm.Transparency - 1).ToString(System.Globalization.CultureInfo.InvariantCulture); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // After leaving the box, show the real value (clamped, or the old one when the text was not a number).
        transparencyBox.LostFocus += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => transparencyBox.Text = vm.TransparencyInput);
        var transparencyPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
        transparencyPanel.Children.Add(transparencyBox);
        transparencyPanel.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 6, 0, 0) });
        Put(transparencyPanel, 4, 1);
        var reset = new Button { Content = vm.ResetLabel, Command = vm.ResetCommand, Padding = new Thickness(12, 6), HorizontalAlignment = HorizontalAlignment.Left };
        Put(reset, 5); Grid.SetRowSpan(reset, 2); reset.VerticalAlignment = VerticalAlignment.Center;
        var save = new Button { Content = vm.SaveLabel, Command = vm.SaveCommand, Padding = new Thickness(22, 8), Margin = new Thickness(0, 0, 8, 0) };
        if (Application.Current?.TryFindResource("AccentButton", out var accent) == true && accent is Avalonia.Styling.ControlTheme t) save.Theme = t;
        Put(save, 6); Grid.SetRowSpan(save, 2); save.VerticalAlignment = VerticalAlignment.Center;
        var cancel = new Button { Content = vm.CancelLabel, Command = vm.CancelCommand, Padding = new Thickness(16, 8), VerticalAlignment = VerticalAlignment.Center };
        Put(cancel, 7); Grid.SetRowSpan(cancel, 2);
        var footerWrap = new StackPanel();
        footerWrap.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 16, 0, 0), [!Border.BackgroundProperty] = new DynamicResourceExtension("DividerBrush") });
        footerWrap.Children.Add(footer);
        DockPanel.SetDock(footerWrap, Dock.Bottom);

        var root = new DockPanel { Margin = new Thickness(24) };
        root.Children.Add(heading);
        root.Children.Add(footerWrap);
        var area = new ScrollViewer
        {
            Name = AreaName, Content = body,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        // A ScrollViewer measures its content without a limit, so the ring's column is given the area's size by hand: as big as the
        // free space (the ring then scales to it), but not smaller than the ring's minimum (then the area scrolls). The settings
        // column keeps its own height; when it is taller than the area, that is what scrolls.
        void Fit()
        {
            double rightW = right.Margin.Left + Math.Max(right.MinWidth, right.Bounds.Width);
            double chrome = where.Bounds.Height + (back.IsVisible ? back.Bounds.Height + left.RowSpacing : 0) + left.RowSpacing;
            left.Height = Math.Max(MinPieDiameter + chrome, area.Bounds.Height);
            // 20 px stay free for the vertical scrollbar, so a scrollbar never makes the other one appear (and eat the ring's room).
            left.Width = Math.Max(MinPieDiameter, area.Bounds.Width - rightW - 20);
        }
        area.SizeChanged += (_, _) => Fit();
        where.SizeChanged += (_, _) => Fit();
        back.SizeChanged += (_, _) => Fit();
        right.SizeChanged += (_, _) => Fit();
        root.Children.Add(area);
        return root;
    }
}

/// <summary>TASKS #60: holds the ring. It gets the free space, draws the ring at <see cref="Preferred"/> (the menu size) when that
/// fits, else as big as fits, never under <see cref="MinDiameter"/> (the caller's area scrolls then). The ring stays centred.</summary>
internal sealed class PieFit : Decorator
{
    public static readonly StyledProperty<double> PreferredProperty = AvaloniaProperty.Register<PieFit, double>(nameof(Preferred), 460);
    public double Preferred { get => GetValue(PreferredProperty); set => SetValue(PreferredProperty, value); }
    public double MinDiameter { get; set; } = 260;

    static PieFit() { AffectsMeasure<PieFit>(PreferredProperty); AffectsArrange<PieFit>(PreferredProperty); }

    private double Diameter(Size available)
    {
        double free = Math.Min(double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width,
                               double.IsInfinity(available.Height) ? double.PositiveInfinity : available.Height);
        return Math.Max(1, Math.Min(Preferred, Math.Max(MinDiameter, free)));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double d = Diameter(availableSize);
        Child?.Measure(new Size(d, d));
        // Take the whole free height (and width) so the ring does not move while the size slider does.
        double w = double.IsInfinity(availableSize.Width) ? Math.Max(d, Preferred) : Math.Max(availableSize.Width, d);
        double h = double.IsInfinity(availableSize.Height) ? Math.Max(d, Preferred) : Math.Max(availableSize.Height, d);
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double d = Diameter(finalSize);
        Child?.Arrange(new Rect((finalSize.Width - d) / 2, (finalSize.Height - d) / 2, d, d));
        return finalSize;
    }
}
