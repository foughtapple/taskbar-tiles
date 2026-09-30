using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace TaskbarTiles
{
    static class StreamDockModuleServiceTests
    {
        static StreamDockRelease Fixture(string temp, string version, bool badPath, out string archive)
        {
            string source = Path.Combine(temp, "producer-" + version); Directory.CreateDirectory(Path.Combine(source, "packages"));
            string package = Path.Combine(source, "packages", "taskbartiles.zip");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create)) {
                using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(DockManager.Encode(new { Version = version, Actions = new[] { new { UUID = "com.foughtapple.test.a" }, new { UUID = "com.foughtapple.test.b" } } }));
                using (var writer = new StreamWriter(zip.CreateEntry("plugin/test.exe").Open())) writer.Write("synthetic runtime " + version);
            }
            var catalog = new DockCatalog { Schema = 1, BundleVersion = version, Packages = new[] { new DockPackage {
                Id = "taskbartiles", Folder = "com.foughtapple.taskbartiles.sdPlugin", Name = "Stream Dock", Version = version, Payload = "packages/taskbartiles.zip", SHA256 = DockManager.Hash(package),
                Actions = new[] { new DockAction { Id = "com.foughtapple.test.a", Name = "A", Type = "Button" }, new DockAction { Id = "com.foughtapple.test.b", Name = "B", Type = "View" } }
            } } };
            DockManager.AtomicText(Path.Combine(source, "catalog.json"), DockManager.Encode(catalog));
            archive = Path.Combine(temp, "module-" + version + ".zip"); ZipFile.CreateFromDirectory(source, archive);
            if (badPath) using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) using (var writer = new StreamWriter(zip.CreateEntry("../escaped.txt").Open())) writer.Write("bad");
            return new StreamDockRelease { Schema = 1, Id = "streamdock", Version = version, MinimumAppVersion = "0.0.0", ReleaseTag = "streamdock-v" + version, AssetName = "TaskbarTiles-StreamDock-" + version + ".zip", SHA256 = DockManager.Hash(archive) };
        }
        internal static int Run(string root)
        {
            int checks = 0; Action<bool, string> check = (ok, label) => { if (!ok) throw new Exception("Optional modules: " + label); checks++; };
            Action<Action, string> rejects = (action, label) => { bool refused = false; try { action(); } catch { refused = true; } check(refused, label); };
            string temp = Path.Combine(root, "download-module-tests"), store = Path.Combine(temp, "state"), plugins = Path.Combine(temp, "plugins"); Directory.CreateDirectory(temp);
            check(!new DockState().AutoUpdate, "fresh module automatic updates require opt-in");
            string archive; var release = Fixture(temp, "1.1.0", false, out archive); string currentArchive = archive; int downloads = 0;
            Action<string, Stream, long, CancellationToken> download = (url, output, limit, token) => { check(url.StartsWith(ReleaseInfo.ProjectUrl + "/releases/download/streamdock-v", StringComparison.Ordinal), "asset URL constrained"); downloads++; using (var input = File.OpenRead(currentArchive)) input.CopyTo(output); };
            rejects(() => StreamDockRelease.Parse("{}"), "malformed index rejected");
            var wrong = StreamDockRelease.Parse(DockManager.Encode(release)); wrong.ReleaseTag = "main";
            rejects(() => StreamDockRelease.Parse(DockManager.Encode(wrong)), "mutable branch reference rejected");
            check(!StreamDockModuleTransport.Allowed(new Uri("http://raw.githubusercontent.com/foughtapple/taskbar-tiles/main/streamdock/module-index.json")), "HTTP index rejected");
            check(!StreamDockModuleTransport.Allowed(new Uri("https://raw.githubusercontent.com/other/repo/main/streamdock/module-index.json")), "foreign index rejected");
            check(!StreamDockModuleTransport.Allowed(new Uri("https://github.com/other/repo/releases/download/streamdock-v1.1.0/a.zip")), "foreign assets rejected");
            var desired = new DockState { EnabledActions = new[] { "com.foughtapple.test.a" }, AutoUpdate = false };
            StreamDockModuleService.Install(release, desired, CancellationToken.None, store, plugins, download, () => "");
            string live = Path.Combine(plugins, "com.foughtapple.taskbartiles.sdPlugin");
            check(DockManager.ManifestActions(live).SetEquals(desired.EnabledActions), "download enables only selected functions");
            check(File.Exists(Path.Combine(store, "active-module.json")), "active module pointer committed");
            check(!new DockManager(StreamDockModuleService.ActiveBundle(store, "unused"), plugins, store, () => "").State(false).AutoUpdate, "per-module update opt-out retained");
            var optedIn = new DockManager(StreamDockModuleService.ActiveBundle(store, "unused"), plugins, store, () => ""); var saved = optedIn.State(false); saved.AutoUpdate = true;
            StreamDockModuleService.ApplyChoices(optedIn, saved, false);
            check(optedIn.State(false).AutoUpdate, "existing saved automatic-update opt-in preserved");
            saved.AutoUpdate = false; StreamDockModuleService.ApplyChoices(optedIn, saved, false);
            File.WriteAllText(Path.Combine(live, "private-settings.json"), "preserve-me");
            StreamDockModuleService.Install(release, desired, CancellationToken.None, store, plugins, download, () => "");
            check(downloads == 1, "same immutable release reuses verified cache");
            var changed = StreamDockRelease.Parse(DockManager.Encode(release)); changed.SHA256 = new string('f', 64);
            rejects(() => StreamDockModuleService.Install(changed, desired, CancellationToken.None, store, plugins, download, () => ""), "changed checksum for published version rejected");
            Directory.Move(Path.Combine(store, "Modules", "1.1.0"), Path.Combine(temp, "missing-cache-backup"));
            check(StreamDockModuleService.ActiveBundle(store, "bootstrap") == "bootstrap", "missing cache leaves Modules page and installer accessible");
            StreamDockModuleService.Install(release, desired, CancellationToken.None, store, plugins, download, () => "");
            check(downloads == 2 && File.ReadAllText(Path.Combine(live, "private-settings.json")) == "preserve-me", "missing cache is downloaded again without losing private settings");
            release = Fixture(temp, "1.2.0", false, out currentArchive);
            using (var entered = new ManualResetEvent(false)) using (var resume = new ManualResetEvent(false)) {
                var update = System.Threading.Tasks.Task.Factory.StartNew(delegate {
                    return StreamDockModuleService.Install(release, desired, CancellationToken.None, store, plugins,
                        (url, output, limit, token) => { entered.Set(); if (!resume.WaitOne(5000)) throw new IOException("Fixture coordination timeout."); using (var input = File.OpenRead(currentArchive)) input.CopyTo(output); }, () => "");
                });
                try {
                    check(entered.WaitOne(3000), "independent module update begins under operation lock");
                    rejects(() => StreamDockModuleService.ApplyChoices(optedIn, new DockState { EnabledActions = new[] { "com.foughtapple.test.b" } }, false), "manual module choices cannot interleave with download and active-pointer commit");
                } finally { resume.Set(); }
                check(update.Wait(5000), "serialized module update completes");
            }
            check(Convert.ToString(DockManager.Manifest(live)["Version"]) == "1.2.0", "independent module release updates without core release");
            check(File.ReadAllText(Path.Combine(live, "private-settings.json")) == "preserve-me", "module update preserves private settings");
            check(DockManager.ManifestActions(live).Count == 1, "module update preserves disabled functions");
            release = Fixture(temp, "1.3.0", true, out currentArchive);
            rejects(() => StreamDockModuleService.Install(release, desired, CancellationToken.None, store, plugins, download, () => ""), "traversal archive rejected before plugin changes");
            check(!File.Exists(Path.Combine(store, "Modules", "escaped.txt")) && Convert.ToString(DockManager.Manifest(live)["Version"]) == "1.2.0", "malicious archive leaves active module usable");
            release = Fixture(temp, "1.4.0", false, out currentArchive); release.SHA256 = new string('0', 64);
            rejects(() => StreamDockModuleService.Install(release, desired, CancellationToken.None, store, plugins, download, () => ""), "corrupt module hash rejected");
            var manager = new DockManager(StreamDockModuleService.ActiveBundle(store, "unused"), plugins, store, () => ""); manager.Apply(new DockState(), false, false);
            check(!Directory.Exists(live) && File.Exists(Path.Combine(store, "Disabled", "com.foughtapple.taskbartiles.sdPlugin", "private-settings.json")), "module removal keeps restorable private data");
            return checks;
        }
    }
}
