# Touch screen monitor support

## Detection first

Touch Return is opt-in and experimental. It runs on the main Windows PC. It neither replaces a remote-display client nor takes ownership of drawing input. The first step is to establish that the actual input path exposes enough information to return safely.

Open **Settings > Touch screen monitor support > Monitors & input detection test**. Select the input monitor in the diagram or dropdown. The test remains visible while you work in another application; this is an explicit exception to Settings click-away. Automatic return cannot run while the test is open.

Keep an application on another monitor active, then touch the selected screen. In an ordinary app, test a tap, a finger held still for at least three seconds, a drag, two fingers with staggered releases, scrolling and any gestures you use. For pen, test a stroke, a held contact, and hover entry/exit. Confirm that pressure, tilt, eraser and palm rejection still behave as before where supported by that app and driver.

The device list must show a supported report format, contact activity and explicit releases. Touch association additionally requires at least two simultaneous observed contacts and a long-held release. A matching pre-touch foreground/cursor snapshot on a different monitor must have been observed. Select the tested device, confirm unchanged input, and choose **Associate tested device**. This is only evidence for the tested provider/path, not a universal compatibility certificate.

Use the Touch/Pen and automatic-return checkboxes for that monitor, then **Use draft**. Enable the master option and click **Apply** in main Settings. Other monitors remain disabled. Names/numbers are labels; matching uses the stored monitor and device identities. Disconnected entries remain in the dropdown. Reconnection with a different identity requires explicit reassociation; no similar-looking monitor is silently enabled.

## Normal operation

The origin is the window that was active before the touch—not the window under the mouse. One original return point is retained for a session. Further contact resets the idle timer. All fingers must release; a stationary held contact cannot expire. Pen hover can keep focus while in range if the provider reports it. The default delays are 1000 ms for touch and 2000 ms for pen; -1 in a monitor's timing fields means use global settings.

Use tray **Touch screen monitor support > Stay here** for extended reading, drawing or thinking. Turning it off starts a fresh countdown. **Pause** cancels the current session. **Return now** still waits for safe contact/modifier release. Optional global shortcut fields are blank initially; conflicts are reported, not overwritten.

Physical or unclassified mouse/trackpad movement, clicks and scrolling always cancel. Typing normally cancels too. Alt+Tab, opening the switcher, closed origins and display/lock/sleep changes discard stale returns. After an environment change, run Restart test before automatic return resumes. Exact executable exclusions can disable return for selected applications.

Focus restoration is a one-shot request verified against the saved window identity. When Windows rejects it, no cursor jump follows in focus+cursor mode. The provider does not repeatedly steal focus or unclamp an application's cursor. A minimised saved window receives a normal restore request but no cursor jump; a full asynchronous minimise/restore transition is not treated as verified immediate focus success.

## Detection-only / unavailable

Do not enable automatic return merely because touching the display moves the mouse. This provider uses passive Raw Input HID reports and a matching pre-delivery mouse-promotion anchor. It does not use a global hidden WM_POINTER listener or redirect all pointer messages into Taskbar Tiles.

Automatic return is unavailable for incomplete/unknown contact formats, ambiguous device associations, virtual mouse-only paths, unavailable required pen hover, or pre-touch snapshots that cannot be verified. The first provider intentionally excludes an origin on the same input monitor. If the target was already active, it does not search older focus history for an unrelated destination.

Spacedesk and Apollo/Moonlight must be tested separately; one succeeding does not prove the other. No automatic fallback converts drawing into mouse input. Missing reports stop return rather than guessing that fingers have lifted. Diagnostics are local and do not record screenshots, keystrokes, search queries or click coordinates.
