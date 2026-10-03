using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TaskbarTilesAudio
{
    static class AudioHost
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 262144 };
        static readonly object JsonLock = new object();
        static readonly Dictionary<string, string> Images = new Dictionary<string, string>();
        static readonly SemaphoreSlim SendLock = new SemaphoreSlim(1, 1);
        static string Encode(object value) { lock (JsonLock) return Json.Serialize(value); }
        static Dictionary<string, object> Decode(string value) { lock (JsonLock) return Json.Deserialize<Dictionary<string, object>>(value); }
        [MTAThread] static int Main(string[] args)
        {
            try {
                if (args.Contains("--self-test")) { AudioTests.Run(Path.Combine(Root, "plugin", "audio-selftest.txt")); return 0; }
                Validate();
                if (args.Contains("--validate")) return 0;
                if (args.Contains("--inspect")) {
                    using (var backend = new CoreAudioBackend(delegate { })) {
                        var endpoints = backend.OpenCapture();
                        var states = endpoints.Select(endpoint => {
                            try { return (object)new { endpointId=endpoint.Id, muted=(bool?)endpoint.ReadMute(), error=(string)null }; }
                            catch (Exception ex) { return (object)new { endpointId=endpoint.Id, muted=(bool?)null, error="0x"+ex.HResult.ToString("X8") }; }
                        }).ToArray();
                        File.WriteAllText(Path.Combine(Root,"plugin","audio-inspection.json"),Encode(new { scope="all active Windows capture endpoints", endpoints=states, readOnly=true }),new UTF8Encoding(false));
                    }
                    return 0;
                }
                Run(args).GetAwaiter().GetResult(); return 0;
            } catch (Exception ex) {
                try { File.WriteAllText(Path.Combine(Root, "plugin", "audio-error.txt"), ex.ToString()); } catch { }
                return 1;
            }
        }
        static void Validate()
        {
            var manifest = Decode(File.ReadAllText(Path.Combine(Root, "manifest.json")));
            var actions = ((System.Collections.IEnumerable)manifest["Actions"]).Cast<Dictionary<string, object>>().ToArray();
            if (actions.Length != 1 || Convert.ToString(actions[0]["UUID"]) != AudioSession.ActionId) throw new IOException("Invalid Audio Control action manifest.");
            foreach (string state in new[] { "live", "muted", "unavailable", "mixed" }) {
                byte[] image = File.ReadAllBytes(Path.Combine(Root, "images", "mic-" + state + ".png"));
                if (image.Length < 8 || image.Length > 65536 || image[0] != 137 || image[1] != 80) throw new IOException("Invalid Audio Control image.");
                Images[state] = "data:image/png;base64," + Convert.ToBase64String(image);
            }
            Images["error"] = Images["unavailable"];
        }
        static async Task Send(ClientWebSocket socket, object value)
        {
            await SendLock.WaitAsync();
            try {
                byte[] data = Encoding.UTF8.GetBytes(Encode(value));
                using (var timeout = new CancellationTokenSource(5000)) await socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, timeout.Token);
            } finally { SendLock.Release(); }
        }
        static async Task Run(string[] args)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < args.Length; i += 2) options[args[i].TrimStart('-')] = args[i + 1];
            string rawPort, uuid, registration; int port;
            if (!options.TryGetValue("port", out rawPort) || !int.TryParse(rawPort, out port) || port < 1 || port > 65535 || !options.TryGetValue("pluginUUID", out uuid) || !options.TryGetValue("registerEvent", out registration) || registration != "registerPlugin") throw new IOException("Add Audio Control from the Taskbar Tiles category in Stream Dock.");
            using (var socket = new ClientWebSocket()) {
                using (var timeout = new CancellationTokenSource(10000)) await socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port), timeout.Token);
                await Send(socket, new { @event = registration, uuid = uuid });
                using (var actor = new AudioActor((ev, context, state) => {
                    if (ev == "alert") { Send(socket, new { @event = "showAlert", context = context }).GetAwaiter().GetResult(); return; }
                    Send(socket, new { @event = "setImage", context = context, payload = new { image = Images[state], target = 0 } }).GetAwaiter().GetResult();
                    // Live and muted labels are in the artwork. Unknown status gets explicit text.
                    string title = state == "error" ? "MIC ERROR" : "";
                    Send(socket, new { @event = "setTitle", context = context, payload = new { title = title, target = 0 } }).GetAwaiter().GetResult();
                }, () => socket.Abort())) {
                    byte[] buffer = new byte[16384];
                    while (socket.State == WebSocketState.Open) {
                        using (var message = new MemoryStream()) {
                            WebSocketReceiveResult part;
                            do {
                                part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                                if (part.MessageType == WebSocketMessageType.Close) return;
                                if (part.MessageType != WebSocketMessageType.Text) throw new IOException("Expected a text SDK message.");
                                message.Write(buffer, 0, part.Count);
                                if (message.Length > 262144) throw new IOException("Oversized SDK message.");
                            } while (!part.EndOfMessage);
                            var e = Decode(Encoding.UTF8.GetString(message.ToArray()));
                            actor.Post(Get(e, "event"), Get(e, "action"), Get(e, "context"), Get(e, "device"));
                        }
                    }
                }
            }
        }
        static string Get(Dictionary<string, object> e, string key) { object value; return e.TryGetValue(key, out value) ? Convert.ToString(value) : ""; }
    }
    sealed class AudioActor : IDisposable
    {
        sealed class Message { internal string Event, Action, Context, Device; internal Snapshot Receipt; }
        readonly Queue<Message> queue = new Queue<Message>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread thread;
        readonly Action<string, string, string> output;
        readonly Action failed;
        volatile bool stopped;
        volatile Snapshot current = new Snapshot(null, "unavailable", 0);
        internal AudioActor(Action<string, string, string> output, Action failed)
        {
            this.output = output; this.failed = failed;
            thread = new Thread(Loop) { IsBackground = true, Name = "Taskbar Tiles Audio Control" };
            thread.SetApartmentState(ApartmentState.MTA); thread.Start();
        }
        internal void Post(string ev, string action, string context, string device)
        {
            lock (queue) {
                if (stopped) return;
                if (queue.Count >= 128) throw new IOException("Audio Control event queue exceeded its safe limit.");
                queue.Enqueue(new Message { Event = ev, Action = action, Context = context, Device = device, Receipt = current });
            }
            wake.Set();
        }
        void Loop()
        {
            try {
                using (var session = new AudioSession(new CoreAudioBackend(() => wake.Set()), output, AudioDiagnostics.Write)) {
                    while (!stopped) {
                        Message message;
                        do {
                            lock (queue) { message = queue.Count != 0 ? queue.Dequeue() : null; }
                            if (message != null && !stopped) {
                                session.Handle(message.Event, message.Action, message.Context, message.Device, message.Receipt);
                                current = session.Current;
                            }
                        } while (message != null && !stopped);
                        if (stopped) break;
                        session.Refresh(); current = session.Current;
                        // Notifications refresh promptly; reconciliation also handles new capture endpoints while mute is on.
                        wake.WaitOne(session.Watching ? 1000 : Timeout.Infinite);
                    }
                }
            } catch (Exception ex) { AudioDiagnostics.Write("actor exception=" + ex.GetType().Name + " hresult=0x" + ex.HResult.ToString("X8")); if (!stopped) failed(); }
        }
        public void Dispose()
        {
            stopped = true; lock (queue) queue.Clear(); wake.Set();
            if (thread.Join(6000)) wake.Dispose();
        }
    }
}
