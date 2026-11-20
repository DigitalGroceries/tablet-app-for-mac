# App icon

Since 0.91.0-R the icon is icon-Z3 (Tablet App for Mac). The note below about the Home page drawing appicon-bw.svg inline is out of date: no view draws it now.

The vector sources are the source of truth; the raster assets are generated from them.

| File | Role |
|------|------|
| `appicon.svg` | Full-color mark — **the app icon** source. |
| `appicon-bw.svg` | Single-color (B&W) silhouette mark. |
| `appicon.png` | 1024×1024 raster (R17, icon-v2 single vanishing point), rendered from `appicon.svg`. Used as `MainWindow.Icon`. |
| `appicon.ico` | Multi-size (16/24/32/48/64/128/256), rendered from `appicon.svg`. Used as the exe `ApplicationIcon` (taskbar / Explorer). |

The **Home page** (About column) draws the `appicon-bw.svg` geometry inline as a theme-tinted
vector (`AboutView.axaml`), so the mono mark reads correctly on both light and dark skins — no
raster needed there. If you change `appicon-bw.svg`, re-copy its path figures into that `Viewbox`.

## The macOS icon file (AppIcon.icns)

Rebuild it from `macos/AppIcon.iconset` with `python3 tools/make_icns.py OpenTabletArtist/Assets/macos/AppIcon.iconset OpenTabletArtist/Assets/macos/AppIcon.icns`.
It writes the slots the way Apple's `iconutil` does: 16 px and 32 px as `ic04`/`ic05` ARGB, everything larger as PNG.
Never put PNG pictures in `icp4`/`icp5`/`icp6` (what icnsutil wrote before Version 29): macOS can't read them there and
Finder's list view and Force Quit show coloured noise (#157). `IcnsSmallSizesTests` fails if that comes back.

## Regenerating the rasters

There are no SVG CLI tools in this environment, so the rasters are rendered with a
throwaway [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) + SkiaSharp console app
(`dotnet run` a tiny program that loads the SVG's `SKPicture` and draws it into an
`SKBitmap` at each size). Steps:

1. `appicon.svg` → `appicon.png` at 256.
2. `appicon.svg` → `appicon.ico` at 16,24,32,48,64,128,256 (PNG-compressed entries,
   Vista+ ICO container).
3. `appicon-bw.svg` → `appicon-home.png` at 512.

(Any SVG→PNG renderer — Inkscape, rsvg-convert, ImageMagick — works equally; the ICO
just packs the per-size PNGs into a standard icon container.)
