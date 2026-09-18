using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class WindowNative
    {
        // Win32 uses the 44-byte structure; SDK rcDevice is conditional on _MAC.
        [StructLayout(LayoutKind.Sequential)] internal struct WINDOWPLACEMENT
        { public uint length, flags, showCmd; public Native.POINT min, max; public Native.RECT normal; }
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint process);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] internal static extern int FrameBounds(IntPtr h, int attribute, out Native.RECT value, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, string text);
        internal static uint ProcessId(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
        internal static Rectangle VisibleBounds(IntPtr h)
        {
            Native.RECT r;
            if (FrameBounds(h, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) == 0 && r.Rectangle.Width > 0) return r.Rectangle;
            Native.GetWindowRect(h, out r); return r.Rectangle;
        }
        internal static Rectangle OuterForVisible(IntPtr h, Rectangle desired)
        {
            Native.RECT r; if (!Native.GetWindowRect(h, out r)) return desired;
            var frame = VisibleBounds(h); var outer = r.Rectangle;
            // DWM's visible frame excludes the invisible resize border.
            int l = Math.Max(0, Math.Min(64, frame.Left - outer.Left));
            int t = Math.Max(0, Math.Min(64, frame.Top - outer.Top));
            int rr = Math.Max(0, Math.Min(64, outer.Right - frame.Right));
            int b = Math.Max(0, Math.Min(64, outer.Bottom - frame.Bottom));
            return Rectangle.FromLTRB(desired.Left - l, desired.Top - t, desired.Right + rr, desired.Bottom + b);
        }
    }
    sealed class WindowRecord
    {
        public IntPtr Handle;
        public uint ProcessId;
        public string Title, Exe, AppId;
        public long ProcessStartTicks;
        internal uint AppProcessId;
        internal long AppProcessStartTicks;
        internal bool IdentityAmbiguous;
        internal DateTime NextIdentityRefresh;
        public override string ToString() { return Title + (string.IsNullOrEmpty(Exe) ? "" : "   [" + Path.GetFileName(Exe) + "]"); }
    }
    static class WindowInventory
    {
        internal static List<WindowRecord> Read(Dictionary<IntPtr, WindowRecord> cache = null)
        {
            var result = new List<WindowRecord>(); uint own = (uint)Process.GetCurrentProcess().Id;
            Native.EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                if (!Native.IsWindowVisible(h) || WindowNative.ProcessId(h) == own) return true;
                long style = Native.GetWindowLongPtr(h, -20).ToInt64();
                if ((style & 0x80) != 0 || (style & 0x08000000) != 0) return true;
                if (Native.GetWindow(h, 4) != IntPtr.Zero && (style & 0x40000) == 0) return true;
                string cls = Native.Class(h);
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "Progman" || cls == "WorkerW") return true;
                int cloaked; if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                var title = new StringBuilder(1024); Native.GetWindowText(h, title, title.Capacity);
                if (title.Length == 0) return true;
                uint pid = WindowNative.ProcessId(h); WindowRecord entry;
                if (cache == null || !cache.TryGetValue(h, out entry) || entry.ProcessId != pid)
                {
                    entry = new WindowRecord { Handle = h, ProcessId = pid };
                    if (cache != null) cache[h] = entry;
                }
                // Metadata is often absent while a packaged app is starting. Refresh
                // during startup; do not cache an empty ID forever (v0.6 behaviour).
                if (DateTime.UtcNow >= entry.NextIdentityRefresh)
                {
                    entry.Exe = ShellIcons.ProcessFile(h);
                    string windowId = ShellIcons.WindowAppId(h);
                    entry.AppId = string.IsNullOrWhiteSpace(windowId) ? PackageIdentity.ForProcess(pid) : windowId;
                    entry.ProcessStartTicks = PackageIdentity.StartTicks(pid);
                    entry.AppProcessId = pid; entry.AppProcessStartTicks = entry.ProcessStartTicks;
                    entry.IdentityAmbiguous = false;
                    HostedWindowIdentity.Refresh(h, entry);
                    entry.NextIdentityRefresh = DateTime.UtcNow.AddMilliseconds(500);
                }
                entry.Title = title.ToString(); result.Add(entry); return true;
            }, IntPtr.Zero);
            return result;
        }
        internal static bool Matches(AppButton app, WindowRecord w)
        { return LaunchIdentity.Matches(app, w, null); }
    }

    sealed class WindowMover : IDisposable
    {
        readonly Timer timer = new Timer { Interval = 70 };
        IntPtr target, undoTarget;
        uint targetProcess, undoProcess;
        WindowNative.WINDOWPLACEMENT undo;
        ZoneDestination destination;
        Rectangle desired;
        DateTime started, phaseTime;
        int phase;
        Action<string> completed;
        public bool HasUndo { get { return undoTarget != IntPtr.Zero && Native.IsWindow(undoTarget) && WindowNative.ProcessId(undoTarget) == undoProcess; } }
        public bool Busy { get { return timer.Enabled; } }
        public WindowMover() { timer.Tick += Tick; }
        public void Place(IntPtr window, ZoneDestination where, Options options, Action<string> done)
        {
            if (Busy) { done("A window move is already in progress."); return; }
            if (!Native.IsWindow(window) || WindowNative.ProcessId(window) == (uint)Process.GetCurrentProcess().Id) { done("That window is no longer available."); return; }
            var monitor = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == where.Monitor.DeviceName);
            if (monitor == null || monitor.Bounds != where.Monitor.Bounds || monitor.WorkingArea != where.Monitor.WorkArea)
            { done("The display layout changed. Reopen the destination picker."); return; }
            desired = where.Maximise ? where.Monitor.WorkArea : Rectangle.Intersect(where.Bounds, where.Monitor.WorkArea);
            if (!where.Maximise && options.ZoneInset > 0) desired.Inflate(-options.ZoneInset, -options.ZoneInset);
            if (desired.Width < 32 || desired.Height < 32) { done("That zone is too small after applying its inset."); return; }
            if (options.EnableUndoMove)
            {
                var original = new WindowNative.WINDOWPLACEMENT { length = (uint)Marshal.SizeOf(typeof(WindowNative.WINDOWPLACEMENT)) };
                if (WindowNative.GetWindowPlacement(window, ref original)) { undoTarget = window; undoProcess = WindowNative.ProcessId(window); undo = original; }
            }
            target = window; targetProcess = WindowNative.ProcessId(window); destination = where; completed = done;
            started = phaseTime = DateTime.UtcNow; phase = 0;
            if (Native.IsIconic(target) || WindowNative.IsZoomed(target)) Native.ShowWindowAsync(target, 9);
            timer.Start();
        }
        bool SetBounds(Rectangle r)
        {
            if (WindowNative.SetWindowPos(target, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, 0x4000 | 0x0004 | 0x0010)) return true;
            int code = Marshal.GetLastWin32Error(); Finish("Windows refused to move that window. " + new Win32Exception(code).Message + " Elevated or protected windows may need to be moved normally."); return false;
        }
        void Tick(object sender, EventArgs e)
        {
            try
            {
                if (!Native.IsWindow(target) || WindowNative.ProcessId(target) != targetProcess) { Finish("The selected window closed before it could be moved."); return; }
                double elapsed = (DateTime.UtcNow - phaseTime).TotalMilliseconds;
                if ((DateTime.UtcNow - started).TotalSeconds > 4) { Finish("The window did not accept the requested size or monitor. It may be fullscreen, non-resizable or busy."); return; }
                if (phase == 0)
                {
                    if (Native.IsIconic(target) || WindowNative.IsZoomed(target)) return;
                    if (!SetBounds(WindowNative.OuterForVisible(target, desired))) return;
                    phase = 1; phaseTime = DateTime.UtcNow;
                }
                else if (phase == 1 && elapsed >= 180)
                {
                    // Recalculate the invisible border after a cross-monitor DPI change.
                    if (!SetBounds(WindowNative.OuterForVisible(target, desired))) return;
                    phase = 2; phaseTime = DateTime.UtcNow;
                }
                else if (phase == 2 && elapsed >= 180)
                {
                    if (destination.Maximise)
                    {
                        if (Screen.FromHandle(target).DeviceName != destination.Monitor.DeviceName) return;
                        Native.ShowWindowAsync(target, 3); phase = 3; phaseTime = DateTime.UtcNow;
                    }
                    else
                    {
                        Rectangle actual = WindowNative.VisibleBounds(target);
                        bool close = Math.Abs(actual.Left - desired.Left) <= 8 && Math.Abs(actual.Top - desired.Top) <= 8 && Math.Abs(actual.Width - desired.Width) <= 8 && Math.Abs(actual.Height - desired.Height) <= 8;
                        Native.SetForegroundWindow(target);
                        Finish(close ? null : "Placement requested, but the app adjusted the final size. Some windows enforce a minimum size.");
                    }
                }
                else if (phase == 3 && elapsed >= 180 && WindowNative.IsZoomed(target))
                { Native.SetForegroundWindow(target); Finish(null); }
            }
            catch (Exception ex) { Finish("Window placement failed: " + ex.Message); }
        }
        void Finish(string message)
        { timer.Stop(); var action = completed; completed = null; if (action != null) action(message); }
        public string Undo()
        {
            if (Busy) return "Wait for the current move to finish.";
            if (!HasUndo) return "There is no window move to undo.";
            var previous = undo; previous.flags |= 0x0004; // asynchronous across input queues
            IntPtr h = undoTarget; undoTarget = IntPtr.Zero;
            if (!WindowNative.SetWindowPlacement(h, ref previous)) return "Windows could not restore that window's previous position.";
            Native.SetForegroundWindow(h); return null;
        }
        public void Dispose() { timer.Stop(); timer.Dispose(); completed = null; }
    }
    sealed class LaunchPlacement : IDisposable
    {
        readonly AppButton app;
        readonly Options options;
        readonly string requestId;
        readonly Action<IntPtr, string> finished;
        readonly Dictionary<IntPtr, WindowRecord> cache = new Dictionary<IntPtr, WindowRecord>();
        readonly Dictionary<IntPtr, uint> before;
        readonly IntPtr foregroundBefore;
        readonly Timer timer = new Timer { Interval = 220 };
        DateTime started, candidateSince, foregroundSince;
        IntPtr candidate, observedForeground;
        uint candidatePid;
        bool completed;
        int lastNew = -1, lastMatches = -1, lastExisting = -1;
        readonly HashSet<string> evidenceLogged = new HashSet<string>();
        LaunchReceipt receipt;
        public Form ActiveDialog { get; private set; }
        public LaunchPlacement(AppButton requested, Options settings, string id, Action<IntPtr, string> callback)
        {
            app = requested; options = settings; finished = callback; requestId = id;
            before = WindowInventory.Read(cache).ToDictionary(w => w.Handle, w => w.ProcessId);
            foregroundBefore = Native.GetForegroundWindow();
            timer.Tick += Tick;
        }
        public void Begin(LaunchReceipt accepted)
        {
            receipt = accepted ?? new LaunchReceipt(); started = DateTime.UtcNow;
            LaunchLog.Write(requestId, "tracking; version=" + Program.Version + "; initial windows=" + before.Count + "; require new=" + receipt.RequireNewWindow + "; expected=" + LaunchResolution.Describe(receipt.ExpectedAppId, receipt.ExpectedExe));
            timer.Start();
        }
        public void Fail(string error) { Complete(IntPtr.Zero, error); }
        bool New(WindowRecord w) { return LaunchIdentity.IsNew(w, before); }
        bool Match(WindowRecord w) { return LaunchIdentity.Matches(app, w, receipt); }
        void Tick(object sender, EventArgs e)
        {
            try
            {
                var now = WindowInventory.Read(cache);
                var matches = now.Where(w => New(w) && Match(w)).ToList();
                int newCount = now.Count(New);
                int existingMatches = now.Count(w => !New(w) && Match(w));
                if (newCount != lastNew || matches.Count != lastMatches || existingMatches != lastExisting)
                {
                    lastNew = newCount; lastMatches = matches.Count; lastExisting = existingMatches;
                    LaunchLog.Write(requestId, "new windows=" + newCount + "; matching new=" + matches.Count + "; matching existing=" + existingMatches);
                }
                foreach (var observed in now.Where(w => New(w) || Match(w)))
                {
                    string evidence = "hwnd=" + observed.Handle.ToInt64().ToString("X") + "; owner pid=" + observed.ProcessId + "; app pid=" + observed.AppProcessId + "; " + LaunchResolution.Describe(observed.AppId, observed.Exe) + "; " + LaunchResolution.Reason(app, observed, receipt);
                    if (evidenceLogged.Count < 96 && evidenceLogged.Add(evidence)) LaunchLog.Write(requestId, evidence);
                }
                double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                IntPtr foreground = Native.GetForegroundWindow();
                if (foreground != observedForeground) { observedForeground = foreground; foregroundSince = DateTime.UtcNow; }
                if (matches.Count == 1)
                {
                    WindowRecord w = matches[0];
                    if (candidate != w.Handle || candidatePid != w.ProcessId)
                    { candidate = w.Handle; candidatePid = w.ProcessId; candidateSince = DateTime.UtcNow; }
                    else if (elapsed >= 1.2 && (DateTime.UtcNow - candidateSince).TotalMilliseconds >= 800)
                    {
                        // Avoid a zero-sized construction window and allow a brief startup settle.
                        Rectangle bounds = WindowNative.VisibleBounds(w.Handle);
                        if (bounds.Width >= 64 && bounds.Height >= 40)
                        { LaunchLog.Write(requestId, "identified stable new window; pid=" + w.ProcessId); Complete(candidate, null); return; }
                    }
                }
                else { candidate = IntPtr.Zero; candidatePid = 0; }
                // Do not move an old matching window at 2.5 seconds: many apps are
                // still starting. Wait the full deadline; never reuse for -w new.
                if (elapsed < options.LaunchTimeoutSeconds) return;
                if (LaunchIdentity.CanReuse(options.ReuseSingleInstance, receipt.RequireNewWindow, elapsed, options.LaunchTimeoutSeconds,
                    matches.Count != 0, foreground != foregroundBefore, (DateTime.UtcNow - foregroundSince).TotalMilliseconds))
                {
                    var reused = now.FirstOrDefault(w => w.Handle == foreground && !New(w) && Match(w));
                    if (reused != null)
                    { LaunchLog.Write(requestId, "reusing newly-focused matching window; pid=" + reused.ProcessId); Complete(reused.Handle, null); return; }
                }
                Ask(Shortlist(now), matches.Count > 1
                    ? "Several matching windows opened. Select the one to place."
                    : "The launch request was sent, but its window is not yet identified. Refresh or wait longer, or select the correct window.");
            }
            catch (Exception ex) { Complete(IntPtr.Zero, "Could not identify the new window: " + ex.Message); }
        }
        List<WindowRecord> Shortlist(List<WindowRecord> now)
        { return now.Where(w => New(w) || Match(w)).ToList(); }
        void Ask(List<WindowRecord> windows, string reason)
        {
            timer.Stop(); LaunchLog.Write(requestId, "manual choice required; candidates=" + windows.Count);
            WindowRecord selected = null; bool wait = false; DialogResult result;
            bool all = windows.Count == 0;
            if (all)
            {
                windows = WindowInventory.Read(cache);
                reason += "\nNo identity match: showing all open windows for manual selection, not an automatic move.";
                LaunchLog.Write(requestId, "manual fallback lists all eligible windows=" + windows.Count);
            }
            using (var picker = new WindowChoiceWindow(app.DisplayName, reason, windows, delegate { return Shortlist(WindowInventory.Read(cache)); }, all))
            {
                ActiveDialog = picker;
                try { result = picker.ShowDialog(); selected = picker.Selected; wait = picker.WaitLongerRequested; }
                finally { ActiveDialog = null; }
            }
            if (completed) return;
            if (wait)
            {
                // Observe the SAME request, with its original before-snapshot. No extra launch.
                started = DateTime.UtcNow; candidate = IntPtr.Zero; candidatePid = 0;
                LaunchLog.Write(requestId, "wait extended without re-launch"); timer.Start(); return;
            }
            if (result == DialogResult.OK && selected != null) Complete(selected.Handle, null);
            else Complete(IntPtr.Zero, null);
        }
        void Complete(IntPtr h, string error)
        {
            if (completed) return; completed = true; timer.Stop();
            LaunchLog.Write(requestId, error != null ? "tracking error" : h == IntPtr.Zero ? "placement cancelled (launched apps stay open)" : "window selected for placement");
            finished(h, error);
        }
        public void Dispose()
        { completed = true; timer.Stop(); timer.Dispose(); if (ActiveDialog != null && !ActiveDialog.IsDisposed) ActiveDialog.Close(); }
    }
    sealed class WindowChoiceWindow : Form
    {
        readonly ListBox list;
        readonly Func<List<WindowRecord>> refresh;
        bool showAll;
        public WindowRecord Selected;
        public bool WaitLongerRequested;
        public WindowChoiceWindow(string app, string reason, List<WindowRecord> windows, Func<List<WindowRecord>> reload, bool initiallyShowAll = false)
        {
            refresh = reload; showAll = initiallyShowAll;
            Text = "Choose window - " + app; StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = false; TopMost = true;
            Size = new Size(850, 460); MinimumSize = new Size(680, 330);
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10);
            var header = Theme.Label(reason + "\nNo unrelated window has been moved. Waiting again will not launch another copy.", 90); header.Padding = new Padding(12);
            list = new ListBox { Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text, IntegralHeight = false, HorizontalScrollbar = true };
            foreach (var w in windows) list.Items.Add(w);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 94, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5), WrapContents = true };
            var move = Theme.Button("Place selected", 140); move.Click += delegate { Accept(); };
            var cancel = Theme.Button("Cancel", 90); cancel.Click += delegate { Close(); };
            var reloadButton = Theme.Button("Refresh list", 110); reloadButton.Click += delegate { Reload(); };
            var waitButton = Theme.Button("Wait longer", 110); waitButton.Click += delegate { WaitLongerRequested = true; DialogResult = DialogResult.Retry; Close(); };
            var all = Theme.Button(showAll ? "Show candidates" : "Show all windows", 150); all.Click += delegate { showAll = !showAll; all.Text = showAll ? "Show candidates" : "Show all windows"; Reload(); };
            var log = Theme.Button("Diagnostics", 105); log.Click += delegate { try { Process.Start("notepad.exe", "\"" + LaunchLog.FilePath + "\""); } catch { } };
            footer.Controls.AddRange(new Control[] { move, cancel, waitButton, reloadButton, all, log });
            list.DoubleClick += delegate { Accept(); };
            Controls.Add(list); Controls.Add(footer); Controls.Add(header); CancelButton = cancel;
        }
        void Reload()
        {
            var old = list.SelectedItem as WindowRecord;
            try
            {
                var current = showAll ? WindowInventory.Read() : refresh();
                list.BeginUpdate(); list.Items.Clear();
                foreach (var w in current)
                { list.Items.Add(w); if (old != null && old.Handle == w.Handle && old.ProcessId == w.ProcessId) list.SelectedItem = w; }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not refresh windows"); }
            finally { list.EndUpdate(); }
        }
        void Accept()
        {
            Selected = list.SelectedItem as WindowRecord; if (Selected == null) return;
            if (!Native.IsWindow(Selected.Handle) || WindowNative.ProcessId(Selected.Handle) != Selected.ProcessId)
            { MessageBox.Show(this, "That window has closed. Refresh the list.", "Window no longer available"); Selected = null; return; }
            DialogResult = DialogResult.OK; Close();
        }
    }
}
