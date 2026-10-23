using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Configurations.Parsers.Wacom.Intuos4;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>Build R15: the tip bar follows only pen-tip reports and the eraser bar only eraser reports,
/// including the Intuos4 (PTK-440) path where the eraser is announced once by a tool report.</summary>
public class BuildR15Tests
{
    private sealed class Feed : IDaemonDebugSession
    {
        public event Action<JObject>? DeviceReport;
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
        private readonly Intuos4ReportParser _parser = new();

        private void Send(JToken data) => DeviceReport?.Invoke(new JObject
        {
            ["Tablet"] = new JObject { ["Properties"] = new JObject { ["Name"] = "Wacom PTK-440", ["Specifications"] = new JObject
            {
                ["Digitizer"] = new JObject { ["Width"] = 157.5, ["Height"] = 98.4, ["MaxX"] = 31496, ["MaxY"] = 19685 },
                ["Pen"] = new JObject { ["MaxPressure"] = 2047 },
            } } },
            ["Data"] = data,
        });

        /// <summary>Exactly what the daemon sends: a real Intuos4 report from the PTK-440's parser, serialised
        /// the way DebugReportData does (JToken.FromObject).</summary>
        public void Bytes(params byte[] report) => Send(JToken.FromObject(_parser.Parse(report)));

        /// <summary>Tool-in-proximity report (0x02 0xC2 …): bit 7 of byte 3 marks the eraser.</summary>
        public void ToolIn(bool eraser) => Bytes(0x02, 0xC2, 0x80, (byte)(eraser ? 0x80 : 0x00), 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);

        /// <summary>Pen report (0x02, byte 1 bit 5 = in range) with an 11-bit pressure.</summary>
        public void Pen(int pressure) => Bytes(0x02, 0xE0, 0x30, 0x00, 0x20, 0x00,
            (byte)(pressure >> 3), (byte)(((pressure >> 1) & 0x03) << 6), 0x00, 0x10);

        public void Generic(double pressure, bool eraser) => Send(new JObject
        {
            ["Position"] = new JObject { ["X"] = 100, ["Y"] = 100 }, ["Pressure"] = pressure, ["Eraser"] = eraser,
        });
    }

    private static (Feed, TabletDetailViewModel, TabletDetailView, Window) Open()
    {
        _ = new ThemeViewModel();
        var feed = new Feed();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) }; TestPens.Arm(profile);
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, penInput: feed);
        var view = new TabletDetailView { DataContext = vm };
        var w = new Window { Content = view, Width = 1100, Height = 1000 }; w.Show();
        view.FindControl<RadioButton>("PenTab")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        return (feed, vm, view, w);
    }

    private static ProgressBar Bar(TabletDetailView v, string tab)
    {
        v.FindControl<RadioButton>(tab)!.IsChecked = true; Dispatcher.UIThread.RunJobs();
        var label = v.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Current pressure" && t.IsEffectivelyVisible);
        return ((Panel)label.Parent!.Parent!).Children.OfType<ProgressBar>().Single();
    }

    [AvaloniaFact]
    public void Intuos4_ParsedReports_CarryTheToolAndPressure()
    {
        var p = new Intuos4ReportParser();
        var tool = JToken.FromObject(p.Parse(new byte[] { 0x02, 0xC2, 0x80, 0x80, 0, 0, 0, 0, 0, 0 }));
        Assert.Equal(1, tool["Tool"]!.Value<int>());        // ToolType.Eraser, serialised as a number
        Assert.True(tool["Eraser"]!.Value<bool>());
        var pen = JToken.FromObject(p.Parse(new byte[] { 0x02, 0xE0, 0x30, 0, 0x20, 0, 128, 0, 0, 0x10 }));
        Assert.Null(pen["Eraser"]);                         // IntuosV1TabletReport has no eraser bit
        Assert.Equal(1024u, pen["Pressure"]!.Value<uint>());
    }

    [AvaloniaFact]
    public void Intuos4_Eraser_MovesOnlyTheEraserBar_AndTip_OnlyTheTipBar()
    {
        var (feed, vm, view, w) = Open();
        feed.ToolIn(eraser: true); feed.Pen(1024); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, Bar(view, "PenTab").Value, 3);
        Assert.Equal(0.5, Bar(view, "EraserTab").Value, 2);
        Assert.Equal("0%", vm.LiveTipPercentText);
        Assert.Equal("50%", vm.LiveEraserPercentText);

        feed.Pen(0); feed.ToolIn(eraser: false); feed.Pen(1536); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.75, Bar(view, "PenTab").Value, 2);
        Assert.Equal(0, Bar(view, "EraserTab").Value, 3);
        Assert.Equal("75%", vm.LiveTipPercentText);
        Assert.Equal("0%", vm.LiveEraserPercentText);
        w.Close();
    }

    [AvaloniaFact]
    public void GenericEraserBit_MovesOnlyItsOwnBar()
    {
        var (feed, vm, view, w) = Open();
        feed.Generic(1023.5, eraser: true); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, Bar(view, "PenTab").Value, 3);
        Assert.Equal(0.5, Bar(view, "EraserTab").Value, 2);
        feed.Generic(2047, eraser: false); Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Bar(view, "PenTab").Value, 2);
        Assert.Equal(0, Bar(view, "EraserTab").Value, 3);
        w.Close();
    }
}
