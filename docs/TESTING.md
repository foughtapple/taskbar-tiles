# Testing and release gates

## What was validated during preparation

The source package was assembled in a Linux environment without a Windows/.NET Framework compiler. Source wiring, XML/JSON/YAML structure, archive contents and Git bundle integrity can be checked there. **The C# app, the Windows installer and live foreground switching were not executed in that environment.** See `PREPARATION-CHECKS.json` for recorded, actually executed preparation checks. Do not treat static checks as Windows tests.

## Automated Windows checks

`Build.cmd` / `tools/Build.ps1` compiles every C# source with the Windows Framework compiler and runs `TaskbarTiles.exe --self-test` from the isolated build folder. A timeout or nonzero result fails the build. The app's normal tray/hook/startup path does not run during these helper tests.

Tests include the inherited layout, labels, FancyZones transforms, interop structure sizes, search, launcher identity, settings-dismissal and title-icon checks. New tests inject a fake OS interface into the exact activation state machine used in production: covered windows, asynchronous restore, rejected requests, foreground bounce, bounded retries, cancellation after new input, reused handles, owned modal dialogs and unrelated same-process windows. Update tests validate strict release versions, allowed hosts, checksum lists and incomplete-release rejection without network access.

`tools/Test-Installer.ps1` is deliberately restricted to disposable GitHub Actions runners. It installs silently, checks per-user registration/version, writes sentinel settings/favourites, upgrades, verifies backups and preservation, uninstalls, and verifies program removal plus user-data retention. It does not test the visual setup wizard.

The release job publishes only after those checks pass. Its `build-info.json` records commit, workflow URL and the exact categories executed. No live desktop or gaming compatibility result is implied.

## Manual Windows release checklist

Record app version/commit, Windows build, display arrangement/scaling and results for each case:

- Open two overlapping windows from different apps. Click the obscured preview and verify focus AND keyboard input reach it.
- Repeat with two Explorer windows and two browser windows in the same process/profile; the exact chosen window must win.
- Repeat for minimised and maximised windows. Restoring a non-minimised maximised window must not shrink it.
- Open a Save/confirmation dialog. The blocked owner's selection should activate its legitimate modal dialog, not an unrelated palette.
- Start a switch then click/type in another app; no later focus steal should occur. Repeat rapidly and close the target before selection.
- Test borderless and exclusive-fullscreen apps, multi-monitor/DPI setups, optional fullscreen minimisation, elevated apps and always-on-top overlays. Record limitations rather than silently changing their policy.
- Verify mouse --toggle and keyboard Alt+Tab/Ctrl+Alt+Space paths, search-window selection, closing settings without saving, text/icon sizes, launch/zone placement and normal X close requests.
- Test the visual installer, startup choice, update notes/checksum failure/cancellation, preservation, uninstall and reinstall on a non-admin account.

No development-time screenshot of an unrelated or private desktop should be committed as a project screenshot without review.
