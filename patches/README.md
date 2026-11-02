# OpenTabletDriver patches

`external/OpenTabletDriver` is OpenTabletDriver v0.6.8-rc1 (upstream commit 126f482, LGPL-3.0), vendored with the patches below already applied, so a plain clone builds the same app.

- `otd-macos-f13-f15.patch`: adds F13, F14 and F15 to the macOS virtual keyboard (needed for Photoshop's Option+F13 / Option+F14 rotation).
- `otd-daemon-quiet-closed-rpc.patch`: the daemon logs a client closing its JSON-RPC connection ("Cannot access a disposed object ... StreamJsonRpc.JsonRpc") at debug level instead of as an error.

## otd-pth651-wireless-receiver.patch

Discussion #31 (best attempt, untested): adds the Wacom Wireless Receiver (ACK-40401, USB 056a:0084, product ID 132) to `Wacom/PTH-651.json`, re-using the USB parsers. Source: Linux `drivers/hid/wacom_wac.c` (`wacom_features_0x84`, "Wacom Wireless Receiver"). Apply from inside `external/OpenTabletDriver` with `git apply`.
