using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Text;
namespace TaskbarTiles
{
    static class StreamDockTests
    {
        static int passed;
        static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed++; }
        static void Refuses(Action a, string name) { bool rejected = false; try { a(); } catch { rejected = true; } Check(rejected, name); }
        static DockCatalog Fixture(string bundle, string version, bool badPath)
        {
            Directory.CreateDirectory(Path.Combine(bundle, "packages"));
            string payload = Path.Combine(bundle, "packages", "demo.zip"); if (File.Exists(payload)) File.Delete(payload);
            var manifest = new { Version = version, Actions = new[] { new { UUID = "com.foughtapple.test.a", Name = "A" }, new { UUID = "com.foughtapple.test.b", Name = "B" } }, CodePath = "plugin/demo.exe" };
            using (var z = ZipFile.Open(payload, ZipArchiveMode.Create)) {
                using (var w = new StreamWriter(z.CreateEntry("manifest.json").Open())) w.Write(DockManager.Encode(manifest));
                using (var w = new StreamWriter(z.CreateEntry(badPath ? "../outside.txt" : "plugin/demo.exe").Open())) w.Write("synthetic binary " + version);
            }
            var c = new DockCatalog { Schema = 1, BundleVersion = "0.10.0", Packages = new[] { new DockPackage { Id = "demo", Folder = "com.foughtapple.test.sdPlugin", Name = "Demo", Version = version, Payload = "packages/demo.zip", SHA256 = DockManager.Hash(payload), Actions = new[] { new DockAction { Id = "com.foughtapple.test.a", Name = "A", Type = "View" }, new DockAction { Id = "com.foughtapple.test.b", Name = "B", Type = "Button" } } } } };
            DockManager.AtomicText(Path.Combine(bundle, "catalog.json"), DockManager.Encode(c)); return c;
        }
        internal static int Run()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "TaskbarTiles-DockTests-" + Guid.NewGuid().ToString("N"));
            var messages = new List<string>();
            try {
                passed = 0; string bundle = Path.Combine(tmp, "bundle"), plugins = Path.Combine(tmp, "plugins"), state = Path.Combine(tmp, "state");
                var c = Fixture(bundle, "1.0.0", false); var m = new DockManager(bundle, plugins, state, () => ""); var p = c.Packages[0];
                string live = Path.Combine(plugins, p.Folder); string a = p.Actions[0].Id, b = p.Actions[1].Id;
                Check(!m.HasState && !m.Rows().Any(x => x.Selected), "new catalogue is opt-in until Setup or Settings explicitly adopts it");
                var freshDefaults=m.FreshInstallDefaults();
                Check(freshDefaults.AutoUpdate&&freshDefaults.EnabledActions.OrderBy(x=>x).SequenceEqual(new[]{a,b}.OrderBy(x=>x))&&freshDefaults.ManagedPackages.SequenceEqual(new[]{"demo"}),"fresh-install Stream Dock option enables the complete bundled package");
                Check(m.Apply(new DockState(), true, false).Contains("off"), "unconfigured startup is read-only");
                Check(!Directory.Exists(plugins), "no plugin root created by read-only startup");
                m.Apply(new DockState { EnabledActions = new[] { a } }, false, false);
                Check(Directory.Exists(live), "enable installs package");
                Check(DockManager.ManifestActions(live).SetEquals(new[] { a }), "only selected action appears in manifest");
                Check(m.State(false).ManagedPackages.Contains("demo"), "manager ownership persisted");
                Check(m.Current(p, new[] { a }), "receipt matches payload and selection");
                File.WriteAllText(Path.Combine(live, "local-settings.json"), "synthetic user settings");
                string unrelated = Path.Combine(plugins, "com.example.untouched.sdPlugin"); Directory.CreateDirectory(unrelated); File.WriteAllText(Path.Combine(unrelated, "settings"), "keep");
                int backups = Directory.GetDirectories(Path.Combine(state, "Backups")).Length;
                Check(m.Apply(m.State(false), true, false).Contains("current"), "idempotent upgrade is a no-op");
                Check(Directory.GetDirectories(Path.Combine(state, "Backups")).Length == backups, "no redundant backups");
                m.Apply(new DockState { EnabledActions = new[] { a, b } }, false, false);
                Check(DockManager.ManifestActions(live).Count == 2, "second action independently enabled");
                Check(File.ReadAllText(Path.Combine(live, "local-settings.json")) == "synthetic user settings", "settings preserved on action change");
                m.Apply(new DockState { EnabledActions = new[] { b } }, false, false);
                Check(DockManager.ManifestActions(live).SetEquals(new[] { b }), "partial disable removes action from discovery");
                Check(File.ReadAllText(Path.Combine(unrelated, "settings")) == "keep", "unrelated plugin untouched");
                m.Apply(new DockState(), false, false);
                Check(!Directory.Exists(live), "all-off unloads entire package folder");
                Check(File.Exists(Path.Combine(state, "Disabled", p.Folder, "local-settings.json")), "off keeps private settings in disabled folder");
                m.Apply(new DockState { EnabledActions = new[] { a } }, false, false);
                Check(File.ReadAllText(Path.Combine(live, "local-settings.json")) == "synthetic user settings", "reenable restores preserved settings");
                Fixture(bundle, "1.1.0", false); m = new DockManager(bundle, plugins, state, () => "");
                m.Apply(m.State(false), true, false);
                Check(Convert.ToString(DockManager.Manifest(live)["Version"]) == "1.1.0", "Taskbar update upgrades enabled package");
                Check(DockManager.ManifestActions(live).SetEquals(new[] { a }), "update does not enable previously disabled action");
                Check(File.ReadAllText(Path.Combine(live, "local-settings.json")) == "synthetic user settings", "update preserves private data");
                var optout = m.State(false); optout.AutoUpdate = false; m.Apply(optout, false, false); Fixture(bundle, "1.2.0", false); m = new DockManager(bundle, plugins, state, () => ""); m.Apply(m.State(false), true, false);
                Check(Convert.ToString(DockManager.Manifest(live)["Version"]) == "1.1.0", "automatic-update opt-out respected");
                var busy = new DockManager(bundle, plugins, state, () => "StreamDock");
                Refuses(() => busy.Apply(new DockState { EnabledActions = new[] { a } }, false, false), "running host blocks replacement");
                Check(Convert.ToString(DockManager.Manifest(live)["Version"]) == "1.1.0", "busy host leaves old package usable");
                m.AfterOldMoved = () => { throw new IOException("simulated failure"); };
                Refuses(() => m.Apply(new DockState { EnabledActions = new[] { a } }, false, false), "simulated swap failure reported");
                Check(Convert.ToString(DockManager.Manifest(live)["Version"]) == "1.1.0", "failed swap restores old package");
                Check(File.ReadAllText(Path.Combine(live, "local-settings.json")) == "synthetic user settings", "rollback preserves settings");
                m.AfterOldMoved = null; m.Apply(m.State(false), true, false);
                Check(Convert.ToString(DockManager.Manifest(live)["Version"]) == "1.2.0", "pending choices reconcile on retry");
                File.AppendAllText(Path.Combine(bundle, "packages/demo.zip"), "tamper");
                Refuses(() => m.Apply(m.State(false), false, true), "corrupt package is rejected");
                Check(Convert.ToString(DockManager.Manifest(live)["Version"]) == "1.2.0", "tamper does not alter installed package");
                Fixture(bundle, "1.3.0", true); m = new DockManager(bundle, plugins, state, () => "");
                Refuses(() => m.Apply(m.State(false), false, true), "zip traversal rejected");
                Check(!File.Exists(Path.Combine(plugins, "outside.txt")), "no file escaped stage");
                Check(Convert.ToString(DockManager.Manifest(live)["Version"]) == "1.2.0", "malicious archive leaves package intact");
                foreach (string path in new[] { "../x", "x/../../z", "/absolute", "C:\\x", "x:stream", "x//y", "x/CON.txt", "x/.. ", "x/file." }) Refuses(() => DockManager.Child(tmp, path), "unsafe path " + path);
                c = Fixture(bundle, "1.3.0", false); c.Packages[0].Actions[1].Id = a;
                Refuses(() => DockManager.ValidateCatalog(c), "duplicate action id rejected");
                c.Packages[0].Folder = "com.someoneelse.test.sdPlugin";
                Refuses(() => DockManager.ValidateCatalog(c), "foreign package identity rejected");
                // Recover an interrupted swap with the old package already backed up.
                Fixture(bundle, "1.3.0", false); m = new DockManager(bundle, plugins, state, () => "");
                string recovery = Path.Combine(state, "Backups", "interrupted", p.Folder);
                Directory.CreateDirectory(Path.GetDirectoryName(recovery)); Directory.Move(live, recovery);
                string abandonedStage = Path.Combine(plugins, ".taskbar-stage-interrupted"); Directory.CreateDirectory(abandonedStage);
                DockManager.AtomicText(Path.Combine(state, "pending-package.json"), DockManager.Encode(new DockJournal { Folder=p.Folder, Backup="Backups/interrupted/"+p.Folder, Stage=".taskbar-stage-interrupted" }));
                m.Apply(new DockState { EnabledActions=new[] {a} }, false, false);
                Check(!Directory.Exists(abandonedStage), "interrupted stage removed");
                Check(File.ReadAllText(Path.Combine(live, "local-settings.json")) == "synthetic user settings", "interrupted recovery retains private settings");
                Check(Convert.ToString(DockManager.Manifest(live)["Version"]) == "1.3.0", "recovered package upgraded on retry");
                Check(!File.Exists(Path.Combine(state,"pending-package.json")), "journal cleared after commit");
                // Consolidating old package folders into one new plugin preserves choices and archives the legacy folder.
                string migrateRoot = Path.Combine(tmp, "migration"), migrateBundle = Path.Combine(migrateRoot, "bundle"), migratePlugins = Path.Combine(migrateRoot, "plugins"), migrateState = Path.Combine(migrateRoot, "state");
                var mc = Fixture(migrateBundle, "2.0.0", false);
                mc.Packages[0].LegacyFolders = new[] { "com.foughtapple.oldtest.sdPlugin" }; mc.Packages[0].LegacyPackageIds = new[] { "oldtest" };
                DockManager.AtomicText(Path.Combine(migrateBundle, "catalog.json"), DockManager.Encode(mc));
                string legacy = Path.Combine(migratePlugins, "com.foughtapple.oldtest.sdPlugin"); Directory.CreateDirectory(legacy);
                DockManager.AtomicText(Path.Combine(legacy, "manifest.json"), DockManager.Encode(new { Version = "1.0.0", Actions = new[] { new { UUID = a, Name = "A" } } }));
                Directory.CreateDirectory(migrateState); DockManager.AtomicText(Path.Combine(migrateState, "state.json"), DockManager.Encode(new DockState { EnabledActions = new[] { a }, ManagedPackages = new[] { "oldtest" } }));
                var mm = new DockManager(migrateBundle, migratePlugins, migrateState, () => "");
                Check(mm.State(false).ManagedPackages.SequenceEqual(new[] { "demo" }), "legacy package ownership maps to unified package");
                Check(mm.Rows().Single(x => x.Action.Id == a).Status.Contains("consolidate"), "legacy action is shown as ready to consolidate");
                mm.Apply(mm.State(false), true, false);
                Check(!Directory.Exists(legacy), "legacy plugin folder removed from Stream Dock discovery");
                Check(Directory.Exists(Path.Combine(migratePlugins, mc.Packages[0].Folder)), "unified package installed during automatic update");
                Check(DockManager.ManifestActions(Path.Combine(migratePlugins, mc.Packages[0].Folder)).SetEquals(new[] { a }), "legacy enabled action remains enabled after consolidation");
                Check(Directory.GetDirectories(Path.Combine(migrateState, "Backups"), "legacy-*", SearchOption.AllDirectories).Length >= 1, "legacy package archived in backup");
                // Validate every actual packaged resource without touching installed plugins.
                var realBundle = Path.Combine(Program.Home,"streamdock");
                if (File.Exists(Path.Combine(realBundle,"catalog.json"))) {
                    var real = new DockManager(realBundle,Path.Combine(tmp,"real-plugins"),Path.Combine(tmp,"real-state"),()=>"");
                    passed += StreamDockMigrationTests.Run(tmp);
                    Check(real.Catalog.Packages.Length == 1, "one unified Taskbar Tiles plugin package included");
                    Check(real.Catalog.Packages.Sum(x=>x.Actions.Length) == 10, "ten independent actions included");
                    foreach (var pack in real.Catalog.Packages) {
                        string stage = Path.Combine(tmp,"validate-"+pack.Id); Directory.CreateDirectory(stage); real.Unpack(pack,stage);
                        Check(DockManager.ManifestActions(stage).Count == pack.Actions.Length, "real package manifest " + pack.Id);
                    }
                }
                if (File.Exists(Path.Combine(Program.Home,"streamdock","catalog.json"))) {
                    Application.EnableVisualStyles();
                    using (var form = new SettingsWindow(new Options(), delegate(Options o) {}, delegate(Options o) { return new Bitmap(300,160); }, new AppButton[0], "Stream Dock")) {
                        form.ValidateStreamDockView(Path.Combine(Program.Home,"streamdock-settings.png"));
                    }
                    Check(true,"actual Settings Stream Dock page paints with ten actions");
                }
                messages.Add("PASS " + passed + " isolated package-manager checks.");
                messages.Add("No real Stream Dock install, private credentials or user folders were used.");
                File.WriteAllLines(Path.Combine(Program.Home, "streamdock-test.log"), messages); return 0;
            }
            catch (Exception ex) { messages.Add(ex.ToString()); File.WriteAllLines(Path.Combine(Program.Home, "streamdock-test.log"), messages); return 1; }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
    }
}
