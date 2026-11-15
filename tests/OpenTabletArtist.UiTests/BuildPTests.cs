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
    public void StatusLightLabels_RefreshOnLanguageChange()   // item 25 (#27)
    {
        var (c, _, _) = Make();
        var vm = new StartupViewModel(c);
        var changed = new System.Collections.Generic.List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.RefreshLocalized();
        Assert.Contains(nameof(StartupViewModel.StatusLedSectionTitle), changed);
        Assert.Contains(nameof(StartupViewModel.StatusLedLabel), changed);
        Assert.Contains(nameof(StartupViewModel.StatusLedDescription), changed);
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

    // Version 28 #79 (Daniel's report, 15 Nov; tests agreed with Devin): the quiet start is only for users who
    // asked for a start at login. The login item's real state decides; the saved choice only when macOS can't tell.
    [Fact]
    public void QuietStart_ManualChoice_SoonAfterLogin_ShowsWindow()
    {
        var (c, _, _) = Make();
        c.Choose(false);                                                     // "manual", login item NotRegistered
        Assert.False(c.IsLoginTimeLaunch(() => TimeSpan.FromSeconds(30)));
        Assert.False(c.IsLikelyLoginLaunch(() => TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void QuietStart_AutoChoice_ButLoginItemSwitchedOff_ShowsWindow()
    {
        var (c, f, _) = Make();
        c.Choose(true);
        f.Status = LoginItemStatus.NotRegistered;                            // removed in System Settings
        Assert.False(c.IsLoginTimeLaunch(() => TimeSpan.FromSeconds(30)));
        Assert.False(c.IsLikelyLoginLaunch(() => TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(LoginItemStatus.Enabled)]
    [InlineData(LoginItemStatus.RequiresApproval)]
    public void QuietStart_LoginItemOn_SoonAfterLogin_StartsHidden(LoginItemStatus status)
    {
        var (c, f, _) = Make();
        c.Choose(true);
        f.Status = status;
        Assert.True(c.IsLoginTimeLaunch(() => TimeSpan.FromSeconds(30)));
        Assert.False(c.IsLoginTimeLaunch(() => TimeSpan.FromMinutes(5)));   // opened by hand later: window
    }

    [Theory]
    [InlineData("auto", true)]
    [InlineData("manual", false)]
    [InlineData(null, false)]
    public void QuietStart_StateUnreadable_FallsBackToSavedChoice(string? choice, bool hidden)
    {
        Assert.Equal(hidden, AutoStartCoordinator.WantsLoginStart(LoginItemStatus.NotFound, choice, out var src));
        Assert.Contains("saved choice", src);
        Assert.Equal(hidden, AutoStartCoordinator.WantsLoginStart(LoginItemStatus.Unsupported, choice, out _));
        Assert.True(AutoStartCoordinator.WantsLoginStart(LoginItemStatus.Enabled, "manual", out src));   // real state wins
        Assert.Contains("login item", src);
    }

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
