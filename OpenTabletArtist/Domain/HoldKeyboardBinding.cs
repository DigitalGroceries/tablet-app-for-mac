using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>
/// V29 #160 (Zak 20 Nov ~09:05-09:24): pen button 1 right-clicks on a quick press and shows or hides the floating keyboard when
/// held (~0.5 s). Factory setting for pen button 1. The other choice, "Drag with the right button", is the plain right click
/// (the button's Default), which can be held down to drag. Plugin type <c>OpenTabletArtist.Dynamics.HoldForKeyboardBinding</c>.
/// </summary>
public static class HoldKeyboardBinding
{
    public const string BindingTypeName = "OpenTabletArtist.Dynamics.HoldForKeyboardBinding";
    public const string ButtonProperty = "Button", HoldMsProperty = "HoldMs";
    public const int DefaultHoldMs = 500;

    public static bool Is(PluginSettingStore? store) => store?.Path == BindingTypeName;

    public static PluginSettingStore Make(string button = "Right", int holdMs = DefaultHoldMs)
    {
        var store = AuxKeyBinding.NewStore(BindingTypeName);
        store.Settings.Add(new PluginSetting(ButtonProperty, button));
        store.Settings.Add(new PluginSetting(HoldMsProperty, holdMs));
        return store;
    }
}
