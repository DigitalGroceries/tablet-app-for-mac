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
    public KeyMappingViewModel(int keyCount, IEnumerable<int>? groups = null, bool flipped = false)
    {
        _keyCount = Math.Max(0, keyCount);
        _groups = (groups ?? Array.Empty<int>()).ToArray();
        _flipped = flipped;
        _session = new KeyMappingSession(_keyCount, _groups);
        Places = BuildPlaces();
        Refresh();
    }

    public IReadOnlyList<KeyMappingPlace> Places { get; }

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

    /// <summary>The mapping as it stands.</summary>
    public KeyOrderMap Result => _session.Result(_flipped);

    /// <summary>A report of the driver: the up/down state of every key.</summary>
    public void OnKeys(IReadOnlyList<bool> states)
    {
        var before = _last;
        _last = states.ToArray();
        if (IsFinished) return;
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
        _session.Skip(NothingCameThrough);
        Message = "";
        Advance();
    }

    [RelayCommand]
    private void Back()
    {
        if (!_session.Back()) return;
        Message = "";
        Advance();
    }

    [RelayCommand]
    private void Stop()
    {
        if (IsFinished) return;
        _session.Stop();
        Message = "";
        Advance();
    }

    [RelayCommand]
    private void StartAgain()
    {
        _session = new KeyMappingSession(_keyCount, _groups);
        Message = "";
        Advance();
    }

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
        IsFinished = _session.IsFinished;
        for (int i = 0; i < Places.Count; i++)
        {
            Places[i].State = _session.StateOf(i);
            Places[i].IsLit = !IsFinished && i == _session.Place;
        }
        CanGoBack = IsRunning && _session.Place > 0;
        OnPropertyChanged(nameof(RecordedCount));

        if (IsFinished)
        {
            StepText = "";
            GroupText = "";
            Instruction = _keyCount == 0
                ? T("keymap.no_keys", "This tablet has no ExpressKeys to set up.")
                : RecordedCount == _keyCount
                    ? T("keymap.done_all", "All {count} keys are set up.").Replace("{count}", _keyCount.ToString(), StringComparison.Ordinal)
                    : T("keymap.done_some", "{number} of {count} keys are set up. The others come last in the list.")
                        .Replace("{number}", RecordedCount.ToString(), StringComparison.Ordinal)
                        .Replace("{count}", _keyCount.ToString(), StringComparison.Ordinal);
            return;
        }

        StepText = T("keymap.step", "Key {number} of {count}")
            .Replace("{number}", (_session.Place + 1).ToString(), StringComparison.Ordinal)
            .Replace("{count}", _keyCount.ToString(), StringComparison.Ordinal);
        var (group, groupCount) = _session.GroupOfPlace(_session.Place);
        GroupText = groupCount > 1
            ? T("keymap.group", "Group {number} of {count}")
                .Replace("{number}", (group + 1).ToString(), StringComparison.Ordinal)
                .Replace("{count}", groupCount.ToString(), StringComparison.Ordinal)
            : "";
        Instruction = T("keymap.press", "Press the lit key on your tablet.");
    }

    private static string T(string key, string english) => Localizer.Te(key, english);
}
