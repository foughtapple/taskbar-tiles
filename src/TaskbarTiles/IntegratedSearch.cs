// Search remains a child surface of the switcher. No Win+S, Explorer search UI,
// web queries, recursive disk crawler, query history or command interpreter.
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class SearchItem
    {
        internal string Name, Kind, Detail, Keywords;
        internal FavouriteEntry Entry;
        internal AppButton Taskbar;
        internal WindowItem Window;
        internal string Key { get { return Window != null ? "window:" + Window.Handle : Taskbar != null ? "taskbar:" + Taskbar.Key : (Entry == null ? Name : Entry.Target + "\n" + Entry.Arguments); } }
        internal FavouriteEntry IconEntry
        {
            get
            {
                if (Entry != null) return Entry;
                string target = Taskbar == null ? "" : !string.IsNullOrWhiteSpace(Taskbar.ShortcutPath) ? Taskbar.ShortcutPath :
                    !string.IsNullOrWhiteSpace(Taskbar.LaunchExe) ? Taskbar.LaunchExe :
                    !string.IsNullOrWhiteSpace(Taskbar.AppId) ? @"shell:AppsFolder\" + Taskbar.AppId : "";
                return new FavouriteEntry { Name = Name, Target = target, Group = Kind };
            }
        }
    }
    static class SearchLogic
    {
        internal static string[] Words(string query)
        { return (query ?? "").Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Take(12).ToArray(); }
        internal static int Score(SearchItem item, string query)
        { return SearchMatch.Score(item, query); }
        internal static List<SearchItem> Filter(IEnumerable<SearchItem> source, string query, string kind)
        {
            return source.Where(x => kind == "All" || kind == "Files" && x.Kind == "Folders" || x.Kind == kind)
                .Select(x => new { Item = x, Rank = Score(x, query) }).Where(x => x.Rank >= 0)
                .OrderByDescending(x => x.Rank).ThenBy(x => x.Item.Name, StringComparer.CurrentCultureIgnoreCase)
                .GroupBy(x => x.Item.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First().Item).Take(300).ToList();
        }
        internal static string SqlLikeLiteral(string word)
        {
            // Windows Search SQL recognises bracket-escaped wildcards; quotes are SQL-escaped.
            var b = new StringBuilder();
            foreach (char c in word) { if (c == '\'') b.Append("''"); else if (c == '[') b.Append("[[]"); else if (c == '%' || c == '_') b.Append('[').Append(c).Append(']'); else if (c != '\0') b.Append(c); }
            return b.ToString();
        }
        internal static string FileQuery(string query)
        {
            string[] words = Words(query); if (words.Length == 0) return "";
            return "SELECT TOP 100 System.ItemNameDisplay, System.ItemPathDisplay, System.ItemUrl FROM SystemIndex WHERE System.ItemUrl LIKE 'file:%' AND " +
                string.Join(" AND ", words.Select(w => "System.ItemNameDisplay LIKE '%" + SqlLikeLiteral(w) + "%'").ToArray()) + " ORDER BY System.ItemNameDisplay";
        }
        internal static SearchItem FromEntry(FavouriteEntry entry, string kind, string detail)
        { return new SearchItem { Name = entry.Name, Kind = kind, Detail = detail ?? entry.Detail, Entry = entry.Clone() }; }
        internal static List<SearchItem> SettingsAndPlaces()
        {
            var result = WindowsSettingsCatalog.Read();
            foreach (var folder in new[] { Tuple.Create("Downloads", "shell:Downloads"), Tuple.Create("Documents", "shell:Personal"), Tuple.Create("Desktop", "shell:Desktop"), Tuple.Create("Pictures", "shell:My Pictures") })
                result.Add(FromEntry(new FavouriteEntry { Name = folder.Item1, Target = folder.Item2, Group = "Places" }, "Folders", "Quick folder"));
            return result;
        }
    }
    static class SearchAppCatalog
    {
        static readonly object sync = new object();
        static List<FavouriteEntry> cached;
        static DateTime fetched;
        static bool busy;
        static readonly List<Action<List<FavouriteEntry>, string>> callbacks = new List<Action<List<FavouriteEntry>, string>>();
        internal static void Read(Action<List<FavouriteEntry>, string> done, bool force)
        {
            lock (sync)
            {
                if (!force && cached != null && (DateTime.UtcNow - fetched).TotalMinutes < 5) { done(cached, ""); return; }
                callbacks.Add(done); if (busy) return; busy = true;
            }
            var thread = new Thread(delegate()
            {
                List<FavouriteEntry> apps; string status = "";
                try { apps = InstalledApps.Read(); if (apps.Count == 0) status = "No installed app shortcuts were returned."; }
                catch { apps = new List<FavouriteEntry>(); status = "Installed app list unavailable; other sources still work."; }
                List<Action<List<FavouriteEntry>, string>> notify;
                lock (sync) { cached = apps; fetched = DateTime.UtcNow; busy = false; notify = callbacks.ToList(); callbacks.Clear(); }
                foreach (var callback in notify) try { callback(apps, status); } catch { }
            }) { IsBackground = true, Name = "Local app search catalogue" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
    }
    sealed class IndexSearchWorker : IDisposable
    {
        sealed class Job { internal string Query; internal int Revision; }
        readonly object sync = new object();
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly Action<int, List<SearchItem>, string> completed;
        Job latest;
        volatile bool disposed;
        internal IndexSearchWorker(Action<int, List<SearchItem>, string> callback)
        {
            completed = callback;
            var t = new Thread(Work) { IsBackground = true, Name = "Local filename index search" };
            t.SetApartmentState(ApartmentState.MTA); t.Start();
        }
        internal void Query(string query, int revision) { lock (sync) { if (disposed) return; latest = new Job { Query = query, Revision = revision }; signal.Set(); } }
        internal void CancelPending() { lock (sync) latest = null; }
        void Work()
        {
            try
            {
                while (true)
                {
                    signal.WaitOne(); Job job;
                    lock (sync) { if (disposed) break; job = latest; latest = null; }
                    if (job == null) continue;
                    var results = new List<SearchItem>(); string status = "";
                    try
                    {
                        using (var connection = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
                        using (var command = new OleDbCommand(SearchLogic.FileQuery(job.Query), connection))
                        {
                            command.CommandTimeout = 3; connection.Open();
                            using (var rows = command.ExecuteReader())
                            {
                                while (!disposed && rows.Read() && results.Count < 100)
                                {
                                    string name = Convert.ToString(rows.GetValue(0)), path = Convert.ToString(rows.GetValue(1)), url = Convert.ToString(rows.GetValue(2));
                                    Uri uri;
                                    // Only local filesystem entries, never web/email/search URIs or UNC servers.
                                    if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !uri.IsFile || uri.IsUnc) continue;
                                    if (string.IsNullOrWhiteSpace(path)) path = uri.LocalPath;
                                    if (string.IsNullOrWhiteSpace(name) || !Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) continue;
                                    results.Add(SearchLogic.FromEntry(new FavouriteEntry { Name = name, Target = path, Group = "Files" }, "Files", path));
                                }
                            }
                        }
                        status = results.Count >= 100 ? "First 100 indexed filename matches. Type more to narrow the list." : "Indexed filenames only; unindexed locations are not searched.";
                    }
                    catch { status = "Filename index unavailable or timed out. Apps and other sources still work."; }
                    if (!disposed) completed(job.Revision, results, status);
                }
            }
            finally { signal.Dispose(); }
        }
        public void Dispose() { lock (sync) { if (disposed) return; disposed = true; latest = null; signal.Set(); } }
    }
    sealed class SearchGeometry
    {
        internal Rectangle Bounds, Input, Kind, Back, Refresh, Rows, Status, Previous, Next;
        internal int Capacity, RowHeight, Gap;
        internal static SearchGeometry Build(Options o, Size owner, Rectangle anchor, float scale)
        {
            Func<int, int> s = n => Math.Max(1, (int)Math.Round(n * scale));
            int w = Math.Max(1, Math.Min(s(o.SearchPanelWidth), owner.Width - s(32))), bottom = Math.Min(owner.Height - s(12), anchor.Bottom);
            int h = Math.Max(1, Math.Min(s(174) + o.SearchVisibleRows * (s(o.SearchRowHeight) + s(4)), bottom - s(12)));
            var g = new SearchGeometry();
            int x = Math.Max(s(12), Math.Min(anchor.Left, owner.Width - w - s(12)));
            g.Bounds = new Rectangle(x, bottom - h, w, h);
            g.Input = new Rectangle(s(16), h - s(46), w - s(32), s(28));
            g.Kind = new Rectangle(s(16), s(49), Math.Min(s(200), w - s(170)), s(27));
            g.Back = new Rectangle(w - s(80), s(10), s(64), s(30));
            g.Refresh = new Rectangle(w - s(169), s(10), s(80), s(30));
            g.RowHeight = Math.Min(s(o.SearchRowHeight), Math.Max(1, h - s(176))); g.Gap = s(4);
            g.Capacity = Math.Max(1, Math.Min(o.SearchVisibleRows, (h - s(174) + g.Gap) / (g.RowHeight + g.Gap)));
            g.Rows = new Rectangle(s(16), s(86), w - s(32), g.Capacity * (g.RowHeight + g.Gap));
            g.Status = new Rectangle(s(16), h - s(80), w - s(128), s(27));
            g.Previous = new Rectangle(w - s(100), h - s(80), s(36), s(26));
            g.Next = new Rectangle(w - s(52), h - s(80), s(36), s(26));
            return g;
        }
    }
    sealed class IntegratedSearch : Panel
    {
        readonly TextBox input = new TextBox();
        readonly ComboBox kind = new ComboBox();
        readonly Button back = Theme.Button("Back", 64), refresh = Theme.Button("Refresh", 80);
        readonly ToolTip tips = new ToolTip { ShowAlways = true, InitialDelay = 500, AutoPopDelay = 20000, ReshowDelay = 100 };
        readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 240 };
        FavouriteIcons icons;
        IndexSearchWorker index;
        Options options;
        float scale;
        SearchGeometry geometry;
        List<SearchItem> local = new List<SearchItem>(), installed = new List<SearchItem>(), files = new List<SearchItem>(), results = new List<SearchItem>();
        int revision, selection, session;
        bool disposed, initialising;
        string appStatus = "", fileStatus = "", downKey = "";
        readonly bool previewOnly;
        Font ownedFont;
        float lastFontScale;
        internal Action CloseRequested;
        internal Action<SearchItem, bool> Activated;
        internal IntegratedSearch(bool preview = false)
        {
            previewOnly = preview; DoubleBuffered = true; BackColor = Theme.Background; Visible = false; TabStop = false;
            input.BorderStyle = BorderStyle.FixedSingle; input.BackColor = Theme.Card; input.ForeColor = Theme.Text; input.MaxLength = 160;
            input.TextChanged += delegate { if (!initialising) StartQuery(true); };
            input.KeyDown += delegate(object sender, KeyEventArgs e) { if (HandleKey(e.KeyData)) { e.SuppressKeyPress = true; e.Handled = true; } };
            kind.DropDownStyle = ComboBoxStyle.DropDownList; kind.Items.AddRange(new object[] { "All", "Apps", "Windows", "Favourites", "Files", "Settings" });
            kind.SelectedIndex = 0; kind.SelectedIndexChanged += delegate { if (!initialising) StartQuery(true); };
            back.Click += delegate { if (CloseRequested != null) CloseRequested(); };
            refresh.Click += delegate { RefreshInstalled(true); StartQuery(false); };
            Controls.AddRange(new Control[] { input, kind, back, refresh });
            tips.SetToolTip(input, "Search locally. Results stay inside Taskbar Tiles. No search history or web queries are sent.\nEnter opens; right-click or Shift+Enter chooses a screen/zone.");
            tips.SetToolTip(kind, "Limit results to a category. Enable or disable the sources in Settings > Search.");
            tips.SetToolTip(back, "Return to window previews without opening anything. Escape clears text first, then returns.");
            tips.SetToolTip(refresh, "Reload installed shortcuts and rerun this local search. Does not rebuild or change the Windows index.");
            debounce.Tick += delegate { debounce.Stop(); SubmitIndex(); };
            if (!preview) index = new IndexSearchWorker(delegate(int id, List<SearchItem> found, string status)
            { Post(delegate { if (id != revision || !Visible) return; files = found; fileStatus = status; Refilter(false); }); });
        }
        void Post(Action action)
        {
            if (disposed || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(delegate { if (!disposed && !IsDisposed) action(); })); } catch (InvalidOperationException) { }
        }
        internal void Open(Options current, IEnumerable<SearchItem> entries, Size owner, Rectangle anchor, float dpi)
        {
            session++; options = current.Clone(); scale = dpi; local = entries.ToList(); installed.Clear(); files.Clear();
            initialising = true; input.Text = ""; kind.SelectedIndex = 0; initialising = false;
            Arrange(owner, anchor); Visible = true; BringToFront(); FocusInput();
            if (!previewOnly) WindowNative.SendMessage(input.Handle, 0x1501, IntPtr.Zero, "Search apps, settings, windows and files…");
            if (icons == null && !previewOnly) icons = new FavouriteIcons(this);
            appStatus = options.SearchInstalledApps ? "Loading installed apps…" : "";
            StartQuery(true); RefreshInstalled(false);
        }
        internal void Arrange(Size owner, Rectangle anchor)
        {
            geometry = SearchGeometry.Build(options, owner, anchor, scale); Bounds = geometry.Bounds;
            if (ownedFont == null || lastFontScale != scale)
            {
                var old = ownedFont; ownedFont = new Font("Segoe UI", 12 * scale, GraphicsUnit.Pixel); lastFontScale = scale;
                Font = ownedFont; input.Font = Font; kind.Font = Font; back.Font = refresh.Font = Font;
                if (old != null) old.Dispose();
            }
            input.Bounds = geometry.Input; kind.Bounds = geometry.Kind; back.Bounds = geometry.Back; refresh.Bounds = geometry.Refresh;
            back.Font = refresh.Font = Font;
            Invalidate();
        }
        internal void FocusInput() { input.Focus(); }
        internal void HideSurface()
        { session++; revision++; Visible = false; debounce.Stop(); if (index != null) index.CancelPending(); }
        void RefreshInstalled(bool force)
        {
            if (previewOnly || options == null || !options.SearchInstalledApps) return;
            int stamp = session;
            appStatus = "Loading installed apps…"; Invalidate();
            SearchAppCatalog.Read(delegate(List<FavouriteEntry> apps, string status)
            {
                Post(delegate
                {
                    if (stamp != session || !Visible) return;
                    installed = apps.Select(e => SearchLogic.FromEntry(e, "Apps", e.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? "Installed app · Start menu" : "Installed app")).ToList();
                    appStatus = status; Refilter(false);
                });
            }, force);
        }
        void StartQuery(bool reset)
        {
            if (options == null || disposed) return;
            revision++; files.Clear(); if (index != null) index.CancelPending(); debounce.Stop();
            string q = input.Text.Trim(), category = Convert.ToString(kind.SelectedItem);
            bool eligible = options.SearchIndexedFiles && q.Length >= 2 && (category == "All" || category == "Files");
            fileStatus = !options.SearchIndexedFiles ? "Filename search is off in Settings > Search." : q.Length < 2 ? "Type at least two characters to search indexed filenames." : eligible ? "Searching indexed filenames…" : "";
            Refilter(reset); if (eligible && !previewOnly) debounce.Start();
        }
        void SubmitIndex()
        { if (!disposed && Visible && index != null) index.Query(input.Text.Trim(), revision); }
        void Refilter(bool reset)
        {
            string keep = !reset && selection >= 0 && selection < results.Count ? results[selection].Key : "";
            results = SearchLogic.Filter(local.Concat(installed).Concat(files), input.Text, Convert.ToString(kind.SelectedItem));
            int retained = keep.Length == 0 ? -1 : results.FindIndex(x => x.Key == keep);
            selection = retained >= 0 ? retained : reset ? 0 : Math.Min(selection, Math.Max(0, results.Count - 1)); Invalidate();
        }
        int Page { get { return geometry == null ? 0 : selection / geometry.Capacity; } }
        int PageCount { get { return geometry == null ? 1 : Math.Max(1, MenuGeometry.Ceiling(results.Count, geometry.Capacity)); } }
        internal bool HandleKey(Keys keys)
        {
            if (!Visible || options == null) return false;
            Keys code = keys & Keys.KeyCode;
            // Let the category dropdown handle its own arrows/Enter/Escape when open.
            if (kind.DroppedDown && (code == Keys.Up || code == Keys.Down || code == Keys.Enter || code == Keys.Escape || code == Keys.PageUp || code == Keys.PageDown)) return false;
            if (code == Keys.Escape) { if (input.Text.Length > 0) input.Clear(); else if (CloseRequested != null) CloseRequested(); return true; }
            if (keys == (Keys.Control | Keys.Shift | Keys.S)) { if (CloseRequested != null) CloseRequested(); return true; }
            if (keys == (Keys.Control | Keys.F)) { input.Focus(); input.SelectAll(); return true; }
            if (code == Keys.F5) { RefreshInstalled(true); StartQuery(false); return true; }
            if (code == Keys.Up || code == Keys.Down || code == Keys.PageUp || code == Keys.PageDown)
            { int n = code == Keys.Up ? -1 : code == Keys.Down ? 1 : (code == Keys.PageUp ? -1 : 1) * geometry.Capacity; selection = Math.Max(0, Math.Min(results.Count - 1, selection + n)); Invalidate(); return true; }
            if (code == Keys.Enter) { Activate((keys & Keys.Shift) != 0); return true; }
            return false;
        }
        void Activate(bool zone)
        { if (selection < 0 || selection >= results.Count || previewOnly || zone && !options.RightClickZones) return; if (Activated != null) Activated(results[selection], zone); }
        Rectangle Row(int i) { return new Rectangle(geometry.Rows.Left, geometry.Rows.Top + i * (geometry.RowHeight + geometry.Gap), geometry.Rows.Width, geometry.RowHeight); }
        int RowAt(Point point)
        {
            if (geometry == null) return -1;
            int offset = Page * geometry.Capacity;
            for (int i = 0; i < Math.Min(geometry.Capacity, results.Count - offset); i++) if (Row(i).Contains(point)) return offset + i;
            return -1;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); int i = RowAt(e.Location); downKey = i >= 0 ? results[i].Key : ""; }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); int i = RowAt(e.Location);
            if (i >= 0 && results[i].Key == downKey && (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right))
            { selection = i; Activate(e.Button == MouseButtons.Right); return; }
            if (e.Button == MouseButtons.Left && PageCount > 1)
            { if (geometry.Previous.Contains(e.Location)) MovePage(-1); else if (geometry.Next.Contains(e.Location)) MovePage(1); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); int i = RowAt(e.Location);
            Cursor = i >= 0 || geometry.Previous.Contains(e.Location) || geometry.Next.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (i >= 0 && selection != i) { selection = i; tips.SetToolTip(this, results[i].Name + "\n" + results[i].Detail + (options.RightClickZones ? "\nRight-click: choose monitor or zone" : "")); Invalidate(); }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); MovePage(e.Delta < 0 ? 1 : -1); }
        void MovePage(int delta) { int page = Math.Max(0, Math.Min(PageCount - 1, Page + delta)); selection = page * geometry.Capacity; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (geometry == null) return;
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawingUtil.Round(g, ClientRectangle, S(12), Theme.Background, Theme.Accent, Math.Max(1.2f, scale));
            using (var heading = new Font("Segoe UI", 15 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, "Search here", heading, new Rectangle(S(16), S(12), Math.Max(1, geometry.Refresh.Left - S(26)), S(29)), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            Text(g, results.Count + " results" + (PageCount > 1 ? " · page " + (Page + 1) + "/" + PageCount : ""), new Rectangle(geometry.Kind.Right + S(14), S(48), Width - geometry.Kind.Right - S(30), S(28)), Theme.Muted);
            int start = Page * geometry.Capacity;
            for (int n = 0; n < Math.Min(geometry.Capacity, results.Count - start); n++)
            {
                SearchItem item = results[start + n]; Rectangle r = Row(n); bool selected = start + n == selection;
                DrawingUtil.Round(g, r, S(7), selected ? Color.FromArgb(44, 65, 91) : Theme.Card, selected ? Theme.Accent : Theme.Border, 1);
                int side = Math.Min(S(34), r.Height - S(12)); var icon = new Rectangle(r.Left + S(12), r.Top + (r.Height - side) / 2, side, side);
                Bitmap bitmap = icons == null ? null : icons.Get(item.IconEntry);
                if (bitmap != null) g.DrawImage(bitmap, DrawingUtil.Fit(icon, bitmap.Width, bitmap.Height));
                else { DrawingUtil.Round(g, icon, S(6), Color.FromArgb(50, 72, 98), Color.Transparent, 0); Text(g, TextTools.Initials(item.Name), icon, Theme.Accent, true); }
                int x = icon.Right + S(12), w = Math.Max(1, r.Right - x - S(12));
                Text(g, item.Name, new Rectangle(x, r.Top + S(5), w, Math.Max(S(18), r.Height / 2 - S(1))), Theme.Text);
                using (var small = new Font("Segoe UI", 10.5f * scale, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, item.Kind + " · " + item.Detail, small, new Rectangle(x, r.Top + r.Height / 2, w, Math.Max(1, r.Height / 2 - S(4))), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            if (results.Count == 0) Text(g, appStatus.Length > 0 ? appStatus : "No matches. Try another name or category.", geometry.Rows, Theme.Muted, true);
            string status = appStatus.Length > 0 ? appStatus : fileStatus.Length > 0 ? fileStatus : "Enter: open · Right-click: choose zone";
            Text(g, status, geometry.Status, Theme.Muted); tips.SetToolTip(input, "Search here — no Windows Search popup.\n" + status + "\nEnter: open. Shift+Enter / right-click: place. Escape: clear, then back.");
            if (PageCount > 1) { Text(g, "‹", geometry.Previous, Theme.Text, true); Text(g, "›", geometry.Next, Theme.Text, true); }
        }
        int S(int n) { return Math.Max(1, (int)Math.Round(n * scale)); }
        void Text(Graphics g, string text, Rectangle r, Color colour, bool centre = false)
        { TextRenderer.DrawText(g, text, Font, r, colour, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | (centre ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left)); }
        internal Bitmap Snapshot(Options o)
        {
            options = o.Clone(); scale = 1;
            local = SearchLogic.SettingsAndPlaces(); results = local.Take(5).ToList(); selection = 0;
            Arrange(new Size(o.SearchPanelWidth + 48, 850), new Rectangle(16, 790, 164, 40));
            initialising = true; input.Text = "Search apps, settings, windows and files…"; initialising = false; fileStatus = "Illustrative results — no index queries run in this preview.";
            var b = new Bitmap(Width, Height); Visible = true; DrawToBitmap(b, new Rectangle(0, 0, Width, Height)); Visible = false; return b;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed) { disposed = true; debounce.Stop(); debounce.Dispose(); tips.Dispose(); if (index != null) index.Dispose(); if (icons != null) icons.Dispose(); if (ownedFont != null) ownedFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
    sealed partial class Switcher
    {
        IntegratedSearch integratedSearch;
        void ShowIntegratedSearch()
        {
            if (!options.WindowsSearchButton || pending != null || launchPlacement != null || mover.Busy) return;
            if (integratedSearch == null)
            {
                integratedSearch = new IntegratedSearch(); Controls.Add(integratedSearch);
                integratedSearch.CloseRequested = HideIntegratedSearch;
                integratedSearch.Activated = delegate(SearchItem item, bool zone)
                {
                    HideIntegratedSearch();
                    if (item.Window != null)
                    {
                        if (zone) { ChooseZone(item.Window, null); return; }
                        ActivateWindow(item.Window);
                    }
                    else
                    {
                        AppButton app = item.Taskbar ?? FavouriteLaunch.AsApp(item.Entry);
                        if (zone) ChooseZone(null, app); else QueueLaunch(app, null);
                    }
                };
            }
            if (integratedSearch.Visible) { integratedSearch.FocusInput(); return; }
            var items = new List<SearchItem>();
            if (options.SearchOpenWindows)
                foreach (var w in GetWindows())
                {
                    string path = ShellIcons.ProcessFile(w.Handle);
                    items.Add(new SearchItem { Name = w.Title, Kind = "Windows", Detail = "Open window · " + MonitorLabel(w.Handle), Window = w,
                        Entry = new FavouriteEntry { Name = w.Title, Target = path, Group = "Windows" } });
                }
            if (options.SearchFavourites) items.AddRange(FavouriteStore.Load().Where(e => e.Enabled).Select(e => SearchLogic.FromEntry(e, "Favourites", e.Detail)));
            if (options.SearchInstalledApps)
                foreach (var a in allApps) items.Add(new SearchItem { Name = a.DisplayName, Kind = "Apps", Detail = "Taskbar app · open another window", Taskbar = a });
            var system = SearchLogic.SettingsAndPlaces();
            if (options.SearchSettings) items.AddRange(system.Where(x => x.Kind == "Settings"));
            if (options.SearchFavourites) items.AddRange(system.Where(x => x.Kind == "Folders"));
            ClearThumbnails();
            integratedSearch.Open(options, items, ClientSize, quick.Search, scale); Invalidate();
        }
        void ArrangeIntegratedSearch() { if (integratedSearch != null && integratedSearch.Visible) integratedSearch.Arrange(ClientSize, quick.Search); }
        void HideIntegratedSearch()
        {
            if (integratedSearch == null || !integratedSearch.Visible) return;
            integratedSearch.HideSurface(); ActiveControl = null;
            if (Visible && !closing && !renderingPreview) UpdateThumbnails();
        }
        void DisposeIntegratedSearch() { if (integratedSearch != null) { integratedSearch.Dispose(); integratedSearch = null; } }
    }
}
