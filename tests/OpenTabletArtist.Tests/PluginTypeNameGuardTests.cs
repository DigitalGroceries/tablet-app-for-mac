using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Guards against drift between <see cref="PressureCurveProfile.FilterTypeName"/> (a hardcoded
/// string, since the app doesn't reference the plugin assembly) and the plugin's actual filter type.
/// If the plugin is renamed without updating the constant, the app would silently write a
/// PluginSettingStore the daemon can't construct — this test fails loudly instead (#104).
/// </summary>
public class PluginTypeNameGuardTests
{
    [Fact]
    public void FilterTypeName_ResolvesToABuiltPluginPipelineElement()
    {
        var dll = FindPluginDll();
        Assert.True(dll != null, "Pen-dynamics plugin DLL not found — build the solution (it compiles the plugin).");

        var asm = Assembly.LoadFrom(dll!);
        var type = asm.GetType(PressureCurveProfile.FilterTypeName);
        Assert.True(type != null,
            $"PressureCurveProfile.FilterTypeName ('{PressureCurveProfile.FilterTypeName}') matches no type in the " +
            "plugin assembly — the plugin was renamed; update the constant.");
        Assert.Contains(type!.GetInterfaces(), i => i.Name.StartsWith("IPositionedPipelineElement"));
    }

    [Fact]
    public void CalibrationFilterTypeName_ResolvesToABuiltPluginPipelineElement()
    {
        var dll = FindPluginDll();
        Assert.True(dll != null, "Plugin DLL not found — build the solution (it compiles the plugin).");

        var asm = Assembly.LoadFrom(dll!);
        var type = asm.GetType(CalibrationProfile.FilterTypeName);
        Assert.True(type != null,
            $"CalibrationProfile.FilterTypeName ('{CalibrationProfile.FilterTypeName}') matches no type in the " +
            "plugin assembly — the calibration filter was renamed; update the constant (#127).");
        Assert.Contains(type!.GetInterfaces(), i => i.Name.StartsWith("IPositionedPipelineElement"));
    }

    [Fact]
    public void HoverFilterTypeName_ResolvesToABuiltPluginPipelineElement()
    {
        var dll = FindPluginDll();
        Assert.True(dll != null, "Plugin DLL not found — build the solution (it compiles the plugin).");

        var asm = Assembly.LoadFrom(dll!);
        var type = asm.GetType(HoverProfile.FilterTypeName);
        Assert.True(type != null,
            $"HoverProfile.FilterTypeName ('{HoverProfile.FilterTypeName}') matches no type in the " +
            "plugin assembly — the hover filter was renamed; update the constant (#188).");
        Assert.Contains(type!.GetInterfaces(), i => i.Name.StartsWith("IPositionedPipelineElement"));
    }

    [Theory]
    [InlineData(RingModeBinding.NextTypeName)]
    [InlineData(RingModeBinding.TurnTypeName)]
    public void RingModeTypeNames_ResolveToBuiltPluginBindings(string typeName)
    {
        var dll = FindPluginDll();
        Assert.True(dll != null, "Plugin DLL not found — build the solution (it compiles the plugin).");

        var asm = Assembly.LoadFrom(dll!);
        var type = asm.GetType(typeName);
        Assert.True(type != null,
            $"'{typeName}' matches no type in the plugin assembly — the ring mode binding was renamed; " +
            "update RingModeBinding's constants.");
        Assert.Contains(type!.GetInterfaces(), i => i.Name == "IStateBinding");
    }

    [Fact]
    public void RingModeTurn_HasAPropertyPerMode_AndNextHasCountAndNames()
    {
        var dll = FindPluginDll();
        Assert.True(dll != null, "Plugin DLL not found — build the solution (it compiles the plugin).");
        var asm = Assembly.LoadFrom(dll!);

        var turn = asm.GetType(RingModeBinding.TurnTypeName)!;
        for (int i = 0; i < RingModeBinding.MaxModes; i++)
            Assert.NotNull(turn.GetProperty(RingModeBinding.ModeKeysProperty(i)));
        var next = asm.GetType(RingModeBinding.NextTypeName)!;
        Assert.NotNull(next.GetProperty(RingModeBinding.ModeCountProperty));
        Assert.NotNull(next.GetProperty(RingModeBinding.ModeNamesProperty));
    }

    private const string PluginDll = "OpenTabletArtist.Dynamics.dll";

    // Walk up from the test output dir and locate the built plugin DLL (the test project doesn't
    // reference the plugin; the solution build produces it).
    //
    // Two layouts, because the output path is not always the repo's (#738): the default per-project
    // bin, and a shared output root — which is how the suite is run when the app is holding a lock on
    // the normal bin directory. Probing only the first made these tests fail for a reason that had
    // nothing to do with what they check.
    private static string? FindPluginDll()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            // The plugin targets net10.0 since OTD 0.6.8 (net8.0 before); prefer the current build so a
            // stale net8.0 DLL left in bin can't hide a rename.
            foreach (var tfm in new[] { "net10.0", "net8.0" })
            {
                foreach (var cfg in new[] { "Debug", "Release" })
                {
                    var candidate = Path.Combine(dir.FullName, "plugins", "OpenTabletArtist.Dynamics",
                        "bin", cfg, tfm, PluginDll);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            // Shared output root: the plugin's net8.0 folder sits beside the test binary's net10.0 one.
            var sibling = Path.Combine(dir.FullName, "net8.0", PluginDll);
            if (File.Exists(sibling)) return sibling;
        }
        return null;
    }
}
