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
| Audio Control - Microphone (module 1.2.1) | Key | Mute or unmute every available Windows microphone; actual aggregate state |
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

## Audio Control (Stream Dock module 1.2.1)

Enable **Audio Control - Microphone** in Modules, then add it from the existing **Taskbar Tiles** category. Existing placements and function choices are retained. The module remains compatible with Taskbar Tiles 0.15.0.

Mute on sets mute for every active Windows capture endpoint. Mute off unmutes every available endpoint, including microphones individually muted before using this button. Speakers and render endpoints are never included. No previous individual mute states are restored.

**MICS LIVE** means every available microphone is unmuted. **MICS MUTED** requires a successful readback from every microphone. **MIXED MICS** means some microphones are muted and others are live. **MIC ERROR** reports unknown state or unsuccessful writes; it never claims all microphones are muted. **NO MIC** means no available capture endpoints. Changes made elsewhere remain visible.

Each deliberate press sets one desired value on all available microphones and verifies every endpoint separately. A failure on one endpoint does not stop the other endpoint commands. From an initial mixed state a press mutes all; while a global mute command is on, the next press unmutes all even if the state is mixed or an earlier write failed. A queued press expires if device membership changes before it is handled.

While global mute is on, newly connected or reconnected microphones are muted once on arrival, including when the button's page is hidden. Ordinary external mute changes on existing endpoints are observed rather than repeatedly overwritten. An already fully muted startup adopts on mode without an initial write. When all microphones are explicitly unmuted elsewhere, on mode clears. No-device, removal and wake cases release/recreate endpoint resources safely.

Local bounded diagnostics in %LOCALAPPDATA%\FoughtApple\TaskbarTilesStreamDock\audio-diagnostic.log record Audio Control events, endpoint IDs, desired mute, actual readback and HRESULT failures. No audio, unrelated actions, credentials or user messages are logged. No audio capture, recording or new device permissions are requested. App-specific and hardware controls can have separate states.

Tests cover synthetic all-endpoint commands, mixed/partial/unknown states, individually pre-muted endpoints, arrival/reconnect, hidden global mute, stale commands and readback failures. Build checks only read live endpoint state. Physical button acceptance requires the user to press mute and unmute when ready; builds never press it.

Windows API references: [capture endpoint enumeration](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-enumaudioendpoints), [endpoint mute](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmute), [device notifications](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient).

Module 1.2.0 corrected the ZIP separators that caused **Unexpected file in module archive** with published 1.1.0. Canonical forward-slash entries remain required by the unchanged Taskbar Tiles 0.15.0 archive reader. Published older assets remain unchanged.
