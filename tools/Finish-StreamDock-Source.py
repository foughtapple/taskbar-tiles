# Temporary, bounded repository migration; remove after applying in preparation CI.
import pathlib,json,re,subprocess,shutil
R=pathlib.Path(__file__).resolve().parents[1]
def read(p):return (R/p).read_text(encoding='utf-8-sig')
def write(p,s):(R/p).write_text(s,encoding='utf-8')
def edit(p,f):write(p,f(read(p)))
# The build keeps the latest taskbar fixes while restoring independent module hooks.
edit('src/TaskbarTiles/TaskbarTiles.cs',lambda s:re.sub(r'internal const string Version = "[0-9.]+";', 'internal const string Version = "0.10.0";',s))
p='src/TaskbarTiles/TaskbarTiles.cs';s=read(p)
if '--sync-streamdock' not in s:
 anchor='            if (args.Contains("--self-test"))'
 assert s.count(anchor)==1
 s=s.replace(anchor,'            if (args.Contains("--sync-streamdock")) { Environment.Exit(DockManager.SyncInstalled(false)); return; }\n            if (args.Contains("--streamdock-ready")) { Environment.Exit(DockManager.SyncInstalled(true)); return; }\n            if (args.Contains("--test-streamdock")) { Environment.Exit(StreamDockTests.Run()); return; }\n'+anchor)
if 'ThreadPool.QueueUserWorkItem(delegate { DockManager.SyncInstalled(false); });' not in s:
 anchor='                    Options.Migrate();';assert s.count(anchor)==1;s=s.replace(anchor,anchor+'\n                    ThreadPool.QueueUserWorkItem(delegate { DockManager.SyncInstalled(false); });')
write(p,s)
p='src/TaskbarTiles/Settings.cs';s=read(p)
if 'AddStreamDockPage();' not in s:
 assert s.count('            AddUpdatesPage();')==1;s=s.replace('            AddUpdatesPage();','            AddUpdatesPage();\n            AddStreamDockPage();')
write(p,s)
edit('src/TaskbarTiles/AssemblyInfo.cs',lambda s:re.sub(r'(Assembly(?:File)?Version\(")[0-9.]+',r'\g<1>0.10.0.0',s))
write('version.txt','0.10.0\n')
p='src/TaskbarTiles/TaskbarTiles.csproj';s=read(p)
for name in ['System.IO.Compression','System.IO.Compression.FileSystem']:
 if f'<Reference Include="{name}"' not in s:s=s.replace('<Reference Include="System.Core" />',f'<Reference Include="System.Core" />\n    <Reference Include="{name}" />')
for name in ['StreamDockModule.cs','StreamDockTests.cs','SettingsStreamDock.cs']:
 if f'Compile Include="{name}"' not in s:s=s.replace('<Compile Include="Settings.cs" />',f'<Compile Include="Settings.cs" />\n    <Compile Include="{name}" />')
write(p,s)
# The combined Steam button superseded the separate indicator in the final design.
c=json.loads(read('streamdock/catalog-source.json'));c['Packages']=[p for p in c['Packages'] if p['Id']!='steam-display'];write('streamdock/catalog-source.json',json.dumps(c,indent=2)+'\n')
edit('src/TaskbarTiles/SettingsStreamDock.cs',lambda s:s.replace('dockGrid.Rows.Count != 11','dockGrid.Rows.Count != DockManager.Open().Catalog.Packages.Sum(p => p.Actions.Length)'))
edit('src/TaskbarTiles/StreamDockTests.cs',lambda s:s.replace('Packages.Length == 5','Packages.Length == 4').replace('five final module packages','four final module packages').replace('Actions.Length) == 11','Actions.Length) == 10').replace('eleven independent','ten independent').replace('with eleven actions','with ten actions'))
# Source-sized artwork: the Steam shape is traced from the supplied icon; live avatars
# are still loaded from Steam. The order count uses a bounded SVG vector renderer.
p='streamdock/modules/steam/package/plugin/steam.cjs';s=read(p)
a=s.index(' staticImage(name){');b=s.index('\n fallback(account)',a)
s=s[:a]+''' staticImage(name){if(!this.static)this.static={};if(!this.static[name]){let p=path.join(this.root,'images',name+'.png'),mime='image/png';if(!fss.existsSync(p)){p=path.join(this.root,'images','steam.svg');mime='image/svg+xml';}this.static[name]='data:'+mime+';base64,'+fss.readFileSync(p).toString('base64');}return this.static[name];}'''+s[b:];write(p,s)
edit('streamdock/modules/steam/package/plugin/main.cjs',lambda s:s.replace("'images','steam.png'","'images','steam.svg'"))
p='streamdock/modules/steam/package/manifest.json';s=read(p).replace('images/steam.png','images/steam.svg');write(p,s)
p='streamdock/modules/orders/package/manifest.json';s=read(p)
for n in ['action.png','category.png','plugin.png','example-unknown.png']:s=s.replace('images/'+n,'images/action.svg')
write(p,s)
edit('streamdock/modules/orders/package/inspector/index.html',lambda s:s.replace('../images/action.png','../images/action.svg'))
edit('streamdock/modules/orders/src/main.go',lambda s:s.replace('".png"','".svg"'))
p='tools/Build.ps1';s=read(p)
s=s.replace("'System.Web.Extensions','System.Data'","'System.Web.Extensions','System.Data','System.IO.Compression','System.IO.Compression.FileSystem'")
s=s.replace("$refs = @('System.dll','System.Core.dll',","$refs = @('System.dll','System.Core.dll', [IO.Compression.ZipArchive].Assembly.Location, [IO.Compression.ZipFile].Assembly.Location,")
if "Build-StreamDock.ps1" not in s:s+='\n& (Join-Path $PSScriptRoot \'Build-StreamDock.ps1\')\n& (Join-Path $PSScriptRoot \'Test-StreamDock.ps1\')\n'
write(p,s)
p='tools/Test-Installer.ps1';s=read(p)
anchor="$uninstall = Join-Path $dest 'unins000.exe'"
if 'Test-StreamDockInstaller.ps1' not in s:s=s.replace(anchor,"& (Join-Path $PSScriptRoot 'Test-StreamDockInstaller.ps1') -InstallRoot $dest -Setup $setup\n"+anchor)
write(p,s)
p='docs/STREAM-DOCK.md';s=read(p);s='\n'.join(l for l in s.splitlines() if not l.startswith('| Steam Account Status |'))+'\n';s=s.replace('and Steam-status action UUIDs','action UUIDs');s+='\nThe obsolete standalone Steam account indicator is intentionally not reinstated; use Steam Smart Switch. Any independently installed copy stays untouched. The count display retains its black/red design as a scalable 256px vector, rather than raster glyph files.\n';write(p,s)
# Preserve main's notes and explicitly document the new module.
p='README.md';s=read(p)
if 'Settings > Stream Dock' not in s:s+='\n## Stream Dock modules\n\nTaskbar Tiles 0.10 includes a separate Settings > Stream Dock tab for ten final native actions. Enable modules once and keep Update enabled modules when Taskbar Tiles updates checked. Close Stream Dock before Apply or updating. See [Stream Dock setup and updates](docs/STREAM-DOCK.md). Other vendors and settings remain untouched.\n'
write(p,s)
p='CHANGELOG.md';s=read(p)
if '## 0.10.0' not in s:s=s.replace('# Changelog','# Changelog\n\n## 0.10.0\n\n- Add optional Settings > Stream Dock package/action management, bundled updates, opt-outs and recoverable backups.\n- Preserve 0.9.1 app reopening and existing taskbar behaviour.\n- Include final Steam, desktop, printer/CPU and order-count actions; leave the obsolete standalone Steam indicator and third-party plugins alone.\n',1)
write(p,s)
# No private settings, recorded output, obsolete installers or transfer fragments.
for p in [R/'preparation',R/'streamdock/modules/steam-display']:
 if p.exists():shutil.rmtree(p)
print('Prepared current-main-compatible 0.10.0 source; four packages, ten actions.')
