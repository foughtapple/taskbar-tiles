using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskbarTilesAudio
{
    interface IAudioEndpoint { string Id { get; } bool ReadMute(); void WriteMute(bool muted); }
    interface IAudioBackend : IDisposable
    {
        long Revision { get; }
        void Start();
        void Stop();
        void Invalidate();
        IAudioEndpoint OpenDefault();
    }
    sealed class Snapshot
    {
        internal readonly string Id, State;
        internal readonly long Revision;
        internal Snapshot(string id, string state, long revision) { Id = id; State = state; Revision = revision; }
    }
    // All methods execute on the audio thread. Only immutable Current is read by the receiver.
    sealed class AudioSession : IDisposable
    {
        internal const string ActionId = "com.foughtapple.audiocontrol.microphone";
        readonly IAudioBackend backend;
        readonly Action<string, string, string> output;
        readonly Dictionary<string, string> devices = new Dictionary<string, string>();
        readonly Dictionary<string, string> painted = new Dictionary<string, string>();
        readonly HashSet<string> pressed = new HashSet<string>();
        internal volatile Snapshot Current = new Snapshot(null, "unavailable", 0);
        internal bool Visible { get { return devices.Count != 0; } }
        internal AudioSession(IAudioBackend backend, Action<string, string, string> output) { this.backend = backend; this.output = output; }
        internal void Handle(string ev, string action, string context, string device, Snapshot receipt)
        {
            if (ev == "deviceDidDisconnect") {
                foreach (string ctx in devices.Where(p => p.Value == device).Select(p => p.Key).ToArray()) Remove(ctx);
                return;
            }
            if (ev == "systemDidWakeUp") { if (Visible) { backend.Invalidate(); Refresh(); } return; }
            if (action != ActionId || string.IsNullOrEmpty(context)) return;
            if (ev == "willAppear") {
                if (devices.Count >= 128 && !devices.ContainsKey(context)) return;
                bool start = !Visible;
                devices[context] = device; painted.Remove(context); pressed.Remove(context);
                if (start) { try { backend.Start(); } catch { Publish(null, "error"); return; } }
                Refresh(); return;
            }
            if (!devices.ContainsKey(context)) return;
            if (ev == "willDisappear") { Remove(context); return; }
            if (ev == "keyUp") { pressed.Remove(context); return; }
            if (ev == "keyDown" && pressed.Add(context)) Toggle(context, receipt);
        }
        void Remove(string context)
        {
            devices.Remove(context); painted.Remove(context); pressed.Remove(context);
            if (!Visible) { backend.Stop(); Current = new Snapshot(null, "unavailable", backend.Revision); }
        }
        void Publish(string id, string state)
        {
            Current = new Snapshot(id, state, backend.Revision);
            foreach (string context in devices.Keys) {
                string previous;
                if (!painted.TryGetValue(context, out previous) || previous != state) {
                    output("state", context, state); painted[context] = state;
                }
            }
        }
        internal void Refresh()
        {
            if (!Visible) return;
            try {
                backend.Start();
                long revision = backend.Revision;
                var endpoint = backend.OpenDefault();
                if (endpoint == null) { Publish(null, "unavailable"); return; }
                string id = endpoint.Id; bool muted = endpoint.ReadMute();
                // Never publish a state from an endpoint that changed during the read.
                var current = backend.OpenDefault();
                if (current == null || current.Id != id || backend.Revision != revision) { Publish(null, "unavailable"); return; }
                Publish(id, muted ? "muted" : "live");
            } catch { backend.Invalidate(); Publish(null, "error"); }
        }
        void Toggle(string context, Snapshot receipt)
        {
            bool failed = false;
            string writtenId = null; bool desired = false;
            try {
                long revision = backend.Revision;
                var endpoint = backend.OpenDefault();
                if (endpoint == null || receipt == null || receipt.Id == null || receipt.Id != Current.Id || receipt.Revision != Current.Revision || endpoint.Id != receipt.Id || receipt.Revision != revision) failed = true;
                else {
                    string id = endpoint.Id;
                    bool muted = endpoint.ReadMute();
                    var current = backend.OpenDefault();
                    if (current == null || current.Id != id || backend.Revision != revision) failed = true;
                    else { desired = !muted; endpoint.WriteMute(desired); writtenId = id; } // One write; never retry onto another endpoint.
                }
            } catch { backend.Invalidate(); failed = true; }
            // Both success and failure reflect a fresh read, never an optimistic local flag.
            Refresh();
            if (writtenId != null && (Current.Id != writtenId || Current.State != (desired ? "muted" : "live"))) failed = true;
            if (failed) output("alert", context, "");
        }
        public void Dispose() { devices.Clear(); painted.Clear(); pressed.Clear(); backend.Dispose(); Current = new Snapshot(null, "unavailable", 0); }
    }
}
