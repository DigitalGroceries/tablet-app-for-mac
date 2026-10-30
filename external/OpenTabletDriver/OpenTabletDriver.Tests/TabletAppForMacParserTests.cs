using System.Linq;
using OpenTabletDriver.Configurations.Parsers.Wacom.Bamboo;
using OpenTabletDriver.Configurations.Parsers.Wacom.CintiqV1;
using OpenTabletDriver.Configurations.Parsers.Wacom.Intuos3;
using OpenTabletDriver.Configurations.Parsers.Wacom.IntuosBluetooth;
using OpenTabletDriver.Configurations.Parsers.Wacom.IntuosV1;
using OpenTabletDriver.Configurations.Parsers.Wacom.IntuosV2;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Wheel;
using Xunit;

namespace OpenTabletDriver.Tests
{
    /// <summary>
    /// Tablet App for Mac (Version 16): the Wacom readings added for tablets OpenTabletDriver 0.6.8-rc1
    /// lists as "Has Quirks" or "Missing Features". Byte layouts follow the Linux Wacom driver
    /// (drivers/hid/wacom_wac.c); none of this has been tested on the real tablets yet.
    /// </summary>
    public class TabletAppForMacParserTests
    {
        // ---- Intuos Pro (2017) PTH-460/660/860: ring mirrored and zero moved to the left ----

        private static byte[] IntuosV2Aux(uint? ring)
        {
            var r = new byte[192];
            r[0] = 0x11;
            r[4] = ring is uint p ? (byte)(0x80 | p) : (byte)0;
            return r;
        }

        [Theory]
        [InlineData(0u, 12u)]    // (71 - 0 + 13) % 72
        [InlineData(13u, 71u)]
        [InlineData(14u, 70u)]
        [InlineData(71u, 13u)]
        public void IntuosPro2017_Ring_Is_Mirrored_With_Zero_At_Left(uint raw, uint expected)
        {
            var report = new IntuosV2ReversedRingReportParser().Parse(IntuosV2Aux(raw));
            var wheel = Assert.IsAssignableFrom<IAbsoluteWheelReport>(report);
            Assert.Equal(expected, wheel.AnalogPositions[0]);
        }

        [Fact]
        public void IntuosPro2017_Ring_Turns_The_Other_Way()
        {
            var parser = new IntuosV2ReversedRingReportParser();
            uint a = ((IAbsoluteWheelReport)parser.Parse(IntuosV2Aux(30))).AnalogPositions[0]!.Value;
            uint b = ((IAbsoluteWheelReport)parser.Parse(IntuosV2Aux(31))).AnalogPositions[0]!.Value;
            Assert.Equal(a - 1, b);   // a rising raw reading now falls
        }

        [Fact]
        public void IntuosPro2017_Untouched_Ring_Stays_Null_And_Buttons_Are_Kept()
        {
            var raw = IntuosV2Aux(null);
            raw[1] = 0b0000_0101;
            var report = new IntuosV2ReversedRingReportParser().Parse(raw);
            Assert.Null(((IAbsoluteWheelReport)report).AnalogPositions[0]);
            Assert.Equal(new IntuosV2AuxReport(raw).AuxButtons, ((IAuxReport)report).AuxButtons);
        }

        [Fact]
        public void IntuosPro2017_WacomDriver_Variant_Skips_The_Leading_Byte()
        {
            var raw = new byte[] { 0x00 }.Concat(IntuosV2Aux(0)).ToArray();
            var report = new WacomDriverIntuosV2ReversedRingReportParser().Parse(raw);
            Assert.Equal(12u, ((IAbsoluteWheelReport)report).AnalogPositions[0]);
        }

        [Fact]
        public void IntuosPro2017_Pen_Reports_Are_Untouched()
        {
            var raw = new byte[192];
            raw[0] = 0x10;
            Assert.IsType(new IntuosV2ReportParser().Parse(raw).GetType(), new IntuosV2ReversedRingReportParser().Parse(raw));
        }

        // ---- Touch strips: Intuos3, Cintiq 21UX (DTZ-2100), Cintiq 12WX ----

        private static byte[] PadReport(int strip1, int strip2, int length = 10)
        {
            var r = new byte[length];
            r[0] = 0x0C;
            r[1] = (byte)((strip1 >> 8) & 0x1F);
            r[2] = (byte)(strip1 & 0xFF);
            r[3] = (byte)((strip2 >> 8) & 0x1F);
            r[4] = (byte)(strip2 & 0xFF);
            return r;
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(1, 0u)]          // top
        [InlineData(1 << 6, 6u)]
        [InlineData(1 << 12, 12u)]   // bottom
        [InlineData(0b11 << 4, 5u)]  // two bits lit: the lower one on the strip wins
        public void Strip_Bit_Gives_Position(int bits, uint? expected)
        {
            Assert.Equal(expected, Intuos3TouchStrips.Position(bits));
        }

        [Fact]
        public void Intuos3_Pad_Report_Has_Both_Strips_And_The_Same_Buttons()
        {
            var raw = PadReport(1 << 3, 1 << 9);
            raw[5] = 0b0000_0011;
            var report = new Intuos3ReportParser().Parse(raw);
            var aux = Assert.IsType<Intuos3AuxReport>(report);
            Assert.Equal(new uint?[] { 3, 9 }, aux.AnalogPositions);
            Assert.True(aux.AuxButtons[0] && aux.AuxButtons[1] && !aux.AuxButtons[2]);
        }

        [Fact]
        public void Intuos3_No_Finger_Gives_Null_Strips()
        {
            var aux = Assert.IsType<Intuos3AuxReport>(new Intuos3ReportParser().Parse(PadReport(0, 0)));
            Assert.Equal(new uint?[] { null, null }, aux.AnalogPositions);
        }

        [Fact]
        public void Cintiq12WX_Pad_Report_Has_Strips_And_Ten_Buttons()
        {
            var raw = PadReport(1 << 12, 1);
            raw[6] = 0b0001_0000;
            var aux = Assert.IsType<Intuos3ExtraAuxReport>(new Intuos3ExtraAuxReportParser().Parse(raw));
            Assert.Equal(new uint?[] { 12, 0 }, aux.AnalogPositions);
            Assert.Equal(10, aux.AuxButtons.Length);
            Assert.True(aux.AuxButtons[9]);
        }

        // ---- Cintiq 21UX (DTK-2100) and 22HD: CintiqV1 buttons plus strips ----

        [Fact]
        public void CintiqV1_TouchStrip_Report_Keeps_Buttons_And_Adds_Strips()
        {
            var raw = PadReport(1 << 2, 1 << 7);
            raw[6] = 0b1000_0001;
            raw[9] = 0b0000_0010;
            var report = new CintiqV1TouchStripReportParser().Parse(raw);
            var aux = Assert.IsType<CintiqV1TouchStripAuxReport>(report);
            Assert.Equal(new CintiqV1AuxReport(raw).AuxButtons, aux.AuxButtons);
            Assert.Equal(new uint?[] { 2, 7 }, aux.AnalogPositions);
        }

        [Fact]
        public void CintiqV1_TouchStrip_Parser_Leaves_Other_Reports_To_CintiqV1()
        {
            var raw = new byte[10];
            raw[0] = 0x02;
            raw[1] = 0x80;
            Assert.IsType<OutOfRangeReport>(new CintiqV1TouchStripReportParser().Parse(raw));
        }

        [Theory]
        [InlineData(5, 0, 0)]
        [InlineData(6, 0, 1)]
        [InlineData(6, 7, 8)]
        [InlineData(7, 0, 9)]
        [InlineData(8, 0, 10)]
        [InlineData(8, 7, 17)]
        [InlineData(9, 0, 18)]
        [InlineData(9, 1, 19)]
        public void CintiqV1_Buttons_Follow_The_Linux_Wacom_Layout(int dataByte, int bit, int expectedIndex)
        {
            // wacom_wac.c (WACOM_21UX2 / WACOM_22HD): buttons = data[8] << 10 | (data[7] & 1) << 9
            // | data[6] << 1 | (data[5] & 1); the 22HD's info and wrench keys are data[9] bits 0 and 1.
            var raw = PadReport(0, 0);
            raw[dataByte] = (byte)(1 << bit);
            foreach (var aux in new[]
            {
                new CintiqV1AuxReport(raw).AuxButtons,
                Assert.IsType<CintiqV1TouchStripAuxReport>(new CintiqV1TouchStripReportParser().Parse(raw)).AuxButtons
            })
            {
                Assert.Equal(20, aux.Length);
                Assert.Equal(new[] { expectedIndex }, Enumerable.Range(0, aux.Length).Where(i => aux[i]));
            }
        }

        [Fact]
        public void CintiqV1_Every_Button_Bit_Has_Its_Own_Slot()
        {
            var raw = PadReport(0, 0);
            raw[5] = 0x01;
            raw[6] = 0xFF;
            raw[7] = 0x01;
            raw[8] = 0xFF;
            raw[9] = 0x03;
            Assert.All(new CintiqV1AuxReport(raw).AuxButtons, Assert.True);
        }

        [Fact]
        public void CintiqV1_Short_Pad_Packet_Is_Not_Read_As_Buttons()
        {
            var raw = new byte[] { 0x0C, 0, 0, 0, 0 };
            Assert.IsNotAssignableFrom<IAuxReport>(new CintiqV1ReportParser().Parse(raw));
            Assert.IsNotAssignableFrom<IAuxReport>(new CintiqV1TouchStripReportParser().Parse(raw));
        }

        // ---- Cintiq 13HD: centre button as ExpressKey 9 ----

        [Fact]
        public void Cintiq13HD_Centre_Button_Is_The_Ninth_Key()
        {
            var raw = new byte[10];
            raw[0] = 0x03;
            raw[3] = 0x01;
            var aux = Assert.IsType<Cintiq13HDAuxReport>(new Cintiq13HDReportParser().Parse(raw));
            Assert.Equal(9, aux.AuxButtons.Length);
            Assert.True(aux.AuxButtons[8]);
            Assert.All(aux.AuxButtons.Take(8), Assert.False);
        }

        [Fact]
        public void Cintiq13HD_First_Eight_Keys_Are_Unchanged()
        {
            var raw = new byte[10];
            raw[0] = 0x03;
            raw[4] = 0b1010_0001;
            var aux = Assert.IsType<Cintiq13HDAuxReport>(new Cintiq13HDReportParser().Parse(raw));
            Assert.Equal(new IntuosV1AuxReport(raw).AuxButtons, aux.AuxButtons.Take(8));
            Assert.False(aux.AuxButtons[8]);
        }

        // ---- Bamboo MTE-450 (Linux type WACOM_MO, like the Bamboo Fun CTE-450/650): real reports ----
        // From an owner's recordings attached to OpenTabletDriver PR 4758: byte 8 is 0 when the ring
        // isn't touched, otherwise 0x80 | position (0-71), in aux reports and in pen reports alike.

        [Theory]
        [InlineData("02 00 00 00 00 00 00 00 AD", 45u)]
        [InlineData("02 00 00 00 00 00 00 00 C7", 71u)]
        [InlineData("02 00 00 00 00 00 00 00 80", 0u)]
        [InlineData("02 00 00 00 00 00 00 00 00", null)]
        public void Bamboo_Real_Aux_Reports_Give_The_Ring_Position(string hex, uint? expected)
        {
            var raw = hex.Split(' ').Select(b => System.Convert.ToByte(b, 16)).ToArray();
            var aux = Assert.IsType<BambooAuxReport>(new BambooReportParser().Parse(raw));
            Assert.Equal(expected, aux.AnalogPositions[0]);
        }

        [Theory]
        [InlineData("02 90 72 20 EB 16 00 00 C3", 67u)]
        [InlineData("02 10 7C 1F D3 17 00 00 00", null)]
        public void Bamboo_Real_Pen_Reports_Also_Carry_The_Ring(string hex, uint? expected)
        {
            var raw = hex.Split(' ').Select(b => System.Convert.ToByte(b, 16)).ToArray();
            var pen = Assert.IsType<BambooTabletReport>(new BambooReportParser().Parse(raw));
            Assert.Equal(expected, pen.AnalogPositions[0]);
        }

        // ---- Bamboo Fun CTE-450/650: touch ring ----

        [Theory]
        [InlineData(0x00, null)]
        [InlineData(0x05, 5u)]
        [InlineData(0x85, 5u)]
        [InlineData(0x80, 0u)]
        [InlineData(0x47, 71u)]
        public void BambooFun_Ring_Reads_Byte_8(byte b8, uint? expected)
        {
            var raw = new byte[9];
            raw[0] = 0x02;
            raw[8] = b8;
            var aux = Assert.IsType<BambooAuxReport>(new BambooReportParser().Parse(raw));
            Assert.Equal(expected, aux.AnalogPositions[0]);
        }

        // ---- Intuos BT CTL-4100WL over Bluetooth: report 0x81, 4 pen frames of 8 bytes, keys in byte 44 ----

        private static byte[] IntuosBt(byte keys, params (byte flags, ushort x, ushort y, ushort p, byte d)[] frames)
        {
            var r = new byte[361];
            r[0] = 0x81;
            for (int i = 0; i < frames.Length; i++)
            {
                int o = 1 + i * 8;
                r[o] = frames[i].flags;
                r[o + 1] = (byte)frames[i].x; r[o + 2] = (byte)(frames[i].x >> 8);
                r[o + 3] = (byte)frames[i].y; r[o + 4] = (byte)(frames[i].y >> 8);
                r[o + 5] = (byte)frames[i].p; r[o + 6] = (byte)(frames[i].p >> 8);
                r[o + 7] = frames[i].d;
            }
            r[44] = keys;
            return r;
        }

        [Fact]
        public void IntuosBt_Reads_The_Newest_Valid_Pen_Frame()
        {
            var raw = IntuosBt(0x05, (0xE0, 100, 200, 10, 5), (0xE2, 15000, 9000, 2047, 0), (0x00, 1, 1, 1, 1));
            var report = Assert.IsType<IntuosBluetoothReport>(new IntuosBluetoothReportParser().Parse(raw));
            Assert.Equal(15000f, report.Position.X);
            Assert.Equal(9000f, report.Position.Y);
            Assert.Equal(2047u, report.Pressure);
            Assert.Equal(new[] { true, false }, report.PenButtons);
            Assert.False(report.Eraser);
            Assert.True(report.NearProximity);
            Assert.Equal(new[] { true, false, true, false }, report.AuxButtons);
        }

        [Fact]
        public void IntuosBt_Pen_Away_Gives_Keys_Only_No_Jump_To_Corner()
        {
            var parser = new IntuosBluetoothReportParser();
            var keys = Assert.IsType<IntuosBluetoothAuxReport>(parser.Parse(IntuosBt(0x08)));
            Assert.Equal(new[] { false, false, false, true }, keys.AuxButtons);
        }

        [Fact]
        public void IntuosBt_Pen_Leaving_Releases_Tip_And_Pen_Buttons()
        {
            var parser = new IntuosBluetoothReportParser();
            var down = Assert.IsType<IntuosBluetoothReport>(parser.Parse(IntuosBt(0, (0xE2, 500, 500, 100, 0))));
            Assert.True(down.PenButtons[0]);
            Assert.IsType<OutOfRangeReport>(parser.Parse(IntuosBt(0, (0xE2, 500, 500, 100, 0), (0x80, 0, 0, 0, 0))));
            Assert.IsType<IntuosBluetoothReport>(parser.Parse(IntuosBt(0, (0x80, 0, 0, 0, 0), (0xE0, 600, 600, 0, 3))));
        }

        [Fact]
        public void IntuosBt_Eraser_And_Other_Reports()
        {
            var parser = new IntuosBluetoothReportParser();
            var eraser = Assert.IsType<IntuosBluetoothReport>(parser.Parse(IntuosBt(0, (0xF0, 5, 5, 300, 0))));
            Assert.True(eraser.Eraser);
            var other = IntuosBt(0, (0xE0, 5, 5, 0, 0));
            other[0] = 0x10;
            Assert.IsType<DeviceReport>(parser.Parse(other));
            Assert.IsType<DeviceReport>(parser.Parse(new byte[] { 0x81, 0xE0 }));
        }
    }
}
