# Architecture

A per-user C# 5 / .NET Framework 4.8 WinForms application. Code is split into cooperating partial classes to preserve the existing implementation rather than replacing a working UI during packaging.

| Component | Responsibility |
| --- | --- |
| TaskbarTiles.cs | Process lifetime, tray, input hotkey, taskbar accessibility, open-window enumeration, core UI and self-test entry point |
| WindowActivation.cs | Exact-window foreground hand-off, deterministic bounded verification state machine, Win32 adapter and local diagnostics |
| LaunchReliability.cs / WindowPlacement.cs | Verified launch identities, asynchronous window discovery and deliberate placement |
| MenuGeometry.cs / Navigation.cs | Fit/balance/paging and keyboard navigation |
| IntegratedSearch.cs / Favourites*.cs | Embedded search and configurable favourites |
| Settings*.cs / WindowHeaderIcons.cs | Draft settings, preview, hover help and cancellation boundary |
| FancyZones.cs / ZonePicker.cs | Read-only saved-layout import, fit-all monitor diagram and placement selection |
| Updates.cs / AutomaticUpdates.cs | Manual and opt-in idle updates with verified installer launches |
| CurrentZone.cs / ProfileLayouts.cs | Current-zone placement and named app layouts with exact monitor identity |

`ActivationAttempt` never activates another window just because its process ID matches. It captures selected HWND+PID, asks for foreground while the switcher is still visible, lets the UI hide, and checks the actual foreground through an injected interface. Disabled owners may resolve to their own enabled modal popup. No AttachThreadInput, injected Alt press, foreground-lock changes, global topmost promotion or process termination is used.

Automatic app updates require opt-in, check daily and wait for a closed/idle app with no launch or layout in progress. Developer builds cannot install automatically. The source repository and installer naming are fixed; unexpected versions/hosts/names are rejected. It downloads into a temporary file, checks SHA-256, marks the download as Internet-origin on NTFS and opens a normal per-user installer. Hashes are not independent code signatures.

Settings/favourites remain beside the installed executable in the same per-user directory used by previous versions. This is intentional for migration and X-Mouse path compatibility. Setup owns a limited file list and leaves user data on uninstall. Developer builds live separately under `build/app`.

`--self-test` runs before the normal single-instance/UI path. `--exit` signals the session-local resident instance. `--toggle`/`--show` cooperate with the existing tray process. The legacy mutex/event names are preserved across upgrades so Setup can shut down an older copy without killing it.

StreamDockModule.cs applies explicit per-action choices. StreamDockModuleService.cs downloads a separately versioned, checksummed release from this repository and stages validated module code before activation. SettingsStreamDock.cs hosts the Modules page. The core installer contains metadata only; module code is cached under StreamDockData/Modules and preserved across core upgrades. Automatic module updates are a separate opt-in and refuse replacement while a plugin host is running. Steam account and game transitions share a Windows mutex across workers.

Touch Return is Developing; its runtime listener is not constructed at normal startup. Source and isolated fixtures remain for future development.
