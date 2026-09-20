// Touch screen monitor support: non-interfering compatibility milestone.
// No automatic return is armed by this diagnostic. Primary promoted mouse events
// alone cannot prove multitouch release or pen hover across another application.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class TouchSource
    {
        internal static string Classify(ulong extra, uint flags)
        {
            uint marker = unchecked((uint)extra);
            if ((marker & 0xFFFFFF00U) == 0xFF515700U) return (marker & 0x80) != 0 ? "Touch (promoted)" : "Pen (promoted)";
            return (flags & 1) != 0 ? "Injected / unknown" : "Mouse / unclassified";
        }
        internal static bool CanArmReturn(bool sourceVerified, bool allContactsVerified, bool beforeStateVerified, bool deviceTestPassed)
        { return sourceVerified && allContactsVerified && beforeStateVerified && deviceTestPassed; }
    }
    sealed class TouchProbe : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct MouseData
        { public Native.POINT point; public uint data, flags, time; public UIntPtr extra; }
        readonly Native.HookProc callback;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        volatile bool stopped;
        internal volatile bool Registered;
        long mouse, touch, pen, unknown;
        internal TouchProbe()
        {
            callback = Observe;
            var worker = new Thread(Run) { IsBackground = true, Name = "Touch input compatibility observer" };
            worker.SetApartmentState(ApartmentState.MTA); worker.Start(); ready.WaitOne(1000);
        }
        void Run()
        {
            IntPtr hook = IntPtr.Zero;
            try
            {
                using (var queue = new Control())
                using (var timer = new System.Windows.Forms.Timer { Interval = 250 })
                {
                    var handle = queue.Handle;
                    hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
                    Registered = hook != IntPtr.Zero; ready.Set();
                    timer.Tick += delegate { if (stopped) Application.ExitThread(); };
                    timer.Start(); if (!stopped) Application.Run();
                }
            }
            catch { try { ready.Set(); } catch { } }
            finally { Registered = false; if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook); }
        }
        IntPtr Observe(int code, IntPtr message, IntPtr data)
        {
            try
            {
                if (code >= 0 && !stopped)
                {
                    var m = (MouseData)Marshal.PtrToStructure(data, typeof(MouseData));
                    string kind = TouchSource.Classify(m.extra.ToUInt64(), m.flags);
                    if (kind == "Touch (promoted)") Interlocked.Increment(ref touch);
                    else if (kind == "Pen (promoted)") Interlocked.Increment(ref pen);
                    else if (kind == "Injected / unknown") Interlocked.Increment(ref unknown);
                    else Interlocked.Increment(ref mouse);
                }
            }
            catch { }
            return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
        }
        internal string Summary()
        {
            return "Passive observer registration: " + (Registered ? "present" : "unavailable") +
                "\r\nPromoted touch events: " + Interlocked.Read(ref touch) + " | promoted pen: " + Interlocked.Read(ref pen) +
                "\r\nMouse/unclassified events: " + Interlocked.Read(ref mouse) + " | injected/unknown: " + Interlocked.Read(ref unknown) +
                "\r\nThese are event counts, not contact counts. A missing marker is NOT proof of physical mouse use.";
        }
        public void Dispose() { stopped = true; }
    }
    static class TouchNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct PointerInfo
        {
            public uint type, id, frame, flags;
            public IntPtr sourceDevice, hwnd;
            public Native.POINT pixel, himetric, pixelRaw, himetricRaw;
            public uint time, history;
            public int inputData;
            public uint keyStates;
            public ulong performanceCount;
            public int buttonChange;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct PenInfo
        { public PointerInfo info; public uint flags, mask, pressure, rotation; public int tiltX, tiltY; }
        [StructLayout(LayoutKind.Sequential)] internal struct RawDevice
        { public ushort page, usage; public uint flags; public IntPtr target; }
        [StructLayout(LayoutKind.Sequential)] internal struct RawHeader
        { public uint type, size; public IntPtr device, parameter; }
        [DllImport("user32.dll")] internal static extern bool GetPointerInfo(uint id, out PointerInfo info);
        [DllImport("user32.dll")] internal static extern bool GetPointerPenInfo(uint id, out PenInfo info);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    }
    sealed class TouchInputTest : Form
    {
        readonly MonitorData monitor;
        readonly TouchProbe probe;
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 250 };
        readonly TextBox report;
        readonly HashSet<uint> contacts = new HashSet<uint>();
        readonly HashSet<uint> hovering = new HashSet<uint>();
        readonly List<Point> dots = new List<Point>();
        long localTouch, localPen, rawReports, rawBytes;
        int maximumContacts, pointerFailures;
        string rawStatus = "Not registered", lastType = "None", pressure = "Not observed";
        bool cleanup;
        DateTime started = DateTime.UtcNow;
        internal TouchInputTest(MonitorData monitor)
        {
            this.monitor = monitor;
            Text = "Touch screen monitor support - compatibility test"; ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual; BackColor = Theme.Background; ForeColor = Theme.Text;
            Size = new Size(Math.Min(1000, monitor.WorkArea.Width - 24), Math.Min(760, monitor.WorkArea.Height - 24));
            Location = new Point(monitor.WorkArea.Left + (monitor.WorkArea.Width - Width) / 2, monitor.WorkArea.Top + (monitor.WorkArea.Height - Height) / 2);
            DoubleBuffered = true; KeyPreview = true;
            var top = Theme.Label("Input test only - automatic focus/cursor return is OFF.\nTouch, hold two fingers and draw below. Then interact with another app while this stays open.\nNo input is intercepted/replayed. Report contents stay local until you copy them.", 92);
            top.Padding = new Padding(12); top.Dock = DockStyle.Top;
            report = new TextBox { Dock = DockStyle.Bottom, Height = 210, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Card, ForeColor = Theme.Text, ScrollBars = ScrollBars.Vertical };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
            var copy = Theme.Button("Copy diagnostic report", 195); copy.Click += delegate { Clipboard.SetText(Report()); };
            var clear = Theme.Button("Clear drawing", 140); clear.Click += delegate { dots.Clear(); Invalidate(); };
            var close = Theme.Button("Close test", 110); close.Click += delegate { Close(); };
            footer.Controls.AddRange(new Control[] { copy, clear, close });
            Controls.Add(report); Controls.Add(footer); Controls.Add(top);
            probe = new TouchProbe();
            Shown += delegate
            {
                var devices = new[] { new TouchNative.RawDevice { page = 0x0D, usage = 2, flags = 0x100, target = Handle },
                    new TouchNative.RawDevice { page = 0x0D, usage = 4, flags = 0x100, target = Handle } };
                rawStatus = TouchNative.RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf(typeof(TouchNative.RawDevice))) ? "registered, awaiting reports" : "unavailable: " + Marshal.GetLastWin32Error();
                refresh.Start();
            };
            refresh.Tick += delegate { report.Text = Report(); if ((DateTime.UtcNow - started).TotalMinutes >= 5) Close(); };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }
        internal string Report()
        {
            return "Taskbar Tiles " + Program.Version + " | " + monitor.Label + " | compatibility test\r\n" +
                "Automatic return: NOT ARMED (background all-contact/pre-touch capture not yet validated)\r\n" +
                "Own-window pointer events - touch: " + localTouch + " | pen: " + localPen + " | last: " + lastType +
                "\r\nOwn-window contacts now: " + contacts.Count + " | maximum simultaneous: " + maximumContacts + " | pen in range here: " + hovering.Count +
                "\r\nPen pressure: " + pressure + " | pointer API failures: " + pointerFailures +
                "\r\nBackground raw digitizer: " + rawStatus + " | reports: " + rawReports + " | bytes: " + rawBytes +
                "\r\n" + probe.Summary() +
                "\r\nNo pointer coordinates, window titles, raw HID data, typed text or images are included.\r\n" +
                "Own-window pointer support does not prove cross-app contact tracking. Surface/spacedesk and Apollo must be tested separately.";
        }
        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == 0xFF && !cleanup)
                {
                    uint size = 0;
                    if (TouchNative.GetRawInputData(m.LParam, 0x10000003, IntPtr.Zero, ref size, (uint)Marshal.SizeOf(typeof(TouchNative.RawHeader))) != uint.MaxValue)
                    { rawReports++; rawBytes += size; }
                }
                if (m.Msg == 0x245 || m.Msg == 0x246 || m.Msg == 0x247 || m.Msg == 0x249 || m.Msg == 0x24A || m.Msg == 0x24C)
                {
                    uint id = unchecked((uint)m.WParam.ToInt64()) & 0xFFFF;
                    TouchNative.PointerInfo info;
                    if (TouchNative.GetPointerInfo(id, out info))
                    {
                        bool down = (info.flags & 4) != 0, inRange = (info.flags & 2) != 0;
                        if (m.Msg == 0x247 || m.Msg == 0x24A || m.Msg == 0x24C || (info.flags & 0x8000) != 0) down = false;
                        if (down) contacts.Add(id); else contacts.Remove(id);
                        maximumContacts = Math.Max(maximumContacts, contacts.Count);
                        if (info.type == 2) { localTouch++; lastType = "Touch"; }
                        else if (info.type == 3)
                        {
                            localPen++; lastType = "Pen";
                            if (inRange && m.Msg != 0x24A && m.Msg != 0x24C) hovering.Add(id); else hovering.Remove(id);
                            TouchNative.PenInfo pen;
                            if (TouchNative.GetPointerPenInfo(id, out pen)) pressure = (pen.mask & 1) != 0 ? pen.pressure.ToString() + " (reported by driver)" : "Not supplied by driver";
                        }
                        else lastType = "Pointer type " + info.type;
                        if (down && (info.type == 2 || info.type == 3))
                        {
                            dots.Add(PointToClient(new Point(info.pixel.x, info.pixel.y)));
                            if (dots.Count > 2000) dots.RemoveRange(0, 500);
                            Invalidate();
                        }
                    }
                    else { pointerFailures++; if (m.Msg == 0x247 || m.Msg == 0x24C) contacts.Remove(id); }
                }
            }
            catch { pointerFailures++; }
            base.WndProc(ref m);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var brush = new SolidBrush(Theme.Accent)) foreach (Point point in dots) e.Graphics.FillEllipse(brush, point.X - 3, point.Y - 3, 6, 6);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !cleanup)
            {
                cleanup = true; refresh.Stop(); refresh.Dispose(); probe.Dispose();
                TouchNative.RegisterRawInputDevices(new[] { new TouchNative.RawDevice { page = 0x0D, usage = 2, flags = 1 },
                    new TouchNative.RawDevice { page = 0x0D, usage = 4, flags = 1 } }, 2, (uint)Marshal.SizeOf(typeof(TouchNative.RawDevice)));
            }
            base.Dispose(disposing);
        }
    }
    sealed class TouchMonitorMap : Control
    {
        internal List<MonitorData> Monitors = new List<MonitorData>();
        internal string SelectedKey;
        internal Action<MonitorData> Selected;
        readonly Dictionary<Rectangle, MonitorData> rectangles = new Dictionary<Rectangle, MonitorData>();
        internal TouchMonitorMap() { Height = 220; Width = 700; DoubleBuffered = true; BackColor = Theme.Card; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); rectangles.Clear(); if (Monitors.Count == 0) return;
            Rectangle desktop = Monitors.Select(m => m.Bounds).Aggregate(Rectangle.Union);
            Rectangle box = DrawingUtil.Fit(Rectangle.Inflate(ClientRectangle, -20, -20), desktop.Width, desktop.Height);
            float scale = box.Width / (float)Math.Max(1, desktop.Width);
            foreach (var m in Monitors)
            {
                var r = new Rectangle(box.Left + (int)((m.Bounds.Left - desktop.Left) * scale), box.Top + (int)((m.Bounds.Top - desktop.Top) * scale),
                    Math.Max(20, (int)(m.Bounds.Width * scale) - 4), Math.Max(20, (int)(m.Bounds.Height * scale) - 4));
                rectangles[r] = m;
                DrawingUtil.Round(e.Graphics, r, 6, Theme.Background, m.Key == SelectedKey ? Theme.Accent : Theme.Border, m.Key == SelectedKey ? 3 : 1);
                TextRenderer.DrawText(e.Graphics, m.Label, Font, r, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            foreach (var pair in rectangles) if (pair.Key.Contains(e.Location))
            { SelectedKey = pair.Value.Key; Invalidate(); if (Selected != null) Selected(pair.Value); break; }
        }
    }
    sealed partial class SettingsWindow
    {
        void AddTouchSupportPage()
        {
            var page = Page("Touch screen monitor support");
            Section(page, "Compatibility test - automatic return is not enabled yet", "This first milestone checks what input your display connection exposes without moving focus/cursor or interfering with gestures. Full return requires verified background contacts and pre-touch state, not just a primary mouse event.");
            var map = new TouchMonitorMap { SelectedKey = edit.TouchTestMonitorKey };
            var key = new TextBox { Text = edit.TouchTestMonitorKey, Visible = false };
            fields["TouchTestMonitorKey"] = key;
            var status = new Label { Width = 700, Height = 50, ForeColor = Theme.Muted };
            Action reload = delegate
            {
                try { map.Monitors = DisplayNative.Monitors(); }
                catch { map.Monitors = new List<MonitorData>(); }
                map.SelectedKey = key.Text;
                var chosen = map.Monitors.Where(m => m.Key == key.Text).ToArray();
                status.Text = key.Text.Length == 0 ? "Select the touchscreen monitor in the diagram." : chosen.Length == 1 ? chosen[0].Label + " selected for testing. Apply remembers its available device identity." : "Disconnected or ambiguous - previous selection retained. Select the correct display again.";
                map.Invalidate();
            };
            map.Selected = delegate(MonitorData m) { key.Text = m.Key; reload(); };
            page.Controls.Add(map); page.Controls.Add(status);
            var buttons = new FlowLayoutPanel { Width = 700, Height = 48 };
            var test = Theme.Button("Open input test", 155); var refreshDisplays = Theme.Button("Refresh displays", 155);
            refreshDisplays.Click += delegate { reload(); };
            test.Click += delegate
            {
                reload(); var matches = map.Monitors.Where(m => m.Key == key.Text).ToArray();
                if (matches.Length != 1) { MessageBox.Show(this, "Select one currently connected monitor first.", "Touchscreen test"); return; }
                bool previousDismissal = dismissOnFocusLoss;
                dismissOnFocusLoss = false;
                try { using (var form = new TouchInputTest(matches[0])) form.ShowDialog(this); }
                finally { dismissOnFocusLoss = previousDismissal; dismissalArmed = false; outsideSince = null; }
            };
            buttons.Controls.AddRange(new Control[] { test, refreshDisplays }); page.Controls.Add(buttons);
            HintTree(test, "Runs a five-minute maximum diagnostic only. No automatic return is armed. Use another app during the test to check passive input visibility; Copy diagnostic report contains counts, not raw input/content.");
            HintTree(map, "Choose the display that should eventually trigger temporary touch interaction. It is NOT the return destination. Disconnected identities are not silently matched to another monitor.");
            Section(page, "Test sequence", "1. Tap once. Hold two fingers down, lift one, then the other.\n2. Draw several pen strokes; hover between strokes.\n3. Touch/draw in another app while this test remains open.\n4. Move/click/scroll with the physical mouse. Copy the report. Test spacedesk and Apollo separately.");
            Section(page, "Planned return behaviour", "Return to the previously active window and cursor, never to a fixed monitor. Suggested starting delays: touch 1,000 ms; pen 2,000 ms. No return during any contact or verified hover. Physical mouse use cancels it. Stay here pauses it. This build deliberately cannot enable automatic return from incomplete input evidence.");
            reload();
        }
    }
}
