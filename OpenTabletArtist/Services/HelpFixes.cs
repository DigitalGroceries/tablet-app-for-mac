using System;

namespace OpenTabletArtist.Services;

/// <summary>V30 #200 B5 (Zak 23 Nov 2026: a troubleshooting/FAQ button; team vote: small, on the existing Help page, Fix it only
/// where the app can really fix it, no new window): the manual's "Something doesn't work?" answers end with an in-app button,
/// written in manual.md as a line of its own, <c>[Text](app:action)</c>. The manual window draws it as a button; the website and
/// the PDF leave the line out. The main window's view model registers <see cref="Run"/> for the actions that need it.</summary>
public static class HelpFixes
{
    /// <summary>Every action the manual may name. A line with another name is left out of the manual window too.</summary>
    public static readonly string[] Actions = { "keys", "ring", "pointer", "driver", "report" };

    /// <summary>Set by the main window's view model: does the action (open a page, restart the driver, …).</summary>
    public static Action<string>? Run { get; set; }

    /// <summary>Pure: the action named by a manual line that is only an app link, else null (unit-tested).</summary>
    public static (string Text, string Action)? ParseLine(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line.Trim(), @"^\[([^\]]+)\]\(app:([a-z-]+)\)$");
        if (!m.Success || Array.IndexOf(Actions, m.Groups[2].Value) < 0) return null;
        return (m.Groups[1].Value, m.Groups[2].Value);
    }
}
