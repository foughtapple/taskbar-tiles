// Touch Return runtime: passive observation, capability gating and one-shot verified return.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarTiles
{
    static class TouchShortcutKeys
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
    sealed class TouchInputObserver : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct Mouse { internal Native.POINT Point; internal uint Data,Flags,Time; internal UIntPtr Extra; }
        internal sealed class Sample { internal uint Tick; internal IntPtr Foreground; internal Point Cursor,Contact; internal bool Pen,Corroborated,Rejected; internal string Device = ""; }
        readonly ConcurrentQueue<Sample> anchors=new ConcurrentQueue<Sample>();
        readonly Native.HookProc mouseCallback,keyCallback;
        readonly Thread thread;
        volatile bool stopped;
        internal volatile bool Installed;
        uint threadId;
        int physicalVersion,typingVersion,navigationVersion,count;
        Point lastMouse;
        internal volatile uint LastPromotedTick;
        internal volatile int PromotedButtons;
        internal int PhysicalVersion { get { return Volatile.Read(ref physicalVersion); } }
        internal int TypingVersion { get { return Volatile.Read(ref typingVersion); } }
        internal int NavigationVersion { get { return Volatile.Read(ref navigationVersion); } }
        internal readonly List<Sample> Recent = new List<Sample>(); // UI thread only.
        internal TouchInputObserver()
        {
            lastMouse=Cursor.Position; mouseCallback=MouseEvent; keyCallback=KeyEvent;
            thread=new Thread(Run) { IsBackground=true,Name="Touch Return passive input observer" }; thread.SetApartmentState(ApartmentState.MTA); thread.Start();
        }
        void Run()
        {
            IntPtr mouse=IntPtr.Zero,key=IntPtr.Zero;
            try
            {
                threadId=Native.GetCurrentThreadId();
                using(var c=new Control())
                {
                    var h=c.Handle; mouse=Native.SetWindowsHookEx(14,mouseCallback,Native.GetModuleHandle(null),0);
                    key=Native.SetWindowsHookEx(13,keyCallback,Native.GetModuleHandle(null),0);
                    Installed=mouse!=IntPtr.Zero && key!=IntPtr.Zero;
                    if(!stopped) Application.Run();
                }
            }
            catch { Installed=false; Interlocked.Increment(ref physicalVersion); }
            finally { Installed=false; if(mouse!=IntPtr.Zero) Native.UnhookWindowsHookEx(mouse); if(key!=IntPtr.Zero) Native.UnhookWindowsHookEx(key); }
        }
        IntPtr MouseEvent(int code,IntPtr message,IntPtr data)
        {
            try
            {
                if(code>=0 && !stopped)
                {
                    var m=(Mouse)Marshal.PtrToStructure(data,typeof(Mouse)); int msg=message.ToInt32();
                    ulong extra=m.Extra.ToUInt64(); bool promoted=(extra & 0xFFFFFF00UL)==0xFF515700UL;
                    if(promoted)
                    {
                        LastPromotedTick=m.Time;
                        if(msg==0x201) PromotedButtons|=((extra & 0x80)==0?2:1);
                        if(msg==0x202) PromotedButtons&=~((extra & 0x80)==0?2:1);
                        if(msg==0x201 && Interlocked.Increment(ref count)<=32)
                            anchors.Enqueue(new Sample { Tick=m.Time,Foreground=Native.GetForegroundWindow(),Cursor=lastMouse,
                                Contact=new Point(m.Point.x,m.Point.y),Pen=(extra & 0x80)==0 });
                        else if(msg==0x201) Interlocked.Decrement(ref count);
                    }
                    else
                    {
                        // Unidentified or injected mouse input also cancels. Never infer
                        // touch simply from the monitor containing the pointer.
                        lastMouse=new Point(m.Point.x,m.Point.y); Interlocked.Increment(ref physicalVersion);
                    }
                }
            }
            catch { Interlocked.Increment(ref physicalVersion); }
            return Native.CallNextHookEx(IntPtr.Zero,code,message,data);
        }
        IntPtr KeyEvent(int code,IntPtr message,IntPtr data)
        {
            try
            {
                if(code>=0 && !stopped && (message.ToInt32()==0x100 || message.ToInt32()==0x104))
                {
                    var k=(Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(data,typeof(Native.KBDLLHOOKSTRUCT)); int v=(int)k.vkCode;
                    if(TouchShortcutKeys.IsShortcut(v)) return Native.CallNextHookEx(IntPtr.Zero,code,message,data);
                    if(v!=0x10 && v!=0x11 && v!=0x12 && !(v>=0xA0 && v<=0xA5)) Interlocked.Increment(ref typingVersion);
                    if(v==9 || v==0x5B || v==0x5C || v==0x1B) Interlocked.Increment(ref navigationVersion);
                }
            }
            catch { Interlocked.Increment(ref navigationVersion); }
            return Native.CallNextHookEx(IntPtr.Zero,code,message,data);
        }
        internal void Drain()
        {
            Sample s; while(anchors.TryDequeue(out s)) { Interlocked.Decrement(ref count); Recent.Add(s); }
            while(Recent.Count>32) { if(!Recent[0].Corroborated)Interlocked.Increment(ref physicalVersion); Recent.RemoveAt(0); }
        }
        public void Dispose() { if(stopped) return; stopped=true; if(threadId!=0) Native.PostThreadMessage(threadId,0x12,IntPtr.Zero,IntPtr.Zero); }
    }
    static class TouchReturnNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo
        { internal uint Size,Flags; internal IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret; internal Native.RECT CaretRect; }
        [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread,ref GuiInfo info);
        [DllImport("user32.dll")] internal static extern bool GetClipCursor(out Native.RECT rect);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr h);
        internal static bool MenusOrDialogs()
        {
            IntPtr h=Native.GetForegroundWindow(); if(h==IntPtr.Zero) return true;
            uint pid; uint thread=WindowNative.GetWindowThreadProcessId(h,out pid);
            var info=new GuiInfo {Size=(uint)Marshal.SizeOf(typeof(GuiInfo))};
            if(!GetGUIThreadInfo(thread,ref info)) return true;
            return (info.Flags & 0x1C)!=0 || info.MenuOwner!=IntPtr.Zero || Native.Class(h)=="#32770";
        }
    }
    sealed class TouchReturnService : IDisposable
    {
        internal static TouchReturnService Current;
        internal static bool TestActive { get { return Current != null && Current.Testing; } }
        readonly Dictionary<string,string> probeAnchors = new Dictionary<string,string>();
        readonly List<int> hotkeys = new List<int>();
        bool manualReturn;
        int rawMouseVersion, rawKeyVersion;
        internal bool HasProbeAnchor(string device, string screen) { string found; return fault.Length==0 && !environmentChanged && probeAnchors.TryGetValue(device,out found) && found==screen; }
        readonly TouchReturnEngine engine=new TouchReturnEngine();
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=40};
        readonly Control owner;
        readonly List<TouchFrame> delayed=new List<TouchFrame>();
        readonly List<string> history=new List<string>();
        RawTouchSource source;
        TouchInputObserver observer;
        Options options;
        List<TouchMonitorRule> rules=new List<TouchMonitorRule>();
        List<MonitorData> monitors=new List<MonitorData>();
        string layout="",lastStatus="",fault="";
        int mouseVersion,keyVersion,navigationVersion,tests;
        long lastPump;
        bool disposed,paused,restoring;
        volatile bool environmentChanged;
        internal bool Stay {get{return engine.Stay;}}
        internal bool Paused {get{return paused;}}
        internal bool Testing {get{return Volatile.Read(ref tests)>0;}}
        internal IEnumerable<TouchDeviceEvidence> Devices {get{return source==null?new TouchDeviceEvidence[0]:source.Devices;}}
        internal string Status {get{return fault.Length>0?"Blocked: "+fault:Testing?"Detection test only — no automatic return":paused?"Paused":engine.Status;}}
        internal string Report {get{return Status+Environment.NewLine+(source==null?"Input observer stopped":source.Status)+Environment.NewLine+
            "Snapshot policy: requires a matching pre-delivery touch/pen mouse-promotion anchor. No anchor = no automatic return."+Environment.NewLine+
            string.Join(Environment.NewLine,Devices.Select(d=>d+"; frames="+d.Frames+"; max contacts="+d.MaxContacts+"; hold="+d.HeldMilliseconds+" ms; release="+d.SawUp+"; hover="+d.HoverKnown))+
            Environment.NewLine+string.Join(Environment.NewLine,history);}}
        internal TouchReturnService(Control control,Options settings)
        {
            owner=control; Current=this; Configure(settings);
            timer.Tick+=delegate{Tick();}; timer.Start();
            SystemEvents.DisplaySettingsChanged+=ChangedDisplay;
            SystemEvents.PowerModeChanged+=ChangedPower;
            SystemEvents.SessionSwitch+=ChangedSession;
        }
        internal void Configure(Options settings)
        {
            options=settings.Clone(); manualReturn=false; engine.Cancel("settings changed"); delayed.Clear();
            try{rules=TouchRules.Parse(options.TouchMonitorRules);}catch{rules=new List<TouchMonitorRule>();fault="invalid monitor rules";}
            monitors=DisplayNative.Monitors(); layout=Fingerprint(monitors);
            RegisterShortcuts();
            engine.Enabled=options.TouchSupportEnabled; engine.WaitHover=options.TouchWaitForHover;
            if(options.TouchSupportEnabled || tests>0) Start(); else Stop();
        }
        internal static string Fingerprint(IEnumerable<MonitorData> screens)
        {return string.Join("|",screens.OrderBy(m=>m.Key).Select(m=>m.Key+":"+m.Bounds+":"+m.WorkArea));}
        void Start()
        {
            if(source!=null || disposed) return;
            fault=""; observer=new TouchInputObserver();
            try { source=new RawTouchSource(OnFrame,Block); }
            catch { observer.Dispose();observer=null;throw; }
            mouseVersion=observer.PhysicalVersion; keyVersion=observer.TypingVersion; navigationVersion=observer.NavigationVersion;rawMouseVersion=source.PhysicalVersion;rawKeyVersion=source.TypingVersion;
            lastPump=Environment.TickCount & int.MaxValue;
        }
        void Stop()
        {
            manualReturn=false; engine.ClearInput("monitor support stopped"); delayed.Clear();
            if(source!=null) source.Dispose(); source=null; if(observer!=null) observer.Dispose(); observer=null;
        }
        internal IDisposable DetectionTest()
        {
            tests++; engine.Cancel("input detection test"); if(tests==1) ResetDetection(); else Start();
            return new TouchTestLease(delegate{tests=Math.Max(0,tests-1);engine.Cancel("detection test finished");if(tests==0&&!options.TouchSupportEnabled)Stop();});
        }
        sealed class TouchTestLease:IDisposable {Action end;internal TouchTestLease(Action action){end=action;}public void Dispose(){var e=end;end=null;if(e!=null)e();}}
        internal void ResetDetection()
        {
            Stop(); fault=""; environmentChanged=false; probeAnchors.Clear(); Start(); engine.Cancel("test restarted; no automatic return during test");
        }
        void OnFrame(TouchFrame frame)
        {
            // Briefly defer policy processing to correlate Raw Input with passive
            // pre-delivery anchors. Contact data is never held back from the app.
            if(delayed.Count>=512){Block("input queue overflow; run detection again");delayed.Clear();return;}
            delayed.Add(frame);
        }
        void Block(string reason){fault=reason;manualReturn=false;engine.Cancel(reason);delayed.Clear();}
        internal void Cancel(string reason){manualReturn=false;engine.Cancel(reason);delayed.Clear();if(observer!=null)foreach(var anchor in observer.Recent)anchor.Rejected=true;}
        internal void SetPaused(bool value){paused=value;Cancel(value?"support paused":"support resumed; waiting for fresh interaction");}
        internal void SetStay(bool value){engine.SetStay(value,Clock());}
        static double Clock(){return Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency;}
        TouchReturnPoint Anchor(TouchFrame frame,MonitorData monitor)
        {
            observer.Drain();
            var a=observer.Recent.LastOrDefault(s=>s.Pen==frame.Pen && Math.Abs(unchecked((int)(frame.Tick-s.Tick)))<=150 && s.Corroborated && !s.Rejected && s.Device==frame.Device && TouchAnchorPolicy.Matches(frame,s.Contact,monitor.Bounds));
            if(a==null || a.Foreground==IntPtr.Zero || !Native.IsWindow(a.Foreground)) return null;
            // Some pointer stacks activate the target before mouse promotion. Do not
            // call that a pre-touch return point. This implementation intentionally
            // requires an origin window on a different screen; it never guesses back
            // through foreground history to resurrect an older application.
            if(Screen.FromHandle(a.Foreground).DeviceName==monitor.DeviceName) return null;
            uint pid=WindowNative.ProcessId(a.Foreground);
            if(pid==0 || pid==(uint)Process.GetCurrentProcess().Id) return null;
            long start=PackageIdentity.StartTicks(pid);if(start==0)return null;
            return new TouchReturnPoint{Window=a.Foreground,Pid=pid,Start=start,Cursor=a.Cursor,Layout=layout,Verified=true};
        }
        void Tick()
        {
            if(disposed || observer==null || source==null || restoring)return;
            if(environmentChanged){Cancel("display/session environment changed");return;}
            try
            {
                long pump=Environment.TickCount & int.MaxValue;
                if(lastPump!=0 && pump-lastPump>1500)Block("input processing paused; run detection again before automatic return");
                lastPump=pump;observer.Drain();
                bool moved=mouseVersion!=observer.PhysicalVersion || rawMouseVersion!=source.PhysicalVersion;
                bool typed=keyVersion!=observer.TypingVersion || rawKeyVersion!=source.TypingVersion;
                bool nav=navigationVersion!=observer.NavigationVersion;
                mouseVersion=observer.PhysicalVersion;keyVersion=observer.TypingVersion;navigationVersion=observer.NavigationVersion;rawMouseVersion=source.PhysicalVersion;rawKeyVersion=source.TypingVersion;
                if(moved || nav || (typed && options.TouchTypingCancels))
                {
                    foreach(var stale in observer.Recent)stale.Rejected=true;
                    manualReturn=false;engine.Cancel(moved?"physical/unclassified mouse or trackpad input":nav?"deliberate keyboard navigation":"typing");
                    // Do not allow an earlier queued raw report to create a new session
                    // after the user's deliberate input already cancelled it.
                    foreach(var f in delayed) engine.Activity(f,false,null,1000,Clock());delayed.Clear();
                }
                uint now=unchecked((uint)Environment.TickCount);
                while(delayed.Count>0 && unchecked((int)(now-delayed[0].Tick))>=90)
                {
                    var f=delayed[0];delayed.RemoveAt(0);
                    var pre=observer.Recent.LastOrDefault(s=>s.Pen==f.Pen && Math.Abs(unchecked((int)(f.Tick-s.Tick)))<=150);
                    if(pre!=null && !pre.Rejected && f.Valid && f.Complete && f.Down>0)
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
                    }
                    var matching=rules.Where(r=>r.Enabled && (f.Pen?r.Pen&&r.PenVerified&&r.PenDevice==f.Device:r.Touch&&r.TouchVerified&&r.TouchDevice==f.Device)).ToList();
                    var rule=matching.Count==1?matching[0]:null;
                    var screen=rule==null?null:monitors.SingleOrDefault(m=>m.Key==rule.Key);
                    var ev=Devices.FirstOrDefault(d=>d.Key==f.Device);
                    bool allowed=screen!=null && !string.IsNullOrEmpty(screen.Instance) && ev!=null&&ev.Supported && observer.Installed && source.Registered &&
                        fault.Length==0 && !Testing && !paused && (!f.Pen || !options.TouchWaitForHover || f.HoverKnown);
                    int delay=f.Pen?(rule!=null&&rule.PenDelay>=0?rule.PenDelay:options.PenReturnDelayMs):(rule!=null&&rule.TouchDelay>=0?rule.TouchDelay:options.TouchReturnDelayMs);
                    // Application exception strings are exact executable leaf names; no content is inspected.
                    string target=string.IsNullOrWhiteSpace(options.TouchExcludedApps)?"":ShellIcons.ProcessFile(Native.GetForegroundWindow());
                    if(options.TouchExcludedApps.Split(new[]{';',','},StringSplitOptions.RemoveEmptyEntries).Any(n=>n.Trim().Equals(Path.GetFileName(target),StringComparison.OrdinalIgnoreCase)))allowed=false;
                    engine.Enabled=options.TouchSupportEnabled && !Testing && !paused && fault.Length==0;
                    engine.Activity(f,allowed,engine.Saved!=null||screen==null?null:Anchor(f,screen),Math.Max(250,delay),Clock());
                }
                if(engine.Saved!=null && (!Native.IsWindow(engine.Saved.Window) || WindowNative.ProcessId(engine.Saved.Window)!=engine.Saved.Pid)) Cancel("saved window closed");
                if(engine.Saved!=null)
                {
                    IntPtr fg=Native.GetForegroundWindow();
                    if(fg==IntPtr.Zero)Cancel("foreground unavailable / secure desktop");
                    else if(WindowNative.ProcessId(fg)==(uint)Process.GetCurrentProcess().Id)Cancel("Taskbar Tiles controls opened");
                    else if(fg!=engine.Saved.Window && !monitors.Any(m=>rules.Any(r=>r.Enabled&&r.Key==m.Key)&&m.Bounds.IntersectsWith(WindowNative.VisibleBounds(fg))))Cancel("foreground moved away from enabled touchscreen");
                }
                foreach(var unknown in observer.Recent.Where(a=>!a.Corroborated&&!a.Rejected&&unchecked((int)(now-a.Tick))>250))
                { unknown.Rejected=true;Cancel("touch/pen promotion without a complete background input path"); }
                bool unknownPromotion=observer.LastPromotedTick!=0 && unchecked((int)(now-observer.LastPromotedTick))<300 &&
                    Math.Abs(unchecked((int)(observer.LastPromotedTick-source.LastDigitizerTick)))>180;
                if(unknownPromotion && unchecked((int)(now-observer.LastPromotedTick))>180)Cancel("unclassified touch/pen input");
                bool blocked=observer.PromotedButtons!=0 || unknownPromotion || observer.Recent.Any(a=>!a.Corroborated&&!a.Rejected) || delayed.Count>0 || Native.Down(0x10)||Native.Down(0x11)||Native.Down(0x12)||Native.Down(0x5B)||Native.Down(0x5C)||
                    (options.TouchPauseForMenus&&TouchReturnNative.MenusOrDialogs());
                var point=engine.Take(Clock(),manualReturn,blocked);if(point!=null){manualReturn=false;Restore(point);}
                RecordStatus();
            }
            catch(Exception ex){Block("input/return error: "+ex.GetType().Name);RecordStatus();}
        }
        void Restore(TouchReturnPoint saved)
        {
            if(disposed||Testing||paused||fault.Length>0||environmentChanged)return;
            if(!saved.Verified||!Native.IsWindow(saved.Window)||WindowNative.ProcessId(saved.Window)!=saved.Pid||PackageIdentity.StartTicks(saved.Pid)!=saved.Start ||
                saved.Layout!=Fingerprint(DisplayNative.Monitors())){engine.Status="Cancelled: saved window or display layout changed";return;}
            restoring=true;
            try
            {
                int input=observer.PhysicalVersion,key=observer.TypingVersion;
                if(input!=mouseVersion || (options.TouchTypingCancels&&key!=keyVersion) || observer.PromotedButtons!=0 || delayed.Count!=0){engine.Status="Cancelled: new input before return";return;}
                bool focus=options.TouchReturnAction!=2,cursor=options.TouchReturnAction!=1;
                if(focus && Native.GetForegroundWindow()!=saved.Window)
                {
                    // No retries or fake clicks. A busy/minimised target is left alone
                    // rather than returning halfway through its restore animation.
                    if(Native.IsIconic(saved.Window)) {Native.ShowWindowAsync(saved.Window,9);engine.Status="Skipped: saved window was minimised; restore requested without cursor jump";return;}
                    if(!Native.SetForegroundWindow(saved.Window)||Native.GetForegroundWindow()!=saved.Window){engine.Status="Failed: Windows did not grant foreground activation";return;}
                }
                if(input!=observer.PhysicalVersion || key!=observer.TypingVersion){engine.Status="Cancelled: input during return";return;}
                if(cursor)
                {
                    Native.RECT clip;
                    if(!TouchReturnNative.GetClipCursor(out clip)||!clip.Rectangle.Contains(saved.Cursor)){engine.Status="Skipped: saved cursor position is clipped; clipping was not changed";return;}
                    if(!Screen.AllScreens.Any(s=>s.Bounds.Contains(saved.Cursor)) || !TouchReturnNative.SetCursorPos(saved.Cursor.X,saved.Cursor.Y)){engine.Status="Failed: cursor return was not accepted";return;}
                }
                engine.Status="Returned";
            }
            finally{restoring=false;}
        }
        internal void ReturnNow() { if(engine.Saved!=null) manualReturn=true; }
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
                    if(!Native.RegisterHotKey(owner.Handle,0xB100+i,0x4000|mods,(uint)key))throw new InvalidOperationException();
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
        void RecordStatus()
        {if(Status==lastStatus)return;lastStatus=Status;history.Add(Status);while(history.Count>12)history.RemoveAt(0);}
        void PostCancel(string reason)
        {
            environmentChanged=true;
            try { owner.BeginInvoke(new Action(delegate
            {
                if(disposed)return;
                Cancel(reason); engine.ClearInput(reason); probeAnchors.Clear();
                monitors=DisplayNative.Monitors();layout=Fingerprint(monitors);
                // Require fresh neutral reports and verified snapshots after a device,
                // lock or power transition; never reuse a pre-transition destination.
                fault="Environment changed; use Restart test to recheck input before automatic return";
                environmentChanged=false;
            })); } catch { }
        }
        void ChangedDisplay(object sender,EventArgs e){PostCancel("display layout changed");}
        void ChangedPower(object sender,PowerModeChangedEventArgs e){PostCancel("power/sleep transition");}
        void ChangedSession(object sender,SessionSwitchEventArgs e){PostCancel("session lock/unlock");}
        public void Dispose()
        {
            if(disposed)return;disposed=true;timer.Stop();timer.Dispose();Stop();
            foreach(int id in hotkeys)Native.UnregisterHotKey(owner.Handle,id);hotkeys.Clear();TouchShortcutKeys.Registered=new Keys[0];
            SystemEvents.DisplaySettingsChanged-=ChangedDisplay;SystemEvents.PowerModeChanged-=ChangedPower;SystemEvents.SessionSwitch-=ChangedSession;
            if(Current==this)Current=null;
        }
    }
}
