using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.CintiqV1
{
    /// <summary>
    /// Pad report of the Cintiq 21UX (DTK-2100) and Cintiq 22HD (DTK-2200, DTH-2200).
    /// <para/>Modified for Tablet App for Mac: the buttons follow the Linux Wacom driver
    /// (wacom_wac.c, wacom_intuos_pad, WACOM_21UX2 / WACOM_22HD): button 1 is data[5] bit 0,
    /// buttons 2–9 are data[6], button 10 is data[7] bit 0, buttons 11–18 are data[8], then the
    /// 22HD's two top keys (data[9] bit 0 = info, bit 1 = wrench). The old layout read a bit 8 of
    /// data[6] and data[8], which a byte does not have, so two slots never fired and the last keys
    /// fell outside the tablets' button counts (OpenTabletDriver issue 5206).
    /// </summary>
    public struct CintiqV1AuxReport : IAuxReport
    {
        public CintiqV1AuxReport(byte[] report)
        {
            Raw = report;

            var leftRadialButton = report[5];
            var leftButtons = report[6];
            var rightRadialButton = report[7];
            var rightButtons = report[8];
            var topButtons = report[9];
            AuxButtons =
            [
                leftRadialButton.IsBitSet(0),
                leftButtons.IsBitSet(0),
                leftButtons.IsBitSet(1),
                leftButtons.IsBitSet(2),
                leftButtons.IsBitSet(3),
                leftButtons.IsBitSet(4),
                leftButtons.IsBitSet(5),
                leftButtons.IsBitSet(6),
                leftButtons.IsBitSet(7),
                rightRadialButton.IsBitSet(0),
                rightButtons.IsBitSet(0),
                rightButtons.IsBitSet(1),
                rightButtons.IsBitSet(2),
                rightButtons.IsBitSet(3),
                rightButtons.IsBitSet(4),
                rightButtons.IsBitSet(5),
                rightButtons.IsBitSet(6),
                rightButtons.IsBitSet(7),
                topButtons.IsBitSet(0), // i-button
                topButtons.IsBitSet(1), // wrench-button
            ];
        }

        public byte[] Raw { set; get; }
        public bool[] AuxButtons { set; get; }
    }
}
