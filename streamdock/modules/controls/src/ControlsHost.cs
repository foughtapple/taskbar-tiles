// Native Stream Dock adapter for the direct controls. Idle unless pressed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace FoughtAppleControls
{
    static class Host
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 262144 };
        static readonly Dictionary<string, string> Files = new Dictionary<string, string> {
            {"com.foughtapple.controls.rocket", "01 - Rocket League - Open Close.exe"},
            {"com.foughtapple.controls.overwatch", "02 - Overwatch - Open Close.exe"},
            {"com.foughtapple.controls.screenshot", "05 - FancyZone Screenshot.exe"},
            {"com.foughtapple.controls.clipboard", "06 - Clipboard History.exe"},
            {"com.foughtapple.controls.voice", "07 - GPT Voice - Pet.exe"} };
        static readonly Dictionary<string, Process> Running = new Dictionary<string, Process>();
        static readonly Dictionary<string, string> Contexts = new Dictionary<string, string>();
        static readonly Dictionary<string, string> Devices = new Dictionary<string, string>();
        static readonly object ProcessLock = new object();
        static readonly SemaphoreSlim SendLock = new SemaphoreSlim(1, 1);
        static ClientWebSocket Socket;
        [STAThread] static int Main(string[] args)
        {
            try { Run(args).GetAwaiter().GetResult(); return 0; }
            catch (Exception ex) { Log(ex.Message); return 1; }
        }
        static void Log(string text)
        {
            try { string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"FoughtApple\StreamDockControls"); Directory.CreateDirectory(dir); string file = Path.Combine(dir, "host.log"); if (File.Exists(file) && new FileInfo(file).Length > 131072) File.WriteAllText(file, ""); File.AppendAllText(file, DateTime.UtcNow.ToString("s") + " " + text + Environment.NewLine); } catch { }
        }
        static async Task Send(object message)
        {
            await SendLock.WaitAsync();
            try { byte[] b = Encoding.UTF8.GetBytes(Json.Serialize(message)); using (var timeout = new CancellationTokenSource(5000)) { await Socket.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, timeout.Token); } }
            finally { SendLock.Release(); }
        }
        static void Launch(string action, string file, string args)
        {
            lock (ProcessLock)
            {
                Process previous;
                if (Running.TryGetValue(action, out previous)) { try { if (!previous.HasExited) return; } catch { } previous.Dispose(); Running.Remove(action); }
                var start = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.Combine(Root, "plugin") };
                var p = new Process { StartInfo = start, EnableRaisingEvents = true };
                p.Exited += delegate { lock (ProcessLock) { Process current; if (Running.TryGetValue(action, out current) && current == p) Running.Remove(action); p.Dispose(); } };
                Running[action] = p;
                try { if (!p.Start()) throw new IOException("Windows did not start the action."); }
                catch { Running.Remove(action); p.Dispose(); throw; }
            }
        }
        static async Task Run(string[] args)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < args.Length; i += 2) options[args[i].TrimStart('-')] = args[i + 1];
            string raw = File.ReadAllText(Path.Combine(Root, "manifest.json")); var manifest = Json.Deserialize<Dictionary<string, object>>(raw);
            var allowed = new HashSet<string>(); foreach (Dictionary<string, object> a in (System.Collections.IEnumerable)manifest["Actions"]) allowed.Add((string)a["UUID"]);
            foreach (string a in allowed) if (!Files.ContainsKey(a) || !File.Exists(Path.Combine(Root, "plugin", Files[a]))) throw new IOException("Missing or unrecognised action executable.");
            if (args.Contains("--validate")) return;
            int port; string value, uuid, registration;
            if (!options.TryGetValue("port", out value) || !int.TryParse(value, out port) || port < 1 || port > 65535 || !options.TryGetValue("pluginUUID", out uuid) || !options.TryGetValue("registerEvent", out registration) || registration != "registerPlugin") throw new IOException("Add Desktop Controls from Stream Dock's Key tab, not Toolbox Open.");
            using (Socket = new ClientWebSocket())
            {
                using (var timeout = new CancellationTokenSource(10000)) await Socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port), timeout.Token);
                await Send(new { @event = registration, uuid = uuid });
                byte[] buffer = new byte[16384];
                while (Socket.State == WebSocketState.Open)
                {
                    using (var message = new MemoryStream())
                    {
                        WebSocketReceiveResult part;
                        do { part = await Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None); if (part.MessageType == WebSocketMessageType.Close) return; if (part.MessageType != WebSocketMessageType.Text) throw new IOException("Expected a text SDK message."); message.Write(buffer, 0, part.Count); if (message.Length > 262144) throw new IOException("Oversized host message."); } while (!part.EndOfMessage);
                        var e = Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(message.ToArray()));
                        string ev = e.ContainsKey("event") ? Convert.ToString(e["event"]) : "", action = e.ContainsKey("action") ? Convert.ToString(e["action"]) : "", ctx = e.ContainsKey("context") ? Convert.ToString(e["context"]) : "";
                        if (ev == "willDisappear") { Contexts.Remove(ctx); Devices.Remove(ctx); continue; }
                        if (ev == "deviceDidDisconnect") { string dev = e.ContainsKey("device") ? Convert.ToString(e["device"]) : ""; foreach (string c in Devices.Where(x => x.Value == dev).Select(x => x.Key).ToArray()) { Contexts.Remove(c); Devices.Remove(c); } continue; }
                        if (!allowed.Contains(action)) continue;
                        if (ev == "willAppear") { if (ctx.Length != 0 && (Contexts.Count < 128 || Contexts.ContainsKey(ctx))) { Contexts[ctx] = action; Devices[ctx] = e.ContainsKey("device") ? Convert.ToString(e["device"]) : ""; } continue; }
                        bool failed = false;
                        try {
                            if (ev == "keyDown" && Contexts.ContainsKey(ctx) && Contexts[ctx] == action) Launch(action, Path.Combine(Root, "plugin", Files[action]), "");
                            else if (ev == "sendToPlugin" && Contexts.ContainsKey(ctx) && Contexts[ctx] == action) {
                                object rawPayload; var payload = e.TryGetValue("payload", out rawPayload) ? rawPayload as Dictionary<string, object> : null; object command;
                                if (payload != null && payload.TryGetValue("command", out command)) {
                                    if (Convert.ToString(command) == "test" && action == "com.foughtapple.controls.screenshot") Launch(action, Path.Combine(Root, "plugin", Files[action]), "--test");
                                    if (Convert.ToString(command) == "setup" && (action == "com.foughtapple.controls.rocket" || action == "com.foughtapple.controls.overwatch")) {
                                        string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
                                        Launch("setup", ps, "-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File \"" + Path.Combine(Root, @"plugin\Engine\Games\DockGames.ps1") + "\" -Mode Setup -PlainRunner");
                                    }
                                }
                            }
                        } catch (Exception ex) { Log(ex.Message); failed = true; }
                        // Windows' inbox compiler is C#5: await must be outside catch.
                        if (failed) await Send(new { @event = "showAlert", context = ctx });
                    }
                }
            }
        }
    }
}
