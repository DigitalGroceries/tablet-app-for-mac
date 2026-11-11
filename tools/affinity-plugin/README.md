# Smooth pen strokes — Affinity plugin (#39)

A Photoshop-style filter plugin for Affinity (2 and 3, macOS) that switches macOS point merging
(`[NSEvent setMouseCoalescingEnabled:NO]`) off inside Affinity, as Blender did in 2020, so fast curves
keep every pen point our driver sends. It never touches the picture: the filter step is cancelled.

- `smooth_strokes.c`: the plugin (no headers; links against the stubs below).
- `libSystem.B.tbd`, `libobjc.A.tbd`: link stubs listing only the symbols used, so it builds without the macOS SDK.
- `make_rsrc.py`: writes the PiPL resource (`.rsrc`) Affinity reads to find the plugin. Byte-for-byte the
  same as Apple's Rez output for gmic-affinity's `GmicFilter.rsrc` (kept here as `gmic-reference.rsrc`, MIT).
- `Info.plist`: bundle info (`8BFM` / `8BIM`, as Affinity expects).
- Build: `scripts/build-affinity-plugin.sh` → `OpenTabletArtist/Assets/affinity-plugin/SmoothPenStrokes.plugin`
  (embedded in the app; Settings › System › Affinity copies it to `~/Library/Application Support/Affinity/Plugins`
  and, if used, `Affinity Photo 2/Plugins`).
- Log: `~/Library/Logs/TabletAppForMac-Affinity.log`, one line each time it switches merging off.

Signing: Affinity only enables the plugin when the WHOLE bundle carries a valid signature (ad-hoc is fine:
`codesign --force --deep --sign -`). The Linux build leaves the bundle unsealed, so after any rebuild the
"Affinity plugin check" workflow signs it on a Mac, checks it loads and runs, and commits the signed bundle back
(`[skip ci]`). The PiPL uses gmic-affinity's enable rule (8-bit RGB / Grey); a bare "true" left it greyed out.

Tested 11 Nov 2026 by Zak in Affinity 3.3.3 (PTK-440): the menu item is enabled after the signature fix, and fast
circles come out smooth with Stabiliser on.
