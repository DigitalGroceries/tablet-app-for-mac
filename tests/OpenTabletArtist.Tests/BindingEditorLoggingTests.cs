using System;
using System.Collections.Generic;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>No silent failures (Zak 17:15, pen button 2 "refused" to take the Radial menu once): the binding editor
/// writes what happens to app.log: opened, tab chosen, Save / Clear / Cancel, and a warning when the Radial menu
/// is chosen while its switch in Settings is off.</summary>
[Collection(DisplayEnumeratorCollection.Name)]
public class BindingEditorLoggingTests
{
    private static List<string> Capture(Action act)
    {
        var lines = new List<string>();
        Action<string> h = l => { lock (lines) lines.Add(l); };
        AppLog.LineWritten += h;
        try { act(); } finally { AppLog.LineWritten -= h; }
        lock (lines) return new List<string>(lines);
    }

    [Fact]
    public void OpenTabChooseSave_AreAllLogged()
    {
        var was = RadialMenuOverlay.Enabled;
        try
        {
            RadialMenuOverlay.Enabled = true;
            var lines = Capture(() =>
            {
                var vm = new BindingEditorViewModel(AuxBinding.Unbound, "Pen button 2");
                vm.IsRadialTab = true;
                vm.SaveCommand.Execute(null);
            });
            Assert.Contains(lines, l => l.Contains("Binding editor opened: 'Pen button 2'"));
            Assert.Contains(lines, l => l.Contains("Radial menu tab chosen"));
            Assert.Contains(lines, l => l.Contains("Save clicked on the Radial menu tab") && l.Contains("Radial menu"));
            Assert.DoesNotContain(lines, l => l.Contains("switch (Settings › System) is OFF"));
        }
        finally { RadialMenuOverlay.Enabled = was; }
    }

    [Fact]
    public void RadialChosenWhileTheSwitchIsOff_IsWarnedAbout()
    {
        var was = RadialMenuOverlay.Enabled;
        try
        {
            RadialMenuOverlay.Enabled = false;
            var lines = Capture(() =>
            {
                var vm = new BindingEditorViewModel(AuxBinding.Unbound, "Pen button 2");
                vm.IsRadialTab = true;
                vm.SaveCommand.Execute(null);
            });
            Assert.Contains(lines, l => l.Contains("OFF") && l.Contains("Show the radial menu"));
        }
        finally { RadialMenuOverlay.Enabled = was; }
    }

    [Fact]
    public void CancelAndClear_AreLogged()
    {
        var lines = Capture(() =>
        {
            new BindingEditorViewModel(AuxBinding.Unbound, "Pen button 1").CancelDialogCommand.Execute(null);
            new BindingEditorViewModel(AuxBinding.Unbound, "Pen button 1").ClearCommand.Execute(null);
        });
        Assert.Contains(lines, l => l.Contains("Cancel (nothing changed)"));
        Assert.Contains(lines, l => l.Contains("Clear clicked"));
    }
}
