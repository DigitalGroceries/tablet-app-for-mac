using System;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>The "App window switching fix" switch on Advanced › Daemon (Version 29, TASKS #85).</summary>
public partial class AppSwitchFixViewModel : ObservableObject
{
    /// <summary>The driver part it controls exists on macOS only. Tests set this.</summary>
    public static bool ShowOnThisSystem { get; set; } = OperatingSystem.IsMacOS();

    public bool IsShown => ShowOnThisSystem || UiPlatform.IsMac;

    public bool On
    {
        get => AppSwitchFix.Enabled;
        set
        {
            if (value == AppSwitchFix.Enabled) return;
            AppSwitchFix.Enabled = value;
            OnPropertyChanged();
        }
    }

    public string Label => Localizer.Te("appswitch.toggle", "App window switching fix");
    public string Description => Localizer.Te("appswitch.description",
        "Leave this off unless tapping another app's window with the pen doesn't switch to that app. You'd see it if the menu at the top of the screen still shows the app you were in. Turning it on can make that first tap up to 1 second slower.");
}
