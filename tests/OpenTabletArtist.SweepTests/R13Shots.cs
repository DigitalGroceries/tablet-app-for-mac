using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;
using static OpenTabletArtist.SweepTests.Sweep;

namespace OpenTabletArtist.SweepTests;

/// <summary>R13 screenshots: Tablet › Pen / Eraser with a fake live pressure feed (tip, then eraser).</summary>
public class R13Shots
{
    private sealed class Feed : IDaemonDebugSession
    {
        public event Action<JObject>? DeviceReport;
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
        public void Raw(JObject data) => DeviceReport?.Invoke(new JObject
        {
            ["Tablet"] = new JObject { ["Properties"] = new JObject { ["Name"] = "Wacom PTK-440", ["Specifications"] = new JObject
            {
                ["Digitizer"] = new JObject { ["Width"] = 152.0, ["Height"] = 95.0, ["MaxX"] = 15200, ["MaxY"] = 9500 },
                ["Pen"] = new JObject { ["MaxPressure"] = 2047 },
            } } },
            ["Data"] = data,
        });
        public void Pen(double p) => Raw(new JObject { ["Position"] = new JObject { ["X"] = 7600, ["Y"] = 4750 }, ["Pressure"] = p });
    }

    [AvaloniaFact]
    public void LivePressureScreenshots()
    {
        _ = new ThemeViewModel();
        var feed = new Feed();
        var profile = new Profile { Tablet = "Wacom PTK-440", OutputMode = new PluginSettingStore(typeof(OpenTabletDriver.Desktop.Output.AbsoluteMode)) };
        var settings = new OpenTabletDriver.Desktop.Settings { Profiles = new ProfileCollection { profile } };
        var vm = new TabletDetailViewModel(profile, settings, penInput: feed);
        var view = new TabletDetailView { DataContext = vm };
        var w = Host(view, 1000, 1900);
        void Tab(string t) { view.FindControl<RadioButton>(t)!.IsChecked = true; Pump(); w.UpdateLayout(); Pump(); }
        // R15: each bar follows only its own end. Tip in use: tip bar 65 %, eraser bar idle; and vice versa.
        feed.Raw(new JObject { ["Tool"] = 0 }); feed.Pen(1330); Pump();
        Tab("PenTab"); Shot(w, "r15-tip-in-use-pen-tab-65pct");
        Tab("EraserTab"); Shot(w, "r15-tip-in-use-eraser-tab-idle");
        feed.Pen(0); feed.Raw(new JObject { ["Tool"] = 1 }); feed.Pen(820); Pump();
        Tab("EraserTab"); Shot(w, "r15-eraser-in-use-eraser-tab-40pct");
        Tab("PenTab"); Shot(w, "r15-eraser-in-use-pen-tab-idle");
        w.Close();
    }
}
