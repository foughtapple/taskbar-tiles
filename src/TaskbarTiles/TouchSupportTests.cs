using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
namespace TaskbarTiles
{
    static class TouchSupportTests
    {
        static int checks;
        static void Require(bool ok,string label){checks++;if(!ok)throw new InvalidOperationException("FAILED: "+label);}
        static TouchContact C(int id,bool down){return new TouchContact {Id=id,Down=down,X=.5,Y=.5};}
        static TouchFrame F(string device,bool pen,bool down,bool hover=false){return new TouchFrame {Device=device,Pen=pen,HoverKnown=pen,Hover=hover,Contacts=new List<TouchContact>{C(1,down)}};}
        static TouchReturnPoint Anchor(){return new TouchReturnPoint {Window=new IntPtr(12),Pid=3,Start=40,Cursor=new Point(10,20),Layout="fixture",Verified=true};}
        static TouchReturnEngine Engine(){return new TouchReturnEngine {Enabled=true,WaitHover=true};}
        internal static void Run(StringBuilder log)
        {
            checks=0;var e=Engine();var p=Anchor();
            var bounds=new Rectangle(1000,0,1000,1000); var coordinate=F("position",false,true);
            Require(TouchAnchorPolicy.Matches(coordinate,new Point(1500,500),bounds),"matching normalized contact and promoted screen point");
            Require(!TouchAnchorPolicy.Matches(coordinate,new Point(1800,500),bounds),"nearby time alone cannot associate a device");
            Require(!TouchAnchorPolicy.Matches(coordinate,new Point(500,500),bounds),"wrong monitor rejected");
            coordinate.Complete=false;Require(!TouchAnchorPolicy.Matches(coordinate,new Point(1500,500),bounds),"incomplete HID frame cannot certify an anchor");
            e.Activity(coordinate,true,p,1000,0);Require(e.Saved==null,"partial first frame cannot save an unverified return point");
            coordinate.Complete=true;e.Activity(coordinate,true,p,1000,1);Require(e.Saved==p,"complete continuation can start a verified session");e=Engine();
            coordinate.Contacts[0].X=double.NaN;Require(!TouchAnchorPolicy.Matches(coordinate,new Point(1500,500),bounds),"invalid coordinate rejected");
            e.Activity(F("touch",false,true),true,p,1000,0);
            Require(e.Take(999999,false,false)==null,"stationary held finger never times out");
            e.Activity(F("touch",false,false),true,null,1000,10000);
            Require(e.Take(10999,false,false)==null&&e.Take(11000,false,false)==p,"delay starts after explicit final release");
            e=Engine();e.Activity(F("one",false,true),true,p,1000,0);e.Activity(F("two",false,true),true,Anchor(),1000,1);e.Activity(F("one",false,false),true,null,1000,2);
            Require(e.Take(99999,true,false)==null,"second device/contact blocks even manual return");e.Activity(F("two",false,false),true,null,1000,99999);Require(e.Take(100999,false,false)==p,"one shared original return point");
            e=Engine();e.Activity(F("pen",true,true,true),true,p,2000,0);e.Activity(F("pen",true,false,true),true,null,2000,1);
            Require(e.Take(100000,true,false)==null,"pen hover blocks automatic and manual return");e.Activity(F("pen",true,false,false),true,null,2000,100001);Require(e.Take(102001,false,false)==p,"pen leaves range then countdown");
            e=Engine();e.Activity(F("pen",true,false,true),true,null,2000,0);e.Activity(F("pen",true,true,true),true,p,2000,1);Require(e.Saved==p,"pen contact can capture after unanchored hover");
            e=Engine();e.Activity(F("touch",false,true),true,p,1000,0);e.Activity(F("touch",false,false),true,null,1000,1);e.SetStay(true,10);
            Require(e.Take(99999,false,false)==null,"Stay here inhibits overdue countdown");e.SetStay(false,100000);Require(e.Take(100999,false,false)==null&&e.Take(101000,false,false)==p,"leaving Stay starts fresh countdown");
            foreach(string why in new[]{"physical mouse","scroll","keyboard navigation","typing","sleep","display disconnected","new task"})
            {e=Engine();e.Activity(F("touch",false,true),true,p,1000,0);e.Cancel(why);e.Activity(F("touch",false,false),true,null,1000,2);Require(e.Take(99999,false,false)==null,"cancel "+why);}
            e=Engine();e.Activity(F("touch",false,true),false,p,1000,0);Require(e.Saved==null,"disabled or unverified monitor never arms");
            e=Engine();p.Verified=false;e.Activity(F("touch",false,true),true,p,1000,0);Require(e.Saved==null,"unknown pre-touch state never guessed");p.Verified=true;
            e=Engine();e.Activity(F("touch",false,true),true,p,1000,0);e.Activity(F("pen",true,true,true),true,null,2000,2);Require(e.Delay==2000,"mixed session keeps longer timeout");
            e.Activity(F("touch",false,false),true,null,1000,3);Require(e.Delay==2000,"finger does not shorten pen delay");
            e=Engine();e.Activity(F("touch",false,true),true,p,1000,0);var incomplete=F("touch",false,false);incomplete.Complete=false;e.Activity(incomplete,true,null,1000,1);Require(e.Take(99999,true,false)==null,"incomplete frame never treated as all up");
            var a=new TouchFrameAssembler();Require(a.Feed(3,new[]{C(1,true),C(2,true)})==null&&a.Partial,"hybrid frame incomplete");var r=a.Feed(0,new[]{C(3,true),C(99,false)});Require(r.Count==3&&r.All(c=>c.Down),"hybrid zero count is continuation not UP");
            a.Feed(3,new[]{C(1,false),C(2,true),C(3,true)});Require(a.Feed(2,new[]{C(2,false),C(3,false)}).All(c=>!c.Down),"all active IDs have explicit release");
            Require(a.Feed(0,new[]{C(0,false)}).Count==0,"neutral frame after explicit release");
            foreach(int scenario in new[]{0,1,2})
            {a=new TouchFrameAssembler();a.Feed(1,new[]{C(1,true)});bool failed=false;try{if(scenario==0)a.Feed(0,new[]{C(1,false)});else if(scenario==1)a.Feed(1,new[]{C(2,true)});else a.Feed(2,new[]{C(1,true),C(1,true)});}catch(InvalidOperationException){failed=true;}Require(failed,"reject missing UP/ID/duplicate "+scenario);}
            var rules=new List<TouchMonitorRule>{new TouchMonitorRule {Key="hardware-id",Nickname="Surface",Enabled=true,TouchDelay=1300}};
            var parsed=TouchRules.Parse(TouchRules.Save(rules));Require(parsed[0].Key=="hardware-id"&&parsed[0].Nickname=="Surface"&&!parsed[0].TouchVerified,"rules preserve hardware identity but never auto-verify");
            var settings=Options.Parse(new[]{"TouchSupportEnabled=true","TouchReturnDelayMs=1","PenReturnDelayMs=999999"});Require(settings.TouchReturnDelayMs==250&&settings.PenReturnDelayMs==60000,"timing bounds");Require(!new Options().TouchSupportEnabled,"default off, detection first");
            Require(Marshal.SizeOf(typeof(TouchHidNative.DeviceRegistration))==(IntPtr.Size==8?16:12),"RAWINPUTDEVICE ABI");
            Require(Marshal.SizeOf(typeof(TouchHidNative.DeviceList))==(IntPtr.Size==8?16:8),"RAWINPUTDEVICELIST ABI");
            Require(Marshal.SizeOf(typeof(TouchReturnNative.GuiInfo))==(IntPtr.Size==8?72:48),"GUITHREADINFO ABI");
            log.AppendLine("PASS: "+checks+" Touch Return policy, contact-frame, cancellation, settings and native-layout assertions. No input or focus changes.");
        }
        internal static int RunNative()
        {
            var log=new StringBuilder();
            try
            {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Run(log);
                using(var source=new RawTouchSource(delegate{},delegate{}))
                {Require(source.Registered,"passive digitizer registrations accepted on Windows");source.Scan();log.AppendLine("PASS: actual passive Raw Input registration/enumeration/disposal. No pointer capture or input replay.");}
                int delivered=0,thread=Thread.CurrentThread.ManagedThreadId;
                using(var hook=new KeyboardHook())
                {
                    Require(hook.Installed,"keyboard hook registered");
                    hook.Pressed=delegate{Require(Thread.CurrentThread.ManagedThreadId==thread,"shortcut delivered outside low-level pump on UI thread");delivered++;};
                    hook.TestDeliver();Require(delivered==1,"queued delivery reached UI callback");
                    int first=hook.Generation;hook.TestRevoke();
                    var wait=Stopwatch.StartNew();while(hook.Generation==first&&wait.ElapsedMilliseconds<3500){Application.DoEvents();Thread.Sleep(10);}
                    Require(hook.Generation>first,"removed hook repaired without application restart or probe input");
                    hook.Enabled=false;hook.Repair();wait.Restart();int next=hook.Generation;while(hook.Generation==next&&wait.ElapsedMilliseconds<3500){Application.DoEvents();Thread.Sleep(10);}
                    Require(!hook.Enabled,"repair preserves deliberate disabled state");
                    Require(delivered==1,"registration test generates no extra shortcut or input");
                    hook.TestStopPump();wait.Restart();while(hook.WorkerRunning&&wait.ElapsedMilliseconds<3500){Application.DoEvents();Thread.Sleep(10);}
                    Require(!hook.WorkerRunning,"test pump stopped without affecting other applications");
                    hook.Enabled=true;int prior=hook.Generation;hook.Repair();wait.Restart();
                    while((!hook.WorkerRunning||hook.Generation==prior)&&wait.ElapsedMilliseconds<3500){Application.DoEvents();Thread.Sleep(10);}
                    Require(hook.WorkerRunning&&hook.Generation>prior,"explicit repair recreates a stopped hook worker");
                }
                log.AppendLine("PASS: native shortcut registration, forced revocation/repair and disabled preference. No user key was synthesised.");
                using(var window=new Form())
                using(var service=new TouchReturnService(window,new Options()))
                {Require(!service.Devices.Any(),"disabled support installs no digitizer listeners");using(service.DetectionTest()){Require(service.Testing,"diagnostic mode cannot auto-return");}Require(!service.Testing,"diagnostic lease released");}
                using(var settings=new SettingsWindow(new Options(),delegate(Options o){throw new InvalidOperationException("Native UI test must never save");},null,new List<AppButton>(),"Touch screen monitor support"))
                using(var image=new Bitmap(settings.Width,settings.Height))
                { settings.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));Require(settings.Controls.Count>0,"actual Settings pages construct and paint without saving"); }
                using(var monitor=new TouchMonitorDialog(""))
                using(var image=new Bitmap(monitor.Width,monitor.Height))
                { monitor.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));Require(monitor.Controls.Count>0,"monitor test UI constructs without enabling or saving devices"); }
                log.AppendLine("Hardware compatibility NOT established: no Surface, spacedesk, Apollo, pen or real digitizer was exercised. Local passive acceptance test remains mandatory.");
                File.WriteAllText(Path.Combine(Program.Home,"touch-shortcut-test.log"),log.ToString());return 0;
            }
            catch(Exception ex){log.AppendLine(ex.ToString());File.WriteAllText(Path.Combine(Program.Home,"touch-shortcut-test.log"),log.ToString());return 1;}
        }
    }
}
