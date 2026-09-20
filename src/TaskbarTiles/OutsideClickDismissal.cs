// Observe outside clicks even when a topmost popup never obtained activation.
// The hook never consumes input. No clicks, coordinates or application content are logged.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class ClickAwayDiagnostics
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
    static class OutsideClickPolicy
    {
        internal static bool Dismiss(int observedSession, int currentSession, bool armed, bool visible, bool enabled,
            bool disposing, bool internalTarget)
        { return observedSession == currentSession && armed && visible && enabled && !disposing && !internalTarget; }
        internal static bool IsDown(int message)
        { return message == 0x0201 || message == 0x0204 || message == 0x0207; } // left/right/middle only; X-Mouse toggle is not swallowed.
    }
    sealed class PopupClickWatcher : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct MouseData
        { public Native.POINT point; public uint data, flags, time; public UIntPtr extra; }
        readonly Form form;
        readonly Func<bool> enabled;
        readonly Action dismiss;
        readonly Native.HookProc callback;
        readonly Thread worker;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        volatile bool armed, disposed, installed;
        int session;
        uint threadId;
        internal bool Installed { get { return installed; } }
        internal int Session { get { return Volatile.Read(ref session); } }
        internal PopupClickWatcher(Form form, Func<bool> enabled, Action dismiss)
        {
            this.form = form; this.enabled = enabled; this.dismiss = dismiss;
            callback = Hook;
            worker = new Thread(Run) { IsBackground = true, Name = "Taskbar Tiles outside-click observer" };
            worker.SetApartmentState(ApartmentState.MTA); worker.Start();
            if (ready.WaitOne(2000)) ready.Dispose();
            form.VisibleChanged += Visibility;
            form.Disposed += FormDisposed;
        }
        void SignalReady() { try { ready.Set(); } catch (ObjectDisposedException) { } }
        void Run()
        {
            IntPtr hook = IntPtr.Zero;
            try
            {
                threadId = Native.GetCurrentThreadId();
                // Creating a native Control guarantees this thread has a message queue.
                using (var queue = new Control())
                {
                    var h = queue.Handle;
                    hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
                    installed = hook != IntPtr.Zero;
                    if (!installed) ClickAwayDiagnostics.Write("outside-click-hook-unavailable", null);
                    SignalReady();
                    if (!disposed) Application.Run();
                }
            }
            catch (Exception ex) { ClickAwayDiagnostics.Write("outside-click-hook", ex); SignalReady(); }
            finally
            {
                installed = false;
                if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            }
        }
        IntPtr Hook(int code, IntPtr message, IntPtr data)
        {
            try
            {
                if (code >= 0 && armed && !disposed && OutsideClickPolicy.IsDown(message.ToInt32()))
                {
                    int observed = Session;
                    var m = (MouseData)Marshal.PtrToStructure(data, typeof(MouseData));
                    // Capture the window under the down-event, not after activation/repaint.
                    IntPtr target = Native.WindowFromPoint(m.point);
                    PostObservation(observed, target, new Point(m.point.x, m.point.y));
                }
            }
            catch { } // Never break another application's mouse event.
            return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
        }
        void PostObservation(int observed, IntPtr target, Point point)
        {
            try { form.BeginInvoke(new Action(delegate { Observe(observed, target, point); })); }
            catch (InvalidOperationException) { }
        }
        internal void Arm() { Interlocked.Increment(ref session); armed = true; }
        internal void Suspend() { armed = false; Interlocked.Increment(ref session); }
        bool InternalTarget(IntPtr target, Point point)
        {
            if (target == IntPtr.Zero) return form.Bounds.Contains(point);
            if (WindowNative.ProcessId(target) == ownPid) return true; // controls, native file pickers and drop-downs.
            IntPtr root = Native.GetAncestor(target, 2);
            return root == form.Handle || SwitcherLayerPolicy.OwnedBy(root, form.Handle);
        }
        internal void Observe(int observed, IntPtr target, Point point)
        {
            if (disposed || form.IsDisposed || !armed || observed != Session) return;
            try
            {
                if (OutsideClickPolicy.Dismiss(observed, Session, armed, form.Visible, enabled(),
                    form.Disposing, InternalTarget(target, point)))
                { Suspend(); dismiss(); }
            }
            catch (Exception ex) { ClickAwayDiagnostics.Write("outside-click-dismissal", ex); }
        }
        void Visibility(object sender, EventArgs e) { if (form.Visible) Arm(); else Suspend(); }
        void FormDisposed(object sender, EventArgs e) { Dispose(); }
        public void Dispose()
        {
            if (disposed) return; Suspend(); disposed = true;
            form.VisibleChanged -= Visibility; form.Disposed -= FormDisposed;
            if (threadId != 0) Native.PostThreadMessage(threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
            // No join on the UI thread. The worker owns/unhooks its hook on exit.
        }
    }
    sealed partial class Switcher
    {
        PopupClickWatcher outsideClicks;
        readonly System.Windows.Forms.Timer outsideFocusTimer = new System.Windows.Forms.Timer { Interval = 90 };
        bool observedOwnFocus;
        DateTime? externalFocusSince;
        void SetupOutsideDismissal()
        {
            outsideClicks = new PopupClickWatcher(this, delegate
            { return options.HideOnFocusLoss && transient == null && !suppressDeactivate && !renderingPreview && activation == null && !closing; },
                delegate { Dismiss(); });
            VisibleChanged += delegate
            {
                observedOwnFocus = false; externalFocusSince = null;
                if (Visible) outsideFocusTimer.Start(); else outsideFocusTimer.Stop();
            };
            outsideFocusTimer.Tick += delegate
            {
                if (!Visible || closing || transient != null || suppressDeactivate || renderingPreview || !options.HideOnFocusLoss)
                { externalFocusSince = null; return; }
                IntPtr foreground = Native.GetForegroundWindow();
                if (foreground == IntPtr.Zero) { externalFocusSince = null; return; }
                if (WindowNative.ProcessId(foreground) == (uint)Process.GetCurrentProcess().Id || SwitcherLayerPolicy.OwnedBy(foreground, Handle))
                { observedOwnFocus = true; externalFocusSince = null; return; }
                // Do not dismiss merely because initial foreground permission was denied.
                // The separate down-event observer handles outside clicks in that case.
                if (!observedOwnFocus) return;
                if (!externalFocusSince.HasValue) externalFocusSince = DateTime.UtcNow;
                else if ((DateTime.UtcNow - externalFocusSince.Value).TotalMilliseconds >= 120) Dismiss();
            };
        }
        void DisposeOutsideDismissal()
        { if (outsideClicks != null) outsideClicks.Dispose(); outsideFocusTimer.Stop(); outsideFocusTimer.Dispose(); }
    }
}
