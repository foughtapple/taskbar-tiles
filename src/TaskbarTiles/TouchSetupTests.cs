using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class TouchSetupTests
    {
        static int checks;
        static void Require(bool ok,string message)
        { checks++; if(!ok)throw new InvalidOperationException("Touch setup regression: "+message); }
        static bool Ready(TouchDeviceEvidence d,bool anchor=true,bool hover=true,string fault="")
        { return TouchSetupChecklist.Ready(d,true,true,anchor,hover,fault); }
        internal static void Run(StringBuilder log)
        {
            checks=0;
            foreach(var kind in new[]{TouchInputKind.Mouse,TouchInputKind.Keyboard,TouchInputKind.Other})
                foreach(int change in new[]{1,2}) foreach(bool known in new[]{false,true})
                    Require(TouchSetupPolicy.DeviceChange(change,known,kind)==TouchChangeAction.Cancel,
                        "non-digitizer changes cancel only, not a permanent provider fault");
            Require(TouchSetupPolicy.DeviceChange(1,true,TouchInputKind.Digitizer)==TouchChangeAction.Ignore,"known enumerated arrival is not a topology failure");
            Require(TouchSetupPolicy.DeviceChange(1,false,TouchInputKind.Digitizer)==TouchChangeAction.Retest,"new digitizer requires retest");
            Require(TouchSetupPolicy.DeviceChange(2,true,TouchInputKind.Digitizer)==TouchChangeAction.Retest,"digitizer removal remains fail-closed");
            foreach(int change in new[]{1,2,99}) foreach(bool known in new[]{false,true})
                Require(TouchSetupPolicy.DeviceChange(change,known,TouchInputKind.Unknown)==TouchChangeAction.Retest,"unknown changes never treated as safe");
            Require(!TouchSetupPolicy.PumpGap(10,40)&&TouchSetupPolicy.PumpGap(10,2000),"watchdog threshold");
            Require(!TouchSetupPolicy.PumpGap(uint.MaxValue-100,10)&&TouchSetupPolicy.PumpGap(uint.MaxValue-2000,10),"tick rollover arithmetic");
            var d=new TouchDeviceEvidence {Key="touch",Kind="Touch",Supported=true,Frames=15,MaxContacts=1,HeldMilliseconds=110,SawUp=true};
            Require(!Ready(d),"reported 15 short single-contact frames do not pass long-hold/multitouch checks");
            var lines=TouchSetupChecklist.Lines(d,true,true,false,true,"");
            Require(lines.Any(s=>s.Contains("110 ms"))&&lines.Any(s=>s.Contains("maximum: 1")),"checklist exposes supplied short hold and one contact");
            Require(lines.Any(s=>s.StartsWith("WAIT:")&&s.Contains("pre-touch")),"missing pre-touch evidence is visible");
            d.HeldMilliseconds=3100;d.MaxContacts=2;
            Require(Ready(d),"complete touch test may associate with verified anchor");
            Require(!Ready(d,false)&&!Ready(d,true,true,"device removed"),"no bypass for snapshot or fault");
            Require(!TouchSetupChecklist.Ready(d,false,true,true,true,"")&&!TouchSetupChecklist.Ready(d,true,false,true,true,""),"connected stable monitor required");
            d.Complete=false;Require(!Ready(d),"partial frame cannot qualify");d.Complete=true;
            d.Contacts=1;Require(!Ready(d),"held contact cannot qualify");d.Contacts=0;
            d.ResetTest();
            Require(d.Supported&&d.Kind=="Touch"&&d.Frames==0&&d.MaxContacts==0&&!d.SawUp&&d.HeldMilliseconds==0&&!Ready(d),"reset discards observations, not device capability");
            var partial=new TouchFrame {Device="touch",Complete=false,Contacts=new List<TouchContact>{new TouchContact {Id=-1,Down=true}}};
            d.Observe(partial,1000);Require(d.MaxContacts==0&&d.DownSince==0&&!d.SawUp,"partial placeholder is not a measured held finger");
            d.ResetTest();
            d.Observe(new TouchFrame {Device="touch",Contacts=new List<TouchContact>{new TouchContact {Id=1,Down=true}}},1000);
            d.ResetTest();
            d.Observe(new TouchFrame {Device="touch",Contacts=new List<TouchContact>{new TouchContact {Id=1,Down=false}}},10000);
            Require(d.HeldMilliseconds==0&&!d.SawUp,"a test pause cannot inflate a hold across reset");
            var pen=new TouchDeviceEvidence {Key="pen",Kind="Pen",Supported=true,HoverKnown=true};
            Require(!Ready(pen),"pen hover descriptor with zero reports is not a pen test");
            pen.Frames=20;pen.HeldMilliseconds=3000;pen.SawUp=true;
            Require(!Ready(pen),"hover exit must be actually observed when required");
            pen.SawHoverExit=true;Require(Ready(pen),"observed pen contact/release/hover exit accepted");
            pen.CurrentHover=true;Require(!Ready(pen),"current hover blocks association with hover protection");
            Require(Ready(pen,true,false),"explicit no-hover mode still requires contact/release but not hover exit");
            Require(TouchSetupChecklist.Lines(null,true,true,false,true,"").Last().StartsWith("WAIT:"),"empty device selection remains explanatory");
            log.AppendLine("PASS: "+checks+" device-change classification, supplied-diagnostic readiness, complete-contact, hover and pause-reset assertions. No physical input generated.");
        }
        internal static void RunNative(StringBuilder log)
        {
            int faults=0;
            using(var source=new RawTouchSource(delegate{},delegate{faults++;}))
            {
                Require(source.Registered,"native passive listener registers");
                int count=source.ReplayKnownArrivalsForTest();
                Require(faults==0,"replayed arrival handling for actual enumerated device classes does not latch a fault");
                source.HandleDeviceChange(2,new IntPtr(-381001));
                Require(faults==1,"unknown removal reaches production fault callback");
                log.AppendLine("PASS: native passive listener and device-change dispatch; known arrival handles replayed="+count+". No input device disconnected.");
            }
            using(var owner=new Form())
            using(var service=new TouchReturnService(owner,new Options()))
            {
                using(service.DetectionTest())
                {
                    service.HandleProcessingPause();
                    Require(service.Testing&&service.BlockingReason.Length==0,"passive test pause does not permanently fault provider");
                    Require(service.Status.Contains("DETECTION TEST ONLY"),"mode explicitly says no automatic return");
                    Require(service.Report.Contains("Saved master switch: OFF"),"diagnostics include actual saved master switch");
                }
                service.Configure(new Options {TouchSupportEnabled=true});
                service.HandleProcessingPause();
                Require(service.BlockingReason.Contains("input processing paused"),"real return watchdog stays fail-closed");
                using(service.DetectionTest())
                {
                    Require(service.BlockingReason.Length==0,"explicit fresh test clears old watchdog block");
                    service.HandleProcessingPause();
                    Require(!service.HasProbeAnchor("unknown","unknown"),"pause cannot invent verified snapshot evidence");
                }
            }
            log.AppendLine("PASS: real service detection-pause reset and runtime-pause fail-closed paths; no return target, synthetic click, settings write or physical touchscreen used.");
        }
    }
}
