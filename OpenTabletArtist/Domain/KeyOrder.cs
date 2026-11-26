using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>
/// TASKS #212, step-by-step button mapping: where each ExpressKey really sits on the tablet.
///
/// The driver numbers a tablet's keys, but nothing says where key 3 is on the tablet: for most models the order was
/// never proved, so the ExpressKeys page could draw the keys in the wrong order. The user proves it instead: the app
/// lights one drawn key at a time, the user presses that key on the tablet, and the app writes down which driver key
/// number came through. This is the result, kept per tablet model.
///
/// <see cref="Keys"/> is in the order the keys sit, top to bottom as the user had the tablet when the mapping was made
/// (group by group when the keys are in two groups); each value is the driver's key number, counted from 0, or
/// <see cref="NotSeen"/> for a key that was skipped or never came through.
/// </summary>
public sealed class KeyOrderMap
{
    /// <summary>A place whose key was skipped, or whose press never reached the app.</summary>
    public const int NotSeen = -1;

    public KeyOrderMap(IEnumerable<int> keys, bool flipped = false, IEnumerable<int>? groups = null)
    {
        Keys = keys.ToArray();
        Flipped = flipped;
        Groups = (groups ?? Array.Empty<int>()).Where(g => g > 0).ToArray();
    }

    /// <summary>Driver key number (from 0) per place, top to bottom; <see cref="NotSeen"/> where none was recorded.</summary>
    public IReadOnlyList<int> Keys { get; }

    /// <summary>True when the mapping was made with the tablet turned round (keys on the other side, as a left-handed
    /// user has it). When the tablet is later turned the other way, the same keys sit in the opposite order.</summary>
    public bool Flipped { get; }

    /// <summary>How many keys each group holds, top group first (an Intuos4 M has 4 above its ring and 4 below).
    /// Empty = one group.</summary>
    public IReadOnlyList<int> Groups { get; }

    /// <summary>The keys in the driver's own order: what the app assumed before anyone mapped anything.</summary>
    public static KeyOrderMap Identity(int keyCount) => new(Enumerable.Range(0, Math.Max(0, keyCount)));

    /// <summary>True when at least one key was recorded.</summary>
    public bool HasAny => Keys.Any(k => k >= 0);

    /// <summary>True when every place has a key of its own.</summary>
    public bool IsComplete(int keyCount) =>
        Keys.Count == keyCount && Valid(keyCount).Count == keyCount;

    /// <summary>
    /// The order to draw <paramref name="keyCount"/> keys in, as driver key numbers from 0: the recorded keys in the
    /// order they sit, then every key nobody recorded, in the driver's order, so that no key is ever missing from the
    /// page. A number the tablet does not have, or one recorded twice, is dropped (a damaged or stale mapping must not
    /// hide a key or show one twice). <paramref name="flippedNow"/> = the tablet is turned round now.
    /// </summary>
    public IReadOnlyList<int> DisplayOrder(int keyCount, bool flippedNow = false)
    {
        var order = Valid(keyCount);
        var seen = new HashSet<int>(order);
        for (int i = 0; i < keyCount; i++)
            if (!seen.Contains(i)) order.Add(i);
        if (flippedNow != Flipped) order.Reverse();
        return order;
    }

    private List<int> Valid(int keyCount)
    {
        var order = new List<int>();
        var seen = new HashSet<int>();
        foreach (var k in Keys)
            if (k >= 0 && k < keyCount && seen.Add(k)) order.Add(k);
        return order;
    }

    public string ToJson() => new JObject
    {
        ["v"] = 1,
        ["keys"] = new JArray(Keys),
        ["flipped"] = Flipped,
        ["groups"] = new JArray(Groups),
    }.ToString(Formatting.None);

    /// <summary>Reads what <see cref="ToJson"/> wrote. Null for anything else: a mapping that cannot be read is no
    /// mapping, and the page falls back to the driver's order.</summary>
    public static KeyOrderMap? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var o = JObject.Parse(json);
            if (o["keys"] is not JArray keys) return null;
            if (keys.Any(k => k.Type != JTokenType.Integer)) return null;
            var groups = o["groups"] is JArray g && g.All(x => x.Type == JTokenType.Integer)
                ? g.Select(x => x.Value<int>())
                : null;
            return new KeyOrderMap(
                keys.Select(k => k.Value<int>()),
                o["flipped"] is { Type: JTokenType.Boolean } f && f.Value<bool>(),
                groups);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>What became of one place in a <see cref="KeyMappingSession"/>.</summary>
public enum KeyPlaceState
{
    /// <summary>Not reached yet.</summary>
    Waiting,
    /// <summary>The user pressed its key and the app recorded the driver's number.</summary>
    Recorded,
    /// <summary>The user chose Skip.</summary>
    Skipped,
    /// <summary>Nothing came through in time. Not a fault: the place is simply left without a key.</summary>
    NotSeen,
}

/// <summary>What a key press did during a <see cref="KeyMappingSession"/>.</summary>
public enum KeyPressResult
{
    /// <summary>Recorded for the lit place; the next place is lit.</summary>
    Recorded,
    /// <summary>That key already belongs to an earlier place. Nothing changed: the user is asked for the lit one.</summary>
    AlreadyRecorded,
    /// <summary>Not a key of this tablet, or the mapping is over. Nothing changed.</summary>
    Ignored,
}

/// <summary>
/// TASKS #212: one run of the step-by-step mapping, without any screen: the place that is lit, what a press does,
/// Skip, Back and Stop. The sheet shows it; this decides it, so every rule can be tested without a tablet.
///
/// Rules (team vote 23 Nov 2026): a key that was already recorded does not overwrite anything; a place nothing came
/// through for is "not seen", not a fault; Stop keeps what was done; Back un-does the place before the lit one so its
/// key can be pressed again.
/// </summary>
public sealed class KeyMappingSession
{
    private readonly int[] _keys;
    private readonly KeyPlaceState[] _states;
    private readonly int[] _groups;
    private bool _stopped;

    public KeyMappingSession(int keyCount, IEnumerable<int>? groups = null)
    {
        if (keyCount < 0) throw new ArgumentOutOfRangeException(nameof(keyCount));
        KeyCount = keyCount;
        _keys = Enumerable.Repeat(KeyOrderMap.NotSeen, keyCount).ToArray();
        _states = new KeyPlaceState[keyCount];
        var g = (groups ?? Array.Empty<int>()).Where(x => x > 0).ToArray();
        // Groups are only kept when they account for every key; a wrong split must not put a key in no group.
        _groups = g.Sum() == keyCount && g.Length > 1 ? g : Array.Empty<int>();
    }

    public int KeyCount { get; }

    /// <summary>The lit place, from 0, top to bottom. Equal to <see cref="KeyCount"/> when every place was handled.</summary>
    public int Place { get; private set; }

    /// <summary>True when there is nothing left to press: every place was handled, or the user chose Stop.</summary>
    public bool IsFinished => _stopped || Place >= KeyCount;

    /// <summary>How many places have a key recorded.</summary>
    public int RecordedCount => _states.Count(s => s == KeyPlaceState.Recorded);

    public KeyPlaceState StateOf(int place) => _states[place];

    /// <summary>The driver key number recorded for a place, or <see cref="KeyOrderMap.NotSeen"/>.</summary>
    public int KeyAt(int place) => _keys[place];

    /// <summary>The group the lit place is in (from 0) and how many groups there are; (0, 1) when the keys are in one
    /// group. The sheet names the group ("the keys above the ring").</summary>
    public (int Group, int GroupCount) GroupOfPlace(int place)
    {
        if (_groups.Length == 0) return (0, 1);
        int end = 0;
        for (int i = 0; i < _groups.Length; i++)
        {
            end += _groups[i];
            if (place < end) return (i, _groups.Length);
        }
        return (_groups.Length - 1, _groups.Length);
    }

    /// <summary>The driver reported that key number <paramref name="driverKey"/> (from 0) went down.</summary>
    public KeyPressResult Press(int driverKey)
    {
        if (IsFinished || driverKey < 0 || driverKey >= KeyCount) return KeyPressResult.Ignored;
        for (int i = 0; i < KeyCount; i++)
            if (i != Place && _states[i] == KeyPlaceState.Recorded && _keys[i] == driverKey)
                return KeyPressResult.AlreadyRecorded;
        _keys[Place] = driverKey;
        _states[Place] = KeyPlaceState.Recorded;
        Place++;
        return KeyPressResult.Recorded;
    }

    /// <summary>Leaves the lit place without a key and lights the next. <paramref name="nothingCameThrough"/> = the
    /// app waited and saw no press (shown differently from a Skip the user chose).</summary>
    public void Skip(bool nothingCameThrough = false)
    {
        if (IsFinished) return;
        _keys[Place] = KeyOrderMap.NotSeen;
        _states[Place] = nothingCameThrough ? KeyPlaceState.NotSeen : KeyPlaceState.Skipped;
        Place++;
    }

    /// <summary>Lights the place before the lit one again and forgets what was recorded for it. False at the first place.</summary>
    public bool Back()
    {
        if (_stopped || Place == 0) return false;
        Place--;
        _keys[Place] = KeyOrderMap.NotSeen;
        _states[Place] = KeyPlaceState.Waiting;
        return true;
    }

    /// <summary>Ends the run. What was recorded so far is kept.</summary>
    public void Stop() => _stopped = true;

    /// <summary>The mapping as it stands. <paramref name="flipped"/> = the tablet is turned round (left-handed).</summary>
    public KeyOrderMap Result(bool flipped = false) => new(_keys, flipped, _groups);

    /// <summary>
    /// Which key went down between two reports of the driver: its number from 0, or -1 when none did, or when more
    /// than one did at the same moment (then nobody can say which is the lit one, and the user is simply asked again).
    /// A key that is still held from before is not a new press.
    /// </summary>
    public static int NewlyPressed(IReadOnlyList<bool>? before, IReadOnlyList<bool> now)
    {
        int found = -1;
        for (int i = 0; i < now.Count; i++)
        {
            bool was = before != null && i < before.Count && before[i];
            if (now[i] && !was)
            {
                if (found >= 0) return -1;
                found = i;
            }
        }
        return found;
    }
}
