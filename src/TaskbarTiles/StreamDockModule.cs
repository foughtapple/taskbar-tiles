// Optional local package manager. No polling, service, network calls or profile edits.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TaskbarTiles
{
    sealed class DockAction { public string Id; public string Name; public string Type; }
    sealed class DockPackage
    {
        public string Id, Name, Folder, Version, Payload, SHA256;
        public string[] LegacyFolders = new string[0];
        public string[] LegacyPackageIds = new string[0];
        public DockAction[] Actions;
    }
    sealed class DockCatalog { public int Schema; public string BundleVersion; public DockPackage[] Packages; }
    sealed class DockState
    {
        public int Schema = 1;
        public bool AutoUpdate = true;
        public string[] EnabledActions = new string[0];
        public string[] ManagedPackages = new string[0];
    }
    sealed class DockLegacyJournal { public string Folder, Backup; }
    sealed class DockJournal { public string Folder, Backup, Stage; public DockLegacyJournal[] Legacy = new DockLegacyJournal[0]; }
    sealed class DockReceipt { public string Package, Version, Hash; public string[] Actions; }
    sealed class DockRow { public DockPackage Package; public DockAction Action; public bool Selected; public string Status; }
    sealed class DockManager
    {
        internal readonly string Bundle, Root, Store;
        internal readonly DockCatalog Catalog;
        internal readonly Func<string> Busy;
        internal Action AfterOldMoved; // Fault injection for isolated rollback tests only.
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        static readonly object JsonLock = new object();
        internal static string Encode(object value) { lock (JsonLock) return Json.Serialize(value); }
        internal static T Decode<T>(string text) { lock (JsonLock) return Json.Deserialize<T>(text); }
        internal static string DefaultRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"HotSpot\StreamDock\plugins"); } }
        internal static DockManager Open() { return new DockManager(Path.Combine(Program.Home, "streamdock"), DefaultRoot, Path.Combine(Program.Home, "StreamDockData"), null); }
        internal string StatePath { get { return Path.Combine(Store, "state.json"); } }
        internal bool HasState { get { return File.Exists(StatePath); } }
        internal DockManager(string bundle, string root, string store, Func<string> busy)
        {
            Bundle = Path.GetFullPath(bundle); Root = Path.GetFullPath(root); Store = Path.GetFullPath(store);
            Busy = busy ?? delegate { return RunningProcesses(Root); };
            Catalog = Decode<DockCatalog>(ReadBounded(Path.Combine(Bundle, "catalog.json"), 262144));
            ValidateCatalog(Catalog);
        }
        internal static string ReadBounded(string path, long limit)
        {
            var f = new FileInfo(path); if (!f.Exists || f.Length > limit) throw new IOException("Missing or oversized file: " + Path.GetFileName(path));
            NoLinks(path); return File.ReadAllText(path, Encoding.UTF8);
        }
        internal static bool IsId(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length <= 160 && value.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-');
        }
        internal static string Child(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Length > 240 || relative.Contains(':') || relative.StartsWith("/") || relative.StartsWith("\\")) throw new IOException("Unsafe package path.");
            var parts = relative.Replace('\\', '/').Split('/');
            foreach (string part in parts)
            {
                if (part == "" || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") || part.IndexOfAny(new[] { '*', '?', '"', '<', '>', '|', '\0' }) >= 0) throw new IOException("Unsafe package path.");
                string stem = part.Split('.')[0].ToUpperInvariant();
                if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem)) throw new IOException("Reserved package path.");
            }
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string result = Path.GetFullPath(Path.Combine(prefix, string.Join(Path.DirectorySeparatorChar.ToString(), parts)));
            if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Package path leaves its folder.");
            return result;
        }
        internal static void NoLinks(string path)
        {
            string p = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(p))
            {
                if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing a linked/reparse-point path: " + p);
                p = Path.GetDirectoryName(p);
            }
        }
        internal static void ValidateCatalog(DockCatalog c)
        {
            if (c == null || c.Schema != 1 || c.Packages == null || c.Packages.Length == 0 || c.Packages.Length > 100) throw new IOException("Unsupported Stream Dock catalogue.");
            Version version; if (!Version.TryParse(c.BundleVersion, out version)) throw new IOException("Invalid bundle version.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var actions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in c.Packages)
            {
                if (p == null || !IsId(p.Id) || !ids.Add(p.Id) || string.IsNullOrEmpty(p.Folder) || !p.Folder.StartsWith("com.foughtapple.", StringComparison.Ordinal) || !p.Folder.EndsWith(".sdPlugin", StringComparison.Ordinal) || !folders.Add(p.Folder) || p.Folder.Contains("/") || p.Folder.Contains("\\")) throw new IOException("Invalid or duplicate package identity.");
                Child(Path.GetTempPath(), p.Folder); Child(Path.GetTempPath(), p.Payload);
                if (!p.Payload.StartsWith("packages/", StringComparison.Ordinal) || !p.Payload.EndsWith(".zip", StringComparison.Ordinal) || !Version.TryParse(p.Version, out version) || p.SHA256 == null || p.SHA256.Length != 64 || !p.SHA256.All(Uri.IsHexDigit)) throw new IOException("Invalid package version or checksum.");
                p.LegacyFolders = p.LegacyFolders ?? new string[0]; p.LegacyPackageIds = p.LegacyPackageIds ?? new string[0];
                if (p.LegacyFolders.Length != p.LegacyPackageIds.Length) throw new IOException("Legacy package mapping is incomplete.");
                for (int i = 0; i < p.LegacyFolders.Length; i++)
                {
                    string legacy = p.LegacyFolders[i], oldId = p.LegacyPackageIds[i];
                    if (string.IsNullOrEmpty(legacy) || !legacy.StartsWith("com.foughtapple.", StringComparison.Ordinal) || !legacy.EndsWith(".sdPlugin", StringComparison.Ordinal) || legacy == p.Folder || legacy.Contains("/") || legacy.Contains("\") || !IsId(oldId)) throw new IOException("Invalid legacy package identity.");
                    Child(Path.GetTempPath(), legacy);
                }
                if (p.Actions == null || p.Actions.Length == 0 || p.Actions.Length > 100) throw new IOException("Missing actions.");
                foreach (var a in p.Actions) if (a == null || !IsId(a.Id) || !actions.Add(a.Id) || string.IsNullOrEmpty(a.Name) || (a.Type != "Button" && a.Type != "View")) throw new IOException("Invalid or duplicate action.");
            }
        }
        DockState NormalizeState(DockState s)
        {
            if (s == null || s.Schema != 1 || s.EnabledActions == null || s.ManagedPackages == null) throw new IOException("Stream Dock choices could not be read. No packages were changed.");
            var mapped = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in s.ManagedPackages)
            {
                var current = Catalog.Packages.FirstOrDefault(p => p.Id == id || p.LegacyPackageIds.Contains(id));
                if (current != null) mapped.Add(current.Id);
            }
            s.ManagedPackages = mapped.OrderBy(x => x).ToArray();
            return s;
        }
        IEnumerable<string> InstalledFolders(DockPackage p)
        {
            yield return p.Folder;
            foreach (string legacy in p.LegacyFolders ?? new string[0]) yield return legacy;
        }
        bool HasLegacy(DockPackage p)
        {
            return (p.LegacyFolders ?? new string[0]).Any(f => Directory.Exists(Child(Root, f)));
        }
        internal DockState State(bool discover)
        {
            if (HasState)
            {
                var s = Decode<DockState>(ReadBounded(StatePath, 262144));
                return NormalizeState(s);
            }
            var enabled = new List<string>(); var managed = new List<string>();
            if (discover)
                foreach (var p in Catalog.Packages)
                {
                    foreach (string folder in InstalledFolders(p))
                    {
                        string dest = Child(Root, folder);
                        if (!File.Exists(Path.Combine(dest, "manifest.json"))) continue;
                        var present = ManifestActions(dest);
                        enabled.AddRange(p.Actions.Where(a => present.Contains(a.Id)).Select(a => a.Id));
                    }
                    // Discovery is not adoption: choices are committed only by Apply.
                }
            return new DockState { EnabledActions = enabled.ToArray(), ManagedPackages = managed.ToArray() };
        }
        internal static Dictionary<string, object> Manifest(string folder)
        {
            return Decode<Dictionary<string, object>>(ReadBounded(Path.Combine(folder, "manifest.json"), 262144));
        }
        internal static HashSet<string> ManifestActions(string folder)
        {
            var d = Manifest(folder); object raw; if (!d.TryGetValue("Actions", out raw)) throw new IOException("Package manifest has no Actions.");
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in (System.Collections.IEnumerable)raw)
            { var a = item as Dictionary<string, object>; object id; if (a == null || !a.TryGetValue("UUID", out id) || !(id is string) || !result.Add((string)id)) throw new IOException("Malformed package actions."); }
            return result;
        }
        internal IEnumerable<DockRow> Rows()
        {
            var s = State(true);
            foreach (var p in Catalog.Packages)
            {
                string installed = Child(Root, p.Folder); HashSet<string> active = new HashSet<string>(); string version = ""; bool legacy = false;
                if (File.Exists(Path.Combine(installed, "manifest.json")))
                { try { active.UnionWith(ManifestActions(installed)); version = Convert.ToString(Manifest(installed)["Version"]); } catch { version = "unreadable"; } }
                foreach (string legacyFolder in p.LegacyFolders ?? new string[0])
                {
                    string old = Child(Root, legacyFolder); if (!File.Exists(Path.Combine(old, "manifest.json"))) continue;
                    legacy = true; try { active.UnionWith(ManifestActions(old)); } catch { }
                }
                foreach (var a in p.Actions)
                {
                    string status = active.Contains(a.Id) ? (legacy ? "Available / consolidate on Apply" : "Available / " + version + (s.ManagedPackages.Contains(p.Id) ? "" : " / not managed yet")) : "Off";
                    yield return new DockRow { Package = p, Action = a, Selected = s.EnabledActions.Contains(a.Id), Status = status };
                }
            }
        }
        internal static void AtomicText(string path, string text)
        {
            NoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, text, new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static string Hash(string file)
        { using (var s = File.OpenRead(file)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
        static void CopySafe(string source, string destination)
        {
            NoLinks(source); long size = 0; int count = 0;
            Action<string, string> copy = null;
            copy = delegate(string src, string dst)
            {
                NoLinks(src); Directory.CreateDirectory(dst);
                foreach (string f in Directory.GetFiles(src))
                { NoLinks(f); size += new FileInfo(f).Length; if (++count > 10000 || size > 256L * 1024 * 1024) throw new IOException("Plugin folder exceeds the safe backup limit."); File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); }
                foreach (string dir in Directory.GetDirectories(src)) copy(dir, Path.Combine(dst, Path.GetFileName(dir)));
            }; copy(source, destination);
        }
        internal void Unpack(DockPackage p, string stage)
        {
            string payload = Child(Bundle, p.Payload); NoLinks(payload);
            if (!Hash(payload).Equals(p.SHA256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Package checksum mismatch: " + p.Name);
            long total = 0; int count = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var zip = ZipFile.OpenRead(payload))
            {
                // Validate the complete archive BEFORE copying any entry.
                foreach (var e in zip.Entries)
                {
                    string name = e.FullName.TrimEnd('/'); if (name == "") throw new IOException("Empty archive name.");
                    string f = Child(stage, name); NoLinks(f);
                    if (!names.Add(name) || ++count > 10000 || (total += e.Length) > 128L * 1024 * 1024 || ((e.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Unsafe or oversized package archive.");
                    if (e.FullName.EndsWith("/")) continue;
                }
                foreach (var e in zip.Entries)
                {
                    string dest = Child(stage, e.FullName.TrimEnd('/'));
                    if (e.FullName.EndsWith("/")) Directory.CreateDirectory(dest);
                    else { Directory.CreateDirectory(Path.GetDirectoryName(dest)); using (var input = e.Open()) using (var output = File.Create(dest)) { var buffer = new byte[65536]; long copied = 0; int n; while ((n = input.Read(buffer, 0, buffer.Length)) != 0) { copied += n; if (copied > e.Length || copied > 128L * 1024 * 1024) throw new IOException("Invalid expanded package size."); output.Write(buffer, 0, n); } if (copied != e.Length) throw new IOException("Truncated package entry."); } }
                }
            }
            var actual = ManifestActions(stage); var expected = new HashSet<string>(p.Actions.Select(a => a.Id));
            if (!actual.SetEquals(expected) || Convert.ToString(Manifest(stage)["Version"]) != p.Version) throw new IOException("Package manifest does not match its catalogue.");
        }
        internal bool Current(DockPackage p, string[] enabled)
        {
            string dest = Child(Root, p.Folder);
            if (HasLegacy(p)) return false;
            if (enabled.Length == 0) return !Directory.Exists(dest);
            try {
                var receipt = Decode<DockReceipt>(ReadBounded(Path.Combine(dest, ".taskbar-tiles-package.json"), 65536));
                return receipt.Package == p.Id && receipt.Version == p.Version && receipt.Hash == p.SHA256 && receipt.Actions != null && new HashSet<string>(receipt.Actions).SetEquals(enabled) && ManifestActions(dest).SetEquals(enabled);
            } catch { return false; }
        }
        void RecoverInterruptedChange()
        {
            string file = Path.Combine(Store, "pending-package.json"); if (!File.Exists(file)) return;
            if (Busy() != "") throw new IOException("A package change was interrupted. Close Stream Dock before recovery.");
            var j = Decode<DockJournal>(ReadBounded(file, 65536));
            if (j == null || !Catalog.Packages.Any(p => p.Folder == j.Folder) || !j.Backup.StartsWith("Backups/", StringComparison.Ordinal) || !j.Stage.StartsWith(".taskbar-stage-", StringComparison.Ordinal)) throw new IOException("Invalid package recovery record; no folders moved.");
            string dest = Child(Root, j.Folder), backup = Child(Store, j.Backup), stage = Child(Root, j.Stage);
            NoLinks(dest); NoLinks(backup); NoLinks(stage);
            // Either the old directory or a fully staged replacement is present.
            // If neither committed, restore the old package before retrying.
            if (!Directory.Exists(dest) && Directory.Exists(backup)) Directory.Move(backup, dest);
            foreach (var legacy in j.Legacy ?? new DockLegacyJournal[0])
            {
                if (legacy == null || !Catalog.Packages.SelectMany(p => p.LegacyFolders ?? new string[0]).Contains(legacy.Folder) || !legacy.Backup.StartsWith("Backups/", StringComparison.Ordinal)) throw new IOException("Invalid legacy recovery record; no folders moved.");
                string old = Child(Root, legacy.Folder), oldBackup = Child(Store, legacy.Backup);
                if (!Directory.Exists(old) && Directory.Exists(oldBackup)) Directory.Move(oldBackup, old);
            }
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            File.Delete(file);
        }
        internal string Apply(DockState desired, bool automatic, bool repair)
        {
            bool acquired = false;
            using (var mutex = new Mutex(false, "Local\\TaskbarTiles.StreamDockManager.v1"))
            {
                try {
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("Another Stream Dock package operation is running.");
                    NoLinks(Root); NoLinks(Store); NoLinks(Bundle);
                    if (automatic && (!HasState || !desired.AutoUpdate)) return "Automatic module updates are off; installed modules were not changed.";
                    var known = new HashSet<string>(Catalog.Packages.SelectMany(p => p.Actions).Select(a => a.Id));
                    if (desired.EnabledActions.Any(id => !known.Contains(id))) throw new IOException("A saved module is not in this catalogue. Refusing to drop it; use a newer Taskbar Tiles build.");
                    desired.EnabledActions = desired.EnabledActions.Distinct().OrderBy(x => x).ToArray();
                    RecoverInterruptedChange();
                    var before = State(false); var managed = new HashSet<string>(before.ManagedPackages.Where(id => Catalog.Packages.Any(p => p.Id == id)));
                    foreach (var p in Catalog.Packages) if (p.Actions.Any(a => desired.EnabledActions.Contains(a.Id))) managed.Add(p.Id);
                    // Explicitly disabling a discovered existing or legacy package is also an adoption.
                    if (!automatic) foreach (var p in Catalog.Packages) if (Directory.Exists(Child(Root, p.Folder)) || HasLegacy(p)) managed.Add(p.Id);
                    desired.ManagedPackages = managed.OrderBy(x => x).ToArray();
                    var plans = Catalog.Packages.Where(p => managed.Contains(p.Id)).Select(p => new { Package = p, Enabled = p.Actions.Where(a => desired.EnabledActions.Contains(a.Id)).Select(a => a.Id).OrderBy(x => x).ToArray() }).Where(x => repair || !Current(x.Package, x.Enabled)).ToList();
                    if (plans.Count == 0) { if (!automatic) AtomicText(StatePath, Encode(desired)); return "Everything is current. No plugin files changed."; }
                    string running = Busy(); if (running != "") throw new IOException("Close Stream Dock fully from its tray icon before applying plugin changes. Still running: " + running);
                    // Entire release payload is verified before any installed folder is moved.
                    foreach (var plan in plans) { string payload = Child(Bundle, plan.Package.Payload); NoLinks(payload); if (Hash(payload) != plan.Package.SHA256) throw new IOException("Package checksum mismatch: " + plan.Package.Name); }
                    Directory.CreateDirectory(Root); Directory.CreateDirectory(Store);
                    string batch = Path.Combine(Store, "Backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(batch);
                    if (HasState) File.Copy(StatePath, Path.Combine(batch, "previous-state.json"));
                    // Persist choices so a failed/blocked upgrade remains visibly pending, never forgotten.
                    AtomicText(StatePath, Encode(desired));
                    int done = 0;
                    foreach (var plan in plans)
                    {
                        var p = plan.Package; string dest = Child(Root, p.Folder), disabled = Child(Path.Combine(Store, "Disabled"), p.Folder);
                        string backup = Child(batch, p.Folder), stage = Child(Root, ".taskbar-stage-" + Guid.NewGuid().ToString("N"));
                        string source = Directory.Exists(dest) ? dest : Directory.Exists(disabled) ? disabled : null;
                        var legacyMoves = (p.LegacyFolders ?? new string[0]).Where(f => Directory.Exists(Child(Root, f))).Select(f => new DockLegacyJournal { Folder = f, Backup = "Backups/" + Path.GetFileName(batch) + "/legacy-" + f }).ToArray();
                        bool movedOld = false; var movedLegacy = new List<DockLegacyJournal>();
                        try {
                            if (source != null) {
                                var prior = Manifest(source); Version priorVersion, incoming;
                                if (Version.TryParse(Convert.ToString(prior["Version"]), out priorVersion) && Version.TryParse(p.Version, out incoming) && priorVersion > incoming) throw new IOException("A newer version of " + p.Name + " is installed. It was not downgraded.");
                                if (ManifestActions(source).Except(p.Actions.Select(a => a.Id)).Any()) throw new IOException("Unrecognised actions in " + p.Folder + "; folder left unchanged.");
                            }
                            foreach (var legacy in legacyMoves)
                            {
                                string old = Child(Root, legacy.Folder);
                                if (ManifestActions(old).Except(p.Actions.Select(a => a.Id)).Any()) throw new IOException("Unrecognised actions in legacy package " + legacy.Folder + "; folder left unchanged.");
                            }
                            if (plan.Enabled.Length != 0) {
                                Directory.CreateDirectory(stage); if (source != null) CopySafe(source, stage);
                                Unpack(p, stage);
                                var manifest = Manifest(stage); var raw = (System.Collections.IEnumerable)manifest["Actions"]; var selected = new List<object>();
                                foreach (var v in raw) { var a = (Dictionary<string, object>)v; if (plan.Enabled.Contains((string)a["UUID"])) selected.Add(a); }
                                manifest["Actions"] = selected.ToArray(); AtomicText(Path.Combine(stage, "manifest.json"), Encode(manifest));
                                AtomicText(Path.Combine(stage, ".taskbar-tiles-package.json"), Encode(new DockReceipt { Package = p.Id, Version = p.Version, Hash = p.SHA256, Actions = plan.Enabled }));
                            }
                            running = Busy(); if (running != "") throw new IOException("Stream Dock reopened during the operation. Close it and Apply again.");
                            NoLinks(dest); NoLinks(disabled); NoLinks(stage);
                            AtomicText(Path.Combine(Store, "pending-package.json"), Encode(new DockJournal { Folder = p.Folder, Backup = "Backups/" + Path.GetFileName(batch) + "/" + p.Folder, Stage = Path.GetFileName(stage), Legacy = legacyMoves }));
                            if (Directory.Exists(dest)) { Directory.Move(dest, backup); movedOld = true; if (AfterOldMoved != null) AfterOldMoved(); }
                            foreach (var legacy in legacyMoves)
                            {
                                string old = Child(Root, legacy.Folder), oldBackup = Child(Store, legacy.Backup); Directory.CreateDirectory(Path.GetDirectoryName(oldBackup)); Directory.Move(old, oldBackup); movedLegacy.Add(legacy);
                            }
                            if (plan.Enabled.Length != 0) Directory.Move(stage, dest);
                            else if (movedOld) {
                                Directory.CreateDirectory(Path.GetDirectoryName(disabled));
                                if (Directory.Exists(disabled)) Directory.Move(disabled, Child(batch, p.Id + "-previously-disabled"));
                                // Keep the backup and an independently restorable disabled copy.
                                CopySafe(backup, disabled);
                            }
                            File.Delete(Path.Combine(Store, "pending-package.json"));
                            done++;
                        }
                        catch {
                            // Roll back this package. Earlier successfully applied packages stay valid.
                            if (movedOld && !Directory.Exists(dest) && Directory.Exists(backup)) Directory.Move(backup, dest);
                            foreach (var legacy in movedLegacy.AsEnumerable().Reverse())
                            {
                                string old = Child(Root, legacy.Folder), oldBackup = Child(Store, legacy.Backup); if (!Directory.Exists(old) && Directory.Exists(oldBackup)) Directory.Move(oldBackup, old);
                            }
                            if ((Directory.Exists(dest) || !movedOld) && movedLegacy.All(x => Directory.Exists(Child(Root, x.Folder)))) File.Delete(Path.Combine(Store, "pending-package.json"));
                            throw;
                        }
                        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
                    }
                    string result = done + " package(s) applied. Reopen Stream Dock to load the available actions. Backup: " + batch;
                    AtomicText(Path.Combine(Store, "last-result.txt"), result); return result;
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }
        internal static string RunningProcesses(string root)
        {
            var found = new List<string>();
            foreach (var p in Process.GetProcesses()) using (p)
            {
                try {
                    string name = p.ProcessName.Replace(" ", "").Replace("-", "").ToLowerInvariant();
                    if (name == "streamdock" || name == "vsdinside") { found.Add(p.ProcessName); continue; }
                    string exe; try { exe = p.MainModule.FileName; } catch { continue; }
                    if (exe.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || ((name == "node20" || name == "node") && exe.Replace(" ", "").IndexOf("streamdock", StringComparison.OrdinalIgnoreCase) >= 0)) found.Add(p.ProcessName);
                } catch { }
            }
            return string.Join(", ", found.Distinct().Take(8));
        }
        internal static int SyncInstalled(bool readinessOnly)
        {
            try {
                var m = Open(); if (!m.HasState || !m.State(false).AutoUpdate) return 0;
                var s = m.State(false);
                if (readinessOnly) {
                    if (s.ManagedPackages.Length == 0) return 0;
                    return m.Busy() == "" ? 0 : 20;
                }
                string message = m.Apply(s, true, false); Program.Log("Stream Dock: " + message); return 0;
            }
            catch (Exception ex) {
                Program.Log("Stream Dock update pending: " + ex.Message);
                try { AtomicText(Path.Combine(Program.Home, "StreamDockData", "last-result.txt"), "UPDATE PENDING: " + ex.Message); } catch { }
                return 20;
            }
        }
    }
}
