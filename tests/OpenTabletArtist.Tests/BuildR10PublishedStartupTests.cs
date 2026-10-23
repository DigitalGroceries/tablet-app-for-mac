using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>R10: R9 crashed at startup on the Mac because the single-file publish left libSkiaSharp /
/// libHarfBuzzSharp / libAvaloniaNative beside the exe and only the exe was installed. This publishes the
/// app the way the Mac build does (single file, self-contained), checks that nothing is left beside the
/// executable and that the natives are embedded, then runs the real published app with its own empty
/// settings folder and checks it is still alive after 15 s (startup, tray icon, main window).</summary>
[Trait("Category", "Publish")]
public class BuildR10PublishedStartupTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "OpenTabletArtist", "OpenTabletArtist.csproj"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static (int code, string output) Run(string file, string args, string cwd, int timeoutMs)
    {
        var psi = new ProcessStartInfo(file, args) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(timeoutMs), $"{file} {args} timed out");
        return (p.ExitCode, o.Result + e.Result);
    }

    // One test, run in order: two publishes of the same project at once fight over obj/ and fail.
    [Fact]
    public void SingleFilePublish_EmbedsNatives_AndTheAppStaysUp()
    {
        PublishAndCheck("osx-arm64", launch: false);
        PublishAndCheck("linux-x64", launch: true);
    }

    private static void PublishAndCheck(string rid, bool launch)
    {
        if (launch && (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))))
            return; // the live launch needs a Linux desktop; the osx-arm64 layout check runs everywhere
        var root = Root();
        var outDir = Path.Combine(Path.GetTempPath(), "ota-r10-publish-" + rid + "-" + Guid.NewGuid().ToString("N"));
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        try
        {
            var (code, output) = Run(dotnet,
                $"publish OpenTabletArtist/OpenTabletArtist.csproj -c Release -r {rid} --self-contained -p:PublishSingleFile=true -o \"{outDir}\"",
                root, 600_000);
            Assert.True(code == 0, output.Length > 3000 ? output[^3000..] : output);
            var (ccode, coutput) = Run("bash", $"tools/check-single-file-publish.sh \"{outDir}\"", root, 60_000);
            Assert.True(ccode == 0, coutput);
            if (!launch) return;

            var data = Path.Combine(outDir, "appdata"); Directory.CreateDirectory(data);
            var psi = new ProcessStartInfo(Path.Combine(outDir, "OpenTabletArtist")) { RedirectStandardError = true, RedirectStandardOutput = true };
            psi.Environment["OTA_APPDATA"] = data;
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            Thread.Sleep(15_000);
            var alive = !p.HasExited;
            if (alive) p.Kill(entireProcessTree: true);
            p.WaitForExit(10_000);
            Assert.True(alive, "the published app exited during startup: " + err.Result);
            Assert.DoesNotContain("Unhandled exception", err.Result);
        }
        finally { try { Directory.Delete(outDir, true); } catch { } }
    }
}
