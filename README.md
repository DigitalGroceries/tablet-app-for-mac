# Tablet App for Mac

> **UNDER CONSTRUCTION**

A free tablet app for Mac: settings for your Wacom drawing tablet (tested with a Wacom Intuos4) on macOS with Apple silicon. A free alternative to the Wacom driver for Mac, built on OpenTabletDriver.

## [⬇ Download for Mac](https://github.com/DigitalGroceries/tablet-app-for-mac/releases/latest/download/Tablet-App-for-Mac.dmg)

1. Open the downloaded file, **Tablet-App-for-Mac.dmg**.
2. Drag **Tablet App for Mac** onto the **Applications** folder.
3. Open the app from Applications. If your Mac warns you, right-click the app and choose **Open**. If there is no Open button, go to **System Settings › Privacy & Security**, scroll down and click **Open Anyway**.

When the app asks, allow **Input Monitoring** and **Device Control and Data Access** (called **Accessibility** before macOS 27) so it can read your pen.

Help and the user manual: https://digitalgroceries.github.io/tablet-app-for-mac/

**New in Version 29:** a colour palette and a floating keyboard on the pen, a radial menu that stays open until you close it, a new Pen page where the eraser has its own feel, and more. See [What is new](https://digitalgroceries.github.io/tablet-app-for-mac/releases/).

### Tell us what works and what doesn't

We read everything, and your reports decide what we fix next.

- **Talk about the latest version:** [the latest version's discussion](https://digitalgroceries.github.io/tablet-app-for-mac/talk/)
- **Report a problem or ask for a feature:** [open an issue](https://github.com/DigitalGroceries/tablet-app-for-mac/issues/new/choose)
- **No GitHub account?** Use **Feedback** on the app's Home page, or [send feedback and screenshots here](https://www.dropbox.com/request/r2fqvmi7n4vozy26zrbf) (no account needed; Dropbox asks for a name and an email, any name is fine)

Tested only with macOS 27.0.1 and a Wacom Intuos4 Small (PTK-440); also runs on macOS 27.2.1 (checked without a tablet). Quit the Wacom driver first.

---

## For developers

A modified version based on [OpenTabletArtist](https://github.com/thesevenpens/OpenTabletArtist) (GPL-3.0) and [OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver) (LGPL-3.0). The code keeps the original internal names (`OpenTabletArtist.*`) so existing OpenTabletDriver settings keep working.

### Use it, fork it, improve it

Anyone may use, copy, change and share this code, and publish an improved version on their own page, under the GNU General Public License v3.0 (see [LICENSE](LICENSE) and [NOTICE](NOTICE)). If you share a changed version, share its source under the same licence and say that you changed it. The bundled OpenTabletDriver in `external/OpenTabletDriver` stays under its own LGPL-3.0 licence (`external/OpenTabletDriver/LICENSE`); the patches applied to it are in [patches](patches).

### Build

Needs the .NET 10 SDK. Build on an Apple silicon Mac (or cross-publish from Linux):

```
git clone https://github.com/DigitalGroceries/tablet-app-for-mac.git
cd tablet-app-for-mac
dotnet publish OpenTabletArtist/OpenTabletArtist.csproj -c Release -r osx-arm64 --self-contained true
```

On a Mac this also assembles `dist/OpenTabletArtist.app` (see `scripts/build-macos-app.sh`). The app is unsigned; sign it with your own certificate if you want to keep macOS permissions across builds.

Tests: `dotnet test tests/OpenTabletArtist.Tests` and `dotnet test tests/OpenTabletArtist.UiTests`.

> Working on this code? Read [AGENTS.md](AGENTS.md) first.
