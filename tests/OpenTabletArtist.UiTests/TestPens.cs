using OpenTabletArtist.Domain;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.UiTests;

/// <summary>R18: give a bare test profile the default tip and eraser actions, as a real profile has.
/// Without them both ends count as disabled and the live bars (correctly) stay at 0%.</summary>
internal static class TestPens
{
    public static Profile Arm(Profile p)
    {
        p.BindingSettings.TipButton ??= PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
        p.BindingSettings.EraserButton ??= PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser);
        return p;
    }
}
