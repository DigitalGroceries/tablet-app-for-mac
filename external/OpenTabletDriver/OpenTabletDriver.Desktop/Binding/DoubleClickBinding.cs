using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    /// <summary>
    /// Pen button / ExpressKey action that posts a left-button double-click (click count 1 then 2)
    /// at the current cursor on press. Release does nothing.
    /// </summary>
    [PluginName(PLUGIN_NAME)]
    public class DoubleClickBinding : IStateBinding
    {
        private const string PLUGIN_NAME = "Double Click";

        [Resolved]
        public IMouseDoubleClickHandler? Pointer { set; get; }

        private bool _warned;

        [OnDependencyLoad]
        public void VerifyInitialization()
        {
            if (Pointer == null)
                Log.Write(PLUGIN_NAME,
                    $"{nameof(IMouseDoubleClickHandler)} unavailable. Your selected output mode is incompatible",
                    LogLevel.Error);
        }

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            if (Pointer == null)
            {
                if (!_warned)
                    Log.Write(PLUGIN_NAME, "No double-click output for this output mode.", LogLevel.Warning);
                _warned = true;
                return;
            }
            Pointer.PostDoubleClick();
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
        }

        public override string ToString() => PLUGIN_NAME;
    }
}
