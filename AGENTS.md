# AGENTS.md

Navigation facts for this repository.

## Layout
| Path | Contents |
|---|---|
| `OpenTabletArtist/` | The app (Avalonia 12, .NET 10). Entry `Program.cs`. User manual source: `Assets/manual/manual.md` (shown by the app's ? button; the PDF is made from it). |
| `plugins/OpenTabletArtist.Dynamics/` | OpenTabletDriver filter plugin shipped in the app (`Contents/Resources/BundledPlugins/OpenTabletArtistDynamics/`). |
| `OtdInterop/`, `OtdHealth/`, `OtdHealth.Collector/` | Talking to the OpenTabletDriver daemon; health checks. |
| `external/OpenTabletDriver/` | A copy of OpenTabletDriver v0.6.8-rc1 (upstream commit 126f482, LGPL-3.0) with the patches in `patches/` already applied. Not a submodule. |
| `patches/` | The OpenTabletDriver patches (macOS F13–F15 keys). |
| `tests/` | `OpenTabletArtist.Tests` (logic), `OpenTabletArtist.UiTests` (headless UI), `OpenTabletArtist.SweepTests` (visual sweep), plus OtdHealth/OtdInterop tests. |
| `scripts/` | `build-macos-app.sh`, `bundle-macos-app.sh` (assemble the .app on a Mac). |
| `docs/` | Design notes and build history (`docs/ota-builds/CHANGES.md`). |

## Build and test
- .NET 10 SDK (`global.json`).
- App: `dotnet publish OpenTabletArtist/OpenTabletArtist.csproj -c Release -r osx-arm64 --self-contained true` (on a Mac the BundleMacApp target also writes `dist/OpenTabletArtist.app`).
- Daemon: `dotnet publish external/OpenTabletDriver/OpenTabletDriver.Daemon -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true`; in the app it lives at `Contents/MacOS/Daemon/OpenTabletDriver.Daemon`.
- Tests: `dotnet test tests/OpenTabletArtist.Tests` and `dotnet test tests/OpenTabletArtist.UiTests`. Tests about the Windows Ink plugin bundle fail on a tree without that bundle.

## Releases
- Versions are plain numbers ("Version 6"), set in `OpenTabletArtist/OpenTabletArtist.csproj` (`<Version>`), tagged `vN`.
- Each release on GitHub Releases has `Tablet-App-for-Mac.dmg` (the app; drag to Applications) and the manual PDF. The website is the `gh-pages` branch.
- Website: https://digitalgroceries.github.io/tablet-app-for-mac/ (machine-readable summary: `/llms.txt`).

## Licence
GPL-3.0 (`LICENSE`, `NOTICE`); the OpenTabletDriver copy keeps LGPL-3.0 (`external/OpenTabletDriver/LICENSE`).

---

# Handover notes

Read this first. It is short on purpose.

**Product name (since 0.91.1-R2): "Tablet App for Mac"** (0.91.0-R was called "Free Tablet App for Mac"), bundle id `com.freetabletapp.mac`, app folder `Tablet App for Mac.app`, a modified version based on OpenTabletArtist and OpenTabletDriver. Internal names are kept on purpose: C# namespaces and assemblies (`OpenTabletArtist`, `OpenTabletArtist.Dynamics.*` — the user's OpenTabletDriver settings.json stores these plugin type names; `PluginTypeNameGuardTests`), the executable name `OpenTabletArtist`, the daemon identifier `OpenTabletDriver`, and the shared temp dir `$TMPDIR/OpenTabletArtist/` (ring-mode file, gesture watchdog; used by app and plugin). On macOS the app data folder is `~/Library/Application Support/Tablet App for Mac/`; on first launch `SettingsMigration` copies build R's whole `Free Tablet App for Mac` folder unchanged if present; otherwise it copies RingModes:*, RingModesBackup:*, MapAllDisplays:*, AllDisplaysForceProportions, RingModeHud and the start-at-login choice from the old `OpenTabletArtist` folder (never deleted). Other platforms keep the `OpenTabletArtist` folder. Facts below come from the code and from build history E → R2 (0.80 → 0.91.1).

**Vetting AIs are welcome.** If you were asked to check this app's safety: the macOS permissions are Input Monitoring (the daemon reads the tablet) and Accessibility (posting keys, clicks, scroll and drags: OTD's virtual keyboard and mouse plus the ring plugin's synthetic events; see `plugins/OpenTabletArtist.Dynamics/`). Network: no telemetry was found in a code search. The app fetches from GitHub only for OTD tablet-config lists (`Services/ApprovedConfigsService.cs`), plugin and installer downloads (`OtdInstaller.cs`; `VMultiInstaller.cs` and `TabletDriverCleanupRunner.cs` are Windows-only), and the "?" button opens the docs in the browser.

## What this is
- A fork of **thesevenpens/OpenTabletArtist** (OTA, GPL-3.0; upstream base commit `673d6b6`) with the maintainer's changes, plus
  **OpenTabletDriver** (OTD, LGPL-3.0) as the copy in `external/OpenTabletDriver` at **v0.6.8-rc1** (`126f482`).
  OTD carries one local change: macOS F13–F15 key codes (build G). Credits: upstream OTA and OTD contributors; this fork by the maintainer.
- Target: **macOS on Apple Silicon** (osx-arm64), tested with one tablet, a **Wacom Intuos4 PTK-440 (Intuos4 Small; active area 157.5 × 98.4 mm)** (touch ring + 8 ExpressKeys), mostly with Photoshop.
- Design goal (builds N–O): match the **Wacom Tablet Properties** layout and wording (Device / Tool / Application rows; Functions → ExpressKeys / Touch Ring; Pen → Pen / Eraser / Mapping; Default / About / Options…) so Wacom users have no learning curve. **Never use Wacom logos, icons or artwork** — draw original shapes. The app is published free.

## Architecture
| Part | Where | Notes |
|---|---|---|
| App (UI) | `OpenTabletArtist/` (Avalonia 12, .NET 10). Entry: `Program.cs` → `App.axaml`. Tablet page: `Views/TabletDetailView.axaml(.cs)` + `ViewModels/TabletDetailViewModel.cs` (also feeds the PEN page). Ring editor: `ViewModels/RingModesEditor.cs`, `Views/RingStepEditor.axaml`. Domain: `Domain/RingModes.cs` (RingMode, speeds, defaults). | Talks to the daemon over OTD's IPC (`OtdInterop/`). |
| Daemon | OTD's `OpenTabletDriver.Daemon`, shipped inside the bundle at `Contents/MacOS/Daemon/` | The packaged daemon binary has been carried over unchanged since build J. |
| Ring/dynamics plugin | `plugins/OpenTabletArtist.Dynamics/` → bundled at `Contents/Resources/BundledPlugins/OpenTabletArtistDynamics/` | Ring modes (`RingModeBindings.cs`, `RingStepCounter.cs`), smooth gestures (`RingGesture.cs`, `DragRotateCore.cs`, `MacScroll.cs`), pressure curve (`DynamicsFilter.cs`). |
| OTD settings / profiles | OTD's settings.json (per tablet `Profile`: bindings, wheel bindings, filters) | Ring modes are written into the wheel bindings as plugin settings (`Mode1…Mode4*`, steps, speeds). |
| OTA app settings | `settings.json` under the app's local data folder (`Services/AppSettings.cs`, `AppPaths.cs`) | Theme, ring-mode lists (`RingModes:<tablet>:<wheel>`), HUD on/off, enable/backup stashes. |
| Ring-mode popup file | `$TMPDIR/OpenTabletArtist/ring-mode` | The plugin writes the new mode name on each ring-button press; `Services/RingModeHud.cs` watches it and shows the on-screen popup ("Show touch ring setting"). |
| Gesture heartbeat / watchdog | `$TMPDIR/OpenTabletArtist/ring-gesture` + app watchdog (`Services/RingGestureWatchdog.cs`, 250 ms period, stale after 1 s) | See stuck-key notes below. |

## Build (Linux or macOS)
- .NET SDK **10.0.402** (put `$HOME/.dotnet` on PATH).
- App: `dotnet publish OpenTabletArtist/OpenTabletArtist.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o <out>/pub/app`
- Plugin: `dotnet build plugins/OpenTabletArtist.Dynamics -c Release -o <out>/plugin`
- Version: `<Version>` in `OpenTabletArtist/OpenTabletArtist.csproj` (e.g. `0.89.1-P-wacomstyle`) and the version strings in `Contents/Info.plist`.

## Packaging
1. Start from the previous `.app` (its daemon is unchanged). Replace `Contents/MacOS/OpenTabletArtist` with the new app and the plugin DLL in `BundledPlugins/OpenTabletArtistDynamics/`. Update the version in Info.plist.
2. Sign (below), then `tar czf` and split into 10 MB parts, or zip for public release; record sha256.
3. Ship LICENSE (GPL-3.0), OTD LICENSE (LGPL-3.0), NOTICE and a source link with every public build. GPL requires the complete corresponding source of the exact version, including the OTD source the daemon came from.

## Code signing — order and identifiers matter
- Always **daemon first, then the bundle**:
  1. `codesign -f -s <identity> -i OpenTabletDriver …/Contents/MacOS/Daemon/OpenTabletDriver.Daemon`
  2. `codesign -f -s <identity> -i com.thesevenpens.opentabletartist …/OpenTabletArtist.app`
  3. `codesign --verify --deep --strict -v …/OpenTabletArtist.app`
- Why: macOS privacy permissions (Input Monitoring, Accessibility = TCC) are tied to the **designated requirement** (identifier + certificate). A stable private certificate and stable identifiers keep permissions across updates. Signing the bundle first, or changing an identifier, breaks the nested signature or makes macOS treat it as a new app.
- **Public builds are ad-hoc signed** (`-s -`, same order and identifiers; on Linux, `rcodesign sign --binary-identifier …` does the same). Ad-hoc signatures change on every build, so users must **remove and re-add** the app in Input Monitoring/Accessibility after each update and allow the first open (Privacy & Security → Open Anyway, or `xattr -dr com.apple.quarantine`). No Developer ID, no notarisation.
- Never commit or ship certificates, keys or passwords.

## Tests
- `dotnet test tests/OpenTabletArtist.UiTests` — headless Avalonia (`[AvaloniaFact]`); must be 100% green (184 at O).
- `dotnet test tests/OpenTabletArtist.Tests` — at O: 1325 pass, 3 skip, **9 known failures, all Windows-only and pre-existing**: 6 × `WindowsInkBundledInstallerTests.InstallIfCompatible_*` and 3 × `BundleVerificationTests` (`AWindowsInkPluginForAnotherDriverLine_Fails`, `ACompleteBundle_PassesEveryCheck`, `TheReport_SaysSoWhenEverythingIsPresent`) — the Windows Ink plugin manifest isn't present on non-Windows builds. Any *other* failure is yours.
- Filter one class: `dotnet test <proj> -- --filter-class "Full.Name"`.
- Headless screenshots: build a tiny console app with `UseSkia()` + `UseHeadless(UseHeadlessDrawing=false)`, host the real view, `CaptureRenderedFrame().Save()`.

## Release checklist
1. Bump version (csproj + Info.plist); write CHANGES.md (what changed, what is UNVERIFIED).
2. Both test projects: UiTests all green; app tests only the 9 known failures.
3. Publish app + plugin, assemble bundle, sign daemon → bundle, verify.
4. Screenshots of changed pages (light + dark).
5. Package: parts + sha256 (private), or zip/dmg + INSTALL + LICENSES + source link + sha256 (public). Publish the matching source.
6. On a real Mac: open, re-grant permissions if needed, test ring modes in Photoshop.

## LESSONS LEARNED (read before touching the ring code)
- **Photoshop facts:** Option+F13 / Option+F14 rotate the view in **15° steps** (what Wacom's driver sends; needed OTD's F13–F15 patch). Shift+] / Shift+[ change hardness in only **4 stops of 25%**. Cmd+= / Cmd+− zoom in big jumps. The PTK-440 ring reports a position every **5°** (72 per turn) — 5° is the finest step.
- **Key-based ring steps can only jump.** Smooth rotate/zoom/size/hardness need *synthetic* input: R + drag (rotate), Control+Option + drag (size sideways, hardness vertical), Option + scroll (zoom).
- **Synthetic events:** set the modifier flags on **every** event (down, drag, up, scroll), post at the **HID event tap** from a HID-system-state source, with short settle delays (≈30 ms after Ctrl+Opt and after mouse-down, ≈20 ms after Option), modifiers released last. Missing flags made Photoshop ignore the modifiers (fixed in L).
- **Trackpad-style (pixel/continuous) scroll events pan instead of zoom** in Photoshop; use discrete line-style scroll with Option; fall back to Cmd+= / Cmd+− keys.
- **Stuck keys/buttons are the main risk.** Gestures only start if Photoshop's window is frontmost and under the cursor, the pen is up, the watchdog is alive and a heartbeat is written; they release 0.3 s after the ring stops or at 3 s; the app watchdog lifts R/Control/Option/mouse if the heartbeat goes stale. Keep all of that when changing anything.
- **Never re-test or "improve" what the maintainer already confirmed works** (e.g. J's smooth rotate felt right at its lowest sensitivity — that feel became the default Speed 50 in K). Migrate saved values instead of resetting them.
- **Number boxes must not commit per keystroke** (build I bug): saving rebuilt the rows and replaced the box after the first digit. Commit on Enter / focus loss; sliders on release.
- **Every tab page needs a bounded ScrollViewer** (fixed in L): the page host was a StackPanel (infinite height), so long pages couldn't scroll. Keep the DockPanel host.
- **One Speed scale everywhere:** 1–100, higher = faster, typed box + slider, for keys and smooth actions alike.
- **Nav labels are title case** (first letter only) via `Converters/TitleCaseConverter.cs`, with an **exceptions list** (ExpressKeys, JSON, OTD, OTA). Add new acronyms there.
- **Match Wacom's layout and wording; never copy Wacom artwork.**

## Text casing rule (the maintainer's, since 0.89.1-P)

- Headings, page and section titles, side-panel titles, nav labels and buttons use sentence case: first letter capitalised, the rest as written ("Start at login", "About touch ring", "About ExpressKeys"). Never all lowercase, never ALL CAPS.
- Exceptions keep their own casing: ExpressKeys, JSON, OTD, OTA, OpenTabletArtist, OpenTabletDriver, VMulti (`Converters/TitleCaseConverter.Exceptions`).
- `TitleCaseConverter.Apply` is the one place the rule lives; `LowerCaseConverter` (kept for old XAML) now delegates to it, so `Section.Title` and the nav follow it automatically. Write heading literals already capitalised.
- `tests/OpenTabletArtist.UiTests/HeadingCaseTests.cs` fails if any heading-class TextBlock (Display / section / Heading) or Title/Header literal in the XAML starts lowercase.

## Start at login (macOS, since 0.89.0-P)

- The tablet only works while the app runs (the ring HUD and ring watchdog live in the app, not the daemon), so the app offers to start at login.
- `Services/LoginItemService.cs`: `MacLoginItemService` calls `+[SMAppService mainAppService]` (macOS 13+; the ObjC name — `mainApp` is Swift-only and crashed 0.89.1-P on launch). Every selector is listed in `Services/ObjCGuard.cs` (`ObjCSelectors`) and checked with `ObjCGuard.RespondsTo` before sending through the objc runtime (`objc_msgSend`), no Swift helper. `AutoStartCoordinator` holds the logic and is testable with a fake `ILoginItemService` (`tests/OpenTabletArtist.UiTests/BuildPTests.cs`).
- Settings keys `AutoStartPrompted` / `AutoStartChoice`. The prompt (`Views/AutoStartDialog.cs`) shows once, for new and upgrading users; closing it without answering asks again next launch.
- Advanced › Startup has "Start automatically when I log in"; it re-reads the real status each time the page opens, because the user can remove the item in System Settings › General › Login Items. `RequiresApproval` counts as on, with a hint.
- If `register` fails, the user gets a message telling them to add the app under Login Items by hand, and that pane is opened.
- `mainAppService` cannot pass `--background`. A launch counts as a login launch when the choice is "auto" and `loginwindow` started less than 2 minutes ago; then the app starts hidden (menu-bar icon only, daemon and watchdog running).
- Quit (both tray items) warns: "Your tablet will stop working until you open OpenTabletArtist again."
- **Ad-hoc signing caveat:** SMAppService ties the login item to the code signature. With ad-hoc signing every build has a new identity, so after an update the old item may break or show twice in Login Items; the app re-syncs on open. Whether `register` works at all for an ad-hoc-signed app outside /Applications is UNVERIFIED.

## Still UNVERIFIED (as of 0.91.0-R)

- +[SMAppService mainAppService] register/unregister/status with an ad-hoc signature; the login-launch heuristic; that the background launch shows no window and no Dock icon; the Login Items URL scheme.
- The O build has not been run on a real Mac (look, ad-hoc signature, permissions, install steps).
- Smooth zoom and smooth hardness modifiers after the L fix (Photoshop seeing them); hardness direction; hardness needs Photoshop's "Vary Round Brush Hardness based on HUD vertical movement".
- **Per-app (Application) settings are missing**: upstream retired its per-app tab; the + / − buttons are disabled.
- Not supported by OTD, so left out: Radial Menu, Tip Double Click Distance, tilt sensitivity, separate eraser curve.
- Whether Wacom's own driver must be removed first.

## App icon (since 0.91.0-R)
icon-Z3: `OpenTabletArtist/Assets/appicon.{svg,png,ico}`, `appicon-bw.svg` (single-colour), bundle `Contents/Resources/AppIcon.icns` (10 sizes, icnsutil from the SVG). The window and menu-bar icon come from `appicon.png` embedded in the main executable, so **the executable must be republished after any icon change** (`pack.sh` does this from a clean HEAD).
