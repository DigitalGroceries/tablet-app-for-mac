using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build P: start-at-login prompt-once logic and the Settings toggle's sync with the real login item.</summary>
public class BuildPTests
{
    private sealed class FakeLoginItems : ILoginItemService
    {
        public bool IsSupported { get; set; } = true;
        public LoginItemStatus Status { get; set; } = LoginItemStatus.NotRegistered;
        public string? FailWith { get; set; }
        public int Opened;
        public bool TryEnable(out string? error)
        {
            error = FailWith;
            if (FailWith != null) return false;
            Status = LoginItemStatus.Enabled; return true;
        }
        public bool TryDisable(out string? error) { error = null; Status = LoginItemStatus.NotRegistered; return true; }
        public void OpenLoginItemsSettings() => Opened++;
    }

    private static (AutoStartCoordinator C, FakeLoginItems F, Dictionary<string, string> Store) Make(Dictionary<string, string>? store = null)
    {
        var f = new FakeLoginItems();
        var s = store ?? new Dictionary<string, string>();
        return (new AutoStartCoordinator(f, k => s.TryGetValue(k, out var v) ? v : null, (k, v) => s[k] = v), f, s);
    }

    [Fact]
    public void Prompt_IsShownOnce_ForNewAndUpgradingUsers()
    {
        var (c, _, store) = Make(new Dictionary<string, string> { ["Theme"] = "Dark" });   // an upgrading user's settings
        Assert.True(c.ShouldPrompt);
        Assert.Null(c.Choose(automatic: false));
        Assert.False(c.ShouldPrompt);
        Assert.Equal("manual", store[AutoStartCoordinator.ChoiceKey]);
        Assert.False(Make(store).C.ShouldPrompt);   // next launch: remembered
    }

    [Fact]
    public void Prompt_NotShown_WhereUnsupported()
    {
        var (c, f, _) = Make();
        f.IsSupported = false;
        Assert.False(c.ShouldPrompt);
    }

    [Fact]
    public void StartAutomatically_RegistersLoginItem()
    {
        var (c, f, store) = Make();
        Assert.Null(c.Choose(automatic: true));
        Assert.Equal(LoginItemStatus.Enabled, f.Status);
        Assert.Equal("auto", store[AutoStartCoordinator.ChoiceKey]);
    }

    [Fact]
    public void RegisterFailure_GivesLoginItemsFallbackMessage()
    {
        var (c, f, _) = Make();
        f.FailWith = "Operation not permitted";
        var msg = c.Choose(automatic: true);
        Assert.NotNull(msg);
        Assert.Contains("System Settings › General › Login Items", msg);
        Assert.Contains("Operation not permitted", msg);
        Assert.False(c.ShouldPrompt);   // still asked only once
    }

    [Fact]
    public void Toggle_FollowsRealState_WhenRemovedInSystemSettings()
    {
        var (c, f, store) = Make();
        c.Choose(true);
        var vm = new StartupViewModel(c);
        Assert.True(vm.StartAtLogin);
        f.Status = LoginItemStatus.NotRegistered;        // user removed it in System Settings
        vm.RefreshLoginItem();
        Assert.False(vm.StartAtLogin);
        Assert.Equal("manual", store[AutoStartCoordinator.ChoiceKey]);
        f.Status = LoginItemStatus.RequiresApproval;
        vm.RefreshLoginItem();
        Assert.True(vm.StartAtLogin);
        Assert.Contains("approval", vm.LoginMessage);
    }

    [Fact]
    public void Toggle_WritesThrough_AndShowsFallbackOnFailure()
    {
        var (c, f, _) = Make();
        var vm = new StartupViewModel(c);
        vm.StartAtLogin = true;
        Assert.Equal(LoginItemStatus.Enabled, f.Status);
        vm.StartAtLogin = false;
        Assert.Equal(LoginItemStatus.NotRegistered, f.Status);
        f.FailWith = "denied";
        vm.StartAtLogin = true;
        Assert.False(vm.StartAtLogin);                 // snapped back to the real state
        Assert.Contains("Login Items", vm.LoginMessage);
    }

    [Theory]
    [InlineData("01:30", 90)]
    [InlineData("1:02:03", 3723)]
    [InlineData("2-00:00:05", 172805)]
    public void Etime_Parses(string s, int seconds) => Assert.Equal(seconds, (int)AutoStartCoordinator.ParseEtime(s)!.Value.TotalSeconds);

    [Fact]
    public void LoginLaunch_OnlyWhenAutoChosen_AndSessionIsYoung()
    {
        var (c, _, _) = Make();
        Assert.False(c.IsLikelyLoginLaunch(() => TimeSpan.FromSeconds(20)));   // no choice yet
        c.Choose(true);
        Assert.True(c.IsLikelyLoginLaunch(() => TimeSpan.FromSeconds(20)));
        Assert.False(c.IsLikelyLoginLaunch(() => TimeSpan.FromMinutes(30)));
        Assert.False(c.IsLikelyLoginLaunch(() => null));
    }

    [AvaloniaFact]
    public void Dialog_DefaultsToStartAutomatically_AndRecordsTheAnswer()
    {
        var d = new AutoStartDialog();
        d.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(d.AutoButton.IsDefault);
        Assert.Equal("Start automatically", d.AutoButton.Content);
        Assert.Equal("Manually", d.ManualButton.Content);
        d.ManualButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.False(d.Result);
    }
}
