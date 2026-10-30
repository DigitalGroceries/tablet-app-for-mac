using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>.tabletsettings is a declared document type with its own icon (Finder shows the app icon on a page).</summary>
public class SettingsFileTypeTests
{
    static string Root
    {
        get
        {
            var d = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenTabletArtist.slnx"))) d = d.Parent;
            return d!.FullName;
        }
    }

    [Theory]
    [InlineData("grok-scripts/dmgroot/Tablet App for Mac.app/Contents/Info.plist")]
    [InlineData("scripts/bundle-macos-app.sh")]
    public void InfoPlist_DeclaresTheSettingsType(string file)
    {
        var s = File.ReadAllText(Path.Combine(Root, file));
        Assert.Contains("CFBundleDocumentTypes", s);
        Assert.Contains("UTExportedTypeDeclarations", s);
        Assert.Contains(SettingsBundle.TypeIdentifier, s);
        Assert.Contains("<string>" + SettingsBundle.Extension + "</string>", s);
        Assert.Contains("public.zip-archive", s);
        Assert.Contains("<string>SettingsDocument</string>", s);
    }

    [Fact]
    public void DocumentIcon_IsShippedByThePackScript()
    {
        Assert.True(File.Exists(Path.Combine(Root, "OpenTabletArtist/Assets/macos/SettingsDocument.icns")));
        Assert.Contains("SettingsDocument.icns", File.ReadAllText(Path.Combine(Root, "scripts/pack-macos-linux.sh")));
    }
}
