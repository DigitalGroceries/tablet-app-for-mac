using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>A change in this version that makes a tablet behave differently after an update, and what the
/// user should do about it. Shown in the main window while one of <see cref="TabletNames"/> is connected,
/// until the user dismisses it. Never a popup window: those get missed on multi-screen desks.</summary>
public sealed record TabletUpdateNotice(string Id, IReadOnlyList<string> TabletNames, string Title, IReadOnlyList<string> Paragraphs);

/// <summary>One notice for one connected tablet, ready to show.</summary>
public sealed record TabletNoticeItem(string Id, string TabletName, string Title, IReadOnlyList<string> Paragraphs);

public static class TabletUpdateNotices
{
    public const string DismissedKeyPrefix = "DismissedTabletNotice:";

    /// <summary>Tablet names are the OpenTabletDriver configuration names (what the driver reports).</summary>
    public static readonly IReadOnlyList<TabletUpdateNotice> All =
    [
        new TabletUpdateNotice(
            "cintiq-v1-expresskeys",
            ["Wacom Cintiq 21UX (DTK-2100)", "Wacom Cintiq 22HD (DTK-2200)", "Wacom Cintiq 22HD Touch (DTH-2200)"],
            "Check your ExpressKeys",
            [
                "This version reads the ExpressKeys on your {tablet} the way the Wacom layout does. Before, two keys did nothing, and the keys after them were moved along by one.",
                "What this means for you: if you set up the ExpressKeys on the right side before this update, some of them may now do a different job. On a Cintiq 22HD, the two top keys now work too.",
                "What to do: open Functions › ExpressKeys, press each key and check what it does. Change any that are wrong, then click Save.",
            ]),
    ];

    public static string DismissedKey(string id) => DismissedKeyPrefix + id;

    /// <summary>The notices to show for the connected tablets, skipping dismissed ones. One item per notice,
    /// for the first connected tablet it applies to.</summary>
    public static IReadOnlyList<TabletNoticeItem> Pending(IEnumerable<string> connectedTablets, Func<string, bool> isDismissed,
        IReadOnlyList<TabletUpdateNotice>? catalog = null)
    {
        var connected = connectedTablets.ToList();
        var result = new List<TabletNoticeItem>();
        foreach (var notice in catalog ?? All)
        {
            if (isDismissed(notice.Id)) continue;
            var tablet = connected.FirstOrDefault(c => notice.TabletNames.Contains(c, StringComparer.OrdinalIgnoreCase));
            if (tablet == null) continue;
            result.Add(new TabletNoticeItem(notice.Id, tablet, notice.Title,
                notice.Paragraphs.Select(p => p.Replace("{tablet}", tablet)).ToList()));
        }
        return result;
    }
}
