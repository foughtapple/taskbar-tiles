# Installation, upgrades and removal

## Published installer

1. Open https://github.com/foughtapple/taskbar-tiles/releases/latest.
2. Under Assets choose `TaskbarTiles-<version>-Setup.exe`, not “Source code”.
3. Run the wizard as your normal Windows user. Keep or change the startup checkbox.
4. Open Taskbar Tiles from Start, or press your existing mouse shortcut.

The setup installs to `%LOCALAPPDATA%\TaskbarTiles`. A fixed per-user path deliberately keeps existing X-Mouse commands working. The app is registered in Installed Apps and uses a normal uninstaller. No administrator rights are needed. A different Windows user gets a separate installation and settings.

.NET Framework 4.8 or later is required. Setup stops with an explanation if it is missing; it does not silently download a runtime. Microsoft download: https://dotnet.microsoft.com/download/dotnet-framework/net48.

## Move from versions 0.1–0.6.2

**Install over the old version; do not uninstall it first.** Setup asks the old tray app to exit, backs up its executable, executable config, `settings.ini` and `favourites.json`, then replaces only program-owned files. The timestamped backup is under `Backups` in the same directory. If the old copy does not exit, Setup stops with an instruction to exit from the tray instead of killing it or unrelated processes.

The startup shortcut keeps its existing choice. Existing favourites, sizes, monitor settings, titles/icons and X-Mouse `--toggle` commands remain in place. No duplicate legacy executable runs alongside the new one.

After successful installation, old downloaded/extracted ZIP folders outside `%LOCALAPPDATA%\TaskbarTiles` are no longer needed. **Do not run an old ZIP's Uninstall.cmd after installing the new version**: old removers were not designed for the registered installer. Use Windows Settings → Apps instead. Historical source/scripts left in the data directory are inert; there is no need to delete them to use the app.

## Updating

Tray → Check for updates → Check GitHub → Download & install. A manual download of the next Setup.exe from Releases works too. Updates keep the same installation directory and settings. There is no startup polling or silent installation.

The checksum comes from the same GitHub release as the installer. It detects truncated/corrupt downloads but is not an independent trust anchor. Releases are not currently Authenticode-signed. Never disable security software to install; consult the source/run provenance when deciding whether to trust a build.

## Remove the installed app

Exit Taskbar Tiles, then Windows Settings → Apps → Installed apps → Taskbar Tiles → Uninstall. Program files and managed shortcuts are removed. Personal data is retained. After uninstalling, you may back up or delete `%LOCALAPPDATA%\TaskbarTiles` yourself to remove favourites, settings, local logs, old scripts and downloaded installers. Remove the X-Mouse binding separately if you no longer use Taskbar Tiles.

## Optional local source build

Before a release exists, `Install.cmd` in the source package compiles/tests first and then installs the local build. It registers **Taskbar Tiles (local source build)** in Installed Apps. The later release Setup.exe supersedes that registration while retaining data. Once Setup.exe is installed, the local script refuses to overwrite it; use another release installer or a separate development build folder.

## Recovery

If a new version misbehaves, first exit it from the tray. Use a previous release's Setup.exe to reinstall that version. Older versions might not understand newer settings; keep your backup first. For a pre-GitHub version, restore its executable/config from a timestamped backup and restore `settings.ini`/`favourites.json` only when you intend to roll those settings back too. Keep the registered uninstaller for eventual removal.
