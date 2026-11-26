using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>What a step after the keys checks (TASKS #212 part 4).</summary>
public enum ControlCheckKind
{
    /// <summary>A Touch Ring (or a scroll-wheel style ring): "Touch the ring".</summary>
    Ring,
    /// <summary>A Touch Strip: "Slide your finger up the strip", which also tells which way the strip counts.</summary>
    Strip,
    /// <summary>Finger touch on the drawing area (touch models only): "Touch the tablet with one finger".</summary>
    Touch,
}

/// <summary>What came of one check.</summary>
public enum ControlCheckState
{
    Waiting,
    /// <summary>The ring, strip or touch reached the app.</summary>
    Works,
    /// <summary>The user chose Skip.</summary>
    Skipped,
    /// <summary>The app waited and nothing came through, then the user went on. Not a fault.</summary>
    NotSeen,
}

/// <summary>Which way a Touch Strip's position runs when the finger moves up (the tablet held as the manual draws it,
/// keys on the left). The app assumes a falling position (Version 16, WheelEditor.IsStrip); the Wacom manuals do
/// not agree on it, so the user's slide decides.</summary>
public enum StripDirection
{
    Unknown,
    /// <summary>Up = a falling position: what the app assumes.</summary>
    UpIsFalling,
    /// <summary>Up = a rising position: the strip counts the other way round.</summary>
    UpIsRising,
}

/// <summary>The saved outcome of one check.</summary>
public sealed record ControlCheckResult(ControlCheckKind Kind, int WheelIndex, ControlCheckState State,
    StripDirection StripUp = StripDirection.Unknown)
{
    public JObject ToJson() => new()
    {
        ["kind"] = Kind.ToString().ToLowerInvariant(),
        ["wheel"] = WheelIndex,
        ["state"] = State.ToString().ToLowerInvariant(),
        ["up"] = StripUp switch { StripDirection.UpIsFalling => "falling", StripDirection.UpIsRising => "rising", _ => null },
    };

    /// <summary>Null for anything that is not a check written by <see cref="ToJson"/>.</summary>
    public static ControlCheckResult? TryParse(JToken? t)
    {
        if (t is not JObject o) return null;
        if (!Enum.TryParse<ControlCheckKind>(o["kind"]?.ToString(), true, out var kind)) return null;
        if (!Enum.TryParse<ControlCheckState>(o["state"]?.ToString(), true, out var state)) return null;
        if (o["wheel"] is not { Type: JTokenType.Integer } w) return null;
        var up = o["up"]?.ToString() switch { "falling" => StripDirection.UpIsFalling, "rising" => StripDirection.UpIsRising, _ => StripDirection.Unknown };
        return new ControlCheckResult(kind, w.Value<int>(), state, up);
    }
}

/// <summary>
/// TASKS #212 part 4: one check after the keys - a ring, a strip or finger touch. It is fed the driver's reports and
/// says when it has seen enough. Rings: any touch or turn. Strips: a slide of <see cref="StripTravel"/> positions
/// while the finger stays down, whose direction is kept. Touch: a report with at least one finger.
/// </summary>
public sealed class ControlCheck
{
    /// <summary>How far (in strip positions) the finger must slide before the direction counts (Intuos3 strips report
    /// 0 to 12): a resting finger that wobbles by one must not decide it.</summary>
    public const int StripTravel = 3;

    private readonly bool _turned;
    private uint? _slideStart;

    /// <param name="turned">The tablet is turned half round: a slide up as held is a slide down on the tablet.</param>
    public ControlCheck(ControlCheckKind kind, int wheelIndex, string title, bool turned = false)
    {
        Kind = kind;
        WheelIndex = kind == ControlCheckKind.Touch ? -1 : wheelIndex;
        Title = title;
        _turned = turned;
    }

    public ControlCheckKind Kind { get; }

    /// <summary>The driver's wheel number of the ring or strip (from 0); -1 for touch.</summary>
    public int WheelIndex { get; }

    /// <summary>"Touch Ring", "Left Touch Strip", "Touch" - the app's own names.</summary>
    public string Title { get; }

    public ControlCheckState State { get; private set; }
    public StripDirection StripUp { get; private set; }

    public bool IsDone => State != ControlCheckState.Waiting;

    /// <summary>An absolute ring or strip reading for wheel <paramref name="wheel"/> (null = no finger). True when this
    /// check has now seen enough.</summary>
    public bool OnPosition(int wheel, uint? position)
    {
        if (IsDone || Kind == ControlCheckKind.Touch || wheel != WheelIndex) return false;
        if (Kind == ControlCheckKind.Ring)
        {
            if (position == null) return false;
            State = ControlCheckState.Works;
            return true;
        }
        if (position is not uint pos) { _slideStart = null; return false; }   // finger lifted: a new slide starts
        _slideStart ??= pos;
        long moved = (long)pos - _slideStart.Value;
        if (Math.Abs(moved) < StripTravel) return false;
        bool falling = moved < 0;
        if (_turned) falling = !falling;
        StripUp = falling ? StripDirection.UpIsFalling : StripDirection.UpIsRising;
        State = ControlCheckState.Works;
        return true;
    }

    /// <summary>A relative ring's steps (a ring that reports turns only, as the Intuos Pro 2017). True when done.</summary>
    public bool OnDelta(int wheel, int delta)
    {
        if (IsDone || Kind != ControlCheckKind.Ring || wheel != WheelIndex || delta == 0) return false;
        State = ControlCheckState.Works;
        return true;
    }

    /// <summary>A touch report with <paramref name="fingers"/> fingers down. True when done.</summary>
    public bool OnTouch(int fingers)
    {
        if (IsDone || Kind != ControlCheckKind.Touch || fingers < 1) return false;
        State = ControlCheckState.Works;
        return true;
    }

    public void Skip(bool nothingCameThrough)
    {
        if (IsDone) return;
        State = nothingCameThrough ? ControlCheckState.NotSeen : ControlCheckState.Skipped;
    }

    /// <summary>Forgets the outcome, for Back.</summary>
    public void Reset()
    {
        State = ControlCheckState.Waiting;
        StripUp = StripDirection.Unknown;
        _slideStart = null;
    }

    public ControlCheckResult Result => new(Kind, WheelIndex, State, StripUp);

    /// <summary>The checks a tablet gets after its keys: each ring or strip the driver reports, in the driver's order,
    /// then finger touch when the tablet has it.</summary>
    public static List<ControlCheck> For(IEnumerable<(bool IsStrip, string Title)> wheels, bool hasTouch, string touchTitle,
        bool turned = false)
    {
        var list = wheels.Select((w, i) => new ControlCheck(w.IsStrip ? ControlCheckKind.Strip : ControlCheckKind.Ring, i, w.Title, turned)).ToList();
        if (hasTouch) list.Add(new ControlCheck(ControlCheckKind.Touch, -1, touchTitle, turned));
        return list;
    }
}
