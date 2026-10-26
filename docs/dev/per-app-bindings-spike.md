# Per-app Application-row bindings (v1, Version 11)

Wacom-style **Application** row: **+** / **−**, tiles beside **All** / **All other**, each listed app has its own ExpressKeys / Touch Ring rotation+button / pen tip+buttons / eraser bindings. Mapping and pressure curves stay on All (v1).

## Store
`AppSettings` key `PerAppTables:{tablet}` — JSON list of `{ BundleId, DisplayName, AppPath, Bindings }`.
All bindings stash: `PerAppAllBindings:{tablet}` (so focus-restore is accurate after a live overlay).

## Runtime
`PerAppBindingRuntime` (macOS): polls frontmost bundle id (~250 ms) via `MacFrontmostApp` (CGWindowList + NSRunningApplication). On match, live-applies that app's `BindingSettings` through `ApplySettingsAsync`. On leave / before Save, restores All from the stash.

## Mid-stroke deferral
`PenBusySampler` enables the daemon DeviceReport stream and feeds `tipDown` from tip contact (`Pressure > 0`, same rule as `PenSample.IsDown` / RingGestureGate tip half). While tip is down, focus/binding switch is deferred (`_pendingBundleId`) until tip-up.

### Residual risks
- **Pen barrel buttons** update `PenBusySampler.PenBusy` but do **not** block apply (only tip contact does). Holding a barrel button alone can still mid-gesture SetSettings.
- **SetTabletDebug sharing**: Pen / Diagnostics / Scribble may call `SetTabletDebug(false)` when leaving their page. The sampler's `Kick()` re-enables when tipDown is polled and samples look stale (&gt;500 ms). A brief window after another consumer disables debug can miss tip-down and allow one mid-stroke apply.
- **Ring modes (Mode1–4)** still edit the All OTD profile only.
- App tile icons are generic (v1).

## Version 11
Ships as official Latest with Applications add-app + V10 i18n.
