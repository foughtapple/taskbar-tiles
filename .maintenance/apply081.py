from pathlib import Path
import re
root = Path('.')
def edit(path, old, new):
    p = root / path
    s = p.read_text(encoding='utf-8-sig')
    assert s.count(old) == 1, (path, old[:100], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
def region(path, begin, end, replacement):
    p = root / path; s = p.read_text(encoding='utf-8-sig')
    assert s.count(begin) == 1 and s.count(end) == 1, path
    i = s.index(begin); j = s.index(end, i)
    p.write_text(s[:i] + replacement + s[j:], encoding='utf-8')
base = 'src/TaskbarTiles/'
for name in ['TaskbarTiles.cs', 'AssemblyInfo.cs', 'app.manifest']:
    p = root / (base + name); s = p.read_text(encoding='utf-8-sig')
    assert '0.8.0' in s, name
    p.write_text(s.replace('0.8.0', '0.8.1'), encoding='utf-8')
assert Path('version.txt').read_text(encoding='utf-8-sig').strip() == '0.8.0'
Path('version.txt').write_text('0.8.1\n', encoding='utf-8')
edit(base+'TaskbarTiles.csproj', '    <Compile Include="TouchSupport.cs" />', '    <Compile Include="TouchSetupDiagnostics.cs" />\n    <Compile Include="TouchSetupTests.cs" />\n    <Compile Include="TouchSupport.cs" />')
# Keep report capability separate from observed frames and current hover.
edit(base+'TouchSupport.cs', '        internal bool Supported, HoverKnown, SawHoverExit, SawUp;', '        internal bool Supported, HoverKnown, SawHoverExit, SawUp, CurrentHover, Complete = true;')
edit(base+'TouchSupport.cs', '            Frames++; Contacts = frame.Down; LastTick = frame.Tick;', '''            Frames++; Contacts = frame.Down; LastTick = frame.Tick;
            Complete = frame.Complete && frame.Valid;
            CurrentHover = frame.Pen && frame.HoverKnown && frame.Hover;
            if (!Complete) { DownSince = 0; Problem = "Incomplete/invalid report; release not verified"; return; }''')
edit(base+'TouchSupport.cs', '        internal bool Ready { get { return Supported && SawUp && HeldMilliseconds >= 2500 && (Kind == "Pen" || MaxContacts >= 2); } }', '''        internal void ResetTest()
        {
            Frames = MaxContacts = HeldMilliseconds = Contacts = 0; DownSince = 0; LastTick = 0;
            SawUp = SawHoverExit = CurrentHover = false; Complete = true;
            Problem = "Evidence reset; repeat hold/release and multitouch or pen test";
        }
        internal bool Ready { get { return Supported && Complete && Contacts == 0 && SawUp && HeldMilliseconds >= 2500 && (Kind == "Pen" || MaxContacts >= 2); } }''')
# Classify actual device-change messages, instead of faulting every registered class.
edit(base+'TouchRawInput.cs', '        public void Dispose() { if(parsed!=IntPtr.Zero) { Marshal.FreeHGlobal(parsed); parsed=IntPtr.Zero; } }', '''        internal void ResetTestEvidence() { assembler.Reset(); Evidence.ResetTest(); }
        public void Dispose() { if(parsed!=IntPtr.Zero) { Marshal.FreeHGlobal(parsed); parsed=IntPtr.Zero; } }''')
edit(base+'TouchRawInput.cs', '        readonly HashSet<IntPtr> rejected=new HashSet<IntPtr>();', '        readonly HashSet<IntPtr> rejected=new HashSet<IntPtr>();\n        readonly Dictionary<IntPtr,TouchInputKind> inputKinds=new Dictionary<IntPtr,TouchInputKind>();')
edit(base+'TouchRawInput.cs', '            foreach(var d in list.Take((int)n).Where(d=>d.Type==2)) GetDevice(d.Device);', '''            foreach(var d in list.Take((int)n))
            {
                var kind=d.Type==0?TouchInputKind.Mouse:d.Type==1?TouchInputKind.Keyboard:TouchSetupPolicy.Kind(d.Device);
                inputKinds[d.Device]=kind;
                if(kind==TouchInputKind.Digitizer) GetDevice(d.Device);
            }''')
edit(base+'TouchRawInput.cs', '        protected override void WndProc(ref Message m)', '''        internal void ResetTestEvidence()
        { foreach(var d in devices.Values) d.ResetTestEvidence(); }
        internal int ReplayKnownArrivalsForTest()
        {
            var known=inputKinds.Where(p=>p.Value!=TouchInputKind.Unknown).Select(p=>p.Key).ToArray();
            foreach(var h in known) HandleDeviceChange(1,h);
            return known.Length;
        }
        internal void HandleDeviceChange(int change,IntPtr handle)
        {
            TouchInputKind kind; bool known=inputKinds.TryGetValue(handle,out kind);
            if(!known) kind=TouchSetupPolicy.Kind(handle);
            var action=TouchSetupPolicy.DeviceChange(change,known,kind);
            if(change==2)
            {
                TouchHidDevice old;
                if(devices.TryGetValue(handle,out old)) { old.Dispose(); devices.Remove(handle); }
                rejected.Remove(handle); inputKinds.Remove(handle);
            }
            else if(change==1)
            {
                inputKinds[handle]=kind;
                if(kind==TouchInputKind.Digitizer) GetDevice(handle);
            }
            if(action==TouchChangeAction.Cancel) PhysicalVersion++; // Cancel a pending return, not the entire provider.
            if(action==TouchChangeAction.Retest)
                invalid((kind==TouchInputKind.Unknown?"Unknown input device":"Touch/pen device")+
                    (change==2?" removed":" changed")+"; pending return discarded; Restart test after the connection settles");
        }
        protected override void WndProc(ref Message m)''')
edit(base+'TouchRawInput.cs', '''            if(m.Msg==0x00FE)
            {
                invalid("input device connected/disconnected; pending return discarded");
                if(m.WParam.ToInt32()==2) { TouchHidDevice old; if(devices.TryGetValue(m.LParam,out old)) { old.Dispose(); devices.Remove(m.LParam); } rejected.Remove(m.LParam); }
                else GetDevice(m.LParam);
            }''', '''            if(m.Msg==0x00FE && !disposed)
            {
                HandleDeviceChange(m.WParam.ToInt32(),m.LParam);
                return;
            }''')
# Explain mode and association evidence; keep current blockers separate from history.
edit(base+'TouchReturnRuntime.cs', '        readonly Dictionary<string,string> probeAnchors = new Dictionary<string,string>();', '        readonly Dictionary<string,string> probeAnchors = new Dictionary<string,string>();\n        readonly Dictionary<string,string> probeNotes = new Dictionary<string,string>();')
edit(base+'TouchReturnRuntime.cs', '        long lastPump;', '        uint lastPump;')
region(base+'TouchReturnRuntime.cs', '        internal string Status {get', '        internal TouchReturnService(Control control', '''        internal string BlockingReason {get{return fault;}}
        internal bool HoverRequired {get{return options.TouchWaitForHover;}}
        internal string Status {get{return Testing?"DETECTION TEST ONLY - no focus or cursor return while this test is open":
            fault.Length>0?"Blocked: "+fault:!options.TouchSupportEnabled?"Disabled - enable Touch Return in main Settings after setup":paused?"Paused":engine.Status;}}
        internal string ProbeStatus(string device)
        {
            string screen,note;
            if(probeAnchors.TryGetValue(device,out screen))
            {
                var monitor=monitors.FirstOrDefault(m=>m.Key==screen);
                return "observed for "+(monitor==null?"a disconnected display":monitor.Label)+(fault.Length>0?"; invalid while blocked":"");
            }
            return probeNotes.TryGetValue(device,out note)?note:"not observed; activate another screen's app before touching";
        }
        internal string Report {get{return Status+Environment.NewLine+
            "Saved master switch: "+(options.TouchSupportEnabled?"ON":"OFF")+"; saved enabled screens: "+rules.Count(r=>r.Enabled)+
            "; saved enabled/associated input paths: "+rules.Count(r=>r.Enabled&&((r.Touch&&r.TouchVerified)||(r.Pen&&r.PenVerified)))+Environment.NewLine+
            "Current blocking condition: "+(fault.Length==0?"none":fault)+Environment.NewLine+
            (source==null?"Input observer stopped":source.Status)+Environment.NewLine+
            "Snapshot policy: matching pre-touch window/cursor evidence is required. Format support alone does not enable return."+Environment.NewLine+
            string.Join(Environment.NewLine,Devices.Select(d=>d+"; frames="+d.Frames+"; max contacts="+d.MaxContacts+
                "; completed hold="+d.HeldMilliseconds+" ms; release="+d.SawUp+"; hover capability="+d.HoverKnown+
                "; hovering now="+d.CurrentHover+"; hover exit observed="+d.SawHoverExit+"; pre-touch="+ProbeStatus(d.Key)))+
            Environment.NewLine+"Recent history (not the current condition):"+Environment.NewLine+string.Join(Environment.NewLine,history);}}
''')
edit(base+'TouchReturnRuntime.cs', '            lastPump=Environment.TickCount & int.MaxValue;', '            lastPump=unchecked((uint)Environment.TickCount);')
edit(base+'TouchReturnRuntime.cs', '            Stop(); fault=""; environmentChanged=false; probeAnchors.Clear(); Start(); engine.Cancel("test restarted; no automatic return during test");', '            Stop(); fault=""; environmentChanged=false; probeAnchors.Clear(); probeNotes.Clear(); Start(); engine.Cancel("test restarted; no automatic return during test");')
edit(base+'TouchReturnRuntime.cs', '        void OnFrame(TouchFrame frame)', '''        internal void HandleProcessingPause()
        {
            if(!Testing) { Block("input processing paused; run detection again before automatic return"); return; }
            // A passive test cannot return. A paused Settings UI invalidates its
            // measurements, not the provider permanently. Never count a pause as a hold.
            manualReturn=false;engine.ClearInput("test paused; evidence reset");delayed.Clear();probeAnchors.Clear();probeNotes.Clear();
            if(observer!=null) { observer.Drain(); foreach(var a in observer.Recent)a.Rejected=true; }
            if(source!=null)source.ResetTestEvidence();
            history.Add("Test UI paused: old measurements discarded. Repeat the hold/release and multi-contact test; return stays disabled.");
            while(history.Count>12)history.RemoveAt(0);
            // Preserve any independent real device/report fault; only Restart test clears it.
        }
        void OnFrame(TouchFrame frame)''')
edit(base+'TouchReturnRuntime.cs', '''                long pump=Environment.TickCount & int.MaxValue;
                if(lastPump!=0 && pump-lastPump>1500)Block("input processing paused; run detection again before automatic return");''', '''                uint pump=unchecked((uint)Environment.TickCount);
                if(TouchSetupPolicy.PumpGap(lastPump,pump))HandleProcessingPause();''')
edit(base+'TouchReturnRuntime.cs', '                    if(pre!=null && !pre.Rejected && f.Valid && f.Complete && f.Down>0)', '''                    if(f.Valid && f.Complete && f.Down>0)
                        probeNotes[f.Device]=pre==null?"no matching pre-touch mouse-promotion snapshot":pre.Rejected?
                            "snapshot cancelled by mouse/keyboard input or test reset":"snapshot timing found; coordinates or previous foreground not yet verified";
                    if(pre!=null && !pre.Rejected && f.Valid && f.Complete && f.Down>0)''')
# Make the mode and missing tests visible in the selected screen's diagnostics.
edit(base+'SettingsTouchSupport.cs', '                using(var dialog=new TouchMonitorDialog(edit.TouchMonitorRules))', '                ReadDraft();\n                using(var dialog=new TouchMonitorDialog(edit.TouchMonitorRules,edit.TouchSupportEnabled,edit.TouchWaitForHover))')
edit(base+'SettingsTouchSupport.cs', '        internal TouchMonitorDialog(string json)', '        readonly bool draftMaster, requireHover;\n        internal TouchMonitorDialog(string json,bool master=false,bool hover=true)')
edit(base+'SettingsTouchSupport.cs', '            rules=TouchRules.Parse(json);monitors=DisplayNative.Monitors();', '            draftMaster=master;requireHover=hover;rules=TouchRules.Parse(json);monitors=DisplayNative.Monitors();')
edit(base+'SettingsTouchSupport.cs', 'Text="Automatic return on this screen"', 'Text="Trigger return after using this screen"')
edit(base+'SettingsTouchSupport.cs', 'Text="Passive test stays open while you use another app. No returns occur. Close this test to restore normal click-away."', 'Text="TEST MODE: no focus/cursor return here. Select the screen you TOUCH, not the screen you return to."')
edit(base+'SettingsTouchSupport.cs', 'Text="Input devices — associate only after testing on the correct monitor"', 'Text="Select Touch for fingers or Pen for a pen. Checklist below shows what is still missing."')
edit(base+'SettingsTouchSupport.cs', 'if(service!=null)Clipboard.SetText("Taskbar Tiles "+Program.Version+Environment.NewLine+service.Report);', 'if(service!=null)Clipboard.SetText("Taskbar Tiles "+Program.Version+Environment.NewLine+DiagnosticText());')
edit(base+'SettingsTouchSupport.cs', 'if(devices.SelectedIndex<0&&devices.Items.Count>0)devices.SelectedIndex=0;', 'if(devices.SelectedIndex<0&&devices.Items.Count>0)devices.SelectedItem=available.OrderByDescending(d=>d.Frames).ThenBy(d=>d.Kind=="Touch"?0:1).First();')
region(base+'SettingsTouchSupport.cs', '            string monitor=selected==null?', '        void Associate()', '''            var text=DiagnosticText();
            if(status.Text!=text)status.Text=text;
        }
        string DiagnosticText()
        {
            var service=TouchReturnService.Current;
            var device=devices.SelectedItem as TouchDeviceEvidence;
            var screen=selected==null?null:monitors.FirstOrDefault(m=>m.Key==selected.Key);
            bool anchor=device!=null&&screen!=null&&service!=null&&service.HasProbeAnchor(device.Key,screen.Key);
            var lines=TouchSetupChecklist.Lines(device,screen!=null,screen!=null&&!string.IsNullOrEmpty(screen.Instance),
                anchor,requireHover,service==null?"Input service unavailable":service.BlockingReason);
            return "TEST MODE - automatic return is disabled until this window is closed."+Environment.NewLine+
                "Selected INPUT screen: "+(screen==null?"not connected":screen.Label)+" (not the return destination)"+Environment.NewLine+
                "Main Settings draft master: "+(draftMaster?"ON":"OFF; enable it after Use draft")+Environment.NewLine+
                "Selected screen draft: trigger="+enabled.Checked+"; touch="+touch.Checked+"; pen="+pen.Checked+Environment.NewLine+
                (selected==null?"":"Associations: touch="+selected.TouchVerified+"; pen="+selected.PenVerified)+Environment.NewLine+
                "SELECTED DEVICE CHECKLIST"+Environment.NewLine+string.Join(Environment.NewLine,lines)+Environment.NewLine+
                "After PASS: confirm unchanged input, Associate tested device, enable this screen's trigger, Use draft, then Apply in main Settings. Keep other screens off."+Environment.NewLine+
                "----- Listener diagnostics -----"+Environment.NewLine+(service==null?"Service not available":service.Report);
        }
''')
region(base+'SettingsTouchSupport.cs', '            if(d==null||screen==null||string.IsNullOrEmpty(screen.Instance)||!d.Ready||!confirmation.Checked)', '            if(d.Kind=="Pen")', '''            var service=TouchReturnService.Current;
            if(!confirmation.Checked||service==null||!TouchSetupChecklist.Ready(d,screen!=null,screen!=null&&!string.IsNullOrEmpty(screen.Instance),
                d!=null&&screen!=null&&service.HasProbeAnchor(d.Key,screen.Key),requireHover,service==null?"Input service unavailable":service.BlockingReason))
            {MessageBox.Show(this,"The selected device is not ready. See each WAIT/BLOCKED item in the checklist below. Format support alone is not a passed test. No association was saved.","Finish the selected device test");return;}
''')
# Regression tests run in existing helper and native touch-test gates.
edit(base+'TouchSupportTests.cs', '            checks=0;var e=Engine();var p=Anchor();', '            TouchSetupTests.Run(log);\n            checks=0;var e=Engine();var p=Anchor();')
edit(base+'TouchSupportTests.cs', '                File.WriteAllText(Path.Combine(Program.Home,"touch-shortcut-test.log"),log.ToString());return 0;', '                TouchSetupTests.RunNative(log);\n                File.WriteAllText(Path.Combine(Program.Home,"touch-shortcut-test.log"),log.ToString());return 0;')
notes = '''## Taskbar Tiles 0.8.1

### Touch detection: false blocks and clearer setup

The supplied 0.8.0 report contained actual single-finger touch reports but no observed pen reports. It did not establish a completed long-hold/multitouch test, a verified screen association, or a pre-touch return point. The passive test intentionally never moves focus or the pointer.

- Fix overly broad device-change handling. Arrival notifications for already-enumerated digitizers no longer block their test. Mouse/keyboard/other known non-digitizer changes cancel a pending return without permanently disabling the digitizer provider. Genuine or unclassified digitizer changes still require a fresh test.
- A pause while the passive test is open now discards old contact measurements, partial frames and pre-touch evidence and continues collecting fresh evidence. It cannot return during this process, count the pause as a held contact, or clear an independent device/report fault. A processing gap during real automatic-return operation still blocks and requires retesting.
- Show a selected-device checklist: actual reports, long held contact followed by release, two simultaneous fingers for touch, completed releases, required pen-hover exit, stable selected monitor, blocking fault and verified pre-touch state.
- Distinguish hover capability from observed pen activity. Default to the touch device for an initial finger test rather than an inactive pen entry. Pen must be tested and associated separately.
- Copy diagnostics includes the selected input screen, draft/saved switches, association state and snapshot evidence, with current faults separated from past history. No screenshots, window titles, typed text or pointer coordinates are included.
- State clearly that the selected monitor is the touchscreen that TRIGGERS return, not the destination. Associate, enable that screen's trigger, Use draft, then enable the master switch and Apply in main Settings. Other screens should remain off. This update does not enable any device or change your saved settings.

### Update and local test

Use Settings > Updates > Check for updates... > Download & install. Do not uninstall first. Close the passive test before trying real return. Keep an ordinary app active on another monitor, then touch the enabled touchscreen without using the physical mouse during the countdown. Use a simple windowed app first; fullscreen games can clip the cursor or reject activation.

A device listed as a supported report format is not a compatibility certificate. Pre-touch capture and full multi-contact release still must pass on the real Surface/spacedesk path. The update does not weaken these requirements or add an unsafe mouse-only fallback.

### Validation

Release gates include Windows compilation, the complete existing rendering/click-away/shortcut/topmost/launch/updater/installer tests, plus input-topology policy, test-pause reset, checklist, incomplete-contact and actual passive-listener lifecycle regressions. Native fixture tests use disposable windows/listeners, not the user's physical touchscreen. The installer remains unsigned.
'''
Path('docs/releases/v0.8.1.md').write_text(notes,encoding='utf-8')
p=Path('CHANGELOG.md');s=p.read_text(encoding='utf-8-sig');assert s.startswith('# Changelog\n\n')
p.write_text(s.replace('# Changelog\n\n','# Changelog\n\n## 0.8.1\n\n- Fix false digitizer blocks from known input-device arrival/keyboard/mouse notifications. Genuine digitizer or unknown topology changes remain fail-closed.\n- Reset stale passive-test evidence after UI pauses rather than latching a new fault; runtime gaps still require retesting.\n- Add a selected-device setup checklist and useful copied diagnostics; distinguish capabilities from actual contact/hover evidence and input screens from return destinations.\n- Preserve all saved settings and the complete 0.8.0 switcher, launch, rendering, shortcut and updater behaviour.\n\n',1),encoding='utf-8')
p=Path('docs/TOUCH-SUPPORT.md');s=p.read_text(encoding='utf-8-sig');s += '\n## 0.8.1 setup diagnostics\n\nThe selected-device checklist distinguishes report-format recognition from received input, hold/release, multitouch, pen hover exit and pre-touch snapshot evidence. The test never returns focus or cursor. Select the touchscreen as the input screen, not the main display as a destination. Keep non-touch screens disabled. After association use the screen trigger, Use draft, then master Enable and Apply. Copy diagnostics includes these draft and saved states. A test-UI pause resets measurements instead of permanently blocking the test; a genuine device/report fault still requires Restart test after the connection settles. Runtime processing gaps remain fail-closed.\n';p.write_text(s,encoding='utf-8')
print('Applied 0.8.1 detection/readiness repair. Windows regression gates must pass before publication.')
