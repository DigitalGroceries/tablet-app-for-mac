using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// TASKS #212 (Digital Groceries 23 Nov 2026: "Step by step mapping"): the "Set up my buttons" sheet. A simple drawn
/// tablet edge with one key lit; the user presses that key on the tablet and the app learns where each key sits. Skip,
/// Back and Stop while it runs; Start again, Don't save and Save at the end. The drawing is the app's own (no Wacom
/// artwork). What a press does is decided by <see cref="KeyMappingViewModel"/>; this only draws it and keeps the wait.
/// The result is the mapping to save, or null when nothing is to be saved.
/// </summary>
public sealed class KeyMappingDialog : AppWindow
{
    private readonly KeyMappingViewModel _vm;
    private readonly TaskCompletionSource<KeyOrderMap?> _result = new();
    private readonly List<(KeyMappingPlace Place, Border Nub, TextBlock Mark)> _nubs = new();
    private readonly DispatcherTimer _wait;

    public TextBlock StepLabel { get; }
    public TextBlock GroupLabel { get; }
    public TextBlock InstructionLabel { get; }
    public TextBlock MessageLabel { get; }
    public Button BackButton { get; }
    public Button SkipButton { get; }
    public Button StopButton { get; }
    public Button StartAgainButton { get; }
    public Button DontSaveButton { get; }
    public Button SaveButton { get; }

    /// <summary>TASKS #212 part 5: the last screen's "Give the keys Wacom's own functions?" (hidden when there is no
    /// offer), its list, and the two answers.</summary>
    public StackPanel WacomChoice { get; }
    public TextBlock WacomFunctionsLabel { get; }
    public RadioButton UseWacomButton { get; }
    public RadioButton ChooseMyselfButton { get; }
    /// <summary>The small drawn tablet: its body, the ring if any, and one key (Border) per place.</summary>
    public Canvas KeysDrawing { get; }

    /// <summary>The drawn keys, place by place.</summary>
    public IReadOnlyList<Border> KeySpots => _nubs.ConvertAll(n => n.Nub);

    private Ellipse? _ring;
    private double _ringThickness = 6;

    /// <summary>TASKS #212 part 4: the small picture of the ring, strip or finger to use, beside the words, when the
    /// drawn tablet does not show it (a strip, two rings, or touch). Hidden for the keys.</summary>
    public Border ControlPicture { get; }

    /// <summary>True while the drawn tablet's ring is lit (the ring check on a tablet whose layout draws its ring).</summary>
    public bool RingIsLit { get; private set; }

    public KeyMappingDialog(KeyMappingViewModel vm)
    {
        _vm = vm;
        Title = Localizer.Te("keymap.title", "Set up my buttons");
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // A small drawn tablet: its keys where the manual shows them (KeyLayouts), or one plain column. The spot with
        // index N is place N; a proven layout is drawn turned half round when the tablet is turned.
        var layout = vm.Layout;
        bool wide = layout.Width > layout.Height;
        double scale = wide ? Math.Min(1.3, 540 / Math.Max(1, layout.Width)) : 1;
        var spots = vm.DrawTurned ? layout.TurnedSpots : layout.Spots;
        var ring = vm.DrawTurned ? layout.TurnedRing : layout.Ring;
        KeysDrawing = new Canvas { Width = layout.Width * scale, Height = layout.Height * scale };
        var body = new Border
        {
            Width = KeysDrawing.Width, Height = KeysDrawing.Height, BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14 * scale),
        };
        body[!Border.BorderBrushProperty] = new DynamicResourceExtension("TextMutedBrush");
        body[!Border.BackgroundProperty] = new DynamicResourceExtension("GlassBgBrush");
        KeysDrawing.Children.Add(body);
        if (ring is { } r)
        {
            var circle = new Ellipse { Width = r.D * scale, Height = r.D * scale, StrokeThickness = 6 * scale };
            _ring = circle;
            _ringThickness = 6 * scale;
            Canvas.SetLeft(circle, r.X * scale);
            Canvas.SetTop(circle, r.Y * scale);
            KeysDrawing.Children.Add(circle);
        }
        for (int i = 0; i < vm.Places.Count && i < spots.Count; i++)
        {
            var place = vm.Places[i];
            var spot = spots[i];
            var number = new TextBlock { Text = place.Number.ToString(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var nub = new Border { Width = spot.W * scale, Height = spot.H * scale, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Child = number };
            Canvas.SetLeft(nub, spot.X * scale);
            Canvas.SetTop(nub, spot.Y * scale);
            KeysDrawing.Children.Add(nub);
            // The tick or dash for a done key, in the key's upper right corner (it never leaves the drawn tablet).
            var mark = new TextBlock { FontSize = 10, FontWeight = FontWeight.Bold, IsHitTestVisible = false };
            mark[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
            Canvas.SetLeft(mark, (spot.X + spot.W) * scale - 11);
            Canvas.SetTop(mark, spot.Y * scale);
            KeysDrawing.Children.Add(mark);
            _nubs.Add((place, nub, mark));
            place.PropertyChanged += OnPlaceChanged;
        }
        var drawing = new Border
        {
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = wide ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Margin = wide ? new Thickness(0, 4, 0, 16) : new Thickness(0, 4, 0, 0), Child = KeysDrawing,
        };

        ControlPicture = new Border { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4), IsVisible = false };

        StepLabel = new TextBlock { FontSize = 20, FontWeight = FontWeight.SemiBold };
        GroupLabel = new TextBlock { FontSize = 14 };
        GroupLabel[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
        InstructionLabel = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap };
        // The remark keeps its room when empty, so the buttons do not jump when one appears.
        MessageLabel = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, MinHeight = 40, FontWeight = FontWeight.SemiBold };

        BackButton = MakeButton("keymap.back", "Back");
        SkipButton = MakeButton("keymap.skip", "Skip");
        StopButton = MakeButton("keymap.stop", "Stop");
        StartAgainButton = MakeButton("keymap.again", "Start again");
        DontSaveButton = MakeButton("keymap.dont_save", "Don't save");
        DontSaveButton.IsCancel = true;
        SaveButton = MakeButton("keymap.save", "Save");
        SaveButton.IsDefault = true;
        WacomFunctionsLabel = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
        WacomFunctionsLabel[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
        UseWacomButton = new RadioButton
        {
            GroupName = "keymap-wacom", Content = Localizer.Te("keymap.fn.use", "Give them Wacom's functions"),
        };
        ChooseMyselfButton = new RadioButton
        {
            GroupName = "keymap-wacom", IsChecked = true,
            Content = Localizer.Te("keymap.fn.myself", "I'll choose myself (on the ExpressKeys page)"),
        };
        UseWacomButton.IsCheckedChanged += (_, _) => _vm.UseWacomFunctions = UseWacomButton.IsChecked == true;
        WacomChoice = new StackPanel
        {
            Spacing = 4, IsVisible = false,
            Children =
            {
                new TextBlock
                {
                    Text = Localizer.Te("keymap.fn.ask", "Give the keys Wacom's own functions?"),
                    FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
                },
                WacomFunctionsLabel, UseWacomButton, ChooseMyselfButton,
            },
        };
        BackButton.Click += (_, _) => _vm.BackCommand.Execute(null);
        SkipButton.Click += (_, _) => _vm.SkipCommand.Execute(null);
        StopButton.Click += (_, _) => _vm.StopCommand.Execute(null);
        StartAgainButton.Click += (_, _) => _vm.StartAgainCommand.Execute(null);
        DontSaveButton.Click += (_, _) => Finish(null);
        SaveButton.Click += (_, _) => Finish(_vm.Result is { HasAny: true } map ? map : null);

        var words = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(26, 0, 0, 0),
            Children =
            {
                new TextBlock { Text = Title, FontWeight = FontWeight.SemiBold, FontSize = 17, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = Localizer.Te("keymap.intro",
                        "The app lights one key at a time. Press that key on your tablet, so the app learns where each key sits. "
                        + "While this is open your keys do nothing else."),
                    FontSize = 14, TextWrapping = TextWrapping.Wrap,
                },
                StepLabel, GroupLabel, ControlPicture, InstructionLabel, WacomChoice, MessageLabel,
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { BackButton, SkipButton, StopButton, StartAgainButton, DontSaveButton, SaveButton },
                },
            },
        };
        // A tall drawing (keys down one edge) stands left of the words; a wide one (keys along the top) above them.
        var grid = new Grid();
        if (wide)
        {
            grid.RowDefinitions = new RowDefinitions("Auto,*");
            words.Margin = new Thickness(0);
            grid.Children.Add(drawing);
            Grid.SetRow(words, 1);
        }
        else
        {
            grid.ColumnDefinitions = new ColumnDefinitions("Auto,*");
            grid.Children.Add(drawing);
            Grid.SetColumn(words, 1);
        }
        grid.Children.Add(words);
        Content = new Border { Padding = new Thickness(22, 18, 22, 16), Child = grid };

        _wait = new DispatcherTimer { Interval = KeyMappingViewModel.QuietTime };
        _wait.Tick += (_, _) => { _wait.Stop(); _vm.NothingCame(); };
        _vm.PropertyChanged += OnVmChanged;
        _vm.PlaceChanged += RestartWait;
        Apply();
        RestartWait();
    }

    private static Button MakeButton(string key, string english) => new()
    {
        Content = Localizer.Te(key, english), MinWidth = 96, Margin = new Thickness(8, 0, 0, 0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };

    private void RestartWait()
    {
        _wait.Stop();
        if (_vm.IsRunning) _wait.Start();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => Apply();
    private void OnPlaceChanged(object? sender, PropertyChangedEventArgs e) => ApplyKeys();

    private void Apply()
    {
        StepLabel.Text = _vm.StepText;
        StepLabel.IsVisible = _vm.StepText.Length > 0;
        GroupLabel.Text = _vm.GroupText;
        GroupLabel.IsVisible = _vm.GroupText.Length > 0;
        InstructionLabel.Text = _vm.Instruction;
        MessageLabel.Text = _vm.Message;
        bool running = _vm.IsRunning, any = _vm.RecordedCount > 0;
        BackButton.IsVisible = SkipButton.IsVisible = StopButton.IsVisible = running;
        BackButton.IsEnabled = _vm.CanGoBack;
        StartAgainButton.IsVisible = !running && _vm.Places.Count > 0;
        SaveButton.IsVisible = !running && any;
        DontSaveButton.IsVisible = !running;
        // With nothing recorded there is nothing to save: the one button left simply closes.
        DontSaveButton.Content = any ? Localizer.Te("keymap.dont_save", "Don't save") : Localizer.Te("app.close", "Close");
        WacomChoice.IsVisible = _vm.OffersWacomFunctions && any;
        WacomFunctionsLabel.Text = _vm.WacomFunctionsText;
        if (UseWacomButton.IsChecked != _vm.UseWacomFunctions)
        {
            UseWacomButton.IsChecked = _vm.UseWacomFunctions;
            ChooseMyselfButton.IsChecked = !_vm.UseWacomFunctions;
        }
        ApplyKeys();
        ApplyControl();
    }

    private void ApplyKeys()
    {
        foreach (var (place, nub, mark) in _nubs)
        {
            nub[!Border.BackgroundProperty] = new DynamicResourceExtension(place.IsLit ? "SelectionFillBrush" : "GlassBgHoverBrush");
            nub[!Border.BorderBrushProperty] = new DynamicResourceExtension(place.IsLit ? "SelectionBorderBrush" : "TextMutedBrush");
            if (nub.Child is TextBlock number)
                number[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(place.IsLit ? "SelectionTextBrush" : "TextPrimaryBrush");
            nub.Opacity = place.IsLeftOut ? 0.4 : 1;
            mark.Text = place.IsRecorded ? "✓" : place.IsLeftOut ? "–" : "";
        }
    }

    private ControlCheck? _pictured;

    private void ApplyControl()
    {
        RingIsLit = _vm.IsRingLit;
        if (_ring != null)
        {
            _ring[!Shape.StrokeProperty] = new DynamicResourceExtension(RingIsLit ? "SelectionFillBrush" : "TextMutedBrush");
            _ring.StrokeThickness = RingIsLit ? _ringThickness * 1.5 : _ringThickness;
        }
        var control = _vm.CurrentControl;
        ControlPicture.IsVisible = control != null && !RingIsLit;
        if (control == null || RingIsLit || ReferenceEquals(control, _pictured)) return;
        _pictured = control;
        ControlPicture.Child = control.Kind switch
        {
            // A strip, upright, with an arrow for the slide up (no Wacom artwork: the app's own simple shapes).
            ControlCheckKind.Strip => Lit(new Border
            {
                Width = 24, Height = 88, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2),
                Child = new TextBlock { Text = "↑", FontSize = 20, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            }),
            ControlCheckKind.Ring => RingPicture(),
            _ => FingerPicture(),
        };
    }

    private static Border Lit(Border b)
    {
        b[!Border.BorderBrushProperty] = new DynamicResourceExtension("SelectionBorderBrush");
        b[!Border.BackgroundProperty] = new DynamicResourceExtension("SelectionFillBrush");
        if (b.Child is TextBlock t) t[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SelectionTextBrush");
        return b;
    }

    private static Control RingPicture()
    {
        var e = new Ellipse { Width = 56, Height = 56, StrokeThickness = 9 };
        e[!Shape.StrokeProperty] = new DynamicResourceExtension("SelectionFillBrush");
        return e;
    }

    private static Control FingerPicture()
    {
        // A fingertip on a piece of the drawing area.
        var pad = new Border { Width = 96, Height = 60, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2) };
        pad[!Border.BorderBrushProperty] = new DynamicResourceExtension("TextMutedBrush");
        pad[!Border.BackgroundProperty] = new DynamicResourceExtension("GlassBgBrush");
        var tip = new Ellipse { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        tip[!Shape.FillProperty] = new DynamicResourceExtension("SelectionFillBrush");
        pad.Child = tip;
        return pad;
    }

    private void Finish(KeyOrderMap? map)
    {
        _result.TrySetResult(map);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _wait.Stop();
        _vm.PropertyChanged -= OnVmChanged;
        _vm.PlaceChanged -= RestartWait;
        foreach (var (place, _, _) in _nubs) place.PropertyChanged -= OnPlaceChanged;
        _result.TrySetResult(null);
    }

    /// <summary>The mapping to save, or null (closed, Don't save, or nothing recorded).</summary>
    public Task<KeyOrderMap?> Result => _result.Task;

    public static async Task<KeyOrderMap?> ShowAsync(Window owner, KeyMappingViewModel vm)
    {
        var dialog = new KeyMappingDialog(vm);
        await OpenTabletArtist.Helpers.InWindowSheet.ShowDialogAsync(dialog, owner);
        return await dialog._result.Task;
    }
}
