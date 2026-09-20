# Touch screen monitor support

## Current release scope

Version 0.7.7 provides the input-detection milestone only. It does not yet automatically return focus or the cursor after touchscreen interaction. Do not treat an increasing promoted-touch event count as proof of safe global multitouch detection.

## Run the compatibility test

In Settings, open **Touch screen monitor support**, select the intended monitor, and choose **Open input test**. Tap in the blank drawing area; hold two fingers, release one, then release the other. Draw and hover with the pen. While leaving the test open, interact with a normal application on that display, then use the physical mouse for movement, clicks and scrolling. Copy the report. Repeat separately for another remoting/input path such as Apollo. No data is uploaded by the test.

The report separates native pointer observations in the test's own window from passive mouse-source signatures and raw digitizer report availability. The latter currently reports counts/byte sizes, not decoded contacts. Native pressure/hover in our canvas is not proof of pressure/hover observation across other applications. Unmarked input stays unclassified rather than being declared a physical mouse.

The selected monitor is remembered on Apply using the available device key. A missing/ambiguous match stays disconnected instead of silently selecting a different screen. This is a trigger-screen choice, not a fixed return destination. The test pauses Taskbar Tiles' Alt+Tab interception and Settings click-away until the test closes, and times out after five minutes.

## Required subsequent acceptance gates

Before enabling automatic return, validate a non-intercepting cross-application source for all contacts and hover, pre-touch foreground/cursor state, and physical-mouse cancellation on the actual device/driver. Preserve every input event and existing pressure/tilt/eraser/palm-rejection behaviour. Unknown or incomplete data must leave automatic return unarmed.

The requested eventual behaviour is one session with a saved previous window and cursor; wait until all contacts end, extend/reset idle on further input, use the longer applicable touch/pen delay, provide Stay here, and cancel on deliberate mouse/keyboard navigation or stale displays/windows. Suggested initial delays are touch 1,000 ms and pen 2,000 ms, not measured optimums. No synthetic click, forced cursor unclipping, repeated foreground-stealing loop or app-specific game rule is acceptable.

## Technical references

- Microsoft LowLevelKeyboardProc: https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
- Microsoft GetPointerInfo ownership restrictions: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getpointerinfo
- Microsoft System Events and Mouse Messages: https://learn.microsoft.com/en-us/windows/win32/tablet/system-events-and-mouse-messages
- Microsoft Raw Input overview: https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input
- Microsoft RAWINPUTDEVICE flags: https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice
