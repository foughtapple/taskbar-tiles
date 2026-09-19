from pathlib import Path

def edit(path, old, new):
    p=Path(path); s=p.read_text(encoding='utf-8-sig')
    assert s.count(old)==1, (path, old[:90], s.count(old))
    p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
def block(path,start,finish,replacement):
    p=Path(path); s=p.read_text(encoding='utf-8-sig')
    assert s.count(start)==1 and s.count(finish)==1, path
    a=s.index(start); b=s.index(finish,a)
    p.write_text(s[:a]+replacement+s[b:],encoding='utf-8',newline='\n')
base='src/TaskbarTiles/'
main=base+'TaskbarTiles.cs'
edit(main,'internal const string Version = "0.7.5";','internal const string Version = "0.7.6";')
edit(main,'// Taskbar Tiles 0.7.4','// Taskbar Tiles 0.7.6')
edit(main,'            if (args.Contains("--test-update-https"))','''            if (args.Contains("--test-clickaway")) { Environment.Exit(OutsideClickTests.RunNative()); return; }
            if (args.Contains("--test-clickaway-target")) { Environment.Exit(OutsideClickTests.RunTarget(args)); return; }
            if (args.Contains("--test-update-https"))''')
edit(main,'            { return !closing && transient == null && activation == null && !fullscreenOpening; });','''            { return !closing && transient == null && activation == null && !fullscreenOpening; });
            SetupOutsideDismissal();''')
edit(main,'        { if (switcherLayer != null) switcherLayer.Suspend(); if (fullscreenOpening)','        { if (outsideClicks != null) outsideClicks.Suspend(); Capture = false; if (switcherLayer != null) switcherLayer.Suspend(); if (fullscreenOpening)')
edit(main,'            if (switcherLayer != null) switcherLayer.Dispose();','            DisposeOutsideDismissal();\n            if (switcherLayer != null) switcherLayer.Dispose();')
edit(main,'                SwitcherLayerTests.Run(log);','                SwitcherLayerTests.Run(log);\n                OutsideClickTests.Run(log);')

settings=base+'SettingsDismissal.cs'
edit(settings,'        readonly Timer dismissTimer','        PopupClickWatcher settingsOutsideClicks;\n        readonly Timer dismissTimer')
edit(settings,'            dismissOnFocusLoss = enabled;','''            dismissOnFocusLoss = enabled;
            settingsOutsideClicks = new PopupClickWatcher(this, delegate
            { return dismissOnFocusLoss && !DismissedByFocusLoss && !closingSettings; }, RequestOutsideSettingsDismissal);''')
edit(settings,'''            DismissedByFocusLoss = true;
            previewTimer.Stop(); settingHints.RemoveAll();
            FinishOutsideDismissal();
        }
        void FinishOutsideDismissal()''','''            RequestOutsideSettingsDismissal();
        }
        void RequestOutsideSettingsDismissal()
        {
            if (IsDisposed || closingSettings || DismissedByFocusLoss) return;
            DismissedByFocusLoss = true;
            previewTimer.Stop(); settingHints.RemoveAll();
            FinishOutsideDismissal();
        }
        void FinishOutsideDismissal()''')
edit(settings,'        { closingSettings = true; dismissTimer.Stop(); dismissTimer.Dispose(); }','        { closingSettings = true; if (settingsOutsideClicks != null) settingsOutsideClicks.Dispose(); dismissTimer.Stop(); dismissTimer.Dispose(); }')

# Keep the already-published 0.7.5 renderer and its full-menu tests unchanged.
# Only adapt the independently tested non-consuming click observer to its logger.
p=Path(base+'OutsideClickDismissal.cs'); s=p.read_text(encoding='utf-8-sig')
s=s.replace('RenderDiagnostics.Write(', 'ClickAwayDiagnostics.Write(')
s=s.replace('            ready.WaitOne(2000);','            if (ready.WaitOne(2000)) ready.Dispose();')
s=s.replace('ready.Set();','SignalReady();')
s=s.replace('        void Run()\n', '''        void SignalReady() { try { ready.Set(); } catch (ObjectDisposedException) { } }
        void Run()
''')
assert 'RenderDiagnostics.' not in s
s=s.replace('    static class OutsideClickPolicy','''    static class ClickAwayDiagnostics
    {
        static readonly object gate = new object();
        static DateTime last; static int count;
        internal static void Write(string phase, Exception ex)
        {
            lock (gate)
            {
                if ((DateTime.UtcNow - last).TotalMinutes >= 1) { last = DateTime.UtcNow; count = 0; }
                if (count++ < 12) RenderingLog.Write(phase, ex, Size.Empty, 1, 0, 0);
            }
        }
    }
    static class OutsideClickPolicy''')
p.write_text(s,encoding='utf-8',newline='\n')

tests=base+'OutsideClickTests.cs'
block(tests,'            bool failed = false;','            log.AppendLine("PASS: " + checks + " paint-boundary', '')
edit(tests,'" paint-boundary and click-away policy assertions; no input was generated."','" outside-click policy assertions; no input was generated."')
block(tests,'        sealed class PaintFixture : Form','        sealed class NoActivateFixture : Form','')
block(tests,'                using (var surface = new Bitmap(500, 220))','                Rectangle work = Screen.PrimaryScreen.WorkingArea;','')
p=Path(tests); s=p.read_text(encoding='utf-8-sig')
s=s.replace('UiReliabilityTests','OutsideClickTests').replace('TT075','TT076').replace('--test-ui-target','--test-clickaway-target').replace('ui-reliability-test.log','outside-click-test.log')
s=s.replace(' native font/preview/paint-fault/click-away assertions, including 300 font preview/reopen cycles.',' native click-away assertions, including clicks on a separate nonactivating process.')
assert 'RenderSafety' not in s and 'FontBinding' not in s
p.write_text(s,encoding='utf-8',newline='\n')

edit(base+'AssemblyInfo.cs','[assembly: AssemblyVersion("0.7.5.0")]','[assembly: AssemblyVersion("0.7.6.0")]')
edit(base+'AssemblyInfo.cs','[assembly: AssemblyFileVersion("0.7.5.0")]','[assembly: AssemblyFileVersion("0.7.6.0")]')
edit(base+'AssemblyInfo.cs','[assembly: AssemblyInformationalVersion("0.7.5")]','[assembly: AssemblyInformationalVersion("0.7.6")]')
edit(base+'app.manifest','version="0.7.5.0"','version="0.7.6.0"')
edit(base+'TaskbarTiles.csproj','    <Compile Include="SwitcherLayer.cs" />','''    <Compile Include="OutsideClickDismissal.cs" />
    <Compile Include="OutsideClickTests.cs" />
    <Compile Include="SwitcherLayer.cs" />''')
edit('version.txt','0.7.5','0.7.6')
edit('CHANGELOG.md','# Changelog\n\n## 0.7.5','''# Changelog

## 0.7.6

- Close the menu on an outside mouse-down even when it never received foreground activation; do not consume the click.
- Reject stale observations from an earlier open/hide session. Preserve clicks inside the menu, embedded search, owned dialogs and X-Mouse toggle behaviour.
- Verify settled external focus after the menu has held focus. Stop all observation on dismissal/selection/shutdown and respect the existing click-away preference.
- Apply the same click event handling to Settings; discard unapplied drafts, retain explicit Apply saves and unwind owned dialogs safely.
- Retain the complete 0.7.5 font lifetime, icon isolation, guarded-paint and bounded recovery fix and its actual-menu rendering tests.
- Add native outside-click regression tests using a disposable nonactivating window in a separate process. Existing layout, launch-instance, topmost, placement and secure updater behavior remains.

## 0.7.5''')
print('Applied 0.7.6 click-away integration; preserved published 0.7.5 rendering implementation. Tests required before committing.')
