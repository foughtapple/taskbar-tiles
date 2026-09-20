from pathlib import Path

def edit(path,old,new):
    p=Path(path);s=p.read_text(encoding='utf-8-sig');assert s.count(old)==1,(path,old[:90],s.count(old));p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
k='src/TaskbarTiles/ShortcutReliability.cs'
edit(k,'        uint threadId;','''        uint threadId;
        Thread worker;
        readonly object workerGate = new object();
        readonly System.Windows.Forms.Timer supervisor = new System.Windows.Forms.Timer { Interval = 1000 };
        internal bool WorkerRunning { get { return worker != null && worker.IsAlive; } }
        void StartWorker()
        {
            lock (workerGate)
            {
                if (stopped || WorkerRunning) return;
                worker = new Thread(Run) { IsBackground = true, Name = "Taskbar Tiles shortcut pump" };
                worker.SetApartmentState(ApartmentState.MTA); worker.Start();
            }
        }''')
edit(k,'''            var worker = new Thread(Run) { IsBackground = true, Name = "Taskbar Tiles shortcut pump" };
            worker.SetApartmentState(ApartmentState.MTA); worker.Start();''','''            supervisor.Tick += delegate { if (!stopped && Enabled && !WorkerRunning) StartWorker(); };
            supervisor.Start(); StartWorker();''')
edit(k,'        public void Repair() { Interlocked.Exchange(ref repairRequested, 1); }','''        public void Repair() { Interlocked.Exchange(ref repairRequested, 1); if (!stopped) StartWorker(); }
        internal void TestStopPump() { if (threadId != 0) Native.PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero); }''')
edit(k,'''        void Enqueue(int signal)
        {
            if (Interlocked.Increment(ref queued) > 64) { Interlocked.Decrement(ref queued); return; }
            signals.Enqueue(signal);
        }''','''        bool Enqueue(int signal)
        {
            if (Interlocked.Increment(ref queued) > 64) { Interlocked.Decrement(ref queued); return false; }
            signals.Enqueue(signal); return true;
        }''')
edit(k,'                    else if (Pressed != null) Pressed','                    else if (Enabled && !TouchReturnService.TestActive && Pressed != null) Pressed')
edit(k,'''                        if (down && Enabled && alt)
                        {
                            swallowTabUp = session = true;
                            bool ctrl = modifiers[0x11] || modifiers[0xA2] || modifiers[0xA3];
                            bool shift = modifiers[0x10] || modifiers[0xA0] || modifiers[0xA1];
                            Enqueue((ctrl ? 1 : 0) | (shift ? 2 : 0)); return new IntPtr(1);
                        }''','''                        if (down && Enabled && alt && !TouchReturnService.TestActive)
                        {
                            bool ctrl = modifiers[0x11] || modifiers[0xA2] || modifiers[0xA3];
                            bool shift = modifiers[0x10] || modifiers[0xA0] || modifiers[0xA1];
                            if (Enqueue((ctrl ? 1 : 0) | (shift ? 2 : 0)))
                            { swallowTabUp = session = true; return new IntPtr(1); }
                        }''')
edit(k,'if (stopped) return; stopped = true; Enabled = false; delivery.Stop(); delivery.Dispose();','if (stopped) return; stopped = true; Enabled = false; delivery.Stop(); delivery.Dispose(); supervisor.Stop(); supervisor.Dispose();')
edit(k,'        void NavigationFailed(Exception ex)','''        internal void HandleUiException(Exception ex)
        {
            if (closing) return;
            try { NavigationFailed(ex); }
            catch (Exception failure) { ShortcutDiagnostics.Write("UI recovery failed: " + failure.GetType().Name); if (hook != null) hook.Repair(); }
        }
        void NavigationFailed(Exception ex)''')
main='src/TaskbarTiles/TaskbarTiles.cs'
edit(main,'Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Log(e.Exception.ToString()); };','''Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                    {
                        Log(e.Exception.ToString());
                        var resident = Application.OpenForms.OfType<Switcher>().FirstOrDefault();
                        if (resident != null) resident.HandleUiException(e.Exception);
                    };
                    AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                    { Log(Convert.ToString(e.ExceptionObject)); ShortcutDiagnostics.Write("unhandled exception; terminating=" + e.IsTerminating); };''')
t='src/TaskbarTiles/TouchReturnRuntime.cs'
edit(t,'internal bool Pen,Corroborated,Rejected;','internal bool Pen,Corroborated,Rejected; internal string Device = "";')
edit(t,'return probeAnchors.TryGetValue(device,out found) && found==screen;','return fault.Length==0 && !environmentChanged && probeAnchors.TryGetValue(device,out found) && found==screen;')
edit(t,'internal bool Testing {get{return tests>0;}}','internal bool Testing {get{return Volatile.Read(ref tests)>0;}}')
edit(t,'tests++; engine.Cancel("input detection test"); Start();','tests++; engine.Cancel("input detection test"); if(tests==1) ResetDetection(); else Start();')
edit(t,'Math.Abs(unchecked((int)(frame.Tick-s.Tick)))<=150 && monitor.Bounds.Contains(s.Contact)','Math.Abs(unchecked((int)(frame.Tick-s.Tick)))<=150 && s.Corroborated && !s.Rejected && s.Device==frame.Device && TouchAnchorPolicy.Matches(frame,s.Contact,monitor.Bounds)')
edit(t,'''                    if(pre!=null && f.Valid)pre.Corroborated=true;
                    if(pre!=null && pre.Foreground!=IntPtr.Zero && WindowNative.ProcessId(pre.Foreground)!=(uint)Process.GetCurrentProcess().Id)
                    { var observedMonitor=monitors.FirstOrDefault(m=>m.Bounds.Contains(pre.Contact)); if(observedMonitor!=null && Native.IsWindow(pre.Foreground) && Screen.FromHandle(pre.Foreground).DeviceName!=observedMonitor.DeviceName)probeAnchors[f.Device]=observedMonitor.Key; }''','''                    if(pre!=null && !pre.Rejected && f.Valid && f.Complete && f.Down>0)
                    {
                        var observedMonitor=monitors.FirstOrDefault(m=>TouchAnchorPolicy.Matches(f,pre.Contact,m.Bounds));
                        if(observedMonitor!=null)
                        {
                            if(pre.Corroborated && pre.Device!=f.Device)
                            {
                                pre.Rejected=true; probeAnchors.Remove(pre.Device); probeAnchors.Remove(f.Device);
                                Block("ambiguous digitizer-to-pointer correlation; retest one device at a time");
                            }
                            else
                            {
                                pre.Corroborated=true; pre.Device=f.Device;
                                if(pre.Foreground!=IntPtr.Zero && Native.IsWindow(pre.Foreground) && WindowNative.ProcessId(pre.Foreground)!=(uint)Process.GetCurrentProcess().Id &&
                                    Screen.FromHandle(pre.Foreground).DeviceName!=observedMonitor.DeviceName)probeAnchors[f.Device]=observedMonitor.Key;
                            }
                        }
                    }''')
edit(t,'                Cancel(reason); engine.ClearInput(reason);','                Cancel(reason); engine.ClearInput(reason); probeAnchors.Clear();')
edit(t,'internal void Cancel(string reason){manualReturn=false;engine.Cancel(reason);delayed.Clear();}', 'internal void Cancel(string reason){manualReturn=false;engine.Cancel(reason);delayed.Clear();if(observer!=null)foreach(var anchor in observer.Recent)anchor.Rejected=true;}')
edit(t,'                    manualReturn=false;engine.Cancel(moved?', '                    foreach(var stale in observer.Recent)stale.Rejected=true;\n                    manualReturn=false;engine.Cancel(moved?')
edit('src/TaskbarTiles/TouchSupport.cs','bool entering = (f.Down > 0 && old.Down == 0) || (f.Hover && !old.Hover && old.Down == 0);','bool entering = f.Complete && ((f.Down > 0 && (old.Down == 0 || old.Partial)) || (f.Hover && !old.Hover && old.Down == 0));')
s='src/TaskbarTiles/SettingsTouchSupport.cs'
edit(s,'Add(footer,"Cancel",100,delegate{DialogResult=DialogResult.Cancel;Close();});','''Add(footer,"Cancel",100,delegate{DialogResult=DialogResult.Cancel;Close();});
            Add(footer,"Copy diagnostics",155,delegate{var service=TouchReturnService.Current;if(service!=null)Clipboard.SetText("Taskbar Tiles "+Program.Version+Environment.NewLine+service.Report);});''')
for name in ['build.yml','release.yml']:
    f='.github/workflows/'+name
    marker='      - name: Test native switcher topmost ordering'
    edit(f,marker,'''      - name: Test native outside-click dismissal
        shell: powershell
        run: ./tools/Test-OutsideClick.ps1
      - name: Test Touch Return policy and shortcut recovery
        shell: powershell
        run: ./tools/Test-TouchSupport.ps1
'''+marker)
edit('.github/workflows/release.yml',"            helper_tests = 'passed'","            helper_tests = 'passed'\n            native_clickaway_tests = 'passed on disposable nonactivating windows'\n            touch_shortcut_tests = 'passed: policy, native listener lifecycle and hook recovery; no hardware compatibility claim'")
edit('src/TaskbarTiles/TaskbarTiles.csproj','    <Compile Include="TouchSupportTests.cs" />','    <Compile Include="TouchSupportTests.cs" />\n    <Compile Include="TouchAnchorPolicy.cs" />')
tests='src/TaskbarTiles/TouchSupportTests.cs'
edit(tests,'            checks=0;var e=Engine();var p=Anchor();','''            checks=0;var e=Engine();var p=Anchor();
            var bounds=new Rectangle(1000,0,1000,1000); var coordinate=F("position",false,true);
            Require(TouchAnchorPolicy.Matches(coordinate,new Point(1500,500),bounds),"matching normalized contact and promoted screen point");
            Require(!TouchAnchorPolicy.Matches(coordinate,new Point(1800,500),bounds),"nearby time alone cannot associate a device");
            Require(!TouchAnchorPolicy.Matches(coordinate,new Point(500,500),bounds),"wrong monitor rejected");
            coordinate.Complete=false;Require(!TouchAnchorPolicy.Matches(coordinate,new Point(1500,500),bounds),"incomplete HID frame cannot certify an anchor");
            e.Activity(coordinate,true,p,1000,0);Require(e.Saved==null,"partial first frame cannot save an unverified return point");
            coordinate.Complete=true;e.Activity(coordinate,true,p,1000,1);Require(e.Saved==p,"complete continuation can start a verified session");e=Engine();
            coordinate.Contacts[0].X=double.NaN;Require(!TouchAnchorPolicy.Matches(coordinate,new Point(1500,500),bounds),"invalid coordinate rejected");''')
edit(tests,'                    Require(delivered==1,"registration test generates no extra shortcut or input");','''                    Require(delivered==1,"registration test generates no extra shortcut or input");
                    hook.TestStopPump();wait.Restart();while(hook.WorkerRunning&&wait.ElapsedMilliseconds<3500){Application.DoEvents();Thread.Sleep(10);}
                    Require(!hook.WorkerRunning,"test pump stopped without affecting other applications");
                    hook.Enabled=true;int prior=hook.Generation;hook.Repair();wait.Restart();
                    while((!hook.WorkerRunning||hook.Generation==prior)&&wait.ElapsedMilliseconds<3500){Application.DoEvents();Thread.Sleep(10);}
                    Require(hook.WorkerRunning&&hook.Generation>prior,"explicit repair recreates a stopped hook worker");''')
notes='docs/releases/v0.8.0.md'
edit(notes,'The keyboard hook now queues events rather than invoking UI work from the low-level callback.','The keyboard hook now queues events rather than invoking UI work from the low-level callback. A stopped input thread is recreated, and the central UI-error handler uses the same bounded recovery path.')
edit(notes,'The passive test intentionally stays open while you use other applications; automatic return is disabled for its entire lifetime.','The passive test intentionally stays open while you use other applications; automatic return and Taskbar Tiles Alt+Tab interception are disabled for its entire lifetime. Each test starts with fresh evidence. Use **Copy diagnostics** to share its local report; no report is uploaded automatically.')
edit(notes,'requires a matching pre-delivery touch/pen mouse-promotion snapshot from a different screen.','requires a matching pre-delivery touch/pen mouse-promotion snapshot from a different screen, corroborated by the contact coordinates. Ambiguous device correlations are rejected.')
print('Final review edits applied. Windows compilation and regression gates must pass.')
