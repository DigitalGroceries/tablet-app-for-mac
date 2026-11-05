using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 22 BETA 3 (owner's request, 2 Nov 2026): "Report a problem or send a recommendation…" from the app window,
/// the pen menu and the app menu. These test the shared sheet's logic: the window picture, extra pictures, the
/// user's text in the zip, the pre-filled GitHub issue and (Version 23) "Send with file".</summary>
public class ReportFromThreePlacesTests
{
    static (List<string>, IReadOnlyList<string>, string?) Inputs() =>
        (new List<string> { "Wacom PTK-440" }, Array.Empty<string>(), "0.6.8");

    static string Png(string dir, string name)
    {
        var p = Path.Combine(dir, name);
        File.WriteAllBytes(p, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        return p;
    }

    static (ReportViewModel vm, List<string> urls, List<string> revealed, string dir) Make(bool withPicture = true)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var shot = withPicture ? Png(dir, "app-window.png") : null;
        var vm = new ReportViewModel(Inputs, shot, "test");
        var urls = new List<string>(); var revealed = new List<string>();
        vm.OpenUrl = urls.Add; vm.Reveal = revealed.Add;
        var outDir = Path.Combine(dir, "out");
        vm.Folder = () => outDir;
        return (vm, urls, revealed, dir);
    }

    [Fact]
    public void Window_picture_is_added_by_default_and_counted()
    {
        var (vm, _, _, _) = Make();
        Assert.True(vm.HasWindowPicture);
        Assert.True(vm.IncludeWindowPicture);
        Assert.Single(vm.Pictures);
        Assert.Contains("1", vm.PicturesText);
    }

    [Fact]
    public void No_window_picture_when_it_could_not_be_taken()
    {
        var vm = new ReportViewModel(Inputs, "/no/such/file.png", "test");
        Assert.False(vm.HasWindowPicture);
        Assert.False(vm.IncludeWindowPicture);
        Assert.Empty(vm.Pictures);
        Assert.Equal("", vm.PicturesText);
    }

    [Fact]
    public async Task Send_puts_pictures_and_text_in_the_zip_opens_the_issue_and_shows_the_file()
    {
        var (vm, urls, revealed, dir) = Make();
        var extra = Png(dir, "my-shot.png");
        vm.PickImages = () => Task.FromResult<IReadOnlyList<string>>(new[] { extra, extra });
        await vm.AddScreenshotsCommand.ExecuteAsync(null);
        Assert.Single(vm.Screenshots);              // the same file twice is added once
        Assert.Equal(2, vm.Pictures.Count);

        vm.Description = "The ring zooms the wrong way in Affinity.\nSecond line.";
        bool? finished = null;
        vm.Finished += sent => finished = sent;
        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(finished);
        Assert.True(File.Exists(vm.ReportFilePath));
        using (var z = ZipFile.OpenRead(vm.ReportFilePath))
        {
            Assert.NotNull(z.GetEntry("summary.txt"));
            Assert.NotNull(z.GetEntry("screenshots/1-app-window.png"));
            Assert.NotNull(z.GetEntry("screenshots/2-my-shot.png"));
            using var r = new StreamReader(z.GetEntry("report.txt")!.Open());
            var report = r.ReadToEnd();
            Assert.StartsWith("Problem", report);
            Assert.Contains("Second line.", report);
        }
        var url = Assert.Single(urls);
        Assert.StartsWith("https://github.com/DigitalGroceries/tablet-app-for-mac/issues/new?labels=bug&", url);
        var text = Uri.UnescapeDataString(url);
        Assert.Contains("Problem: The ring zooms the wrong way in Affinity.", text);
        Assert.Contains(Path.GetFileName(vm.ReportFilePath), text);
        Assert.Equal(vm.ReportFilePath, Assert.Single(revealed));
    }

    /// <summary>Saves where the user chose (the test's stand-in for the save panel) and records the suggested name.</summary>
    static List<string> SaveTo(ReportViewModel vm, string? path)
    {
        var suggested = new List<string>();
        vm.PickSaveFile = name => { suggested.Add(name); return Task.FromResult(path); };
        return suggested;
    }

    [Fact]
    public async Task Send_with_file_asks_where_to_save_then_says_where_it_is_and_opens_nothing()
    {
        // Version 24 (Zak, 5 Nov 2026): the button asks where to save, then says where the file is and
        // how to upload it. No web page and no Finder until the user asks.
        var (vm, urls, revealed, dir) = Make();
        var chosen = Directory.CreateDirectory(Path.Combine(dir, "My reports")).FullName;
        var suggested = SaveTo(vm, Path.Combine(chosen, "pen report"));   // no .zip typed: it is added
        vm.Description = "Pen buttons swap in Krita.\nSecond line.";
        bool? finished = null;
        vm.Finished += sent => finished = sent;
        await vm.SendFileCommand.ExecuteAsync(null);

        var name = Assert.Single(suggested);
        Assert.StartsWith("TabletApp-diagnostics-", name);
        Assert.EndsWith(".zip", name);
        var saved = Path.Combine(chosen, "pen report.zip");
        Assert.Equal(saved, vm.SavedPath);
        Assert.Equal(saved, vm.ReportFilePath);
        Assert.True(File.Exists(saved));
        using (var z = ZipFile.OpenRead(saved))
        {
            Assert.NotNull(z.GetEntry("summary.txt"));
            Assert.NotNull(z.GetEntry("screenshots/1-app-window.png"));
            using var r = new StreamReader(z.GetEntry("report.txt")!.Open());
            var report = r.ReadToEnd();
            Assert.StartsWith("Problem", report);
            Assert.Contains("Second line.", report);
        }
        Assert.Empty(urls);
        Assert.Empty(revealed);
        Assert.Null(finished);                                        // the sheet stays open with the message
        Assert.True(vm.IsSaved);
        Assert.Contains(saved, vm.SavedMessage);
        Assert.Contains("Open upload page", vm.SavedMessage);
        Assert.DoesNotContain("{", vm.SavedMessage);
        Assert.DoesNotContain(DiagnosticBundle.FeedbackEmail, vm.SavedMessage);
        Assert.Equal("", vm.Status);

        // Zak 5 Nov 2026: no Show in Finder — path is already on the message; only Open upload page + Close.
        Assert.Empty(revealed);
        vm.OpenUploadCommand.Execute(null);
        Assert.Equal(DiagnosticBundle.ReportUploadUrl, Assert.Single(urls));
        vm.DoneCommand.Execute(null);
        Assert.True(finished);
    }

    [Fact]
    public async Task Closing_the_save_panel_saves_nothing_and_keeps_the_report_open()
    {
        var (vm, urls, revealed, _) = Make();
        SaveTo(vm, null);
        var shot = vm.WindowPicturePath!;
        bool? finished = null;
        vm.Finished += sent => finished = sent;
        await vm.SendFileCommand.ExecuteAsync(null);

        Assert.Null(finished);
        Assert.False(vm.IsSaved);
        Assert.Equal("", vm.ReportFilePath);
        Assert.Equal("", vm.SavedMessage);
        Assert.True(File.Exists(shot));                               // still needed if the user tries again
        Assert.Empty(urls); Assert.Empty(revealed);
        Assert.True(vm.SendFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task Send_with_file_replaces_a_file_of_the_same_name()
    {
        // The save panel asks before replacing; once the user agrees, the old file is replaced.
        var (vm, _, _, dir) = Make();
        var target = Path.Combine(dir, "report.zip");
        File.WriteAllText(target, "old");
        SaveTo(vm, target);
        await vm.SendFileCommand.ExecuteAsync(null);
        using var z = ZipFile.OpenRead(target);
        Assert.NotNull(z.GetEntry("report.txt"));
    }

    [Fact]
    public async Task A_failed_save_shows_the_reason_and_keeps_the_report_open()
    {
        var (vm, _, _, dir) = Make();
        SaveTo(vm, Path.Combine(dir, "no-such-folder", "report.zip"));
        bool? finished = null;
        vm.Finished += sent => finished = sent;
        await vm.SendFileCommand.ExecuteAsync(null);
        Assert.Null(finished);
        Assert.False(vm.IsSaved);
        Assert.StartsWith("Couldn't create the report", vm.Status);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task The_temporary_window_picture_is_deleted_after_send_and_after_cancel()
    {
        var (vm, _, _, dir) = Make();
        SaveTo(vm, Path.Combine(dir, "report.zip"));
        var shot = vm.WindowPicturePath!;
        await vm.SendFileCommand.ExecuteAsync(null);
        Assert.False(File.Exists(shot));
        using (var z = ZipFile.OpenRead(vm.ReportFilePath))
            Assert.NotNull(z.GetEntry("screenshots/1-app-window.png"));   // it went into the zip first

        var (vm2, _, _, dir2) = Make();
        var added = Path.Combine(dir2, "mine.png");
        File.WriteAllBytes(added, new byte[] { 1 });
        vm2.Screenshots.Add(added);
        var shot2 = vm2.WindowPicturePath!;
        vm2.CancelCommand.Execute(null);
        Assert.False(File.Exists(shot2));
        Assert.True(File.Exists(added));                                  // the user's own pictures stay
    }

    [Fact]
    public void Upload_page_is_Dropbox_and_shows_in_the_note()
    {
        Assert.Equal("https://www.dropbox.com/request/9k5b0u782t79plarauv9", DiagnosticBundle.ReportUploadUrl);
        var vm = new ReportViewModel(Inputs, null, "test");
        Assert.Contains("Open upload page", vm.Note);
        Assert.DoesNotContain("{email}", vm.Note);
        Assert.DoesNotContain(DiagnosticBundle.FeedbackEmail, vm.Note);
    }

    [Fact]
    public async Task Unticking_the_window_picture_leaves_it_out()
    {
        var (vm, _, _, _) = Make();
        vm.IncludeWindowPicture = false;
        Assert.Empty(vm.Pictures);
        await vm.SendCommand.ExecuteAsync(null);
        using var z = ZipFile.OpenRead(vm.ReportFilePath);
        Assert.DoesNotContain(z.Entries, e => e.FullName.StartsWith("screenshots/"));
    }

    [Fact]
    public async Task A_recommendation_gets_its_own_title_and_label()
    {
        var (vm, urls, _, _) = Make(withPicture: false);
        vm.IsRecommendation = true;
        vm.Description = "Please add a dark menu icon.";
        await vm.SendCommand.ExecuteAsync(null);
        var url = Assert.Single(urls);
        Assert.Contains("labels=enhancement", url);
        Assert.Contains("Recommendation: Please add a dark menu icon.", Uri.UnescapeDataString(url));
        using var z = ZipFile.OpenRead(vm.ReportFilePath);
        using var r = new StreamReader(z.GetEntry("report.txt")!.Open());
        Assert.StartsWith("Recommendation", r.ReadToEnd());
    }

    [Fact]
    public async Task Cancel_closes_without_making_a_file_or_opening_a_page()
    {
        var (vm, urls, revealed, _) = Make();
        bool? finished = null;
        vm.Finished += sent => finished = sent;
        vm.CancelCommand.Execute(null);
        await Task.Yield();
        Assert.False(finished);
        Assert.Empty(urls); Assert.Empty(revealed);
        Assert.Equal("", vm.ReportFilePath);
    }

    [Fact]
    public void At_most_ten_pictures()
    {
        var (vm, _, _, dir) = Make();
        for (int i = 0; i < 15; i++) vm.Screenshots.Add(Png(dir, $"s{i}.png"));
        Assert.Equal(ReportViewModel.MaxPictures, vm.Pictures.Count);
        Assert.Equal(Path.Combine(dir, "app-window.png"), vm.Pictures[0]);
    }

    [Fact]
    public void Long_text_is_cut_in_the_link_but_the_title_stays_short()
    {
        var longText = new string('a', 5000);
        var url = DiagnosticBundle.ReportIssueUrl("x.zip", false, longText, null, Array.Empty<string>());
        var text = Uri.UnescapeDataString(url);
        Assert.Contains("report.txt", text);
        Assert.True(url.Length < 8000, $"link is {url.Length} characters");
        var title = text.Split("&title=")[1].Split("&body=")[0];
        Assert.True(title.Length < 90, title);
    }

    [Fact]
    public void Empty_text_still_makes_a_clear_title()
    {
        var url = DiagnosticBundle.ReportIssueUrl("x.zip", false, "  ", null, Array.Empty<string>());
        var text = Uri.UnescapeDataString(url);
        Assert.Contains("title=Problem from the app", text);
        Assert.Contains("(no text)", text);
    }

    [Fact]
    public void Serial_numbers_in_tablet_names_are_removed_from_the_link()
    {
        var url = DiagnosticBundle.ReportIssueUrl("x.zip", false, "x", null, new[] { "Wacom PTK-440 serial: 8HAB001234" });
        var text = Uri.UnescapeDataString(url);
        Assert.DoesNotContain("8HAB001234", text);
        Assert.Contains("[removed]", text);
    }

    [Fact]
    public void Home_feedback_link_asks_for_the_report()
    {
        var about = new AboutViewModel();
        int asked = 0;
        about.ReportRequested += () => asked++;
        about.OpenReportCommand.Execute(null);
        Assert.Equal(1, asked);
    }

    [Fact]
    public void Every_language_has_the_report_texts()
    {
        var dir = Path.Combine(Root(), "OpenTabletArtist", "Assets", "i18n");
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var keys = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
            foreach (var k in new[] { "report.menu", "report.title", "report.note", "report.window_picture",
                         "report.send_github", "report.send_file", "report.save_title", "report.saved_title",
                         "report.saved_text", "report.show_file", "report.open_upload", "app.close",
                         "diag.report.cancel", "diag.report.screenshots" })
                Assert.True(keys[k] != null, $"{Path.GetFileName(file)} has no {k}");
            Assert.DoesNotContain("{email}", (string)keys["report.note"]!);
            Assert.Contains("{path}", (string)keys["report.saved_text"]!);
            Assert.DoesNotContain("{email}", (string)keys["report.saved_text"]!);
            // The email button and its message are gone (owner's request, 4 Nov 2026).
            Assert.Null(keys["report.send_email"]);
            Assert.Null(keys["report.mail_opened"]);
        }
    }

    static string Root([System.Runtime.CompilerServices.CallerFilePath] string f = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, "..", ".."));
}
