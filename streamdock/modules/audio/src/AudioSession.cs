using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskbarTilesAudio
{
    interface IAudioEndpoint { string Id { get; } bool ReadMute(); void WriteMute(bool muted); }
    interface IAudioBackend : IDisposable {
        long Revision { get; }
        void Start(); void Stop(); void Invalidate();
        IAudioEndpoint[] OpenCapture();
    }
    sealed class Snapshot {
        internal readonly string Id, State;
        internal readonly long Revision;
        internal readonly int Muted, Live, Unknown, Failed;
        internal Snapshot(string id, string state, long revision) : this(id,state,revision,0,0,0,0) { }
        internal Snapshot(string id,string state,long revision,int muted,int live,int unknown,int failed) { Id=id;State=state;Revision=revision;Muted=muted;Live=live;Unknown=unknown;Failed=failed; }
    }
    sealed class AudioSession : IDisposable {
        internal const string ActionId="com.foughtapple.audiocontrol.microphone";
        readonly IAudioBackend backend;
        readonly Action<string,string,string> output;
        readonly Action<string> trace;
        readonly Dictionary<string,string> devices=new Dictionary<string,string>();
        readonly Dictionary<string,string> painted=new Dictionary<string,string>();
        readonly HashSet<string> pressed=new HashSet<string>();
        readonly HashSet<string> known=new HashSet<string>();
        readonly Dictionary<string,bool> failedWrites=new Dictionary<string,bool>();
        bool globalMute, initialized;
        internal volatile Snapshot Current=new Snapshot(null,"unavailable",0);
        internal bool Visible { get { return devices.Count!=0; } }
        internal bool Watching { get { return Visible || globalMute; } }
        internal bool GlobalMute { get { return globalMute; } }
        internal AudioSession(IAudioBackend backend,Action<string,string,string> output) : this(backend,output,null) { }
        internal AudioSession(IAudioBackend backend,Action<string,string,string> output,Action<string> trace) { this.backend=backend;this.output=output;this.trace=trace??delegate{}; }
        internal void Handle(string ev,string action,string context,string device,Snapshot receipt) {
            if(ev=="deviceDidDisconnect") { foreach(string ctx in devices.Where(p=>p.Value==device).Select(p=>p.Key).ToArray()) Remove(ctx);return; }
            if(ev=="systemDidWakeUp") { if(Watching){ backend.Invalidate();Refresh(); }return; }
            if(action!=ActionId || string.IsNullOrEmpty(context)) return;
            if(ev=="willAppear" || ev=="willDisappear" || ev=="keyDown" || ev=="keyUp") trace("event="+ev+" visible="+devices.ContainsKey(context)+" held="+pressed.Contains(context));
            if(ev=="willAppear") {
                if(devices.Count>=128 && !devices.ContainsKey(context))return;
                devices[context]=device;painted.Remove(context);pressed.Remove(context);Refresh();return;
            }
            if(!devices.ContainsKey(context))return;
            if(ev=="willDisappear"){Remove(context);return;}
            if(ev=="keyUp"){pressed.Remove(context);return;}
            if(ev=="keyDown" && pressed.Add(context))Toggle(context,receipt);
        }
        void Remove(string context) { devices.Remove(context);painted.Remove(context);pressed.Remove(context);if(!Watching){backend.Stop();known.Clear();Current=new Snapshot(null,"unavailable",backend.Revision);} }
        void Publish(string id,string state,int muted,int live,int unknown) {
            int failed=failedWrites.Count;
            var next=new Snapshot(id,state,backend.Revision,muted,live,unknown,failed);
            if(Current.Id!=id || Current.State!=state || Current.Revision!=next.Revision || Current.Failed!=failed)trace("state="+state+" muted="+muted+" live="+live+" unknown="+unknown+" failed="+failed+" intentMute="+globalMute+" revision="+next.Revision);
            Current=next;
            foreach(string ctx in devices.Keys){string old;if(!painted.TryGetValue(ctx,out old)||old!=state){output("state",ctx,state);painted[ctx]=state;}}
        }
        static string Identity(IAudioEndpoint[] endpoints) { return string.Join("|",endpoints.Select(e=>e.Id).OrderBy(id=>id,StringComparer.Ordinal).ToArray()); }
        bool Write(IAudioEndpoint endpoint,bool desired,string reason) {
            try {
                bool before=endpoint.ReadMute();trace("write endpoint="+endpoint.Id+" before="+before+" desired="+desired+" reason="+reason);
                endpoint.WriteMute(desired);
                bool after=endpoint.ReadMute();trace("readback endpoint="+endpoint.Id+" actual="+after+" desired="+desired);
                if(after!=desired){failedWrites[endpoint.Id]=desired;trace("write failed endpoint="+endpoint.Id+" reason=readback-mismatch");return false;}
                failedWrites.Remove(endpoint.Id);return true;
            }catch(Exception ex){failedWrites[endpoint.Id]=desired;trace("write failed endpoint="+endpoint.Id+" exception="+ex.GetType().Name+" hresult=0x"+ex.HResult.ToString("X8"));return false;}
        }
        internal void Refresh() {
            if(!Watching)return;
            try {
                backend.Start();long revision=backend.Revision;var endpoints=backend.OpenCapture();var ids=new HashSet<string>(endpoints.Select(e=>e.Id));
                known.RemoveWhere(id=>!ids.Contains(id));foreach(string id in failedWrites.Keys.Where(id=>!ids.Contains(id)).ToArray())failedWrites.Remove(id);
                // Once mute is on, newly available capture endpoints are muted once on arrival.
                // Ordinary external changes on existing devices remain visible, rather than being overwritten.
                if(globalMute)foreach(var endpoint in endpoints)if(known.Add(endpoint.Id))Write(endpoint,true,"capture-arrival");
                foreach(var endpoint in endpoints)known.Add(endpoint.Id);
                int muted=0,live=0,unknown=0;
                foreach(var endpoint in endpoints)try{bool value=endpoint.ReadMute();if(value)muted++;else live++;bool requested;if(failedWrites.TryGetValue(endpoint.Id,out requested)&&value==requested)failedWrites.Remove(endpoint.Id);}catch(Exception ex){unknown++;trace("read failed endpoint="+endpoint.Id+" hresult=0x"+ex.HResult.ToString("X8"));}
                if(backend.Revision!=revision){trace("read interrupted by device change");Publish(null,"error",muted,live,unknown+1);return;}
                string state=endpoints.Length==0?"unavailable":unknown!=0||failedWrites.Count!=0?"error":muted==endpoints.Length?"muted":live==endpoints.Length?"live":"mixed";
                if(!initialized && endpoints.Length!=0){initialized=true;globalMute=state=="muted";}
                if(state=="live" && failedWrites.Count==0)globalMute=false;
                Publish(endpoints.Length==0?null:Identity(endpoints),state,muted,live,unknown);
            }catch(Exception ex){trace("enumeration failed hresult=0x"+ex.HResult.ToString("X8"));backend.Invalidate();Publish(null,"error",0,0,1);}
        }
        void Toggle(string context,Snapshot receipt) {
            bool failed=false;
            try {
                backend.Start();var endpoints=backend.OpenCapture();string identity=Identity(endpoints);
                trace("toggle count="+endpoints.Length+" receiptRevision="+(receipt==null?"none":receipt.Revision.ToString())+" revision="+backend.Revision+" intentMute="+globalMute);
                if(endpoints.Length==0 || receipt==null || receipt.Id==null || receipt.Id!=Current.Id || receipt.Id!=identity || receipt.Revision!=Current.Revision || receipt.Revision!=backend.Revision){trace("toggle skipped reason=stale-or-unavailable");failed=true;}
                else {
                    bool desired=!(globalMute || Current.State=="muted");globalMute=desired;failedWrites.Clear();known.Clear();
                    foreach(var endpoint in endpoints){known.Add(endpoint.Id);if(!Write(endpoint,desired,"button"))failed=true;}
                    trace("toggle complete desired="+desired+" failed="+failed+" count="+endpoints.Length);
                }
            }catch(Exception ex){trace("toggle exception="+ex.GetType().Name+" hresult=0x"+ex.HResult.ToString("X8"));backend.Invalidate();failed=true;}
            Refresh();if(Current.State=="error")failed=true;
            if(failed)output("alert",context,"");
        }
        public void Dispose(){devices.Clear();painted.Clear();pressed.Clear();known.Clear();backend.Dispose();Current=new Snapshot(null,"unavailable",0);}
    }
}
