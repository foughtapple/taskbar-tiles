using System;
using System.IO;
using System.Linq;
namespace TaskbarTiles
{
    static class StreamDockMigrationTests
    {
        internal static int Run(string temp)
        {
            int checks = 0;
            Action<bool, string> check = (ok, label) => { if (!ok) throw new Exception("Unified migration: " + label); checks++; };
            string bundle = Path.Combine(Program.Home, "streamdock");
            string root = Path.Combine(temp, "unified-legacy-tests"), plugins = Path.Combine(root, "plugins"), store = Path.Combine(root, "state"), seed = Path.Combine(root, "seed");
            var m = new DockManager(bundle, plugins, store, () => ""); var p = m.Catalog.Packages.Single();
            m.Unpack(p, seed);
            string clip = "com.foughtapple.controls.clipboard";
            string[] old = p.LegacyFolders.Select(f => Path.Combine(plugins, f)).ToArray();
            // Genuine legacy worker metadata plus private-file fixtures. No worker is run.
            for (int i = 0; i < old.Length; i++) {
                Directory.CreateDirectory(old[i]);
                File.Copy(Path.Combine(seed, "workers", p.LegacyPackageIds[i], "manifest.json"), Path.Combine(old[i], "manifest.json"));
                File.WriteAllText(Path.Combine(old[i], "private.json"), "keep-" + p.LegacyPackageIds[i]);
            }
            DockManager.AtomicText(Path.Combine(store, "state.json"), DockManager.Encode(new DockState { ManagedPackages = p.LegacyPackageIds, EnabledActions = new[] { clip } }));
            string priorManifest = File.ReadAllText(Path.Combine(old[0], "manifest.json"));
            var newer = DockManager.Manifest(old[0]); newer["Version"] = "99.0.0";
            DockManager.AtomicText(Path.Combine(old[0], "manifest.json"), DockManager.Encode(newer));
            bool rejected = false; try { m.Apply(m.State(false), true, false); } catch (IOException) { rejected = true; }
            check(rejected && old.All(Directory.Exists), "newer legacy worker is not downgraded or moved");
            File.WriteAllText(Path.Combine(old[0], "manifest.json"), priorManifest);
            int moved = 0; m.AfterLegacyMoved = () => { if (++moved == 2) throw new IOException("injected partial migration"); };
            rejected = false; try { m.Apply(m.State(false), true, false); } catch (IOException) { rejected = true; }
            check(rejected && old.All(Directory.Exists), "failure after two archived folders restores all old folders");
            check(!Directory.Exists(Path.Combine(plugins, p.Folder)), "failed migration leaves no competing unified plugin");
            check(!File.Exists(Path.Combine(store, "pending-package.json")), "completed rollback clears journal");
            m.AfterLegacyMoved = null; m.Apply(m.State(false), true, false);
            string live = Path.Combine(plugins, p.Folder);
            check(old.All(f => !Directory.Exists(f)), "retry removes all legacy headings");
            for (int i = 0; i < old.Length; i++)
                check(File.ReadAllText(Path.Combine(live, "workers", p.LegacyPackageIds[i], "private.json")) == "keep-" + p.LegacyPackageIds[i], "worker private data imported: " + p.LegacyPackageIds[i]);
            check(DockManager.ManifestActions(live).SetEquals(new[] { clip }), "disabled actions not re-enabled during migration");
            // An interrupt after promotion but before deleting the transaction journal
            // must not restore legacy folders alongside the promoted replacement.
            string batch = Path.Combine(store, "Backups", "after-promotion"); Directory.CreateDirectory(batch);
            var legacyRecord = new DockLegacyJournal { Folder = p.LegacyFolders[0], Backup = "Backups/after-promotion/old" };
            Directory.CreateDirectory(Path.Combine(batch, "old"));
            File.WriteAllText(Path.Combine(batch, "old", "manifest.json"), priorManifest);
            File.WriteAllText(Path.Combine(batch, "old", "private.json"), "keep-controls");
            DockManager.AtomicText(Path.Combine(store, "pending-package.json"), DockManager.Encode(new DockJournal {
                Folder = p.Folder, Backup = "Backups/after-promotion/no-prior-unified", Stage = ".taskbar-stage-promoted", Legacy = new[] { legacyRecord }
            }));
            m.Apply(m.State(false), true, false);
            check(!Directory.Exists(old[0]) && Directory.Exists(live), "post-promotion interrupt recovers to one active plugin");
            check(File.ReadAllText(Path.Combine(live, "workers", "controls", "private.json")) == "keep-controls", "post-promotion retry retains private settings");
            // Disabling directly from old layout preserves a restorable unified copy.
            string root2 = Path.Combine(root, "alloff"), plugins2 = Path.Combine(root2, "plugins"), store2 = Path.Combine(root2, "state");
            var m2 = new DockManager(bundle, plugins2, store2, () => "");
            string old2 = Path.Combine(plugins2, p.LegacyFolders[0]); Directory.CreateDirectory(old2);
            File.WriteAllText(Path.Combine(old2, "manifest.json"), priorManifest); File.WriteAllText(Path.Combine(old2, "private.json"), "off-state-kept");
            DockManager.AtomicText(Path.Combine(store2, "state.json"), DockManager.Encode(new DockState { ManagedPackages = new[] { "controls" } }));
            m2.Apply(m2.State(false), true, false);
            check(!Directory.Exists(old2) && !Directory.Exists(Path.Combine(plugins2, p.Folder)), "all-off migration exposes no category");
            check(File.ReadAllText(Path.Combine(store2, "Disabled", p.Folder, "workers", "controls", "private.json")) == "off-state-kept", "all-off migration keeps disabled worker data");
            m2.Apply(new DockState { EnabledActions = new[] { clip } }, false, false);
            check(File.ReadAllText(Path.Combine(plugins2, p.Folder, "workers", "controls", "private.json")) == "off-state-kept", "re-enable restores data after all-off migration");
            return checks;
        }
    }
}
