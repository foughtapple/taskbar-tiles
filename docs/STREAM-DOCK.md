# Optional modules

Taskbar Tiles 0.15.0 separates the core taskbar application from optional modules. **Settings > Modules** lists Stream Dock and **Touch Return — Developing / unavailable**. Touch Return cannot be installed or activated in this version.

The ordinary app installer includes Stream Dock action/release metadata only. It neither downloads workers nor changes existing Stream Dock plugins. Stream Dock is an independent optional download with its own version and update preference.

## Install Stream Dock

1. Open **Settings > Modules**, enable **Stream Dock**, and check the exact functions you want.
2. Fully exit Stream Dock from its Windows notification-area icon.
3. Click **Install / Update**. Taskbar Tiles checks the module release index, downloads the immutable versioned ZIP and checksum, verifies the download, and stages the selected functions.
4. Reopen Stream Dock. Add buttons from **Taskbar Tiles** under Key or views under Info board.

Existing matching actions are discovered. Their UUIDs, scene placements, private account/printer/store settings and unrelated plugins are preserved. New functions default to Off. The four previous separate FoughtApple plugins consolidate into the single `com.foughtapple.taskbartiles.sdPlugin` package when the module is installed.

## Included functions

| Function | Placement | Behaviour |
| --- | --- | --- |
| Rocket League / Overwatch | Key | Open or request normal closure; no force kill |
| FancyZone Screenshot | Key | Zone under pointer; entire-monitor fallback; copy to clipboard |
| Clipboard History | Key | Win+V |
| GPT Voice | Key | Ctrl+Alt+Shift+F; receiving app must be running |
| Audio Control - Microphone (module 1.2.0) | Key | Toggle the current Windows default input mute; live endpoint state and device changes |
| Steam Smart Switch | Key | Current-account avatar; switch to the other remembered account; unknown state opens Steam |
| Steam Switch + Rocket League | Key | Close Rocket normally, switch account, confirm account, launch Rocket |
| P1S Print Status | Info board / Key | Local printer status with certificate pinning |
| PC CPU + RAM | Info board / Key | Five-second default monitoring while visible |
| NickNacks Orders | Info board / Key | Read-only Processing count; five-minute visible-only checks |

Game launching and Steam switching share a Windows transition mutex so a game launch cannot race account selection. Avatar sizes are capped to fit the bridge transport. Hidden printer/store views do not monitor their services.

## Updates and removal

**Automatically update this module** is a separate opt-in checkbox. Existing saved preferences are retained. Installed modules check at most daily while Taskbar Tiles runs, independently of app updates. If Stream Dock is open, the update remains available until you close it and click Install / Update. No live worker is replaced.

**Apply function choices** changes the available functions without downloading a release. **Repair module** reapplies downloaded code and retains private settings. **Remove module** disables all functions and keeps a restorable disabled copy. Scene files are never rewritten; disabled placements may appear unavailable until restored. General Settings Apply/Cancel does not install modules.

Downloads and changes are serialized, bounded, checksum verified, path validated and staged with rollback journals. Same-version checksum changes and downgrades are refused. SHA-256 checks integrity; it is not an independent publisher signature.

## Storage and publishing

- Core metadata: `%LOCALAPPDATA%\TaskbarTiles\streamdock`.
- Independently downloaded bundles: `%LOCALAPPDATA%\TaskbarTiles\StreamDockData\Modules\<version>`.
- Choices, disabled copies, receipts and backups: `%LOCALAPPDATA%\TaskbarTiles\StreamDockData`.
- Active plugin: `%APPDATA%\HotSpot\StreamDock\plugins\com.foughtapple.taskbartiles.sdPlugin`.
- Existing private credentials remain in their per-user FoughtApple data folders. App uninstall preserves optional module/private data.

For module updates, change the unified manifest/catalog version and `streamdock/module-index.json`, then build `tools/Build-StreamDock.ps1`. It creates `dist/TaskbarTiles-StreamDock-<version>.zip`, `module-SHA256SUMS.txt` and `module-build-info.json`. Publish them on immutable `streamdock-v<version>` releases with `latest=false`; the checksum uploads as `SHA256SUMS.txt`. Module changes do not require an app version bump. `tools/Build-StreamDockMetadata.ps1` generates the small core metadata payload without Go or Node.

The module index is fetched from the repository's controlled raw GitHub path. It names an exact version/tag/asset and minimum compatible app version. The immutable release checksum is mandatory; an optional SHA256 index pin must agree with it. Published versions/assets must never be replaced.

## Validation boundary

Tests cover synthetic Steam switching, cross-worker transition exclusion, avatar limits, fake MQTT/MCP, native workers without game/input commands, module download/archive/hash validation, immutable versions, serialization, temporary plugin upgrades, opt-outs, rollback, private state and core-only installer preservation. These checks do not reproduce the owner's real Steam accounts, printer, store endpoint, physical Stream Dock or Windows scene layout.

## Audio Control (Stream Dock module 1.2.0)

Enable **Audio Control - Microphone** under Settings > Modules after installing module 1.2.0, then add it from the existing **Taskbar Tiles** category. It defaults to Off, like other new functions. Module 1.2.0 remains compatible with Taskbar Tiles 0.15.0; no separate audio app is installed.

The button controls the ordinary Windows default input endpoint (eCapture/eMultimedia). **MIC LIVE** means that endpoint is unmuted; **MIC MUTED** means it is muted. It follows endpoint mute changes made elsewhere and default-input changes, using Windows notifications plus a one-second read-only reconciliation while visible. Multiple placements share the same observed state. When no default input is available or its state cannot be read, the neutral **NO MIC** / **MIC ERROR** presentation replaces the last known state.

Each deliberate key press reads the current mute state and performs at most one endpoint write. A changed endpoint invalidates a queued press; the button refreshes and alerts so the next deliberate press can control the new default. Failed writes are not retried onto another microphone. Hidden actions stop endpoint observation, and disconnect/shutdown releases Core Audio resources.

Only the current default input is affected. Other physical microphones, speakers, and applications pinned to another input or a separate default communications device retain their own device selection. Hardware mute switches and an app's own mute button can have independent states. There is no audio capture, recording, keyboard simulation or new permission request.

Audio tests use fake endpoints for mute writes and device-swap/error scenarios. The native worker bridge check displays real endpoint state without emitting key presses. A real-device acceptance check still requires installing the module while Stream Dock is closed and pressing the button when it is safe to interrupt microphone audio. Builds do not install, restart Stream Dock or publish the module.

Windows API references: [default endpoint selection](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-getdefaultaudioendpoint), [endpoint mute](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmute), [device notifications](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient).

Module 1.2.0 also corrects the ZIP path separators that caused **Unexpected file in module archive** when installing the published 1.1.0 bundle. Canonical forward-slash entries are accepted by the unchanged Taskbar Tiles 0.15.0 archive reader; path and checksum validation remain strict. Published older assets are retained unchanged.
