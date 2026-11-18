using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>Version 29 (Zak, 17 Nov 2026): "Send with file" with the email box empty asks "Send without your email?"
/// with "Add my email" and "Send without". It asks; it never blocks.</summary>
public class ReportEmailQuestionTests
{
    static (List<string>, IReadOnlyList<string>, string?) Inputs() => (new List<string>(), Array.Empty<string>(), null);

    static (ReportViewModel vm, List<string> asked, string target) Make()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var vm = new ReportViewModel(Inputs, null, "test") { AskForEmail = true };
        vm.OpenUrl = _ => { }; vm.Reveal = _ => { }; vm.Folder = () => Path.Combine(dir, "out");
        var asked = new List<string>(); var target = Path.Combine(dir, "r.zip");
        vm.PickSaveFile = name => { asked.Add(name); return Task.FromResult<string?>(target); };
        vm.Description = "The pen tap is slow.";
        return (vm, asked, target);
    }

    [Fact]
    public async Task EmptyEmail_AsksFirst_AndSavesNothingYet()
    {
        var (vm, asked, target) = Make();
        await vm.SendFileCommand.ExecuteAsync(null);
        Assert.True(vm.AskingEmail);
        Assert.Empty(asked);
        Assert.False(File.Exists(target));
        Assert.StartsWith("Send without your email?", vm.EmailQuestion);
        Assert.Equal("Add my email", vm.AddEmailLabel);
        Assert.Equal("Send without", vm.SendWithoutEmailLabel);
    }

    [Fact]
    public async Task AddMyEmail_ClosesTheQuestion_AndAsksTheWindowToPutTheCursorInTheEmailBox()
    {
        var (vm, asked, _) = Make();
        var focused = 0; vm.FocusEmailRequested += () => focused++;
        await vm.SendFileCommand.ExecuteAsync(null);
        vm.AddEmailCommand.Execute(null);
        Assert.False(vm.AskingEmail);
        Assert.Equal(1, focused);
        Assert.Empty(asked);

        vm.Email = "artist@example.com";                 // typed in, then Send with file again: no question
        await vm.SendFileCommand.ExecuteAsync(null);
        Assert.False(vm.AskingEmail);
        Assert.Single(asked);
    }

    [Fact]
    public async Task SendWithout_SavesTheReportAsBefore_WithNoEmailInIt()
    {
        var (vm, asked, target) = Make();
        await vm.SendFileCommand.ExecuteAsync(null);
        await vm.SendWithoutEmailCommand.ExecuteAsync(null);
        Assert.False(vm.AskingEmail);
        Assert.Single(asked);
        Assert.True(File.Exists(target));
        using var z = ZipFile.OpenRead(target);
        using var r = new StreamReader(z.GetEntry("report.txt")!.Open());
        Assert.DoesNotContain("Email:", r.ReadToEnd());

        await vm.SendFileCommand.ExecuteAsync(null);     // the next Send with file asks again (the choice is per press)
        Assert.True(vm.AskingEmail);
    }

    [Fact]
    public async Task WithAnEmail_ThereIsNoQuestion()
    {
        var (vm, asked, target) = Make();
        vm.Email = "  artist@example.com ";
        await vm.SendFileCommand.ExecuteAsync(null);
        Assert.False(vm.AskingEmail);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task OnlyTheReportWindowTurnsTheQuestionOn()
    {
        var (vm, asked, target) = Make();
        vm.AskForEmail = false;
        await vm.SendFileCommand.ExecuteAsync(null);
        Assert.False(vm.AskingEmail);
        Assert.True(File.Exists(target));
    }
}
