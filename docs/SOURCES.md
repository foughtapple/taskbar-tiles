# Technical references

Reviewed 17 September 2026. Public Microsoft primary sources. Links describe APIs and file formats; this app is not a Microsoft product or official PowerToys plugin.

## Saved layouts and monitor identity

- Microsoft FancyZones documentation: https://learn.microsoft.com/en-us/windows/powertoys/fancyzones
- Saved applied-layout schema: https://github.com/microsoft/PowerToys/blob/main/src/modules/fancyzones/FancyZonesLib/FancyZonesData/AppliedLayouts.h
- Applied-layout parsing: https://github.com/microsoft/PowerToys/blob/main/src/modules/fancyzones/FancyZonesLib/FancyZonesData/AppliedLayouts.cpp
- Custom-layout schema: https://github.com/microsoft/PowerToys/blob/main/src/modules/fancyzones/FancyZonesLib/FancyZonesData/CustomLayouts.h
- Custom-layout parsing: https://github.com/microsoft/PowerToys/blob/main/src/modules/fancyzones/FancyZonesLib/FancyZonesData/CustomLayouts.cpp
- Template/grid/canvas algorithms: https://github.com/microsoft/PowerToys/blob/main/src/modules/fancyzones/FancyZonesLib/LayoutConfigurator.cpp
- Hardware identity: https://github.com/microsoft/PowerToys/blob/main/src/modules/fancyzones/FancyZonesLib/MonitorUtils.cpp
- EnumDisplayDevicesW: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw
- IVirtualDesktopManager: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager

Grid spacing uses the layout's full spacing at outer edges and half spacing at internal cell boundaries. Percentages use denominator 10000. Canvas coordinates are scaled from their saved reference work area. Priority-grid templates follow the inspected native implementation; its 11-zone case currently falls back to ordinary Grid despite an 11-zone template constant being defined.

These saved files and algorithms are version-sensitive. Geometry matching is not equivalent to registering a window in FancyZones' internal membership/history. The app intentionally does not invoke undocumented private snapping messages or write those files.

## Window placement and close requests

- SetWindowPos: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
- GetWindowRect and invisible resize borders: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect
- ShowWindowAsync: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindowasync
- PostMessageW and UIPI restrictions: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew
- GetWindowPlacement: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowplacement
- SetWindowPlacement: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowplacement

App tiles retain v0.2's ordinary taskbar new-instance action. The watcher observes resulting window identities, rather than intercepting processes or injecting into apps. Unknown/ambiguous matching asks the user.

## Attribution

The compatibility algorithms/template constants are adapted from the MIT-licensed PowerToys source. See the accompanying notice for its copyright and permission terms.

Windows structure packing was checked against the Microsoft SDK header (the `rcDevice` field of WINDOWPLACEMENT is guarded by `_MAC`, not part of its desktop Windows ABI): https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/WinUser.h


## 0.3.1 picker presentation

- Microsoft, ScrollableControl.AutoScroll: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.scrollablecontrol.autoscroll . This patch uses a non-scrollable Control and explicitly fits the complete diagram instead of enabling AutoScroll.
- Microsoft, DWMWINDOWATTRIBUTE: https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute . The optional title-bar styling uses documented attributes 20, 35 and 36, ignoring unsupported requests. Window/zone placement code is unchanged.

The fit-all map layout is original code; no third-party UI library or new package dependency was added.

## Version 0.4 additions

- Microsoft Support, **Keyboard shortcuts in Windows**. Windows+S opens Search; Windows+D shows/hides the desktop; Windows+V opens clipboard history. Clipboard history may need enabling in Windows.
  https://support.microsoft.com/en-us/accessibility/windows/keyboard-shortcuts-in-windows
- Microsoft Learn, **SendInput function (winuser.h)**. Input injection is subject to UIPI; existing modifier state is not reset automatically. Version 0.4 waits for modifiers to be released before injecting an intentional native shortcut.
  https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
- Microsoft Learn, **ShellExecuteExW function (shellapi.h)**. Shell-based execution uses the installed handlers and may depend on COM apartment setup. Favourite launches run from the existing STA UI thread via ProcessStartInfo.UseShellExecute.
  https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shellexecuteexw

The favourites collection, live settings preview and bottom action bar are original application code. The optional Windows shortcuts are not a replacement for Windows' native Search/clipboard UI, and the utility does not provide its own search index or clipboard recorder.


## 0.5 additions (Microsoft primary documentation)

- Local Windows Search provider and C# OleDbConnection access; local `FROM SystemIndex` and read-only semantics:
  https://learn.microsoft.com/en-us/previous-versions/windows/desktop/legacy/ff684395(v=vs.85)
- Windows Search SQL LIKE literal matching and bracket-escaped wildcards:
  https://learn.microsoft.com/en-us/windows/win32/search/-search-sql-like
- Windows Search SQL overview and SELECT/TOP:
  https://learn.microsoft.com/en-us/windows/win32/search/-search-sql-ovwofsearchquery
- Supported common Windows Settings URIs (availability varies by Windows version):
  https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings
- Windows Forms tooltips, SetToolTip and timing properties:
  https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.tooltip.settooltip

The implementation uses only bounded local filename queries; it does not reproduce the complete Windows Search UI/service feature set. These sources describe APIs, not evidence that this build ran on Windows.


## 0.6 implementation references (Microsoft primary documentation)

- Control.DeviceDpi: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.control.devicedpi?view=netframework-4.8.1
- TextRenderer.DrawText: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.textrenderer.drawtext
- ShowWindowAsync: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindowasync
- SW_MINIMIZE / SW_RESTORE: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow
- QUERY_USER_NOTIFICATION_STATE: https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ne-shellapi-query_user_notification_state

The API documentation describes the request semantics; it is not proof of successful runtime integration with every game or protected window. Balanced row distribution and compact settings-row geometry are implemented locally, not imported from these sources.


## 0.6.1 launch patch: primary API references

- Windows Terminal CLI, including `--window new` / `-w new`: https://learn.microsoft.com/en-us/windows/terminal/command-line-arguments
- Process AppUserModel identity: https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getapplicationusermodelid
- Enumerate packages in a family for the current user: https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagesbypackagefamily
- Resolve installed package path: https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagepathbyfullname
- ShellExecute return process is optional and may refer to an existing process / activation route: https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfow

These references document the APIs, not a claim that this build has been run on Windows. The conservative matching/settling/cancellation policy is Taskbar Tiles code.


## v0.6.2 header icons and Settings dismissal

Consulted Microsoft documentation:

- WM_GETICON and borrowed window/class icon fallback: https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-geticon
- Form.Deactivate describes loss of form activation, including internal form changes: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.form.deactivate
- WM_ACTIVATEAPP distinguishes application activation and deactivation: https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-activateapp

The implementation checks settled foreground process/owner relationships for the whole Settings session rather than treating every Form.Deactivate event as an outside click. Normal close requests unwind owned dialogs; saving remains explicit. API documentation is not evidence that these flows were run on Windows.
