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
        Assert.True(File.Exists(Path.Combine(Root, "packaging/macos/SettingsDocument.icns")));
        Assert.Contains("SettingsDocument.icns", File.ReadAllText(Path.Combine(Root, "scripts/pack-macos-linux.sh")));
    }

    /// <summary>Item 27 (#28): the fallback path (no prebuilt AppIcon.icns) still ships the document icon.</summary>
    [Fact]
    public void FallbackBundle_StillCopiesTheDocumentIcon()
    {
        if (System.OperatingSystem.IsWindows()) return;
        var t = Path.Combine(Path.GetTempPath(), "bundletest-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(t, "scripts"));
            Directory.CreateDirectory(Path.Combine(t, "packaging/macos"));
            Directory.CreateDirectory(Path.Combine(t, "OpenTabletArtist/Assets"));   // no macos/AppIcon.icns → fallback
            Directory.CreateDirectory(Path.Combine(t, "bin"));
            Directory.CreateDirectory(Path.Combine(t, "pub"));
            File.Copy(Path.Combine(Root, "scripts/bundle-macos-app.sh"), Path.Combine(t, "scripts/bundle-macos-app.sh"));
            File.WriteAllText(Path.Combine(t, "packaging/macos/SettingsDocument.icns"), "doc");
            File.WriteAllText(Path.Combine(t, "OpenTabletArtist/Assets/appicon.png"), "png");
            File.WriteAllText(Path.Combine(t, "pub/OpenTabletArtist"), "bin");
            // Stand-ins for the macOS-only sips/iconutil.
            File.WriteAllText(Path.Combine(t, "bin/sips"), "#!/bin/sh\nexit 0\n");
            File.WriteAllText(Path.Combine(t, "bin/iconutil"), "#!/bin/sh\nwhile [ $# -gt 0 ]; do [ \"$1\" = -o ] && touch \"$2\"; shift; done\n");
            foreach (var f in new[] { "bin/sips", "bin/iconutil", "scripts/bundle-macos-app.sh" })
                File.SetUnixFileMode(Path.Combine(t, f), (UnixFileMode)0b111_101_101);
            var psi = new System.Diagnostics.ProcessStartInfo("bash",
                new[] { Path.Combine(t, "scripts/bundle-macos-app.sh"), Path.Combine(t, "pub"), Path.Combine(t, "X.app"), "1.0" })
                { RedirectStandardError = true, RedirectStandardOutput = true };
            psi.Environment["PATH"] = Path.Combine(t, "bin") + ":" + System.Environment.GetEnvironmentVariable("PATH");
            using var p = System.Diagnostics.Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, err);
            Assert.True(File.Exists(Path.Combine(t, "X.app/Contents/Resources/AppIcon.icns")));
            Assert.True(File.Exists(Path.Combine(t, "X.app/Contents/Resources/SettingsDocument.icns")));
        }
        finally { try { Directory.Delete(t, true); } catch { } }
    }
}
