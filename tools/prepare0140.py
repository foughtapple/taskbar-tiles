# One-time, branch-scoped source edit. Removed before the review commit is pushed.
from pathlib import Path
import re
ROOT = Path(__file__).resolve().parents[1]
def edit(path, old, new, count=1):
    p=ROOT/path; s=p.read_text(encoding='utf-8-sig')
    n=s.count(old)
    if n!=count: raise RuntimeError(f'{path}: expected {count} anchors, found {n}: {old[:100]}')
    p.write_text(s.replace(old,new),encoding='utf-8')
def region(path,start,end,new):
    p=ROOT/path; s=p.read_text(encoding='utf-8-sig'); a=s.index(start); b=s.index(end,a)
    p.write_text(s[:a]+new+s[b:],encoding='utf-8')
base='src/TaskbarTiles/'
# Configuration remains schema-compatible; new fields have defaults without resetting old preferences.
edit(base+'Settings.cs','        public int AppRows = 2;', '''        public int AppRows = 2;
        public int WindowSortMode = 0;
        public bool ShowPriorityButtons = true;
        public string WindowPrioritySlots = "";
        public bool DisplaySettingsButton = true;
        public bool BluetoothSettingsButton = true;
        public bool TaskManagerButton = true;''')
edit(base+'Settings.cs','            { "NotificationIconSize", new[] { 16, 48 } },','            { "WindowSortMode", new[] { 0, 2 } },\n            { "NotificationIconSize", new[] { 16, 48 } },')
edit(base+'Settings.cs','            AttachSettingsNavigation(content);','            AttachSettingsWorkspace(content);')
edit(base+'Settings.cs','            AddTouchSupportPage(); AddShortcutRecoveryPage();','            AddTouchSupportPage(); AddShortcutRecoveryPage(); AddWindowOrganisationPage();')
edit(base+'Settings.cs','            loading = false; UpdatePageControls(); QueuePreview();','            loading = false; PopulatePriorityList(-1); UpdatePageControls(); QueuePreview();')
# Give all three surfaces explicit, non-overlapping rectangles.
region(base+'SettingsExtras.cs','            content.SizeChanged += delegate\n','            previewTimer.Tick +=', '''            content.SizeChanged += delegate { ArrangeSettingsWorkspace(); QueuePreview(); };
''')
edit(base+'SettingsNavigation.cs','new Group("GENERAL", "Appearance", "Navigation", "Quick access"),','new Group("GENERAL", "Appearance", "Navigation", "Window organisation", "Quick access"),')
edit(base+'SettingsNavigation.cs','                UseVisualStyleBackColor = false, TabStop = true','                UseVisualStyleBackColor = false, UseMnemonic = false, TabStop = true')
edit(base+'SettingsNavigation.cs','                Text = text, AutoSize = false, Height = 24, Width = 145,','                Text = text, UseMnemonic = false, AutoSize = false, Height = 24, Width = 145,')
edit(base+'SettingsNavigation.cs','        protected override void OnDrawItem(DrawItemEventArgs e)', '''        // Suppress the native tab border/header in the page display rectangle.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x1328 && m.LParam != IntPtr.Zero)
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(new Native.RECT(ClientRectangle), m.LParam, false);
                m.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref m);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)''')
# Existing navigation tests must cover the added page and actual containment.
edit(base+'SettingsNavigationTests.cs','"Appearance","Navigation","Screens & zones"','"Appearance","Navigation","Window organisation","Screens & zones"')
edit(base+'SettingsNavigationTests.cs','ordered.Take(3).SequenceEqual(new[] { "Appearance","Navigation","Quick access" })','ordered.Take(3).SequenceEqual(new[] { "Appearance","Navigation","Window organisation" })')
edit(base+'SettingsNavigationTests.cs','Count() == 13','Count() == 14')
edit(base+'SettingsNavigationTests.cs','                    using (var image = new Bitmap(form.Width, form.Height))','                    form.AssertSettingsWorkspace();\n                    using (var image = new Bitmap(form.Width, form.Height))')
# Correct the app-priority insertion policy: preserve gaps and never silently lose slot 50.
region(base+'WindowOrganisation.cs','            // Moving an assigned app','            return slots.ToArray();', '''            if (old >= 0) slots[old] = null;
            if (slots[target] == null) slots[target] = app.Clone();
            else
            {
                int gap = slots.FindIndex(target, a => a == null);
                if (gap >= 0)
                {
                    for (int i = gap; i > target; i--) slots[i] = slots[i - 1];
                    slots[target] = app.Clone();
                }
                else if (old >= 0 && old < target)
                {
                    for (int i = old; i < target; i++) slots[i] = slots[i + 1];
                    slots[target] = app.Clone();
                }
                else throw new InvalidOperationException("There is no free priority slot at or below " + number + ". Remove or move an entry first; no priority has been dropped.");
            }
''')
edit(base+'WindowOrganisation.cs','Color.FromArgb(37, sixty(), 83)','Color.FromArgb(37, 60, 83)')
edit(base+'WindowOrganisation.cs','        static int sixty() { return 60; }\n','')
edit(base+'WindowOrganisation.cs','window.PriorityName = string.IsNullOrWhiteSpace(name) ? "Application" : name;','window.PriorityName = string.IsNullOrWhiteSpace(name) ? "Application" : name.Substring(0, Math.Min(256,name.Length));')
edit(base+'OrganisationTests.cs','Require(NotificationRoots.IsChevron("OverflowChevron","","",false)==false,"placeholder");','Require(NotificationRoots.IsChevron("OverflowChevron","",""),"overflow chevron is not an application icon");')
edit(base+'OrganisationTests.cs','WindowPriorityModel.Rank(slots,"steam")>0','WindowPriorityModel.Rank(slots,"steam")==50')
# Window identity is resolved during enumeration, never in a paint/hook callback.
edit(base+'TaskbarTiles.cs','        public uint ProcessId;\n    }\n\n    sealed partial class Switcher','        public uint ProcessId;\n        public string PriorityKey = "", PriorityName = "";\n    }\n\n    sealed partial class Switcher')
old='                list.Add(new WindowItem { Handle = h, ProcessId = WindowNative.ProcessId(h), Title = actualTitle.Length > 0 ? actualTitle.ToString() : title.ToString() });'
new='''                var item = new WindowItem { Handle = h, ProcessId = WindowNative.ProcessId(h), Title = actualTitle.Length > 0 ? actualTitle.ToString() : title.ToString() };
                WindowPriorityIdentity.Populate(item); list.Add(item);'''
edit(base+'TaskbarTiles.cs',old,new)
edit(base+'Navigation.cs','''            windows = allWindows.Where(w => MatchesQuery(w.Title) && (!options.CurrentMonitorOnly || Screen.FromHandle(w.Handle).DeviceName == Screen.FromPoint(monitorPoint).DeviceName)).ToList();''','''            IntPtr previous = selected >= 0 && selected < windows.Count ? windows[selected].Handle : IntPtr.Zero;
            windows = OrganiseWindows(allWindows.Where(w => MatchesQuery(w.Title) && (!options.CurrentMonitorOnly || Screen.FromHandle(w.Handle).DeviceName == Screen.FromPoint(monitorPoint).DeviceName)));
            if (!reset && previous != IntPtr.Zero) { int keep = windows.FindIndex(w => w.Handle == previous); if (keep >= 0) selected = keep; }''')
edit(base+'TaskbarTiles.cs','            selected = windows.Count > 1 ? (reverse ? windows.Count - 1 : 1) : 0;','''            selected = windows.Count > 1 ? (reverse ? windows.Count - 1 : options.WindowSortMode == 0 ? 1 : 0) : 0;
            if (options.WindowSortMode != 0 && windows.Count > 1 && windows[selected].Handle == foregroundBeforeOpen) selected = reverse ? windows.Count - 2 : 1;''')
edit(base+'QuickAccess.cs','                selected = windowPage = appPage = 0; LayoutMenu();','                windows = OrganiseWindows(windows); selected = windowPage = appPage = 0; LayoutMenu();')
# Each card has a separately hit-tested P button, before its app icon/title.
edit(base+'WindowHeaderIcons.cs','        internal Rectangle Icon, Title, Close;','        internal Rectangle Icon, Title, Close, Priority;')
edit(base+'WindowHeaderIcons.cs','''            if (o.ShowWindowTitleIcons)
            {''','''            if (o.ShowPriorityButtons)
            {
                g.Priority = new Rectangle(card.Left + s(6), card.Top + (band - s(24)) / 2, s(34), s(24));
                left = g.Priority.Right + s(6);
            }
            if (o.ShowWindowTitleIcons)
            {''')
edit(base+'TaskbarTiles.cs','            pageInfoRect = new Rectangle(S(204), S(14), Math.Max(1, winPrev.Left - S(280)), S(28));','            ArrangeOrganisation();')
edit(base+'TaskbarTiles.cs','            PaintAction(g, closeRect, -1, "close"); PaintAction(g, gearRect, -8, "gear");','            PaintOrganisation(g);\n            PaintAction(g, closeRect, -1, "close"); PaintAction(g, gearRect, -8, "gear");')
edit(base+'TaskbarTiles.cs','                var header = WindowHeaderGeometry.Build(r, options, scale);','                var header = WindowHeaderGeometry.Build(r, options, scale);\n                PaintPriority(g, header, windows[index], i);')
edit(base+'TaskbarTiles.cs','''        int Hit(Point p)
        {
            if (pageInfoRect.Contains(p)) return -17;''','''        int Hit(Point p)
        {
            if (organisationRect.Contains(p)) return -21;
            int priority = HitPriority(p); if (priority != -100) return priority;
            if (pageInfoRect.Contains(p)) return -17;''')
edit(base+'TaskbarTiles.cs','            else if (hit >= 3000) text = NotificationTip(hit);','''            else if (hit >= 4000) text = "Set this app's priority (1-50). Ten choices are visible at a time; scroll for more.";
            else if (hit == -21) text = "Cycle active-window order: Recent, A-Z, Priority. The last mode is saved. Edit the priority list in Settings > Window organisation.";
            else if (hit == -22) text = NotificationTip(hit);
            else if (hit >= 3000) text = NotificationTip(hit);''')
edit(base+'TaskbarTiles.cs','''            if (e.Button == MouseButtons.Right)
            {
                if (hit >= 3000)''','''            if (e.Button == MouseButtons.Right)
            {
                if (hit >= 4000) { ChooseWindowPriority(hit - 4000); return; }
                if (hit == -21) { Dismiss(); SettingsCore("Window organisation"); return; }
                if (hit >= 3000)''')
edit(base+'TaskbarTiles.cs','''            if (e.Button != MouseButtons.Left) return;
            if (hit >= 3000)''','''            if (e.Button != MouseButtons.Left) return;
            if (hit >= 4000) { ChooseWindowPriority(hit - 4000); return; }
            if (hit == -21) { CycleOrganisation(); return; }
            if (hit == -22) { LoadWindowsTray(); return; }
            if (hit >= 3000)''')
edit(base+'TaskbarTiles.cs','''                int pages = Math.Max(1, (notificationItems.Count + notificationPerPage - 1) / notificationPerPage);
                notificationPage = (notificationPage + (hit == -19 ? -1 : 1) + pages) % pages; LayoutMenu(); return;''','''                PageNotifications(hit == -19 ? -1 : 1); return;''')
edit(base+'TaskbarTiles.cs','(hit <= -12 && hit >= -16) || hit == -18','QuickAccessLayout.IsHit(hit)',2)
edit(base+'TaskbarTiles.cs','if (code == Keys.F5) { RefreshMenuGraphics(); RefreshWindows(); RefreshApps(); return true; }','if (code == Keys.F5) { RefreshMenuGraphics(); RefreshWindows(); RefreshApps(); RefreshNotificationArea(); return true; }')
# Replace the footer geometry; existing launch and keyboard code remains intact.
region(base+'QuickAccess.cs','    sealed class QuickAccessLayout\n','    sealed partial class Switcher\n','')
edit(base+'QuickAccess.cs','''            if (hit == -12) return "Search here''','''            if (hit == -30) return "Open Windows Display settings";
            if (hit == -31) return "Open Windows Bluetooth settings";
            if (hit == -32) return "Open Task Manager";
            if (hit == -12) return "Search here''')
edit(base+'QuickAccess.cs','            PaintQuickButton(g, quick.Help, -16, "", "help");','''            PaintQuickButton(g, quick.Help, -16, "", "help");
            PaintQuickButton(g, quick.Display, -30, quick.Display.Width >= S(110) ? "Display settings" : "", "desktop");
            PaintQuickButton(g, quick.Bluetooth, -31, quick.Bluetooth.Width >= S(110) ? "Bluetooth" : "", "bluetooth");
            PaintQuickButton(g, quick.TaskManager, -32, quick.TaskManager.Width >= S(110) ? "Task Manager" : "", "tasks");''')
edit(base+'QuickAccess.cs','''                else if (glyph == "clipboard")''','''                else if (glyph == "bluetooth")
                { g.DrawLines(pen, new[]{new PointF(x,y-d),new PointF(x+d*.65f,y-d*.4f),new PointF(x-d*.65f,y+d*.5f)}); g.DrawLines(pen,new[]{new PointF(x,y+d),new PointF(x+d*.65f,y+d*.4f),new PointF(x-d*.65f,y-d*.5f)});g.DrawLine(pen,x,y-d,x,y+d); }
                else if (glyph == "tasks")
                {g.DrawRectangle(pen,x-d,y-d,d*2,d*2);g.DrawLines(pen,new[]{new PointF(x-d*.8f,y+d*.3f),new PointF(x-d*.2f,y),new PointF(x,y-d*.65f),new PointF(x+d*.25f,y+d*.5f),new PointF(x+d*.75f,y-d*.2f)});}
                else if (glyph == "clipboard")''')
edit(base+'QuickAccess.cs','''            if (hit == -12) { ShowIntegratedSearch(); return; }''','''            if (hit <= -30 && hit >= -32)
            { Dismiss(); try { SystemQuickActions.Open(hit); } catch (Exception ex) { Notify(ex.Message); } return; }
            if (hit == -12) { ShowIntegratedSearch(); return; }''')
edit(base+'SettingsExtras.cs','            Check(p, "WindowsSearchButton", "Show integrated Search on the bottom-left");','''            Check(p, "WindowsSearchButton", "Show integrated Search on the bottom-left");
            Check(p, "DisplaySettingsButton", "Show Display settings on the bottom bar");
            Check(p, "BluetoothSettingsButton", "Show Bluetooth settings on the bottom bar");
            Check(p, "TaskManagerButton", "Show Task Manager on the bottom bar");''')
# Discover the actual Windows 11 hidden-icons host and distinguish system controls.
p=ROOT/(base+'NotificationArea.cs'); s=p.read_text(encoding='utf-8'); marker='    sealed partial class Switcher\n'; a=s.index(marker); p.write_text(s[:a]+'}\n',encoding='utf-8')
edit(base+'NotificationArea.cs','        internal bool Offscreen, Enabled;','        internal bool Offscreen, Enabled, SystemItem;')
edit(base+'NotificationArea.cs','''            if((name??"").StartsWith("Taskbar Tiles",StringComparison.OrdinalIgnoreCase)) return true;''','''            if(NotificationRoots.IsChevron(id,cls,name)) return true;''')
edit(base+'NotificationArea.cs','''                if(c.ControlType!=ControlType.Button && c.ControlType!=ControlType.ListItem && c.ControlType!=ControlType.MenuItem) return false;''','''                if(c.ControlType!=ControlType.Button && c.ControlType!=ControlType.ListItem && c.ControlType!=ControlType.MenuItem &&
                    !(c.ControlType==ControlType.Custom && (c.ClassName??"").EndsWith("IconView",StringComparison.Ordinal))) return false;''')
edit(base+'NotificationArea.cs','''                else if(cls=="NotifyIconOverflowWindow") overflow.Add(h);''','''                else if(NotificationRoots.IsOverflowClass(cls))
                {
                    overflow.Add(h);
                    IntPtr bridge=NotificationRoots.FindWindowEx(h,IntPtr.Zero,"Windows.UI.Composition.DesktopWindowContentBridge",null);
                    if(bridge!=IntPtr.Zero)overflow.Add(bridge);
                }''')
edit(base+'NotificationArea.cs','bool overflow=Native.Class(rootHandle)=="NotifyIconOverflowWindow";','bool overflow=NotificationRoots.IsOverflow(rootHandle);',2)
edit(base+'NotificationArea.cs','new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem));','new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem),\n                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Custom));',2)
edit(base+'NotificationArea.cs','Bitmap artwork=TrayArtwork.Capture(bounds,c.IsOffscreen);','Bitmap artwork=!c.IsOffscreen && NotificationRoots.Uncovered(rootHandle,TrayArtwork.InnerBounds(bounds)) ? TrayArtwork.Capture(bounds,false) : null;')
edit(base+'NotificationArea.cs','Offscreen=c.IsOffscreen,Enabled=c.IsEnabled,Image=artwork','Offscreen=c.IsOffscreen,Enabled=c.IsEnabled,Image=artwork,SystemItem=NotificationRoots.IsSystem(c.AutomationId,c.ClassName,c.Name,overflow)')
# Cache actual observed artwork in memory only. Never screenshot a covering app.
edit(base+'NotificationArea.cs','        List<FavouriteEntry> catalog;','        readonly Dictionary<string,Bitmap> artworkCache=new Dictionary<string,Bitmap>(StringComparer.Ordinal);\n        List<FavouriteEntry> catalog;')
edit(base+'NotificationArea.cs','                foreach(var image in cache.Values) if(image!=null) image.Dispose();','                foreach(var image in cache.Values) if(image!=null) image.Dispose();\n                foreach(var image in artworkCache.Values) if(image!=null) image.Dispose();')
edit(base+'NotificationArea.cs','''                            if(item.Image!=null) continue; // Prefer artwork captured from the actual Windows tray item.
                            int discordCount;
                            if(DiscordNotificationVisual.TryCount(item.Name,out discordCount)) continue;''','''                            Bitmap previous;
                            if(item.Image!=null)
                            {
                                if(artworkCache.TryGetValue(item.Key,out previous)){previous.Dispose();artworkCache.Remove(item.Key);}
                                if(artworkCache.Count>=256){string first=artworkCache.Keys.First();artworkCache[first].Dispose();artworkCache.Remove(first);}
                                artworkCache[item.Key]=(Bitmap)item.Image.Clone();continue;
                            }
                            if(artworkCache.TryGetValue(item.Key,out previous)){item.Image=(Bitmap)previous.Clone();continue;}''')
# Keying out a corner colour could erase legitimate black/white icon pixels.
region(base+'NotificationArea.cs','                // The taskbar button background','                return image;', '')
# All system icons fit at the right, with smaller targets only when the monitor is very narrow.
region(base+'NotificationStrip.cs','            int right = width-pad','            int reserved =', '''            int right=width-pad, systemCount=system;
            int systemGap=gap, systemCell=cell;
            if(systemCount>0 && systemCount*cell+(systemCount-1)*gap>width/3)
            {
                systemGap=Math.Max(1,Math.Min(gap,width/Math.Max(1,systemCount*12)));
                systemCell=Math.Max(1,(width/3-(systemCount-1)*systemGap)/systemCount);
            }
            int systemWidth=systemCount==0?0:systemCount*systemCell+(systemCount-1)*systemGap;
            for(int i=0;i<systemCount;i++)g.System.Add(new Rectangle(right-systemWidth+i*(systemCell+systemGap),top,systemCell,cell));
''')
edit(base+'NotificationStrip.cs','            int arrows = paged ? 2*(cell+gap) : 0;','            int arrow=Math.Max(8,(int)Math.Round(16*dpi)), arrowGap=Math.Max(1,(int)Math.Round(4*dpi));\n            int arrows = paged ? 2*(arrow+arrowGap) : 0;')
edit(base+'NotificationStrip.cs','g.Previous=new Rectangle(Math.Max(centreLeft,x-cell-gap),top,cell,cell);','g.Previous=new Rectangle(Math.Max(centreLeft,x-arrow-arrowGap),top,arrow,cell);')
edit(base+'NotificationStrip.cs','g.Next=new Rectangle(x+rowWidth+gap,top,cell,cell);','g.Next=new Rectangle(x+rowWidth+arrowGap,top,arrow,cell);')
edit(base+'NotificationStrip.cs','                int size=S(options.NotificationIconSize);','                int size=Math.Max(1,Math.Min(S(options.NotificationIconSize),Math.Min(r.Width,r.Height)-S(4)));')
# Release tests, version and documentation.
edit(base+'TaskbarTiles.cs','internal const string Version = "0.13.0";','internal const string Version = "0.14.0";')
edit(base+'TaskbarTiles.cs','            if (args.Contains("--test-settings-navigation"))','            if (args.Contains("--test-navigation-update")) { Environment.Exit(OrganisationTests.RunNative()); return; }\n            if (args.Contains("--test-settings-navigation"))')
edit(base+'TaskbarTiles.cs','                SettingsNavigationTests.Run(log);','                SettingsNavigationTests.Run(log);\n                OrganisationTests.Run(log);')
edit(base+'AssemblyInfo.cs','0.13.0','0.14.0',3)
(ROOT/'version.txt').write_text('0.14.0\n',encoding='utf-8')
for path in ['.github/workflows/build.yml','.github/workflows/release.yml']:
    edit(path,'''      - name: Test dark Settings navigation
        shell: powershell
        run: ./tools/Test-SettingsNavigation.ps1''','''      - name: Test dark Settings navigation
        shell: powershell
        run: ./tools/Test-SettingsNavigation.ps1
      - name: Test Settings containment, sorting, priorities and tray groups
        shell: powershell
        run: ./tools/Test-Organisation.ps1''')
# All new C# sources are explicitly included for MSBuild as well as CodeDOM's glob.
project=ROOT/(base+'TaskbarTiles.csproj'); text=project.read_text(encoding='utf-8')
for name in ['SettingsWorkspace.cs','WindowOrganisation.cs','NotificationStrip.cs','SystemQuickActions.cs','OrganisationTests.cs']:
    assert ('Include="'+name+'"') not in text
    text=text.replace('    <Compile Include="TaskbarTiles.cs" />','    <Compile Include="'+name+'" />\n    <Compile Include="TaskbarTiles.cs" />')
project.write_text(text,encoding='utf-8')
(ROOT/'config/settings.ini').write_text((ROOT/'config/settings.ini').read_text(encoding='utf-8')+'\n# Window organisation and bottom system shortcuts\nWindowSortMode=0\nShowPriorityButtons=true\nWindowPrioritySlots=\nDisplaySettingsButton=true\nBluetoothSettingsButton=true\nTaskManagerButton=true\n',encoding='utf-8')
changelog=ROOT/'CHANGELOG.md'; text=changelog.read_text(encoding='utf-8'); assert text.startswith('# Changelog\n')
entry='''\n## 0.14.0\n\n- Fix the Settings editor being covered by its sidebar: navigation, editor and live preview now have separate measured rectangles.\n- Read the Windows 11 hidden-icons host and separate application tray icons (centre) from taskbar system controls (far right).\n- Cache only visible, unobstructed tray artwork in memory; never capture a window covering the taskbar.\n- Add Display settings, Bluetooth settings and Task Manager to the bottom bar, with individual switches.\n- Add persistent Recent / A-Z / Priority ordering for active windows, P buttons, a 50-slot app-priority picker showing ten rows, and a draft-aware Settings editor.\n- Preserve existing installer, Stream Dock, touch, launch, rendering and updater configuration.\n'''
changelog.write_text('# Changelog\n'+entry+text[len('# Changelog\n'):],encoding='utf-8')
readme=ROOT/'README.md'; text=readme.read_text(encoding='utf-8'); text+='''\n## Active-window organisation (0.14.0)\n\nThe main **Order** button cycles **Recent**, **A-Z** and **Priority** and saves the mode. Recent preserves Windows switching order; A-Z sorts window titles. The **P** at the top-left of a window card assigns its application a saved priority from 1 to 50. The picker shows ten rows and scrolls through all fifty. Each occupied slot shows its app name; inserting into an occupied slot shifts entries without dropping an application. Unassigned apps follow assigned apps, and multiple windows of one app retain their recent order. Manage the draft list under **Settings > Window organisation**; Apply saves, Cancel discards.\n\nApplication tray icons occupy the middle strip; language/network/audio/clock and other system controls sit at the far right. The Windows 11 hidden-icons window is now included. If Explorer has not created that surface yet, use **Load tray apps...**, then reopen Taskbar Tiles. This opens the native tray only on your explicit click and never changes Windows pin/visibility settings. Actual exposed tray artwork is preferred; offscreen icons without cached artwork use their local application icon or a neutral fallback.\n\nThe bottom bar includes **Display settings**, **Bluetooth** and **Task Manager**; individual switches are under **Settings > Quick access**.\n'''; readme.write_text(text,encoding='utf-8')
# This temporary preparation script and workflow never become part of the release tree.
(ROOT/'tools/prepare0140.py').unlink()
(ROOT/'.github/workflows/prepare0140.yml').unlink()
print('Source changes applied. Ready for normal PR review and Windows validation.')
