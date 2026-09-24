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
| Updates.cs | Explicit GitHub release check, bounded HTTPS transport, strict assets/hash checks and installer launch |

`ActivationAttempt` never activates another window just because its process ID matches. It captures selected HWND+PID, asks for foreground while the switcher is still visible, lets the UI hide, and checks the actual foreground through an injected interface. Disabled owners may resolve to their own enabled modal popup. No AttachThreadInput, injected Alt press, foreground-lock changes, global topmost promotion or process termination is used.

The updater has no background scheduler. The source repository and installer naming are fixed; unexpected versions/hosts/names are rejected. It downloads into a temporary file, checks SHA-256, marks the download as Internet-origin on NTFS and opens a normal per-user installer. Hashes are not independent code signatures.

Settings/favourites remain beside the installed executable in the same per-user directory used by previous versions. This is intentional for migration and X-Mouse path compatibility. Setup owns a limited file list and leaves user data on uninstall. Developer builds live separately under `build/app`.

`--self-test` runs before the normal single-instance/UI path. `--exit` signals the session-local resident instance. `--toggle`/`--show` cooperate with the existing tray process. The legacy mutex/event names are preserved across upgrades so Setup can shut down an older copy without killing it.

`StreamDockModule.cs` is an optional filesystem-only package manager; `SettingsStreamDock.cs` hosts its independent settings tab. It loads the source-built bundled catalogue, validates checksums/paths/manifests and reconciles explicit per-action choices. User choices live in StreamDockData; payloads live in streamdock. Setup and one-shot application startup use `--sync-streamdock`; Setup calls `--streamdock-ready` before overwriting code. It neither runs monitors inside Taskbar Tiles nor contacts a plugin server. SDK appearance/disappearance logic remains in each plugin.
