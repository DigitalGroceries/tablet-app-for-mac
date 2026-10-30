using System;
using System.Numerics;

namespace OpenTabletArtist.Domain;

/// <summary>Which way a pen scroll may move the page.</summary>
public enum PenScrollAxes
{
    Both,
    Vertical,
    Horizontal,
}

/// <summary>
/// Pen scroll ("mouse wheel" on a pen button, like Wacom's Pan/Scroll): turns pen movement in millimetres
/// into OpenTabletDriver scroll amounts. Shared by the plugin (linked) and the app's tests, so the feel is
/// defined in one place. Works in millimetres, so every tablet scrolls the same distance for the same hand
/// movement whatever its resolution.
///
/// <para>OTD's scroll convention: a negative amount scrolls up / left, a positive one down / right
/// (see OTD's MouseScrollBinding). By default the page follows the pen, as if you grabbed it.</para>
/// </summary>
public static class PenScrollMath
{
    /// <summary>The app's one Speed scale: 1–100, higher = faster.</summary>
    public const int MinSpeed = 1, MaxSpeed = 100, DefaultSpeed = 50;

    /// <summary>Scroll pixels per millimetre of pen travel at Speed 1. Speed 50 = 20 px per mm.</summary>
    public const float PixelsPerMmPerSpeed = 0.4f;

    /// <summary>Pen movement below this (mm) is ignored as hand jitter while the button is first held.</summary>
    public const float StartDeadZoneMm = 0.5f;

    public static float PixelsPerMm(int speed) => Math.Clamp(speed, MinSpeed, MaxSpeed) * PixelsPerMmPerSpeed;

    /// <summary>Millimetres per tablet unit on each axis, or null when the digitizer size is unknown.</summary>
    public static Vector2? MmPerUnit(float widthMm, float heightMm, float maxX, float maxY)
    {
        if (widthMm <= 0 || heightMm <= 0 || maxX <= 0 || maxY <= 0) return null;
        return new Vector2(widthMm / maxX, heightMm / maxY);
    }

    public static PenScrollAxes ParseAxes(string? value) =>
        Enum.TryParse<PenScrollAxes>(value, true, out var a) ? a : PenScrollAxes.Both;

    /// <summary>
    /// The whole scroll amounts to send for a pen movement of <paramref name="deltaMm"/>, carrying the
    /// fractional rest in <paramref name="remainder"/> so slow movement and low speeds still scroll.
    /// </summary>
    /// <param name="invert">False: the page follows the pen (drag). True: the classic wheel direction.</param>
    public static (int Vertical, int Horizontal) Step(
        Vector2 deltaMm, int speed, PenScrollAxes axes, bool invert, ref Vector2 remainder)
    {
        if (axes == PenScrollAxes.Vertical) deltaMm.X = 0;
        if (axes == PenScrollAxes.Horizontal) deltaMm.Y = 0;

        float k = PixelsPerMm(speed) * (invert ? 1f : -1f);
        var want = remainder + deltaMm * k;
        int v = (int)want.Y;   // truncates toward zero for both signs
        int h = (int)want.X;
        remainder = new Vector2(want.X - h, want.Y - v);
        return (v, h);
    }
}
