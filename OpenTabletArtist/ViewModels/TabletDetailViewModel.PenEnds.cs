using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

public partial class TabletDetailViewModel
{
    // ── V29 #180 (Digital Groceries' Pen page mockup, 21 Nov 2026) ────────────────────────────────────────────────────────────────────────
    // The Eraser tab is gone: the Pen page's "Tip Feel | Eraser Feel" switch swaps the page's own settings between the two pen
    // ends ("Clicking Pen button brings the user to the pen settings and clicking the eraser button brings the user in eraser
    // settings menu"): the feel, Manual Pressure, Double Click Fix (tip only) and Pen / Eraser input. The pen buttons are shared
    // and stay the same either way (Digital Groceries ~12:40). "1 Can you make them two different setting values?": the eraser has its own feel
    // curve; a profile written by an older app has none, so the eraser starts with the tip's curve and keeps its own from the
    // first save on.

    /// <summary>The eraser end's own feel curve (a copy of the tip's until the profile has one).</summary>
    [ObservableProperty] private PressureCurveSettings _eraserCurve = PressureCurveSettings.Default;

    /// <summary>The switch at the top of the Pen page's left column: false = the tip's settings, true = the eraser's.</summary>
    [ObservableProperty] private bool _eraserSettingsShown;

    /// <summary>The pen tip touches the tablet. Build 17 (Digital Groceries' list 1, item 1): the round "Tip" circle no longer lights for it.</summary>
    [ObservableProperty] private bool _tipPressed;
    /// <summary>The eraser touches the tablet (the "Eraser" circle no longer lights for it, build 17).</summary>
    [ObservableProperty] private bool _eraserPressed;

    /// <summary>The other side of <see cref="EraserSettingsShown"/>, for the "Tip Feel" half of the switch.</summary>
    public bool TipSettingsShown
    {
        get => !EraserSettingsShown;
        set { if (value) EraserSettingsShown = false; }
    }

    partial void OnEraserSettingsShownChanged(bool value)
    {
        OnPropertyChanged(nameof(TipSettingsShown));
        NotifyActiveCurve();
        AppLog.Info($"Pen page: the {(value ? "eraser" : "pen tip")}'s settings are shown.");
    }

    /// <summary>Eraser Feel, Soft (left) to Firm (right), like <see cref="TipFeel"/> for the eraser's own curve.</summary>
    public double EraserFeel
    {
        get => -EraserCurve.Softness;
        set
        {
            var soft = Math.Clamp(-value, SoftnessMin, SoftnessMax);
            if (Math.Abs(EraserCurve.Softness - soft) > 1e-6) EraserCurve = EraserCurve with { Softness = soft };
        }
    }

    /// <summary>The Eraser Feel number box, 1 (Soft) to 100 (Firm); 50 is the straight curve.</summary>
    public string EraserFeelInput
    {
        get => FeelScale.ToDisplay(EraserFeel).ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (TypedValue.TryParse(value, FeelScale.Min, FeelScale.Max, 0, out var v)) EraserFeel = FeelScale.FromDisplay((int)v);
            OnPropertyChanged();
        }
    }

    partial void OnEraserCurveChanged(PressureCurveSettings value)
    {
        OnPropertyChanged(nameof(EraserFeel));
        OnPropertyChanged(nameof(EraserFeelInput));
        NotifyActiveCurve();
        SchedulePersist();
    }

    // ── The curve editor under Customise… edits the end whose settings are shown ──────────────────────────────────────────────

    /// <summary>The tip's curve, or the eraser's while the eraser's settings are shown.</summary>
    public PressureCurveSettings ActiveCurve
    {
        get => EraserSettingsShown ? EraserCurve : Curve;
        set { if (EraserSettingsShown) EraserCurve = value; else Curve = value; }
    }

    public double ActiveSoftness
    {
        get => ActiveCurve.Softness;
        set { if (Math.Abs(ActiveCurve.Softness - value) > 1e-6) ActiveCurve = ActiveCurve with { Softness = value }; }
    }

    public bool ActiveCutBelowMinimum
    {
        get => ActiveCurve.MinApproach == PressureMinApproach.Cut;
        set
        {
            var want = value ? PressureMinApproach.Cut : PressureMinApproach.Clamp;
            if (ActiveCurve.MinApproach != want) ActiveCurve = ActiveCurve with { MinApproach = want };
        }
    }

    public string ActiveDeadzoneText => ActiveCurve.InputMinimum.ToString("0.0000");

    /// <summary>The editor's title: whose curve it shows.</summary>
    public string ActiveCurveTitle => EraserSettingsShown
        ? Localizer.Te("tablet.eraser.feel_details", "Eraser Feel details")
        : Localizer.Te("tablet.pen.feel_details", "Tip Feel details");

    [RelayCommand]
    private void ApplyActivePreset(string softness) => ActiveCurve = PressureCurveSettings.Default with
    {
        Softness = double.Parse(softness, System.Globalization.CultureInfo.InvariantCulture),
    };

    private void NotifyActiveCurve()
    {
        OnPropertyChanged(nameof(ActiveCurve));
        OnPropertyChanged(nameof(ActiveSoftness));
        OnPropertyChanged(nameof(ActiveCutBelowMinimum));
        OnPropertyChanged(nameof(ActiveDeadzoneText));
        OnPropertyChanged(nameof(ActiveCurveTitle));
    }

    /// <summary>Loads the eraser's curve from the profile (the tip's when it has none), without saving.</summary>
    private void LoadEraserCurve()
    {
        var own = PressureCurveProfile.ReadEraserCurve(_settings, _profile.Tablet ?? "");
        var was = _skipCurvePersist;
        _skipCurvePersist = true;
        try { EraserCurve = own ?? Curve; }
        finally { _skipCurvePersist = was; }
    }

    /// <summary>The pen sample's end lights its round label on the drawing while it touches the tablet.</summary>
    private void UpdatePressedEnds(PenSample s)
    {
        TipPressed = s.IsDown && !s.IsEraser && s.Pressure > 0;
        EraserPressed = s.IsDown && s.IsEraser && s.Pressure > 0;
    }

    // ── Right column texts (Digital Groceries' mockup and his 21 Nov answers) ─────────────────────────────────────────────────────────────
    public string RadialTransparencyTitle => Localizer.Te("radial_spot.transparency", "Radial Menu Transparency");
    public string RadialSizeTitle => Localizer.Te("radial_spot.size", "Radial Menu Size");
    public string PaletteSizeTitle => Localizer.Te("palette_spot.size", "Colour Palette Size");
    public string EraserLabel => Localizer.Te("tablet.pen.eraser_label", "Eraser");
    public string TipLabel => Localizer.Te("tablet.pen.tip_label", "Tip");
    public string TipFeelSwitchLabel => Localizer.Te("tablet.pen.tip_feel", "Tip Feel");
    public string EraserFeelSwitchLabel => Localizer.Te("tablet.eraser.feel", "Eraser Feel");
    public string ManualPressureTitle => Localizer.Te("tablet.pen.manual_pressure", "Manual Pressure");

    /// <summary>The line under the colour palette: "By default…" while the radial menu (whose button, pressed twice, shows the
    /// palette) is on its factory pen button 2; else which button opens it, or that none does.</summary>
    [ObservableProperty] private string _paletteSpotLine = "";
}
