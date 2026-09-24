// Consolidation helpers. Private worker data is copied before bundled code overlays it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace TaskbarTiles
{
    sealed partial class DockManager
    {
        internal Action AfterLegacyMoved; // Fault injection; never set by production.
        void ImportLegacyWorkers(DockPackage p, string stage, string current)
        {
            if (p.Id != "taskbartiles") return;
            for (int i = 0; i < p.LegacyFolders.Length; i++)
            {
                string live = Child(Root, p.LegacyFolders[i]);
                string disabled = Child(Path.Combine(Store, "Disabled"), p.LegacyFolders[i]);
                string old = Directory.Exists(live) ? live : Directory.Exists(disabled) ? disabled : null;
                if (old == null) continue;
                string worker = Child(stage, "workers/" + p.LegacyPackageIds[i]);
                NoLinks(old); NoLinks(worker);
                var incoming = Manifest(worker); var prior = Manifest(old);
                Version oldVersion, newVersion;
                if (!Version.TryParse(Convert.ToString(prior["Version"]), out oldVersion) ||
                    !Version.TryParse(Convert.ToString(incoming["Version"]), out newVersion) || oldVersion > newVersion)
                    throw new IOException("Legacy worker " + p.LegacyFolders[i] + " is newer or unrecognised; no downgrade was applied.");
                if (ManifestActions(old).Except(ManifestActions(worker)).Any())
                    throw new IOException("Unrecognised actions in legacy worker " + p.LegacyFolders[i]);
                // Once unified, its worker settings are authoritative; do not restore an
                // older disabled copy over settings subsequently edited by the user.
                if (current != null && File.Exists(Child(current, "workers/" + p.LegacyPackageIds[i] + "/manifest.json"))) continue;
                CopySafe(old, worker);
            }
        }
        void RollbackJournal(DockJournal j)
        {
            // Validate EVERY path first, before moving either the replacement or old data.
            if (j == null || !Catalog.Packages.Any(p => p.Folder == j.Folder) ||
                string.IsNullOrEmpty(j.Backup) || !j.Backup.StartsWith("Backups/", StringComparison.Ordinal) ||
                string.IsNullOrEmpty(j.Stage) || !j.Stage.StartsWith(".taskbar-stage-", StringComparison.Ordinal))
                throw new IOException("Invalid package recovery record; no folders moved.");
            var package = Catalog.Packages.Single(p => p.Folder == j.Folder);
            string dest = Child(Root, j.Folder), backup = Child(Store, j.Backup), stage = Child(Root, j.Stage);
            NoLinks(dest); NoLinks(backup); NoLinks(stage);
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var legacy in j.Legacy ?? new DockLegacyJournal[0])
            {
                if (legacy == null || !package.LegacyFolders.Contains(legacy.Folder) || !folders.Add(legacy.Folder) ||
                    string.IsNullOrEmpty(legacy.Backup) || !legacy.Backup.StartsWith("Backups/", StringComparison.Ordinal))
                    throw new IOException("Invalid legacy recovery record; no folders moved.");
                NoLinks(Child(Root, legacy.Folder)); NoLinks(Child(Store, legacy.Backup));
            }
            // A pending journal means the transaction never committed. Quarantine the
            // replacement only after it was promoted (stage no longer exists), or when
            // a backed-up old unified folder proves that the existing dest is new.
            bool promoted = !Directory.Exists(stage) && (j.Legacy ?? new DockLegacyJournal[0]).Any(l => Directory.Exists(Child(Store, l.Backup)));
            if (Directory.Exists(dest) && (Directory.Exists(backup) || promoted))
            {
                string failed = Child(Store, "Backups/interrupted-new-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.GetDirectoryName(failed)); Directory.Move(dest, failed);
            }
            if (!Directory.Exists(dest) && Directory.Exists(backup)) Directory.Move(backup, dest);
            foreach (var legacy in (j.Legacy ?? new DockLegacyJournal[0]).Reverse())
            {
                string old = Child(Root, legacy.Folder), saved = Child(Store, legacy.Backup);
                if (!Directory.Exists(old) && Directory.Exists(saved)) Directory.Move(saved, old);
            }
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }
}
