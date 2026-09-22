from pathlib import Path

def edit(path, old, new):
    p=Path(path); text=p.read_text(encoding='utf-8-sig'); assert text.count(old)==1,(path,old,text.count(old));p.write_text(text.replace(old,new),encoding='utf-8',newline='\n')
edit('src/TaskbarTiles/SearchPageTests.cs','limits.SearchPanelWidth == 1100','limits.SearchPanelWidth == 1400')
edit('src/TaskbarTiles/RecentApps.cs','                var key = LauncherKey.FromEntry(entry, true);\n                if (key.Exe.Length == 0)', '''                var key = LauncherKey.FromEntry(entry, true);
                // A document shortcut can also end in .lnk. It is not an app launcher.
                if (entry.ExpandedTarget.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                    !key.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !LaunchResolution.ExplicitId(key.AppId)) return;
                if (key.Exe.Length == 0)''')
print('Review corrections applied; checks remain mandatory.')
