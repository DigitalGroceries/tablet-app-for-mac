# Intuos4 (PTK-440) ExpressKey OLEDs and ring LEDs — findings (30 Oct 2026)

Source: MockTab `Notes/Wacom-LED-and-OLED-Control.md` (Cyzor, GPL-3) and the Linux `wacom_sys.c` it cites.

## What the tablet needs
- **Ring LED (Intuos4):** HID feature report `0x20`, 9 bytes: `[1]` = luminance (bits 0–4) | LED 0–3 (bits 5–7), `[2]` = high luminance. Source: Linux kernel; MockTab marks it "kernel", not tested on a real Intuos4.
- **OLED key labels:** eight 64×32 displays. `0x21 [0x21,0x01]` begin, then per key four `0x23 [0x23, key&7, chunk, 256 bytes]` chunks (1024 bytes, 4-bit grey, kernel row-interleaved order), then `0x21 [0x21,0x00]` end. USB only; Bluetooth uses another encoding. Also "kernel; no Intuos4 on hand" in MockTab.

## Can our OpenTabletDriver send them on macOS?
- Yes in principle: the daemon has an RPC `SendFeatureInit(vid, pid, interface, bytes)` (`DriverDaemon.cs`), which opens the device through HidSharp and calls SetFeature (IOHIDDeviceSetReport on macOS). OTD already sends the PTK-440's own feature init (`02 02`) this way at start.
- Open questions before shipping: (1) the RPC finds the device by the `USB_INTERFACE_NUMBER` attribute, which may not be filled in on macOS; (2) it re-opens the HID device while the digitizer stream is open — needs a test that pen input is not interrupted; (3) the report lengths must match the PTK-440's HID descriptor (unknown here; read with `ioreg`/`hidutil` on Zak's Mac); (4) the OLED image order needs the kernel encoder ported and checked against real hardware.

## Decision
Not implemented in this update: nothing here has been tested on an Intuos4, and a wrong feature report could leave the ring/keys in an odd state until the tablet is replugged (no permanent harm is known). Proposed next step: a Dev-only "Test ring LED" button that sends one `0x20` report for LED 1–4, tried by Zak on his PTK-440; if that works, OLED labels (text rendered to 64×32 grey) follow as a BETA.

## Update (30 Oct 2026, evening): status LED brightness implemented
Zak's PTK-440 (Intuos4 Small) has one status LED and no OLEDs. The Linux source (`wacom_led_control`, Intuos4 branch) shows the real layout, which corrects the "bits 5–7" line above: report `0x20`, 9 bytes, `[1] = select | 0x04`, `[2] = llv` (idle brightness), `[3] = hlv` (brightness while the pen touches / a button is pressed), `[4] = img_lum` (OLED), levels 0–127. The tablet switches between llv and hlv itself, so `Services/Intuos4StatusLed.cs` sends one report per connection (idle 10, touch 127; setting off: 40/40), at most every 500 ms, only on change, and stops after the first failure.
