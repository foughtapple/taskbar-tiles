// Tests use pure metadata and disposable local files/Forms. No taskbar, user
// settings, Windows usage records or installed apps are changed or launched.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class LauncherExperienceTests
    {
        static int checks;
        static void Require(bool condition, string name)
        { checks++; if (!condition) throw new InvalidOperationException("FAILED: " + name); }
        static LauncherKey Key(string id, string exe = "", string args = "")
        { return new LauncherKey { AppId = id, Exe = exe, Arguments = args }; }
        static AppButton App(string name, string id)
        { return new AppButton { Name = name, DisplayName = name, Id = "Appid:" + id, LauncherIdentity = Key(id) }; }
        internal static void Run(StringBuilder log)
        {
            checks = 0;
            Rectangle desktop = new Rectangle(0, 0, 1920, 1080), hidden = new Rectangle(800, 1090, 44, 44);
            Require(TaskbarScanPolicy.Accept(true, true, true, hidden, desktop), "hidden app remains inventory");
            Require(TaskbarScanPolicy.Accept(true, true, false, Rectangle.Empty, desktop), "virtualised bounds do not erase known identity");
            Require(!TaskbarScanPolicy.Accept(false, true, true, hidden, desktop), "hidden item is never a click target");
            Require(!TaskbarScanPolicy.Accept(false, false, true, Rectangle.Empty, desktop), "empty geometry cannot receive a click");
            Require(TaskbarScanPolicy.Accept(false, false, true, new Rectangle(800, 1030, 44, 44), desktop), "visible click target still eligible");
            Require(TaskbarScanPolicy.KnownApp("Appid:app", "Button", false, "Application"), "known identity accepted");
            Require(!TaskbarScanPolicy.KnownApp("StartButton", "Button", false, "Start"), "Start is not an app tile");
            Require(!TaskbarScanPolicy.KnownApp("", "SystemTrayIcon", false, "Volume"), "tray controls are not app tiles");
            Require(TaskbarScanPolicy.Bounds(System.Windows.Rect.Empty).IsEmpty, "empty automation rectangle safe");
            Require(TaskbarScanPolicy.Bounds(new System.Windows.Rect(0, 0, double.PositiveInfinity, 20)).IsEmpty, "infinite automation rectangle safe");
            Require(LauncherKey.Same(Key("app"), Key("APP")), "app identities case insensitive");
            Require(!LauncherKey.Same(Key("Chrome.Profile1", @"C:\Browser\chrome.exe"), Key("Chrome.Profile2", @"C:\Browser\chrome.exe")), "profiles never collapsed");
            Require(!LauncherKey.Same(Key("Chrome.Profile1", @"C:\Browser\chrome.exe"), Key("", @"C:\Browser\chrome.exe")), "missing profile cannot become generic executable match");
            Require(!LauncherKey.Same(Key("", @"C:\App\app.exe", "--one"), Key("", @"C:\App\app.exe", "--two")), "distinct explicit arguments retained");
            Require(LauncherKey.Same(Key("", @"C:\App\app.exe"), Key("", @"c:/app/app.exe")), "path aliases deduplicate");
            var merged = TaskbarFallback.Merge(new[] { App("B", "b"), App("removed", "stale"), App("A", "a") },
                new[] { App("A", "a"), App("B", "b") }, new[] { App("A running", "a"), App("C", "c") });
            Require(string.Join(",", merged.Select(a => a.AppId)) == "b,a,c", "fallback keeps known current order, removes stale and deduplicates");
            var copy = LauncherDescriptor.Copy(App("X", "x")); Require(copy.Image == null, "metadata snapshot owns no shared bitmap");
            Require(!LauncherDescriptor.ApplicationTarget("https://example.com") && !LauncherDescriptor.ApplicationTarget(@"C:\Private\document.txt"), "history excludes websites and documents");
            var catalogue = WindowsSettingsCatalog.Read();
            Require(catalogue.Count >= 80, "expanded Windows settings catalogue");
            Require(catalogue.All(s => s.Kind == "Settings" && s.Entry.Target.StartsWith("ms-settings:", StringComparison.Ordinal)), "settings launch only documented URI scheme");
            Require(catalogue.Select(s => s.Entry.Target).Distinct().Count() == catalogue.Count, "unique settings targets");
            string[,] queries = {
                { "display settings", "ms-settings:display" }, { "screen resolution", "ms-settings:display" },
                { "change resolution", "ms-settings:display" }, { "refresh rate", "ms-settings:display-advanced" },
                { "mouse speed", "ms-settings:mousetouchpad" }, { "auto hide taskbar", "ms-settings:taskbar" },
                { "dark theme", "ms-settings:personalization-colors" }, { "default browser", "ms-settings:defaultapps" },
                { "microphone permissions", "ms-settings:privacy-microphone" }, { "check for updates", "ms-settings:windowsupdate" }
            };
            for (int i = 0; i < queries.GetLength(0); i++)
            { var results = SearchLogic.Filter(catalogue, queries[i, 0], "All"); Require(results.Count > 0 && results[0].Entry.Target == queries[i, 1], "settings query: " + queries[i, 0]); }
            Require(SearchMatch.Typo("dispaly", "display") && SearchMatch.Typo("setings", "settings"), "single edits and transpositions");
            Require(!SearchMatch.Typo("abcd", "abdc") && !SearchMatch.Typo("display", "printers"), "no fuzzy short-token guessing");
            Require(SearchLogic.Filter(catalogue, "flibbertigibbet", "All").Count == 0, "unknown query stays unmatched");
            Require(SearchLogic.Filter(catalogue, "display", "Apps").Count == 0, "category restriction retained");
            int layouts = 0;
            foreach (float scale in new[] { .5f, .65f, 1f, 1.25f, 2f, 3f })
                foreach (int logicalWidth in new[] { 492, 640, 1000, 1500, 2500 })
                    foreach (int searchWidth in new[] { 200, 420, 900 })
                        for (int flags = 0; flags < 32; flags++)
                        {
                            var o = new Options { SearchButtonWidth = searchWidth, WindowsSearchButton = (flags & 1) != 0,
                                FavouritesButton = (flags & 2) != 0, DesktopButton = (flags & 4) != 0,
                                ClipboardButton = (flags & 8) != 0, RecentAppsButton = (flags & 16) != 0 };
                            int width = (int)Math.Round(logicalWidth * scale), height = (int)Math.Round(600 * scale);
                            var g = QuickAccessLayout.Build(width, height, scale, o); var buttons = g.Buttons().ToList();
                            Require(QuickAccessLayout.Enabled(o) == (flags != 0), "action bar enablement includes recents");
                            Require(buttons.All(r => r.Width > 0 && r.Height > 0 && new Rectangle(0, 0, width, height).Contains(r)), "all footer buttons fit");
                            for (int i = 0; i < buttons.Count; i++) for (int j = i + 1; j < buttons.Count; j++) Require(!buttons[i].IntersectsWith(buttons[j]), "footer targets never overlap");
                            if (!g.Recent.IsEmpty) Require(g.Hit(new Point(g.Recent.Left + g.Recent.Width / 2, g.Recent.Top + g.Recent.Height / 2)) == -18, "Recent hit does not collide with page-info -17");
                            layouts++;
                        }
            var normal = QuickAccessLayout.Build(1500, 650, 1, new Options());
            Require(normal.Search.Width == 420 && normal.Recent.Right < normal.Favourites.Left, "wide search and Recent beside Favourites defaults");
            var parsed = Options.Parse(new[] { "SearchButtonWidth=10000", "RecentAppsLimit=50", "SearchSettings=false" });
            Require(parsed.SearchButtonWidth == 900 && parsed.RecentAppsLimit == 10 && !parsed.SearchSettings, "new settings bounded without overriding source preference");
            log.AppendLine("PASS: " + checks + " launcher inventory/identity/search assertions, including " + layouts + " footer layouts. No app launches or taskbar changes.");
        }
        static RecentAppRecord Record(int number)
        {
            return new RecentAppRecord { Launcher = new FavouriteEntry { Id = "test" + number, Name = "App " + number, Target = @"C:\Fixture\App" + number + ".exe" },
                Identity = Key("fixture." + number), OpenedUtc = number, ExplicitLaunch = true };
        }
        internal static int RunNative()
        {
            var log = new StringBuilder(); string folder = Path.Combine(Path.GetTempPath(), "TaskbarTiles-LauncherTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Run(log);
                Directory.CreateDirectory(folder); string path = Path.Combine(folder, "recent-apps.json");
                var store = new RecentAppsStore(path); Require(store.Visible(new LauncherKey[0], 10).Count == 0, "history never invents prior launches");
                int epoch = store.Epoch;
                for (int i = 1; i <= 20; i++) store.Remember(Record(i), epoch);
                var excluded = Enumerable.Range(11, 10).Select(i => Key("fixture." + i)).ToArray();
                var visible = store.Visible(excluded, 10);
                Require(visible.Count == 10 && visible[0].Name == "App 10" && visible[9].Name == "App 1", "filter all taskbar apps before taking ten");
                var repeated = Record(4); repeated.OpenedUtc = 100; store.Remember(repeated, epoch);
                visible = store.Visible(excluded, 10); Require(visible[0].Name == "App 4" && visible.Count(e => e.Name == "App 4") == 1, "repeat moves to front without duplicate");
                Require(new RecentAppsStore(path).Visible(excluded, 10)[0].Name == "App 4", "local history round trip");
                store.Clear(); store.Remember(Record(99), epoch);
                Require(store.Visible(new LauncherKey[0], 10).Count == 0 && !File.Exists(path), "Clear rejects stale queued writes and deletes history");
                var profiles = new RecentAppsStore(path);
                var p1 = Record(1); p1.Identity = Key("Chrome.Profile1", @"C:\Browser\chrome.exe");
                var p2 = Record(2); p2.Identity = Key("Chrome.Profile2", @"C:\Browser\chrome.exe");
                profiles.Remember(p1, profiles.Epoch); profiles.Remember(p2, profiles.Epoch);
                Require(profiles.Visible(new[] { p1.Identity }, 10).Count == 1, "taskbar exclusion preserves another browser profile");
                var equal = Record(5); equal.Identity = Key("same"); profiles.Remember(equal, profiles.Epoch);
                var observed = Record(6); observed.Identity = Key("same"); observed.ExplicitLaunch = false; profiles.Remember(observed, profiles.Epoch);
                Require(profiles.Visible(new LauncherKey[0], 10).Any(e => e.Name == "App 5") && !profiles.Visible(new LauncherKey[0], 10).Any(e => e.Name == "App 6"), "external observation cannot discard explicit shortcut");
                using (var settings = new SettingsWindow(new Options(), delegate { throw new InvalidOperationException("UI fixture must never save"); }, null, new List<AppButton>(), "Recent apps"))
                using (var image = new Bitmap(settings.Width, settings.Height)) settings.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                using (var palette = new FavouritesWindow(new Options { FavouriteAlphabetical = false }, visible, new Rectangle(0, 0, 1000, 650), "Recent apps", "Nothing recent", delegate { return visible; }))
                using (var image = new Bitmap(palette.Width, palette.Height)) palette.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                using (var search = new IntegratedSearch(true)) using (var image = search.Snapshot(new Options { SearchPanelWidth = 1000 }))
                    Require(image.Width > 600, "wide integrated search preview paints");
                log.AppendLine("PASS: real Windows recent-history persistence/exclusion/clear and Settings/Recent/Search construction and paint. Test files removed; no app launched.");
                log.AppendLine("The reporter's Explorer/Windhawk/autohide desktop is not present on CI. Offscreen discovery policy is exercised with metadata fixtures; no claim of a physical desktop reproduction.");
                File.WriteAllText(Path.Combine(Program.Home, "launcher-experience-test.log"), log.ToString()); return 0;
            }
            catch (Exception ex) { log.AppendLine(ex.ToString()); File.WriteAllText(Path.Combine(Program.Home, "launcher-experience-test.log"), log.ToString()); return 1; }
            finally { try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { } }
        }
    }
}
