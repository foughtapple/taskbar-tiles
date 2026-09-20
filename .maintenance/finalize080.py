from pathlib import Path
B=Path('src/TaskbarTiles')
def edit(path,old,new):
 p=Path(path);s=p.read_text(encoding='utf-8-sig');assert s.count(old)==1,(str(path),old[:100],s.count(old));p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
# Always call DefWindowProc for WM_INPUT cleanup, including filtered packets.
r=B/'TouchRawInput.cs'
edit(r,'        protected override void WndProc(ref Message m)\n        {','''        protected override void WndProc(ref Message m)
        {
            try { ReadMessage(ref m); }
            finally { base.WndProc(ref m); }
        }
        void ReadMessage(ref Message m)
        {''')
edit(r,'            base.WndProc(ref m); // DefWindowProc cleans up foreground WM_INPUT.','')
# A pen held in range blocks return even when no primary-mouse promotion occurred.
# Require complete reported frames and fresh, cross-screen pre-activation evidence.
r=B/'TouchReturnRuntime.cs'
edit(r,'        void Block(string reason){fault=reason;engine.Cancel(reason);delayed.Clear();}','        void Block(string reason){fault=reason;manualReturn=false;engine.Cancel(reason);delayed.Clear();}')
edit(r,'        internal void Cancel(string reason){engine.Cancel(reason);delayed.Clear();}','        internal void Cancel(string reason){manualReturn=false;engine.Cancel(reason);delayed.Clear();}')
edit(r,'            options=settings.Clone(); engine.Cancel("settings changed"); delayed.Clear();','            options=settings.Clone(); manualReturn=false; engine.Cancel("settings changed"); delayed.Clear();')
edit(r,'            if(a==null || a.Foreground==IntPtr.Zero || !Native.IsWindow(a.Foreground)) return null;','''            if(a==null || a.Foreground==IntPtr.Zero || !Native.IsWindow(a.Foreground)) return null;
            // Some pointer stacks activate the target before mouse promotion. Do not
            // call that a pre-touch return point. This implementation intentionally
            // requires an origin window on a different screen; it never guesses back
            // through foreground history to resurrect an older application.
            if(Screen.FromHandle(a.Foreground).DeviceName==monitor.DeviceName) return null;''')
edit(r,'if(observedMonitor!=null)probeAnchors[f.Device]=observedMonitor.Key;','if(observedMonitor!=null && Native.IsWindow(pre.Foreground) && Screen.FromHandle(pre.Foreground).DeviceName!=observedMonitor.DeviceName)probeAnchors[f.Device]=observedMonitor.Key;')
edit(r,'            engine.ClearInput("monitor support stopped"); delayed.Clear();','            manualReturn=false; engine.ClearInput("monitor support stopped"); delayed.Clear();')
edit(r,'        internal void SetPaused(bool value){paused=value;Cancel(value?"support paused":"support resumed; waiting for fresh interaction");}','        internal void SetPaused(bool value){paused=value;Cancel(value?"support paused":"support resumed; waiting for fresh interaction");}')
# Avoid synchronous process enumeration for every high-rate pen report; only one
# identity snapshot can be created per session, never on subsequent contact frames.
edit(r,'engine.Activity(f,allowed,screen==null?null:Anchor(f,screen),Math.Max(250,delay),Clock());','engine.Activity(f,allowed,engine.Saved!=null||screen==null?null:Anchor(f,screen),Math.Max(250,delay),Clock());')
# Cancel/lock before UI dispatch too: the return guard sees invalidation even while
# a SystemEvents callback is waiting in the UI queue. Configuration does not clear it.
edit(r,'        bool disposed,paused,restoring;','        bool disposed,paused,restoring;\n        volatile bool environmentChanged;')
edit(r,'            if(disposed || observer==null || source==null || restoring)return;','            if(disposed || observer==null || source==null || restoring)return;\n            if(environmentChanged){Cancel("display/session environment changed");return;}')
edit(r,'            if(disposed||Testing||paused||fault.Length>0)return;','            if(disposed||Testing||paused||fault.Length>0||environmentChanged)return;')
edit(r,'        void PostCancel(string reason)\n        {try{owner.BeginInvoke(new Action(delegate{if(!disposed){Cancel(reason);monitors=DisplayNative.Monitors();layout=Fingerprint(monitors);}}));}catch{}}','''        void PostCancel(string reason)
        {
            environmentChanged=true;
            try { owner.BeginInvoke(new Action(delegate
            {
                if(disposed)return;
                Cancel(reason); engine.ClearInput(reason);
                monitors=DisplayNative.Monitors();layout=Fingerprint(monitors);
                // Require fresh neutral reports and verified snapshots after a device,
                // lock or power transition; never reuse a pre-transition destination.
                fault="Environment changed; use Restart test to recheck input before automatic return";
                environmentChanged=false;
            })); } catch { }
        }''')
edit(r,'            Stop(); fault=""; probeAnchors.Clear(); Start();','            Stop(); fault=""; environmentChanged=false; probeAnchors.Clear(); Start();')
# Discard manual request when the saved destination is gone; no cursor clipping bypass.
edit(r,'                if(engine.Saved!=null)\n                {\n                    IntPtr fg=','''                if(engine.Saved!=null && (!Native.IsWindow(engine.Saved.Window) || WindowNative.ProcessId(engine.Saved.Window)!=engine.Saved.Pid)) Cancel("saved window closed");
                if(engine.Saved!=null)
                {
                    IntPtr fg=''')
# Entering and exiting a diagnostic lease must reset startup time to avoid treating
# initial HID enumeration as a stalled input session.
edit(r,'            mouseVersion=observer.PhysicalVersion; keyVersion=observer.TypingVersion; navigationVersion=observer.NavigationVersion;','            mouseVersion=observer.PhysicalVersion; keyVersion=observer.TypingVersion; navigationVersion=observer.NavigationVersion;rawMouseVersion=source.PhysicalVersion;rawKeyVersion=source.TypingVersion;')
# Keep this optional module from taking down the switcher if a hardware driver fails.
r=B/'SettingsTouchSupport.cs'
edit(r,'        void SetupTouchSupport(){touchService=new TouchReturnService(this,options);}','''        void SetupTouchSupport()
        {
            try { touchService=new TouchReturnService(this,options); }
            catch(Exception ex){Program.Log("Touch support unavailable: "+ex);Notify("Touch screen monitor support could not initialise. The switcher is still available; see TaskbarTiles.log.");}
        }''')
edit(r,'It is an explicit diagnostic exception','It is an explicit diagnostic exception') if False else None
# UI records full-monitor hardware key, never just DISPLAYn; hand-confirm the
# actual drawing path after the passive evidence checks.
edit(r,'A matching pre-touch foreground/cursor snapshot has not been observed on that display.','A matching pre-touch foreground/cursor snapshot from a different screen has not been observed on that display.')
# CI filenames and provenance. No published release/tag is changed in preparation.
for file in ['.github/workflows/build.yml','.github/workflows/release.yml']:
 p=Path(file);s=p.read_text();s=s.replace('./tools/Test-ClickAway.ps1','./tools/Test-OutsideClick.ps1');p.write_text(s,encoding='utf-8',newline='\n')
edit('.github/workflows/release.yml',"            helper_tests = 'passed'","""            helper_tests = 'passed'
            touch_return_policy_and_shortcuts = 'passed: engine/contact cancellation tests, native passive registrations, hook renewal; no actual touch hardware'
            outside_click_tests = 'passed on disposable separate-process nonactivating windows'
            touchscreen_hardware_compatibility = 'NOT certified; local detection test required; unsupported input remains inactive'""")
print('Safety review edits applied; hardware detection and cross-screen pre-touch evidence remain mandatory.')
