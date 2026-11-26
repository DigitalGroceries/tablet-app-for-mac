using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.SweepTests;

/// <summary>Version 29 (Setting search on Home): Home with the box, the list of matches, and a chosen setting
/// marked on its page. Written to $SHOTS_DIR (default /workspace/ota-29-search).</summary>
public class V29SearchShots
{
    static string Dir => Environment.GetEnvironmentVariable("SHOTS_DIR") ?? Sweep.ShotsRoot + "/ota-29-search";

    static void Settle(MainWindow w)
    {
        for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
    }

    [AvaloniaTheory]
    [InlineData("en", "pressure")]
    [InlineData("bg", "натиск")]
    public void Home_search_list_and_mark(string lang, string query)
    {
        Localizer.SetPreference(lang);
        SettingSearch.ResetForTests();
        using var mac = UiPlatform.PretendMac();
        var w = new MainWindow { Width = 1100, Height = 760 };
        w.Show();
        try
        {
            var vm = (MainViewModel)w.DataContext!;
            vm.NavigateCommand.Execute(vm.Dashboard);
            Settle(w);
            Directory.CreateDirectory(Dir);
            w.CaptureRenderedFrame()!.Save($"{Dir}/{lang}-1-home.png");

            vm.Search.Query = query;
            Settle(w);
            w.CaptureRenderedFrame()!.Save($"{Dir}/{lang}-2-list.png");

            var hit = SettingSearch.Find("automatic backups").FirstOrDefault()
                      ?? SettingSearch.Index().First(e => e.Route == "settings/System");
            vm.Search.OpenCommand.Execute(hit);
            Settle(w);
            w.CaptureRenderedFrame()!.Save($"{Dir}/{lang}-3-opened-and-marked.png");
        }
        finally
        {
            w.AllowCloseForQuit();
            w.Close();
            Localizer.SetPreference("en");
            SettingSearch.ResetForTests();
        }
    }

    /// <summary>Item 4: the new languages' longest tab names (Swedish FÖRINSTÄLLNINGAR, KONFIGURATIONER) on screen.</summary>
    [AvaloniaTheory]
    [InlineData("it")]
    [InlineData("sv")]
    [InlineData("pt-BR")]
    [InlineData("hi")]      // Version 30
    [InlineData("sw")]
    public void New_language_pages(string lang)
    {
        Localizer.SetPreference(lang);
        SettingSearch.ResetForTests();
        using var mac = UiPlatform.PretendMac();
        var w = new MainWindow { Width = 1100, Height = 760 };
        w.Show();
        try
        {
            var vm = (MainViewModel)w.DataContext!;
            Directory.CreateDirectory(Dir);
            foreach (var (name, page) in new (string, object)[] { ("home", vm.Dashboard), ("settings", vm.Settings), ("advanced", vm.Advanced) })
            {
                vm.NavigateCommand.Execute(page);
                Settle(w);
                w.CaptureRenderedFrame()!.Save($"{Dir}/lang-{lang}-{name}.png");
            }
        }
        finally
        {
            w.AllowCloseForQuit();
            w.Close();
            Localizer.SetPreference("en");
            SettingSearch.ResetForTests();
        }
    }
}
