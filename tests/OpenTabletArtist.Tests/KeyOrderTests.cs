using System;
using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>TASKS #212, step-by-step button mapping: the run itself (what a press, Skip, Back and Stop do), the saved
/// result, and the order the ExpressKeys page draws the keys in. No screen and no tablet needed.</summary>
public class KeyOrderTests
{
    // ── the run ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EachPressIsRecordedForTheLitPlaceAndTheNextPlaceLights()
    {
        var s = new KeyMappingSession(4);
        Assert.Equal(0, s.Place);
        Assert.Equal(KeyPressResult.Recorded, s.Press(2));
        Assert.Equal(1, s.Place);
        Assert.Equal(KeyPressResult.Recorded, s.Press(0));
        Assert.Equal(KeyPressResult.Recorded, s.Press(3));
        Assert.False(s.IsFinished);
        Assert.Equal(KeyPressResult.Recorded, s.Press(1));
        Assert.True(s.IsFinished);
        Assert.Equal(new[] { 2, 0, 3, 1 }, s.Result().Keys);
        Assert.True(s.Result().IsComplete(4));
    }

    [Fact]
    public void AKeyThatWasAlreadyRecordedChangesNothing()
    {
        var s = new KeyMappingSession(3);
        s.Press(1);
        Assert.Equal(KeyPressResult.AlreadyRecorded, s.Press(1));
        Assert.Equal(1, s.Place);                       // the same place is still lit
        Assert.Equal(new[] { 1, -1, -1 }, s.Result().Keys);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void AKeyTheTabletDoesNotHaveIsIgnored(int key)
    {
        var s = new KeyMappingSession(3);
        Assert.Equal(KeyPressResult.Ignored, s.Press(key));
        Assert.Equal(0, s.Place);
    }

    [Fact]
    public void SkipLeavesThePlaceWithoutAKeyAndGoesOn()
    {
        var s = new KeyMappingSession(3);
        s.Press(2);
        s.Skip();
        s.Press(0);
        Assert.True(s.IsFinished);
        Assert.Equal(new[] { 2, KeyOrderMap.NotSeen, 0 }, s.Result().Keys);
        Assert.Equal(KeyPlaceState.Skipped, s.StateOf(1));
        Assert.Equal(2, s.RecordedCount);
    }

    [Fact]
    public void NothingCameThroughIsNotedAsNotSeenAndIsNotAFault()
    {
        var s = new KeyMappingSession(2);
        s.Skip(nothingCameThrough: true);
        Assert.Equal(KeyPlaceState.NotSeen, s.StateOf(0));
        Assert.Equal(KeyPressResult.Recorded, s.Press(0));   // the run simply goes on
        Assert.True(s.IsFinished);
    }

    [Fact]
    public void BackLightsThePlaceBeforeAndItsKeyCanBePressedAgain()
    {
        var s = new KeyMappingSession(3);
        Assert.False(s.Back());                          // nothing before the first place
        s.Press(1);
        s.Press(2);
        Assert.True(s.Back());
        Assert.Equal(1, s.Place);
        Assert.Equal(KeyPlaceState.Waiting, s.StateOf(1));
        Assert.Equal(KeyPressResult.Recorded, s.Press(2)); // the same key again: it is free once more
        Assert.Equal(KeyPressResult.Recorded, s.Press(0));
        Assert.Equal(new[] { 1, 2, 0 }, s.Result().Keys);
    }

    [Fact]
    public void BackAfterTheLastPlaceOpensItAgain()
    {
        var s = new KeyMappingSession(2);
        s.Press(0);
        s.Press(1);
        Assert.True(s.IsFinished);
        Assert.True(s.Back());
        Assert.False(s.IsFinished);
        Assert.Equal(1, s.Place);
    }

    [Fact]
    public void StopEndsTheRunAndKeepsWhatWasDone()
    {
        var s = new KeyMappingSession(4);
        s.Press(3);
        s.Press(1);
        s.Stop();
        Assert.True(s.IsFinished);
        Assert.Equal(KeyPressResult.Ignored, s.Press(0));
        Assert.False(s.Back());
        Assert.Equal(new[] { 3, 1, -1, -1 }, s.Result().Keys);
        Assert.False(s.Result().IsComplete(4));
    }

    [Fact]
    public void ATabletWithNoKeysIsFinishedAtOnce()
    {
        var s = new KeyMappingSession(0);
        Assert.True(s.IsFinished);
        Assert.Empty(s.Result().Keys);
    }

    // ── two groups of keys (four above a ring and four below) ───────────────────────────────────

    [Fact]
    public void ThePlaceKnowsWhichGroupItIsIn()
    {
        var s = new KeyMappingSession(8, new[] { 4, 4 });
        Assert.Equal((0, 2), s.GroupOfPlace(0));
        Assert.Equal((0, 2), s.GroupOfPlace(3));
        Assert.Equal((1, 2), s.GroupOfPlace(4));
        Assert.Equal((1, 2), s.GroupOfPlace(7));
        Assert.Equal(new[] { 4, 4 }, s.Result().Groups);
    }

    [Fact]
    public void GroupsThatDoNotAddUpToTheKeysAreDropped()
    {
        var s = new KeyMappingSession(6, new[] { 4, 4 });
        Assert.Equal((0, 1), s.GroupOfPlace(5));
        Assert.Empty(s.Result().Groups);
    }

    // ── which key went down ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ANewPressIsTheKeyThatWentDownSinceTheLastReport()
    {
        Assert.Equal(2, KeyMappingSession.NewlyPressed(new[] { false, false, false }, new[] { false, false, true }));
        Assert.Equal(1, KeyMappingSession.NewlyPressed(null, new[] { false, true }));
    }

    [Fact]
    public void AKeyStillHeldFromBeforeIsNotANewPress()
    {
        Assert.Equal(-1, KeyMappingSession.NewlyPressed(new[] { true, false }, new[] { true, false }));
        Assert.Equal(1, KeyMappingSession.NewlyPressed(new[] { true, false }, new[] { true, true }));
        Assert.Equal(-1, KeyMappingSession.NewlyPressed(new[] { true, true }, new[] { false, false }));   // a release
    }

    [Fact]
    public void TwoKeysAtTheSameMomentAreNoPress()
    {
        Assert.Equal(-1, KeyMappingSession.NewlyPressed(new[] { false, false, false }, new[] { true, false, true }));
    }

    // ── the order the page draws the keys in ────────────────────────────────────────────────────

    [Fact]
    public void ThePageDrawsTheKeysInTheOrderTheySit()
    {
        var map = new KeyOrderMap(new[] { 2, 0, 3, 1 });
        Assert.Equal(new[] { 2, 0, 3, 1 }, map.DisplayOrder(4));
    }

    [Fact]
    public void KeysNobodyRecordedComeAfterTheRecordedOnesSoNoKeyIsMissing()
    {
        var map = new KeyOrderMap(new[] { 3, KeyOrderMap.NotSeen, 1, KeyOrderMap.NotSeen });
        Assert.Equal(new[] { 3, 1, 0, 2 }, map.DisplayOrder(4));
    }

    [Fact]
    public void ADamagedMappingNeverHidesAKeyOrShowsOneTwice()
    {
        var map = new KeyOrderMap(new[] { 1, 1, 7, -5, 0 });
        var order = map.DisplayOrder(4);
        Assert.Equal(new[] { 1, 0, 2, 3 }, order);
        Assert.Equal(4, order.Distinct().Count());
    }

    [Fact]
    public void TurningTheTabletRoundTurnsTheOrderRound()
    {
        var rightHanded = new KeyOrderMap(new[] { 2, 0, 1 }, flipped: false);
        Assert.Equal(new[] { 1, 0, 2 }, rightHanded.DisplayOrder(3, flippedNow: true));
        var leftHanded = new KeyOrderMap(new[] { 2, 0, 1 }, flipped: true);
        Assert.Equal(new[] { 2, 0, 1 }, leftHanded.DisplayOrder(3, flippedNow: true));
        Assert.Equal(new[] { 1, 0, 2 }, leftHanded.DisplayOrder(3, flippedNow: false));
    }

    [Fact]
    public void WithoutAMappingTheDriversOrderIsUsed()
    {
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, KeyOrderMap.Identity(5).DisplayOrder(5));
    }

    // ── saving ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AMappingComesBackAsItWasSaved()
    {
        var map = new KeyOrderMap(new[] { 5, 4, KeyOrderMap.NotSeen, 0, 1, 2, 3, 6 }, flipped: true, groups: new[] { 4, 4 });
        var back = KeyOrderMap.TryParse(map.ToJson());
        Assert.NotNull(back);
        Assert.Equal(map.Keys, back!.Keys);
        Assert.True(back.Flipped);
        Assert.Equal(new[] { 4, 4 }, back.Groups);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("{\"v\":1}")]
    [InlineData("{\"keys\":[\"a\",1]}")]
    [InlineData("[1,2,3]")]
    public void WhatCannotBeReadIsNoMapping(string? json) => Assert.Null(KeyOrderMap.TryParse(json));

    [Fact]
    public void TheStoreKeepsOneMappingPerModelAndARerunReplacesIt()
    {
        var tablet = "TestTablet-KeyOrder-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Assert.Null(KeyOrderStore.Load(tablet));
            Assert.False(KeyOrderStore.Has(tablet));

            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 1, 0 }));
            Assert.True(KeyOrderStore.Has(tablet));
            Assert.Equal(new[] { 1, 0 }, KeyOrderStore.Load(tablet)!.Keys);

            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 0, 1 }));
            Assert.Equal(new[] { 0, 1 }, KeyOrderStore.Load(tablet)!.Keys);

            KeyOrderStore.Clear(tablet);
            Assert.Null(KeyOrderStore.Load(tablet));
        }
        finally { KeyOrderStore.Clear(tablet); }
    }

    [Fact]
    public void ARunStoppedBeforeAnyKeyIsNotSavedAsAMapping()
    {
        var tablet = "TestTablet-KeyOrder-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            KeyOrderStore.Save(tablet, new KeyOrderMap(new[] { 1, 0 }));
            var s = new KeyMappingSession(2);
            s.Stop();
            KeyOrderStore.Save(tablet, s.Result());
            Assert.Null(KeyOrderStore.Load(tablet));       // an empty run removes the entry rather than keep a blank one
        }
        finally { KeyOrderStore.Clear(tablet); }
    }

    [Fact]
    public void NoTabletNameNoEntry()
    {
        KeyOrderStore.Save(null, new KeyOrderMap(new[] { 0 }));
        KeyOrderStore.Save("", new KeyOrderMap(new[] { 0 }));
        Assert.Null(KeyOrderStore.Load(null));
        Assert.False(KeyOrderStore.Has(""));
    }
}
