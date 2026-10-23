using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R7: the pen card reports the binding, not hardware health — "Default setting" or
/// "Modified to X" (+ Fix); never "Working normally" or "custom action".</summary>
public class BuildR7PenStatusTests
{
    public sealed class Row
    {
        public string SectionLabel { get; init; } = "BUTTON 1";
        public string CurrentPlainLabel { get; init; } = "Right click";
        public bool ShowChooseAction { get; init; }
        public bool IsRecommended { get; init; }
        public bool IsNotRecommended => !IsRecommended;
        public bool ShowUseAdaptive => true;
    }

    private static string[] Visible(bool recommended)
    {
        var card = new PenSwitchCard { DataContext = new Row { IsRecommended = recommended } };
        var w = new Window { Content = card }; w.Show(); Dispatcher.UIThread.RunJobs();
        var t = card.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).Select(x => x.Text ?? "").ToArray();
        w.Close();
        return t;
    }

    [AvaloniaFact]
    public void Default_SaysDefaultSetting()
    {
        var t = Visible(true);
        Assert.Contains("Default setting", t);
        Assert.DoesNotContain(t, s => s.Contains("Working normally") || s.Contains("custom action") || s.StartsWith("Modified"));
    }

    [AvaloniaFact]
    public void Modified_SaysModifiedToX_InOneLine_WithFix()
    {
        var t = Visible(false);
        Assert.Contains("Modified to Right click", t);
        Assert.Contains("Fix", t);
        Assert.DoesNotContain(t, s => s.Contains("Currently:") || s.Contains("custom action") || s == "Default setting");
    }
}
