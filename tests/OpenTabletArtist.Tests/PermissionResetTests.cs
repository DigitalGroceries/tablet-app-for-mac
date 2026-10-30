using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PermissionResetTests
{
    [Fact]
    public void Commands_ResetBothServicesForAppAndDriver()
    {
        var c = PermissionReset.Commands();
        Assert.Equal(4, c.Count);
        Assert.Contains(c, a => a[1] == "ListenEvent" && a[2] == "com.freetabletapp.mac");
        Assert.Contains(c, a => a[1] == "Accessibility" && a[2] == "com.freetabletapp.mac");
        Assert.Contains(c, a => a[1] == "ListenEvent" && a[2] == "com.freetabletapp.mac.daemon");
        Assert.Contains(c, a => a[1] == "Accessibility" && a[2] == "com.freetabletapp.mac.daemon");
        Assert.All(c, a => Assert.Equal("reset", a[0]));
    }

    [Theory]
    [InlineData("/Applications/Tablet App for Mac.app/Contents/MacOS/", false)]
    [InlineData("/Volumes/Tablet App for Mac/Tablet App for Mac.app/Contents/MacOS/", true)]
    [InlineData("/private/var/folders/xy/T/AppTranslocation/ABC/d/Tablet App for Mac.app/Contents/MacOS/", true)]
    [InlineData("", false)]
    public void DetectsDiskImageOrTranslocation(string path, bool expected)
        => Assert.Equal(expected, PermissionReset.IsRunningFromDiskImageOrTranslocated(path));

    [Fact]
    public void UserTextNeverSaysDaemon()
    {
        var en = System.IO.File.ReadAllText(System.IO.Path.Combine(FindRoot(), "OpenTabletArtist/Assets/i18n/en.json"));
        foreach (var line in en.Split('\n'))
            if (line.Contains("\"home.perm_reset.")) Assert.DoesNotContain("daemon", line, System.StringComparison.OrdinalIgnoreCase);
    }

    static string FindRoot()
    {
        var d = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "OpenTabletArtist/OpenTabletArtist.csproj"))) d = d.Parent;
        return d!.FullName;
    }
}
