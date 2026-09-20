from pathlib import Path
B=Path('src/TaskbarTiles')
def edit(file,old,new):
 p=Path(file);s=p.read_text(encoding='utf-8-sig');assert s.count(old)==1,(str(file),old[:90],s.count(old));p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
def cut(file,start,end,new):
 p=Path(file);s=p.read_text(encoding='utf-8-sig');assert s.count(start)==1 and s.count(end)==1,(file,start,end);a=s.index(start);b=s.index(end,a);p.write_text(s[:a]+new+s[b:],encoding='utf-8',newline='\n')
main=B/'TaskbarTiles.cs'
cut(main,'    sealed class KeyboardHook : IDisposable','    sealed class WindowItem','')
edit(main,'internal const string Version = "0.7.6";','internal const string Version = "0.8.0";')
edit(main,'// Taskbar Tiles 0.7.6','// Taskbar Tiles 0.8.0')
edit(main,'            if (args.Contains("--test-update-https"))','            if (args.Contains("--test-touch-shortcuts")) { Environment.Exit(TouchSupportTests.RunNative()); return; }\n            if (args.Contains("--test-update-https"))')
edit(main,'                    if (args.Contains("--toggle") || args.Contains("--show"))','                    if (args.Length == 0 || args.Contains("--toggle") || args.Contains("--show"))')
edit(main,'            SetupOutsideDismissal();','            SetupOutsideDismissal(); SetupShortcutRecovery(); SetupTouchSupport();')
edit(main,'            menu.Items.Add("Refresh taskbar apps", null, delegate { RefreshApps(); });','''            menu.Items.Add("Refresh taskbar apps", null, delegate { RefreshApps(); });
            menu.Items.Add("Repair shortcuts", null, delegate { RepairShortcuts(); });
            menu.Items.Add("Open shortcut diagnostics", null, delegate { OpenFile(ShortcutDiagnostics.PathName); });
            var touchMenu = new ToolStripMenuItem("Touch screen monitor support");
            var pauseTouch = new ToolStripMenuItem("Pause automatic return") { CheckOnClick = true };
            pauseTouch.Click += delegate { if (touchService != null) touchService.SetPaused(pauseTouch.Checked); };
            var stayTouch = new ToolStripMenuItem("Stay here") { CheckOnClick = true };
            stayTouch.Click += delegate { if (touchService != null) touchService.SetStay(stayTouch.Checked); };
            touchMenu.DropDownOpening += delegate { if (touchService != null) { pauseTouch.Checked = touchService.Paused; stayTouch.Checked = touchService.Stay; } };
            touchMenu.DropDownItems.Add(pauseTouch); touchMenu.DropDownItems.Add(stayTouch);
            touchMenu.DropDownItems.Add("Return now (when safe)", null, delegate { if (touchService != null) touchService.ReturnNow(); });
            touchMenu.DropDownItems.Add("Monitors, test and settings...", null, delegate { ShowTouchSupport(); });
            menu.Items.Add(touchMenu);''')
edit(main,'''                        if (hook != null) hook.Enabled = false;
                        if (interceptItem != null) interceptItem.Checked = false;
                        pending = null;
                        try { Dismiss(); Notify("An error occurred. Native Alt+Tab has been restored. See TaskbarTiles.log."); } catch { }''','''                        try { NavigationFailed(ex); } catch (Exception recovery) { Program.Log("Navigation recovery: " + recovery); }''')
edit(main,'                hook.Enabled = options.InterceptAltTab; interceptItem.Checked = hook.Enabled;\n                ApplyFilter(false);','                hook.Enabled = options.InterceptAltTab; interceptItem.Checked = hook.Enabled;\n                if (touchService != null) touchService.Configure(options);\n                ApplyFilter(false);')
edit(main,'        public void ToggleMenu()\n        {','        public void ToggleMenu()\n        {\n            RepairShortcuts();\n            if (touchService != null) touchService.Cancel("switcher command");')
edit(main,'        void OpenOrCycle(bool forceSticky, bool reverse, bool minimiseFullscreen = true)\n        {','        void OpenOrCycle(bool forceSticky, bool reverse, bool minimiseFullscreen = true)\n        {\n            if (touchService != null) touchService.Cancel("switcher navigation");')
edit(main,'            if (m.Msg == 0x312 && m.WParam.ToInt32() == 10)','            if (m.Msg == 0x312 && touchService != null && touchService.HandleHotkey(m.WParam.ToInt32())) return;\n            if (m.Msg == 0x312 && m.WParam.ToInt32() == 10)')
edit(main,'            DisposeOutsideDismissal();','            DisposeShortcutRecovery();\n            if (touchService != null) touchService.Dispose();\n            DisposeOutsideDismissal();')
edit(main,'                OutsideClickTests.Run(log);','                OutsideClickTests.Run(log);\n                TouchSupportTests.Run(log);')
settings=B/'Settings.cs'
edit(settings,'        public int TileSize = 120;','''        public bool TouchSupportEnabled = false;
        public int TouchReturnDelayMs = 1000, PenReturnDelayMs = 2000, TouchReturnAction = 0;
        public bool TouchWaitForHover = true, TouchTypingCancels = true, TouchPauseForMenus = true;
        public string TouchMonitorRules = "", TouchExcludedApps = "", TouchPauseShortcut = "", TouchStayShortcut = "", TouchReturnShortcut = "";
        public int TileSize = 120;''')
edit(settings,'            { "TileSize", new[] { 56, 256 } },','''            { "TouchReturnDelayMs", new[] { 250, 60000 } }, { "PenReturnDelayMs", new[] { 250, 60000 } }, { "TouchReturnAction", new[] { 0, 2 } },
            { "TileSize", new[] { 56, 256 } },''')
edit(settings,'            MonitorOverrides = MonitorOverrides ?? "";','''            MonitorOverrides = MonitorOverrides ?? "";
            TouchMonitorRules = TouchMonitorRules ?? ""; TouchExcludedApps = TouchExcludedApps ?? "";
            TouchPauseShortcut = TouchPauseShortcut ?? ""; TouchStayShortcut = TouchStayShortcut ?? ""; TouchReturnShortcut = TouchReturnShortcut ?? "";''')
edit(settings,'            LoadControls(); HookLiveChanges(); InstallSettingHints();','            AddTouchSupportPage(); AddShortcutRecoveryPage();\n            LoadControls(); HookLiveChanges(); InstallSettingHints();')
# The passive hardware test must remain visible while the user works in another app.
# It is an explicit diagnostic exception to ordinary Settings click-away, never a save.
sd=B/'SettingsDismissal.cs'
edit(sd,'            { return dismissOnFocusLoss && !DismissedByFocusLoss && !closingSettings; }, RequestOutsideSettingsDismissal);','            { return dismissOnFocusLoss && !DismissedByFocusLoss && !closingSettings && !TouchReturnService.TestActive; }, RequestOutsideSettingsDismissal);')
edit(sd,'            if (IsDisposed || closingSettings) return;\n            if (DismissedByFocusLoss)','            if (IsDisposed || closingSettings) return;\n            if (TouchReturnService.TestActive) { outsideSince = null; return; }\n            if (DismissedByFocusLoss)')
# Skip our diagnostic view's outside watcher; main settings resumes it when test closes.
ui=B/'SettingsTouchSupport.cs'
edit(ui,'outside=new PopupClickWatcher(this,delegate{return true;},delegate{DialogResult=DialogResult.Cancel;Close();});outside.Arm();','')
edit(ui,'        PopupClickWatcher outside;','')
edit(ui,'if(outside!=null)outside.Dispose();','')
edit(ui,'            Number(p,"TouchReturnAction","Return action","0 = focus and cursor; 1 = focus only; 2 = cursor only. No synthetic click is used.",0,2,1);','            Choice(p,"TouchReturnAction","Return action","No synthetic click is used. Focus failure does not move the cursor.",new[] {"Focus + cursor","Focus only","Cursor only"});')
edit(ui,'            Section(panel,label,help);\n            var box=','            var box=')
edit(ui,'            fields[key]=box;panel.Controls.Add(box);settingHints.SetToolTip(box,help);','            fields[key]=box;Row(panel,label,help,box);settingHints.SetToolTip(box,help);')
edit(ui,'Test first: keep another app active, then touch/draw on the selected display. No returns happen while this test is open.','Passive test stays open while you use another app. No returns occur. Close this test to restore normal click-away.')
# Recover a removed hook even when its lost key-up left a stale logical session.
h=B/'ShortcutReliability.cs'
edit(h,'        long lastInstalled;','        long lastInstalled, lastEvent;')
edit(h,'            if (swallowTabUp || session) return false;','')
edit(h,'            return true;\n        }\n        void Install()','''            if (swallowTabUp || session)
            {
                if ((Stopwatch.GetTimestamp() - lastEvent) / (double)Stopwatch.Frequency < 2) return false;
                swallowTabUp = session = false; Array.Clear(modifiers, 0, modifiers.Length);
            }
            return true;
        }
        void Install()''')
edit(h,'                    int msg = wp.ToInt32(), vk = (int)k.vkCode;','                    lastEvent = Stopwatch.GetTimestamp();\n                    int msg = wp.ToInt32(), vk = (int)k.vkCode;')
edit(h,'        internal void TestRevoke()','        internal void TestDeliver() { Enqueue(0); Drain(); }\n        internal void TestRevoke()')
edit(h,'            ShortcutDiagnostics.Write("resident started; " + hook.Status);','''            ShortcutDiagnostics.Write("resident started; " + hook.Status);
            Microsoft.Win32.SystemEvents.PowerModeChanged += ShortcutPowerChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch += ShortcutSessionChanged;''')
edit(h,'        void NavigationFailed(Exception ex)','''        void ShortcutPowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e) { Post(delegate { RepairShortcuts(); }); }
        void ShortcutSessionChanged(object sender, Microsoft.Win32.SessionSwitchEventArgs e) { Post(delegate { RepairShortcuts(); }); }
        void DisposeShortcutRecovery()
        {
            shortcutRecoveryTimer.Stop(); shortcutRecoveryTimer.Dispose();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= ShortcutPowerChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch -= ShortcutSessionChanged;
            ShortcutDiagnostics.Write("resident exiting normally");
        }
        void NavigationFailed(Exception ex)''')
# Touch contacts can begin after hover without a mouse-promotion anchor for hover.
edit(B/'TouchSupport.cs','bool entering = (f.Down > 0 || f.Hover) && old.Down == 0 && !old.Hover;','bool entering = (f.Down > 0 && old.Down == 0) || (f.Hover && !old.Hover && old.Down == 0);')
# Raw hardware mouse/keyboard observation backs up cancellation independently of hooks.
r=B/'TouchRawInput.cs'
edit(r,'path=Marshal.PtrToStringUni(name);','path=Marshal.PtrToStringUni(name,(int)size).TrimEnd(\'\\0\');')
edit(r,'        internal bool Registered { get; private set; }','        internal int PhysicalVersion, TypingVersion;\n        internal bool Registered { get; private set; }')
edit(r,'Flags=0x100|0x2000,Target=Handle}).ToArray();','Flags=0x100|0x2000,Target=Handle}).Concat(new[] { new TouchHidNative.DeviceRegistration {Page=1,Usage=2,Flags=0x100|0x2000,Target=Handle}, new TouchHidNative.DeviceRegistration {Page=1,Usage=6,Flags=0x100|0x2000,Target=Handle} }).ToArray();')
edit(r,' || Marshal.ReadInt32(buffer)!=2) return;',' ) return;\n                    int type=Marshal.ReadInt32(buffer);\n                    if(type==0)\n                    {\n                        if(size<header+24) return;\n                        uint extra=unchecked((uint)Marshal.ReadInt32(buffer,(int)header+20));\n                        if((extra & 0xFFFFFF00u)!=0xFF515700u && (Marshal.ReadInt32(buffer,(int)header+12)!=0 || Marshal.ReadInt32(buffer,(int)header+16)!=0 || Marshal.ReadInt16(buffer,(int)header+4)!=0)) PhysicalVersion++;\n                        return;\n                    }\n                    if(type==1)\n                    {\n                        if(size<header+16) return; int key=Marshal.ReadInt16(buffer,(int)header+6);\n                        if((Marshal.ReadInt16(buffer,(int)header+2)&1)==0 && !TouchShortcutKeys.IsShortcut(key) && key!=0x10&&key!=0x11&&key!=0x12&&!(key>=0xA0&&key<=0xA5)) TypingVersion++;\n                        return;\n                    }\n                    if(type!=2)return;')
edit(r,'Flags=1,Target=IntPtr.Zero}).ToArray();','Flags=1,Target=IntPtr.Zero}).Concat(new[] { new TouchHidNative.DeviceRegistration {Page=1,Usage=2,Flags=1}, new TouchHidNative.DeviceRegistration {Page=1,Usage=6,Flags=1} }).ToArray();')
# Runtime anchor evidence, optional shortcuts, strict cancellation and bounded work.
r=B/'TouchReturnRuntime.cs'
edit(r,'        internal static TouchReturnService Current;','''        internal static TouchReturnService Current;
        internal static bool TestActive { get { return Current != null && Current.Testing; } }
        readonly Dictionary<string,string> probeAnchors = new Dictionary<string,string>();
        readonly List<int> hotkeys = new List<int>();
        bool manualReturn;
        int rawMouseVersion, rawKeyVersion;
        internal bool HasProbeAnchor(string device, string screen) { string found; return probeAnchors.TryGetValue(device,out found) && found==screen; }''')
edit(r,'            finally { Installed=false;','            catch { Installed=false; Interlocked.Increment(ref physicalVersion); }\n            finally { Installed=false;')
edit(r,'int v=(int)k.vkCode;','int v=(int)k.vkCode;\n                    if(TouchShortcutKeys.IsShortcut(v)) return Native.CallNextHookEx(IntPtr.Zero,code,message,data);')
edit(r,'            monitors=DisplayNative.Monitors(); layout=Fingerprint(monitors);','            monitors=DisplayNative.Monitors(); layout=Fingerprint(monitors);\n            RegisterShortcuts();')
edit(r,'            Stop(); fault=""; Start();','            Stop(); fault=""; probeAnchors.Clear(); Start();')
edit(r,'            bool moved=mouseVersion!=observer.PhysicalVersion;','            bool moved=mouseVersion!=observer.PhysicalVersion || rawMouseVersion!=source.PhysicalVersion;')
edit(r,'            bool typed=keyVersion!=observer.TypingVersion;','            bool typed=keyVersion!=observer.TypingVersion || rawKeyVersion!=source.TypingVersion;')
edit(r,'                mouseVersion=observer.PhysicalVersion;keyVersion=observer.TypingVersion;navigationVersion=observer.NavigationVersion;','                mouseVersion=observer.PhysicalVersion;keyVersion=observer.TypingVersion;navigationVersion=observer.NavigationVersion;rawMouseVersion=source.PhysicalVersion;rawKeyVersion=source.TypingVersion;')
edit(r,'                    var f=delayed[0];delayed.RemoveAt(0);','''                    var f=delayed[0];delayed.RemoveAt(0);
                    var pre=observer.Recent.LastOrDefault(s=>s.Pen==f.Pen && Math.Abs(unchecked((int)(f.Tick-s.Tick)))<=150);
                    if(pre!=null && pre.Foreground!=IntPtr.Zero && WindowNative.ProcessId(pre.Foreground)!=(uint)Process.GetCurrentProcess().Id)
                    { var observedMonitor=monitors.FirstOrDefault(m=>m.Bounds.Contains(pre.Contact)); if(observedMonitor!=null)probeAnchors[f.Device]=observedMonitor.Key; }''')
edit(r,'                    string target=ShellIcons.ProcessFile(Native.GetForegroundWindow());','                    string target=string.IsNullOrWhiteSpace(options.TouchExcludedApps)?"":ShellIcons.ProcessFile(Native.GetForegroundWindow());')
edit(r,'                    else if(fg!=engine.Saved.Window','                    else if(WindowNative.ProcessId(fg)==(uint)Process.GetCurrentProcess().Id)Cancel("Taskbar Tiles controls opened");\n                    else if(fg!=engine.Saved.Window')
edit(r,'                var point=engine.Take(Clock(),false,blocked);if(point!=null)Restore(point);','                var point=engine.Take(Clock(),manualReturn,blocked);if(point!=null){manualReturn=false;Restore(point);}')
cut(r,'        internal void ReturnNow()','        void RecordStatus()','''        internal void ReturnNow() { if(engine.Saved!=null) manualReturn=true; }
        void RegisterShortcuts()
        {
            foreach(int id in hotkeys)Native.UnregisterHotKey(owner.Handle,id); hotkeys.Clear();
            var combinations=new List<Keys>(); string[] values={options.TouchPauseShortcut,options.TouchStayShortcut,options.TouchReturnShortcut};
            for(int i=0;i<values.Length;i++)
            {
                if(string.IsNullOrWhiteSpace(values[i]))continue;
                try
                {
                    Keys value=(Keys)new KeysConverter().ConvertFromInvariantString(values[i]);int key=(int)(value&Keys.KeyCode);
                    uint mods=(uint)(((value&Keys.Alt)!=0?1:0)|((value&Keys.Control)!=0?2:0)|((value&Keys.Shift)!=0?4:0));
                    if(key<0x20 || key>0xFE || mods==0 || key==0x5B || key==0x5C)throw new ArgumentException();
                    if(!Native.RegisterHotKey(owner.Handle,0xB100+i,0x4000|mods,key))throw new InvalidOperationException();
                    hotkeys.Add(0xB100+i);combinations.Add(value);
                }
                catch {history.Add("Shortcut unavailable: "+values[i]+" (invalid or already assigned)");}
            }
            TouchShortcutKeys.Registered=combinations.ToArray();
        }
        internal bool HandleHotkey(int id)
        {
            if(!hotkeys.Contains(id))return false;
            if(id==0xB100)SetPaused(!Paused);else if(id==0xB101)SetStay(!Stay);else ReturnNow();return true;
        }
''')
edit(r,'            if(disposed)return;disposed=true;timer.Stop();timer.Dispose();Stop();','            if(disposed)return;disposed=true;timer.Stop();timer.Dispose();Stop();\n            foreach(int id in hotkeys)Native.UnregisterHotKey(owner.Handle,id);hotkeys.Clear();TouchShortcutKeys.Registered=new Keys[0];')
edit(r,'    sealed class TouchInputObserver : IDisposable','''    static class TouchShortcutKeys
    {
        internal static volatile Keys[] Registered = new Keys[0];
        internal static bool IsShortcut(int key)
        {
            if(Registered.Length==0)return false;
            Keys value=(Keys)key;
            if(Native.Down(0x11))value|=Keys.Control;if(Native.Down(0x12))value|=Keys.Alt;if(Native.Down(0x10))value|=Keys.Shift;
            return Registered.Contains(value);
        }
    }
    sealed class TouchInputObserver : IDisposable''')
# Test actual callback delivery outside the low-level hook, without injecting any key.
t=B/'TouchSupportTests.cs'
edit(t,'                    int first=hook.Generation;hook.TestRevoke();','                    hook.TestDeliver();Require(delivered==1,"queued delivery reached UI callback");\n                    int first=hook.Generation;hook.TestRevoke();')
edit(t,'                    Require(delivered==0,"registration test generates no shortcut or input");','                    Require(delivered==1,"registration test generates no extra shortcut or input");')
# Project version and includes.
for f in ['AssemblyInfo.cs','app.manifest']:
 p=B/f;s=p.read_text(encoding='utf-8-sig');s=s.replace('0.7.6','0.8.0');p.write_text(s,encoding='utf-8',newline='\n')
edit('version.txt','0.7.6','0.8.0')
new_files=['ShortcutReliability','TouchSupport','TouchRawInput','TouchReturnRuntime','SettingsTouchSupport','TouchSupportTests']
edit(B/'TaskbarTiles.csproj','    <Compile Include="SwitcherLayer.cs" />',''.join('    <Compile Include="'+n+'.cs" />\n' for n in new_files)+'    <Compile Include="SwitcherLayer.cs" />')
for f in ['.github/workflows/build.yml','.github/workflows/release.yml']:
 edit(f,'      - name: Test native switcher topmost ordering','''      - name: Test touch support and shortcut recovery
        shell: powershell
        run: ./tools/Test-TouchSupport.ps1
      - name: Test native outside-click dismissal
        shell: powershell
        run: ./tools/Test-ClickAway.ps1
      - name: Test native switcher topmost ordering''')
edit('CHANGELOG.md','# Changelog\n','# Changelog\n\n## 0.8.0\n\n- Recover Alt+Tab interception after transient UI errors without changing the saved preference. Keep callbacks off the low-level pump, periodically renew between gestures, rearm after wake/unlock and repair on explicit reopen. Add Shortcut health and diagnostics.\n- Include the previously staged outside-click fix from 0.7.6 and all published rendering/launch/updater repairs.\n- Add experimental Touch screen monitor support: passive HID detection, full-frame contact tracking, per-monitor verified device association, touch/pen idle delays, Stay here, pause, optional hotkeys and one-shot focus/cursor return. Physical mouse always cancels.\n- Default Touch Return off. Unsupported reports, virtual mouse-only input, unknown pre-touch state or missing required hover information cannot trigger automatic return. A local cross-application detection test is mandatory; CI does not certify spacedesk/Apollo hardware.\n')
# No default monitor or hotkey is enabled. Main Apply owns these values.
p=Path('config/settings.ini');s=p.read_text(encoding='utf-8-sig');s+='\n# Touch Return: opt in only after the passive device test.\nTouchSupportEnabled=false\nTouchReturnDelayMs=1000\nPenReturnDelayMs=2000\nTouchReturnAction=0\nTouchWaitForHover=true\nTouchTypingCancels=true\nTouchPauseForMenus=true\nTouchMonitorRules=\nTouchExcludedApps=\nTouchPauseShortcut=\nTouchStayShortcut=\nTouchReturnShortcut=\n';p.write_text(s,encoding='utf-8',newline='\n')
print('0.8.0 integration applied. Publication requires Windows tests; hardware compatibility remains detection-gated.')
