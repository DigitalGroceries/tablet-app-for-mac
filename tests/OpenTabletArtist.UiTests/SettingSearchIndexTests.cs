using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Converters;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>The index walk changes the app's language, so it runs on its own, after the other tests.</summary>
[CollectionDefinition(nameof(SettingSearchIndexCollection), DisableParallelization = true)]
public class SettingSearchIndexCollection { }

/// <summary>Version 29 (Setting search on Home): the index lists the words on every page and tab, in every
/// app language. This test walks the real pages (laid out as on a Mac) and fails when the index files in
/// OpenTabletArtist/Assets/search/ no longer match them. To write them again, run this test on Linux with
/// OTA_WRITE_SEARCH_INDEX=1.</summary>
[Collection(nameof(SettingSearchIndexCollection))]
public class SettingSearchIndexTests
{
    public const string WriteVariable = "OTA_WRITE_SEARCH_INDEX";

    [AvaloniaFact]
    public void The_index_of_every_language_matches_the_pages()
    {
        // The pages are laid out as on a Mac on every machine, but a real Mac also asks its own services (the
        // login item's state adds a line), so the index is made, and checked, on Linux: the GitHub ubuntu box.
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The setting-search index is made and checked on Linux.");
        var folder = SettingSearchIndexWalk.IndexFolder();
        var write = Environment.GetEnvironmentVariable(WriteVariable) == "1";
        var stale = new List<string>();
        var preference = Localizer.Preference;
        try
        {
            foreach (var code in SettingSearchIndexWalk.Languages)
            {
                var json = JsonSerializer.Serialize(SettingSearchIndexWalk.Build(code), SettingSearch.Json) + "\n";
                var file = Path.Combine(folder, code + ".json");
                var old = File.Exists(file) ? File.ReadAllText(file) : null;
                if (old == json) continue;
                if (write) File.WriteAllText(file, json);
                else stale.Add($"{code}: {FirstDifference(old, json)}");
            }
        }
        finally
        {
            Localizer.SetPreference(preference);
            SettingSearch.ResetForTests();
        }
        Assert.True(stale.Count == 0,
            $"The setting-search index is out of date (a page changed). Run this test with {WriteVariable}=1 and commit "
            + "OpenTabletArtist/Assets/search/*.json.\n" + string.Join("\n", stale));
    }

    static string FirstDifference(string? old, string now)
    {
        if (old is null) return "no index file";
        var a = old.Split('\n');
        var b = now.Split('\n');
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : "(end)";
            var y = i < b.Length ? b[i] : "(end)";
            if (x != y) return $"line {i + 1}: file has {x.Trim()} | pages have {y.Trim()}";
        }
        return "line endings differ";
    }

    [AvaloniaFact]
    public void Every_index_entry_has_words_a_place_and_a_route_the_app_knows()
    {
        foreach (var code in SettingSearchIndexWalk.Languages)
        {
            var file = Path.Combine(SettingSearchIndexWalk.IndexFolder(), code + ".json");
            var list = JsonSerializer.Deserialize<List<SettingSearchEntry>>(File.ReadAllText(file), SettingSearch.Json)!;
            Assert.True(list.Count > 100, $"{code}: only {list.Count} entries");
            foreach (var e in list)
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Text), $"{code}: empty words at {e.Route}");
                Assert.False(string.IsNullOrWhiteSpace(e.Path), $"{code}: no place for '{e.Text}'");
                Assert.True(SettingSearchRoute.TryParse(e.Route, out _), $"{code}: unknown route '{e.Route}' for '{e.Text}'");
            }
        }
    }
}

/// <summary>Walks every page and tab of the real app in one language and lists the words on it.</summary>
internal static class SettingSearchIndexWalk
{
    public static readonly string[] Languages =
        Localizer.Languages.Select(l => l.Code).Where(c => c != Localizer.SystemCode).ToArray();

    /// <summary>OpenTabletArtist/Assets/search in the source tree (found from the test's folder upwards).</summary>
    public static string IndexFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var assets = Path.Combine(dir.FullName, "OpenTabletArtist", "Assets");
            if (Directory.Exists(assets))
            {
                var search = Path.Combine(assets, "search");
                Directory.CreateDirectory(search);
                return search;
            }
        }
        throw new DirectoryNotFoundException("OpenTabletArtist/Assets not found above " + AppContext.BaseDirectory);
    }

    public static List<SettingSearchEntry> Build(string code)
    {
        Localizer.SetPreference(code);
        using var mac = UiPlatform.PretendMac();
        var list = new List<SettingSearchEntry>();
        var seen = new HashSet<(string, string)>();
        void Collect(Visual root, string path, string route, Func<Control, bool>? skip = null)
        {
            // A details panel's route ("tablet/ToolPenButton/PenTab/FeelDetails") lists only what the closed panel hid.
            var parent = route.Count(c => c == '/') == 3 ? route[..route.LastIndexOf('/')] : null;
            foreach (var text in Words(root, skip))
                if ((parent is null || !seen.Contains((text, parent))) && seen.Add((text, route)))
                    list.Add(new SettingSearchEntry(text, path, route));
        }

        var w = new MainWindow { Width = 1100, Height = 760 };
        w.Show();
        Pump(w);
        var vm = (MainViewModel)w.DataContext!;
        // The Settings and Advanced tab rails name the pages; they aren't words on them.
        bool Rails(Control c) => c is ItemsControl ic
            && (ReferenceEquals(ic.ItemsSource, vm.Settings.Tabs) || ReferenceEquals(ic.ItemsSource, vm.Advanced.Tabs));
        void CollectPage(Visual root, string path, string route, Func<Control, bool>? skip = null) =>
            Collect(root, path, route, c => Rails(c) || skip?.Invoke(c) == true);
        try
        {
            string Nav(object page) => Title(vm.NavSections.First(n => ReferenceEquals(n.Page, page)).Label);
            var content = w.GetVisualDescendants().OfType<TransitioningContentControl>().First();

            vm.NavigateCommand.Execute(vm.Dashboard); Pump(w);
            CollectPage(content, Nav(vm.Dashboard), "home");
            if (w.FindControl<ContentControl>("HomeLinksHost") is { } links) CollectPage(links, Nav(vm.Dashboard), "home");

            vm.NavigateCommand.Execute(vm.Test); Pump(w);
            CollectPage(content, Nav(vm.Test), "scribble");

            // A page with a sub-tab rail (Settings › Theme, Settings › Dev) is collected one sub-tab at a time.
            void Tabbed(string path, string route)
            {
                var rail = content.GetVisualDescendants().OfType<Panel>().FirstOrDefault(p => p.Name == "SubtabRail" && p.IsEffectivelyVisible);
                bool Rail(Control c) => c.Name == "SubtabRail";
                if (rail is null) { CollectPage(content, path, route, Rail); return; }
                foreach (var r in rail.Children.OfType<RadioButton>().Where(r => r.IsVisible && r.Name is { Length: > 0 }).ToList())
                {
                    r.IsChecked = true; Pump(w);
                    CollectPage(content, $"{path} › {Title(r.Content as string)}", $"{route}/{r.Name}", Rail);
                }
            }

            foreach (var tab in vm.Settings.Tabs.Where(t => t.IsVisible).ToList())
            {
                vm.Settings.SelectedTab = tab.Tab;
                vm.NavigateCommand.Execute(vm.Settings); Pump(w);
                Tabbed($"{Nav(vm.Settings)} › {Title(tab.Label)}", $"settings/{tab.Tab}");
            }

            foreach (var tab in vm.Advanced.Tabs.Where(t => t.IsVisible).ToList())
            {
                vm.Advanced.SelectedTab = tab.Tab;
                vm.NavigateCommand.Execute(vm.Advanced); Pump(w);
                Tabbed($"{Nav(vm.Advanced)} › {Title(tab.Label)}", $"advanced/{tab.Tab}");
            }

            TabletPages(Nav(vm.TabletPage), Collect);
        }
        finally
        {
            w.AllowCloseForQuit();
            w.Close();
            (vm as IDisposable)?.Dispose();
            Pump(null);
        }

        // Words on many pages ("On", "Save") would only crowd the list; their pages are found by their own words.
        var common = list.GroupBy(e => e.Text).Where(g => g.Count() > 5).Select(g => g.Key).ToHashSet();
        return list.Where(e => !common.Contains(e.Text)).ToList();
    }

    /// <summary>The TABLET page, with a sample Intuos4 (ExpressKeys and a Touch Ring): each Tool tile and its tabs.</summary>
    static void TabletPages(string tabletLabel, Action<Visual, string, string, Func<Control, bool>?> collect)
    {
        var settings = SampleTablet();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live),
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>((settings, settings.Profiles[0])),
            tabletDigitizer: (224f, 148f)) { ShowHeader = false };
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = new Border { Child = view, Padding = new Thickness(24, 0) }, Width = 1100, Height = 760 };
        w.Show();
        Pump(w);
        try
        {
            var tools = new (string Tile, string Label, string[] Tabs)[]
            {
                ("ToolFunctionsButton", "LblFunctions", new[] { "ButtonsTab", "WheelsTab" }),
                ("ToolPenButton", "LblToolPen", new[] { "PenTab", "EraserTab", "MappingTab" }),
                ("ToolMoreButton", "LblMore", new[] { "AboutTab", "CalibrationTab", "HoverTab", "FiltersTab", "JsonTab" }),
            };
            bool Chrome(Control c) => c.Name is "WacomHeader" or "TabRail";
            foreach (var (tile, label, tabs) in tools)
            {
                var toolName = Title(view.FindControl<TextBlock>(label)?.Text) ?? tile;
                foreach (var tab in tabs)
                {
                    if (!view.OpenSearchPlace(tile, tab)) continue;
                    Pump(w);
                    var pivot = view.FindControl<RadioButton>(tab)!;
                    if (!pivot.IsVisible) continue;
                    var path = $"{tabletLabel} › {toolName} › {Title(pivot.Content as string)}";
                    collect(view, path, $"tablet/{tile}/{tab}", Chrome);
                    var panel = tab switch { "PenTab" => "FeelDetails", "EraserTab" => "EraserFeelDetails", _ => null };
                    if (panel is null || !view.OpenSearchPlace(tile, tab, panel)) continue;
                    Pump(w);
                    collect(view, path, $"tablet/{tile}/{tab}/{panel}", Chrome);
                    view.FindControl<Control>(panel)!.IsVisible = false;
                }
            }
        }
        finally
        {
            w.Close();
            vm.Dispose();
        }
    }

    static Settings SampleTablet()
    {
        var p = new Profile
        {
            Tablet = "PTK-470",
            OutputMode = new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)),
            AbsoluteModeSettings = new AbsoluteModeSettings
            {
                Display = new AreaSettings { Width = 2560, Height = 1440, X = 1280, Y = 720 },
                Tablet = new AreaSettings { Width = 224, Height = 148, X = 112, Y = 74 },
            },
            BindingSettings = new BindingSettings(),
        };
        var b = p.BindingSettings;
        b.TipButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
        b.EraserButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Eraser);
        b.PenButtons = new OpenTabletDriver.Desktop.Reflection.PluginSettingStoreCollection
        {
            PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton),
            PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton),
        };
        b.AuxButtons = new OpenTabletDriver.Desktop.Reflection.PluginSettingStoreCollection();
        foreach (var k in new[] { "B", "E", "Space", "Control", "Shift", "Alt", "Z", "H" }) b.AuxButtons.Add(AuxKeyBinding.MakeKeyBinding(k));
        b.WheelBindings = new List<WheelBindingSettings> { new() };
        return new Settings { Profiles = new ProfileCollection { p } };
    }

    /// <summary>Home's notices come and go with the machine's state (driver stopped, a permission missing); they
    /// aren't places to find.</summary>
    static readonly HashSet<string> Notices = new()
    {
        "AttentionNotices", "AccessibilityMissingCard", "PermissionsResetCard", "OfflineOfferCard",
    };

    static readonly Regex Numbers = new(@"\d{2,}|\d[.,:]\d", RegexOptions.Compiled);

    /// <summary>The short labels on screen under <paramref name="root"/>: setting names, switch and button
    /// labels, headings. Left out: sentences (descriptions), numbers and dates, what is typed in boxes and
    /// lists, and the parts <paramref name="skip"/> names.</summary>
    static IEnumerable<string> Words(Visual root, Func<Control, bool>? skip)
    {
        foreach (var tb in root.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!tb.IsEffectivelyVisible || tb is SelectableTextBlock) continue;
            var inside = tb.GetVisualAncestors().TakeWhile(a => !ReferenceEquals(a, root)).OfType<Control>().ToList();
            if (inside.Any(a => a is ComboBox or TextBox or ListBox or AutoCompleteBox or ToolTip
                                || a.Name is { } n && Notices.Contains(n) || skip?.Invoke(a) == true)) continue;
            var raw = tb.Text ?? string.Concat(tb.Inlines?.OfType<Run>().Select(r => r.Text) ?? Array.Empty<string>());
            if (string.IsNullOrWhiteSpace(raw) || raw.Contains('\n')) continue;
            var text = Regex.Replace(raw, @"\s+", " ").Trim().TrimEnd(':', '…').Trim();
            if (text.Length < 2 || text.Length > 60) continue;
            if (text.Count(char.IsLetter) < 2 || Numbers.IsMatch(text)) continue;
            if (text.Length > 30 && text.EndsWith('.')) continue;
            if (text.StartsWith('/') || text.StartsWith('~') || text.Contains(":\\")) continue;   // a folder on this machine
            yield return text;
        }
    }

    static string Title(string? s) => TitleCaseConverter.Apply(s) ?? "";

    /// <summary>Run the queued work. Page fades are switched off first (the next page then replaces the last one at
    /// once), so only the page now shown is on screen.</summary>
    static void Pump(Window? w)
    {
        for (int i = 0; i < 3; i++)
        {
            if (w != null)
                foreach (var t in w.GetVisualDescendants().OfType<TransitioningContentControl>()) t.PageTransition = null;
            Dispatcher.UIThread.RunJobs();
            w?.UpdateLayout();
        }
    }
}
