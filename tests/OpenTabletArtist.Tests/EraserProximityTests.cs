using System.IO;
using OpenTabletDriver.Desktop.Interop.Input;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #89 (Daniel, CTH-461, GIMP 3.2): turning the pen over must make GIMP switch to the eraser's tool.
/// GTK on macOS keeps the tool that entered proximity first active until it leaves (gtk-3-24
/// gdk/quartz/gdkdevicemanager-core-quartz.c, _gdk_quartz_device_core_is_active), so the driver posts a leave for
/// the old end before the enter for the new one, as a real Wacom pen does.</summary>
public class EraserProximityTests
{
    [Fact]
    public void Turning_the_pen_over_leaves_with_the_tip_then_enters_with_the_eraser()
    {
        Assert.Equal(new[] { (false, false), (true, true) }, MacOSProximity.ToolChange(previous: false, eraser: true));
        Assert.Equal(new[] { (true, false), (false, true) }, MacOSProximity.ToolChange(previous: true, eraser: false));
    }

    [Fact]
    public void The_first_report_only_enters_and_the_same_end_posts_nothing()
    {
        Assert.Equal(new[] { (true, true) }, MacOSProximity.ToolChange(previous: null, eraser: true));
        Assert.Empty(MacOSProximity.ToolChange(previous: false, eraser: false));
    }

    /// <summary>The driver really uses the plan: SetEraser posts each step with its own enter flag.</summary>
    [Fact]
    public void SetEraser_posts_the_planned_steps()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "external", "OpenTabletDriver", "OpenTabletDriver.Desktop",
            "Interop", "Input", "MacOSVirtualMouse.cs"));
        Assert.Contains("foreach (var (eraser, enter) in MacOSProximity.ToolChange(previous, isEraser))", src);
        Assert.Contains("tabletProximityEventEnterProximity, enter ? 1 : 0", src);
    }

    static string RepoRoot()
    {
        for (var d = new DirectoryInfo(System.AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "external", "OpenTabletDriver"))) return d.FullName;
        throw new DirectoryNotFoundException("repo root");
    }
}
