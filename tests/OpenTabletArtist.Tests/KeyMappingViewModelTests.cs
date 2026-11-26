using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #212, the "Set up my buttons" sheet without its window: the lit key, the words, and what the
/// driver's reports and the Skip / Back / Stop / Start again buttons do.</summary>
public class KeyMappingViewModelTests
{
    private static bool[] Keys(int count, params int[] down)
    {
        var s = new bool[count];
        foreach (var d in down) s[d] = true;
        return s;
    }

    /// <summary>A press and its release, as the driver reports them.</summary>
    private static void Tap(KeyMappingViewModel vm, int count, int key)
    {
        vm.OnKeys(Keys(count, key));
        vm.OnKeys(Keys(count));
    }

    [Fact]
    public void ItStartsWithTheTopKeyLit()
    {
        var vm = new KeyMappingViewModel(4);
        Assert.Equal(new[] { true, false, false, false }, vm.Places.Select(p => p.IsLit));
        Assert.Equal("Key 1 of 4", vm.StepText);
        Assert.Equal("", vm.GroupText);
        Assert.Equal("Press the lit key on your tablet.", vm.Instruction);
        Assert.False(vm.CanGoBack);
        Assert.True(vm.IsRunning);
    }

    [Fact]
    public void APressRecordsTheLitKeyAndLightsTheNext()
    {
        var vm = new KeyMappingViewModel(3);
        var changes = 0; vm.PlaceChanged += () => changes++;
        Tap(vm, 3, 2);
        Assert.True(vm.Places[0].IsRecorded);
        Assert.Equal(new[] { false, true, false }, vm.Places.Select(p => p.IsLit));
        Assert.Equal("Key 2 of 3", vm.StepText);
        Assert.True(vm.CanGoBack);
        Assert.Equal(1, changes);                        // the release is not a second press
        Assert.Equal(new[] { 2, -1, -1 }, vm.Result.Keys);
    }

    [Fact]
    public void HoldingAKeyDownIsOnePress()
    {
        var vm = new KeyMappingViewModel(3);
        vm.OnKeys(Keys(3, 1));
        vm.OnKeys(Keys(3, 1));
        vm.OnKeys(Keys(3, 1));
        Assert.Equal(1, vm.RecordedCount);
        Assert.Equal("", vm.Message);
    }

    [Fact]
    public void AKeyAlreadyDoneGetsARemarkAndChangesNothing()
    {
        var vm = new KeyMappingViewModel(3);
        Tap(vm, 3, 1);
        Tap(vm, 3, 1);
        Assert.Equal("You pressed a key you already did. Press the lit one.", vm.Message);
        Assert.Equal(1, vm.RecordedCount);
        Assert.True(vm.Places[1].IsLit);
        Tap(vm, 3, 0);                                   // the right key clears the remark
        Assert.Equal("", vm.Message);
    }

    [Fact]
    public void TwoKeysAtOnceAreNotGuessed()
    {
        var vm = new KeyMappingViewModel(3);
        vm.OnKeys(Keys(3, 0, 2));
        Assert.Equal("Press one key at a time.", vm.Message);
        Assert.Equal(0, vm.RecordedCount);
        Assert.True(vm.Places[0].IsLit);
    }

    [Fact]
    public void WhenNothingComesThroughTheSheetSaysSoAndSkipNotesItAsNotSeen()
    {
        var vm = new KeyMappingViewModel(2);
        vm.NothingCame();
        Assert.True(vm.NothingCameThrough);
        Assert.Equal("Nothing came through. Press the key again, or choose Skip.", vm.Message);
        vm.SkipCommand.Execute(null);
        Assert.Equal(KeyPlaceState.NotSeen, vm.Places[0].State);
        Assert.True(vm.Places[0].IsLeftOut);
        Assert.False(vm.NothingCameThrough);             // the next key starts with a fresh wait
        Assert.Equal("", vm.Message);
        Assert.True(vm.Places[1].IsLit);
    }

    [Fact]
    public void APressAfterNothingCameThroughStillCounts()
    {
        var vm = new KeyMappingViewModel(2);
        vm.NothingCame();
        Tap(vm, 2, 1);
        Assert.True(vm.Places[0].IsRecorded);
        Assert.Equal("", vm.Message);
    }

    [Fact]
    public void SkipChosenByTheUserIsNotedAsSkipped()
    {
        var vm = new KeyMappingViewModel(2);
        vm.SkipCommand.Execute(null);
        Assert.Equal(KeyPlaceState.Skipped, vm.Places[0].State);
    }

    [Fact]
    public void BackLightsTheKeyBeforeAgain()
    {
        var vm = new KeyMappingViewModel(3);
        Tap(vm, 3, 2);
        Tap(vm, 3, 0);
        vm.BackCommand.Execute(null);
        Assert.True(vm.Places[1].IsLit);
        Assert.Equal(KeyPlaceState.Waiting, vm.Places[1].State);
        Assert.Equal(1, vm.RecordedCount);
        vm.BackCommand.Execute(null);
        vm.BackCommand.Execute(null);                    // at the first key Back does nothing
        Assert.True(vm.Places[0].IsLit);
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public void WhenEveryKeyIsDoneTheSheetSaysSo()
    {
        var vm = new KeyMappingViewModel(2);
        Tap(vm, 2, 1);
        Tap(vm, 2, 0);
        Assert.True(vm.IsFinished);
        Assert.False(vm.IsRunning);
        Assert.Equal("All 2 keys are set up.", vm.Instruction);
        Assert.Equal("", vm.StepText);
        Assert.DoesNotContain(vm.Places, p => p.IsLit);
        Assert.Equal(new[] { 1, 0 }, vm.Result.Keys);
    }

    [Fact]
    public void StopKeepsWhatWasDoneAndSaysHowMany()
    {
        var vm = new KeyMappingViewModel(4);
        Tap(vm, 4, 3);
        vm.StopCommand.Execute(null);
        Assert.True(vm.IsFinished);
        Assert.Equal("1 of 4 keys are set up. The others come last in the list.", vm.Instruction);
        Tap(vm, 4, 0);                                   // presses after Stop change nothing
        Assert.Equal(new[] { 3, -1, -1, -1 }, vm.Result.Keys);
    }

    [Fact]
    public void StartAgainClearsEverything()
    {
        var vm = new KeyMappingViewModel(2);
        Tap(vm, 2, 1);
        Tap(vm, 2, 0);
        vm.StartAgainCommand.Execute(null);
        Assert.True(vm.IsRunning);
        Assert.Equal(0, vm.RecordedCount);
        Assert.True(vm.Places[0].IsLit);
        Assert.Equal("Key 1 of 2", vm.StepText);
    }

    [Fact]
    public void TwoGroupsAreNamedAndTheSecondStartsWithAGap()
    {
        var vm = new KeyMappingViewModel(8, new[] { 4, 4 });
        Assert.Equal("Group 1 of 2", vm.GroupText);
        Assert.Equal(new[] { false, false, false, false, true, false, false, false }, vm.Places.Select(p => p.StartsNewGroup));
        for (int k = 0; k < 4; k++) Tap(vm, 8, k);
        Assert.Equal("Group 2 of 2", vm.GroupText);
        Assert.Equal(new[] { 4, 4 }, vm.Result.Groups);
    }

    [Fact]
    public void TheResultRemembersThatTheTabletWasTurnedRound()
    {
        var vm = new KeyMappingViewModel(2, flipped: true);
        Tap(vm, 2, 0);
        Assert.True(vm.Result.Flipped);
    }

    [Fact]
    public void ATabletWithoutKeysHasNothingToSetUp()
    {
        var vm = new KeyMappingViewModel(0);
        Assert.True(vm.IsFinished);
        Assert.Equal("This tablet has no ExpressKeys to set up.", vm.Instruction);
    }
}
