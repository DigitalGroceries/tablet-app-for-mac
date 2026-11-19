// Added 2026 by the Tablet App for Mac team (DigitalGroceries), issue #118: lets the driver tests check
// MacDeviceKey (internal) without changing HidSharp's public API.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("OpenTabletDriver.Tests")]
