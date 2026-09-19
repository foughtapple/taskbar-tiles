// Maintain this popup's Z order independently of the selected app's activation.
// Only our own HWND is raised. Never alter another app's topmost status or inject input.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class SwitcherLayerPolicy
    {
        internal const uint RaiseFlags = 0x0001 | 0x0002 | 0x0010 | 0x0200;
        internal static bool CanRaise(bool presenting, bool visible, bool allowed, bool disposed)
        { return presenting && visible && allowed && !disposed; }
        internal static bool OwnedBy(IntPtr candidate, IntPtr owner)
        {
            for (int depth = 0; depth < 32 && candidate != IntPtr.Zero; depth++)
            {
                candidate = Native.GetWindow(candidate, 4); // GW_OWNER, not another same-PID window.
                if (candidate == owner) return true;
            }
            return false;
        }
        internal static bool HasTopmostStyle(IntPtr h)
        { return (Native.GetWindowLongPtr(h, -20).ToInt64() & 0x00000008L) != 0; }
        internal static bool NeedsRepair(IntPtr h)
        {
            if (!HasTopmostStyle(h)) return true;
            Native.RECT own;
            if (!Native.GetWindowRect(h, out own)) return false;
            bool covered = false;
            IntPtr candidate = Native.GetWindow(h, 3); // GW_HWNDPREV walks windows above us.
            for (int count = 0; count < 256 && candidate != IntPtr.Zero; count++)
            {
                if (Native.IsWindowVisible(candidate) && !Native.IsIconic(candidate))
                {
                    // Keep our own dropdown, tooltip or dialog above its owner.
                    if (OwnedBy(candidate, h)) return false;
                    int cloaked;
                    Native.RECT other;
                    bool hidden = Native.DwmGetWindowAttribute(candidate, 14, out cloaked, 4) == 0 && cloaked != 0;
                    if (!hidden && Native.GetWindowRect(candidate, out other) && own.Rectangle.IntersectsWith(other.Rectangle)) covered = true;
                }
                candidate = Native.GetWindow(candidate, 3);
            }
            return covered;
        }
    }

    // All callbacks run on the Form's UI thread. A hidden/dismissed popup is never shown
    // by this class. SWP_NOACTIVATE means maintenance cannot steal keyboard focus.
    sealed class SwitcherLayer : IDisposable
    {
        readonly Form form;
        readonly Func<bool> allowed;
        readonly Timer timer = new Timer { Interval = 180 };
        bool presenting, disposed, raising, failureLogged;
        internal SwitcherLayer(Form form, Func<bool> allowed)
        {
            if (form == null) throw new ArgumentNullException("form");
            this.form = form; this.allowed = allowed ?? delegate { return true; };
            form.Activated += Activated;
            form.VisibleChanged += VisibleChanged;
            form.HandleCreated += HandleCreated;
            form.HandleDestroyed += HandleDestroyed;
            form.Disposed += FormDisposed;
            timer.Tick += Tick;
        }
        internal void Begin()
        {
            if (disposed) return;
            presenting = true; failureLogged = false;
            // WinForms caches TopMost; the explicit native call below is still needed.
            form.TopMost = true;
        }
        internal void Suspend()
        { presenting = false; timer.Stop(); }
        bool CanRaise()
        {
            return SwitcherLayerPolicy.CanRaise(presenting, form.Visible, !disposed && allowed(),
                disposed || form.IsDisposed || form.Disposing) && form.IsHandleCreated && form.Enabled;
        }
        internal bool RaiseNow()
        {
            if (raising || !CanRaise()) return false;
            // Owned modal windows must stay above their parent, not behind it.
            foreach (Form owned in form.OwnedForms) if (owned.Visible) return false;
            raising = true;
            try
            {
                form.TopMost = true;
                bool ok = WindowNative.SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, SwitcherLayerPolicy.RaiseFlags);
                if (!ok && !failureLogged)
                { failureLogged = true; ActivationLog.Write("switcher-layer: topmost request rejected; version=" + Program.Version); }
                return ok;
            }
            finally { raising = false; }
        }
        internal void Maintain()
        {
            if (!CanRaise()) { timer.Stop(); return; }
            // The initial user-triggered open already raised us. While in use, repair
            // only our own layer; never chase a user who moved focus to another app.
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground != form.Handle) return;
            if (SwitcherLayerPolicy.NeedsRepair(form.Handle)) RaiseNow();
        }
        void Tick(object sender, EventArgs e) { Maintain(); }
        void Activated(object sender, EventArgs e)
        { if (CanRaise()) { RaiseNow(); timer.Start(); } }
        void VisibleChanged(object sender, EventArgs e)
        {
            if (!form.Visible) { Suspend(); return; }
            if (CanRaise()) { RaiseNow(); timer.Start(); }
        }
        void HandleCreated(object sender, EventArgs e)
        { if (CanRaise()) { RaiseNow(); timer.Start(); } }
        void HandleDestroyed(object sender, EventArgs e) { timer.Stop(); }
        void FormDisposed(object sender, EventArgs e) { Dispose(); }
        public void Dispose()
        {
            if (disposed) return;
            Suspend(); disposed = true; timer.Dispose();
            form.Activated -= Activated; form.VisibleChanged -= VisibleChanged;
            form.HandleCreated -= HandleCreated; form.HandleDestroyed -= HandleDestroyed;
            form.Disposed -= FormDisposed;
        }
    }
}
