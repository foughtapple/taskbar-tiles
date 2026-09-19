// Taskbar Tiles 0.6 - native Windows actions, favourites and side-effect-free previews.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class QuickAccessLayout
    {
        internal Rectangle Search, Favourites, Desktop, Clipboard, Help;
        internal static bool Enabled(Options o) { return o.WindowsSearchButton || o.FavouritesButton || o.DesktopButton || o.ClipboardButton; }
        internal static QuickAccessLayout Build(int width, int height, float scale, Options o)
        {
            var result = new QuickAccessLayout(); if (!Enabled(o)) return result;
            Func<int, int> s = n => Math.Max(1, (int)Math.Round(n * scale));
            int pad = s(22), gap = s(8), h = s(o.FooterButtonHeight), y = height - h - s(12), left = pad, right = width - pad;
            if (o.FavouritesButton) { result.Favourites = new Rectangle(right - s(156), y, s(156), h); right = result.Favourites.Left - gap; }
            if (o.WindowsSearchButton) { result.Search = new Rectangle(left, y, s(164), h); left = result.Search.Right + gap; }
            if (o.DesktopButton) { result.Desktop = new Rectangle(left, y, s(36), h); left = result.Desktop.Right + gap; }
            if (o.ClipboardButton) { result.Clipboard = new Rectangle(left, y, s(36), h); left = result.Clipboard.Right + gap; }
            if (right - left > s(55)) result.Help = new Rectangle(left, y, s(36), h);
            return result;
        }
        internal IEnumerable<Rectangle> Buttons()
        { return new[] { Search, Favourites, Desktop, Clipboard, Help }.Where(r => !r.IsEmpty); }
        internal int Hit(Point p)
        {
            if (!Search.IsEmpty && Search.Contains(p)) return -12; if (!Favourites.IsEmpty && Favourites.Contains(p)) return -13;
            if (!Desktop.IsEmpty && Desktop.Contains(p)) return -14; if (!Clipboard.IsEmpty && Clipboard.Contains(p)) return -15;
            if (!Help.IsEmpty && Help.Contains(p)) return -16; return -100;
        }
    }
    sealed partial class Switcher
    {
        QuickAccessLayout quick = new QuickAccessLayout();
        readonly System.Windows.Forms.Timer windowsActionTimer = new System.Windows.Forms.Timer { Interval = 40 };
        ushort windowsActionKey;
        DateTime windowsActionStart;
        bool renderingPreview;
        int QuickAccessExtra { get { return QuickAccessLayout.Enabled(options) ? S(options.FooterButtonHeight + 16) : 0; } }
        void SetupQuickAccess()
        {
            windowsActionTimer.Tick += delegate
            {
                if (windowsActionKey == 0) { windowsActionTimer.Stop(); return; }
                if (Native.LaunchModifiersDown() || Native.Down(0x10) || Native.Down(windowsActionKey))
                {
                    if ((DateTime.UtcNow - windowsActionStart).TotalSeconds > 4)
                    { windowsActionKey = 0; windowsActionTimer.Stop(); Notify("Release the keyboard modifiers, then try that Windows button again."); }
                    return;
                }
                ushort key = windowsActionKey; windowsActionKey = 0; windowsActionTimer.Stop();
                try { Native.WindowsShortcut(key); } catch (Exception ex) { Notify(ex.Message); }
            };
        }
        void ArrangeQuickAccess() { quick = QuickAccessLayout.Build(Width, Height, scale, options); }
        int HitQuickAccess(Point p) { return quick.Hit(p); }
        string QuickAccessTip(int hit)
        {
            if (hit == -12) return "Search here — installed apps, open windows, favourites, settings and indexed filenames (Ctrl+Shift+S)";
            if (hit == -13) return "Favourites — apps, folders and websites you choose in Settings (Ctrl+Space here)";
            if (hit == -14) return "Show the desktop (Win+D)";
            if (hit == -15) return "Windows clipboard history (Win+V). Taskbar Tiles never reads your clipboard.";
            return "Keyboard shortcuts (F1)";
        }
        void PaintQuickAccess(Graphics g)
        {
            if (QuickAccessExtra == 0) return;
            int line = Height - S(options.FooterButtonHeight + 22);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, S(22), line, Width - S(22), line);
            PaintQuickButton(g, quick.Search, -12, "Search…", "search");
            PaintQuickButton(g, quick.Favourites, -13, "Favourites", "star");
            PaintQuickButton(g, quick.Desktop, -14, "", "desktop");
            PaintQuickButton(g, quick.Clipboard, -15, "", "clipboard");
            PaintQuickButton(g, quick.Help, -16, "", "help");
        }
        void PaintQuickButton(Graphics g, Rectangle r, int hit, string title, string glyph)
        {
            if (r.IsEmpty) return;
            DrawingUtil.Round(g, r, S(9), lastMouseHit == hit ? Color.FromArgb(45, 65, 90) : Theme.Card,
                lastMouseHit == hit ? Theme.Accent : Theme.Border, 1);
            float x = title.Length == 0 ? r.Left + r.Width / 2f : r.Left + S(22), y = r.Top + r.Height / 2f, d = S(6);
            using (var pen = new Pen(lastMouseHit == hit ? Theme.Accent : Theme.Text, Math.Max(1.4f, scale * 1.6f)))
            {
                if (glyph == "search") { g.DrawEllipse(pen, x - d, y - d, d * 1.6f, d * 1.6f); g.DrawLine(pen, x + d * .4f, y + d * .4f, x + d, y + d); }
                else if (glyph == "star")
                {
                    var points = new PointF[10];
                    for (int i = 0; i < 10; i++) { double a = -Math.PI / 2 + i * Math.PI / 5; double radius = (i % 2 == 0 ? 1.3 : .55) * d; points[i] = new PointF(x + (float)(Math.Cos(a) * radius), y + (float)(Math.Sin(a) * radius)); }
                    g.DrawPolygon(pen, points);
                }
                else if (glyph == "desktop") { g.DrawRectangle(pen, x - d, y - d, d * 2, d * 1.5f); g.DrawLine(pen, x, y + d * .5f, x, y + d); g.DrawLine(pen, x - d * .6f, y + d, x + d * .6f, y + d); }
                else if (glyph == "clipboard") { g.DrawRectangle(pen, x - d * .8f, y - d, d * 1.6f, d * 2); g.DrawRectangle(pen, x - d * .4f, y - d * 1.2f, d * .8f, d * .5f); }
                else Label(g, "?", r, true, Theme.Muted, true);
            }
            if (title.Length > 0) Label(g, title, new Rectangle(r.Left + S(42), r.Top, r.Width - S(49), r.Height), false, Theme.Text, false);
        }
        void ExecuteQuickAccess(int hit)
        {
            if (hit == -12) { ShowIntegratedSearch(); return; }
            if (hit == -13) { ShowFavourites(); return; }
            if (hit == -16) { ShowShortcuts(); return; }
            if (pending != null || launchPlacement != null || mover.Busy) { Notify("Finish the current window action first."); return; }
            windowsActionKey = hit == -14 ? (ushort)0x44 : (ushort)0x56;
            windowsActionStart = DateTime.UtcNow; Dismiss(); windowsActionTimer.Start();
        }
        bool QuickAccessKey(Keys keys)
        {
            if (keys == Keys.F1) { ShowShortcuts(); return true; }
            if (!options.LocalHotkeys) return false;
            if (keys == (Keys.Control | Keys.Space) && options.FavouritesButton) { ShowFavourites(); return true; }
            if (keys == (Keys.Control | Keys.Shift | Keys.S) && options.WindowsSearchButton) { ExecuteQuickAccess(-12); return true; }
            if (keys == (Keys.Control | Keys.Oemcomma)) { ShowSettings(); return true; }
            if (keys == (Keys.Control | Keys.Z) && options.EnableUndoMove && !searchBox.Focused) { UndoMove(); return true; }
            if (keys == (Keys.Shift | Keys.Enter) && options.RightClickZones && windows.Count > 0)
            { ChooseZone(windows[Math.Max(0, Math.Min(selected, windows.Count - 1))], null); return true; }
            return false;
        }
        void ShowShortcuts()
        {
            bool reopen = Visible; Dismiss();
            MessageBox.Show("Main menu\n\nCtrl+Space — Favourites\nCtrl+Shift+S — Search inside Taskbar Tiles\nCtrl+, — Settings\nCtrl+F / typing — filter windows and taskbar apps\nF5 — Refresh\nShift+Enter — place selected window into a zone\nCtrl+Z — undo a window move (outside the search field)\nEsc — clear the filter, then close\n\nSearch here\n\nType — search apps, windows, favourites and indexed filenames\nUp / Down — select · Enter — open · Right-click — choose a zone\nEsc — clear the query, then return to the menu\n\nFavourites\n\nUp / Down — choose an item\nEnter — open   •   Shift+Enter / right-click — choose a zone\nAlt+1 … Alt+9 — open a visible favourite\nPage Up / Page Down / wheel — next page\n\nMouse\n\nCtrl+wheel — resize the section under the mouse\nMiddle-click a window to close it, when enabled.\n\nOptional shortcuts can be turned off in Settings → Quick access.",
                "Taskbar Tiles - shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (reopen && !closing) OpenOrCycle(true, false, false);
        }
        void ShowFavourites()
        {
            if (!options.FavouritesButton || transient != null) return;
            if (pending != null || launchPlacement != null || mover.Busy) { Notify("Finish the current launch or window move first."); return; }
            Rectangle anchor = Bounds;
            Dismiss(); FavouriteEntry chosen = null; bool place = false, returnToMenu = false;
            while (!closing)
            {
                bool settings;
                using (var form = new FavouritesWindow(options, FavouriteStore.Load(), anchor))
                {
                    transient = form;
                    try { form.ShowDialog(); } finally { transient = null; }
                    chosen = form.Chosen; place = form.PlaceInZone; settings = form.SettingsRequested; returnToMenu = form.ReturnToMenu;
                }
                if (!settings) break;
                if (!SettingsCore("Favourites")) return;
                if (!options.FavouritesButton) break;
            }
            if (closing) return;
            if (chosen == null) { if (returnToMenu) OpenOrCycle(true, false, false); return; }
            var app = FavouriteLaunch.AsApp(chosen);
            if (place) ChooseZone(null, app); else QueueLaunch(app, null);
        }
        void DispatchAppLaunch(AppButton app, LaunchOperation operation, Action<LaunchReceipt, string> completed)
        { ReliableLauncher.Start(app, options.Clone(), reader, operation, completed); }
        void ShutdownQuickAccess() { DisposeIntegratedSearch(); windowsActionTimer.Stop(); windowsActionTimer.Dispose(); }

        // This reuses the real menu's layout and paint path, but the form stays hidden.
        // There are no simulated clicks, launches, window moves, focus changes or saves.
        string lastMenuPreviewSummary = "";
        Bitmap CaptureMenuPreview(Options draft)
        {
            if (Visible) throw new InvalidOperationException("Close the switcher before requesting a settings preview.");
            EnsureMenuFonts(); MenuFonts liveFonts = menuFonts, previewFonts = null;
            Options oldOptions = options; Rectangle oldArea = area, oldBounds = Bounds;
            float oldScale = scale; int oldSelected = selected, oldWindowPage = windowPage, oldAppPage = appPage;
            var oldWindows = windows; var oldApps = apps;
            string oldQuery = searchBox.Text;
            try
            {
                renderingPreview = true; options = draft.Clone(); options.Validate();
                area = Screen.FromPoint(monitorPoint).WorkingArea;
                scale = MenuGeometry.ScaleFor(options, area.Size, Native.ScaleAt(monitorPoint));
                previewFonts = new MenuFonts(options, scale); BindMenuFonts(previewFonts);
                updatingSearch = true; searchBox.Text = ""; updatingSearch = false;
                windows = allWindows.Where(w => !options.CurrentMonitorOnly || Screen.FromHandle(w.Handle).DeviceName == Screen.FromPoint(monitorPoint).DeviceName).ToList(); apps = allApps.ToList();
                if (windows.Count == 0) windows = Enumerable.Range(1, 4).Select(n => new WindowItem { Title = "Example window " + n, Handle = IntPtr.Zero }).ToList();
                if (apps.Count == 0) apps = new[] { "Browser", "Files", "Editor", "Terminal", "Settings", "Music" }.Select(n => new AppButton { Name = n, DisplayName = n }).ToList();
                selected = windowPage = appPage = 0; LayoutMenu();
                lastMenuPreviewSummary = menuGeometry.Summary(options);
                var image = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
                try
                {
                    using (var g = Graphics.FromImage(image)) { g.Clear(BackColor); OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, image.Size))); }
                    return image;
                }
                catch { image.Dispose(); throw; }
            }
            finally
            {
                BindMenuFonts(liveFonts);
                if (previewFonts != null) previewFonts.Dispose();
                options = oldOptions; scale = oldScale; area = oldArea; windows = oldWindows; apps = oldApps;
                selected = oldSelected; windowPage = oldWindowPage; appPage = oldAppPage; Bounds = oldBounds;
                updatingSearch = true; searchBox.Text = oldQuery; updatingSearch = false; searchBox.Visible = false;
                renderingPreview = false; quick = new QuickAccessLayout();
            }
        }
        void PaintPreviewSearch(Graphics g)
        {
            DrawingUtil.Round(g, searchBox.Bounds, S(5), Theme.Card, Theme.Border, 1);
            Label(g, "Filter open windows and taskbar apps...", Rectangle.Inflate(searchBox.Bounds, -S(9), 0), false, Theme.Muted, false);
        }
        void PaintExampleWindow(Graphics g, Rectangle box, int variant)
        {
            if (box.Width <= S(20) || box.Height <= S(16)) return;
            var r = DrawingUtil.Fit(Rectangle.Inflate(box, -S(8), -S(5)), 16, 9);
            DrawingUtil.Round(g, r, S(3), Color.FromArgb(27 + variant % 3 * 3, 36, 48), Color.FromArgb(51, 66, 85), 1);
            using (var brush = new SolidBrush(Color.FromArgb(74, 95, 119)))
            {
                int top = r.Top + Math.Max(4, r.Height / 8), left = r.Left + Math.Max(5, r.Width / 12);
                for (int i = 0; i < 4; i++)
                { int w = Math.Max(2, r.Width * (i % 2 == 0 ? 68 : 48) / 100); g.FillRectangle(brush, left, top + i * Math.Max(3, r.Height / 7), w, Math.Max(2, r.Height / 20)); }
            }
        }
    }
}
