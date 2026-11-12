using System;
using System.Collections.Generic;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Undo and redo for the Scribble canvas (TASKS #53), kept apart from the drawing code so it can be tested.
/// It stores whole "states" (the canvas as it was before a stroke), newest last. <see cref="Record"/> is called
/// with the state from BEFORE a change; <see cref="Undo"/> and <see cref="Redo"/> are given the state as it is
/// NOW and return the state to go back to (or null when there is nothing to do). A new change empties the
/// redo list, like every drawing app. The oldest states are dropped when there are more than
/// <c>maxSteps</c> of them or they take more than <c>maxBytes</c> together (a big canvas keeps fewer steps);
/// <c>discard</c> is called for every state that is dropped for good, so bitmaps can be freed.
/// </summary>
public sealed class UndoHistory<T> where T : class
{
    private readonly List<T> _undo = new();
    private readonly List<T> _redo = new();
    private readonly int _maxSteps;
    private readonly long _maxBytes;
    private readonly Func<T, long> _sizeOf;
    private readonly Action<T>? _discard;

    public UndoHistory(int maxSteps, long maxBytes, Func<T, long> sizeOf, Action<T>? discard = null)
    {
        _maxSteps = Math.Max(1, maxSteps);
        _maxBytes = Math.Max(1, maxBytes);
        _sizeOf = sizeOf;
        _discard = discard;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    /// <summary>A change is about to happen: remember the state before it. Forgets everything that could be redone.</summary>
    public void Record(T before)
    {
        DiscardAll(_redo);
        _undo.Add(before);
        Trim();
    }

    /// <summary>Go back one step: <paramref name="current"/> becomes redoable, the previous state is returned.</summary>
    public T? Undo(T current)
    {
        if (_undo.Count == 0) return null;
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
        return previous;
    }

    /// <summary>Go forward one step again: <paramref name="current"/> becomes undoable, the redone state is returned.</summary>
    public T? Redo(T current)
    {
        if (_redo.Count == 0) return null;
        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
        Trim();
        return next;
    }

    /// <summary>Forget everything (the states are discarded).</summary>
    public void Reset()
    {
        DiscardAll(_undo);
        DiscardAll(_redo);
    }

    private void Trim()
    {
        long total = 0;
        foreach (var s in _undo) total += _sizeOf(s);
        // Always keep the newest step, however big.
        while (_undo.Count > 1 && (_undo.Count > _maxSteps || total > _maxBytes))
        {
            var oldest = _undo[0];
            _undo.RemoveAt(0);
            total -= _sizeOf(oldest);
            _discard?.Invoke(oldest);
        }
    }

    private void DiscardAll(List<T> list)
    {
        foreach (var s in list) _discard?.Invoke(s);
        list.Clear();
    }
}
