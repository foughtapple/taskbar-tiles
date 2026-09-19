from pathlib import Path
import re

def edit(path, before, after):
    p = Path(path); text = p.read_text(encoding='utf-8-sig')
    if text.count(before) != 1:
        raise RuntimeError(f'{path}: expected one source anchor: {before[:100]!r}, got {text.count(before)}')
    p.write_text(text.replace(before, after), encoding='utf-8', newline='\n')

root='src/TaskbarTiles/'
edit(root+'TaskbarTiles.cs', 'internal const string Version = "0.7.3";', 'internal const string Version = "0.7.4";')
edit(root+'TaskbarTiles.cs', '// Taskbar Tiles 0.7.3 - Windows utility.', '// Taskbar Tiles 0.7.4 - Windows utility.')
edit(root+'TaskbarTiles.cs', '            if (args.Contains("--self-test"))', '''            if (args.Contains("--test-launch-fixture")) { Environment.Exit(LaunchOutcomeTests.Fixture(args)); return; }
            if (args.Contains("--test-launch-outcome")) { Environment.Exit(LaunchOutcomeTests.RunNative()); return; }
            if (args.Contains("--self-test"))''')
edit(root+'TaskbarTiles.cs', '                LaunchReliabilityTests.Run(log);', '                LaunchReliabilityTests.Run(log);\n                LaunchOutcomeTests.Run(log);')
edit(root+'TaskbarTiles.cs', '''            LaunchPlacement tracking = null;
            if (destination != null)
''', '''            // Track ordinary launches too: Shell acceptance is not foreground success.
            LaunchPlacement tracking = null;
''')
edit(root+'TaskbarTiles.cs', '''                        var old = launchPlacement; launchPlacement = null; if (old != null) old.Dispose();
                        if (closing) return;
                        if (error != null) Notify(error);
                        else if (window != IntPtr.Zero) MoveTo(window, destination, false);
                    });''', '''                        if (!ReferenceEquals(launchPlacement, tracking)) return;
                        var resolved = tracking.SelectedWindow;
                        launchPlacement = null; tracking.Dispose();
                        if (closing) return;
                        if (error != null) Notify(error);
                        else if (window != IntPtr.Zero && resolved != null)
                        {
                            if (destination != null) MoveTo(window, destination, false);
                            else ActivateWindow(new WindowItem { Handle = window, ProcessId = resolved.ProcessId, Title = resolved.Title });
                        }
                    }, destination != null);''')
edit(root+'TaskbarTiles.cs', '''        public void ToggleMenu()
        {
''', '''        public void ToggleMenu()
        {
            CancelPassiveLaunchObservation();
''')
edit(root+'TaskbarTiles.cs', '''        void OpenOrCycle(bool forceSticky, bool reverse, bool minimiseFullscreen = true)
        {
''', '''        void OpenOrCycle(bool forceSticky, bool reverse, bool minimiseFullscreen = true)
        {
            CancelPassiveLaunchObservation();
''')
for name, anchor in [
    ('Navigation.cs', '        void QueueLaunch(AppButton app, ZoneDestination destination)\n        {\n'),
    ('Navigation.cs', '        void ChooseZone(WindowItem window, AppButton app)\n        {\n'),
    ('Navigation.cs', '        void ShowSettings()\n        {\n'),
    ('WindowActivation.cs', '        void ActivateWindow(WindowItem item)\n        {\n'),
    ('Updates.cs', '        void ShowUpdates()\n        {\n')]:
    edit(root+name, anchor, anchor+'            CancelPassiveLaunchObservation();\n')
p=Path(root+'WindowPlacement.cs'); text=p.read_text(encoding='utf-8-sig')
start='    sealed class LaunchPlacement : IDisposable\n'; end='    sealed class WindowChoiceWindow : Form\n'
if text.count(start)!=1 or text.count(end)!=1: raise RuntimeError('LaunchPlacement class markers changed')
a=text.index(start); b=text.index(end,a)
p.write_text(text[:a]+text[b:],encoding='utf-8',newline='\n')
edit(root+'Settings.cs', 'public int ConfigVersion = 7;', 'public int ConfigVersion = 8;')
edit(root+'Settings.cs', 'public bool TerminalNewWindow = true;', 'public bool TerminalNewWindow = false; // App-managed windows unless the user explicitly opts in.')
p=Path(root+'Settings.cs'); text=p.read_text(encoding='utf-8-sig')
if text.count('o.ConfigVersion = Math.Max(7, o.ConfigVersion);') != 2: raise RuntimeError('schema upgrade anchors changed')
text=text.replace('o.ConfigVersion = Math.Max(7, o.ConfigVersion);','o.ConfigVersion = Math.Max(8, o.ConfigVersion);')
p.write_text(text,encoding='utf-8',newline='\n')
edit(root+'Settings.cs', '''            if (StoredVersion(lines) < 7) { o.WindowTitleFontSize = 22; o.AppLabelFontSize = 22; }
''', '''            if (StoredVersion(lines) < 7) { o.WindowTitleFontSize = 22; o.AppLabelFontSize = 22; }
            // Replace our historical forced-new default once; never change an app's
            // configuration or a user's explicit shortcut command-line arguments.
            if (StoredVersion(lines) < 8) o.TerminalNewWindow = false;
''')
edit(root+'Settings.cs', 'if (StoredVersion(raw) >= 7) return;', 'if (StoredVersion(raw) >= 8) return;')
edit(root+'Settings.cs', 'FilePath + ".pre-v062"', 'FilePath + ".pre-v074"')
edit(root+'Settings.cs', 'Check(navigation, "TerminalNewWindow", "Open plain Terminal launchers in a new window");', 'Check(navigation, "TerminalNewWindow", "Override Terminal settings: always request a new window (optional)");')
edit(root+'Settings.cs', 'Check(navigation, "ReuseSingleInstance", "Move a matching existing window if an app reuses it");', 'Check(navigation, "ReuseSingleInstance", "Bring forward an existing window when the app reuses it");')
edit(root+'Settings.cs', '''Number(navigation, "LaunchTimeoutSeconds", "Wait for a new app window", "Seconds. Waits for a stable new window before considering reuse. Ambiguous matches let you refresh, wait longer or choose.", 5, 60, 5);''', '''Number(navigation, "LaunchTimeoutSeconds", "Wait for the app's launch result", "Seconds. New windows take priority; at this deadline a single verified existing window may be restored. Zone ambiguity asks you to choose. Ordinary launch observation never blocks your next action.", 5, 60, 5);''')
edit(root+'Settings.cs', 'Section(navigation, "Window placement", "Right-click selects a destination; ordinary left-click behaviour is unchanged.");', 'Section(navigation, "Launching and window placement", "Each click sends one normal launch request. The application decides whether to create a window or reuse one; its own settings and your shortcut arguments are respected.");')
p=Path(root+'SettingsHelp.cs'); text=p.read_text(encoding='utf-8-sig')
replacements={
'TerminalNewWindow':'Off by default: let Terminal decide whether to create a window or reuse an existing one, like other apps. Enabling this optional override requests -w new for plain recognised Terminal launchers only. Explicit shortcut/favourite arguments are never replaced.',
'ReuseSingleInstance':'Send the original launch request even when the app is already running. Prefer the new window it creates; if it reuses a window, bring that window forward instead. A single verified old window can be restored after the wait even if it stayed covered or minimised. No list of single-instance app names is used. Disable to require deliberate selection for old-window zone placement.'}
for key,value in replacements.items():
    pattern=r'(\{ "'+key+r'", ")[^"\n]*(" \},)'
    text,n=re.subn(pattern,lambda m:m.group(1)+value+m.group(2),text)
    if n!=1: raise RuntimeError('setting help anchor '+key)
p.write_text(text,encoding='utf-8',newline='\n')
edit(root+'InterfacePolishTests.cs','upgrade.ConfigVersion == 7','upgrade.ConfigVersion == 8')
edit(root+'LaunchReliabilityTests.cs','Check(defaults.DirectAppLaunch && defaults.TerminalNewWindow,"launch reliability defaults on");','Check(defaults.DirectAppLaunch && !defaults.TerminalNewWindow,"direct launch enabled without forcing app window policy");')
edit(root+'TaskbarTiles.csproj','    <Compile Include="LaunchReliability.cs" />','    <Compile Include="LaunchReliability.cs" />\n    <Compile Include="LaunchOutcome.cs" />\n    <Compile Include="LaunchOutcomeTests.cs" />')
p=Path(root+'AssemblyInfo.cs'); text=p.read_text(encoding='utf-8-sig')
if text.count('0.7.3')!=3: raise RuntimeError('Assembly version markers')
p.write_text(text.replace('0.7.3','0.7.4'),encoding='utf-8',newline='\n')
edit(root+'app.manifest','version="0.7.3.0"','version="0.7.4.0"')
edit('version.txt','0.7.3','0.7.4')
edit('config/settings.ini','ConfigVersion=7','ConfigVersion=8')
edit('config/settings.ini','TerminalNewWindow=true','TerminalNewWindow=false')
edit('config/settings.ini','# Plain Terminal entries request a new window. User-supplied arguments are untouched.','# Optional Terminal-only override, off by default. App settings and explicit arguments decide window behaviour.')
edit('CHANGELOG.md','# Changelog\n\n## 0.7.3', '''# Changelog

## 0.7.4

- Observe normal launches as well as zone launches. Send one request and honour whichever new or reused window the app actually exposes.
- Prefer stable new windows, including a new HWND in an existing process. After the bounded wait, restore a single verified existing window even if it never came to the foreground.
- Keep profile/identity checks, ambiguity safeguards and exact-window activation. No product-name list, app-setting edits or duplicate launch attempts.
- Let new navigation supersede passive launch observation; no modal chooser for ordinary shortcuts and no delayed focus steal after the user changes apps.
- Turn the old forced-new Terminal default off once on upgrade; keep an explicit optional override and preserve custom shortcut arguments and other settings.
- Add native end-to-end fixture tests that change single/multiple-instance preference between requests, exercise minimised-window reuse and delayed new-window placement.

## 0.7.3''')
print('0.7.4 source edits applied. Windows compilation and native launch tests still required.')
