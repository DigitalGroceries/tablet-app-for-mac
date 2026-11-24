# Tablet App for Mac

> **UNDER CONSTRUCTION**

A free tablet app for Mac: settings for your Wacom drawing tablet (tested by users with Intuos3, Intuos4, Intuos5, Intuos Pro, Intuos and Bamboo tablets) on macOS with Apple silicon. A free alternative to the Wacom driver for Mac, built on OpenTabletDriver.

## [⬇ Download for Mac](https://github.com/DigitalGroceries/tablet-app-for-mac/releases/latest/download/Tablet-App-for-Mac.dmg)

1. Open the downloaded file, **Tablet-App-for-Mac.dmg**.
2. Drag **Tablet App for Mac** onto the **Applications** folder.
3. Open the app from Applications. If your Mac warns you, right-click the app and choose **Open**. If there is no Open button, go to **System Settings › Privacy & Security**, scroll down and click **Open Anyway**.

When the app asks, allow **Input Monitoring** and **Device Control and Data Access** (called **Accessibility** before macOS 27) so it can read your pen.

Help and the user manual: https://digitalgroceries.github.io/tablet-app-for-mac/

**New in Version 30:** Off / On switches for the radial menu and the colour palette, a window to choose the palette's own colours, keys that do nothing yet are easy to find and set up, one button design everywhere, and Hindi and Swahili. See [What is new](https://digitalgroceries.github.io/tablet-app-for-mac/releases/).

### Tell us what works and what doesn't

We read everything, and your reports decide what we fix next.

- **Talk about the latest version:** [the latest version's discussion](https://digitalgroceries.github.io/tablet-app-for-mac/talk/)
- **Report a problem or ask for a feature:** [open an issue](https://github.com/DigitalGroceries/tablet-app-for-mac/issues/new/choose)
- **No GitHub account?** Use **Feedback** on the app's Home page, or [send feedback and screenshots here](https://www.dropbox.com/request/r2fqvmi7n4vozy26zrbf) (no account needed; the page asks for a name and an email, any name is fine)

Quit the Wacom driver first.

## Tablets users have tested

People using the app have told us it works with these tablets:

| Tablet | Model | Tested with |
|---|---|---|
| Intuos3 6x11 | PTZ-631W | Version 29 |
| Intuos4 Small | PTK-440 | Every version (our own tablet) |
| Intuos4 Medium | PTK-640 | Version 26, including the ExpressKey displays |
| Intuos5 touch Small | PTH-450 | Version 25 |
| Intuos5 touch Medium | PTH-650 | Version 29 |
| Intuos5 touch Large | PTH-850 | Version 28 |
| Intuos Pro Medium (2013) | PTH-651 | Version 21, including the Wireless Kit |
| Intuos S Bluetooth | CTL-4100WL | Version 28, over USB and Bluetooth |
| Bamboo Fun Small (Pen & Touch) | CTH-461 | Version 28, including finger touch |

Is your tablet working, or not yet? Tell us on [the Tablets page](https://github.com/DigitalGroceries/tablet-app-for-mac/discussions/categories/tablets), so other artists know too. The full list of tablets the app knows is on [the website](https://digitalgroceries.github.io/tablet-app-for-mac/tablets/).

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
