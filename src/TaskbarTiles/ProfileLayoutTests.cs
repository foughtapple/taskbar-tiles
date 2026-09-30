using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class ProfileLayoutTests
    {
        static int checks;
        static void Require(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException("Profile layouts: " + message); }
        static void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (Exception ex) { if (!(ex is InvalidDataException) && !(ex is ArgumentException)) throw; rejected = true; } Require(rejected, message); }
        static MonitorData Monitor()
        {
            return new MonitorData { Key = "hardware#instance", Model = "hardware", Instance = "instance", DeviceName = @"\\.\DISPLAY3", Number = 3,
                Bounds = new Rectangle(-1920, -40, 1920, 1080), WorkArea = new Rectangle(-1920, -40, 1920, 1040),
                Zones = new List<ZoneRect> { new ZoneRect { Number = 1, Bounds = new Rectangle(-1920, -40, 960, 1040) }, new ZoneRect { Number = 2, Bounds = new Rectangle(-960, -40, 960, 1040) } } };
        }
        static ProfileLayoutEntry Entry(string name)
        {
            var monitor = Monitor();
            return ProfileLayoutEntry.From(new FavouriteEntry { Name = name, Target = @"C:\Fixture\" + name + ".exe" },
                new ZoneDestination { Monitor = monitor, Number = 1, Bounds = monitor.Zones[0].Bounds });
        }
        static ProfileLayout Layout(params string[] names) { return new ProfileLayout { Name = "Fixture layout", Entries = names.Select(Entry).ToList() }; }
        static WindowRecord Window(int number) { return new WindowRecord { Handle = new IntPtr(number), ProcessId = (uint)number, Exe = @"C:\Fixture\app.exe" }; }
        sealed class FakeApi : IProfileLayoutApi
        {
            internal readonly Dictionary<string, ProfileWindowMatches> Matches = new Dictionary<string, ProfileWindowMatches>();
            internal readonly HashSet<string> MissingDestinations = new HashSet<string>();
            internal readonly List<string> Launched = new List<string>(), Moved = new List<string>();
            internal Action<WindowRecord, string> LaunchCallback;
            internal Action<string> MoveCallback;
            internal int Cancelled;
            public bool DestinationAvailable(ProfileLayoutEntry entry, out string error) { error = "monitor disconnected"; return !MissingDestinations.Contains(entry.Launcher.Name); }
            public ProfileWindowMatches Find(ProfileLayoutEntry entry) { ProfileWindowMatches matches; return Matches.TryGetValue(entry.Launcher.Name, out matches) ? matches : new ProfileWindowMatches(); }
            public void Launch(ProfileLayoutEntry entry, Action<WindowRecord, string> completed) { Launched.Add(entry.Launcher.Name); LaunchCallback = completed; }
            public void Move(ProfileLayoutEntry entry, WindowRecord window, Action<string> completed) { Moved.Add(entry.Launcher.Name); MoveCallback = completed; }
            public void Cancel() { Cancelled++; }
            public void Dispose() { }
        }
        internal static void Run(StringBuilder log)
        {
            checks = 0;
            var document = new ProfileLayoutDocument { Layouts = new List<ProfileLayout> { Layout("editor") } };
            document.Layouts[0].Entries[0].Launcher.Arguments = "--profile-directory=\"Work profile\" --flag";
            document.Layouts[0].Entries[0].Launcher.WorkingDirectory = @"C:\Fixture\work";
            document.Layouts[0].Entries[0].Launcher.AppId = "Chrome.Work";
            var clone = ProfileLayoutStore.Clone(document);
            Require(clone.Layouts[0].Entries[0].Launcher.Arguments == document.Layouts[0].Entries[0].Launcher.Arguments &&
                clone.Layouts[0].Entries[0].Launcher.AppId == "Chrome.Work" && clone.Layouts[0].Entries[0].Launcher.WorkingDirectory == @"C:\Fixture\work", "JSON retains exact profile arguments, app ID and working directory");
            Require(!ReferenceEquals(clone.Layouts[0].Entries[0].Launcher, document.Layouts[0].Entries[0].Launcher), "editing a clone does not mutate saved launcher metadata");
            Require(clone.Layouts[0].Entries[0].DestinationLabel.Contains("Monitor ") && clone.Layouts[0].Entries[0].DestinationLabel.Contains("Zone 1"), "saved destinations identify both monitor and zone");
            var empty = ProfileLayoutStore.Parse("{\"Version\":1,\"Layouts\":[]}"); Require(empty.Layouts.Count == 0, "empty layout document accepted");
            Reject(delegate { ProfileLayoutStore.Parse("{\"Version\":99,\"Layouts\":[]}"); }, "future document version rejected rather than overwritten");
            Reject(delegate { ProfileLayoutStore.Parse("null"); }, "null document rejected");
            Reject(delegate { ProfileLayoutStore.Parse(new string(' ', ProfileLayoutStore.MaxBytes + 1)); }, "oversized document rejected before deserialize");
            clone = ProfileLayoutStore.Clone(document); clone.Layouts.Add(ProfileLayoutStore.Clone(document).Layouts[0]);
            Reject(delegate { ProfileLayoutStore.Validate(clone); }, "duplicate layout identities and names rejected");
            clone = ProfileLayoutStore.Clone(document); clone.Layouts[0].Entries.Add(clone.Layouts[0].Entries[0]);
            Reject(delegate { ProfileLayoutStore.Validate(clone); }, "duplicate exact profile launchers rejected");
            clone = ProfileLayoutStore.Clone(document); clone.Layouts[0].Entries[0].Launcher.Target = "javascript:alert(1)";
            Reject(delegate { ProfileLayoutStore.Validate(clone); }, "script target rejected");
            clone = ProfileLayoutStore.Clone(document); clone.Layouts[0].Entries[0].Bounds.Width = -100;
            Reject(delegate { ProfileLayoutStore.Validate(clone); }, "invalid destination rectangle rejected");
            clone = ProfileLayoutStore.Clone(document); clone.Layouts[0].Entries[0].Bounds.X = 5000;
            Reject(delegate { ProfileLayoutStore.Validate(clone); }, "destination outside saved monitor rejected");
            clone = new ProfileLayoutDocument { Layouts = Enumerable.Range(0, 33).Select(i => new ProfileLayout { Name = "Layout " + i }).ToList() };
            Reject(delegate { ProfileLayoutStore.Validate(clone); }, "layout count bounded");
            clone = new ProfileLayoutDocument { Layouts = new List<ProfileLayout> { Layout(Enumerable.Range(0, 33).Select(i => "app" + i).ToArray()) } };
            Reject(delegate { ProfileLayoutStore.Validate(clone); }, "apps per layout bounded");

            string temporary = Path.Combine(Path.GetTempPath(), "TaskbarTiles-profile-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                string path = Path.Combine(temporary, "layouts.json"); ProfileLayoutStore.Save(document, path);
                Require(ProfileLayoutStore.Read(path).Layouts.Count == 1, "isolated atomic file write/read");
                var replacement = ProfileLayoutStore.Clone(document); replacement.Layouts[0].Name = "Updated"; ProfileLayoutStore.Save(replacement, path);
                Require(ProfileLayoutStore.Read(path).Layouts[0].Name == "Updated" && ProfileLayoutStore.Read(path + ".bak").Layouts[0].Name == "Fixture layout", "atomic replacement retains recoverable prior document");
                string before = File.ReadAllText(path); replacement.Version = 2;
                Reject(delegate { ProfileLayoutStore.Save(replacement, path); }, "invalid replacement refused");
                Require(File.ReadAllText(path) == before && !Directory.GetFiles(temporary, "*.tmp").Any(), "invalid save preserves existing contents and leaves no temporary file");
            }
            finally { foreach (string path in Directory.GetFiles(temporary)) File.Delete(path); Directory.Delete(temporary); }

            var entry = Entry("editor"); ZoneDestination destination; string error;
            Require(ProfileLayoutPolicy.TryDestination(entry, new[] { Monitor() }, out destination, out error) && destination.Bounds == entry.Bounds.Rectangle, "stable monitor resolves exact negative-coordinate zone");
            Require(!ProfileLayoutPolicy.TryDestination(entry, new MonitorData[0], out destination, out error) && error.Contains("disconnected"), "missing monitor never redirected");
            Require(!ProfileLayoutPolicy.TryDestination(entry, new[] { Monitor(), Monitor() }, out destination, out error), "ambiguous monitor identity rejected");
            var changed = Monitor(); changed.Bounds = new Rectangle(0, 0, 1920, 1080);
            Require(!ProfileLayoutPolicy.TryDestination(entry, new[] { changed }, out destination, out error), "changed display geometry rejected");
            changed = Monitor(); changed.Instance = "another device";
            Require(!ProfileLayoutPolicy.TryDestination(entry, new[] { changed }, out destination, out error), "replaced monitor identity rejected");
            changed = Monitor(); changed.Zones[1].Bounds = new Rectangle(-960, -40, 900, 1040);
            Require(!ProfileLayoutPolicy.TryDestination(entry, new[] { changed }, out destination, out error) && error.Contains("zone layout"), "changed sibling zone invalidates saved layout signature");
            entry = ProfileLayoutEntry.From(new FavouriteEntry { Name = "editor", Target = @"C:\Fixture\editor.exe" }, new ZoneDestination { Monitor = Monitor(), Maximise = true, Bounds = Monitor().WorkArea });
            Require(ProfileLayoutPolicy.TryDestination(entry, new[] { changed }, out destination, out error) && destination.Maximise, "full monitor destination does not depend on zones");
            entry = ProfileLayoutEntry.From(new FavouriteEntry { Name = "editor", Target = @"C:\Fixture\editor.exe" }, new ZoneDestination { Monitor = Monitor(), Bounds = new Rectangle(-1800, 0, 800, 500) }, true);
            Require(ProfileLayoutPolicy.TryDestination(entry, new[] { changed }, out destination, out error) && destination.Bounds == new Rectangle(-1800, 0, 800, 500), "captured app position remains exact when zone configuration changes");

            var app = new AppButton { Id = "Appid:Chrome.Work", LaunchExe = @"C:\Fixture\chrome.exe", LauncherIdentity = new LauncherKey { Arguments = "--profile-directory=Work" } };
            var matching = ProfileLayoutPolicy.MatchWindows(app, new[] { new WindowRecord { AppId = "Chrome.Home", Exe = app.LaunchExe }, new WindowRecord { AppId = "Chrome.Work", Exe = app.LaunchExe } });
            Require(matching.Windows.Count == 1 && !matching.Uncertain, "specific browser profile matches its own window without treating another profile as ambiguity");
            matching = ProfileLayoutPolicy.MatchWindows(app, new[] { new WindowRecord { AppId = "", Exe = app.LaunchExe } });
            Require(matching.Windows.Count == 0 && matching.Uncertain, "temporarily unidentified browser window prevents duplicate launching");
            app.Id = ""; matching = ProfileLayoutPolicy.MatchWindows(app, new[] { new WindowRecord { Exe = app.LaunchExe } });
            Require(matching.Windows.Count == 0 && matching.Uncertain && ProfileLayoutPolicy.UnscopedProfile(app), "arguments alone never select a random Chrome profile");

            var api = new FakeApi(); var one = new ProfileWindowMatches(); one.Windows.Add(Window(1)); api.Matches["open"] = one;
            int finished = 0;
            using (var run = new ProfileLayoutExecution(api, delegate { }, delegate { finished++; }))
            {
                run.Begin(Layout("open", "missing"), true);
                Require(api.Moved.SequenceEqual(new[] { "open" }) && api.Launched.Count == 0 && run.Busy, "move-only starts one existing move and never dispatches an app");
                api.MoveCallback(null);
                Require(!run.Busy && api.Launched.Count == 0 && run.Report.Contains("missing: Skipped: app is not open") && finished == 1, "move-only skips missing app and finishes after existing move");
            }
            api = new FakeApi(); api.MissingDestinations.Add("disconnected"); var ambiguous = new ProfileWindowMatches(); ambiguous.Windows.Add(Window(1)); ambiguous.Windows.Add(Window(2)); api.Matches["ambiguous"] = ambiguous;
            api.Matches["uncertain"] = new ProfileWindowMatches { Uncertain = true };
            using (var run = new ProfileLayoutExecution(api, delegate { }, delegate { }))
            {
                run.Begin(Layout("disconnected", "ambiguous", "uncertain"), false);
                Require(!run.Busy && api.Launched.Count == 0 && api.Moved.Count == 0, "changed destinations and ambiguous identities skip without launch or movement");
                Require(run.Report.Contains("disconnected") && run.Report.Contains("ambiguous") && run.Report.Contains("uncertain"), "all skipped entries receive a report");
            }
            api = new FakeApi();
            using (var run = new ProfileLayoutExecution(api, delegate { }, delegate { }))
            {
                run.Begin(Layout("first", "second"), false); var oldLaunch = api.LaunchCallback;
                Require(api.Launched.SequenceEqual(new[] { "first" }) && api.Moved.Count == 0, "missing apps dispatched one at a time");
                oldLaunch(Window(1), null); Require(api.Moved.SequenceEqual(new[] { "first" }) && api.Launched.Count == 1, "identified launch moves before advancing to next app");
                api.MoveCallback(null); Require(api.Launched.SequenceEqual(new[] { "first", "second" }), "next app starts only after prior placement finishes");
                oldLaunch(Window(1), null); Require(api.Moved.Count == 1, "late duplicate callback cannot move prior app or advance new operation");
                api.LaunchCallback(null, "timed out; no retry");
                Require(!run.Busy && api.Launched.Count == 2 && run.Report.Contains("timed out"), "timeout reports uncertain outcome without relaunch");
            }
            api = new FakeApi();
            using (var run = new ProfileLayoutExecution(api, delegate { }, delegate { }))
            {
                run.Begin(Layout("first", "second"), false); var lateLaunch = api.LaunchCallback; run.Cancel(); lateLaunch(Window(1), null);
                Require(!run.Busy && api.Cancelled == 1 && api.Launched.Count == 1 && api.Moved.Count == 0 && run.Report.Contains("already launched apps stay open"), "cancelled launch callback cannot move apps or dispatch remainder");
            }
            api = new FakeApi(); api.Matches["open"] = one;
            using (var run = new ProfileLayoutExecution(api, delegate { }, delegate { }))
            {
                run.Begin(Layout("open", "missing"), false); var lateMove = api.MoveCallback; run.Cancel(); lateMove(null);
                Require(api.Launched.Count == 0 && !run.Busy, "cancelled move callback cannot launch next app");
            }
            log.AppendLine("PASS: " + checks + " profile layout storage, monitor/zone identity, profile matching, sequential dispatch, existing-only, timeout and cancellation assertions. Tests use injected fixtures; no user apps opened or moved.");
        }
        internal static void RunNativeUI(StringBuilder log)
        {
            var document = new ProfileLayoutDocument { Layouts = new List<ProfileLayout> { Layout("editor", "browser") } };
            using (var picker = new ProfileLayoutsWindow(document, false))
            {
                picker.Show(); Application.DoEvents();
                using (var image = new Bitmap(picker.Width, picker.Height))
                {
                    picker.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    Require(picker.Controls.OfType<ListBox>().Single().Items.Count == 1, "actual profile chooser paints fixture layout without running it");
                    image.Save(Path.Combine(Program.Home, "profile-layout-picker-test.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                picker.Close();
            }
            using (var editor = new ProfileLayoutEditor(document.Layouts[0], new Options()))
            {
                editor.Show(); Application.DoEvents();
                using (var image = new Bitmap(editor.Width, editor.Height))
                {
                    editor.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    Require(editor.Controls.OfType<ListBox>().Single().Items.Count == 2, "actual profile editor paints exact fixture destinations without app enumeration or launches");
                    image.Save(Path.Combine(Program.Home, "profile-layout-editor-test.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                editor.Close();
            }
            using (var progress = new ProfileLayoutProgress("Fixture layout"))
            {
                progress.Show(); progress.UpdateReport("Fixture app: Arranged"); progress.Finish("Fixture app: Arranged"); Application.DoEvents();
                using (var image = new Bitmap(progress.Width, progress.Height)) progress.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                Require(progress.Controls.OfType<Button>().Single().Text == "Close", "finished layout report replaces cancel with close"); progress.Close();
            }
            log.AppendLine("PASS: native profile chooser/editor/progress paint with synthetic launcher data; no apps launched, windows moved or layouts saved.");
        }
    }
}
