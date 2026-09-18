// Centralised, plain-language help. Tips are shown on labels as well as inputs,
// including numeric spinners, so disabled dependent settings remain explainable.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class SettingsHelp
    {
        static readonly Dictionary<string, string> hints = new Dictionary<string, string> {
            { "MinimizeFullscreenOnOpen", "Opening with Alt+Tab, Ctrl+Alt+Space or your X-Mouse command sends a normal minimise request to the foreground fullscreen app. Only that foreground app is affected. Ordinary maximised title-bar windows are left alone.\n\nThe app stays running and can be restored from its preview. Some games may briefly change display modes or refuse the request. No F11, forced shutdown or process injection is used. Turning this off restores overlay-only behaviour." },
            { "ShowAppLabels", "Show names beneath the square taskbar app tiles. Turn off for an icon-only strip; hover still identifies the app." },
            { "ShowLivePreviews", "Show live Windows-composited thumbnails of open windows. Turning this off leaves labelled cards and avoids live thumbnail rendering. Some minimised or protected apps cannot supply a preview." },
            { "ShowMonitorBadges", "Add the monitor number to each window card. This identifies the window's current screen, not a move destination." },
            { "QuickSizeButtons", "Show separate minus/plus controls for window previews and app tiles. They save size changes immediately; Ctrl+mouse-wheel over a section also changes its size." },
            { "EnableSearch", "Show the narrow filter at the top of the main menu. It filters open-window titles and visible taskbar app names only. It is separate from the broader bottom-left Search launcher." },
            { "CurrentMonitorOnly", "Limit the main open-window grid to the monitor where you open Taskbar Tiles. The broader Search launcher can still find windows across monitors." },
            { "ShowCloseButtons", "Show an X in the top-right of every window card. It sends a normal close request, so Save / Don't save / Cancel prompts are respected. It never force-kills an app." },
            { "HideOnFocusLoss", "Clicking another app or the desktop dismisses the menu and Settings, including its previews and pickers, without saving unapplied changes. It does not reopen the menu. Switching between Settings and its own dialogs is not an outside click. Changes already saved with Apply stay saved. Turn off and Apply to keep Settings open while using other apps." },
            { "ShowWindowTitleIcons", "Show the app's icon immediately before each open-window title. This does not change taskbar launcher icons or the close X. Icons load in the background; an unavailable icon uses a neutral window symbol." },
            { "WindowTitleIconSize", "Size of the icon before an open-window title, in logical pixels before Windows display scaling. Default 28, range 12-64. Independent of title text and launcher icon sizes. The header expands to avoid the preview or close X. The live preview shows the result." },
            { "StickyAltTab", "Leave Taskbar Tiles open after Alt is released. Useful for pressing your mouse button once and then clicking a result. Turn off to accept the highlighted window on Alt release." },
            { "InterceptAltTab", "Use Taskbar Tiles instead of the normal Alt+Tab switcher while this app is running. Turning this off restores native Alt+Tab; your X-Mouse Run Application command and Ctrl+Alt+Space still work." },
            { "RightClickZones", "Right-click a window, taskbar tile, favourite or Search result to choose a monitor/zone. Left-click keeps its normal switch/open behaviour. This is not a Windows context menu." },
            { "KeepOpenAfterMove", "After placing an existing window, reopen the main menu so you can organise another window. Does not continuously reopen while a new app is starting." },
            { "EnableUndoMove", "Remember the previous position of the last moved window. Use the curved-arrow button or Ctrl+Z outside text fields to undo that move. This is a one-step undo, not an application undo history." },
            { "DirectAppLaunch", "Use the verified pinned shortcut or shell app identity, without moving the pointer or requiring a visible taskbar. Shortcut arguments and profiles are kept. Turn off to use the legacy taskbar action. If no verified target exists, the revalidated taskbar action is used; a request is never automatically repeated." },
            { "TerminalNewWindow", "For plain Windows Terminal launchers, request -w new so Terminal cannot silently add a tab to an old window. Custom favourites or shortcuts with arguments are left exactly as configured. Requires a recognised Terminal executable or package; a similarly named app is not enough." },
            { "ReuseSingleInstance", "After the new-window wait expires, allow reuse only of a matching window that became foreground after launch and stayed there. New Terminal-window requests never reuse an old window automatically. Otherwise you choose; no unrelated window is guessed." },
            { "ShowZoneNumbers", "Draw numbers inside the monitor's zones. The numbers identify destinations within that monitor and do not alter the saved FancyZones layout." },
            { "ShowZoneButtons", "Add numbered targets below each monitor, making overlapping or nested zones easier to select. The whole map still fits without scrolling." },
            { "ReadFancyZones", "Read PowerToys' saved layout geometry to display zones. No PowerToys configuration or zone-history file is modified. Disable to use your selected basic/full-screen layout." },
            { "RespectZoneSpacing", "Use the saved FancyZones spacing when calculating each destination rectangle. Extra zone inset, if set, is then applied in addition." },
            { "ShowBasicFallback", "Offer clearly labelled Basic zones when no usable FancyZones layout is found. Disable to offer full-screen placement only in that case." },
            { "WindowsSearchButton", "Show Search in the bottom-left of Taskbar Tiles. Clicking expands a local icon-and-name search panel inside this window. It never sends Win+S or opens the Windows Search interface. Choose its sources on the Search tab." },
            { "FavouritesButton", "Show your custom launcher in the bottom-right. Populate it on the Favourites tab; it is separate from the installed app catalogue and the taskbar strip." },
            { "DesktopButton", "Show a button that sends Windows+D to show/hide the desktop. This uses the standard Windows desktop action, not a search function." },
            { "ClipboardButton", "Show a button that sends Windows+V. Windows may ask you to enable clipboard history. Taskbar Tiles does not read or retain clipboard contents." },
            { "LocalHotkeys", "Enable Ctrl+Space for Favourites, Ctrl+Shift+S for integrated Search, Ctrl+, for Settings, and placement/undo shortcuts while Taskbar Tiles has focus. Other applications' shortcuts are not changed." },
            { "MiddleClickClose", "Middle-click a window card to close that window normally. Disabled by default to prevent accidental closes. App tiles are not closed by this action." },
            { "LiveSettingsPreview", "Update the preview while you edit without applying or saving changes. Main-menu window contents are illustrative; Search preview never queries the index. Apply commits edits; Cancel discards unapplied edits." },
            { "FavouriteGroups", "Show a group selector when your enabled favourites span multiple groups. Group names are set in the entry editor; they do not move or modify installed apps." },
            { "FavouriteDetails", "Show a smaller group or website label beneath each favourite's name. Full targets remain available by hovering." },
            { "FavouriteAlphabetical", "Display favourites alphabetically. Your saved custom order is retained and becomes visible again when this is turned off." },
            { "FavouriteNumberKeys", "Show Alt+1 through Alt+9 beside the first nine visible favourites and allow those keys to open them. Numbers apply to the current page only." },
            { "SearchInstalledApps", "Search Start-menu shortcuts, the Windows installed-app catalogue and the visible taskbar apps. The catalogue is read locally and cached for five minutes. Refresh updates it sooner. Portable apps without shortcuts may need adding to Favourites." },
            { "SearchOpenWindows", "Include currently open windows across monitors. Clicking one switches to that existing window; it does not launch another instance." },
            { "SearchFavourites", "Include enabled favourites, plus quick entries for Documents, Downloads, Desktop and Pictures. Disabled favourites stay hidden." },
            { "SearchSettings", "Include a curated set of common Windows Settings pages, such as Display, Sound, Bluetooth and Windows Update. This is not the complete Windows Settings search catalogue." },
            { "SearchIndexedFiles", "Search local filenames already present in the Windows Search index after two or more characters. No folder crawl, content search, web search or index rebuild is performed. Results depend on Windows indexing and permissions. If unavailable, apps and other search sources still work." }
        };
        internal static string For(string key)
        { string text; return hints.TryGetValue(key, out text) ? text : "Change this preference in the draft. Check the preview, then Apply to save or Cancel to discard unapplied changes."; }
        internal static string Action(string title)
        {
            switch ((title ?? "").Replace("&&", "&").Trim().TrimEnd('.', '…'))
            {
                case "Apply": return "Save all current settings and favourites, update the tray app, and keep Settings open. Later Cancel does not undo changes already applied.";
                case "Save & close": return "Apply the current settings and favourites, then close Settings.";
                case "Cancel": return "Close Settings without saving unapplied edits. Any changes already saved using Apply remain.";
                case "Open launch diagnostics": return "Open the local launch log. Records request stage, matching counts and error codes, but not command arguments or window titles. Nothing is uploaded. The file rotates at 512 KB.";
                case "Restore defaults": return "Reset appearance/navigation draft values to defaults after confirmation. Keep startup, monitor mappings and your favourites list. Nothing is saved until Apply.";
                case "Installed apps": return "Choose apps from local Start-menu shortcuts and the Windows app catalogue. Select entries and add them to your draft favourites.";
                case "From taskbar": return "Add favourites from currently visible taskbar apps. Prefer Installed apps or a shortcut when a taskbar entry has no reusable launch target.";
                case "Add shortcut": return "Create an app, file or website entry. You can set a friendly name, arguments, working folder and optional custom icon.";
                case "Add folder": return "Pick a folder to add to Favourites. Opening it uses File Explorer.";
                case "Edit": return "Edit the selected favourite's name, target, group, arguments or icon. Double-clicking its list row also opens the editor.";
                case "Remove": return "Remove the selected favourite after confirmation. Does not uninstall the app or delete the target file.";
                case "Up": return "Move the selected favourite one place earlier in the custom order. Turn alphabetical sorting off to use this order in the launcher.";
                case "Down": return "Move the selected favourite one place later in the custom order. Turn alphabetical sorting off to use this order in the launcher.";
                case "Import list": return "Merge a Taskbar Tiles favourites JSON export into this draft. Exact duplicates are skipped. Apply saves the merged list.";
                case "Export list": return "Write the current favourites draft to a JSON backup file you choose. Export does not apply unrelated settings.";
                case "Full size": return "Open a larger read-only preview using the draft settings. Preview rows and zones cannot launch, close or move applications.";
                case "Refresh": return "Regenerate the preview from the current draft, including when Live preview is paused. No changes are saved.";
                case "Browse folder": return "Choose the folder containing your saved PowerToys FancyZones JSON files. Leave the setting blank for the normal location.";
                case "Copy X-Mouse command": return "Copy the command for X-Mouse's Run Application action. It toggles the already-running tray app; it does not simulate Alt+Tab.";
                case "Windows display settings": return "Open Windows Display settings to check monitor numbers, arrangement, resolution and scaling.";
                case "Write zone diagnostics": return "Write a local diagnostic report of monitors and imported zones, then open it in Notepad. Nothing is uploaded.";
                case "Open app folder": return "Open the local Taskbar Tiles installation folder containing source, settings, logs and rollback files.";
                case "Start Taskbar Tiles when I sign in": return "Create or remove a shortcut in your user Startup folder when Apply is clicked. No administrator service or scheduled task is installed.";
                default: return "Review the draft and its preview. Apply saves changes; Cancel leaves unapplied changes unsaved.";
            }
        }
        internal static void Tree(ToolTip tips, Control root, string text)
        {
            tips.SetToolTip(root, text); root.AccessibleDescription = text;
            foreach (Control child in root.Controls) Tree(tips, child, text);
        }
    }
    sealed partial class SettingsWindow
    {
        readonly ToolTip settingHints = new ToolTip { ShowAlways = true, InitialDelay = 550, ReshowDelay = 100, AutoPopDelay = 24000 };
        internal Func<string> LayoutSummary;
        void HintTree(Control root, string text) { SettingsHelp.Tree(settingHints, root, text); }
        void InstallSettingHints()
        {
            tabs.ShowToolTips = true;
            foreach (TabPage page in tabs.TabPages)
                page.ToolTipText = page.Text == "Appearance" ? "Independent tile and text sizes, maximum columns/rows, automatic page capacity and balanced rows." :
                    page.Text == "Search" ? "Sources and size for Search inside Taskbar Tiles. No Windows Search popup." :
                    page.Text == "Favourites" ? "Build, organise and preview your custom app-and-folder launcher." :
                    page.Text == "Navigation" ? "Window switching, closing, filtering and placement behaviour." :
                    page.Text == "Screens & zones" ? "Monitor picker size, full-screen buttons and zone geometry." :
                    page.Text == "Monitor layouts" ? "Choose the layout for each monitor without changing PowerToys files." :
                    page.Text == "Quick access" ? "Bottom-bar buttons, local keyboard shortcuts and live preview." : "Startup, diagnostics and X-Mouse setup.";
            HintTree(startup, "Start the tray app when you sign in. Apply saves this choice; no administrator service is installed.");
            if (favouritesList != null) HintTree(favouritesList, "Tick entries to show them. Unticking hides without removing. Select a row to Edit / Remove / Move it; double-click to edit. Apply saves your list.");
            FillHints(this);
        }
        void FillHints(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Button && string.IsNullOrEmpty(settingHints.GetToolTip(c))) HintTree(c, SettingsHelp.Action(c.Text));
                if (c is ComboBox && c == previewView) HintTree(c, "Choose what to preview: main menu, favourites, monitor map or integrated Search. The preview uses draft settings and never launches anything.");
                FillHints(c);
            }
        }
        void UpdatePageControls()
        {
            Control cols, rows;
            if (pageCapacityLabel == null || !fields.TryGetValue("WindowColumns", out cols) || !fields.TryGetValue("WindowRows", out rows)) return;
            int c = (int)((NumericUpDown)cols).Value, r = (int)((NumericUpDown)rows).Value;
            pageCapacityLabel.Text = c + " columns x " + r + " rows = up to " + (c * r) + " windows per page";
        }
        void AddSearchSettingsPage()
        {
            var p = Page("Search"); var tab = (TabPage)p.Parent; tabs.TabPages.Remove(tab); tabs.TabPages.Insert(3, tab);
            Section(p, "Search inside Taskbar Tiles", "Click Search at the bottom-left to expand an icon-and-name search surface in the same window. It does not open the Windows Search UI or search the web.");
            Check(p, "SearchInstalledApps", "Search installed apps and taskbar apps");
            Check(p, "SearchOpenWindows", "Search existing open windows");
            Check(p, "SearchFavourites", "Search enabled favourites and common folders");
            Check(p, "SearchSettings", "Search common Windows Settings pages");
            Check(p, "SearchIndexedFiles", "Search indexed local filenames (Windows indexing required)");
            Section(p, "Search panel appearance", "The panel expands upwards from the bottom-left box and stays inside the main window. Extra results use pages; the monitor picker is unchanged.");
            Number(p, "SearchPanelWidth", "Search panel width", "Preferred logical pixels. Limited by the main menu's current width.", 420, 1100, 20);
            Number(p, "SearchVisibleRows", "Preferred visible result rows", "The number of icon-and-name rows to show. Reduces only when the main window cannot fit them.", 3, 12, 1);
            Number(p, "SearchRowHeight", "Search result row height", "Increase for larger mouse targets and icons. More height leaves fewer rows on small screens.", 40, 84, 4);
            Section(p, "Scope and privacy", "Filename matches use the existing local Windows index. Unindexed files, file contents, cloud search and the complete Windows Settings catalogue are not included. Queries are not saved or sent online. App launches only happen when you select a result.");
        }
        string PreviewPageSummary() { return LayoutSummary == null ? "Maximum " + edit.WindowColumns + " columns x " + edit.WindowRows + " rows = " + (edit.WindowColumns * edit.WindowRows) + " windows/page" : LayoutSummary(); }
        Bitmap SearchSnapshot() { using (var panel = new IntegratedSearch(true)) return panel.Snapshot(edit.Clone()); }
    }
}
