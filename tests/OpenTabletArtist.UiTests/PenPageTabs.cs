using Avalonia.Controls;
using Avalonia.Threading;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.UiTests;

/// <summary>V29 #180 (Zak's Pen page mockup, 21 Nov 2026): the Eraser tab became the Pen page's "Eraser Feel" switch. Tests written
/// for the two tabs open them through here: "EraserTab" = the Pen tab with the eraser's settings shown.</summary>
internal static class PenPageTabs
{
    public static void Select(TabletDetailView view, string tab)
    {
        if (tab == "EraserTab")
        {
            view.FindControl<RadioButton>("PenTab")!.IsChecked = true;
            view.FindControl<RadioButton>("EraserFeelChip")!.IsChecked = true;
        }
        else
        {
            view.FindControl<RadioButton>(tab)!.IsChecked = true;
            if (tab == "PenTab") view.FindControl<RadioButton>("TipFeelChip")!.IsChecked = true;
        }
        Dispatcher.UIThread.RunJobs();
    }
}
