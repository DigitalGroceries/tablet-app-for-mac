using OpenTabletArtist.Domain;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 25: Double Click pen-button / ExpressKey action.</summary>
public class DoubleClickBindingTests
{
    private sealed class FakeDoubleClick : IMouseDoubleClickHandler
    {
        public int DoubleClickMoveTolerance { get; set; } = 40;
        public int Calls { get; private set; }
        public void PostDoubleClick() => Calls++;
    }

    [Fact]
    public void MakeMouseBinding_DoubleClick_uses_DoubleClickBinding_path()
    {
        var store = AuxKeyBinding.MakeMouseBinding(AuxKeyBinding.DoubleClickValue);
        Assert.NotNull(store);
        Assert.Equal(AuxKeyBinding.DoubleClickBindingPath, store!.Path);
        Assert.Equal(AuxKind.Mouse, AuxKeyBinding.ReadBinding(store)!.Kind);
        Assert.Equal(AuxKeyBinding.DoubleClickValue, AuxKeyBinding.ReadBinding(store)!.MouseButton);
        Assert.Equal("Double Click", AuxKeyBinding.Describe(AuxKeyBinding.ReadBinding(store)!));
    }

    [Fact]
    public void DoubleClickBinding_Press_requests_PostDoubleClick_once()
    {
        var fake = new FakeDoubleClick();
        var binding = new DoubleClickBinding { Pointer = fake };
        binding.Press(null!, null!);
        binding.Release(null!, null!);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public void MouseButtonOptions_include_DoubleClick()
    {
        Assert.Contains(AuxKeyBinding.MouseButtonOptions,
            o => o.Display == "Double Click" && o.Value == AuxKeyBinding.DoubleClickValue);
    }
}
