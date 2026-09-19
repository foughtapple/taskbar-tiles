// Rendering safety: retain fonts while controls use them; isolate optional images.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class FontBinding
    {
        // Control.Font compares values, not references. Assigning an equal new Font
        // can leave the old reference installed. Detach before retiring that object.
        internal static void Assign(Control control, Font font)
        {
            if (control == null || control.IsDisposed) return;
            if (ReferenceEquals(control.Font, font)) return;
            control.Font = null;
            control.Font = font;
        }
        internal static Font ForSize(Font current, float size, FontStyle style)
        {
            if (current != null && Math.Abs(current.Size - size) < .01f && current.Style == style && current.Unit == GraphicsUnit.Pixel)
                return current;
            return new Font("Segoe UI", Math.Max(1, size), style, GraphicsUnit.Pixel);
        }
        internal static void Retire(Font previous, Font current)
        { if (previous != null && !ReferenceEquals(previous, current)) previous.Dispose(); }
    }

    static class RenderDiagnostics
    {
        static readonly object gate = new object();
        static DateTime last;
        static int count;
        [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flags);
        internal static string FilePath { get { return Path.Combine(Program.Home, "rendering-diagnostics.log"); } }
        internal static void Write(string stage, Exception ex)
        {
            lock (gate) try
            {
                if ((DateTime.UtcNow - last).TotalMinutes >= 1) { last = DateTime.UtcNow; count = 0; }
                if (count++ >= 16) return;
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 262144)
                { File.Copy(FilePath, FilePath + ".previous", true); File.Delete(FilePath); }
                uint gdi = 0, user = 0;
                using (var p = Process.GetCurrentProcess()) { gdi = GetGuiResources(p.Handle, 0); user = GetGuiResources(p.Handle, 1); }
                // No exception message, source paths, app titles, queries or input coordinates.
                string detail = ex == null ? "" : ex.GetType().FullName + " HRESULT=0x" + ex.HResult.ToString("X8") + Environment.NewLine + new StackTrace(ex, false);
                File.AppendAllText(FilePath, DateTime.UtcNow.ToString("o") + " version=" + Program.Version + " stage=" + stage + " GDI=" + gdi + " USER=" + user + Environment.NewLine + detail + Environment.NewLine);
            }
            catch { }
        }
    }

    static class RenderSafety
    {
        static readonly ConditionalWeakTable<Image, object> rejected = new ConditionalWeakTable<Image, object>();
        internal static bool Recoverable(Exception ex)
        { return !(ex is StackOverflowException || ex is AccessViolationException || ex is ThreadAbortException); }
        internal static bool DrawImage(Graphics graphics, Image image, Rectangle box)
        {
            if (image == null || box.Width < 1 || box.Height < 1) return false;
            object marker; if (rejected.TryGetValue(image, out marker)) return false;
            try
            {
                int width = image.Width, height = image.Height;
                if (width < 1 || height < 1) return false;
                var saved = graphics.Save();
                try
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (var attributes = new ImageAttributes())
                    {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        graphics.DrawImage(image, DrawingUtil.Fit(box, width, height), 0, 0, width, height, GraphicsUnit.Pixel, attributes);
                    }
                }
                finally { graphics.Restore(saved); }
                return true;
            }
            catch (Exception ex)
            {
                if (!Recoverable(ex)) throw;
                rejected.GetValue(image, delegate(Image key) { return new object(); });
                RenderDiagnostics.Write("optional-icon", ex);
                return false;
            }
        }
        internal static bool Paint(Action draw, Action<Exception> onFailure)
        {
            try { draw(); return true; }
            catch (Exception ex)
            {
                if (!Recoverable(ex)) throw;
                onFailure(ex); return false;
            }
        }
    }

    sealed partial class Switcher
    {
        int renderSession, renderAttempts, graphicsTransition;
        bool renderRecoveryQueued;
        void RefreshMenuFonts()
        {
            graphicsTransition++;
            Font oldUi = uiFont, oldHeading = headingFont, oldTile = tileFont, oldTitle = windowTitleFont;
            try
            {
                // Reuse equal fonts. This also avoids needless GDI allocations on every open.
                uiFont = FontBinding.ForSize(uiFont, 12f * scale, FontStyle.Regular);
                headingFont = FontBinding.ForSize(headingFont, 14f * scale, FontStyle.Bold);
                tileFont = FontBinding.ForSize(tileFont, options.AppLabelFontSize * scale, FontStyle.Regular);
                windowTitleFont = FontBinding.ForSize(windowTitleFont, options.WindowTitleFontSize * scale, FontStyle.Regular);
                FontBinding.Assign(this, uiFont); FontBinding.Assign(searchBox, uiFont);
                FontBinding.Retire(oldUi, uiFont); FontBinding.Retire(oldHeading, headingFont);
                FontBinding.Retire(oldTile, tileFont); FontBinding.Retire(oldTitle, windowTitleFont);
            }
            finally { graphicsTransition--; }
        }
        void StartRenderSession()
        { renderSession++; renderAttempts = 0; renderRecoveryQueued = false; }
        void EndRenderSession()
        { renderSession++; renderRecoveryQueued = false; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            RenderSafety.Paint(delegate { e.Graphics.Clear(BackColor); }, delegate(Exception ex) { QueueRenderRecovery(ex); });
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (closing || renderingPreview || graphicsTransition != 0 || headingFont == null || tileFont == null) return;
            bool ok = RenderSafety.Paint(delegate { base.OnPaint(e); PaintMenuContents(e.Graphics); }, QueueRenderRecovery);
            if (!ok)
            {
                // Never let one failed paint latch WinForms' permanent white/red-X state.
                RenderSafety.Paint(delegate
                {
                    e.Graphics.Clear(BackColor);
                    TextRenderer.DrawText(e.Graphics, "Refreshing the menu...  Esc closes it.", SystemFonts.MessageBoxFont,
                        new Rectangle(20, 12, Math.Max(1, Width - 40), 40), ForeColor, TextFormatFlags.EndEllipsis);
                }, delegate(Exception ex) { RenderDiagnostics.Write("fallback-paint", ex); });
            }
        }
        void QueueRenderRecovery(Exception ex)
        {
            RenderDiagnostics.Write("menu-paint", ex);
            if (closing || renderingPreview || !Visible || transient != null || renderRecoveryQueued || !IsHandleCreated) return;
            renderRecoveryQueued = true; int session = renderSession; renderAttempts++;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (session != renderSession || closing || IsDisposed || !Visible || transient != null) return;
                    renderRecoveryQueued = false;
                    if (renderAttempts > 2)
                    { Dismiss(); Notify("Menu drawing could not recover. Reopen the menu or use Repair menu rendering from the tray. Your settings are unchanged; see rendering diagnostics."); return; }
                    try { RepairMenuRendering(false); }
                    catch (Exception fault) { RenderDiagnostics.Write("menu-recovery", fault); Dismiss(); Notify("Menu drawing was stopped safely. See rendering diagnostics. Settings were not changed."); }
                }));
            }
            catch (InvalidOperationException) { renderRecoveryQueued = false; }
        }
        void RepairMenuRendering(bool manual)
        {
            if (closing || renderingPreview || transient != null) return;
            if (manual) StartRenderSession();
            graphicsTransition++;
            try
            {
                ClearThumbnails();
                if (headerIcons != null) headerIcons.ClearCache();
                foreach (var app in allApps) { var image = app.Image; app.Image = null; if (image != null) image.Dispose(); }
                // Detach controls before releasing any font, including value-equal ones.
                FontBinding.Assign(this, SystemFonts.MessageBoxFont); FontBinding.Assign(searchBox, SystemFonts.MessageBoxFont);
                var retired = new HashSet<Font>(new[] { uiFont, headingFont, tileFont, windowTitleFont });
                uiFont = headingFont = tileFont = windowTitleFont = null;
                foreach (var font in retired) if (font != null) font.Dispose();
                RefreshMenuFonts();
                if (area.Width > 0 && area.Height > 0) LayoutMenu();
                RefreshApps(); Invalidate();
            }
            finally { graphicsTransition--; }
        }
        void OpenRenderingDiagnostics()
        { if (!File.Exists(RenderDiagnostics.FilePath)) RenderDiagnostics.Write("No rendering failures recorded", null); OpenFile(RenderDiagnostics.FilePath); }
        void ReleaseMenuFonts()
        {
            FontBinding.Assign(this, SystemFonts.MessageBoxFont); FontBinding.Assign(searchBox, SystemFonts.MessageBoxFont);
            foreach (var font in new HashSet<Font>(new[] { uiFont, headingFont, tileFont, windowTitleFont })) if (font != null) font.Dispose();
            uiFont = headingFont = tileFont = windowTitleFont = null;
        }
    }
}
