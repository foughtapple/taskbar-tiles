using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class LaunchOutcomeTests
    {
        static int checks;
        static void Require(bool condition, string name) { checks++; if (!condition) throw new InvalidOperationException("FAILED: " + name); }
        static LaunchCandidate C(int handle, bool fresh, bool responded = false, bool foreground = false, bool ready = true)
        {
            return new LaunchCandidate { Window = new WindowRecord { Handle = new IntPtr(handle), ProcessId = 100,
                ProcessStartTicks = 200, Exe = @"C:\Example\app.exe", Title = "Fixture" }, IsNew = fresh,
                Responded = responded, Foreground = foreground, Ready = ready };
        }
        internal static void Run(StringBuilder log)
        {
            checks = 0;
            foreach (bool placement in new[] { false, true })
            {
                var s = new LaunchOutcomeSelector(); var old = C(1, false);
                Require(s.Step(new[] { old }, 0, 5000, true, false, placement).Kind == LaunchOutcomeKind.Waiting, "old window does not bypass launch");
                Require(s.Step(new[] { old }, 2500, 5000, true, false, placement).Kind == LaunchOutcomeKind.Waiting, "late new window has time to start");
                var fresh = C(2, true);
                s.Step(new[] { old, fresh }, 3000, 5000, true, false, placement);
                Require(s.Step(new[] { old, fresh }, 4000, 5000, true, false, placement).Window == fresh.Window, "prefer new HWND even in existing PID");
                s = new LaunchOutcomeSelector(); s.Step(new[] { old }, 0, 5000, true, false, placement);
                Require(s.Step(new[] { old }, 5000, 5000, true, false, placement).Kind == LaunchOutcomeKind.ReusedWindow, "covered or minimised singleton reused without foreground change");
                Require(s.Step(new[] { old }, 5000, 5000, false, false, placement).Window == null, "reuse opt-out honoured");
                Require(s.Step(new[] { old }, 5000, 5000, true, true, placement).Window == null, "explicit new-window override does not fall back");
                s = new LaunchOutcomeSelector(); var second = C(3, false);
                s.Step(new[] { old, second }, 0, 5000, true, false, placement);
                Require(s.Step(new[] { old, second }, 5000, 5000, true, false, placement).Kind == LaunchOutcomeKind.Ambiguous, "multiple old windows not guessed");
                old.Responded = old.Foreground = true;
                s.Step(new[] { old, second }, 5200, 5000, true, false, placement);
                Require(s.Step(new[] { old, second }, 6200, 5000, true, false, placement).Window == old.Window, "app-chosen old window disambiguates");
            }
            var early = new LaunchOutcomeSelector(); var response = C(4, false, true, true);
            early.Step(new[] { response }, 0, 15000, true, false, false);
            Require(early.Step(new[] { response }, 1300, 15000, true, false, false).Kind == LaunchOutcomeKind.ReusedWindow, "ordinary launch acknowledges native response promptly");
            var zone = new LaunchOutcomeSelector(); zone.Step(new[] { response }, 0, 15000, true, false, true);
            Require(zone.Step(new[] { response }, 1300, 15000, true, false, true).Kind == LaunchOutcomeKind.Waiting, "zone placement never moves old startup window early");
            var transient = new LaunchOutcomeSelector(); var unfinished = C(5, true, false, false, false);
            transient.Step(new[] { response, unfinished }, 0, 5000, true, false, false);
            Require(transient.Step(new[] { response, unfinished }, 5100, 5000, true, false, false).Window == null, "construction window blocks old fallback");
            transient.Step(new[] { unfinished }, 5300, 8000, true, false, false); unfinished.Ready = true;
            Require(transient.Step(new[] { unfinished }, 5500, 8000, true, false, false).Window == null, "new ready state needs to settle");
            Require(transient.Step(new[] { unfinished }, 6400, 8000, true, false, false).Window == unfinished.Window, "stable replacement selected");
            var reusedHandle = new LaunchOutcomeSelector(); var reused = C(6, true);
            reusedHandle.Step(new[] { reused }, 0, 5000, true, false, false); reused.Window.ProcessId = 102;
            Require(reusedHandle.Step(new[] { reused }, 1500, 5000, true, false, false).Window == null, "PID changes reset stability");
            Require(reusedHandle.Step(new[] { reused }, 2500, 5000, true, false, false).Window == reused.Window, "new identity settles afresh");
            var d = new Options(); Require(!d.TerminalNewWindow && d.ReuseSingleInstance, "defaults respect app window settings");
            var upgraded = Options.UpgradeToCurrent(new[] { "ConfigVersion=7", "TerminalNewWindow=true", "TileSize=160", "WindowTitleFontSize=25", "ReuseSingleInstance=false" });
            Require(!upgraded.TerminalNewWindow && upgraded.ConfigVersion == 8 && upgraded.TileSize == 160 && upgraded.WindowTitleFontSize == 25 && !upgraded.ReuseSingleInstance,
                "one-time removal of old forced-Terminal default preserves other preferences");
            Require(Options.UpgradeToCurrent(new[] { "ConfigVersion=8", "TerminalNewWindow=true" }).TerminalNewWindow, "subsequent deliberate Terminal override survives");
            var app = new AppButton { Id = "Appid:Browser.Profile.A", LaunchExe = @"C:\Browser\browser.exe" };
            Require(!LaunchIdentity.Matches(app, new WindowRecord { AppId = "Browser.Profile.B", Exe = app.LaunchExe }, null), "runtime result observation keeps explicit profile conflicts");
            Require(!LaunchIdentity.Matches(new AppButton { LaunchExe = @"C:\One\app.exe" }, new WindowRecord { AppId = "", Exe = @"D:\Other\app.exe" }, null), "same filename is not application identity");
            var operation = new LaunchOperation(); Require(operation.TryDispatch() && !operation.TryDispatch(), "one dispatch only regardless of launch outcome");
            log.AppendLine("PASS: " + checks + " app-managed new/reused-window, delay, ambiguity, identity and migration assertions.");
        }
        static void PumpUntil(Func<bool> ready, int milliseconds, string name)
        {
            var watch = Stopwatch.StartNew();
            while (!ready() && watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(20); }
            Require(ready(), name);
        }
        static string FixtureRoot(string token) { return Path.Combine(Path.GetTempPath(), "TaskbarTiles-LaunchTest-" + token); }
        // Only explicit self-test invocations enter this mode. No installed app state,
        // normal hooks, favourites, startup entries or third-party apps are touched.
        internal static int Fixture(string[] args)
        {
            int at = Array.IndexOf(args, "--test-launch-fixture");
            if (at < 0 || at + 1 >= args.Length || !Regex.IsMatch(args[at + 1], "^[a-f0-9]{32}$")) return 2;
            string token = args[at + 1], root = FixtureRoot(token);
            if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "mode.txt"))) return 2;
            string mode = File.ReadAllText(Path.Combine(root, "mode.txt")).Trim();
            string mutexName = "Local\\TaskbarTiles.LaunchTest." + token, eventName = mutexName + ".Activate";
            using (var process = Process.GetCurrentProcess())
                File.AppendAllText(Path.Combine(root, "starts.txt"), process.Id + " " + process.StartTime.ToUniversalTime().Ticks + "\n");
            bool first;
            using (var mutex = new Mutex(true, mutexName, out first))
            {
                if (!first && mode.StartsWith("single", StringComparison.Ordinal))
                {
                    if (mode != "single-quiet")
                    {
                        uint pid;
                        if (uint.TryParse(File.ReadAllText(Path.Combine(root, "primary.txt")), out pid)) Native.AllowSetForegroundWindow(pid);
                        using (var signal = EventWaitHandle.OpenExisting(eventName)) signal.Set();
                    }
                    return 0;
                }
                if (!first && mode == "delayed") Thread.Sleep(2600);
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new Form { Text = "Taskbar Tiles launch fixture", Size = new Size(380, 180),
                    StartPosition = FormStartPosition.Manual, Location = new Point(80, 100) })
                using (var expiry = new System.Windows.Forms.Timer { Interval = 60000 })
                using (var activate = first ? new EventWaitHandle(false, EventResetMode.AutoReset, eventName) : null)
                {
                    var handle = form.Handle;
                    if (first) File.WriteAllText(Path.Combine(root, "primary.txt"), Process.GetCurrentProcess().Id.ToString());
                    RegisteredWaitHandle wait = null;
                    if (first) wait = ThreadPool.RegisterWaitForSingleObject(activate, delegate(object state, bool timedOut)
                    {
                        try { form.BeginInvoke(new Action(delegate { form.WindowState = FormWindowState.Normal; form.Show(); form.Activate(); Native.SetForegroundWindow(form.Handle); })); } catch (InvalidOperationException) { }
                    }, null, -1, false);
                    expiry.Tick += delegate { form.Close(); }; expiry.Start();
                    try { Application.Run(form); }
                    finally { if (wait != null) wait.Unregister(null); }
                }
            }
            return 0;
        }
        static WindowRecord Observe(string token, bool forPlacement, StringBuilder log)
        {
            string exe = Application.ExecutablePath;
            var entry = new FavouriteEntry { Target = exe, Arguments = "--test-launch-fixture " + token, Name = "Launch fixture" };
            var app = new AppButton { DisplayName = "Launch fixture", Id = "", LaunchExe = exe, Favourite = entry };
            var options = new Options { LaunchTimeoutSeconds = 5, TerminalNewWindow = false, ReuseSingleInstance = true };
            WindowRecord selected = null; string failure = null; bool finished = false;
            using (var dispatcher = new Control())
            {
                var handle = dispatcher.Handle;
                LaunchPlacement tracking = null;
                using (tracking = new LaunchPlacement(app, options, "fixture", delegate(IntPtr h, string error)
                { selected = tracking.SelectedWindow; failure = error; finished = true; }, forPlacement))
                {
                    var request = new LaunchOperation();
                    ReliableLauncher.Start(app, options, null, request, delegate(LaunchReceipt receipt, string error)
                    {
                        if (dispatcher.IsDisposed) return;
                        dispatcher.BeginInvoke(new Action(delegate { if (error != null) tracking.Fail(error); else tracking.Begin(receipt); }));
                    });
                    PumpUntil(delegate { return finished; }, 12000, "actual normal launch completed observation");
                    Require(failure == null && selected != null, "native launch resolved to a verified window: " + (failure ?? "no result"));
                }
            }
            log.AppendLine("PASS: native app-managed launch resolved; zone=" + forPlacement + "; PID=" + selected.ProcessId);
            return selected;
        }
        internal static int RunNative()
        {
            string token = Guid.NewGuid().ToString("N"), root = FixtureRoot(token);
            var log = new StringBuilder(); checks = 0;
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "mode.txt"), "multiple");
                var first = Observe(token, false, log);
                var second = Observe(token, false, log);
                Require(first.Handle != second.Handle, "multi-window app receives another launch even while already running");
                Native.ShowWindowAsync(first.Handle, 6);
                PumpUntil(delegate { return Native.IsIconic(first.Handle); }, 2000, "existing fixture minimised");
                File.WriteAllText(Path.Combine(root, "mode.txt"), "single");
                var reused = Observe(token, false, log);
                Require(reused.Handle == first.Handle, "same app switches to its own single-instance preference dynamically");
                WindowNative.PostMessage(second.Handle, 0x10, IntPtr.Zero, IntPtr.Zero);
                PumpUntil(delegate { return !Native.IsWindow(second.Handle); }, 2000, "extra fixture closed");
                Native.ShowWindowAsync(first.Handle, 6);
                PumpUntil(delegate { return Native.IsIconic(first.Handle); }, 2000, "singleton stays minimised for quiet launch test");
                File.WriteAllText(Path.Combine(root, "mode.txt"), "single-quiet");
                var quiet = Observe(token, false, log);
                Require(quiet.Handle == first.Handle, "covered/minimised existing window resolves even if app did not focus it");
                Native.ShowWindowAsync(first.Handle, 9);
                PumpUntil(delegate { return !Native.IsIconic(first.Handle); }, 2000, "resolved existing window can be restored");
                File.WriteAllText(Path.Combine(root, "mode.txt"), "delayed");
                var delayed = Observe(token, true, log);
                Require(delayed.Handle != first.Handle, "zone launch waits for a slow new window instead of taking the old one");
                Require(File.ReadAllLines(Path.Combine(root, "starts.txt")).Length == 5, "exactly five launch requests; no automatic retries or duplicate dispatch");
                log.AppendLine("PASS: " + checks + " native launch/outcome checks with disposable out-of-process fixture windows.");
                log.AppendLine("Actual Steam/Bambu and the user's desktop were not tested by this fixture.");
                File.WriteAllText(Path.Combine(Program.Home, "launch-outcome-test.log"), log.ToString());
                return 0;
            }
            catch (Exception ex)
            {
                log.AppendLine(ex.ToString());
                File.WriteAllText(Path.Combine(Program.Home, "launch-outcome-test.log"), log.ToString()); return 1;
            }
            finally
            {
                // Only PIDs/creation times written by this test's nonce-bound fixture.
                try
                {
                    foreach (string line in File.ReadAllLines(Path.Combine(root, "starts.txt")))
                    {
                        string[] p = line.Split(' '); int pid; long ticks;
                        if (p.Length != 2 || !int.TryParse(p[0], out pid) || !long.TryParse(p[1], out ticks)) continue;
                        try
                        {
                            using (var process = Process.GetProcessById(pid))
                            {
                                if (process.StartTime.ToUniversalTime().Ticks != ticks || !LaunchIdentity.SamePath(process.MainModule.FileName, Application.ExecutablePath)) continue;
                                process.CloseMainWindow(); if (!process.WaitForExit(1500)) process.Kill();
                            }
                        }
                        catch (ArgumentException) { }
                        catch (InvalidOperationException) { }
                    }
                }
                catch { }
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}
