from pathlib import Path

def edit(path, old, new):
    p=Path(path); s=p.read_text(encoding='utf-8-sig')
    assert s.count(old)==1, (path, old[:100], s.count(old))
    p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
main='src/TaskbarTiles/TaskbarTiles.cs'
p=Path(main); s=p.read_text(encoding='utf-8-sig'); start=s.index('    sealed class KeyboardHook : IDisposable'); end=s.index('    sealed class WindowItem',start)
s=s[:start]+s[end:]; p.write_text(s,encoding='utf-8',newline='\n')
edit(main,'new KeyboardHook()', 'new KeyboardHook(Handle)')
edit(main,'// Taskbar Tiles 0.7.6','// Taskbar Tiles 0.7.7')
edit(main,'internal const string Version = "0.7.6";', 'internal const string Version = "0.7.7";\n        internal const string RepairEventName = "Local\\\\TaskbarTiles.Repair.v077";')
edit(main,'        internal static void Log(string text)', '''        static void SignalRepair()
        { try { using (var e = EventWaitHandle.OpenExisting(RepairEventName)) e.Set(); } catch { } }
        internal static void Log(string text)''')
edit(main,'                if (!first)\n                {','                if (!first)\n                {\n                    SignalRepair();')
edit(main,'            if (args.Contains("--test-clickaway"))','            if (args.Contains("--test-input-support")) { Environment.Exit(InputSupportTests.NativeTest()); return; }\n            if (args.Contains("--test-clickaway"))')
edit(main,'Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Log(e.Exception.ToString()); };','''Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                    { try { if (Switcher.Resident != null) Switcher.Resident.RecoverUiFault(e.Exception); else Log(e.Exception.ToString()); } catch (Exception recovery) { Log("UI recovery failed: " + recovery); } };
                    AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                    { Log("Unhandled process exception: " + Convert.ToString(e.ExceptionObject)); ShortcutLog.Write("unhandled exception; terminating=" + e.IsTerminating); };''')
edit(main,'                    using (var toggle = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName))','''                    using (var toggle = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName))
                    using (var repair = new EventWaitHandle(false, EventResetMode.AutoReset, RepairEventName))''')
edit(main,'                        if (args.Contains("--toggle") || args.Contains("--show"))\n                            popup.BeginInvoke','''                        var repairWait = ThreadPool.RegisterWaitForSingleObject(repair, delegate(object s, bool timedOut)
                        { try { popup.BeginInvoke(new Action(delegate { popup.RepairShortcuts(true); })); } catch { } }, null, -1, false);
                        if (args.Contains("--toggle") || args.Contains("--show"))
                            popup.BeginInvoke''')
edit(main,'finally { wait.Unregister(null); toggleWait.Unregister(null); }','finally { wait.Unregister(null); toggleWait.Unregister(null); repairWait.Unregister(null); }')
edit(main,'SetupOutsideDismissal();','SetupOutsideDismissal(); SetupShortcutRecovery();')
edit(main,'            menu.Items.Add("Open rendering diagnostics",','''            menu.Items.Add("Shortcut health / repair", null, delegate { OpenShortcutHealth(); });
            menu.Items.Add("Open rendering diagnostics",''')
edit(main,'''                        Program.Log("UI action: " + ex);
                        if (hook != null) hook.Enabled = false;
                        if (interceptItem != null) interceptItem.Checked = false;
                        pending = null;
                        try { Dismiss(); Notify("An error occurred. Native Alt+Tab has been restored. See TaskbarTiles.log."); } catch { }''','''                        RecoverUiFault(ex);''')
edit(main,'        public void ToggleMenu()\n        {','        public void ToggleMenu()\n        {\n            RepairShortcuts(true);')
edit(main,'            if (m.Msg == 0x312 && m.WParam.ToInt32() == 10)', '''            if (m.Msg == KeyboardHook.ActionMessage)
            { int action = m.WParam.ToInt32(); Post(delegate { if (hook != null) hook.Deliver(action); }); return; }
            if (m.Msg == 0x218 && (m.WParam.ToInt32() == 7 || m.WParam.ToInt32() == 18))
                Post(delegate { RepairShortcuts(false); });
            if (m.Msg == 0x312 && m.WParam.ToInt32() == 10)''')
edit(main,'            if (closing) return; closing = true;','            if (closing) return; closing = true;\n            ShortcutLog.Write("resident shutdown requested");')
edit(main,'                tip.Dispose();','                DisposeShortcutRecovery();\n                tip.Dispose();')
edit(main,'                OutsideClickTests.Run(log);','                OutsideClickTests.Run(log);\n                InputSupportTests.Run(log);')
settings='src/TaskbarTiles/Settings.cs'
edit(settings,'        public string MonitorOverrides = "";','        public string MonitorOverrides = "";\n        public string TouchTestMonitorKey = "";')
edit(settings,'            MonitorOverrides = MonitorOverrides ?? "";','            MonitorOverrides = MonitorOverrides ?? "";\n            TouchTestMonitorKey = TouchTestMonitorKey ?? "";\n            if (TouchTestMonitorKey.Length > 1024) TouchTestMonitorKey = "";')
edit(settings,'            AddSearchSettingsPage();','            AddSearchSettingsPage();\n            AddTouchSupportPage();')
edit(settings,'            system.Controls.AddRange(new Control[] { xmouse, display, diag, launchLog, switching, updates, appFolder });','''            system.Controls.AddRange(new Control[] { xmouse, display, diag, launchLog, switching, updates, appFolder });
            Section(system, "Shortcut recovery", "Unrelated interface errors no longer leave Alt+Tab permanently disabled. Idle registration refresh and bounded error recovery respect your saved preference. The X-Mouse command and Ctrl+Alt+Space are independent of the hook.");
            var repairKeys = Theme.Button("Repair shortcut handling", 235);
            repairKeys.Click += delegate { if (Switcher.Resident != null) { Switcher.Resident.RepairShortcuts(true); MessageBox.Show(this, "Repair requested. Release any held keys; registration refreshes when safe. Your settings were not changed.", "Shortcut repair"); } };
            var shortcutStatus = Theme.Button("Show shortcut status", 235);
            shortcutStatus.Click += delegate { if (Switcher.Resident != null) MessageBox.Show(this, Switcher.Resident.ShortcutStatus, "Shortcut health"); };
            var shortcutLog = Theme.Button("Open shortcut diagnostics", 235);
            shortcutLog.Click += delegate { if (!File.Exists(ShortcutLog.PathName)) ShortcutLog.Write("diagnostics opened"); System.Diagnostics.Process.Start("notepad.exe", "\\\"" + ShortcutLog.PathName + "\\\""); };
            system.Controls.AddRange(new Control[] { repairKeys, shortcutStatus, shortcutLog });
            HintTree(repairKeys, "Repairs the resident shortcut registration without restarting apps, changing your mouse bindings or saving Settings. Held keys defer re-registration.");''')
health='src/TaskbarTiles/ShortcutHealth.cs'
edit(health,'        bool shortcutRecoveryReady;','''        bool shortcutRecoveryReady, touchDiagnosticActive, shortcutRecoveryDisposed;
        internal void SetTouchDiagnosticActive(bool active)
        { touchDiagnosticActive = active; if (hook != null) hook.Enabled = !active && options.InterceptAltTab; }''')
edit(health,'            if (closing || !shortcutRecoveryReady) return;','            if (closing || !shortcutRecoveryReady || touchDiagnosticActive) return;')
edit(health,'                hook.Enabled = options.InterceptAltTab;\n                Notify("Taskbar Tiles closed','                hook.Enabled = !touchDiagnosticActive && options.InterceptAltTab;\n                Notify("Taskbar Tiles closed')
edit(health,'        { if (ReferenceEquals(Resident, this)) Resident = null; shortcutRecoveryTimer.Stop(); shortcutRecoveryTimer.Dispose(); }','        { if (shortcutRecoveryDisposed) return; shortcutRecoveryDisposed = true; if (ReferenceEquals(Resident, this)) Resident = null; shortcutRecoveryTimer.Stop(); shortcutRecoveryTimer.Dispose(); }')
touch='src/TaskbarTiles/TouchSupport.cs'
edit(touch,'            fields["TouchTestMonitorKey"] = key;','            fields["TouchTestMonitorKey"] = key; page.Controls.Add(key);')
edit(touch,'                dismissOnFocusLoss = false;','                dismissOnFocusLoss = false;\n                if (Switcher.Resident != null) Switcher.Resident.SetTouchDiagnosticActive(true);')
edit(touch,'                finally { dismissOnFocusLoss = previousDismissal; dismissalArmed = false; outsideSince = null; }','''                finally
                {
                    if (Switcher.Resident != null) Switcher.Resident.SetTouchDiagnosticActive(false);
                    dismissOnFocusLoss = previousDismissal; dismissalArmed = false; outsideSince = null;
                }''')
proj='src/TaskbarTiles/TaskbarTiles.csproj'
edit(proj,'    <Compile Include="OutsideClickTests.cs" />','''    <Compile Include="OutsideClickTests.cs" />
    <Compile Include="ShortcutHealth.cs" />
    <Compile Include="TouchSupport.cs" />
    <Compile Include="InputSupportTests.cs" />''')
for path in ['src/TaskbarTiles/AssemblyInfo.cs','src/TaskbarTiles/app.manifest']:
    p=Path(path); s=p.read_text(encoding='utf-8-sig'); assert '0.7.6' in s; p.write_text(s.replace('0.7.6','0.7.7'),encoding='utf-8',newline='\n')
Path('version.txt').write_text('0.7.7\n',encoding='utf-8')
for name in ['build.yml','release.yml']:
    path='.github/workflows/'+name
    marker='      - name: Test native switcher topmost ordering'
    extra='''      - name: Test native outside-click dismissal
        shell: powershell
        run: ./tools/Test-OutsideClick.ps1
      - name: Test shortcut registration recovery
        shell: powershell
        run: ./tools/Test-InputSupport.ps1
'''
    edit(path,marker,extra+marker)
notes='''## 0.7.7

- Stop permanently disabling Alt+Tab after an unrelated recoverable UI error. Repeated faults use a visible ten-second cooldown without changing the saved preference.
- Keep low-level keyboard callbacks free of UI/Shell work; post bounded native messages. Renew registration when idle, repair a stopped hook thread, and support explicit repair via tray, Settings and reopening the installed executable.
- Include the previously prepared outside-click dismissal repair, including Settings draft discard and nonactivating windows, alongside the 0.7.5 rendering/font fix.
- Add **Touch screen monitor support** as the brief's first compatibility-test milestone: monitor selection, local pointer/contact/pen-pressure/hover observations, passive source-marker counts and background digitizer-report availability. **Automatic return is not implemented/enabled in this release.** No primary-only approximation is permitted.
- Add runtime shortcut diagnostics and native removal/re-registration tests. Retain existing placement, app-instance, topmost, updater and installer safeguards.

'''
edit('CHANGELOG.md','# Changelog\n\n','# Changelog\n\n'+notes)
print('Applied 0.7.7 integration; compile/native tests must pass before commit.')
