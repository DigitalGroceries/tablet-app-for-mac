using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Version 28 (TASKS #77, Daniel's CTH-461; plan agreed with Devin 15 Nov 22:27): a pen button or ExpressKey that
/// turns finger touch on or off each time it is pressed, like Wacom's "Touch On/Off". Offered only for tablets
/// whose touch the app drives (<see cref="TouchTrackpad.Tablets"/>). Plugin type
/// <c>OpenTabletArtist.Dynamics.TouchToggleBinding</c>; it flips the touch trackpad until the switch on the
/// tablet's page is changed or the driver restarts, and has no settings of its own.
/// </summary>
public static class TouchToggleBinding
{
    public const string BindingTypeName = "OpenTabletArtist.Dynamics.TouchToggleBinding";

    public static bool Is(PluginSettingStore? store) => store?.Path == BindingTypeName;

    public static PluginSettingStore Make() => AuxKeyBinding.NewStore(BindingTypeName);

    public static string Describe() => Services.Localizer.Te("binding.touch.name", "Touch on/off");
}
