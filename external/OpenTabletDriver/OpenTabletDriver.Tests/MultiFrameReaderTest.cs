using System;
using System.Collections.Generic;
using NSubstitute;
using OpenTabletDriver.Configurations.Parsers.Wacom.IntuosBluetooth;
using OpenTabletDriver.Devices;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests
{
    /// <summary>
    /// Tablet App for Mac #120 (GitHub issue by jbrannigan, CTL-4100WL): a Bluetooth report with several pen
    /// frames reaches the rest of the driver as one report per frame, oldest first, raw copies included.
    /// </summary>
    public class MultiFrameReaderTest
    {
        private sealed class QueueStream : IDeviceEndpointStream
        {
            private readonly Queue<byte[]> _reports;
            public QueueStream(params byte[][] reports) => _reports = new Queue<byte[]>(reports);
            public byte[] Read() => _reports.Count > 0 ? _reports.Dequeue() : throw new ObjectDisposedException("test stream");
            public void Write(byte[] buffer) { }
            public void GetFeature(byte[] buffer) { }
            public void SetFeature(byte[] buffer) { }
            public void Dispose() { }
        }

        private sealed class Reader : DeviceReader<IDeviceReport>
        {
            public Reader(IReportParser<IDeviceReport> parser) : base(Substitute.For<IDeviceEndpoint>(), parser) { }
            public void Run(IDeviceEndpointStream stream) { ReportStream = stream; Main(); }
        }

        private static byte[] Bt(params (byte flags, ushort x)[] frames)
        {
            var r = new byte[361];
            r[0] = 0x81;
            for (int i = 0; i < frames.Length; i++)
            {
                r[1 + i * 8] = frames[i].flags;
                r[2 + i * 8] = (byte)frames[i].x;
                r[3 + i * 8] = (byte)(frames[i].x >> 8);
            }
            return r;
        }

        [Fact]
        public void Every_Frame_Goes_On_As_Its_Own_Report_Oldest_First()
        {
            var reader = new Reader(new IntuosBluetoothReportParser()) { RawClone = true };
            var reports = new List<IDeviceReport>();
            var raws = new List<IDeviceReport>();
            reader.Report += (_, r) => reports.Add(r);
            reader.RawReport += (_, r) => raws.Add(r);

            reader.Run(new QueueStream(
                Bt((0xE0, 1), (0xE0, 2), (0xE0, 3), (0xE0, 4)),
                Bt((0xE0, 5)),
                Bt((0xE0, 6), (0x80, 0))));

            Assert.Equal(7, reports.Count);
            Assert.Equal(new[] { 1f, 2f, 3f, 4f, 5f, 6f }, reports.GetRange(0, 6).ConvertAll(r => ((ITabletReport)r).Position.X));
            Assert.IsType<OutOfRangeReport>(reports[6]);
            Assert.DoesNotContain(reports, r => r is IMultiFrameReport);
            Assert.Equal(7, raws.Count);
        }
    }
}
