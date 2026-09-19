from pathlib import Path

root = Path('.')
def edit(path, old, new):
    p = root / path
    text = p.read_text(encoding='utf-8-sig')
    if text.count(old) != 1:
        raise RuntimeError(f'{path}: expected exactly one anchor {old[:100]!r}; got {text.count(old)}')
    p.write_text(text.replace(old, new), encoding='utf-8', newline='\n')

def block(path, start, finish, replacement):
    p = root / path
    text = p.read_text(encoding='utf-8-sig')
    if text.count(start) != 1 or text.count(finish) != 1:
        raise RuntimeError(f'{path}: ambiguous block anchor')
    a = text.index(start); b = text.index(finish, a)
    p.write_text(text[:a] + replacement + text[b:], encoding='utf-8', newline='\n')

base = 'src/TaskbarTiles/'
main = base + 'TaskbarTiles.cs'
edit(main, 'internal const string Version = "0.7.4";', 'internal const string Version = "0.7.5";')
edit(main, '// Taskbar Tiles 0.7.4', '// Taskbar Tiles 0.7.5')
edit(main, '            if (args.Contains("--test-update-https"))', '''            if (args.Contains("--test-ui-reliability")) { Environment.Exit(UiReliabilityTests.RunNative()); return; }
            if (args.Contains("--test-ui-target")) { Environment.Exit(UiReliabilityTests.RunTarget(args)); return; }
            if (args.Contains("--test-update-https"))''')
edit(main, '            menu.Items.Add("Open switching diagnostics", null, delegate { OpenSwitchingDiagnostics(); });', '''            menu.Items.Add("Open switching diagnostics", null, delegate { OpenSwitchingDiagnostics(); });
            menu.Items.Add("Repair menu rendering", null, delegate { try { RepairMenuRendering(true); } catch (Exception ex) { RenderDiagnostics.Write("manual-repair", ex); Notify("See rendering diagnostics."); } });
            menu.Items.Add("Open rendering diagnostics", null, delegate { OpenRenderingDiagnostics(); });''')
edit(main, '            { return !closing && transient == null && activation == null && !fullscreenOpening; });', '''            { return !closing && transient == null && activation == null && !fullscreenOpening; });
            SetupOutsideDismissal();''')
edit(main, '            foregroundBeforeMenu = foregroundBeforeOpen;', '            StartRenderSession();\n            foregroundBeforeMenu = foregroundBeforeOpen;')
block(main, '            Font oldFont = uiFont;', '            LayoutMenu();\n            suppressDeactivate = true;', '            RefreshMenuFonts();\n')
block(main, '        void RefreshLabelFonts()', '        void LayoutMenu()', '')
edit(main, '            RefreshLabelFonts();', '            RefreshMenuFonts();')
edit(main, 'if (options.EnableSearch) { searchBox.Font = Font;', 'if (options.EnableSearch) { FontBinding.Assign(searchBox, Font);')
edit(main, '''        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics;''', '''        void PaintMenuContents(Graphics g)
        {
            if (windowPage * perWindowPage < 0 || windowPage * perWindowPage + cardRects.Count > windows.Count ||
                appPage * perAppPage < 0 || appPage * perAppPage + tileRects.Count > apps.Count || cardCloseRects.Count < cardRects.Count)
                throw new InvalidOperationException("The paint layout is stale; rebuild before drawing.");''')
edit(main, '''                    if (icon != null)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(icon, DrawingUtil.Fit(header.Icon, icon.Width, icon.Height));
                    }
                    else WindowHeaderGeometry.PaintFallback(g, header.Icon, accent);''', '''                    if (!RenderSafety.DrawImage(g, icon, header.Icon)) WindowHeaderGeometry.PaintFallback(g, header.Icon, accent);''')
block(main, '''                if (app.Image != null)
                {''', '''                var label = new Rectangle(r.Left + S(5), r.Bottom - labelHeight - S(5),''', '''                if (!RenderSafety.DrawImage(g, app.Image, box))
                {
                    DrawingUtil.Round(g, box, S(9), Color.FromArgb(45, 65, 89), Color.Transparent, 0);
                    Label(g, TextTools.Initials(app.DisplayName), box, true, accent, true);
                }
''')
edit(main, '        { if (switcherLayer != null) switcherLayer.Suspend(); if (fullscreenOpening)', '        { EndRenderSession(); if (outsideClicks != null) outsideClicks.Suspend(); Capture = false; if (switcherLayer != null) switcherLayer.Suspend(); if (fullscreenOpening)')
edit(main, '            if (switcherLayer != null) switcherLayer.Dispose();', '            DisposeOutsideDismissal();\n            if (switcherLayer != null) switcherLayer.Dispose();')
edit(main, '            Close(); Application.ExitThread();', '            ReleaseMenuFonts(); Close(); Application.ExitThread();')
edit(main, '                SwitcherLayerTests.Run(log);', '                SwitcherLayerTests.Run(log);\n                UiReliabilityTests.Run(log);')

# Main menu previews must not leave equal-but-disposed control fonts or draft geometry behind.
quick = base + 'QuickAccess.cs'
edit(quick, 'oldTitle = windowTitleFont, oldFont = Font;', 'oldTitle = windowTitleFont, oldFont = Font, oldSearchFont = searchBox.Font;')
edit(quick, 'windowTitleFont = new Font("Segoe UI", options.WindowTitleFontSize * scale, GraphicsUnit.Pixel); Font = uiFont;', 'windowTitleFont = new Font("Segoe UI", options.WindowTitleFontSize * scale, GraphicsUnit.Pixel); FontBinding.Assign(this, uiFont); FontBinding.Assign(searchBox, uiFont);')
edit(quick, '''                using (var g = Graphics.FromImage(image)) { g.Clear(BackColor); OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, image.Size))); }
                return image;''', '''                try { using (var g = Graphics.FromImage(image)) { g.Clear(BackColor); PaintMenuContents(g); } return image; }
                catch { image.Dispose(); throw; }''')
edit(quick, '                Font = oldFont; searchBox.Font = oldFont;', '                FontBinding.Assign(this, oldFont); FontBinding.Assign(searchBox, oldSearchFont);')
edit(quick, '                renderingPreview = false; quick = new QuickAccessLayout();', '''                // Restore the full live layout, not just its Bounds and backing lists.
                // Keep native paints suppressed until every rectangle/font belongs to the live state.
                try { if (area.Width > 0 && area.Height > 0) LayoutMenu(); }
                finally { searchBox.Visible = false; renderingPreview = false; }''')

icons = base + 'WindowHeaderIcons.cs'
edit(icons, '        public void Dispose()\n        {', '''        internal void ClearCache()
        {
            if (stopped) return;
            foreach (var entry in cache.Values) if (entry.Image != null) entry.Image.Dispose();
            cache.Clear();
        }
        public void Dispose()
        {''')

settings = base + 'SettingsDismissal.cs'
edit(settings, '        readonly Timer dismissTimer', '        PopupClickWatcher settingsOutsideClicks;\n        readonly Timer dismissTimer')
edit(settings, '            dismissOnFocusLoss = enabled;', '''            dismissOnFocusLoss = enabled;
            settingsOutsideClicks = new PopupClickWatcher(this, delegate
            { return dismissOnFocusLoss && !DismissedByFocusLoss && !closingSettings; }, RequestOutsideSettingsDismissal);''')
edit(settings, '''            DismissedByFocusLoss = true;
            previewTimer.Stop(); settingHints.RemoveAll();
            FinishOutsideDismissal();
        }
        void FinishOutsideDismissal()''', '''            RequestOutsideSettingsDismissal();
        }
        void RequestOutsideSettingsDismissal()
        {
            if (IsDisposed || closingSettings || DismissedByFocusLoss) return;
            DismissedByFocusLoss = true;
            previewTimer.Stop(); settingHints.RemoveAll();
            FinishOutsideDismissal();
        }
        void FinishOutsideDismissal()''')
edit(settings, '        { closingSettings = true; dismissTimer.Stop(); dismissTimer.Dispose(); }', '        { closingSettings = true; if (settingsOutsideClicks != null) settingsOutsideClicks.Dispose(); dismissTimer.Stop(); dismissTimer.Dispose(); }')

# Font uses value equality; resource ownership must use reference equality here too.
render = base + 'RenderReliability.cs'
edit(render, 'var retired = new HashSet<Font>(new[] { uiFont, headingFont, tileFont, windowTitleFont });', 'var retired = new[] { uiFont, headingFont, tileFont, windowTitleFont };')
edit(render, 'foreach (var font in new HashSet<Font>(new[] { uiFont, headingFont, tileFont, windowTitleFont }))', 'foreach (var font in new[] { uiFont, headingFont, tileFont, windowTitleFont })')

edit(base + 'AssemblyInfo.cs', '[assembly: AssemblyVersion("0.7.4.0")]', '[assembly: AssemblyVersion("0.7.5.0")]')
edit(base + 'AssemblyInfo.cs', '[assembly: AssemblyFileVersion("0.7.4.0")]', '[assembly: AssemblyFileVersion("0.7.5.0")]')
edit(base + 'AssemblyInfo.cs', '[assembly: AssemblyInformationalVersion("0.7.4")]', '[assembly: AssemblyInformationalVersion("0.7.5")]')
edit(base + 'app.manifest', 'version="0.7.4.0"', 'version="0.7.5.0"')
edit(base + 'TaskbarTiles.csproj', '    <Compile Include="SwitcherLayer.cs" />', '''    <Compile Include="RenderReliability.cs" />
    <Compile Include="OutsideClickDismissal.cs" />
    <Compile Include="UiReliabilityTests.cs" />
    <Compile Include="SwitcherLayer.cs" />''')
edit('version.txt', '0.7.4', '0.7.5')
edit('CHANGELOG.md', '# Changelog\n\n## 0.7.4', '''# Changelog

## 0.7.5

- Fix a value-equal Font assignment/disposal bug on repeated opening and settings preview restoration. Reuse live fonts and detach controls before retiring replaced resources.
- Restore live menu geometry after preview rendering, isolate bad optional icons and catch menu-paint errors before Windows Forms latches its white/red-X placeholder.
- Bound automatic render repair; add Repair menu rendering and Open rendering diagnostics in the tray. Do not reset settings or restart/launch applications as recovery.
- Observe outside left/right/middle clicks without consuming them, including when the popup never obtained focus. Reject stale clicks from earlier sessions and preserve owned dialogs, search controls and the click-away preference.
- Apply the same outside-click safety to Settings; unapplied drafts still discard and explicit Apply remains saved.
- Add native font/preview-cycle, paint-fault and nonactivating outside-click regression tests. Preserve topmost, launch, placement and updater fixes.

## 0.7.4''')

for path in ['.github/workflows/build.yml', '.github/workflows/release.yml']:
    edit(path, '      - name: Test native switcher topmost ordering', '''      - name: Test rendering lifetime and outside-click dismissal
        shell: powershell
        run: ./tools/Test-UiReliability.ps1
      - name: Test native switcher topmost ordering''')
edit('.github/workflows/release.yml', "            helper_tests = 'passed'", "            helper_tests = 'passed'\n            ui_rendering_clickaway_tests = 'passed on disposable Windows fixtures; not the user desktop'")
print('Exact 0.7.5 edits applied. Windows compilation and native regressions are required before committing.')
