namespace OpenTabletDriver.Plugin.Tablet
{
    /// <summary>
    /// Tablet App for Mac (#120): one input report that carries several samples. A Wacom Intuos BT tablet
    /// (CTL-4100WL, CTL-6100WL) sends up to 4 pen frames in each Bluetooth report. The device reader hands each
    /// frame on as its own report, oldest first, like the Linux Wacom driver does (wacom_intuos_pro2_bt_pen).
    /// </summary>
    public interface IMultiFrameReport : IDeviceReport
    {
        /// <summary>The reports in this input report, oldest first.</summary>
        IDeviceReport[] Frames { get; }
    }

    /// <summary>Tablet App for Mac (#120): the plain <see cref="IMultiFrameReport"/> (a struct, like every report: CodeValidationTest).</summary>
    public struct MultiFrameReport : IMultiFrameReport
    {
        public MultiFrameReport(byte[] report, IDeviceReport[] frames)
        {
            Raw = report;
            Frames = frames;
        }

        public byte[] Raw { set; get; }
        public IDeviceReport[] Frames { get; }
    }
}
