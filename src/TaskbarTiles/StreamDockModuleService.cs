// Optional modules have independent immutable releases. The core ships metadata,
// and downloads code only after Install or an installed module's opt-in update.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace TaskbarTiles
{
    sealed class StreamDockRelease
    {
        public int Schema;
        public string Id, Version, MinimumAppVersion, ReleaseTag, AssetName, SHA256;
        internal string AssetUrl { get { return ReleaseInfo.ProjectUrl + "/releases/download/" + ReleaseTag + "/" + AssetName; } }
        internal static StreamDockRelease Parse(string text)
        {
            var r = DockManager.Decode<StreamDockRelease>(text); Version version, minimum;
            if (r == null || r.Schema != 1 || r.Id != "streamdock" || !System.Version.TryParse(r.Version, out version) || version.ToString(3) != r.Version ||
                !System.Version.TryParse(r.MinimumAppVersion, out minimum) || minimum.ToString(3) != r.MinimumAppVersion ||
                r.ReleaseTag != "streamdock-v" + r.Version || r.AssetName != "TaskbarTiles-StreamDock-" + r.Version + ".zip" ||
                (!string.IsNullOrEmpty(r.SHA256) && (r.SHA256.Length != 64 || !r.SHA256.All(Uri.IsHexDigit)))) throw new IOException("Invalid Stream Dock module release index.");
            if (minimum > new System.Version(Program.Version)) throw new IOException("This Stream Dock release requires Taskbar Tiles " + r.MinimumAppVersion + " or newer.");
            if (!string.IsNullOrEmpty(r.SHA256)) r.SHA256 = r.SHA256.ToLowerInvariant(); return r;
        }
    }
    sealed class StreamDockModuleReceipt { public string Version, SHA256; }
    static class StreamDockModuleTransport
    {
        internal const string IndexUrl = "https://raw.githubusercontent.com/foughtapple/taskbar-tiles/main/streamdock/module-index.json";
        internal static bool Allowed(Uri u)
        {
            if (u == null || u.Scheme != Uri.UriSchemeHttps || !u.IsDefaultPort || u.UserInfo.Length != 0 || u.Fragment.Length != 0) return false;
            if (u.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)) return u.AbsoluteUri == IndexUrl;
            return ReleaseInfo.AllowedDownloadUri(u);
        }
        internal static void Download(string source, Stream target, long limit, CancellationToken token)
        {
            Uri uri = new Uri(source); var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int redirects = 0; redirects < 6; redirects++) {
                if (!Allowed(uri)) throw new IOException("Module download redirected outside the trusted repository hosts.");
                token.ThrowIfCancellationRequested();
                var request = (HttpWebRequest)WebRequest.Create(uri); request.AllowAutoRedirect = false; request.Timeout = request.ReadWriteTimeout = 25000;
                request.UserAgent = "TaskbarTiles-Modules/" + Program.Version;
                using (token.Register(request.Abort)) using (var response = (HttpWebResponse)request.GetResponse()) {
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code < 400) { Uri next; if (!Uri.TryCreate(uri, response.Headers["Location"], out next)) throw new IOException("Invalid module redirect."); uri = next; continue; }
                    if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > limit) throw new IOException("Invalid or oversized module response.");
                    using (var input = response.GetResponseStream()) {
                        if (input == null) throw new IOException("Empty module response.");
                        byte[] b = new byte[65536]; long total = 0; int n;
                        while ((n = input.Read(b, 0, b.Length)) != 0) { token.ThrowIfCancellationRequested(); total += n; if (total > limit || watch.Elapsed.TotalMinutes > 5) throw new IOException("Module download exceeded its size/time limit."); target.Write(b, 0, n); }
                        if (response.ContentLength >= 0 && response.ContentLength != total) throw new IOException("Truncated module response.");
                    }
                    return;
                }
            }
            throw new IOException("Too many module redirects.");
        }
        internal static StreamDockRelease Latest(CancellationToken token)
        {
            StreamDockRelease release;
            using (var memory = new MemoryStream()) { Download(IndexUrl, memory, 65536, token); release = StreamDockRelease.Parse(Encoding.UTF8.GetString(memory.ToArray()).TrimStart((char)0xFEFF)); }
            using (var memory = new MemoryStream()) {
                Download(ReleaseInfo.ProjectUrl + "/releases/download/" + release.ReleaseTag + "/SHA256SUMS.txt", memory, 65536, token);
                string actual = ReleaseInfo.ExpectedHash(Encoding.UTF8.GetString(memory.ToArray()).TrimStart((char)0xFEFF), release.AssetName);
                if (!string.IsNullOrEmpty(release.SHA256) && release.SHA256 != actual) throw new IOException("Module index and immutable release checksum disagree.");
                release.SHA256 = actual; return release;
            }
        }
    }
    static class StreamDockModuleService
    {
        static int checking;
        internal static T Exclusive<T>(Func<T> action)
        {
            bool acquired = false;
            using (var mutex = new Mutex(false, "Local\\TaskbarTiles.OptionalStreamDock.v1")) {
                try {
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("Another Stream Dock module operation is running. Try again when it finishes.");
                    return action();
                } finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }
        internal static string ApplyChoices(DockManager manager, DockState desired, bool repair)
        { return Exclusive(delegate { return manager.Apply(desired, false, repair); }); }
        internal static IDisposable StartAutomaticChecks()
        { return new System.Threading.Timer(delegate { CheckAutomaticAsync(); }, null, TimeSpan.Zero, TimeSpan.FromMinutes(30)); }
        internal static string ActiveBundle(string store, string bootstrap)
        {
            string path = Path.Combine(store, "active-module.json"); if (!File.Exists(path)) return bootstrap;
            var r = DockManager.Decode<StreamDockModuleReceipt>(DockManager.ReadBounded(path, 8192)); System.Version v;
            if (r == null || !System.Version.TryParse(r.Version, out v) || v.ToString(3) != r.Version || r.SHA256 == null || r.SHA256.Length != 64 || !r.SHA256.All(Uri.IsHexDigit)) throw new IOException("Saved module version is invalid; choose Install / Update to repair it.");
            string bundle = DockManager.Child(Path.Combine(store, "Modules"), r.Version); DockManager.NoLinks(bundle);
            // Keep the saved receipt as the downgrade/checksum authority, but let
            // Settings and Install restore a cache folder removed outside the app.
            if (!Directory.Exists(bundle)) return bootstrap;
            if (!File.Exists(Path.Combine(bundle, "catalog.json"))) throw new IOException("The downloaded module is missing; choose Install / Update to restore it.");
            return bundle;
        }
        internal static bool Downloaded(DockManager manager)
        { return manager.Catalog.Packages.All(p => File.Exists(DockManager.Child(manager.Bundle, p.Payload))); }
        internal static void ExtractBundle(string archive, string stage)
        {
            long total = 0; var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var zip = ZipFile.OpenRead(archive)) {
                foreach (var e in zip.Entries) {
                    string name = e.FullName.TrimEnd('/'); string path = DockManager.Child(stage, name); DockManager.NoLinks(path);
                    if (!names.Add(name) || names.Count > 100 || (total += e.Length) > 256L * 1024 * 1024 || ((e.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Unsafe or oversized module archive.");
                    if (name != "catalog.json" && name != "README.md" && name != "packages" && name != "packages/taskbartiles.zip") throw new IOException("Unexpected file in module archive.");
                }
                foreach (var e in zip.Entries) {
                    string path = DockManager.Child(stage, e.FullName.TrimEnd('/'));
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var input = e.Open()) using (var output = File.Create(path)) {
                        byte[] b = new byte[65536]; long copied = 0; int n;
                        while ((n = input.Read(b, 0, b.Length)) != 0) { copied += n; if (copied > e.Length || copied > 256L * 1024 * 1024) throw new IOException("Invalid expanded module size."); output.Write(b, 0, n); }
                        if (copied != e.Length) throw new IOException("Truncated module archive entry.");
                    }
                }
            }
        }
        internal static string Install(StreamDockRelease release, DockState desired, CancellationToken token, string store = null, string root = null, Action<string, Stream, long, CancellationToken> download = null, Func<string> busy = null)
        { return Exclusive(delegate { return InstallCore(release, desired, token, store, root, download, busy); }); }
        static string InstallCore(StreamDockRelease release, DockState desired, CancellationToken token, string store = null, string root = null, Action<string, Stream, long, CancellationToken> download = null, Func<string> busy = null)
        {
            store = store ?? Path.Combine(Program.Home, "StreamDockData"); root = root ?? DockManager.DefaultRoot;
            release = StreamDockRelease.Parse(DockManager.Encode(release)); DockManager.NoLinks(store); DockManager.NoLinks(root);
            if (string.IsNullOrEmpty(release.SHA256)) throw new IOException("The module release has no verified checksum.");
            string active = Path.Combine(store, "active-module.json");
            if (File.Exists(active)) {
                var prior = DockManager.Decode<StreamDockModuleReceipt>(DockManager.ReadBounded(active, 8192));
                if (prior == null || prior.Version == null) throw new IOException("Invalid installed module receipt.");
                if (new System.Version(prior.Version) > new System.Version(release.Version)) throw new IOException("The module index would downgrade your installed version; no files changed.");
                if (prior.Version == release.Version && !string.Equals(prior.SHA256, release.SHA256, StringComparison.OrdinalIgnoreCase)) throw new IOException("A published module version changed its checksum. Refusing replacement; publish a new version.");
            }
            string modules = Path.Combine(store, "Modules"), bundle = DockManager.Child(modules, release.Version), receipt = Path.Combine(bundle, "module-receipt.json");
            Directory.CreateDirectory(modules); DockManager.NoLinks(bundle);
            if (Directory.Exists(bundle)) {
                var cached = DockManager.Decode<StreamDockModuleReceipt>(DockManager.ReadBounded(receipt, 8192));
                if (cached.Version != release.Version || cached.SHA256 != release.SHA256) throw new IOException("Cached module does not match its immutable release.");
            } else {
                string temp = Path.Combine(modules, ".download-" + Guid.NewGuid().ToString("N") + ".zip"), stage = Path.Combine(modules, ".stage-" + Guid.NewGuid().ToString("N"));
                try {
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) (download ?? StreamDockModuleTransport.Download)(release.AssetUrl, output, 134217728, token);
                    token.ThrowIfCancellationRequested(); if (DockManager.Hash(temp) != release.SHA256) throw new IOException("Stream Dock module checksum mismatch; no plugin files changed.");
                    Directory.CreateDirectory(stage); ExtractBundle(temp, stage);
                    var staged = new DockManager(stage, root, store, busy);
                    if (staged.Catalog.BundleVersion != release.Version || staged.Catalog.Packages.Length != 1 || staged.Catalog.Packages[0].Id != "taskbartiles" || staged.Catalog.Packages[0].Version != release.Version) throw new IOException("Module catalogue does not match its release.");
                    foreach (var p in staged.Catalog.Packages) if (DockManager.Hash(DockManager.Child(stage, p.Payload)) != p.SHA256) throw new IOException("Module package checksum mismatch.");
                    DockManager.AtomicText(Path.Combine(stage, "module-receipt.json"), DockManager.Encode(new StreamDockModuleReceipt { Version = release.Version, SHA256 = release.SHA256 }));
                    Directory.Move(stage, bundle);
                } finally { if (File.Exists(temp)) File.Delete(temp); if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            }
            token.ThrowIfCancellationRequested();
            var manager = new DockManager(bundle, root, store, busy); string result = manager.Apply(desired, false, false);
            DockManager.AtomicText(active, DockManager.Encode(new StreamDockModuleReceipt { Version = release.Version, SHA256 = release.SHA256 }));
            return "Stream Dock " + release.Version + " installed. " + result;
        }
        internal static string InstallLatest(DockState desired, CancellationToken token)
        { return Exclusive(delegate { return InstallCore(StreamDockModuleTransport.Latest(token), desired, token); }); }
        internal static void CheckAutomaticAsync()
        {
            if (Interlocked.CompareExchange(ref checking, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate {
                try {
                    var manager = DockManager.Open(); if (!manager.HasState) return;
                    var state = manager.State(false); if (!state.AutoUpdate || state.EnabledActions.Length == 0 || state.ManagedPackages.Length == 0) return;
                    string stamp = Path.Combine(manager.Store, "module-last-check.txt"); DateTime last;
                    if (File.Exists(stamp) && DateTime.TryParse(DockManager.ReadBounded(stamp, 256), null, System.Globalization.DateTimeStyles.RoundtripKind, out last) && DateTime.UtcNow - last.ToUniversalTime() < TimeSpan.FromHours(24)) return;
                    DockManager.AtomicText(stamp, DateTime.UtcNow.ToString("o"));
                    using (var stop = new CancellationTokenSource(TimeSpan.FromMinutes(6))) {
                        var release = StreamDockModuleTransport.Latest(stop.Token);
                        if (Downloaded(manager) && new System.Version(manager.Catalog.BundleVersion) >= new System.Version(release.Version)) return;
                        if (manager.Busy() != "") { DockManager.AtomicText(Path.Combine(manager.Store, "last-result.txt"), "UPDATE AVAILABLE: Stream Dock " + release.Version + ". Close Stream Dock, then choose Install / Update in Modules."); return; }
                        string outcome = Exclusive(delegate {
                            var current = DockManager.Open(); var saved = current.State(false);
                            if (!saved.AutoUpdate || saved.EnabledActions.Length == 0) return "Automatic Stream Dock updates are off.";
                            return InstallCore(release, saved, stop.Token);
                        });
                        Program.Log(outcome);
                    }
                } catch (Exception ex) { Program.Log("Optional Stream Dock update: " + ex.Message); }
                finally { Interlocked.Exchange(ref checking, 0); }
            });
        }
    }
}
