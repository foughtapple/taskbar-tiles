using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace TaskbarTilesAudio {
static class AudioTests {
sealed class FakeEndpoint : IAudioEndpoint {
public string Id { get; set; }
internal bool Muted, ReadFails, WriteFails, IgnoreWrite;
internal int Writes; internal Action OnWrite;
public bool ReadMute(){if(ReadFails)throw new IOException("read");return Muted;}
public void WriteMute(bool value){Writes++;if(WriteFails)throw new IOException("write");if(!IgnoreWrite)Muted=value;if(OnWrite!=null)OnWrite();}
}
sealed class FakeBackend : IAudioBackend {
internal IAudioEndpoint[] Endpoints=new IAudioEndpoint[0];internal long Epoch;internal bool Running,Disposed,EnumFails;internal int Opens,Starts,Stops;
public long Revision {get{return Epoch;}}
public void Start(){if(!Running){Running=true;Starts++;}}
public void Stop(){if(Running){Running=false;Stops++;}}
public void Invalidate(){Epoch++;Stop();}
public IAudioEndpoint[] OpenCapture(){Opens++;if(EnumFails)throw new IOException("enum");return Endpoints.ToArray();}
public void Dispose(){Stop();Disposed=true;}
}
internal static void Run(string file){
int checks=0;var log=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);checks++;log.Add("PASS "+label);};
var a=new FakeEndpoint{Id="mic-a"};var b=new FakeEndpoint{Id="mic-b",Muted=true};var c=new FakeEndpoint{Id="mic-c"};var speaker=new FakeEndpoint{Id="speaker"};
var backend=new FakeBackend{Endpoints=new IAudioEndpoint[]{a,b}};var events=new List<string>();var trace=new List<string>();
using(var s=new AudioSession(backend,(ev,ctx,state)=>events.Add(ev+":"+ctx+":"+state),trace.Add)){
Action<string,string> emit=(ev,ctx)=>s.Handle(ev,AudioSession.ActionId,ctx,"dock",s.Current);Action<string> click=ctx=>{emit("keyDown",ctx);emit("keyUp",ctx);};
s.Refresh();check(backend.Opens==0,"hidden off worker does not inspect devices");
emit("willAppear","one");check(s.Current.State=="mixed"&&s.Current.Muted==1&&s.Current.Live==1,"mixed capture state is explicit");
check(a.Writes==0&&b.Writes==0,"initial appearance is read-only");
emit("willAppear","two");check(events.Contains("state:two:mixed"),"second placement receives aggregate state");
click("one");check(a.Muted&&b.Muted&&a.Writes==1&&b.Writes==1,"on command sets all available microphones muted");
check(s.Current.State=="muted"&&s.Current.Muted==2&&s.GlobalMute,"muted image requires every endpoint readback");
check(events.Contains("state:one:muted")&&events.Contains("state:two:muted"),"duplicate placements reflect aggregate readback");
check(speaker.Writes==0,"render endpoint is never included");
click("two");check(!a.Muted&&!b.Muted,"off command unmutes individually pre-muted microphone too");
check(!s.GlobalMute&&s.Current.State=="live","off state is all microphones live");
emit("keyDown","one");emit("keyDown","one");check(a.Writes==3&&b.Writes==3,"held repeated keyDown writes only once");emit("keyUp","one");
backend.Endpoints=new IAudioEndpoint[]{a,b,c};backend.Epoch++;s.Refresh();check(c.Muted&&c.Writes==1,"new capture device is muted while global on");
check(s.Current.State=="muted"&&s.Current.Muted==3,"arrival is included in aggregate state");
s.Refresh();check(c.Writes==1,"unchanged polling does not repeat mute writes");
c.Muted=false;s.Refresh();check(s.Current.State=="mixed"&&c.Writes==1,"external unmute is shown honestly without fighting existing device");
click("one");check(!a.Muted&&!b.Muted&&!c.Muted,"off command from mixed on state unmutes all");
var old=s.Current;backend.Endpoints=new IAudioEndpoint[]{a,b};backend.Epoch++;s.Handle("keyDown",AudioSession.ActionId,"one","dock",old);emit("keyUp","one");check(a.Writes==4&&b.Writes==4,"queued press expires after capture membership changes");
check(s.Current.State=="live"&&s.Current.Live==2,"membership change refreshes actual remaining devices");
b.IgnoreWrite=true;click("one");check(a.Muted&&!b.Muted&&s.Current.State=="error"&&s.Current.Failed==1,"ignored device write cannot show all muted");
check(events.Contains("alert:one:"),"partial write failure alerts user");
check(trace.Any(x=>x.Contains("readback-mismatch")),"ignored write diagnosis identifies readback mismatch");
b.IgnoreWrite=false;click("one");check(!a.Muted&&!b.Muted&&s.Current.Failed==0,"off recovers partial failed on attempt");
a.WriteFails=true;click("one");check(!a.Muted&&b.Muted&&s.Current.State=="error","one failing endpoint does not block commands on others");
check(trace.Any(x=>x.Contains("write failed endpoint=mic-a")&&x.Contains("hresult=")),"write failure records endpoint and HRESULT");
a.WriteFails=false;click("one");check(!a.Muted&&!b.Muted,"off command clears failure and unmutes all");
click("one");backend.Endpoints=new IAudioEndpoint[]{a,b,c};backend.Epoch++;c.Muted=false;c.WriteFails=true;s.Refresh();check(s.Current.State=="error"&&s.Current.Failed==1&&!c.Muted,"new device mute failure is honest");
int failedArrivalWrites=c.Writes;s.Refresh();check(c.Writes==failedArrivalWrites,"failed arrival is not retried every poll");
c.WriteFails=false;click("one");check(!a.Muted&&!b.Muted&&!c.Muted,"explicit off clears arrival failure");
a.ReadFails=true;s.Refresh();check(s.Current.State=="error"&&s.Current.Unknown==1,"failed read remains unknown");
int before=b.Writes;click("one");check(b.Muted&&b.Writes==before+1&&s.Current.State=="error","read failure on one microphone does not hide successful others");
a.ReadFails=false;s.Refresh();check(s.Current.State=="error"&&s.Current.Failed==1,"read recovery does not erase unsuccessful mute attempt");
click("one");check(!a.Muted&&!b.Muted&&!c.Muted,"off after read recovery explicitly unmutes every device");
click("one");emit("willDisappear","one");emit("willDisappear","two");check(s.Watching&&backend.Running,"global mute remains monitored when page hides");
backend.Endpoints=new IAudioEndpoint[]{a,b};backend.Epoch++;s.Refresh();c.Muted=false;backend.Endpoints=new IAudioEndpoint[]{a,b,c};backend.Epoch++;s.Refresh();check(c.Muted,"same endpoint ID reconnect is muted again");
emit("willAppear","one");click("one");s.Handle("deviceDidDisconnect","","","dock",null);check(!s.Visible&&!s.Watching&&!backend.Running,"off plus dock disconnect stops observation");
int opens=backend.Opens;s.Refresh();check(backend.Opens==opens,"hidden off does no polling");
emit("willAppear","one");backend.Endpoints=new IAudioEndpoint[0];backend.Epoch++;s.Refresh();check(s.Current.State=="unavailable"&&s.Current.Id==null,"no microphones shows unavailable");
int writes=a.Writes;click("one");check(a.Writes==writes,"no-device press cannot toggle stale microphone");
backend.Endpoints=new IAudioEndpoint[]{a,b};backend.Epoch++;s.Refresh();check(s.Current.State=="live","available devices recover on refresh");
backend.EnumFails=true;s.Refresh();check(s.Current.State=="error"&&s.Current.Id==null,"enumeration failure cannot claim muted");
backend.EnumFails=false;s.Refresh();old=s.Current;backend.Invalidate();s.Refresh();s.Handle("keyDown",AudioSession.ActionId,"one","dock",old);emit("keyUp","one");check(a.Writes==writes,"queued click expires across backend recovery");
s.Handle("keyDown","wrong.action","one","dock",s.Current);emit("keyDown","unknown");check(a.Writes==writes,"unowned action or context cannot toggle");
int starts=backend.Starts;s.Handle("systemDidWakeUp","","","",null);check(backend.Starts==starts+1,"wake recreates native observation");
check(trace.Any(x=>x.StartsWith("event=keyDown"))&&trace.Any(x=>x.StartsWith("readback endpoint=")),"diagnostics cover events and actual readbacks");
}
check(backend.Disposed&&!backend.Running,"shutdown disposes native backend");
var allMuted=new FakeEndpoint{Id="already-muted",Muted=true};var fresh=new FakeEndpoint{Id="fresh"};var restart=new FakeBackend{Endpoints=new IAudioEndpoint[]{allMuted}};
using(var s=new AudioSession(restart,(ev,ctx,state)=>{})){
s.Handle("willAppear",AudioSession.ActionId,"one","dock",s.Current);check(s.GlobalMute&&allMuted.Writes==0,"already fully muted startup adopts on mode without initial write");
restart.Endpoints=new IAudioEndpoint[]{allMuted,fresh};restart.Epoch++;s.Refresh();check(fresh.Muted,"new microphone stays muted after fully muted startup");
}
int changes=0;var notifications=new DeviceNotifications(change=>{if(change)changes++;});notifications.OnDefaultDeviceChanged(0,0,"speaker");notifications.OnDefaultDeviceChanged(1,1,"default-mic");check(changes==0,"default roles do not change all-capture target");notifications.OnDeviceAdded("new");check(changes==1,"device arrival invalidates capture membership");
var during=new FakeEndpoint{Id="during-command"};var arrive=new FakeEndpoint{Id="arrival-during-command"};var race=new FakeBackend{Endpoints=new IAudioEndpoint[]{during}};
using(var s=new AudioSession(race,(ev,ctx,state)=>{})){
s.Handle("willAppear",AudioSession.ActionId,"one","dock",s.Current);
during.OnWrite=()=>{during.OnWrite=null;race.Endpoints=new IAudioEndpoint[]{during,arrive};race.Epoch++;};
s.Handle("keyDown",AudioSession.ActionId,"one","dock",s.Current);s.Handle("keyUp",AudioSession.ActionId,"one","dock",s.Current);
check(during.Muted&&arrive.Muted&&s.Current.State=="muted","arrival during on command is also muted and verified");
}
log.Add("Passed "+checks+" checks; fake endpoints only; no real audio changes.");File.WriteAllLines(file,log);
}
}
}
