using System;
using System.Collections.Generic;
using System.Threading;
using NSubstitute;
using OpenTabletDriver.Configurations.Parsers.Wacom.IntuosBluetooth;
using OpenTabletDriver.Devices;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Output;
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
            Assert.Equal(3, raws.Count);     // one raw copy per input report, not per frame (Grok's review)
        }

        private sealed class Interpolator : AsyncPositionedPipelineElement<IDeviceReport>
        {
            public override PipelinePosition Position => PipelinePosition.PreTransform;
            protected override void ConsumeState() { }
            protected override void UpdateState() { }
            public bool InRange => PenIsInRange();
        }

        [Fact]
        public void Interpolators_Do_Not_Freeze_Between_Bluetooth_Reports()
        {
            // Grok's review of #120: four frames microseconds apart pulled the report-interval average down to a
            // quarter, so PenIsInRange went false for most of every gap and interpolating filters stopped.
            var f = new Interpolator();
            var pen = new TabletReport { Raw = new byte[1] };
            for (int i = 0; i < 25; i++)
            {
                for (int k = 0; k < 4; k++) f.Consume(pen);
                Thread.Sleep(30);
            }
            for (int k = 0; k < 4; k++) f.Consume(pen);
            Thread.Sleep(20);                // two thirds of the way to the next report
            Assert.True(f.InRange);
        }
    }
}
