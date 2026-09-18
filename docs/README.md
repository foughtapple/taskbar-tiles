# Taskbar Tiles 0.6.2

A mouse-first Windows tray utility: switch between open windows, launch taskbar apps or your own favourites, and place windows on a monitor or into a zone. This is a focused interface update on the working v0.6.1. Its launch fixes, balanced rows, settings alignment, fullscreen behaviour, integrated Search, Favourites and zone features are retained.

## New in 0.6.2

**Both main label sizes default to 22 logical pixels.** This upgrade sets open-window title text and taskbar-app name text to 22 once. It preserves other preferences, including grid limits, tile sizes, startup and launch settings. Subsequent custom font changes survive restarts and reinstalling this version. The one-time migration records config schema 7; the previous settings are backed up by the installer and as `settings.ini.pre-v062`.

**Click away from Settings to dismiss the whole interface without saving unapplied edits.** This includes closing an owned large preview or favourite/file picker first. The main switcher, zone picker or Favourites will not reopen behind it. Clicking between Settings and its own controls, dropdowns and dialogs does not count as an outside click. A short focus-settle delay avoids discarding a draft during normal activation transitions. Changes deliberately saved with Apply stay saved. The tray app remains running.

This uses the existing **Settings > Navigation > Close the menu and Settings when clicking outside** option, on by default. Its saved value is respected. To compare settings while interacting with other apps, turn it off and click Apply first. Merely editing that checkbox in the draft does not silently change saved behaviour.

**App icons now appear immediately before open-window titles.** The close X stays at the far right. Under **Settings > Appearance > Window title icons** you can show/hide them or choose a size from 12 to 64 logical pixels (default 28). This size is independent of the title font and the large launcher icons. The title band reserves enough height and width to avoid overlapping the preview or the X. Live/full-size settings previews use the same header geometry.

Icons are requested from the window, its class, app identity or executable on a background worker. The UI does not wait for icon extraction. The bitmap cache is bounded, verifies process ownership, and releases old images; an unavailable icon shows a neutral application-window symbol rather than a guessed icon from another app.

## Launch fixes in 0.6.1

The "Choose window" message concerns matching a launched window to its destination. It is not, by itself, evidence that the app failed to launch: the list can include newly opened or pre-existing windows.

Taskbar Tiles now prefers a verified pinned shortcut or resolvable Shell app identity instead of a simulated click on the real taskbar. Passing the real shortcut to Windows preserves its saved arguments, profile and other launch details. A shortcut found only by an ambiguous icon-name match is not used as a launcher. When no verified target exists, the legacy taskbar action remains available with fresh coordinates, pointer/coverage checks and a short Shift-key hold. That fallback still needs the actual taskbar button to be visible.

Plain recognised Windows Terminal entries request `-w new`, rather than relying on Terminal's default window/tab preference. Stable/Preview package lookup stays within the selected package. Custom Terminal favourites/shortcuts with explicit arguments are not rewritten; those commands retain their configured behaviour. A custom CLI can request a new window itself using `-w new`.

Window detection now uses the window's explicit AppUserModel ID, with a packaged process-ID fallback, and refreshes startup identities rather than permanently caching missing data. A returned launch process is supporting evidence only when its identity can be verified; generic Shell/console hosts are not treated as proof. Different browser-profile IDs remain distinct. New windows must settle before placement. Reusing an old window is considered only after the configured wait expires, and only with a matching, newly focused stable foreground window; a forced-new Terminal launch cannot reuse an old window automatically.

If identification is still ambiguous, use **Refresh list**, **Wait longer**, or select **Place selected**. **Wait longer observes the same request and does not start another copy.** No automatic second launch is sent after Shell dispatch, even on timeout. Cancelling placement leaves any launched apps running. A dispatch already handed to Windows cannot be recalled, so it may still finish after cancellation.

Under **Settings > Navigation**, **Launch verified shortcuts directly** and **Open plain Terminal launchers in a new window** are enabled by default. Turn off the former to use the taskbar compatibility path. The existing wait-time/reuse settings remain. The tray also has **Cancel pending launch / placement**.

**Settings > Startup & tools > Open launch diagnostics** (also in the tray) opens a local rotating log of launch stages, matching counts and dispatch error types/codes. It does not record command arguments, window titles or search queries and does not upload anything. If a problem persists, retain this log rather than repeatedly clicking launch.

## Install or update

1. Extract the ZIP to a normal folder. Do not run the installer inside ZIP preview.
2. Double-click `Install.cmd` normally, not as administrator.
3. Use your existing mouse shortcut, or press Ctrl+Alt+Space.

No uninstall or X-Mouse changes are required when updating. The destination is `%LOCALAPPDATA%\TaskbarTiles`. Existing app tile sizes, preview sizes, monitor mappings, favourites and sign-in preference are kept. New installations use 120-pixel app tiles, 120% window previews, 22-pixel main labels and 28-pixel window-title icons.

The installer uses Windows PowerShell and the Microsoft .NET Framework compiler already used by previous versions. It does not download packages, install a service, request administrator access or permanently change PowerShell execution policy. It builds the source, runs its helper tests, then backs up and replaces the working executable. A failed build or failed helper test does not replace the existing executable. Read `build.log` or `self-test.log` in the app folder if installation fails.

This package was not compiled or run on Windows in the authoring environment. See TESTING.md for exactly which checks were and were not executed.

## Search inside the bottom-left box

Click **Search…** at the bottom-left. An icon-and-name search panel expands upward **inside the same Taskbar Tiles window**, with the query field at the bottom. It does not send Win+S, open the Windows Search panel or hand your query to a browser.

Search sources can be toggled individually in **Settings > Search**:

| Source | What it searches / does |
| --- | --- |
| Apps | Local installed-app and Start-menu shortcuts, plus taskbar apps. Portable apps without a shortcut may need adding to Favourites. |
| Windows | Current open-window titles across monitors. Selecting one switches to that existing window. |
| Favourites | Enabled entries from your own favourites list, plus common folder shortcuts. |
| Settings | A curated set of common Windows Settings pages, not the complete OS settings catalogue. |
| Files | Local filenames already in the Windows Search index. Two characters minimum; up to 100 indexed matches per query. |

Use the category dropdown to narrow results. Multiword matching, partial names and exact-name preference are supported. Queries are limited to 160 characters / 12 terms. Installed shortcuts are cached for five minutes; Refresh reloads them sooner. File queries run on a single background worker after a short typing delay, and stale results are ignored.

Click or press Enter to open the selected result. Open-window results switch to an existing window; taskbar results request a new instance; installed app and favourite results use their launch target. Apps that only allow one instance may reuse it. Right-click or Shift+Enter opens the existing monitor/zone picker when that feature is enabled.

Up/Down selects results; Page Up/Page Down or the wheel changes pages. Escape clears the query first, then returns to the window grid. Clicking outside the search surface inside the main menu dismisses the search without also activating the obscured card. The optional local Ctrl+Shift+S shortcut opens this same embedded search.

### Search limits and privacy

This is an integrated local launcher, **not a complete replacement for every Windows Search feature**. It does not crawl all disks, search document contents, search the web/cloud, maintain query history or rebuild the Windows index. Missing or stale file results depend on indexing and permissions. If the index/provider is unavailable or times out, apps, windows, favourites and settings searches remain available; the status explains the file-search problem.

There are no search analytics or query uploads. Choosing a website favourite can open your browser; choosing an application runs that application normally. Such explicit launches are separate from searching. Filenames are presented with Shell icons rather than loading file contents as thumbnails. Local custom icon images are only read when explicitly configured in Favourites.

Use **Settings > Search** to change panel width, result-row height and preferred visible rows. The panel is always constrained to the main window. Extra results use pages. The top-of-menu filter remains a separate, narrower filter for window titles and taskbar apps.

## Independent text sizes

Open **Settings > Appearance > Text sizes**.

| Control | Changes |
| --- | --- |
| Open-window title text size | The title above each active-window preview. |
| App-launch tile text size | The app names below the taskbar launcher icons. |

Both accept **9-32 logical pixels**, default **22**. Windows display scaling is then applied. These controls are separate from app tile size and window preview size. They do not change the fonts inside your applications, the monitor badges, or the separate Search/Favourites result lists.

The same fonts are used in the live preview and real menu. The card header reserves space for larger title text; app labels reserve up to two lines. Full names remain available by hovering. Very large fonts on unusually small tiles can require larger rendered tiles; the preview/size notice explains this and the saved tile-size value is not overwritten. Long horizontal titles use an ellipsis rather than painting over the close button.

## Maximum columns x maximum rows

Open **Settings > Appearance > Open-window layout**. Set **Maximum columns** and **Maximum rows**. A read-only line shows their product; there is no second, editable windows-per-page setting.

For a **6-column x 2-row maximum**, capacity is **12 windows per page** when the screen can fit the chosen card size:

| Open windows on the page | Visible rows, top to bottom |
| --- | --- |
| 4 | 4 |
| 6 | 6 |
| 7 | 3 + 4 |
| 8 | 4 + 4 |
| 9 | 4 + 5 |
| 11 | 5 + 6 |
| 12 | 6 + 6 |

The thirteenth window starts the next page, on a single centred row. One row is used until the column maximum is exceeded. Then windows are distributed as evenly as possible; lower rows receive extras. **Each row has its own centred position**, so odd row counts need not line up vertically. Unused rows no longer reserve an empty area.

The same rule works with three rows. Window order is retained. Up/Down now selects the closest horizontal centre in the neighbouring row instead of assuming every row has the maximum number of columns. Ordinary Tab/Left/Right traversal, close buttons and right-click zone placement remain available.

The panel grows around the chosen preview size. The app-strip width preference is not a cap on a larger open-window row. Physical screen limits still apply: extra app rows yield first, then the effective column/row capacity is limited to what fits. The menu's size notice and live preview explain constraints; saved preferences are preserved. Reducing font size, card size or row/column limits can make a layout fit a smaller monitor.

### Migration

Explicit prior column, row and size choices are retained. The former separate `LockWindowPageSize` and `WindowsPerPage` settings are retired. The old Auto columns value of 0 is translated to a concrete column count based on the old width and preview size. New installations default to a 6 x 2 maximum. Pre-update settings are backed up for rollback.

## Opening over a fullscreen application

**Settings > Navigation > Minimise the foreground fullscreen app when opening the switcher** defaults on. Both Alt+Tab interception and the existing X-Mouse / Ctrl+Alt+Space entry point use it.

The old version only showed/activated the switcher; it did not explicitly request minimisation. This version detects a monitor-sized foreground fullscreen window and sends `ShowWindowAsync(..., SW_MINIMIZE)`. The app remains running; choosing its card uses the existing restore/switch action. Ordinary maximised windows with title bars are excluded. Windows on other monitors are not minimised merely because the switcher is opened.

The switcher waits briefly for the asynchronous transition without blocking its input hook. It refreshes monitor geometry after the transition and avoids retrying indefinitely. Turning this setting off restores the previous overlay-only behaviour. Returning from Settings or a placement dialog does not accidentally minimise the window you just placed.

Fullscreen detection and game behaviour are not universal. Protected/nonresponsive apps can refuse or delay requests, and exclusive games may change display modes on losing focus. A briefly black display, paused game, or unavailable minimised thumbnail can depend on the application. The utility does not send F11, change game settings/resolutions, inject into processes or force-kill anything. This Windows integration has not been tested in the authoring environment.

## Settings help and previews

Hover a checkbox, setting label, numeric field, dropdown, tab or action button for an explanation. Tips include dependencies, units, side effects and the difference between draft edits and saved preferences. Labels, explanations and controls are now grouped into measured compact rows, with consistent input alignment. The favourite editor also explains each field.

The preview offers Main menu, Favourites, Screens & zones and Search. Changes are draft-only until Apply or Save & close. Cancel discards unapplied edits. The main-menu preview uses the actual layout and label-font code with illustrative window contents, including the draft maximum page capacity, row distribution and physical-fit warnings. Search preview shows illustrative rows without querying the app catalogue or index. Full size opens a read-only enlarged preview. Previews do not launch, close or move windows.

Live preview can be paused; Refresh or Full size still renders the current draft. On smaller Settings windows the preview moves below the controls. Hover its caption to read text that is truncated at the available size.

## Existing features retained

- Window cards switch to that existing window. Top-right X sends a normal close request; apps may ask to save. Optional middle-click close is off by default.
- App tiles mirror accessible visible taskbar apps in their order. Clicking requests launch through a verified shortcut/Shell identity when available, otherwise the revalidated taskbar action. Single-instance apps may reuse an existing window.
- Favourites sits at the bottom-right with icon/name rows, grouping, filtering, paging and import/export. Configure installed apps, shortcuts, files, folders or websites in its Settings tab. Favourites are saved separately in `favourites.json`.
- Right-click an applicable item to select a monitor or zone. The picker fits all monitor diagrams without scrolling, and includes full-screen monitor buttons, numbered destinations, Identify and Refresh.
- Full screen means maximise on the selected monitor with the normal taskbar, not F11 or exclusive full-screen mode.
- The FancyZones importer reads saved geometry without changing PowerToys configuration or zone history. Basic-zone fallback is labelled. Manual per-monitor layout choices remain available.
- Ambiguous newly launched windows can be selected manually instead of moving a guessed window. App minimum sizes, elevation, fullscreen rules and single-instance behaviour can limit placement.
- One-step Undo remembers the last window move, when enabled.
- Optional Show desktop and clipboard-history buttons still use Windows+D and Windows+V. The Search button no longer uses a native Windows shortcut.
- Tray menu, sign-in startup, Alt+Tab toggle and the existing X-Mouse IPC command are unchanged.

The real taskbar is not required for a resolved direct shortcut/Shell launch, but must be visible for the legacy taskbar fallback. Taskbar tile discovery still reads its accessible visible buttons. Auto-hidden taskbars, taskbar overflow, custom shell replacements, elevated/protected windows and live-thumbnail availability remain integration limits.

## Keyboard and mouse reference

| Input | Action |
| --- | --- |
| Existing X-Mouse Run Application command | Toggle Taskbar Tiles. |
| Ctrl+Alt+Space | Toggle the sticky switcher. |
| Alt+Tab | Use this switcher when interception is enabled. |
| Ctrl+Shift+S in the switcher | Integrated Search. |
| Ctrl+Space in the switcher | Favourites. |
| Ctrl+, in the switcher | Settings. |
| Ctrl+F / typing in the main grid | Focus/type in the top grid filter. |
| Shift+Enter / right-click an item | Monitor/zone picker when enabled. |
| Ctrl+Z outside a text field | Undo the last move when enabled. |
| Ctrl+wheel over a grid section | Resize that section. |
| F1 | Shortcut reference. |

Optional local shortcuts can be turned off under Quick access. They do not change shortcuts in other applications. Search's ordinary text editing and selection keys apply inside Search.

## Rollback, stop and uninstall

`RestorePrevious.cmd` restores the executable, configuration and settings/favourites saved at the last successful update. Those backups reflect their pre-update state, not edits made after updating.

Exit from the tray to stop the utility and restore native Alt+Tab. Disable Start when I sign in to stop automatic startup. `Uninstall.cmd` removes the executable and startup shortcut; read the script for its retained files before using it.

## Source / verification

C# 5 / .NET Framework 4.8. Windows Forms, DWM thumbnails, UI Automation, normal Shell launch APIs and local Windows Search OLE DB are used. Build inputs are listed explicitly in Install.ps1. `Verify.cmd` runs the installed helper tests. Source and checks are included; there is no precompiled executable in this ZIP.

See CHANGELOG.md, TESTING.md and SOURCES.md. SHA256SUMS.txt lists packaged file hashes and excludes itself.
