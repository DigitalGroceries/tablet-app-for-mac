using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Build Q: experimental hold-and-drag actions are off by default and labelled; saved choices load unchanged.</summary>
public class BuildQTests
{
    [Fact]
    public void Defaults_UseNoExperimentalDragAction()
    {
        Assert.All(RingModeBinding.Defaults, m =>
        {
            Assert.False(m.UsesDragRotate);
            Assert.False(m.UsesDragSize);
            Assert.False(m.UsesDragHardness);
        });
    }

    [Fact]
    public void DropdownLabels_NoLongerCarryExperimental_R18TheStepEditorSaysIt()
    {
        var n = RingModeRow.ActionNames;
        Assert.DoesNotContain("Experimental", n[(int)RingAction.SmoothRotate]);
        Assert.DoesNotContain("Experimental", n[(int)RingAction.SmoothZoom]);
        Assert.DoesNotContain("Experimental", n[(int)RingAction.SmoothSize]);
        Assert.DoesNotContain("Experimental", n[(int)RingAction.SmoothHardness]);
        Assert.DoesNotContain("Experimental", n[(int)RingAction.Keys]);
    }

    [Fact]
    public void SavedUserChoice_WithDragActions_LoadsUnchanged()
    {
        var saved = RingModeBinding.Defaults.ToList();
        saved[0] = saved[0] with { RotateSensitivity = 50 };
        saved[2] = saved[2] with { SizeSensitivity = 30 };
        saved[3] = saved[3] with { HardnessSensitivity = 70 };
        var back = RingModeBinding.Deserialize(RingModeBinding.Serialize(saved))!;
        Assert.Equal(50, back[0].RotateSensitivity);
        Assert.Equal(30, back[2].SizeSensitivity);
        Assert.Equal(70, back[3].HardnessSensitivity);
    }
}

/// <summary>Build Q: build P crashed sending the Swift-only "mainApp" selector. Every selector we send must be
/// its Objective-C name, and a missing class or selector must fall back instead of sending.</summary>
public class ObjCSelectorTests
{
    [Fact]
    public void SelectorTable_UsesObjCNames()
    {
        var expected = new[]
        {
            "mainAppService", "status", "registerAndReturnError:", "unregisterAndReturnError:", "localizedDescription",
            "UTF8String", "setIgnoresMouseEvents:", "setCollectionBehavior:", "window", "setLevel:", "screen", "frame",
            "setFrame:display:", "agentServiceWithPlistName:", "stringWithUTF8String:",
        };
        Assert.Equal(expected, OpenTabletArtist.Services.ObjCSelectors.All);
        Assert.DoesNotContain("mainApp", OpenTabletArtist.Services.ObjCSelectors.All);
        Assert.Equal(new[] { "runningApplicationWithProcessIdentifier:", "bundleIdentifier", "UTF8String" },
            OpenTabletArtist.Dynamics.RingGestureSelectors.All);
    }

    [Fact]
    public void NoSourceFile_SendsALiteralSelector_OrMainApp()
    {
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../.."));
        foreach (var dir in new[] { "OpenTabletArtist", "plugins" })
            foreach (var f in System.IO.Directory.EnumerateFiles(System.IO.Path.Combine(root, dir), "*.cs", System.IO.SearchOption.AllDirectories))
            {
                if (f.Contains("/obj/") || f.Contains("/bin/")) continue;
                var text = System.IO.File.ReadAllText(f);
                Assert.False(text.Contains("sel_registerName(\"mainApp\")"), f);
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, "sel_registerName\\(\""), $"literal selector in {f}");
            }
    }

    [Fact]
    public void ResolveMainApp_NoClass_ReturnsZero_WithoutSending()
    {
        bool sent = false;
        var r = OpenTabletArtist.Services.MacLoginItemService.ResolveMainApp(_ => System.IntPtr.Zero, (_, _) => true, (_, _) => { sent = true; return (System.IntPtr)1; });
        Assert.Equal(System.IntPtr.Zero, r);
        Assert.False(sent);
    }

    [Fact]
    public void ResolveMainApp_ClassDoesNotRespond_ReturnsZero_WithoutSending()
    {
        bool sent = false;
        var r = OpenTabletArtist.Services.MacLoginItemService.ResolveMainApp(_ => (System.IntPtr)5, (_, _) => false, (_, _) => { sent = true; return (System.IntPtr)1; });
        Assert.Equal(System.IntPtr.Zero, r);
        Assert.False(sent);
    }

    [Fact]
    public void ResolveMainApp_Responds_SendsMainAppService()
    {
        string? sel = null; string? checkedSel = null;
        var r = OpenTabletArtist.Services.MacLoginItemService.ResolveMainApp(n => n == "SMAppService" ? (System.IntPtr)5 : System.IntPtr.Zero,
            (_, s) => { checkedSel = s; return true; }, (_, s) => { sel = s; return (System.IntPtr)9; });
        Assert.Equal((System.IntPtr)9, r);
        Assert.Equal("mainAppService", sel);
        Assert.Equal("mainAppService", checkedSel);
    }

    [Fact]
    public void RespondsTo_NullReceiver_IsFalse() =>
        Assert.False(OpenTabletArtist.Services.ObjCGuard.RespondsTo(System.IntPtr.Zero, "status"));
}
