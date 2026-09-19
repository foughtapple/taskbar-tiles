from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
def read(path):
    return (root / path).read_text(encoding='utf-8-sig')
def write(path, content):
    with (root / path).open('w', encoding='utf-8', newline='\n') as f:
        f.write(content)
def edit(path, before, after):
    s = read(path)
    if s.count(before) != 1:
        raise RuntimeError(f'{path}: expected one exact anchor: {before[:100]!r}, got {s.count(before)}')
    write(path, s.replace(before, after, 1))

base = 'src/TaskbarTiles/'
assert read('version.txt').strip() == '0.7.4'
edit(base+'TaskbarTiles.cs', 'internal const string Version = "0.7.4";', 'internal const string Version = "0.7.5";')
edit(base+'TaskbarTiles.cs', 'if (args.Contains("--self-test"))', 'if (args.Contains("--test-rendering")) { Environment.Exit(Switcher.RunRenderingRegressionTests()); return; }\n            if (args.Contains("--self-test"))')
edit(base+'TaskbarTiles.cs', '        public Switcher()\n        {', '        public Switcher() : this(false) { }\n        internal Switcher(bool renderingTest)\n        {')
edit(base+'TaskbarTiles.cs', '            var h = Handle; // Create the message target without showing the menu.\n', '            var h = Handle; // Create the message target without showing the menu.\n            if (renderingTest) { Controls.Add(searchBox); return; } // Explicit isolated rendering tests: no hooks/tray/workers.\n')
edit(base+'TaskbarTiles.cs', '            menu.Items.Add("Open switching diagnostics", null, delegate { OpenSwitchingDiagnostics(); });', '            menu.Items.Add("Open switching diagnostics", null, delegate { OpenSwitchingDiagnostics(); });\n            menu.Items.Add("Open rendering diagnostics", null, delegate { OpenRenderingDiagnostics(); });\n            menu.Items.Add("Refresh menu graphics", null, delegate { RefreshMenuGraphics(); });')
edit(base+'TaskbarTiles.cs', '''            Font oldFont = uiFont;
            uiFont = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            Font = uiFont;
            if (oldFont != null) oldFont.Dispose();
            if (headingFont != null) headingFont.Dispose();
            headingFont = new Font("Segoe UI", 14f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            if (tileFont != null) tileFont.Dispose();
            tileFont = new Font("Segoe UI", options.AppLabelFontSize * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            if (windowTitleFont != null) windowTitleFont.Dispose();
            windowTitleFont = new Font("Segoe UI", options.WindowTitleFontSize * scale, FontStyle.Regular, GraphicsUnit.Pixel);''', '''            ResetPaintRecovery();
            EnsureMenuFonts();''')
edit(base+'TaskbarTiles.cs', '''            float title = options.WindowTitleFontSize * scale, app = options.AppLabelFontSize * scale;
            if (windowTitleFont == null || Math.Abs(windowTitleFont.Size - title) > .01f)
            { if (windowTitleFont != null) windowTitleFont.Dispose(); windowTitleFont = new Font("Segoe UI", title, FontStyle.Regular, GraphicsUnit.Pixel); }
            if (tileFont == null || Math.Abs(tileFont.Size - app) > .01f)
            { if (tileFont != null) tileFont.Dispose(); tileFont = new Font("Segoe UI", app, FontStyle.Regular, GraphicsUnit.Pixel); }''', '            EnsureMenuFonts();')
edit(base+'TaskbarTiles.cs', '        protected override void OnPaint(PaintEventArgs e)\n', '        void PaintMenu(PaintEventArgs e)\n')
edit(base+'TaskbarTiles.cs', '''                    if (icon != null)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(icon, DrawingUtil.Fit(header.Icon, icon.Width, icon.Height));
                    }
                    else WindowHeaderGeometry.PaintFallback(g, header.Icon, accent);''', '''                    if (!DrawMenuImage(g, icon, header.Icon, "window title icon"))
                        WindowHeaderGeometry.PaintFallback(g, header.Icon, accent);''')
edit(base+'TaskbarTiles.cs', '''                if (app.Image != null)
                {
                    Rectangle imageRect = DrawingUtil.Fit(box, app.Image.Width, app.Image.Height);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (var attributes = new ImageAttributes())
                    {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(app.Image, imageRect, 0, 0, app.Image.Width, app.Image.Height, GraphicsUnit.Pixel, attributes);
                    }
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                }
                else''', '''                if (!DrawMenuImage(g, app.Image, box, "app launch icon"))''')
edit(base+'TaskbarTiles.cs', '            if (code == Keys.F5) { RefreshWindows(); RefreshApps(); return true; }', '            if (code == Keys.F5) { RefreshMenuGraphics(); RefreshWindows(); RefreshApps(); return true; }')
edit(base+'TaskbarTiles.cs', '            Color light = ForeColor, muted = Color.FromArgb(156, 171, 192), accent = Color.FromArgb(111, 193, 250);', '            paintPhase = "menu header";\n            Color light = ForeColor, muted = Color.FromArgb(156, 171, 192), accent = Color.FromArgb(111, 193, 250);')
edit(base+'TaskbarTiles.cs', '                bool hover = lastMouseHit == i, active = index == selected;', '                paintPhase = "window card";\n                bool hover = lastMouseHit == i, active = index == selected;')
edit(base+'TaskbarTiles.cs', '            PaintQuickAccess(g);\n', '            paintPhase = "footer";\n            PaintQuickAccess(g);\n')
edit(base+'TaskbarTiles.cs', '''                if (headingFont != null) headingFont.Dispose();
                if (uiFont != null) uiFont.Dispose();
                if (tileFont != null) tileFont.Dispose();
                if (windowTitleFont != null) windowTitleFont.Dispose();
                tip.Dispose();''', '''                tip.Dispose();''')
edit(base+'TaskbarTiles.cs', '''            base.Dispose(disposing);
        }
    }

    static class DrawingUtil''', '''            base.Dispose(disposing);
            // Inherited child-control fonts must outlive the controls themselves.
            if (disposing && menuFonts != null) { menuFonts.Dispose(); menuFonts = null; }
            if (disposing) rejectedPaintImages.Clear();
        }
    }

    static class DrawingUtil''')
edit(base+'QuickAccess.cs', '            Options oldOptions = options; Rectangle oldArea = area, oldBounds = Bounds;', '            EnsureMenuFonts(); MenuFonts liveFonts = menuFonts, previewFonts = null;\n            Options oldOptions = options; Rectangle oldArea = area, oldBounds = Bounds;')
edit(base+'QuickAccess.cs', '            var oldWindows = windows; var oldApps = apps; Font oldUi = uiFont, oldHeading = headingFont, oldTile = tileFont, oldTitle = windowTitleFont, oldFont = Font;', '            var oldWindows = windows; var oldApps = apps;')
edit(base+'QuickAccess.cs', '''                uiFont = new Font("Segoe UI", 12 * scale, GraphicsUnit.Pixel); headingFont = new Font("Segoe UI", 14 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
                tileFont = new Font("Segoe UI", options.AppLabelFontSize * scale, GraphicsUnit.Pixel);
                windowTitleFont = new Font("Segoe UI", options.WindowTitleFontSize * scale, GraphicsUnit.Pixel); Font = uiFont;''', '''                previewFonts = new MenuFonts(options, scale); BindMenuFonts(previewFonts);''')
edit(base+'QuickAccess.cs', '''                using (var g = Graphics.FromImage(image)) { g.Clear(BackColor); OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, image.Size))); }
                return image;''', '''                try
                {
                    using (var g = Graphics.FromImage(image)) { g.Clear(BackColor); OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, image.Size))); }
                    return image;
                }
                catch { image.Dispose(); throw; }''')
edit(base+'QuickAccess.cs', '''                Font = oldFont; searchBox.Font = oldFont;
                if (uiFont != oldUi) uiFont.Dispose(); if (headingFont != oldHeading) headingFont.Dispose(); if (tileFont != oldTile) tileFont.Dispose(); if (windowTitleFont != oldTitle && windowTitleFont != null) windowTitleFont.Dispose();
                uiFont = oldUi; headingFont = oldHeading; tileFont = oldTile; windowTitleFont = oldTitle;''', '''                BindMenuFonts(liveFonts);
                if (previewFonts != null) previewFonts.Dispose();''')
edit(base+'TaskbarTiles.csproj', '    <Compile Include="WindowActivation.cs" />', '    <Compile Include="Rendering.cs" />\n    <Compile Include="RenderingTests.cs" />\n    <Compile Include="WindowActivation.cs" />')
for name in ['AssemblyInfo.cs', 'app.manifest']:
    s = read(base+name)
    if '0.7.4' not in s: raise RuntimeError('Version anchor missing: ' + name)
    write(base+name, s.replace('0.7.4', '0.7.5'))
write('version.txt', '0.7.5\n')
for filename in ['.github/workflows/build.yml', '.github/workflows/release.yml']:
    edit(filename, '      - name: Test native switcher topmost ordering', '      - name: Test real menu rendering and recovery\n        shell: powershell\n        run: ./tools/Test-Rendering.ps1\n      - name: Test native switcher topmost ordering')
edit('.github/workflows/release.yml', "            helper_tests = 'passed'", "            helper_tests = 'passed'\n            native_rendering_tests = 'passed: repeated real-menu/preview rendering, disposed-font reproduction, icon isolation and bounded recovery'")
edit('CHANGELOG.md', '# Changelog\n\n## 0.7.4', '''# Changelog

## 0.7.5

- Fix font lifetime across repeated opens and settings previews: WinForms may retain an equal old Font, so detach control bindings before disposing its owner. Reuse unchanged font generations.
- Isolate bad icons and contain managed paint failures before WinForms latches its white/red-X error surface. Retry resource rebuild at most twice per open, with F5/tray graphics refresh and local rendering diagnostics.
- Native regressions reproduce the old disposed-font defect and exercise the real menu paint/preview path, equal metrics, font/scale changes, damaged icons, persistent failures and dismissal safety.
- Keep app-managed launch outcomes, exact-window switching, topmost handling, updater checks and existing user configuration unchanged.

## 0.7.4''')
print('Applied 0.7.5 exact font-lifetime and paint-recovery edits; Windows regression tests required.')
