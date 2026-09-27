from pathlib import Path
root=Path('.')
def edit(path, old, new):
    p=root/path
    s=p.read_text(encoding='utf-8-sig')
    if s.count(old)!=1: raise RuntimeError(f'{path}: expected 1 occurrence of {old[:80]!r}, got {s.count(old)}')
    p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
strip='src/TaskbarTiles/NotificationStrip.cs'
area='src/TaskbarTiles/NotificationArea.cs'
edit(strip,'||closing||notificationMenu!=null)return;','||closing||NotificationMenuOpen)return;')
edit(strip,'if (pressedMouseHit >= 3000 || notificationMenu != null)','if (pressedMouseHit >= 3000 || NotificationMenuOpen)')
p=root/strip;s=p.read_text();start=s.index('        void ShowNotificationActions(');end=s.index('        bool NotificationAreaWheel',start)
s=s[:start]+'''        void ShowNotificationActions(NotificationItem item,Point point)
        {
            if(item==null || closing || IsDisposed)return;
            EnsureNotificationMenu();
            if(notificationMenu.Visible)notificationMenu.Close();
            notificationMenuTarget=item;
            notificationMenu.Show(this,point);
        }
'''+s[end:];p.write_text(s,encoding='utf-8')
edit(strip,'if(notificationMenu!=null){var menu=notificationMenu;notificationMenu=null;menu.Close();menu.Dispose();}',
     'if(notificationMenu!=null){var menu=notificationMenu;notificationMenu=null;TrayMenuLifetime.Release(this,menu);}')
edit(strip,'''                    DrawingUtil.Round(g,box,S(5),Theme.Card,Theme.Border,1);
                    Label(g,TextTools.Initials(item.Name),box,false,accent,true);''',
     '''                    TrayImageFallback.Paint(g,box);''')
edit(strip,'"Tray application")+" · Left-click: default action · Right-click: actions";',
     '"Tray application")+" · "+item.ImageSource+" · Left-click: default action · Right-click: actions";')
edit(strip,'''            string[] system = { "clock", "controlcenter", "actioncenter", "notificationcenter", "language", "inputindicator", "texticonview", "battery", "volume", "network", "touchkeyboard", "penmenu" };''',
'''            if (metadata.Contains("notifyicon") || metadata.Contains("normaliconview")) return false;
            string[] system = { "clock", "controlcenter", "actioncenter", "notificationcenter", "language", "inputindicator", "texticonview", "texticoncontent", "battery", "volume", "network", "touchkeyboard", "penmenu", "microphone", "omnibutton" };''')
edit(area,'        internal IntPtr Root;','        internal string ImageSource = "Image unavailable";\n        internal IntPtr Root;')
edit(area,'        List<FavouriteEntry> catalog;','        readonly WindowsTrayArt windowsArt = new WindowsTrayArt();\n        List<FavouriteEntry> catalog;')
edit(area,'if(catalog==null) try { catalog=InstalledApps.Read(); } catch { catalog=new List<FavouriteEntry>(); }',
     'windowsArt.Refresh();')
edit(area,'''                                artworkCache[item.Key]=(Bitmap)item.Image.Clone();continue;
                            }
                            if(artworkCache.TryGetValue(item.Key,out previous)){item.Image=(Bitmap)previous.Clone();continue;}
                            string target=TargetFor(item.Name);''',
'''                                artworkCache[ArtworkKey(item)]=(Bitmap)item.Image.Clone();continue;
                            }
                            if(artworkCache.TryGetValue(ArtworkKey(item),out previous)){item.Image=(Bitmap)previous.Clone();item.ImageSource="Cached live tray artwork";continue;}
                            item.Image=windowsArt.Get(item.Name,item.AutomationId);
                            if(item.Image!=null){item.ImageSource="Windows saved tray artwork (status may lag)";continue;}
                            if(catalog==null) try { catalog=InstalledApps.Read(); } catch { catalog=new List<FavouriteEntry>(); }
                            string target=TargetFor(item.Name);''')
edit(area,'if(artworkCache.TryGetValue(item.Key,out previous)){previous.Dispose();artworkCache.Remove(item.Key);}',
     'if(artworkCache.TryGetValue(ArtworkKey(item),out previous)){previous.Dispose();artworkCache.Remove(ArtworkKey(item));}')
edit(area,'{ cached=ShellIcons.Extract(Environment.ExpandEnvironmentVariables(target),64); cache[target]=cached; }',
     '{ cached=ShellIcons.Extract(Environment.ExpandEnvironmentVariables(target),64); if(cached!=null)cache[target]=cached; }')
edit(area,'if(cached!=null) item.Image=(Bitmap)cached.Clone();',
     'if(cached!=null){item.Image=(Bitmap)cached.Clone();item.ImageSource="Application icon fallback";}')
edit(area,'        string TargetFor(string name)',
'''        // A changing tooltip/state or recycled runtime ID cannot inherit another item's pixels.
        static string ArtworkKey(NotificationItem item)
        { return item.Key + "|" + item.ClassName + "|" + item.Name; }
        string TargetFor(string name)''')
edit(area,'Offscreen=c.IsOffscreen,Enabled=c.IsEnabled,Image=artwork,SystemItem=',
     'Offscreen=c.IsOffscreen,Enabled=c.IsEnabled,Image=artwork,ImageSource=artwork==null?"Image unavailable":"Live Windows tray artwork",SystemItem=')
edit(area,'''                var list=Scan(includeHidden); string status=list.Count==0?
                    "Windows has not exposed notification-area items yet. Refresh after the taskbar/Explorer is ready.":"";
                icons.Resolve(list,delegate{completed(list,status);});''',
'''                try
                {
                    var list=Scan(includeHidden); string status=list.Count==0?
                        "Windows has not exposed notification-area items yet. Refresh after the taskbar/Explorer is ready.":"";
                    icons.Resolve(list,delegate{completed(list,status);});
                }
                catch(Exception ex){Program.Log("Tray scan failed: "+ex.GetType().Name);completed(new List<NotificationItem>(),"Tray scan unavailable; try Refresh tray items.");}''')
edit('src/TaskbarTiles/NotificationAreaTests.cs','                Run(log);\n                string token=',
     '                Run(log);\n                TrayRepairTests.RunNative(log);\n                string token=')
edit('src/TaskbarTiles/TaskbarTiles.csproj','    <Compile Include="NotificationAreaTests.cs" />',
     '    <Compile Include="NotificationAreaTests.cs" />\n    <Compile Include="TrayRepair.cs" />\n    <Compile Include="TrayRepairTests.cs" />')
edit('src/TaskbarTiles/TaskbarTiles.cs','internal const string Version = "0.14.0";', 'internal const string Version = "0.14.1";')
p=root/'src/TaskbarTiles/AssemblyInfo.cs';s=p.read_text(encoding='utf-8-sig');assert '0.14.0' in s;p.write_text(s.replace('0.14.0','0.14.1'),encoding='utf-8')
(root/'version.txt').write_text('0.14.1\n',encoding='utf-8')
p=root/'CHANGELOG.md';s=p.read_text(encoding='utf-8-sig');assert s.startswith('# Changelog\n');p.write_text(s.replace('# Changelog\n','# Changelog\n\n## 0.14.1\n\n- Fix tray right-click/click-away termination: reuse the context menu and defer command/disposal work until ToolStrip completes its close path.\n- Read matching Windows saved tray PNG artwork without needing the overflow panel visible; never add historical registry records to the live tray list. Registry access is read-only.\n- Retain live tray captures first, reject ambiguous snapshot matches and label cached/app-icon sources. Unavailable artwork uses a neutral application symbol rather than initials.\n- Add Copy tray diagnostics and a load/refresh-images action. Keep app icons centred and taskbar system controls at the far right.\n- Add native production-menu cancellation/outside-click/reopen tests and bounded PNG/cache-match tests.\n',1),encoding='utf-8')
p=root/'README.md';s=p.read_text(encoding='utf-8-sig');s+='\n### Tray repair in 0.14.1\n\nThe tray mirror now reads Windows saved `IconSnapshot` PNGs using read-only access to `HKCU\\Control Panel\\NotifyIconSettings`. Only an unambiguous match to an item already in the live tray inventory supplies an image; old registry records never create extra app buttons. Windows may update saved artwork later than the live icon. Visible unobstructed captures still take precedence. Unknown images use a neutral application symbol; right-click **Copy tray diagnostics** reports each image source locally. This cache format is an optional Windows compatibility path, not a guaranteed public API.\n\nRight-click menus are reused rather than disposed during their `Closed` event. Commands and final disposal run after the close stack completes. **Load / refresh Windows tray images** remains an explicit user action; no hidden taskbar coordinates are clicked and Windows pin/visibility settings are not modified.\n';p.write_text(s,encoding='utf-8')
(root/'.github/workflows/prepare-tray-repair.yml').unlink()
(root/'tools/apply_tray_repair.py').unlink()
