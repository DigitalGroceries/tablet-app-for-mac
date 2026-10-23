using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using OpenTabletArtist.Controls;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Hosts a single tablet's tabbed settings (DataContext is a <see cref="TabletDetailViewModel"/>).
/// Owns the view-side lifecycle: stream the live device reports only while a live tab (Buttons /
/// Wheels) is visible (#102), refresh the display list when monitors change (#95), and deep-link to
/// the tab that carries a health-issue fix. Scoped to the view's attach/detach.
/// </summary>
public partial class TabletDetailView : UserControl, ITabbedContent
{
    private Screens? _screens;

    public TabletDetailView()
    {
        InitializeComponent();
        // Build L: arrow keys scroll the visible tab (Page Up/Down, Home/End are handled by ScrollViewer itself).
        // Not while a control that uses the arrows has focus (text box, slider, dropdown, list).
        AddHandler(KeyDownEvent, OnTabKeyDown, RoutingStrategies.Bubble, handledEventsToo: false);
        // Build O: the Tool tile follows the open tab (and picks the tab group), from load on.
        foreach (var t in new[] { ButtonsTab, WheelsTab, PenTab, EraserTab, MappingTab, AboutTab, CalibrationTab, FiltersTab, JsonTab, HoverTab })
            t.IsCheckedChanged += (_, _) => SyncToolChips();
        PenTab.IsCheckedChanged += OnLiveTabChanged;
        EraserTab.IsCheckedChanged += OnLiveTabChanged;
        SyncToolChips();
    }

    internal void SyncToolChips()
    {
        if (ButtonsTab.IsChecked == true || WheelsTab.IsChecked == true) SelectTool(ToolFunctionsButton);
        else if (PenTab.IsChecked == true || EraserTab.IsChecked == true || MappingTab.IsChecked == true) SelectTool(ToolPenButton);
        else if (AboutTab.IsChecked == true || CalibrationTab.IsChecked == true || FiltersTab.IsChecked == true
                 || JsonTab.IsChecked == true || HoverTab.IsChecked == true) SelectTool(ToolMoreButton);
    }

    // Build R2: the Tool tiles are exclusive by hand. Their GroupName stopped grouping them once the panel moved
    // out of the header (R), so Functions and Pen could both stay lit and both tab groups showed.
    private void SelectTool(RadioButton tile)
    {
        foreach (var t in new[] { ToolFunctionsButton, ToolPenButton, ToolMoreButton })
            t.IsChecked = ReferenceEquals(t, tile);
    }

    // Build O: Tool tiles choose the tab group, like Wacom's Tool row.
    private void OnToolFunctions(object? s, RoutedEventArgs e) { ButtonsTab.IsChecked = true; SelectTool(ToolFunctionsButton); }
    private void OnToolPen(object? s, RoutedEventArgs e) { PenTab.IsChecked = true; SelectTool(ToolPenButton); }
    private void OnToolMore(object? s, RoutedEventArgs e) { AboutTab.IsChecked = true; SelectTool(ToolMoreButton); }

    private void OnTileScrollLeft(object? s, RoutedEventArgs e) => ScrollTiles(s, -60);
    private void OnTileScrollRight(object? s, RoutedEventArgs e) => ScrollTiles(s, 60);
    private void ScrollTiles(object? s, double dx)
    {
        if ((s as Control)?.Tag is string name && this.FindControl<ScrollViewer>(name) is { } sv)
            sv.Offset = new Vector(Math.Max(0, sv.Offset.X + dx), sv.Offset.Y);
    }

    private void OnCustomizeFeel(object? s, RoutedEventArgs e)
    {
        PenTab.IsChecked = true;
        FeelDetails.IsVisible = !FeelDetails.IsVisible;
    }

    /// <summary>R18: the Eraser tab's Customize… opens the same (shared) curve under its own frame.</summary>
    private void OnCustomizeEraserFeel(object? s, RoutedEventArgs e) =>
        EraserFeelDetails.IsVisible = !EraserFeelDetails.IsVisible;


    private void OnBottomOptions(object? s, RoutedEventArgs e)
    {
        if (CalibrationTab.IsVisible || Vm?.HasAreaMapping == true) CalibrationTab.IsChecked = true; else AboutTab.IsChecked = true;
    }
    // R18: '?' opens the in-app user manual (no upstream link).
    private void OnBottomHelp(object? s, RoutedEventArgs e) => ManualWindow.ShowManual();

    // Build N.1: Wacom-like "Default" buttons. Always confirm; they edit like any change (no extra save).
    private async void OnRingDefault(object? s, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (await OpenTabletArtist.Helpers.Dialogs.ShowConfirmAsync("Reset Touch Ring?",
                "Put the four ring modes (names, functions, speeds and keys) back to the defaults? Your other settings are not touched."))
            await vm.ResetTouchRingToDefaultsAsync();
    }

    private async void OnKeysDefault(object? s, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (await OpenTabletArtist.Helpers.Dialogs.ShowConfirmAsync("Reset ExpressKeys?",
                "Clear every ExpressKey action? Your other settings are not changed."))
            await vm.ResetExpressKeysToDefaultsAsync();
    }

    private void OnTabKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != Avalonia.Input.KeyModifiers.None) return;
        if (e.Key is not (Avalonia.Input.Key.Up or Avalonia.Input.Key.Down)) return;
        if (e.Source is TextBox or Slider or ComboBox or ListBox or NumericUpDown) return;
        if (e.Source is Avalonia.Visual v && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<ComboBox>(v) != null) return;
        var tab = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<ScrollViewer>()
            .FirstOrDefault(s => s.Classes.Contains("tabScroll") && s.IsEffectivelyVisible);
        if (tab == null || tab.Extent.Height <= tab.Viewport.Height) return;
        if (e.Key == Avalonia.Input.Key.Down) tab.LineDown(); else tab.LineUp();
        e.Handled = true;
    }

    private TabletDetailViewModel? Vm => DataContext as TabletDetailViewModel;

    /// <summary>The tab rail's currently-visible tab buttons, in order (#437: the screenshot sweep checks
    /// each in turn to capture every sub-tab of a connected tablet).</summary>
    public IReadOnlyList<RadioButton> VisibleTabButtons() =>
        TabRail.Children.OfType<RadioButton>().Where(r => r.IsVisible).ToList();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Health-issue "Fix" deep-link: open on the tab that carries the fix (e.g. Display Mapping for an
        // off-screen mapping) instead of the default About tab. Also handle a request that arrives while
        // the page is already shown (re-navigation wouldn't re-attach the view).
        if (Vm != null) Vm.TabRequested += OnTabRequested;
        if (Vm?.ConsumePendingTab() is { } pending) SelectTab(pending);

        _screens = TopLevel.GetTopLevel(this)?.Screens;
        if (_screens != null) _screens.Changed += OnScreensChanged;
        // Live device-report stream feeds the aux-button highlight (Buttons tab) and the wheel gauge
        // (Wheels tab), so watch those tabs.
        ButtonsTab.IsCheckedChanged += OnLiveTabChanged;
        WheelsTab.IsCheckedChanged += OnLiveTabChanged;
        UpdateLiveInput(); // start now if we opened on a live tab

        // Interactive active-area edits from the diagram → persist through the VM (#199).
        ActiveAreaDiagramControl.AreaCommitted += OnActiveAreaCommitted;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Vm != null) Vm.TabRequested -= OnTabRequested;
        if (_screens != null) { _screens.Changed -= OnScreensChanged; _screens = null; }
        ButtonsTab.IsCheckedChanged -= OnLiveTabChanged;
        WheelsTab.IsCheckedChanged -= OnLiveTabChanged;
        ActiveAreaDiagramControl.AreaCommitted -= OnActiveAreaCommitted;
        Vm?.StopLiveInput();
    }

    private void OnActiveAreaCommitted(object? sender, ActiveAreaEdit e) =>
        _ = Vm?.CommitActiveArea(e.Width, e.Height, e.CenterX, e.CenterY);

    // Calibration report moved off the tab into a dialog (#500/#501): open it over this window, bound to
    // the same VM so its report bindings resolve.
    private void OnViewCalibrationReport(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && TopLevel.GetTopLevel(this) is Window owner)
            _ = CalibrationReportDialog.ShowAsync(owner, vm);
    }

    /// <summary>Start the density picked in the calibrate button's flyout, and close it behind you.
    /// A Click handler rather than a Command binding: the flyout's contents live in a popup outside this
    /// view's visual tree, so <c>$parent[ItemsControl]</c> cannot reach the page's command from in
    /// there — the same reason the preset rows use handlers.</summary>
    private void OnStartCalibrationChoice(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CalibrationModeChoice choice }) return;
        CalibrateChevron.Flyout?.Hide(); // the flyout this button lives in belongs to the chevron
        Vm?.StartCalibrationCommand.Execute(choice);
    }

    private static readonly FilePickerFileType CalibrationFileType =
        new("Calibration") { Patterns = new[] { "*.json" } };

    // #545: export/import this tablet's calibration. The VM builds/consumes the JSON (pure); the file
    // picker lives here because it needs the window's StorageProvider.
    private async void OnExportCalibration(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var json = vm.BuildCaptureJson();
        if (json is null) return; // vm set CaptureStatus explaining why

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export calibration",
            SuggestedFileName = vm.SuggestedCaptureFileName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { CalibrationFileType },
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return; // cancelled

        try
        {
            await File.WriteAllTextAsync(path, json);
            vm.NoteCaptureExported(path);
        }
        catch (Exception ex)
        {
            vm.CaptureStatus = $"Couldn't write the file: {ex.Message}";
        }
    }

    private async void OnImportCalibration(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import calibration",
            AllowMultiple = false,
            FileTypeFilter = new[] { CalibrationFileType },
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return; // cancelled

        try
        {
            var json = await File.ReadAllTextAsync(path);
            await vm.ImportCalibrationAsync(json, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            vm.CaptureStatus = $"Couldn't read the file: {ex.Message}";
        }
    }

    private void OnScreensChanged(object? sender, EventArgs e) =>
        Vm?.RefreshDisplaysCommand.Execute(null);

    // A deep-link arriving while the page is already shown — clear the pending flag (we're handling it
    // live) and switch tabs.
    private void OnTabRequested(TabletDetailTab tab)
    {
        Vm?.ConsumePendingTab();
        SelectTab(tab);
    }

    // Check the RadioButton for a deep-linked tab. Its content ScrollViewer is gated on IsChecked, so
    // checking the tab shows it (and unchecks whatever was selected — they share the rail's group).
    private void SelectTab(TabletDetailTab tab)
    {
        switch (tab)
        {
            case TabletDetailTab.DisplayMapping: MappingTab.IsChecked = true; break;
            // R13: the pressure curve's one home is Tablet › Pen › Feel details (Pen › Pressure is gone).
            case TabletDetailTab.PenTilt:
            case TabletDetailTab.PenBehavior:
            case TabletDetailTab.PenInputs:
                ToolPenButton.IsChecked = true; PenTab.IsChecked = true; break;
            case TabletDetailTab.PenDynamics:
                ToolPenButton.IsChecked = true; PenTab.IsChecked = true; FeelDetails.IsVisible = true; break;
            // R14: every pen deep-link (behaviour, inputs, tilt) lands on Tablet › Pen; the PEN page is gone.
        }
    }

    private void OnLiveTabChanged(object? sender, RoutedEventArgs e) => UpdateLiveInput();

    private void UpdateLiveInput()
    {
        if (Vm is not { } vm) return;
        if (ButtonsTab.IsChecked == true || WheelsTab.IsChecked == true || PenTab.IsChecked == true || EraserTab.IsChecked == true)
            vm.StartLiveInput();
        else vm.StopLiveInput();
    }

}
