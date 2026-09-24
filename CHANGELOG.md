# Changelog

## 0.10.0

- Add optional Settings > Stream Dock package/action management, bundled updates, opt-outs and recoverable backups.
- Preserve 0.9.1 app reopening and existing taskbar behaviour.
- Include final Steam, desktop, printer/CPU and order-count actions; leave the obsolete standalone Steam indicator and third-party plugins alone.


## 0.9.1

- Reopen ordinary Steam entries through the registered client and its open-main request, including helper-hosted and tray-hidden UI; preserve explicit game/account/custom commands.
- Prefer paired app-authored taskbar relaunch metadata to a bare UI-process executable. Keep the relauncher identity in taskbar fallback and recent apps.
- Use Explorer default Invoke actions in the compatibility fallback, with a normal visible click only when Invoke is unavailable before dispatch. No forced Shift/new-instance action or repeat after an ambiguous dispatch.
- Add policy and native hidden-window/default-action regressions; retain the Stream Dock work on its separate branch and all existing settings.

## 0.9.0

- Discover taskbar apps while auto-hidden; separate inventory from visible click targets and label the pinned/running fallback.
- Widen integrated Search; add documented Windows Settings deep links, query aliases and limited typo tolerance.
- Add a local Recent apps palette beside Favourites, capped at ten after profile-aware taskbar exclusion; collection controls and immediate Clear history.
- Preserve previous launcher/activation/rendering/touch/updater safeguards and add regression coverage.

## 0.8.1

- Fix false digitizer blocks from known input-device arrival/keyboard/mouse notifications. Genuine digitizer or unknown topology changes remain fail-closed.
- Reset stale passive-test evidence after UI pauses rather than latching a new fault; runtime gaps still require retesting.
- Add a selected-device setup checklist and useful copied diagnostics; distinguish capabilities from actual contact/hover evidence and input screens from return destinations.
- Preserve all saved settings and the complete 0.8.0 switcher, launch, rendering, shortcut and updater behaviour.

## 0.8.0

- Recover Alt+Tab interception after transient UI errors without changing the saved preference. Keep callbacks off the low-level pump, periodically renew between gestures, rearm after wake/unlock and repair on explicit reopen. Add Shortcut health and diagnostics.
- Include the previously staged outside-click fix from 0.7.6 and all published rendering/launch/updater repairs.
- Add experimental Touch screen monitor support: passive HID detection, full-frame contact tracking, per-monitor verified device association, touch/pen idle delays, Stay here, pause, optional hotkeys and one-shot focus/cursor return. Physical mouse always cancels.
- Default Touch Return off. Unsupported reports, virtual mouse-only input, unknown pre-touch state or missing required hover information cannot trigger automatic return. A local cross-application detection test is mandatory; CI does not certify spacedesk/Apollo hardware.

## 0.7.6

- Close the menu on an outside mouse-down even when it never received foreground activation; do not consume the click.
- Reject stale observations from an earlier open/hide session. Preserve clicks inside the menu, embedded search, owned dialogs and X-Mouse toggle behaviour.
- Verify settled external focus after the menu has held focus. Stop all observation on dismissal/selection/shutdown and respect the existing click-away preference.
- Apply the same click event handling to Settings; discard unapplied drafts, retain explicit Apply saves and unwind owned dialogs safely.
- Retain the complete 0.7.5 font lifetime, icon isolation, guarded-paint and bounded recovery fix and its actual-menu rendering tests.
- Add native outside-click regression tests using a disposable nonactivating window in a separate process. Existing layout, launch-instance, topmost, placement and secure updater behavior remains.

## 0.7.5

- Fix font lifetime across repeated opens and settings previews: WinForms may retain an equal old Font, so detach control bindings before disposing its owner. Reuse unchanged font generations.
- Isolate bad icons and contain managed paint failures before WinForms latches its white/red-X error surface. Retry resource rebuild at most twice per open, with F5/tray graphics refresh and local rendering diagnostics.
- Native regressions reproduce the old disposed-font defect and exercise the real menu paint/preview path, equal metrics, font/scale changes, damaged icons, persistent failures and dismissal safety.
- Keep app-managed launch outcomes, exact-window switching, topmost handling, updater checks and existing user configuration unchanged.

## 0.7.4

- Observe normal launches as well as zone launches. Send one request and honour whichever new or reused window the app actually exposes.
- Prefer stable new windows, including a new HWND in an existing process. After the bounded wait, restore a single verified existing window even if it never came to the foreground.
- Keep profile/identity checks, ambiguity safeguards and exact-window activation. No product-name list, app-setting edits or duplicate launch attempts.
- Let new navigation supersede passive launch observation; no modal chooser for ordinary shortcuts and no delayed focus steal after the user changes apps.
- Turn the old forced-new Terminal default off once on upgrade; keep an explicit optional override and preserve custom shortcut arguments and other settings.
- Add native end-to-end fixture tests that change single/multiple-instance preference between requests, exercise minimised-window reuse and delayed new-window placement.

## 0.7.3

- Reassert the switcher's own native topmost layer on opening/activation and repair overlap while the popup is in use.
- Suspend layer maintenance before handing focus to the exact selected window, dismissal and modal transitions. Never change another app's topmost preference or steal focus during maintenance.
- Add native Z-order regression tests for competing topmost windows, stale cached/native style, repeated hide/reopen, modal ordering and handoff gates.
- Retain updater TLS, verified downloads, launch-placement fixes and all preferences; correct the tray version label.

## 0.7.2

- Fix updater TLS negotiation in CodeDOM-built executables by declaring the .NET Framework 4.8 target in assembly metadata and opting into OS-selected TLS/strong cryptography in the app-local configuration.
- Retain normal certificate validation, HTTPS-only trusted redirects and SHA-256 verification. Do not enable legacy protocols, modify machine-wide TLS policy or silently retry with weaker security.
- Fix post-download path errors by writing and verifying the Internet security marker through the native named-stream API before exposing the installer.
- Provide an always-available Browser download action and specific TLS/certificate failure explanations.
- Add offline runtime-policy regressions plus an explicit online integration test using the actual built executable to read GitHub metadata, download/verify a released installer and delete the test download without running it.
- Preserve the 0.7.1 launch-placement fixes, Settings Updates tab and all user configuration.

## 0.7.1

- Improve launch metadata and hosted-window identity matching; retain browser-profile and new-window safeguards.
- Show all eligible windows for deliberate selection when the safe placement shortlist is empty.
- Add privacy-conscious launch diagnostics and a dedicated Settings Updates tab.

## 0.7.0

### Fixed
- Activate the exact selected window before hiding the picker; verify foreground hand-off and wait for asynchronous restoration.
- Do not replace an enabled window with an arbitrary owned popup or accept another same-process window as success.
- Add bounded, user-input-aware retry and local focus diagnostics. The integrated search uses the same activation path.

### Added
- Per-user Inno Setup installer, upgrade backup, normal uninstaller and migration from source-built versions.
- Explicit GitHub update check/download with strict release/host/filename validation and SHA-256 verification.
- Version/product metadata, app icon, professional repository documentation, issue forms and Windows build/release workflows.
- Deterministic activation/update tests and an isolated Windows installer lifecycle smoke test.

### Preserved
- Default title/app-name text 22, independent title icon sizing, balanced paging, live settings preview/discard, favourites, integrated search, launch reliability and monitor/FancyZones placement.

### Validation
- See docs/TESTING.md. Preparation-time static checks are not a claim of a successful Windows compile or live focus test. Published release provenance records the CI checks actually executed.

## Earlier local versions

# 0.6.2 - readable defaults, window-title icons and settings click-away

- Default both open-window title and taskbar app-label fonts to 22 logical pixels; apply the requested value once when migrating pre-schema-7 settings. Preserve later custom font edits and all other preferences.
- Add show/hide and independent 12-64 logical-pixel size control for app icons immediately before open-window titles (default 28).
- Share title/icon/close-button geometry with the live settings preview; increase header space where required and leave the close X at the far right.
- Read and cache icons on a separate background worker, verify window/process ownership before accepting a result, bound image resources and provide a neutral fallback.
- Extend saved HideOnFocusLoss behaviour to Settings and its owned dialogs, discarding unapplied edits without reopening the switcher, zone picker or Favourites.
- Treat Settings' own controls, dropdowns and child dialogs as internal. Null/transitional focus does not discard the draft. Nested modals cancel normally before Settings closes.
- Explicit Apply remains a save; later outside dismissal never undoes already-applied changes. Dismissal paths cannot commit drafts.
- Retain v0.6.1 launch and fullscreen code. Add installer helper regressions for defaults/migration, focus rules and header geometry. Windows runtime behaviour has not been tested in the authoring environment.

# Taskbar Tiles changelog

## 0.6.1

- Prefer verified pinned shortcuts and resolvable Shell identities over simulated taskbar clicks; preserve shortcut arguments/profiles. Metadata lookup no longer depends on icon extraction succeeding.
- Plain Windows Terminal launchers explicitly request a new window; keep selected stable/Preview package and do not overwrite user command arguments.
- Read packaged process AppUserModel IDs when a window exposes no explicit ID; refresh startup metadata instead of caching blanks indefinitely.
- Capture usable launch-process evidence; do not treat generic broker/console hosts, window titles or friendly app names as proof. Retain explicit browser-profile mismatch protection.
- Wait for a stable new window; remove the previous 2.5-second early reuse of old windows. No automatic reuse for a forced-new Terminal request.
- Run launch preparation off the UI thread. Bound dispatch waiting; prevent an unfinished pre-dispatch resolver from launching after cancellation. Do not silently repeat already-dispatched launches.
- Improve fallback Shift+click timing and check that the pointer/taskbar stayed available before clicking.
- Add Refresh list / Wait longer / Diagnostics in the window-choice fallback. Extending the wait never launches another instance.
- Add a local rotating launch log and tray cancellation/diagnostic actions. No command arguments, window titles or search queries in the new log.
- Retain v0.6 layout, text settings, fullscreen handling, Search/Favourites and monitor-zone functionality.
- Include additional pure C# identity, request-state and policy regressions; installer compiles/tests before replacing the existing executable.

## 0.6

- Two independent font settings: open-window title text and app-launch tile text (9-32 logical pixels).
- Live previews and live menu share those fonts; title/label areas reserve space for larger text.
- Compact, measured settings rows align each input with its own label and put its help directly below.
- Page limit is maximum columns x maximum rows. The separate page-size lock/count controls are retired.
- Active windows use one row first, then evenly balanced rows with extras below. Every row is centred separately; unused rows do not reserve space.
- Up/Down navigation follows visual row centres and handles partial pages.
- Explicit foreground fullscreen minimise request for Alt+Tab and mouse/shortcut opening, with an enabled-by-default opt-out setting and bounded asynchronous transition.
- Internal returns from Settings/placement do not minimise a just-placed app.
- Preview summaries use the draft values rather than stale saved ones.
- Existing sizes, favourites, monitor mappings, startup preference and X-Mouse command retained, with documented migration for former Auto columns.
- Added pure C# font/layout/fullscreen regressions to the install-time checks. No Windows runtime testing is claimed for this release.

## 0.5 — embedded Search, fixed window pages and hover help

- Replaced the Win+S route with a child Search panel anchored inside the bottom-left of the main menu. Icon/name results, categories, keyboard selection, paging and right-click placement use existing launch/window paths.
- Added separate source toggles for installed/taskbar apps, open windows, enabled favourites/common folders, curated Settings pages and local indexed filenames. No web query, full-disk crawl or query history.
- Added LockWindowPageSize, WindowsPerPage and WindowColumns. Page slots remain aligned; the menu grows around the selected card size. Physical monitor constraints produce an explicit effective-capacity notice instead of off-screen controls.
- Added explanatory hover tips to the Settings controls, tabs, actions and favourite editor. Dependent page settings enable/disable coherently.
- Added a Search settings tab and a read-only Search preview; main preview caption includes effective grid dimensions and fit warnings.
- Preserved existing settings, including custom sizes of 88, favourites, monitor mappings, X-Mouse command and sign-in choice. Config schema is 5; the legacy WindowsSearchButton key now controls the embedded button.
- Search worker coalesces requests, rejects stale results and caps local index queries. File result icons do not load document/image contents unless an icon image was explicitly configured.
- Added 3,600 main-grid and 2,700 embedded-search helper cases plus matching/escaping/configuration checks. Windows execution remains part of the installer and has not been performed in the authoring environment.

# 0.4 — Search, Favourites and live settings previews

- Added a bottom-left native Windows Search action (Win+S) and bottom-right favourites launcher.
- Added a dedicated Favourites settings tab: installed-app selection, taskbar shortcuts, custom paths/arguments, folders, websites, groups, ordering, per-entry enablement, custom icons and explicit JSON import/export.
- Added icon-and-name rows, filtering, optional group/detail labels, optional Alt+1…9, mouse/keyboard paging, and existing right-click zone placement for favourites.
- Added live read-only settings previews of the main menu, favourite rows and screen/zone map, with a full-size view, manual refresh and a disable toggle.
- The main preview uses the real layout/paint path with illustrative window contents; favourite preview and popup share geometry/row rendering.
- Added optional desktop and Windows clipboard-history buttons, local navigation shortcuts, a shortcut reference, and opt-in middle-click close requests.
- Added modifier-release checks before native shortcuts, import validation, unique favourite identities, and Apply rollback for file/startup failures.
- Extended helper tests and installer source/backup lists. Existing sizes, startup, X-Mouse command, taskbar launching and fit-all zone picker are retained.
- Runtime Windows compilation/UI integration were not performed in the package-authoring environment. See TESTING.md.

---

# Changelog

## 0.3.1

- Fit the entire monitor diagram, including buttons, captions and alternate zone targets, to both viewport dimensions. No scrolling or hidden panning.
- Refit and centre automatically on resize; preserve monitor aspect ratios and relative desktop arrangement.
- Keep aligned monitor rows/columns together when inserting space for controls.
- Compact toolbar and one-line source/shortcut footer under each monitor. Full layout warnings remain available on hover.
- Preserve every numbered zone shortcut, including overlapping/nested zones, with exact original desktop destinations after display scaling.
- Optional matching native dark title-bar colours on supported Windows versions.
- Preferred button sizes are retained; only the presentation shrinks when necessary to fit. No settings migration or changes to taskbar launching, placement, mouse bindings or startup.
- Add complete-viewport, hit-test and centred-layout regression checks; the prior checks did not assert that the completed diagram stayed inside its viewport.

## 0.3

- Independent open-window preview scaling and app tile sizing; defaults 120% and 120 logical pixels.
- Settings cog with five organised settings pages, feature switches, Apply, Save and restore-defaults controls.
- Right-click monitor/zone picker for existing windows and taskbar app launches.
- Read-only FancyZones Grid/Canvas/template compatibility, monitor/virtual-desktop identity matching and manual per-monitor overrides.
- Large adjustable maximise-on-monitor buttons, monitor identification, layout refresh and numbered alternate zone targets.
- Normal close X on each preview; preserves applications' unsaved-work prompts.
- Quick search, current-monitor-only filtering, monitor badges, Ctrl+wheel sizing and Undo last move.
- New-window identity watcher with single-instance handling, timeout and manual selection when a reliable match is unavailable.
- Previous executable/settings backup and RestorePrevious.cmd.
- Expanded pure helper tests, JSON reference, geometry checks and source attribution.
- Preserves v0.2 Shell icons, taskbar launch mechanism, named-event mouse command and startup preference.

## 0.2 baseline

The working user-confirmed build: extracted Shell icons rather than taskbar screenshot crops, balanced tiles and cleaned labels, quick app size controls, direct X-Mouse Run Application command and helper-test-gated local installation.
