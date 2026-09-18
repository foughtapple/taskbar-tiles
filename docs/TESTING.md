# Verification - Taskbar Tiles 0.6.2

## Executed in the authoring environment

C#-aware lexical scans checked delimiter/literal structure in all 24 C# source files. Source-wiring checks cover the complete installer compilation list, appearance default synchronization, migration gating, Settings dismissal propagation through all three entry paths, cancellation results, and the absence of saving/reopening calls in the dismissal implementation.

An independent Python translation of the geometry passed **1,792 title-header combinations** and **31,104 menu configurations**, including font sizes 9/22/32, header icon sizes 12/28/64, icons on/off, close X on/off, screen fitting, multiple preview sizes, grids, counts and display scales. Tests check title/icon/X separation, containment, icon dimensions, reserved preview space, balanced bottom-heavy rows and menu screen bounds. The exact executed offline report is in `OFFLINE-CHECKS.json`.

**No C# compilation or Windows application execution was performed here.** These arithmetic and lexical checks are not a C# typecheck and do not establish live Windows focus/icon behaviour. No live Shell, FancyZones, X-Mouse, Terminal or fullscreen test is claimed. Earlier-version testing descriptions are not counted as new executions.

## Windows install-time helper tests (included, not run here)

`Install.cmd` compiles the 24 source inputs and runs `--self-test` before replacing the working executable. A compile failure leaves it unchanged. Logs are in `%LOCALAPPDATA%\TaskbarTiles\build.log` and `self-test.log`. The earlier executable, settings and favourites are backed up after checks pass. `RestorePrevious.cmd` rolls back.

`InterfacePolishTests.cs` checks the two 22-pixel defaults, independent icon sizing, bounds, one-time upgrade semantics, preservation of later custom values, external-vs-owned focus decisions, and the shared header geometry. All existing launch-identity, dispatch, balanced-grid, font, zone, Search/Favourites and fullscreen helper suites still run. These helper suites do not launch, move, minimise or close actual applications, or write user settings.

## Windows checks still required

1. Upgrade from v0.6.1. Confirm both main fonts read 22 while other preferences are retained. Change one to 20, Apply and restart: 20 must persist. Reinstalling v0.6.2 must not reset it.
2. Open Settings, change a size without Apply, then click another app or desktop. Settings must dismiss; no parent switcher, Favourites or zone picker should reopen. Reopen Settings: the unsaved change must be absent. The tray app should remain running.
3. Repeat after Apply, then make a second change. Click away: the applied value must remain but the second change must not be saved.
4. Switch between Settings tabs, numeric spinners, dropdowns, full preview, Installed apps, favourite editor and Open/Save/Folder dialogs. These internal transitions must not dismiss Settings. Clicking an external app while a nested picker is open should cancel that child and then Settings. Test save-confirmation dialogs with Cancel too.
5. Turn the outside-close option off and Apply. Clicking another app must now keep Settings open. Restore it afterwards.
6. Inspect icons for a browser profile, Explorer, Terminal and a packaged app. Change title icon size between 12, 28 and 64; compare main/full previews and actual menu at multiple display scales. Title, icon, preview and close X must not overlap. Disabling icons must restore title space.
7. Verify the prior new-window, right-click zone, Search, Favourites, fullscreen minimise and close-X actions still work. Check repeated opening does not accumulate image resources. The launch-identification safeguards are retained.
