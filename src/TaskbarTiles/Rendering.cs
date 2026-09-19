// Owned font generations and a bounded paint recovery boundary. UI-thread use only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class FontBinding
    {
        internal static void Assign(Control control, Font font)
        {
            if (ReferenceEquals(control.Font, font)) return;
            // Control.Font compares VALUES, not references. Reset the local value
            // before rebinding an equal-but-distinct font whose predecessor will die.
            control.Font = null;
            if (font != null) control.Font = font;
        }
    }
    sealed class MenuFonts : IDisposable
    {
        internal Font Ui, Heading, App, Title;
        readonly float scale;
        readonly int appSize, titleSize;
        bool disposed;
        internal MenuFonts(Options o, float scale)
        {
            this.scale = scale; appSize = o.AppLabelFontSize; titleSize = o.WindowTitleFontSize;
            try
            {
                Ui = new Font("Segoe UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                Heading = new Font("Segoe UI", 14 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
                App = new Font("Segoe UI", appSize * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                Title = new Font("Segoe UI", titleSize * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            catch { Dispose(); throw; }
        }
        internal bool Matches(Options o, float s)
        { return !disposed && scale == s && appSize == o.AppLabelFontSize && titleSize == o.WindowTitleFontSize; }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (Ui != null) Ui.Dispose(); if (Heading != null) Heading.Dispose();
            if (App != null) App.Dispose(); if (Title != null) Title.Dispose();
        }
    }
    static class RenderingLog
    {
        internal static string FilePath { get { return Path.Combine(Program.Home, "rendering-diagnostics.log"); } }
        [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flags);
        internal static bool Fatal(Exception ex)
        { return ex is StackOverflowException || ex is AccessViolationException || ex is ThreadAbortException; }
        internal static void Write(string phase, Exception ex, Size size, float scale, int windows, int apps)
        {
            try
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 262144)
                { File.Copy(FilePath, FilePath + ".previous", true); File.Delete(FilePath); }
                uint gdi = 0; using (var p = Process.GetCurrentProcess()) gdi = GetGuiResources(p.Handle, 0);
                // No titles, search queries, screenshots, command arguments or app paths.
                string text = DateTime.UtcNow.ToString("o") + " version=" + Program.Version + " phase=" + phase +
                    " size=" + size.Width + "x" + size.Height + " scale=" + scale + " windows=" + windows + " apps=" + apps + " gdi=" + gdi;
                if (ex != null) text += " exception=" + ex.GetType().FullName + " hresult=0x" + ex.HResult.ToString("X8") + Environment.NewLine + ex.StackTrace;
                File.AppendAllText(FilePath, text + Environment.NewLine);
            }
            catch { /* Diagnostics must not create a second paint failure. */ }
        }
    }
    sealed partial class Switcher
    {
        MenuFonts menuFonts;
        bool paintFault, paintRecoveryQueued;
        int paintRecoveryAttempts;
        string paintPhase = "menu";
        readonly HashSet<Image> rejectedPaintImages = new HashSet<Image>();
        const int MaximumPaintRecoveries = 2;

        void BindMenuFonts(MenuFonts next)
        {
            // Both controls must release the old font BEFORE its owner disposes it.
            FontBinding.Assign(this, next.Ui); FontBinding.Assign(searchBox, next.Ui);
            uiFont = next.Ui; headingFont = next.Heading; tileFont = next.App; windowTitleFont = next.Title;
            menuFonts = next;
        }
        void EnsureMenuFonts(bool force = false)
        {
            if (!force && menuFonts != null && menuFonts.Matches(options, scale)) return;
            var next = new MenuFonts(options, scale); var old = menuFonts;
            try { BindMenuFonts(next); }
            catch
            {
                if (old != null) BindMenuFonts(old);
                else { FontBinding.Assign(searchBox, null); FontBinding.Assign(this, null); }
                next.Dispose(); throw;
            }
            if (old != null) old.Dispose();
        }
        void ResetPaintRecovery()
        { paintFault = false; paintRecoveryAttempts = 0; rejectedPaintImages.Clear(); }
        void RefreshMenuGraphics()
        {
            ResetPaintRecovery();
            if (!Visible || transient != null || closing) return;
            try { EnsureMenuFonts(true); LayoutMenu(); }
            catch (Exception ex) { if (RenderingLog.Fatal(ex)) throw; RecordPaintFailure(ex); }
        }
        void OpenRenderingDiagnostics()
        {
            if (!File.Exists(RenderingLog.FilePath)) RenderingLog.Write("no drawing errors recorded", null, ClientSize, scale, windows.Count, apps.Count);
            OpenFile(RenderingLog.FilePath);
        }
        bool DrawMenuImage(Graphics graphics, Image image, Rectangle box, string kind)
        {
            if (image == null || rejectedPaintImages.Contains(image)) return false;
            GraphicsState state = null;
            try
            {
                state = graphics.Save();
                // Access dimensions inside the boundary: a disposed image fails here.
                Rectangle fit = DrawingUtil.Fit(box, image.Width, image.Height);
                if (fit.IsEmpty) return false;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, fit, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (RenderingLog.Fatal(ex)) throw;
                if (rejectedPaintImages.Count < 256) rejectedPaintImages.Add(image);
                RenderingLog.Write(kind, ex, ClientSize, scale, windows.Count, apps.Count);
                return false; // One bad icon gets a neutral fallback, not a broken form.
            }
            finally
            {
                try { if (state != null) graphics.Restore(state); }
                catch (Exception ex) { if (RenderingLog.Fatal(ex)) throw; RenderingLog.Write("image graphics state", ex, ClientSize, scale, windows.Count, apps.Count); }
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            try { base.OnPaintBackground(e); }
            catch (Exception ex)
            {
                if (RenderingLog.Fatal(ex) || renderingPreview) throw;
                RecordPaintFailure(ex); PaintRecoveryMessage(e.Graphics);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            // Direct preview calls must report failure to Settings and dispose their
            // temporary bitmap, not schedule recovery against the hidden live menu.
            if (renderingPreview) { PaintMenu(e); return; }
            if (paintFault) { PaintRecoveryMessage(e.Graphics); return; }
            GraphicsState state = null;
            try
            {
                state = e.Graphics.Save();
                paintPhase = "menu"; PaintMenu(e);
            }
            catch (Exception ex)
            {
                if (RenderingLog.Fatal(ex)) throw;
                RecordPaintFailure(ex);
                PaintRecoveryMessage(e.Graphics);
            }
            finally
            {
                try { if (state != null) e.Graphics.Restore(state); }
                catch (Exception ex) { if (RenderingLog.Fatal(ex)) throw; RecordPaintFailure(ex); }
            }
        }
        void RecordPaintFailure(Exception ex)
        {
            paintFault = true;
            RenderingLog.Write(paintPhase, ex, ClientSize, scale, windows.Count, apps.Count);
            try { ClearThumbnails(); } catch { }
            if (paintRecoveryQueued || paintRecoveryAttempts >= MaximumPaintRecoveries || closing || IsDisposed || !IsHandleCreated) return;
            paintRecoveryQueued = true;
            try { BeginInvoke(new Action(RecoverPaint)); }
            catch (InvalidOperationException) { paintRecoveryQueued = false; }
        }
        void RecoverPaint()
        {
            paintRecoveryQueued = false;
            // Never reopen after a user dismissed the popup or left for Settings.
            if (closing || IsDisposed || !Visible || renderingPreview || transient != null || !paintFault) return;
            if (paintRecoveryAttempts >= MaximumPaintRecoveries) return;
            paintRecoveryAttempts++;
            try
            {
                EnsureMenuFonts(true); LayoutMenu(); paintFault = false;
                RenderingLog.Write("resources rebuilt; attempt=" + paintRecoveryAttempts, null, ClientSize, scale, windows.Count, apps.Count);
                Invalidate(); // Asynchronous: no recursion or refresh loop inside paint.
            }
            catch (Exception ex)
            {
                if (RenderingLog.Fatal(ex)) throw;
                RenderingLog.Write("recovery failed", ex, ClientSize, scale, windows.Count, apps.Count);
                paintFault = true; Invalidate();
            }
        }
        void PaintRecoveryMessage(Graphics graphics)
        {
            try
            {
                graphics.Clear(Theme.Background);
                TextRenderer.DrawText(graphics,
                    "Display interrupted. Press F5 to retry, or Esc to close.\nDetails are in the tray menu's Open rendering diagnostics.",
                    SystemFonts.MessageBoxFont, Rectangle.Inflate(ClientRectangle, -24, -24), Theme.Text,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                PaintAction(graphics, closeRect, -1, "close");
            }
            catch (Exception ex) { if (RenderingLog.Fatal(ex)) throw; }
        }
    }
}
