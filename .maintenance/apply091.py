from pathlib import Path

root = Path('.')
def edit(name, old, new):
    p = root / name
    text = p.read_text(encoding='utf-8-sig')
    if text.count(old) != 1:
        raise RuntimeError(f'Expected one exact source anchor in {name}: {old[:100]!r}; got {text.count(old)}')
    p.write_text(text.replace(old, new), encoding='utf-8', newline='\n')

main = 'src/TaskbarTiles/TaskbarTiles.cs'
launch = 'src/TaskbarTiles/LaunchReliability.cs'
inventory = 'src/TaskbarTiles/LauncherInventory.cs'

edit(main, '        public static string WindowAppId(IntPtr h)\n        {\n            IPropertyStore store = null;',
    '        public static string WindowAppId(IntPtr h) { return WindowProperty(h, "System.AppUserModel.ID"); }\n        public static string WindowProperty(IntPtr h, string name)\n        {\n            IPropertyStore store = null;')
edit(main, '                return ReadProperty(store, "System.AppUserModel.ID");', '                return ReadProperty(store, name);')
edit(launch, '        internal bool RequireNewWindow;', '        internal bool RequireNewWindow;\n        internal string ReopenClient = ""; // Captured, verified installation for explicit UI reopening.')
edit(launch, '            if (LaunchResolution.ProfileScoped(requested)) return false;',
    '            if (LaunchResolution.ProfileScoped(requested)) return false;\n            if (SteamReopen.MatchesWindow(receipt, w)) return true;')
edit(launch,
    '                    if (settings.TerminalNewWindow && TryTerminal(app, target, "", receipt, ref start)) receipt.Method = "Terminal new window";\n                    else receipt.Method = "shell target";',
    '                    if (settings.TerminalNewWindow && TryTerminal(app, target, "", receipt, ref start)) receipt.Method = "Terminal new window";\n                    else if (!SteamReopen.TryPrepare(app, target, "", receipt, ref start)) receipt.Method = "shell target";')
edit(launch,
    '            if (settings.TerminalNewWindow && TryTerminal(app, entry.ExpandedTarget, entry.Arguments, receipt, ref start)) receipt.Method = "Terminal new window";\n            Dispatch(start, receipt, operation, completed);',
    '            if (settings.TerminalNewWindow && TryTerminal(app, entry.ExpandedTarget, entry.Arguments, receipt, ref start)) receipt.Method = "Terminal new window";\n            else SteamReopen.TryPrepare(app, entry.ExpandedTarget, entry.Arguments, receipt, ref start);\n            Dispatch(start, receipt, operation, completed);')
edit(launch, 'Method = "taskbar Shift+click"', 'Method = "taskbar default action"')
edit(main,
    '                    if (operation.Cancelled) return;\n                    // Re-resolve the button before clicking: never trust old coordinates.',
    '                    if (operation.Cancelled) return;\n                    string defaultError;\n                    if (TaskbarDefaultAction.TryInvoke(original, operation, out defaultError)) { completed(defaultError); return; }\n                    // Only an unavailable pre-dispatch action reaches the visible click fallback.\n                    // Re-resolve the button before clicking: never trust old coordinates.')
edit(main, '                    Native.ShiftClick(target, item.Taskbar);', '                    Native.ShiftClick(target, item.Taskbar, false); // Native default click, not force-new Shift+click.')
edit(main, '        public static void ShiftClick(POINT target, IntPtr expectedTaskbar)', '        public static void ShiftClick(POINT target, IntPtr expectedTaskbar, bool newInstance = true)')
edit(main,
    '                shiftAttempted = true;\n                var prepare = new[] { Key(0xA0, false), Mouse(0, target, true) };',
    '                shiftAttempted = newInstance;\n                var prepare = newInstance ? new[] { Key(0xA0, false), Mouse(0, target, true) } : new[] { Mouse(0, target, true) };')
edit(inventory,
    '            if (window == null || window.IdentityAmbiguous) return null;\n            string exe = window.Exe ?? "", id = LaunchIdentity.CleanId(window.AppId), target = "", name = "";',
    '            if (window == null || window.IdentityAmbiguous) return null;\n            var declared = WindowRelaunch.Read(window);\n            if (declared != null) return declared;\n            var client = SteamReopen.FromWindow(window);\n            if (client != null) return client;\n            string exe = window.Exe ?? "", id = LaunchIdentity.CleanId(window.AppId), target = "", name = "";')
edit(inventory,
    'var app = FavouriteLaunch.AsApp(entry); app.VerifiedShortcut = true; app.LaunchExe = key.Exe; app.LauncherIdentity = key; pins.Add(app);',
    'var app = FavouriteLaunch.AsApp(entry); app.ClassName = "TaskbarPinnedShortcut"; app.VerifiedShortcut = true; app.LaunchExe = key.Exe; app.LauncherIdentity = key; pins.Add(app);')
edit(inventory,
    '                var app = FavouriteLaunch.AsApp(entry); app.LaunchExe = window.Exe;\n                app.LauncherIdentity = new LauncherKey { AppId = window.AppId, Exe = window.Exe, Target = entry.ExpandedTarget };',
    '                var app = FavouriteLaunch.AsApp(entry);\n                app.LauncherIdentity = LauncherKey.FromEntry(entry, true);\n                app.LaunchExe = app.LauncherIdentity.Exe; // Keep the app relauncher, not its UI helper.')
edit('src/TaskbarTiles/RecentApps.cs',
    '                var identity = LauncherKey.FromEntry(entry, true); identity.Exe = w.Exe ?? identity.Exe;',
    '                var identity = LauncherKey.FromEntry(entry, true);\n                if (string.IsNullOrWhiteSpace(identity.Exe)) identity.Exe = w.Exe ?? "";')
edit(main,
    '            if (args.Contains("--self-test")) { Environment.Exit(SelfTests.Run()); return; }',
    '            if (args.Contains("--test-reopen-target")) { Environment.Exit(AppReopenTests.Fixture(args)); return; }\n            if (args.Contains("--test-app-reopen")) { Environment.Exit(AppReopenTests.RunNative()); return; }\n            if (args.Contains("--self-test")) { Environment.Exit(SelfTests.Run()); return; }')
edit(main, '                LaunchReliabilityTests.Run(log);', '                AppReopenTests.Run(log);\n                LaunchReliabilityTests.Run(log);')
edit('src/TaskbarTiles/TaskbarTiles.csproj',
    '    <Compile Include="LaunchReliability.cs" />',
    '    <Compile Include="AppReopen.cs" />\n    <Compile Include="AppReopenTests.cs" />\n    <Compile Include="TaskbarDefaultAction.cs" />\n    <Compile Include="LaunchReliability.cs" />')
# Do not skip machine-wide installation discovery when the user-specific key is absent.
edit('src/TaskbarTiles/AppReopen.cs',
    '''                        if (key == null) continue;
                        paths.Add(Convert.ToString(key.GetValue("SteamExe", "")));
                        string dir = Convert.ToString(key.GetValue("SteamPath", ""));
                        if (!string.IsNullOrWhiteSpace(dir)) paths.Add(Path.Combine(dir, "steam.exe"));''',
    '''                        if (key != null)
                        {
                            paths.Add(Convert.ToString(key.GetValue("SteamExe", "")));
                            string dir = Convert.ToString(key.GetValue("SteamPath", ""));
                            if (!string.IsNullOrWhiteSpace(dir)) paths.Add(Path.Combine(dir, "steam.exe"));
                        }''')
for name in [main, 'src/TaskbarTiles/AssemblyInfo.cs', 'version.txt']:
    p = root / name
    text = p.read_text(encoding='utf-8-sig')
    if '0.9.0' not in text:
        raise RuntimeError('Unexpected version in ' + name)
    p.write_text(text.replace('0.9.0', '0.9.1'), encoding='utf-8', newline='\n')
edit('CHANGELOG.md', '# Changelog\n', '# Changelog\n\n## 0.9.1\n\n- Reopen ordinary Steam entries through the registered client and its open-main request, including helper-hosted and tray-hidden UI; preserve explicit game/account/custom commands.\n- Prefer paired app-authored taskbar relaunch metadata to a bare UI-process executable. Keep the relauncher identity in taskbar fallback and recent apps.\n- Use Explorer default Invoke actions in the compatibility fallback, with a normal visible click only when Invoke is unavailable before dispatch. No forced Shift/new-instance action or repeat after an ambiguous dispatch.\n- Add policy and native hidden-window/default-action regressions; retain the Stream Dock work on its separate branch and all existing settings.\n')
edit('README.md', '## Licence\n', '''## Tray-hidden application reopening

Ordinary Steam launcher entries ask the registered Steam client to show its main UI, including when it is already running in the notification area. The renderer/helper process is not launched in isolation. Explicit favourite arguments, game shortcuts and account-related commands are retained; no Steam setting or running game is changed.

Taskbar fallback discovery prefers an application's own paired relaunch metadata. When a verified launch target is unavailable, the compatibility path tries Explorer's exact app-button default action rather than assuming Shift+click/new-instance. A physical fallback still requires a freshly visible, unobstructed taskbar button. Unsupported or uncertain actions are not automatically repeated. Apps with a normal verified shortcut keep their own single/multiple-instance policy. This is not a universal tray-protocol emulation: applications can implement their own activation behaviour.

## Licence
''')
print('0.9.1 source edits applied. Windows compilation, native tests and release gates must pass; no production branch or local install changed.')
