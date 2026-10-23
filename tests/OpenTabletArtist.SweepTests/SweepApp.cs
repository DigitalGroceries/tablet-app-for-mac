using Avalonia;
using Avalonia.Headless;
using OpenTabletArtist;

[assembly: AvaloniaTestApplication(typeof(OpenTabletArtist.SweepTests.SweepApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OpenTabletArtist.SweepTests;

/// <summary>The real App on a headless platform with real Skia rendering, so frames can be captured.</summary>
public static class SweepApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}
