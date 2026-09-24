# Stream Dock module

Taskbar Tiles 0.10.1 uses an optional **Settings > Stream Dock** tab and one unified **Taskbar Tiles** plugin/category. It is a local package manager, separate from the taskbar switcher. It does not run the monitors, rewrite Stream Dock scenes, or change other installed plugins.

## First upgrade / bring the existing plugins under management

1. Update Taskbar Tiles normally using **Settings > Updates > Check for updates**, or install the current GitHub release's Setup executable. Do not run the old separate plugin installers afterward.
2. Open **Taskbar Tiles Settings > Stream Dock**. Existing matching custom plugins are discovered and preselected; new actions start unchecked. Check the exact actions you want. Keep **Update enabled modules when Taskbar Tiles updates** checked.
3. Fully exit Stream Dock using its Windows notification-area icon. Closing just its window is not enough. Leave Taskbar Tiles open.
4. Choose **Apply Stream Dock choices**. The manager validates packages, backs up matching existing folders, and adopts the selected actions. It preserves external account/printer/store credentials and local settings files inside existing plugin folders. It refuses newer/unrecognised plugin versions rather than downgrading them.
5. Reopen Stream Dock. Add available actions from the single **Taskbar Tiles** section under **Key** or **Info board**. Do not use Toolbox > Open for native plugin actions.

All ten managed actions now live in one `com.foughtapple.taskbartiles.sdPlugin` package. Their existing action UUIDs are retained. On the first 0.10.1 Apply/update, the four 0.10.0 managed plugin folders are archived to the Taskbar Tiles backup area so Stream Dock no longer shows repeated headings. Other vendors' plugins, the working audio switch, and the removed Codex Monitor are not managed or reinstated.

## Included actions

| Action | Type | Behaviour |
| --- | --- | --- |
| Rocket League - Open / Close | Key | Steam launch or normal close request; no force kill |
| Overwatch - Open / Close | Key | Existing Steam/shortcut launcher choice; normal close request |
| FancyZone Screenshot | Key | Zone under pointer; falls back to that entire monitor, including its taskbar; copies to clipboard |
| Clipboard History | Key | Win+V |
| GPT Voice | Key | Ctrl+Alt+Shift+F; receiving app must be running |
| Steam Smart Switch | Key | Shows active avatar and switches to the other remembered account; unknown/closed state opens Steam |
| Steam Switch + Rocket League | Key | Requests Rocket's closure, waits, changes account, verifies sign-in, then launches Rocket |
| P1S Print Status | Info board / Key | Version 1.2 reconnect fixes, lightweight visible-only printer monitoring |
| PC CPU + RAM | Info board / Key | Five-second default monitoring, no monitoring while absent |
| NickNacks Orders | Info board / Key | Five-minute visible-only Processing count over the configured read-only MCP tool |

The unified package supports both buttons and views; the tab's Type column indicates the intended placement. The exact action selection is written to the installed manifest and enforced at runtime. Turning off P1S does not turn off CPU/RAM, even though they share an executable. Existing placements for disabled actions may show missing/unavailable until re-enabled; no profiles are edited to remove them.

## Future updates: one updater for both applications

The ordinary Taskbar Tiles release installer includes the reviewed Stream Dock package catalogue and all module payloads. There is **no second network updater**, no downloads from mutable branch files, and no need to run the standalone plugin installers.

1. Use Taskbar Tiles' ordinary **Check for updates** and install the new release.
2. When module auto-updates have been enabled, Setup requires Stream Dock to be fully closed before proceeding. It does not kill Stream Dock or plugin processes.
3. Setup installs the new Taskbar Tiles files and synchronises previously selected modules. Previously unchecked actions remain off; brand-new actions appear unchecked. A disabled package stays outside Stream Dock's active plugin folder.
4. Reopen Stream Dock to load the updated plugins. Existing placements and settings are retained.

Taskbar Tiles also performs one reconciliation at startup, covering portable/source updates. If Stream Dock is still running, changes remain **pending**; it does not repeatedly retry, replace live executables, or silently claim an update succeeded. Close Stream Dock and choose Apply from the tab. The last outcome is shown there and in `StreamDockData/last-result.txt`.

Turning off the auto-update checkbox (then Apply) opts out of automatic synchronisation, not the installed actions. You can still Apply manually. There is no background package-monitoring loop in Taskbar Tiles.

## Storage, disable, recovery

- Bundled code: `%LOCALAPPDATA%\TaskbarTiles\streamdock` (installer-owned).
- Choices, disabled packages and backups: `%LOCALAPPDATA%\TaskbarTiles\StreamDockData` (user-owned; not erased on app upgrade/uninstall).
- Active plugins: `%APPDATA%\HotSpot\StreamDock\plugins`.
- Existing Steam/printer/MCP credentials remain in their existing per-user FoughtApple data locations. They are never stored in this repository, bundle, catalogue or package receipt.
- All-off removes the package directory from Stream Dock discovery but keeps an independently restorable disabled copy and backup. Re-enabling restores configuration.
- Each change is staged, SHA-256 checked, path validated, then swapped with a recovery journal. A failed individual replacement restores its old directory; earlier successfully applied packages remain valid. A subsequent Apply reconciles pending choices. Hashes provide integrity, not an independent code signature.
- **Repair enabled packages** reapplies bundled code even if the receipt already matches; local private settings are retained.
- Backups are intentionally not automatically deleted. After successful use, older backups can be removed manually.
- Uninstalling Taskbar Tiles does not silently remove plugins you enabled or their private data. Disable the managed actions first to unload them from Stream Dock.

## Adding the next module in GitHub

1. Add the action/worker under `streamdock/modules/`, keep its stable action UUID, and expose it through the unified `taskbartiles` package/bridge. Keep secrets/runtime files out of source and add tests beside the worker or bridge.
2. Add its action record to the single `taskbartiles` entry in `streamdock/catalog-source.json` and increment the unified package version whenever its shipped code changes. Never reuse a published version for different code. New UUIDs default to Off.
3. Extend `tools/Build-StreamDock.ps1` for its build command if it uses a new language/backend. Continue enforcing manifest-based runtime allow-listing for individually disabled actions.
4. Increment `version.txt`, `Program.Version`, `AssemblyInfo`, add a changelog entry and `docs/releases/vX.Y.Z.md`.
5. Submit a PR and pass the Windows build, synthetic lifecycle tests, manager tests and existing taskbar regressions. Merge; the existing Release workflow creates the installer and checksums. Users receive the catalogue/modules through the next ordinary Taskbar Tiles update.

All runtime binaries are rebuilt by CI from the repository. Module packages are assembled with per-package SHA-256 hashes at build time, then included inside the normal release installer. Developers need Windows/.NET Framework compiler, Go and Node; end users do not need build tools. Steam Smart Switch uses Stream Dock's existing Node 20 plugin runtime (Stream Dock 3.10.188.226+).

## Validation boundary

CI exercises synthetic account switches, fake MQTT/MCP/Stream Dock connections, Windows controls startup (without game launch/input), package installation into temporary directories, opt-outs, per-action disabling, upgrades, private-settings preservation, traversal/integrity rejection, busy-process refusal and rollback. This does not reproduce the owner's actual Steam accounts, P1S, store endpoint, display layout or physical 293S. Test those on the PC after the first integration.

Official SDK references: [manifest/controllers](https://sdk.key123.vip/en/guide/manifest.html), [plugin discovery](https://sdk.key123.vip/en/guide/get-started.html), [visibility events](https://sdk.key123.vip/en/guide/events-received.html).

The obsolete standalone Steam account indicator is intentionally not reinstated; use Steam Smart Switch. Any independently installed copy stays untouched. The count display retains its black/red design as a scalable 256px vector, rather than raster glyph files.
