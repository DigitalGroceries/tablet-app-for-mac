using System;
using System.Collections.Generic;
using System.Threading;
using OpenTabletArtist.Domain;
using HoldForKeyboardBinding = OpenTabletArtist.Dynamics.HoldForKeyboardBinding;
using FloatingKeyboardKeys = OpenTabletArtist.Dynamics.FloatingKeyboardKeys;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Output;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Platform.Pointer;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>V29 #160 (Zak 20 Nov ~09:05-09:24): pen button 1 right-clicks on a quick press and shows or hides the floating keyboard
/// when held ("what about long press instead of click?"), or, if the user chooses, is a plain right button to drag with.</summary>
[Collection("FloatingKeyboardFiles")]
public class HoldForKeyboardTests : IDisposable
{
    /// <summary>Like the Mac pointer: it posts at Flush, so a down and an up need a flush between them (Zak 20 Nov: "No right click").</summary>
    private sealed class FakeMouse : IMouseButtonHandler, ISynchronousPointer
    {
        public readonly List<string> Events = new();
        public void MouseDown(MouseButton button) { lock (Events) Events.Add("down " + button); }
        public void MouseUp(MouseButton button) { lock (Events) Events.Add("up " + button); }
        public void Flush() { lock (Events) Events.Add("flush"); }
        public void Reset() { }
    }

    private int _toggles;
    private readonly string _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ota-hold-" + Guid.NewGuid().ToString("N"));

    public HoldForKeyboardTests()
    {
        HoldForKeyboardBinding.ToggleRunner = () => Interlocked.Increment(ref _toggles);
        // keep the driver's key watcher on files of our own
        System.IO.Directory.CreateDirectory(_dir);
        FloatingKeyboardKeys.OpenFlagPath = System.IO.Path.Combine(_dir, "keyboard-open");
        FloatingKeyboardKeys.CommandFilePath = System.IO.Path.Combine(_dir, "keyboard-cmd");
    }

    public void Dispose()
    {
        HoldForKeyboardBinding.ToggleRunner = null;
        FloatingKeyboardKeys.Stop();
        FloatingKeyboardKeys.OpenFlagPath = FloatingKeyboardFiles.OpenFlag;
        FloatingKeyboardKeys.CommandFilePath = FloatingKeyboardFiles.CommandFile;
        try { System.IO.Directory.Delete(_dir, true); } catch (System.IO.IOException) { }
    }

    [Fact]
    public void AQuickPress_RightClicksWhenLetGo_AndNeverOpensTheKeyboard()
    {
        var mouse = new FakeMouse();
        var b = new HoldForKeyboardBinding { Pointer = mouse, HoldMs = 400 };
        b.Press(null!, null!);
        Assert.Empty(mouse.Events);                 // nothing yet: it might become a hold
        b.Release(null!, null!);
        Assert.Equal(new[] { "down Right", "flush", "up Right" }, mouse.Events);   // the down is posted before the up
        Thread.Sleep(600);
        Assert.Equal(0, _toggles);
    }

    [Fact]
    public void Holding_OpensTheKeyboard_AndLettingGoDoesNotClick()
    {
        var mouse = new FakeMouse();
        var b = new HoldForKeyboardBinding { Pointer = mouse, HoldMs = 150 };
        b.Press(null!, null!);
        var until = Environment.TickCount64 + 3000;
        while (Volatile.Read(ref _toggles) == 0 && Environment.TickCount64 < until) Thread.Sleep(10);
        Assert.Equal(1, _toggles);
        b.Release(null!, null!);
        Assert.Empty(mouse.Events);
        // a second hold toggles again (hides it)
        b.Press(null!, null!);
        until = Environment.TickCount64 + 3000;
        while (Volatile.Read(ref _toggles) == 1 && Environment.TickCount64 < until) Thread.Sleep(10);
        b.Release(null!, null!);
        Assert.Equal(2, _toggles);
        Assert.Empty(mouse.Events);
    }

    [Fact]
    public void TheToggleFile_CarriesANumberTheAppCanRead()
    {
        Assert.Equal(0, FloatingKeyboardFiles.ParseToggle(null));
        Assert.Equal(0, FloatingKeyboardFiles.ParseToggle("x"));
        Assert.Equal(638999999999L, FloatingKeyboardFiles.ParseToggle(" 638999999999\n"));
        Assert.EndsWith("keyboard-toggle", FloatingKeyboardFiles.ToggleRequest);
    }

    [Fact]
    public void RestoreFactorySettings_PutsHoldForKeyboardOnPenButton1_AndTheRadialMenuOnButton2()
    {
        var p = new Profile
        {
            Tablet = "Wacom PTK-440",
            OutputMode = new PluginSettingStore(typeof(AbsoluteMode)),
            BindingSettings = new BindingSettings
            {
                PenButtons = new PluginSettingStoreCollection { PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton, 1),
                                                                PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton, 2) },
            },
        };
        var merged = FactorySettingsMerge.Apply(null, new Settings { Profiles = new ProfileCollection { p } });
        var buttons = merged.Profiles[0].BindingSettings.PenButtons;
        Assert.True(HoldKeyboardBinding.Is(buttons[0]));
        Assert.Equal("Right", buttons[0]!.Settings[0].Value?.ToString());
        Assert.True(RadialMenuBinding.Is(buttons[1]));
    }

    [Fact]
    public void ThePenButtonCard_SaysWhatButton1Does()
    {
        var row = new OpenTabletArtist.ViewModels.PenSwitchRowViewModel(PenSwitchKind.PenButton, 1, HoldKeyboardBinding.Make(), true,
            (_, _, _) => System.Threading.Tasks.Task.CompletedTask);
        Assert.Equal("Right click; hold for the keyboard", row.CurrentPlainLabel);
    }
}

/// <summary>V29 #162 (Zak 20 Nov ~09:10-09:12: "the keyboard is microscopic and you did not include a keyboard resizing slider", "Let a
/// corner dragging resize the floating keyboard too") and the factory look (~09:26: transparency 40%).</summary>
[Collection("FloatingKeyboardFiles")]
public class FloatingKeyboardSizeTests
{
    [Fact]
    public void FactoryLook_Is40PercentSeeThrough_And150PercentSize()
    {
        Assert.Equal(40, FloatingKeyboard.DefaultTransparency);
        Assert.Equal(150, FloatingKeyboard.DefaultSize);
        FloatingKeyboard.SizePercent = 220;
        FloatingKeyboard.Transparency = 10;
        FloatingKeyboard.ResetToFactory();
        Assert.Equal(150, FloatingKeyboard.SizePercent);
        Assert.Equal(40, FloatingKeyboard.Transparency);
    }

    [Fact]
    public void TheSize_ScalesTheWholeKeyboard_AndStaysInRange()
    {
        var (bw, bh) = FloatingKeyboard.BaseSize();
        FloatingKeyboard.SizePercent = 200;
        var (w, h) = FloatingKeyboard.Size();
        Assert.Equal(Math.Round(bw * 2), w);
        Assert.Equal(Math.Round(bh * 2), h);
        FloatingKeyboard.SizePercent = 1000;
        Assert.Equal(FloatingKeyboard.MaxSize, FloatingKeyboard.SizePercent);
        FloatingKeyboard.SizePercent = 5;
        Assert.Equal(FloatingKeyboard.MinSize, FloatingKeyboard.SizePercent);
        FloatingKeyboard.ResetToFactory();
    }

    [Fact]
    public void ACornerDrag_ChangesTheSizeByHowFarTheEdgeMoved()
    {
        Assert.Equal(300, FloatingKeyboard.SizeForDrag(150, 700, 700));    // twice as wide
        Assert.Equal(75, FloatingKeyboard.SizeForDrag(150, 700, -350));    // half as wide
        Assert.Equal(60, FloatingKeyboard.SizeForDrag(150, 700, -690));    // never below 60%
        Assert.Equal(300, FloatingKeyboard.SizeForDrag(150, 700, 5000));   // never above 300%
        Assert.Equal(150, FloatingKeyboard.SizeForDrag(150, 0, 50));       // no width yet: unchanged
    }

    [Fact]
    public void TheSizeBox_ReadsPercentAndClamps()
    {
        var look = new OpenTabletArtist.ViewModels.FloatingKeyboardLookViewModel();
        look.SizeInput = "180%";
        Assert.Equal(180, look.Size);
        look.SizeInput = "999";
        Assert.Equal(300, look.Size);
        look.SizeInput = "abc";
        Assert.Equal(300, look.Size);
        FloatingKeyboard.ResetToFactory();
    }
}
