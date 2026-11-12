# OpenTabletDriver patches

`external/OpenTabletDriver` is OpenTabletDriver v0.6.8-rc1 (upstream commit 126f482, LGPL-3.0), vendored with the patches below already applied, so a plain clone builds the same app.

- `otd-macos-f13-f15.patch`: adds F13, F14 and F15 to the macOS virtual keyboard (needed for Photoshop's Option+F13 / Option+F14 rotation).
- `otd-daemon-quiet-closed-rpc.patch`: the daemon logs a client closing its JSON-RPC connection ("Cannot access a disposed object ... StreamJsonRpc.JsonRpc") at debug level instead of as an error.

## otd-pth651-wireless-receiver.patch

Discussion #31 (best attempt, untested): adds the Wacom Wireless Receiver (ACK-40401, USB 056a:0084, product ID 132) to `Wacom/PTH-651.json`, re-using the USB parsers. Source: Linux `drivers/hid/wacom_wac.c` (`wacom_features_0x84`, "Wacom Wireless Receiver"). Apply from inside `external/OpenTabletDriver` with `git apply`.

- `otd-macos-double-click.patch`: Version 25 tip double-click. Hover between taps no longer breaks the click count; only a drag while held does. Adds Tip Double Click Distance (default 40 px), `IMouseDoubleClickHandler`, and a Double Click binding that posts left down/up click-count 1 then 2.
- `otd-macos-edge-activation.patch` (apply after the double-click patch): Version 25. In absolute mode the macOS pointer is kept 1/64 px inside the display it is on, so the Dock and hot corners trigger at the edge (OTD #4726). Before a left mouse-down, the app that owns the window under the pointer is activated (macOS 27 raises the window but doesn't activate it; desktop → Finder). Fails open.
- Issue #73 (on top of the edge-activation patch, in the vendored files): `MacOSWindowPick.Pick` skips a Window Server window that covers the display under the pen (seen on macOS 27.2: every tap picked Window Server, so no app was activated); smaller Window Server windows still stop the search. `MacOSAppActivator` logs once what a picked non-app window is (owner, layer, alpha, bounds).
