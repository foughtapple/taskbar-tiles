// Local recent application launches. No Windows usage database, document titles,
// browser history, process command lines, or network requests are collected.
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class RecentAppRecord
    {
        public FavouriteEntry Launcher;
        public LauncherKey Identity;
        public long OpenedUtc;
        public bool ExplicitLaunch;
    }
    sealed class RecentAppsDocument
    {
        public int Version = 1;
        public List<RecentAppRecord> Items = new List<RecentAppRecord>();
    }
    sealed class RecentAppsStore
    {
        readonly object gate = new object();
        readonly string path;
        List<RecentAppRecord> items = new List<RecentAppRecord>();
        int epoch;
        internal int Epoch { get { lock (gate) return epoch; } }
        internal RecentAppsStore(string file)
        {
            path = file;
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length <= 524288)
                {
                    var document = FavouriteStore.Json().Deserialize<RecentAppsDocument>(File.ReadAllText(path));
                    if (document != null && document.Version == 1 && document.Items != null)
                        items = document.Items.Where(Valid).OrderByDescending(x => x.OpenedUtc).Take(64).ToList();
                }
            }
            catch { Program.Log("Recent apps history could not be read; history starts empty."); }
        }
        internal static bool Valid(RecentAppRecord r)
        { return r != null && r.Launcher != null && r.Identity != null && r.Launcher.ValidateEntry() == null && LauncherDescriptor.ApplicationTarget(r.Launcher.ExpandedTarget); }
        internal void InvalidatePending() { lock (gate) epoch++; }
        internal void Remember(RecentAppRecord record, int observedEpoch)
        {
            if (!Valid(record)) return;
            lock (gate)
            {
                if (observedEpoch != epoch) return;
                var old = items.FirstOrDefault(x => LauncherKey.Same(x.Identity, record.Identity));
                if (old != null)
                {
                    items.Remove(old);
                    // A generic outside-window observation must not lose the exact
                    // shortcut/profile the user deliberately launched earlier.
                    if (old.ExplicitLaunch && !record.ExplicitLaunch)
                    { old.OpenedUtc = record.OpenedUtc; record = old; }
                }
                items.Insert(0, record); items = items.OrderByDescending(x => x.OpenedUtc).Take(64).ToList(); Save();
            }
        }
        internal List<FavouriteEntry> Visible(IEnumerable<LauncherKey> excluded, int limit)
        {
            var keys = excluded.ToList(); var accepted = new List<RecentAppRecord>();
            lock (gate)
                foreach (var item in items.OrderByDescending(x => x.OpenedUtc))
                {
                    if (keys.Any(k => LauncherKey.Same(k, item.Identity)) || accepted.Any(r => LauncherKey.Same(r.Identity, item.Identity))) continue;
                    accepted.Add(item); if (accepted.Count >= Math.Max(1, Math.Min(10, limit))) break;
                }
            return accepted.Select(x => { var e = x.Launcher.Clone(); e.Group = "Recently opened app"; return e; }).ToList();
        }
        internal void Clear()
        {
            lock (gate)
            {
                epoch++; items.Clear();
                foreach (string suffix in new[] { "", ".tmp", ".bak" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
            }
        }
        void Save()
        {
            try
            {
                string temp = path + ".tmp";
                File.WriteAllText(temp, FavouriteStore.Json().Serialize(new RecentAppsDocument { Items = items }), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null, true); else File.Move(temp, path);
            }
            catch { Program.Log("Recent apps history could not be saved."); }
        }
    }
    sealed class RecentAppService : IDisposable
    {
        internal static RecentAppService Current;
        internal readonly RecentAppsStore Store = new RecentAppsStore(Path.Combine(Program.Home, "recent-apps.json"));
        readonly BlockingCollection<Action> jobs = new BlockingCollection<Action>();
        volatile bool stopping;
        volatile Options options;
        Dictionary<string, WindowRecord> previous;
        readonly Dictionary<string, long> pending = new Dictionary<string, long>();
        int baselineEpoch = -1;
        internal RecentAppService(Options settings)
        {
            Current = this; options = settings.Clone();
            var worker = new Thread(Work) { IsBackground = true, Name = "Local recent-app observer" };
            worker.SetApartmentState(ApartmentState.STA); worker.Start();
        }
        internal void Configure(Options settings)
        {
            bool reset = options.RememberRecentApps != settings.RememberRecentApps || options.RecentObserveExternal != settings.RecentObserveExternal;
            options = settings.Clone(); if (reset) Store.InvalidatePending();
        }
        internal void Launched(AppButton app, WindowRecord window)
        {
            if (stopping || !options.RememberRecentApps || window == null) return;
            var copy = LauncherDescriptor.Copy(app);
            var resolved = new WindowRecord { Exe = window.Exe, AppId = window.AppId, IdentityAmbiguous = window.IdentityAmbiguous };
            int epoch = Store.Epoch; long opened = DateTime.UtcNow.Ticks;
            try { jobs.Add(delegate
            {
                if (!options.RememberRecentApps || epoch != Store.Epoch) return;
                var entry = LauncherDescriptor.FromApp(copy);
                if (entry == null) return; // Documents, folders and URLs are not app-history entries.
                var key = LauncherKey.FromEntry(entry, true);
                if (key.Exe.Length == 0) key.Exe = resolved.Exe ?? "";
                if (!LaunchIdentity.ConflictingIds(key.AppId, resolved.AppId) && !string.IsNullOrWhiteSpace(resolved.AppId)) key.AppId = resolved.AppId;
                entry.AppId = key.AppId;
                Store.Remember(new RecentAppRecord { Launcher = entry, Identity = key, OpenedUtc = opened, ExplicitLaunch = true }, epoch);
            }); } catch (InvalidOperationException) { }
        }
        static string WindowKey(WindowRecord w) { return w.Handle.ToInt64() + ":" + w.ProcessId + ":" + w.ProcessStartTicks; }
        void Poll()
        {
            if (!options.RememberRecentApps || !options.RecentObserveExternal) { previous = null; pending.Clear(); return; }
            int epoch = Store.Epoch;
            var windows = WindowInventory.Read();
            var current = windows.ToDictionary(WindowKey, w => w);
            if (previous == null || baselineEpoch != epoch) { previous = current; baselineEpoch = epoch; pending.Clear(); return; }
            long now = DateTime.UtcNow.Ticks;
            foreach (var key in pending.Keys.Where(k => !current.ContainsKey(k)).ToArray()) pending.Remove(key);
            foreach (var w in windows.AsEnumerable().Reverse())
            {
                string key = WindowKey(w);
                if (!previous.ContainsKey(key) && !pending.ContainsKey(key)) pending[key] = now;
                long since;
                // Wait for a second snapshot; do not catalogue splash-only windows.
                if (!pending.TryGetValue(key, out since) || now - since < TimeSpan.TicksPerSecond) continue;
                pending.Remove(key);
                var entry = LauncherDescriptor.FromWindow(w); if (entry == null) continue;
                var identity = LauncherKey.FromEntry(entry, true); identity.Exe = w.Exe ?? identity.Exe;
                Store.Remember(new RecentAppRecord { Launcher = entry, Identity = identity, OpenedUtc = now }, epoch);
            }
            previous = current;
        }
        void Work()
        {
            while (!stopping)
            {
                Action job;
                try { if (jobs.TryTake(out job, 1500)) job(); if (!stopping) Poll(); }
                catch (Exception ex) { Program.Log("Recent apps observer: " + ex.GetType().Name); }
            }
        }
        public void Dispose()
        { if (stopping) return; stopping = true; Store.InvalidatePending(); jobs.CompleteAdding(); if (Current == this) Current = null; }
    }
    sealed partial class Switcher
    {
        RecentAppService recentApps;
        void SetupRecentApps() { recentApps = new RecentAppService(options); }
        List<FavouriteEntry> RecentEntries()
        {
            if (recentApps == null) return new List<FavouriteEntry>();
            return recentApps.Store.Visible(allApps.Select(a => a.LauncherIdentity ?? LauncherKey.FromApp(a, false)), options.RecentAppsLimit);
        }
        void ShowRecentApps()
        {
            if (!options.RecentAppsButton || transient != null) return;
            CancelPassiveLaunchObservation();
            if (pending != null || launchPlacement != null || mover.Busy) { Notify("Finish the current launch or window move first."); return; }
            RefreshApps(); Rectangle anchor = Bounds; Dismiss();
            FavouriteEntry chosen; bool place, settings, back;
            var palette = options.Clone(); palette.FavouriteGroups = false; palette.FavouriteAlphabetical = false;
            palette.FavouriteWidth = Math.Max(440, palette.FavouriteWidth); palette.FavouriteVisibleRows = 10;
            using (var form = new FavouritesWindow(palette, RecentEntries(), anchor, "Recent apps",
                "No recent apps outside your taskbar yet.\nHistory starts with new app openings while Taskbar Tiles is running.", RecentEntries))
            {
                transient = form;
                try { form.ShowDialog(); } finally { transient = null; }
                chosen = form.Chosen; place = form.PlaceInZone; settings = form.SettingsRequested; back = form.ReturnToMenu;
            }
            if (closing) return;
            if (settings) { SettingsCore("Recent apps"); return; }
            if (chosen == null) { if (back) OpenOrCycle(true, false, false); return; }
            var app = FavouriteLaunch.AsApp(chosen); if (place) ChooseZone(null, app); else QueueLaunch(app, null);
        }
    }
    sealed partial class SettingsWindow
    {
        void AddRecentAppsPage()
        {
            var p = Page("Recent apps");
            Section(p, "Recently opened applications", "Up to ten icon-and-name entries, newest first. Apps already in the entire Taskbar apps section are excluded, including its other pages. Window switching alone does not add an entry.");
            Check(p, "RecentAppsButton", "Show Recent apps beside Favourites");
            Check(p, "RememberRecentApps", "Remember app openings locally");
            Check(p, "RecentObserveExternal", "Also notice new app windows opened outside Taskbar Tiles");
            Number(p, "RecentAppsLimit", "Maximum recent apps shown", "One to ten. History retains up to 64 app launch entries so excluding taskbar apps can still leave ten useful choices.", 1, 10, 1);
            Section(p, "Local history and control", "History begins after this update. It contains app names and relaunch shortcuts, not document titles, typed searches or browser history. Nothing is uploaded. Switching off collection stops new entries; existing history remains until cleared. Apps open before the observer starts are not backfilled.");
            var clear = Theme.Button("Clear recent history now", 260);
            clear.Click += delegate
            {
                if (MessageBox.Show(this, "Clear the locally remembered recent apps now? This explicit action is immediate and is not undone by Cancel in Settings.", "Clear recent apps", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                try { if (RecentAppService.Current != null) RecentAppService.Current.Store.Clear(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not clear history"); }
            };
            p.Controls.Add(clear); HintTree(clear, "Delete the local history immediately. Queued observations from before the clear cannot repopulate it. This does not change your favourites, taskbar pins or Windows history.");
            Section(p, "Size and navigation", "The palette uses the Favourites width and row-size controls, preserves most-recent order, and supports arrows, Enter and right-click monitor/zone placement. There are no automatic launches or imported Windows usage records.");
        }
    }
}
