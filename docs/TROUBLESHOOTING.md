# Troubleshooting

## A covering window stays in front

Open the tray's **Open switching diagnostics** command. Version 0.7.0 records the selected window handle/PID, activation attempts and verified result, not its title or contents. Reproduce once, then inspect the latest entries. `foreground verified` means the exact selected window (or its blocking owned modal dialog) became foreground; it does not promise that another program's permanently topmost overlay disappeared.

The new switcher requests foreground before hiding, waits for a restore to complete and does not accept a different top-level window from the same process as success. It cancels retries after new input to avoid fighting your navigation. Windows may still deny activation, especially across privilege boundaries or while an application is busy. Use the ordinary taskbar as a fallback. Do not run everything as administrator merely to hide this limitation.

When reporting a problem, include Windows/app versions, whether two windows belong to the same app, whether the target is minimised/fullscreen/elevated, display scaling and relevant redacted diagnostics.

## A tile does not open another window

Some applications enforce one instance. Shortcuts/profiles with custom arguments may also target existing windows. Plain Terminal entries use an explicit new-window request; custom arguments are preserved. The launcher uses a verified shortcut/app identity first, with taskbar Shift-click as a compatibility fallback. For that fallback, keep the real taskbar button visible and release modifier keys.

Placement is separate from launch. “Choose window” means the new window could not be identified safely, not necessarily that launch failed. Choose the correct window, refresh or wait longer; the app does not blindly launch another copy. See **Open launch diagnostics** in Settings → Startup & tools.

## The mouse button opens normal Windows Alt+Tab

Use X-Mouse **Run Application**, not its built-in ALT-TAB action. Paste **Copy X-Mouse command** from the Taskbar Tiles tray. Check the active X-Mouse profile/layer. Taskbar Tiles cannot override a game profile that maps the button elsewhere. Ctrl+Alt+Space is the independent test shortcut.

## GitHub says Not Found / no release yet

The repository/release must first be published. The first publisher script runs through your browser-authorised GitHub CLI and watches the release workflow. A repository without a completed Release workflow has no installer download yet. The updater will not install a draft, prerelease or release missing either its expected installer or checksum asset.

## A build/release fails

Open the failing GitHub Actions run and inspect the step/logs. The release workflow does not publish until compilation, helper tests and installer smoke tests pass. Fix the problem on main; do not overwrite an already published tag or installer. A draft may be retried after investigation. CI test results only apply to their exact commit/run.

## FancyZones layout differs

Refresh layouts; verify the monitor mapping in Settings. PowerToys must have saved the layout. The importer is read-only and can display Basic zones when a layout cannot be resolved. Applying a rectangle does not update FancyZones' internal window history. Minimum-size constraints in some apps may prevent an exact fit.
