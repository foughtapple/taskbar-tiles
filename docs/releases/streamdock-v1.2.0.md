# Stream Dock module 1.2.0

Adds **Audio Control - Microphone** to the existing **Taskbar Tiles** category. Press it to mute or unmute the current Windows default input microphone. Clear **MIC LIVE** and **MIC MUTED** images reflect the actual Windows endpoint state, including changes made elsewhere. Default-input changes are followed automatically; missing devices and errors use a neutral unavailable state.

The control affects only the current default input. It does not mute all microphones or speakers, record audio, or request new permissions. Device changes and endpoint failures invalidate queued presses instead of replaying a toggle onto another microphone.

This release also fixes **Unexpected file in module archive** during module updates. ZIP entries now use canonical forward slashes accepted by Taskbar Tiles 0.15.0's existing strict archive reader. Validation remains strict; the published 1.1.0 asset is not replaced.

Update with **Taskbar Tiles > Settings > Modules > Stream Dock > Install / Update** while Stream Dock is fully closed. Existing function choices, private settings and unrelated plugins are preserved. Enable **Audio Control - Microphone**, apply function choices, reopen Stream Dock and add it from **Taskbar Tiles** under Key. New functions stay off until selected.

Compatible with **Taskbar Tiles 0.15.0 or later**; the core app version is unchanged. The module has its own version and update preference.

Validation includes 33 fake-endpoint audio checks, seven unified bridge tests, native worker appearance/shutdown without microphone key presses, and 104 package-manager/migration checks that exercise the exact finished outer ZIP through the production downloader/extractor in temporary folders. A physical mute/unmute acceptance test remains separate from these non-disruptive checks.
