using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>One drawn key on the "Set up my buttons" sheet.</summary>
public sealed partial class KeyMappingPlace : ObservableObject
{
    public KeyMappingPlace(int number, bool startsNewGroup)
    {
        Number = number;
        StartsNewGroup = startsNewGroup;
    }

    /// <summary>The key's place, from 1, top to bottom.</summary>
    public int Number { get; }

    /// <summary>The first key of a second group: drawn with a gap above it.</summary>
    public bool StartsNewGroup { get; }

    /// <summary>This is the key the user is asked to press now.</summary>
    [ObservableProperty] private bool _isLit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecorded))]
    [NotifyPropertyChangedFor(nameof(IsLeftOut))]
    private KeyPlaceState _state;

    public bool IsRecorded => State == KeyPlaceState.Recorded;

    /// <summary>Skipped, or nothing came through: drawn faint.</summary>
    public bool IsLeftOut => State is KeyPlaceState.Skipped or KeyPlaceState.NotSeen;
}

/// <summary>
/// TASKS #212: the "Set up my buttons" sheet without its window: which drawn key is lit, the words under it, and what
/// Skip, Back, Stop and Start again do. The rules are <see cref="KeyMappingSession"/>'s; this adds the words and turns
/// the driver's reports (every key's up/down state) into single presses.
/// </summary>
public sealed partial class KeyMappingViewModel : ObservableObject
{
    /// <summary>How long the sheet waits for a press before it says that nothing came through.</summary>
    public static readonly TimeSpan QuietTime = TimeSpan.FromSeconds(10);

    private readonly int _keyCount;
    private readonly int[] _groups;
    private readonly bool _flipped;
    private KeyMappingSession _session;
    private bool[]? _last;

    /// <param name="keyCount">How many ExpressKeys the tablet has.</param>
    /// <param name="groups">How many keys each group holds, top group first; null or empty = one group.</param>
    /// <param name="flipped">The tablet is turned round (left-handed use) while this runs.</param>
    /// <param name="layout">Where the keys sit (<see cref="KeyLayouts.For"/>); null = the plain column. A layout with
    /// another number of keys than <paramref name="keyCount"/> is not used.</param>
    /// <param name="controls">TASKS #212 part 4: the checks after the keys (rings, strips, finger touch), from
    /// <see cref="ControlCheck.For"/>; null = none.</param>
    public KeyMappingViewModel(int keyCount, IEnumerable<int>? groups = null, bool flipped = false, KeyLayout? layout = null,
        IEnumerable<ControlCheck>? controls = null)
    {
        Controls = (controls ?? Array.Empty<ControlCheck>()).ToList();
        _keyCount = Math.Max(0, keyCount);
        if (layout is { Proven: true } proven && proven.KeyCount == _keyCount)
        {
            // A proven layout: the places are the tablet's own keys, in the tablet's own order, whichever way it is
            // held. Turned round, the drawing is turned and the same keys are lit; the map is saved as not turned.
            Layout = proven;
            _groups = proven.GroupSizes.ToArray();
            DrawTurned = flipped;
            _flipped = false;
        }
        else
        {
            // The plain column: lit top to bottom as the tablet is held now (the map remembers that it was turned).
            _groups = (groups ?? Array.Empty<int>()).ToArray();
            Layout = KeyLayouts.Column(_keyCount, _groups);
            _flipped = flipped;
        }
        _session = new KeyMappingSession(_keyCount, _groups);
        Places = BuildPlaces();
        Refresh();
    }

    public IReadOnlyList<KeyMappingPlace> Places { get; }

    /// <summary>The checks after the keys, in order (part 4).</summary>
    public IReadOnlyList<ControlCheck> Controls { get; }

    // Which check is on (index into Controls); -1 while the keys are being done.
    private int _control = -1;

    /// <summary>The check on now, or null while the keys are being done (or when everything is over).</summary>
    public ControlCheck? CurrentControl => !IsFinished && _control >= 0 && _control < Controls.Count ? Controls[_control] : null;

    /// <summary>The check on now is the ring drawn on the tablet (one ring, and the layout draws it): the sheet lights it.</summary>
    public bool IsRingLit => CurrentControl is { Kind: ControlCheckKind.Ring } && Layout.Ring != null
        && Controls.Count(c => c.Kind == ControlCheckKind.Ring) == 1;

    /// <summary>What the sheet draws: spot N is place N.</summary>
    public KeyLayout Layout { get; }

    /// <summary>A proven layout with the tablet turned round: the sheet draws it turned half round.</summary>
    public bool DrawTurned { get; }

    /// <summary>"Key 3 of 8".</summary>
    [ObservableProperty] private string _stepText = "";

    /// <summary>"Group 2 of 2", or empty when the keys are in one group.</summary>
    [ObservableProperty] private string _groupText = "";

    /// <summary>What to do now, or what came of it once the run is over.</summary>
    [ObservableProperty] private string _instruction = "";

    /// <summary>A remark about the last press ("you already did that key"), or empty.</summary>
    [ObservableProperty] private string _message = "";

    [ObservableProperty] private bool _canGoBack;

    /// <summary>Nothing is left to press: every key was handled, or the user chose Stop.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private bool _isFinished;

    public bool IsRunning => !IsFinished;

    /// <summary>The sheet waited <see cref="QuietTime"/> and saw no press for the lit key.</summary>
    public bool NothingCameThrough { get; private set; }

    /// <summary>The lit key changed (or the run ended): the sheet starts its wait again.</summary>
    public event Action? PlaceChanged;

    public int RecordedCount => _session.RecordedCount;

    /// <summary>The mapping as it stands, with what the checks after the keys found.</summary>
    public KeyOrderMap Result
    {
        get
        {
            var keys = _session.Result(_flipped);
            return new KeyOrderMap(keys.Keys, keys.Flipped, keys.Groups,
                Controls.Where(c => c.IsDone).Select(c => c.Result));
        }
    }

    /// <summary>An absolute ring or strip reading from the driver (wheel number, position or null = no finger).</summary>
    public void OnWheelPosition(int wheel, uint? position)
    {
        if (CurrentControl is { } c && c.OnPosition(wheel, position)) NextControl();
    }

    /// <summary>A relative ring's steps from the driver.</summary>
    public void OnWheelDelta(int wheel, int delta)
    {
        if (CurrentControl is { } c && c.OnDelta(wheel, delta)) NextControl();
    }

    /// <summary>A touch report from the driver with this many fingers down.</summary>
    public void OnTouch(int fingers)
    {
        if (CurrentControl is { } c && c.OnTouch(fingers)) NextControl();
    }

    private void NextControl()
    {
        Message = "";
        _control++;
        Advance();
    }

    /// <summary>A report of the driver: the up/down state of every key.</summary>
    public void OnKeys(IReadOnlyList<bool> states)
    {
        var before = _last;
        _last = states.ToArray();
        if (IsFinished || _control >= 0) return;          // the keys are done: a press now is not for a key
        // Two keys going down in the same report cannot be told apart; say so rather than guess.
        int downNow = 0;
        for (int i = 0; i < states.Count; i++)
            if (states[i] && !(before != null && i < before.Length && before[i])) downNow++;
        if (downNow == 0) return;                               // a release, or a key still held
        if (downNow > 1) { Message = T("keymap.one_at_a_time", "Press one key at a time."); return; }

        switch (_session.Press(KeyMappingSession.NewlyPressed(before, states)))
        {
            case KeyPressResult.Recorded:
                Message = "";
                Advance();
                break;
            case KeyPressResult.AlreadyRecorded:
                Message = T("keymap.already", "You pressed a key you already did. Press the lit one.");
                NothingCameThrough = false;
                PlaceChanged?.Invoke();                         // a press did come through: wait afresh
                break;
        }
    }

    /// <summary>The sheet's wait ran out with no press for the lit key.</summary>
    public void NothingCame()
    {
        if (IsFinished) return;
        NothingCameThrough = true;
        Message = T("keymap.nothing", "Nothing came through. Press the key again, or choose Skip.");
    }

    [RelayCommand]
    private void Skip()
    {
        if (IsFinished) return;
        if (CurrentControl is { } c)
        {
            c.Skip(NothingCameThrough);
            NextControl();
            return;
        }
        _session.Skip(NothingCameThrough);
        Message = "";
        Advance();
    }

    [RelayCommand]
    private void Back()
    {
        if (IsFinished) return;
        if (_control > 0)
        {
            _control--;
            Controls[_control].Reset();
        }
        else if (_control == 0)
        {
            // From the first check back to the last key (if there are keys).
            if (_keyCount == 0) return;
            _control = -1;
            _session.Back();
        }
        else if (!_session.Back()) return;
        Message = "";
        Advance();
    }

    [RelayCommand]
    private void Stop()
    {
        if (IsFinished) return;
        _session.Stop();
        _stopped = true;
        Message = "";
        Advance();
    }

    [RelayCommand]
    private void StartAgain()
    {
        _session = new KeyMappingSession(_keyCount, _groups);
        foreach (var c in Controls) c.Reset();
        _control = -1;
        _stopped = false;
        Message = "";
        Advance();
    }

    private bool _stopped;

    private void Advance()
    {
        NothingCameThrough = false;
        Refresh();
        PlaceChanged?.Invoke();
    }

    private List<KeyMappingPlace> BuildPlaces()
    {
        var starts = new HashSet<int>();
        if (_groups.Length > 1 && _groups.Sum() == _keyCount)
            for (int g = 0, at = 0; g < _groups.Length - 1; g++) { at += _groups[g]; starts.Add(at); }
        return Enumerable.Range(0, _keyCount).Select(i => new KeyMappingPlace(i + 1, starts.Contains(i))).ToList();
    }

    private void Refresh()
    {
        // The keys are done (not stopped): the checks after them follow, one by one.
        if (!_stopped && _session.IsFinished && _control < 0) _control = 0;
        IsFinished = _stopped || (_session.IsFinished && _control >= Controls.Count);
        bool inKeys = !IsFinished && _control < 0;
        for (int i = 0; i < Places.Count; i++)
        {
            Places[i].State = _session.StateOf(i);
            Places[i].IsLit = inKeys && i == _session.Place;
        }
        CanGoBack = IsRunning && (inKeys ? _session.Place > 0 : _control > 0 || _keyCount > 0);
        OnPropertyChanged(nameof(RecordedCount));
        OnPropertyChanged(nameof(CurrentControl));
        OnPropertyChanged(nameof(IsRingLit));

        if (IsFinished)
        {
            StepText = "";
            GroupText = "";
            var keys = _keyCount == 0
                ? (Controls.Count == 0 ? T("keymap.no_keys", "This tablet has no ExpressKeys to set up.") : "")
                : RecordedCount == _keyCount
                    ? T("keymap.done_all", "All {count} keys are set up.").Replace("{count}", _keyCount.ToString(), StringComparison.Ordinal)
                    : T("keymap.done_some", "{number} of {count} keys are set up. The others come last in the list.")
                        .Replace("{number}", RecordedCount.ToString(), StringComparison.Ordinal)
                        .Replace("{count}", _keyCount.ToString(), StringComparison.Ordinal);
            var lines = new List<string>();
            if (keys.Length > 0) lines.Add(keys);
            foreach (var c in Controls)
                if (ControlSummary(c) is { Length: > 0 } line) lines.Add(line);
            Instruction = string.Join("\n", lines);
            return;
        }

        if (CurrentControl is { } control)
        {
            StepText = control.Title;
            GroupText = "";
            Instruction = control.Kind switch
            {
                ControlCheckKind.Ring => T("keymap.ring", "Touch the ring with one finger and move it round a little."),
                ControlCheckKind.Strip => T("keymap.strip", "Slide one finger up the strip, from the bottom to the top."),
                _ => T("keymap.touch", "Touch the tablet with one finger."),
            };
            return;
        }

        StepText = T("keymap.step", "Key {number} of {count}")
            .Replace("{number}", (_session.Place + 1).ToString(), StringComparison.Ordinal)
            .Replace("{count}", _keyCount.ToString(), StringComparison.Ordinal);
        var (group, groupCount) = _session.GroupOfPlace(_session.Place);
        // A group's own name ("Above the ring") only when the tablet is not turned: turned, it would be wrong as seen.
        GroupText = groupCount <= 1 ? ""
            : !DrawTurned && group < Layout.GroupNames.Count && Layout.GroupNames.Count == groupCount
                ? T(Layout.GroupNames[group].Key, Layout.GroupNames[group].English)
                : T("keymap.group", "Group {number} of {count}")
                    .Replace("{number}", (group + 1).ToString(), StringComparison.Ordinal)
                    .Replace("{count}", groupCount.ToString(), StringComparison.Ordinal);
        Instruction = T("keymap.press", "Press the lit key on your tablet.");
    }

    /// <summary>The line for one check on the last screen; empty for a check that was skipped or never reached.</summary>
    private static string ControlSummary(ControlCheck c)
    {
        string Fill(string text) => text.Replace("{name}", c.Title, StringComparison.Ordinal);
        return c.State switch
        {
            ControlCheckState.Works when c.Kind == ControlCheckKind.Strip && c.StripUp == StripDirection.UpIsRising =>
                Fill(T("keymap.strip_reversed", "{name} works, but it counts the other way round. The app has noted it for this tablet.")),
            ControlCheckState.Works when c.Kind == ControlCheckKind.Touch => T("keymap.touch_works", "Touch works."),
            ControlCheckState.Works => Fill(T("keymap.control_works", "{name} works.")),
            ControlCheckState.NotSeen when c.Kind == ControlCheckKind.Touch => T("keymap.touch_nothing", "Nothing came through from touch."),
            ControlCheckState.NotSeen => Fill(T("keymap.control_nothing", "Nothing came through from {name}.")),
            _ => "",
        };
    }

    private static string T(string key, string english) => Localizer.Te(key, english);
}
