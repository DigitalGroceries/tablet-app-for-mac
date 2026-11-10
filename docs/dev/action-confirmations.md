# Action confirmation overlay ("Settings saved")

Every button that **does** something shows the same big on-screen message as the touch-ring mode popup
("Zoom", "Brush Size", …), so the user knows the action really ran. Asked by Zak on 10 Nov 2026.

## How
- `Services/ActionConfirmation.cs`: `ActionConfirmation.Show("confirm.settings_saved", "Settings saved")`.
  Safe from any thread (the display hops to the UI thread), never takes focus, does nothing before the popup exists.
- `RingModeHud.ShowConfirmation(header, message)` is the **same window** as the ring popup: same size (a third
  of the frontmost window, ≥ 360 pt), position (centre of the frontmost window / cursor display), 40 % opacity,
  2.5 s then 0.3 s fade, click-through, over full-screen apps. Header line = the app name (the ring popup shows
  "TOUCH RING"); a ✓ sits where the ring's mode dots are. It ignores the "Show touch ring setting" switch.
- Texts are `confirm.*` keys in `Assets/i18n/*.json` (all 8 locales), past tense, short.

## Rules
1. Call it **only after the action succeeded** (the driver took the change, the file was written…). On failure keep the
   existing error UI and show nothing. If a confirm dialog comes first, call it after the user confirmed and it finished.
2. Only for buttons whose result is not otherwise obvious. Not for navigation, opening a menu/dialog/picker/folder/URL,
   toggles and switches (their state is visible), Cancel/Close/Done, or edits that show their result in place.
3. Add the key to all 8 locales (a test checks it) and use the same English text as the in-code fallback.
4. Tests: `tests/OpenTabletArtist.Tests/ActionConfirmationTests.cs` (`ActionConfirmation.Recorder` sees the keys);
   headless picture of the overlay next to the ring popup: `tests/OpenTabletArtist.SweepTests/ConfirmationOverlayShots.cs`.
