using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenTabletArtist.Services;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>V30 #200 B5 (Zak 23 Nov 2026: a troubleshooting/FAQ button; team vote: small, on the existing Help page, Fix it only
/// where the app can really fix it, no new window): the manual's "Something doesn't work?" answers end with in-app buttons.</summary>
public class HelpFixesTests
{
    [Fact]
    public void Only_a_line_that_is_just_a_known_app_link_is_a_button()
    {
        Assert.Equal(("Set them up now", "keys"), HelpFixes.ParseLine("[Set them up now](app:keys)"));
        Assert.Equal(("Report a problem", "report"), HelpFixes.ParseLine("  [Report a problem](app:report)  "));
        Assert.Null(HelpFixes.ParseLine("[Wipe](app:wipe-everything)"));
        Assert.Null(HelpFixes.ParseLine("See [the site](https://example.com)"));
        Assert.Null(HelpFixes.ParseLine("Text before [Set them up now](app:keys)"));
    }

    [AvaloniaFact]
    public void The_manual_has_the_five_answers_with_their_buttons_and_no_raw_app_links()
    {
        var md = ManualWindow.Load("en");
        Assert.Contains("## Something doesn't work?", md);
        var pressed = new List<string>();
        var page = ManualWindow.Build(md, a => pressed.Add(a));
        var w = new Window { Content = new ScrollViewer { Content = page }, Width = 900, Height = 900 }; w.Show();
        var buttons = page.Children.OfType<Button>().Where(b => b.Name?.StartsWith("HelpFix_") == true).ToList();
        Assert.Equal(new[] { "keys", "ring", "pointer", "driver", "report" }, buttons.Select(b => (string)b.Tag!).ToArray());
        Assert.Equal("Set them up now", buttons[0].Content);
        Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains("app:"));
        foreach (var b in buttons) b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { "keys", "ring", "pointer", "driver", "report" }, pressed.ToArray());
        w.Close();
    }

    [AvaloniaFact]
    public void An_unknown_app_link_is_left_out_of_the_manual_window()
    {
        var page = ManualWindow.Build("Before.\n\n[Wipe](app:wipe-everything)\n\nAfter.");
        Assert.Empty(page.Children.OfType<Button>());
        Assert.DoesNotContain(page.Children.OfType<TextBlock>(), t => (t.Text ?? "").Contains("Wipe"));
    }

    [Fact]
    public void The_app_actions_and_the_manual_name_the_same_buttons()
    {
        var md = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "OpenTabletArtist", "Assets", "manual", "manual.md"));
        var named = md.Split('\n').Select(HelpFixes.ParseLine).Where(x => x != null).Select(x => x!.Value.Action).ToArray();
        Assert.Equal(HelpFixes.Actions, named);
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(thisFile)!, "..", ".."));
}
