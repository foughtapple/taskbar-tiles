from pathlib import Path
B=Path('src/TaskbarTiles')
def edit(path,old,new):
 p=Path(path);s=p.read_text(encoding='utf-8-sig');assert s.count(old)==1,(str(path),old[:100],s.count(old));p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
edit(B/'SettingsHelp.cs','        static readonly Dictionary<string, string> hints = new Dictionary<string, string> {','''        static readonly Dictionary<string, string> hints = new Dictionary<string, string> {
            { "TouchSupportEnabled", "Opt in to automatic return only for explicitly associated, tested touchscreen devices. Default off. Run the passive detection test first. Unknown or incomplete input never triggers a return; enabling this alone does not enable every display." },
            { "TouchWaitForHover", "Keep focus while the pen reports that it remains in detection range, including pauses between strokes. This requires actual in-range reports. A pen device without hover information cannot auto-return while this option is enabled; the utility never invents hover state." },
            { "TouchTypingCancels", "Typing cancels the current return point so focus does not jump away while entering text. Physical mouse/trackpad use and deliberate task navigation always cancel independently of this option. Touch Return's own assigned shortcuts are excluded." },
            { "TouchPauseForMenus", "Prevent return while Windows reliably reports an open menu or standard dialog. Some applications draw custom menus that Windows cannot identify. Use Stay here for extended reading, drawing or any task that must keep focus." },
            { "TouchReturnDelayMs", "Milliseconds after all reported fingers have lifted. Subsequent touch restarts the timer; a still-held finger never expires. Default 1000. A monitor can override this value." },
            { "PenReturnDelayMs", "Milliseconds after pen contact and, when enabled and supported, hover range have ended. Default 2000. A mixed touch/pen session uses the longer applicable delay." },
            { "TouchReturnAction", "Return focus and cursor, focus only, or cursor only. No click is synthesised. Cursor clipping is respected. When focus is requested but Windows denies it, the cursor is not moved." },''')
# Keep the brief's diagnostic terminology visible on the new tabs as well.
edit(B/'SettingsHelp.cs','page.Text == "Quick access" ? "Bottom-bar buttons, local keyboard shortcuts and live preview." : "Startup, diagnostics and X-Mouse setup.";','page.Text == "Quick access" ? "Bottom-bar buttons, local keyboard shortcuts and live preview." : page.Text == "Touch screen monitor support" ? "Detection-first Touch Return, monitor associations and safe idle timings." : page.Text == "Shortcut health" ? "Repair Alt+Tab interception and inspect local shortcut diagnostics." : "Startup, diagnostics and X-Mouse setup.";')
print('Specific hover help added for every new boolean and timing/action control.')
