from pathlib import Path

root = Path(__file__).resolve().parent.parent

def edit(path, before, after):
    p = root / path
    text = p.read_text(encoding='utf-8-sig')
    if text.count(before) != 1:
        raise RuntimeError(f'{path}: expected one exact anchor: {before[:100]!r}')
    p.write_text(text.replace(before, after), encoding='utf-8', newline='\n')

core = 'src/TaskbarTiles/TaskbarTiles.cs'
edit(core, 'internal const string Version = "0.7.2";', 'internal const string Version = "0.7.3";')
edit(core, '// Taskbar Tiles 0.7.2 - Windows utility.', '// Taskbar Tiles 0.7.3 - Windows utility.')
edit(core, '        public Switcher()\n', '        readonly SwitcherLayer switcherLayer;\n\n        public Switcher()\n')
edit(core, '            SetupFeatures(); SetupQuickAccess(); SetupFullscreen(); SetupActivation();\n', '''            SetupFeatures(); SetupQuickAccess(); SetupFullscreen(); SetupActivation();
            switcherLayer = new SwitcherLayer(this, delegate
            { return !closing && transient == null && activation == null && !fullscreenOpening; });
''')
edit(core, '                if (integratedSearch != null && integratedSearch.Visible) { integratedSearch.FocusInput(); return; }', '''                switcherLayer.RaiseNow();
                if (integratedSearch != null && integratedSearch.Visible) { integratedSearch.FocusInput(); return; }''')
edit(core, '''            suppressDeactivate = true;
            Show(); Activate(); Native.SetForegroundWindow(Handle);
            try { int corner = 2; Native.DwmSetWindowAttribute(Handle, 33, ref corner, 4); } catch { }
            suppressDeactivate = false;''', '''            suppressDeactivate = true;
            try
            {
                switcherLayer.Begin();
                Show(); switcherLayer.RaiseNow();
                Activate(); Native.SetForegroundWindow(Handle);
                // Activation and topmost order are different operations. Reassert our
                // own HWND after activation; never promote the selected app to topmost.
                switcherLayer.RaiseNow();
                try { int corner = 2; Native.DwmSetWindowAttribute(Handle, 33, ref corner, 4); } catch { }
            }
            finally { suppressDeactivate = false; }''')
edit(core, '        { if (fullscreenOpening) CancelFullscreenOpen(); HideIntegratedSearch(); ClearThumbnails(); tip.Hide(this); Hide(); }', '        { if (switcherLayer != null) switcherLayer.Suspend(); if (fullscreenOpening) CancelFullscreenOpen(); HideIntegratedSearch(); ClearThumbnails(); tip.Hide(this); Hide(); }')
edit(core, '            CancelPendingLaunch(); DisposeActivation(); ShutdownFullscreen(); ShutdownQuickAccess(); ShutdownFeatures();', '            if (switcherLayer != null) switcherLayer.Dispose();\n            CancelPendingLaunch(); DisposeActivation(); ShutdownFullscreen(); ShutdownQuickAccess(); ShutdownFeatures();')
edit(core, '                ActivationTests.Run(log);', '                ActivationTests.Run(log);\n                SwitcherLayerTests.Run(log);')
edit(core, '            if (args.Contains("--self-test")) { Environment.Exit(SelfTests.Run()); return; }', '            if (args.Contains("--test-switcher-layer")) { Environment.Exit(SwitcherLayerTests.RunNative()); return; }\n            if (args.Contains("--self-test")) { Environment.Exit(SelfTests.Run()); return; }')
edit(core, 'Text = "Taskbar Tiles 0.7.0", Visible = true', 'Text = "Taskbar Tiles " + Program.Version, Visible = true')
edit(core, 'new StringBuilder("Taskbar Tiles 0.7.0 icon sources (local only)"', 'new StringBuilder("Taskbar Tiles " + Program.Version + " icon sources (local only)"')
edit('src/TaskbarTiles/WindowActivation.cs', '                activationClock.Restart();', '''                // Suspend before the first selected-window activation, not after it.
                // Otherwise a queued layer callback could cover the app we just chose.
                switcherLayer.Suspend();
                activationClock.Restart();''')
edit('src/TaskbarTiles/TaskbarTiles.csproj', '    <Compile Include="WindowActivation.cs" />', '    <Compile Include="SwitcherLayer.cs" />\n    <Compile Include="SwitcherLayerTests.cs" />\n    <Compile Include="WindowActivation.cs" />')
for field in ('AssemblyVersion', 'AssemblyFileVersion'):
    edit('src/TaskbarTiles/AssemblyInfo.cs', f'{field}("0.7.2.0")', f'{field}("0.7.3.0")')
edit('src/TaskbarTiles/AssemblyInfo.cs', 'AssemblyInformationalVersion("0.7.2")', 'AssemblyInformationalVersion("0.7.3")')
edit('src/TaskbarTiles/app.manifest', 'version="0.7.2.0"', 'version="0.7.3.0"')
edit('version.txt', '0.7.2', '0.7.3')
for workflow in ('.github/workflows/build.yml', '.github/workflows/release.yml'):
    edit(workflow, '      - name: Test actual updater HTTPS and verified download', '''      - name: Test native switcher topmost ordering
        shell: powershell
        run: ./tools/Test-SwitcherLayer.ps1
      - name: Test actual updater HTTPS and verified download''')
edit('.github/workflows/release.yml', "            helper_tests = 'passed'", "            helper_tests = 'passed'\n            native_switcher_layer_tests = 'passed on disposable test Forms; not the user desktop'")
edit('CHANGELOG.md', '# Changelog\n\n## 0.7.2', '''# Changelog

## 0.7.3

- Reassert the switcher's own native topmost layer on opening/activation and repair overlap while the popup is in use.
- Suspend layer maintenance before handing focus to the exact selected window, dismissal and modal transitions. Never change another app's topmost preference or steal focus during maintenance.
- Add native Z-order regression tests for competing topmost windows, stale cached/native style, repeated hide/reopen, modal ordering and handoff gates.
- Retain updater TLS, verified downloads, launch-placement fixes and all preferences; correct the tray version label.

## 0.7.2''')
print('Exact 0.7.3 edits applied; Windows tests must pass before committing.')
