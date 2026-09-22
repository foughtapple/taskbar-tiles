<p align="center"><img src="assets/mark.svg" width="88" alt="Taskbar Tiles icon"></p>
<h1 align="center">Taskbar Tiles</h1>
<p align="center"><strong>Switch to the right window. Launch into the right place.</strong></p>
<p align="center">A mouse-first Windows window switcher, app launcher and monitor/zone picker.</p>
<p align="center">
  <a href="https://github.com/foughtapple/taskbar-tiles/releases/latest"><img src="https://img.shields.io/github/v/release/foughtapple/taskbar-tiles?label=download" alt="Latest GitHub release"></a>
  <a href="https://github.com/foughtapple/taskbar-tiles/actions/workflows/build.yml"><img src="https://github.com/foughtapple/taskbar-tiles/actions/workflows/build.yml/badge.svg" alt="Windows build and helper tests"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT licensed"></a>
</p>

## Download and install

**[Download the latest Windows installer](https://github.com/foughtapple/taskbar-tiles/releases/latest)** and choose **`TaskbarTiles-<version>-Setup.exe`** under **Assets**. The automatically generated “Source code” archives are for developers, not the installer.

Run Setup normally. It installs for your account, adds a Start menu shortcut and an entry in **Windows Settings → Apps**, and offers startup at sign-in. Administrator access, Git and developer tools are not required for an installed release. Requires Windows 10/11 with .NET Framework 4.8 or later; Windows 11 is the primary manual-testing target.

**Upgrading from a previous source-built version? Do not uninstall first.** Setup updates the same `%LOCALAPPDATA%\TaskbarTiles` directory, preserving your settings, favourites, startup choice and X-Mouse command. [Migration and removal instructions](docs/INSTALLATION.md).

> Builds are currently unsigned. SHA-256 checksums detect corruption; they do not independently authenticate the publisher. Review the source and provenance, and do not disable Windows security to install this app.

## Navigate without leaving the switcher

| Area | What it does |
| --- | --- |
| **Open windows** | Live previews, application icons and readable titles. Click to activate that exact window; use its X to request a normal close. |
| **Taskbar apps** | Larger launcher tiles, in taskbar order. Launch through a verified shortcut/app identity where possible, then follow the new or reused window the app produces. |
| **Search** | An integrated icon-and-name search panel for apps, open windows, favourites, common settings/folders and indexed filenames. It does not open Windows Search. |
| **Favourites** | A curated launcher, with groups, import/export, custom icons and optional arguments. |
| **Screens & zones** | Right-click a window or launcher entry, then choose a monitor or zone. “Full screen” maximises on that monitor; it is not F11/exclusive fullscreen. |
| **Settings** | Independent tile, title, icon and app-name sizes; balanced paging; hover tips; live previews; and feature toggles. |

The monitor picker fits the entire arrangement without scrolling. Saved FancyZones layouts are read-only: Taskbar Tiles uses their rectangles without changing PowerToys configuration or zone history. Where a layout cannot be resolved, the UI labels its fallback **Basic zones** rather than pretending to show a saved layout.

With a maximum of **6 columns × 2 rows**, seven windows become **3 above and 4 below**, each row centred. Screen space can reduce the effective capacity at unusually large sizes; the UI explains the limit.

## First run and mouse setup

Launch Taskbar Tiles from Start. It stays in the system tray. **Ctrl+Alt+Space** opens its sticky switcher; Alt+Tab replacement is optional. Escape or clicking outside dismisses it.

For **X-Mouse Button Control**, use the tray command **Copy X-Mouse command**, then bind Mouse Button 4 or 5 to **Run Application** and paste it. A typical command is:

```text
"C:\Users\YOURNAME\AppData\Local\TaskbarTiles\TaskbarTiles.exe" --toggle
```

One press opens; another closes. Bind the default profile only when game-specific profiles should keep their own actions. [Detailed X-Mouse setup](docs/XMOUSE-SETUP.txt).

## Updates and removal

Open **Settings → Updates → Check for updates…**, or use the tray's update command. Review the version/notes, then choose **Download & install**. The updater downloads the named installer from this repository, verifies its release SHA-256 and opens the normal Setup wizard. It never installs silently or polls at startup. Upgrading retains your configuration.

Uninstall via **Windows Settings → Apps → Taskbar Tiles**. Settings, favourites, backups and diagnostics remain in `%LOCALAPPDATA%\TaskbarTiles`; remove that folder manually only after uninstalling and only when you want to erase those files.

## Window and shortcut reliability

The switcher requests activation **before hiding**, verifies the selected HWND becomes foreground, waits for asynchronous restore and uses a short bounded retry when safe. It does not substitute another window simply because both belong to the same process. A disabled owner may legitimately direct focus to its own modal dialog.

**Settings → Shortcut health → Repair shortcuts now** and tray **Repair shortcuts** restore the resident input registration without resetting preferences. Recoverable UI errors no longer leave Alt+Tab disabled indefinitely. Registrations are renewed between gestures and a stopped hook thread is recreated. Windows can silently remove a timed-out low-level hook, so a missed shortcut can still occur before the next idle renewal. Ctrl+Alt+Space and X-Mouse `--toggle` do not depend on that hook. Opening the installed app again also requests repair. Local shortcut diagnostics record failures, not typed text.

Windows still controls foreground permission. Elevated apps, protected windows, exclusive-fullscreen transitions and deliberately always-on-top windows can require special handling. Taskbar Tiles does not bypass foreground locks, attach foreign input queues, inject code, or forcibly change another app's topmost policy. [Troubleshooting](docs/TROUBLESHOOTING.md).

## Touch screen monitor support — experimental

**Touch Return is off by default.** On a specifically enabled and successfully tested touchscreen, use another application temporarily, then return to the previously active window and cursor after the last verified contact ends. Defaults are **1 second for touch** and **2 seconds for pen**. Per-monitor touch/pen switches, timing overrides, nicknames, pen-hover protection where supported, Pause, Stay here and Return now are provided.

Start in **Settings → Touch screen monitor support → Monitors & input detection test…**. The passive test stays open while you use other applications; no automatic return runs during the test. Associate a device only after the test validates complete contact reports and a corroborated pre-touch state. Main Settings **Apply** saves the draft. Normal mouse/trackpad use cancels a pending return; no synthetic click or input replay is used.

**Hardware compatibility is not assumed.** The initial provider accepts a strict subset of HID digitizer formats and requires a pre-touch snapshot from a different display. Virtual mouse-only paths, incomplete contacts, ambiguous device mappings, unsupported coordinate transforms or missing required hover data remain detection-only. Surface/spacedesk and Apollo/Moonlight must be tested separately on the real setup. [Touchscreen setup and limitations](docs/TOUCH-SUPPORT.md).

## Auto-hide, broader search and Recent apps

Taskbar inventory includes known app buttons even while the real taskbar is auto-hidden. If Explorer temporarily exposes no buttons, a clearly labelled fallback combines current pinned shortcuts with safely relaunchable running apps; exact pin coverage/order is not guaranteed in that fallback. No taskbar setting is changed and no hidden coordinate is clicked.

The wider bottom-left **Search apps, settings and files** box searches inside Taskbar Tiles. Try **display settings**, **screen resolution**, **refresh rate**, **mouse speed**, **auto hide taskbar**, **default browser** or **microphone permissions**. This uses documented Settings deep links and aliases, not a private Windows Search embedding or web search. Configure box/results widths and source switches in **Settings > Search**. The narrow top field still filters only windows/taskbar apps.

**Recent apps**, beside Favourites, offers up to ten recently opened applications after excluding all Taskbar apps, across pages. Collection starts when this version runs; verified launcher results and optionally newly observed outside-app windows are included, not old Windows usage records or simple focus switches. **Settings > Recent apps** controls local collection and offers **Clear recent history**. Up to 64 local launch descriptors are retained; explicit shortcut paths/arguments can be stored, but window/document titles, queries, browser history and process command lines are not. Nothing is uploaded. Use Ctrl+Shift+R while the menu has focus or right-click an entry for placement.

## Privacy and control

No telemetry, account login or usage analytics in the app. Icons/previews, taskbar accessibility, saved layouts and launch detection are processed locally. Search uses your existing local filename index; it does not crawl document contents. Logs are local; general launch/layout logs may include paths or titles, so review before sharing. Touch diagnostics are local and copied only on request; no reports or input recordings are uploaded automatically.

GitHub is contacted **only after you explicitly request an update check/download or open a repository link**. Checking updates discloses normal HTTP request metadata to GitHub. Websites you deliberately launch may make their own network requests. [Security policy](SECURITY.md).

## Build and contribute

The app is C# 5 / .NET Framework 4.8 / WinForms, without third-party runtime packages. Run **`Build.cmd`** on Windows to compile and run helper tests into `build/app`. Building does not alter your installation. Packaging needs [Inno Setup 6](https://jrsoftware.org/isdl.php).

GitHub Actions compiles/tests pull requests and builds a normal installer for a version update on main or matching version tags. Release gates cover real-menu rendering, native outside clicks, topmost ordering, launch fixtures, Touch Return policy/listener lifecycle, shortcut repair, updater HTTPS/checksums and installer lifecycle. Published releases include `SHA256SUMS.txt` and `build-info.json`; the latter records the exact commit/run and what CI tested. CI does not certify touchscreen hardware or the user's interactive desktop. [Testing](docs/TESTING.md) · [Architecture](docs/ARCHITECTURE.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md)

## Licence

MIT. See [LICENSE](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.txt) for the adapted FancyZones compatibility algorithms. Taskbar Tiles is independent of Microsoft, PowerToys and X-Mouse Button Control.
