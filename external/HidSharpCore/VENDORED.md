# HidSharpCore (vendored)

- **What:** HidSharpCore 1.3.0, the HID library OpenTabletDriver uses (namespace `HidSharp`, assembly `HidSharpCore`).
- **From:** https://github.com/OpenTabletDriver/HidSharpCore at commit `bca4aee1c349985b0119eed24aba83e2dd63492d`,
  the commit named in the published NuGet package `HidSharpCore 1.3.0` (its .nuspec `repository commit`).
  Only the `HidSharp/` project, `LICENSE` and `NOTICE.txt` are copied. The first commit that adds this folder is the
  upstream code unchanged; our changes are in the commits after it.
- **Licence:** Apache License 2.0 (`LICENSE`, `NOTICE.txt`). Changed files carry a "Modified 2026 by the Tablet App
  for Mac team" note at the top, as the licence asks.
- **Why vendored:** GitHub issue #118 (CTL-4100WL over Bluetooth). On macOS every `IOHIDUserDevice` node (Bluetooth
  tablet, Magic Trackpad, Logitech BLE devices) can have the same I/O Registry path. HidSharpCore keyed devices by that
  path, so only the first device at the path was listed, and opening by path always reached the first one. The fix
  has to be inside HidSharpCore, so the package was replaced by this copy (`ProjectReference` in
  `external/OpenTabletDriver/OpenTabletDriver/OpenTabletDriver.csproj`).

## Our changes (issue #118)
- `Platform/MacOS/MacDeviceKey.cs` (new): a device key made of the registry path and the registry entry ID
  (`IORegistryEntryGetRegistryEntryID`). Equal keys = same entry ID; when the ID can't be read (0), the path alone, as
  before. `ToDevicePath()` gives `path#<ID in hex>`, so OpenTabletDriver (which compares devices by `DevicePath`) sees
  two devices too.
- `MacHidManager.GetDeviceKeys`: builds those keys. Serial devices still open by path, unchanged.
- `MacHidDevice.TryCreate`, `MacHidDevice.GetUsbDeviceId`, `MacHidStream.Init`: find the device by entry ID
  (`IOServiceGetMatchingService` + `IORegistryEntryIDMatching`, the same calls hidapi uses) instead of
  `IORegistryEntryFromPath`, so the right device opens, never the first one with that path.
- `NativeMethods`: the three IOKit calls above.
- `Properties/InternalsVisibleToTests.cs`: lets `OpenTabletDriver.Tests` test `MacDeviceKey`.
- Outside this folder: `OpenTabletDriver.Daemon/MacHidFeature.cs` (Intuos4 OLED labels, #7) opens devices itself, so
  it reads the `#ID` ending through `Devices/HidSharpBackend/MacRegistryPath.cs`.

## Known limits
- A registry entry ID is new every time a device reconnects (or the Mac re-lists it after sleep). Hot-plug handles
  that (the old ID goes, the new one comes); nothing in the app stores a device path.
- Upstream HidSharpCore later moved from `io_string_t` (512 bytes) to `IORegistryEntryCopyPath` for very long paths
  (upstream commit 320bb4c). Not taken here, to keep the change small; take it if a user hits a path over 512 bytes.
- Tests (`OpenTabletDriver.Tests/MacDeviceKeyTest.cs`) cover the key and the path format. Finding two real devices
  with the same path needs two Bluetooth HID devices on a Mac.
