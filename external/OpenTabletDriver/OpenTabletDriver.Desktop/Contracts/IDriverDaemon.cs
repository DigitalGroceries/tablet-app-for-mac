using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Diagnostics;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Desktop.RPC;
using OpenTabletDriver.Desktop.Updater;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Contracts
{
    public interface IDriverDaemon
    {
        event EventHandler<LogMessage>? Message;
        event EventHandler<DebugReportData>? DeviceReport;
        event EventHandler<IEnumerable<TabletReference>>? TabletsChanged;
        event EventHandler? Resynchronize;

        Task WriteMessage(LogMessage message);

        Task LoadPlugins();
        Task<bool> InstallPlugin(string filePath);
        Task<bool> UninstallPlugin(string friendlyName);
        Task<bool> DownloadPlugin(PluginMetadata metadata);

        Task<IEnumerable<SerializedDeviceEndpoint>> GetDevices();

        Task<IEnumerable<TabletReference>> GetTablets();
        Task<IEnumerable<TabletReference>> DetectTablets();

        Task SetSettings(Settings settings);
        Task<Settings> GetSettings();
        Task ResetSettings();

        Task<AppInfo> GetApplicationInfo();

        Task SetTabletDebug(bool isEnabled);
        Task<string> RequestDeviceString(int vendorID, int productID, int index);
        Task SendFeatureInit(int vendorID, int productID, string strInterface, byte[] initData);
        /// <summary>Tablet App for Mac: like SendFeatureInit, but to EVERY matching device (two identical
        /// tablets share vid/pid/interface). Returns how many accepted it.</summary>
        Task<int> SendFeatureInitAll(int vendorID, int productID, string strInterface, byte[] initData);
        /// <summary>Tablet App for Mac (#7, Intuos4 OLED key labels): several feature reports in order through ONE
        /// open stream per device, to every matching device. Returns how many devices took all of them.</summary>
        Task<int> SendFeatureSequence(int vendorID, int productID, string strInterface, byte[][] reports);
        Task SendOutputInit(int vendorID, int productID, string strInterface, byte[] initData);

        Task<IEnumerable<LogMessage>> GetCurrentLog();
        Task<DiagnosticInfo> GetDiagnosticInfo();

        Task<SerializedUpdateInfo?> CheckForUpdates();
        Task InstallUpdate();

        Task ForceResynchronize();
    }
}
