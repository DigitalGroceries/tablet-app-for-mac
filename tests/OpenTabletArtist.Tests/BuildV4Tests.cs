using OpenTabletArtist.Controls;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 4: the ring's raw zero sits at 3 o'clock; marker and readout are turned back 90° so 12 o'clock is 0°.</summary>
public class BuildV4Tests
{
    [Theory]
    [InlineData(0.25, 0)]    // finger at 12 o'clock
    [InlineData(0.5, 90)]    // 3 o'clock
    [InlineData(0.75, 180)]  // 6 o'clock
    [InlineData(0.0, 270)]   // 9 o'clock
    public void MarkerAngle_MatchesTheFinger(double norm, double deg) =>
        Assert.Equal(deg, WheelGauge.AngleDeg(norm), 6);
}
