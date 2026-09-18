// Normal Windows minimise requests only. No F11, forced termination or injection.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace TaskbarTiles
{
    static class FullscreenLogic
    {
        internal static bool Covers(Rectangle r, Rectangle screen)
        {
            return r.Width > 0 && r.Height > 0 && screen.Width > 0 && screen.Height > 0 &&
                r.Left <= screen.Left + 3 && r.Top <= screen.Top + 3 && r.Right >= screen.Right - 3 && r.Bottom >= screen.Bottom - 3;
        }
        internal static bool ShouldMinimize(Rectangle frame, Rectangle client, Rectangle screen, bool caption, bool maximised, bool exclusiveDirect3D)
        {
            if (caption && maximised) return false; // Do not treat ordinary maximised windows as fullscreen.
            bool f = Covers(frame, screen), c = Covers(client, screen);
            if (!f && !c) return false;
            return !caption || c || (exclusiveDirect3D && f);
        }
    }
    static class FullscreenWindows
    {
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out Native.RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref Native.POINT p);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        internal static bool RequestMinimize(IntPtr foreground, IntPtr switcher)
        {
            try
            {
                if (foreground == IntPtr.Zero || foreground == switcher || !Native.IsWindow(foreground) || !Native.IsWindowVisible(foreground) || Native.IsIconic(foreground)) return false;
                if (WindowNative.ProcessId(foreground) == (uint)Process.GetCurrentProcess().Id) return false;
                string cls = Native.Class(foreground);
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "Progman" || cls == "WorkerW" || cls == "#32768") return false;
                if ((Native.GetWindowLongPtr(foreground, -20).ToInt64() & (0x80 | 0x08000000)) != 0) return false;
                int cloaked; if (Native.DwmGetWindowAttribute(foreground, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
                Native.RECT r; Rectangle client = Rectangle.Empty;
                if (GetClientRect(foreground, out r))
                {
                    var a = new Native.POINT(r.left, r.top); var b = new Native.POINT(r.right, r.bottom);
                    if (ClientToScreen(foreground, ref a) && ClientToScreen(foreground, ref b)) client = Rectangle.FromLTRB(a.x, a.y, b.x, b.y);
                }
                bool caption = (Native.GetWindowLongPtr(foreground, -16).ToInt64() & 0x00C00000) == 0x00C00000;
                int state; bool exclusive = SHQueryUserNotificationState(out state) == 0 && state == 3;
                if (!FullscreenLogic.ShouldMinimize(WindowNative.VisibleBounds(foreground), client, Screen.FromHandle(foreground).Bounds, caption, WindowNative.IsZoomed(foreground), exclusive)) return false;
                if (Native.GetForegroundWindow() != foreground) return false;
                bool queued = Native.ShowWindowAsync(foreground, 6); // SW_MINIMIZE, not force-minimise.
                if (!queued) Program.Log("Fullscreen: Windows rejected the minimise request.");
                return queued;
            }
            catch (Exception ex) { Program.Log("Fullscreen minimise: " + ex.Message); return false; }
        }
    }
    sealed partial class Switcher
    {
        readonly System.Windows.Forms.Timer fullscreenTimer = new System.Windows.Forms.Timer { Interval = 25 };
        bool fullscreenOpening, openingReverse, acceptAfterFullscreenOpen;
        int openingCycles;
        IntPtr openingForeground;
        uint openingProcess;
        DateTime openingDeadline;
        void SetupFullscreen()
        {
            fullscreenTimer.Tick += delegate
            {
                try
                {
                    bool valid = Native.IsWindow(openingForeground) && WindowNative.ProcessId(openingForeground) == openingProcess;
                    bool minimised = valid && Native.IsIconic(openingForeground);
                    if (valid && !minimised && DateTime.UtcNow < openingDeadline) return;
                    fullscreenTimer.Stop(); fullscreenOpening = false;
                    if (closing) return;
                    if (valid && !minimised) Program.Log("Fullscreen: minimise was not confirmed before timeout; no further requests will be sent.");
                    int cycles = openingCycles; bool accept = acceptAfterFullscreenOpen;
                    bool reverse = openingReverse; IntPtr original = openingForeground;
                    openingCycles = 0; acceptAfterFullscreenOpen = false; openingForeground = IntPtr.Zero;
                    // Exclusive games may change resolution on minimising; read screen bounds after the transition.
                    ShowMenuCore(reverse, original);
                    if (cycles != 0) MoveSelection(cycles);
                    if (accept && Visible) AcceptWindow();
                }
                catch (Exception ex)
                {
                    CancelFullscreenOpen(); Program.Log("Fullscreen transition: " + ex);
                    hook.Enabled = false; interceptItem.Checked = false;
                    Notify("The fullscreen transition failed. Native Alt+Tab has been restored; see TaskbarTiles.log.");
                }
            };
        }
        bool StartFullscreenOpen(IntPtr foreground, bool reverse)
        {
            if (!options.MinimizeFullscreenOnOpen || !FullscreenWindows.RequestMinimize(foreground, Handle)) return false;
            openingForeground = foreground; openingProcess = WindowNative.ProcessId(foreground);
            openingReverse = reverse; openingCycles = 0; acceptAfterFullscreenOpen = false;
            openingDeadline = DateTime.UtcNow.AddMilliseconds(600); fullscreenOpening = true; fullscreenTimer.Start(); return true;
        }
        void AltReleased()
        {
            if (fullscreenOpening) { acceptAfterFullscreenOpen = !stickySession; return; }
            if (Visible && !stickySession) AcceptWindow();
        }
        void CancelFullscreenOpen()
        { fullscreenTimer.Stop(); fullscreenOpening = false; openingCycles = 0; openingForeground = IntPtr.Zero; acceptAfterFullscreenOpen = false; }
        void ShutdownFullscreen() { CancelFullscreenOpen(); fullscreenTimer.Dispose(); }
        void MoveVerticalSelection(int direction)
        {
            if (windows.Count == 0 || cardRects.Count == 0) return;
            int index = selected - windowPage * perWindowPage;
            if (index < 0 || index >= cardRects.Count) return;
            double x = (cardRects[index].Left + cardRects[index].Width / 2.0) / Math.Max(1, Width);
            int next = BalancedGrid.VerticalNeighbour(cardRects, index, direction);
            if (next >= 0) { selected = windowPage * perWindowPage + next; Invalidate(); return; }
            int pages = PageCount(windows.Count, perWindowPage); if (pages == 1) return;
            int page = (windowPage + Math.Sign(direction) + pages) % pages;
            selected = page * perWindowPage; LayoutMenu();
            int y = direction > 0 ? cardRects.Min(r => r.Top) : cardRects.Max(r => r.Top);
            next = BalancedGrid.NearestOnRow(cardRects, y, x * Width);
            if (next >= 0) { selected = windowPage * perWindowPage + next; Invalidate(); }
        }
    }
}
