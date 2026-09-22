from pathlib import Path
import re

ROOT=Path('.')
def load(p): return (ROOT/p).read_text(encoding='utf-8-sig')
def save(p,s):
    dest=ROOT/p; dest.parent.mkdir(parents=True,exist_ok=True); dest.write_text(s,encoding='utf-8',newline='\n')
def edit(p,old,new,count=1):
    s=load(p); assert s.count(old)==count,(p,old[:100],s.count(old)); save(p,s.replace(old,new))
def span(p,start,end,new):
    s=load(p); assert s.count(start)==1,(p,start); a=s.index(start); b=s.index(end,a); save(p,s[:a]+new+s[b:])
main='src/TaskbarTiles/TaskbarTiles.cs'
assert load('version.txt').strip()=='0.8.1'
edit(main,'internal const string Version = "0.8.1";','internal const string Version = "0.9.0";')
s=load(main); s=re.sub(r'^// Taskbar Tiles [\d.]+', '// Taskbar Tiles 0.9.0',s,count=1); save(main,s)
edit('src/TaskbarTiles/AssemblyInfo.cs','0.8.1','0.9.0',3)
save('version.txt','0.9.0\n')
edit(main,'        public FavouriteEntry Favourite;','        public FavouriteEntry Favourite;\n        internal LauncherKey LauncherIdentity;')
edit(main,'        readonly IconWorker icons = new IconWorker();','        readonly IconWorker icons = new IconWorker();\n        readonly object inventoryGate = new object();\n        List<AppButton> lastInventory = new List<AppButton>();')
span(main,'        public void Read(Point monitorPoint, Action<List<AppButton>, string> completed)','        public void Launch(AppButton original',r'''        public void Read(Point monitorPoint, Action<List<AppButton>, string> completed)
        {
            if (jobs.IsAddingCompleted) return;
            jobs.Add(delegate()
            {
                var list = new List<AppButton>(); string status = "";
                try { list = Scan(monitorPoint, true); }
                catch (Exception ex) { Program.Log("Taskbar inventory: " + ex.GetType().Name); }
                if (list.Count == 0)
                {
                    try
                    {
                        List<AppButton> previous;
                        lock (inventoryGate) previous = lastInventory.Select(LauncherDescriptor.Copy).ToList();
                        list = TaskbarFallback.Read(previous);
                        status = list.Count > 0 ? "Fallback: pinned shortcuts + running apps; exact taskbar order temporarily unavailable" :
                            "Explorer has not exposed app entries yet. Refresh taskbar apps from the tray; Favourites and Search remain available.";
                    }
                    catch (Exception ex) { Program.Log("Taskbar fallback: " + ex.GetType().Name); status = "Taskbar inventory unavailable; Search and Favourites remain available."; }
                }
                icons.Resolve(list, delegate
                {
                    lock (inventoryGate) lastInventory = list.Select(LauncherDescriptor.Copy).ToList();
                    completed(list, status);
                });
            });
        }
''')
span(main,'        static List<AppButton> Scan(Point prefer, bool images)','        public void Dispose() { jobs.CompleteAdding(); icons.Dispose(); }',r'''        static List<AppButton> Scan(Point prefer, bool images)
        {
            // images=true denotes inventory. This path never uses screen positions
            // to click and must accept offscreen/virtualised auto-hidden app buttons.
            string preferredScreen = Screen.FromPoint(prefer).DeviceName;
            var roots = Roots().OrderByDescending(h => Screen.FromHandle(h).DeviceName == preferredScreen).ToList();
            foreach (IntPtr root in roots)
            {
                var result = new List<AppButton>();
                try
                {
                    IntPtr legacy = IntPtr.Zero;
                    Native.EnumChildWindows(root, delegate(IntPtr h, IntPtr p)
                    { if (Native.Class(h) == "MSTaskListWClass") legacy = h; return true; }, IntPtr.Zero);
                    var scopes = new List<IntPtr>();
                    if (legacy != IntPtr.Zero) scopes.Add(legacy);
                    if (legacy != root) scopes.Add(root);
                    foreach (IntPtr scope in scopes)
                    {
                        var condition = new OrCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                        var elements = AutomationElement.FromHandle(scope).FindAll(TreeScope.Descendants, condition);
                        var seen = new HashSet<string>();
                        for (int i = 0; i < elements.Count; i++)
                        {
                            try
                            {
                                var element = elements[i]; var a = element.Current;
                                string id = a.AutomationId ?? "", cls = a.ClassName ?? "", name = a.Name ?? "";
                                if (!TaskbarScanPolicy.KnownApp(id, cls, scope == legacy, name)) continue;
                                Rectangle bounds = TaskbarScanPolicy.Bounds(a.BoundingRectangle);
                                if (!TaskbarScanPolicy.Accept(images, a.IsOffscreen, a.IsEnabled, bounds, SystemInformation.VirtualScreen)) continue;
                                string runtime;
                                try { runtime = string.Join(".", element.GetRuntimeId().Select(n => n.ToString())); }
                                catch { runtime = id + "|" + name + "|" + i; }
                                // Different ungrouped buttons can have identical/empty
                                // rectangles while hidden. Deduplicate the UIA object only.
                                if (!seen.Add(runtime)) continue;
                                result.Add(new AppButton { Id = id, Name = name, ClassName = cls, Bounds = bounds,
                                    Taskbar = root, DisplayName = TextTools.CleanAppName(name) });
                            }
                            catch (ElementNotAvailableException) { }
                            catch (Exception ex) { Program.Log("Skipped taskbar inventory item: " + ex.GetType().Name); }
                        }
                        if (result.Count != 0) break;
                    }
                }
                catch (Exception ex) { Program.Log("Taskbar root: " + ex.GetType().Name); }
                if (result.Count > 0)
                    return result.All(a => !a.Bounds.IsEmpty) ? result.OrderBy(a => a.Bounds.Top).ThenBy(a => a.Bounds.Left).ToList() : result;
            }
            return new List<AppButton>();
        }
''')
edit(main,'try { ResolveOne(app, running); }','try { ResolveOne(app, running); app.LauncherIdentity = LauncherKey.FromApp(app, true); }')
edit(main,'            SetupOutsideDismissal(); SetupShortcutRecovery(); SetupTouchSupport();','            SetupOutsideDismissal(); SetupShortcutRecovery(); SetupTouchSupport(); SetupRecentApps();')
edit(main,'                if (touchService != null) touchService.Configure(options);','                if (touchService != null) touchService.Configure(options);\n                if (recentApps != null) recentApps.Configure(options);')
edit(main,'            DisposeShortcutRecovery();','            if (recentApps != null) recentApps.Dispose();\n            DisposeShortcutRecovery();')
edit(main,'                            if (destination != null) MoveTo(window, destination, false);','                            if (recentApps != null) recentApps.Launched(app, resolved);\n                            if (destination != null) MoveTo(window, destination, false);')
edit(main,'if (hit <= -12 && hit >= -16)','if ((hit <= -12 && hit >= -16) || hit == -18)',2)
edit(main,'Label(g, "Open another window", new Rectangle(S(164), appTop + S(1), Width - S(300), S(24)), false, muted, false);','Label(g, string.IsNullOrEmpty(taskbarStatus) ? "Open app (new or existing window as the app decides)" : taskbarStatus, new Rectangle(S(164), appTop + S(1), Width - S(300), S(24)), false, muted, false);')
edit(main,'                LauncherTests.Run(log);','                LauncherTests.Run(log);\n                LauncherExperienceTests.Run(log);')
edit(main,'            if (args.Contains("--self-test"))','            if (args.Contains("--test-launcher-experience")) { Environment.Exit(LauncherExperienceTests.RunNative()); return; }\n            if (args.Contains("--self-test"))')

quick='src/TaskbarTiles/QuickAccess.cs'
edit(quick,'internal Rectangle Search, Favourites, Desktop, Clipboard, Help;','internal Rectangle Search, Favourites, Recent, Desktop, Clipboard, Help;')
edit(quick,'o.WindowsSearchButton || o.FavouritesButton || o.DesktopButton || o.ClipboardButton','o.WindowsSearchButton || o.FavouritesButton || o.RecentAppsButton || o.DesktopButton || o.ClipboardButton')
span(quick,'        internal static QuickAccessLayout Build(','        internal IEnumerable<Rectangle> Buttons()',r'''        internal static QuickAccessLayout Build(int width, int height, float scale, Options o)
        {
            var result = new QuickAccessLayout(); if (!Enabled(o)) return result;
            Func<int, int> s = n => Math.Max(1, (int)Math.Round(n * scale));
            int pad = s(22), gap = s(8), h = s(o.FooterButtonHeight), y = height - h - s(12), left = pad, right = width - pad;
            int named = (o.FavouritesButton ? 1 : 0) + (o.RecentAppsButton ? 1 : 0);
            int small = (o.DesktopButton ? 1 : 0) + (o.ClipboardButton ? 1 : 0);
            int namedWidth = Math.Min(s(156), Math.Max(s(60), (right - left - (o.WindowsSearchButton ? s(120) : 0) - small * (s(36) + gap) - named * gap) / Math.Max(1, named)));
            if (o.FavouritesButton) { result.Favourites = new Rectangle(right - namedWidth, y, namedWidth, h); right = result.Favourites.Left - gap; }
            if (o.RecentAppsButton) { result.Recent = new Rectangle(right - namedWidth, y, namedWidth, h); right = result.Recent.Left - gap; }
            if (o.WindowsSearchButton)
            {
                int searchWidth = Math.Max(1, Math.Min(s(o.SearchButtonWidth), right - left - small * (s(36) + gap)));
                result.Search = new Rectangle(left, y, searchWidth, h); left = result.Search.Right + gap;
            }
            if (o.DesktopButton) { result.Desktop = new Rectangle(left, y, s(36), h); left = result.Desktop.Right + gap; }
            if (o.ClipboardButton) { result.Clipboard = new Rectangle(left, y, s(36), h); left = result.Clipboard.Right + gap; }
            if (right - left > s(55)) result.Help = new Rectangle(left, y, s(36), h);
            return result;
        }
''')
edit(quick,'new[] { Search, Favourites, Desktop, Clipboard, Help }','new[] { Search, Favourites, Recent, Desktop, Clipboard, Help }')
edit(quick,'            if (!Desktop.IsEmpty && Desktop.Contains(p))','            if (!Recent.IsEmpty && Recent.Contains(p)) return -18;\n            if (!Desktop.IsEmpty && Desktop.Contains(p))')
edit(quick,'            if (hit == -13) return','            if (hit == -18) return "Recent apps — up to ten recently opened apps not already in the Taskbar apps section (Ctrl+Shift+R here)";\n            if (hit == -13) return')
edit(quick,'PaintQuickButton(g, quick.Search, -12, "Search…", "search");','PaintQuickButton(g, quick.Search, -12, "Search apps, settings and files…", "search");\n            PaintQuickButton(g, quick.Recent, -18, "Recent apps", "clock");')
edit(quick,'                else if (glyph == "desktop")','                else if (glyph == "clock") { g.DrawEllipse(pen, x - d, y - d, d * 2, d * 2); g.DrawLine(pen, x, y, x, y - d * .65f); g.DrawLine(pen, x, y, x + d * .55f, y + d * .25f); }\n                else if (glyph == "desktop")')
edit(quick,'            if (hit == -13) { ShowFavourites(); return; }','            if (hit == -13) { ShowFavourites(); return; }\n            if (hit == -18) { ShowRecentApps(); return; }')
edit(quick,'            if (keys == (Keys.Control | Keys.Space) && options.FavouritesButton)','            if (keys == (Keys.Control | Keys.Shift | Keys.R) && options.RecentAppsButton) { ShowRecentApps(); return true; }\n            if (keys == (Keys.Control | Keys.Space) && options.FavouritesButton)')
edit(quick,'Ctrl+Space — Favourites\\nCtrl+Shift+S','Ctrl+Space — Favourites\\nCtrl+Shift+R — Recent apps\\nCtrl+Shift+S')

search='src/TaskbarTiles/IntegratedSearch.cs'
edit(search,'internal string Name, Kind, Detail;','internal string Name, Kind, Detail, Keywords;')
span(search,'        internal static int Score(SearchItem item, string query)','        internal static List<SearchItem> Filter(','''        internal static int Score(SearchItem item, string query)
        { return SearchMatch.Score(item, query); }
''')
s=load(search); start=s.index('            var result = new List<SearchItem>();',s.index('internal static List<SearchItem> SettingsAndPlaces()')); end=s.index('            foreach (var folder in',start); s=s[:start]+'            var result = WindowsSettingsCatalog.Read();\n'+s[end:]; save(search,s)
edit(search,'Search apps, windows, files…','Search apps, settings, windows and files…',2)

fav='src/TaskbarTiles/FavouritesUI.cs'
edit(fav,'        bool ready, layingOut;','        bool ready, layingOut;\n        readonly string paletteTitle, emptyHint;\n        readonly Func<List<FavouriteEntry>> liveEntries;\n        readonly System.Windows.Forms.Timer liveTimer = new System.Windows.Forms.Timer { Interval = 600 };')
edit(fav,'internal FavouritesWindow(Options current, List<FavouriteEntry> favourites, Rectangle anchor)','internal FavouritesWindow(Options current, List<FavouriteEntry> favourites, Rectangle anchor, string caption = "Favourites", string emptyMessage = null, Func<List<FavouriteEntry>> refreshEntries = null)')
edit(fav,'            options = current.Clone(); entries = favourites.Select(e => e.Clone()).ToList();','            paletteTitle = caption; emptyHint = emptyMessage; liveEntries = refreshEntries;\n            options = current.Clone(); entries = favourites.Select(e => e.Clone()).ToList();\n            liveTimer.Tick += delegate { RefreshLiveEntries(); };\n            Shown += delegate { if (liveEntries != null) liveTimer.Start(); };\n            FormClosed += delegate { liveTimer.Stop(); };')
edit(fav,'Text = "Taskbar Tiles - Favourites";','Text = "Taskbar Tiles - " + paletteTitle;')
edit(fav,'manage = Theme.Button("Manage", 92);','manage = Theme.Button(paletteTitle == "Favourites" ? "Manage" : "Settings", 92);')
edit(fav,'WindowNative.SendMessage(filter.Handle, 0x1501, IntPtr.Zero, "Find a favourite...");','WindowNative.SendMessage(filter.Handle, 0x1501, IntPtr.Zero, paletteTitle == "Favourites" ? "Find a favourite..." : "Find a recent app...");')
edit(fav,'TextRenderer.DrawText(g, "Favourites", font,','TextRenderer.DrawText(g, paletteTitle, font,')
edit(fav,'entries.Any(x => x.Enabled) ? "No matching favourites." : "Add your apps with Manage.\\nYou can include folders and websites too."','entries.Any(x => x.Enabled) ? "No matching entries." : emptyHint ?? "Add your apps with Manage.\\nYou can include folders and websites too."')
needle='        int S(int n) { return Math.Max(1, (int)Math.Round(n * scale)); }'
assert load(fav).count(needle)==1
edit(fav,needle,r'''        void RefreshLiveEntries()
        {
            if (liveEntries == null || IsDisposed) return;
            var next = liveEntries();
            Func<FavouriteEntry, string> signature = e => e.Id + "|" + e.Target + "|" + e.Arguments + "|" + e.Name;
            if (entries.Select(signature).SequenceEqual(next.Select(signature))) return;
            string keep = selected >= 0 && selected < visible.Count ? visible[selected].Id : "";
            entries.Clear(); entries.AddRange(next.Select(e => e.Clone())); ApplyFilter();
            int retained = visible.FindIndex(e => e.Id == keep); if (retained >= 0) selected = retained; Arrange();
        }
''' +needle)
edit(fav,'            if (keyData == (Keys.Control | Keys.F))','            if (key == Keys.F5 && liveEntries != null) { RefreshLiveEntries(); return true; }\n            if (keyData == (Keys.Control | Keys.F))')
edit(fav,'{ if (disposing) { if (icons != null) icons.Dispose(); tips.Dispose(); } base.Dispose(disposing); }','{ if (disposing) { liveTimer.Stop(); liveTimer.Dispose(); if (icons != null) icons.Dispose(); tips.Dispose(); } base.Dispose(disposing); }')

settings='src/TaskbarTiles/Settings.cs'
edit(settings,'        public int SearchPanelWidth = 660;','        public int SearchPanelWidth = 860;\n        public int SearchButtonWidth = 420;\n        public bool RecentAppsButton = true;\n        public bool RememberRecentApps = true;\n        public bool RecentObserveExternal = true;\n        public int RecentAppsLimit = 10;')
edit(settings,'{ "SearchPanelWidth", new[] { 420, 1100 } }','{ "SearchButtonWidth", new[] { 200, 900 } }, { "RecentAppsLimit", new[] { 1, 10 } },\n            { "SearchPanelWidth", new[] { 420, 1400 } }')
edit(settings,'            AddSearchSettingsPage();','            AddSearchSettingsPage();\n            AddRecentAppsPage();')
help='src/TaskbarTiles/SettingsHelp.cs'
edit(help,'            { "FavouritesButton",','            { "SearchButtonWidth", "Preferred width of the bottom-left Search box in logical pixels. Default 420. It becomes narrower only when needed to keep Recent apps, Favourites and other controls inside the menu." },\n            { "RecentAppsButton", "Show the local Recent apps launcher beside Favourites. The full Taskbar apps section, including other pages, is excluded before selecting up to ten entries." },\n            { "RememberRecentApps", "Remember actual app openings locally from now on, not just window switches or unverified launch attempts. Turning this off stops new history; Clear recent history deletes saved entries." },\n            { "RecentObserveExternal", "Also notice new eligible app windows outside Taskbar Tiles while it runs. Existing windows at startup are not backfilled. Document titles, process command lines and browser history are not recorded." },\n            { "RecentAppsLimit", "Show one to ten recent applications after excluding taskbar duplicates. Up to 64 local app entries are retained so the list can still offer ten useful alternatives." },\n            { "FavouritesButton",')
edit(help,'Include a curated set of common Windows Settings pages, such as Display, Sound, Bluetooth and Windows Update. This is not the complete Windows Settings search catalogue.','Find Windows Settings pages by title or common phrases such as display settings, screen resolution, refresh rate, mouse speed and auto hide taskbar. Uses a documented deep-link catalogue with limited typo tolerance, not Windows Search private ranking, Bing or every Settings subpage.')
edit(help,'            Check(p, "SearchSettings", "Search common Windows Settings pages");','            Check(p, "SearchSettings", "Search Windows Settings pages and common setting names");')
edit(help,'            Number(p, "SearchPanelWidth", "Search panel width", "Preferred logical pixels. Limited by the main menu\'s current width.", 420, 1100, 20);','            Number(p, "SearchButtonWidth", "Bottom-left search box width", "Preferred logical pixels; default 420. Fits alongside Recent apps and Favourites, shrinking only on narrow menus.", 200, 900, 20);\n            Number(p, "SearchPanelWidth", "Search panel width", "Preferred logical pixels. Limited by the main menu\'s current width.", 420, 1400, 20);')
edit(help,'page.Text == "Favourites" ?','page.Text == "Recent apps" ? "Local recent-app collection, exclusions, list size and Clear history." :\n                    page.Text == "Favourites" ?')
edit('src/TaskbarTiles/SettingsExtras.cs','Favourites remain at the bottom-right.','Recent apps and Favourites remain at the bottom-right.')
edit('src/TaskbarTiles/LauncherTests.cs','new Options { FooterButtonHeight=height, WindowsSearchButton=','new Options { RecentAppsButton=false, FooterButtonHeight=height, WindowsSearchButton=')

project='src/TaskbarTiles/TaskbarTiles.csproj'
edit(project,'    <Compile Include="QuickAccess.cs" />','    <Compile Include="QuickAccess.cs" />\n    <Compile Include="LauncherInventory.cs" />\n    <Compile Include="RecentApps.cs" />\n    <Compile Include="WindowsSettingsCatalog.cs" />\n    <Compile Include="LauncherExperienceTests.cs" />')
config=load('config/settings.ini'); config=config.replace('SearchPanelWidth=660','SearchPanelWidth=860'); config+='\n# Auto-hide discovery is automatic and does not alter the Windows taskbar.\nSearchButtonWidth=420\nRecentAppsButton=true\nRememberRecentApps=true\nRecentObserveExternal=true\nRecentAppsLimit=10\n'; save('config/settings.ini',config)
notes='''## Taskbar Tiles 0.9.0

### Auto-hidden taskbar apps

The old inventory incorrectly applied click-target visibility rules to discovery. Auto-hidden app buttons can be offscreen or have empty rectangles, so they disappeared from the launcher. Inventory now keeps known app identities independently of those rectangles. Actual fallback clicks still require fresh visible, unobstructed taskbar geometry; no click is sent into a hidden bar.

When Explorer temporarily exposes no app entries, use current pinned shortcuts and safely relaunchable running apps, deduplicated and in the last known order where corroborated. The header labels this fallback: the shortcut folder does not guarantee every packaged pin or the exact Windows order. Refresh resynchronises when Explorer exposes its real buttons. No taskbar setting, registry value, cursor position or Windhawk configuration is changed, and the taskbar is not forced open.

### Wider integrated search

The bottom-left box now defaults to 420 logical pixels and says Search apps, settings and files. Settings > Search controls the box width separately from the results panel. Both remain inside Taskbar Tiles and the footer fits around Recent apps and Favourites.

Search now includes an expanded catalogue of documented Windows Settings links, common phrases and limited spelling tolerance. Examples: display settings, screen resolution, refresh rate, mouse speed, auto hide taskbar, default browser, microphone permissions and check for updates. Results include icons, names and page breadcrumbs; selecting one opens that settings page. The old top filter remains a windows/taskbar-only filter.

This is a Windows-style local search, not an embedded copy of Windows Search, Bing, Copilot or its private ranking/index. Existing app, favourite, open-window and indexed-filename sources remain available. Some settings pages depend on the Windows version, edition or hardware and can open a parent page. Existing source preferences and custom sizes are preserved.

### Recent apps next to Favourites

Recent apps shows up to ten most recently opened applications, excluding the entire Taskbar apps inventory, including its other pages. Repeated openings move the same app forward instead of adding duplicates. Browser profile identities are not merged just because their executable is the same. The palette refreshes when taskbar inventory changes and supports icons/names, keyboard navigation, right-click monitor/zone placement and Ctrl+Shift+R while Taskbar Tiles has focus.

History begins after this update. Verified app launches through Taskbar Tiles are recorded; an optional background observer also notices new eligible app windows opened elsewhere while the utility runs. It does not backfill old Windows usage records or count switching to an already-open window as a fresh opening. Websites, documents and folders are not app-history entries. Recent launch descriptors can include local shortcut paths and explicit launch arguments; window/document titles, typed search text, browser history and process command lines are not collected.

Settings > Recent apps controls the button, collection, outside-app observation and one-to-ten display limit, and provides Clear recent history. Up to 64 local entries are retained so exclusions can leave ten useful choices. Clear is explicit and immediate, including cancellation of older pending writes. Disabling collection stops new records but retains existing history until cleared. No history is uploaded.

### Update and validation

Settings > Updates > Check for updates > Download & install. Do not uninstall first. Existing favourites, startup, X-Mouse command, sizes and touch settings are preserved. No intermediate release is required.

Release gates run Windows compilation, helper regressions, real-menu rendering/recovery, native click-away/topmost, launch fixtures, touch/shortcut tests, verified updater HTTPS/download and installer lifecycle checks. New tests exercise hidden-inventory-versus-click policies, profile-aware exclusion, search queries, footer layout, local recent-history persistence/clear and actual Settings/Recent/Search UI painting. The reporter's personalised Explorer/Windhawk desktop and physical touch hardware are not reproduced by CI. The installer is unsigned.
'''
save('docs/releases/v0.9.0.md',notes)
changelog=load('CHANGELOG.md'); assert changelog.startswith('# Changelog\n\n'); save('CHANGELOG.md','# Changelog\n\n## 0.9.0\n\n- Discover taskbar apps while auto-hidden; separate inventory from visible click targets and label the pinned/running fallback.\n- Widen integrated Search; add documented Windows Settings deep links, query aliases and limited typo tolerance.\n- Add a local Recent apps palette beside Favourites, capped at ten after profile-aware taskbar exclusion; collection controls and immediate Clear history.\n- Preserve previous launcher/activation/rendering/touch/updater safeguards and add regression coverage.\n\n'+changelog[len('# Changelog\n\n'):])
readme=load('README.md'); marker='## Privacy and control'; assert readme.count(marker)==1
readme=readme.replace(marker,'''## Auto-hide, broader search and Recent apps

Taskbar inventory includes known app buttons even while the real taskbar is auto-hidden. If Explorer temporarily exposes no buttons, a clearly labelled fallback combines current pinned shortcuts with safely relaunchable running apps; exact pin coverage/order is not guaranteed in that fallback. No taskbar setting is changed and no hidden coordinate is clicked.

The wider bottom-left **Search apps, settings and files** box searches inside Taskbar Tiles. Try **display settings**, **screen resolution**, **refresh rate**, **mouse speed**, **auto hide taskbar**, **default browser** or **microphone permissions**. This uses documented Settings deep links and aliases, not a private Windows Search embedding or web search. Configure box/results widths and source switches in **Settings > Search**. The narrow top field still filters only windows/taskbar apps.

**Recent apps**, beside Favourites, offers up to ten recently opened applications after excluding all Taskbar apps, across pages. Collection starts when this version runs; verified launcher results and optionally newly observed outside-app windows are included, not old Windows usage records or simple focus switches. **Settings > Recent apps** controls local collection and offers **Clear recent history**. Up to 64 local launch descriptors are retained; explicit shortcut paths/arguments can be stored, but window/document titles, queries, browser history and process command lines are not. Nothing is uploaded. Use Ctrl+Shift+R while the menu has focus or right-click an entry for placement.

'''+marker)
save('README.md',readme)
print('0.9.0 source integration completed; no production branch or installation was changed. Windows tests must pass before commit/publication.')
