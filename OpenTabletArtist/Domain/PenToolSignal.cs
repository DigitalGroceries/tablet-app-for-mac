using System;
using System.Globalization;
using System.IO;

namespace OpenTabletArtist.Domain;

/// <summary>Version 14: which pen is near the tablet, passed from the pen-dynamics plugin (in the daemon) to
/// the app through a small file in the shared temp folder — the same way the ring-mode popup works.
/// Wacom Intuos-protocol tablets (Intuos4 included) send a tool report when a pen comes near: it carries the
/// pen's serial number and tool id, and says whether the tip or the eraser end is in use.
/// Source-shared with the plugin (linked in its csproj), so both sides read and write one format.</summary>
public readonly record struct PenToolSignal(ulong Serial, uint RawToolId, bool Eraser)
{
    /// <summary>Per-user temp folder; the daemon and the app run as the same user, so both see it.</summary>
    public static string FilePath { get; set; } = Path.Combine(Path.GetTempPath(), "OpenTabletArtist", "pen-tool");

    /// <summary>One physical pen. Both ends of a pen share its serial number, so the eraser end belongs to
    /// the same pen. A pen without a serial (some tools report 0) is told apart by its tool id.</summary>
    public string Key => Serial != 0
        ? "s" + Serial.ToString("X", CultureInfo.InvariantCulture)
        : "t" + RawToolId.ToString("X", CultureInfo.InvariantCulture);

    /// <summary>"serial toolid end", e.g. "1A2B3C 802 tip". Hex numbers, one line.</summary>
    public string Format() => string.Create(CultureInfo.InvariantCulture,
        $"{Serial:X} {RawToolId:X} {(Eraser ? "eraser" : "tip")}");

    public static PenToolSignal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        if (!ulong.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var serial)) return null;
        if (!uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)) return null;
        bool eraser = parts.Length > 2 && parts[2].Equals("eraser", StringComparison.OrdinalIgnoreCase);
        if (serial == 0 && id == 0) return null;
        return new PenToolSignal(serial, id, eraser);
    }

    /// <summary>Written by the plugin: temp file then move, so the app never reads half a line.</summary>
    public void Write(string? path = null)
    {
        path ??= FilePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Format());
        File.Move(tmp, path, overwrite: true);
    }

    public static PenToolSignal? Read(string? path = null)
    {
        try
        {
            path ??= FilePath;
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
