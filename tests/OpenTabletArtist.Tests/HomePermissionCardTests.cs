using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Item 7 (#15): Home stops offering Reset permissions once both grants are there.</summary>
public class HomePermissionCardTests
{
    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, true, true)]
    public void Evaluate(bool trusted, bool pen, bool accessibilityMissing, bool permissionsMissing)
        => Assert.Equal((accessibilityMissing, permissionsMissing), DashboardViewModel.EvaluatePermissions(trusted, pen));

    [Fact]
    public void CardGoes_AfterSetup_ButStaysForDiskImage()
    {
        var (_, missing) = DashboardViewModel.EvaluatePermissions(true, true);
        Assert.False(DashboardViewModel.PermissionsCardVisible(missing, runningFromDiskImage: false));
        Assert.True(DashboardViewModel.PermissionsCardVisible(missing, runningFromDiskImage: true));   // by design
    }
}
