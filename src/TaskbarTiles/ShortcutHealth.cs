// Keyboard callbacks only classify keys and post a native message. UI work runs elsewhere.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class ShortcutLog
    {
        static readonly object gate = new object();
        internal static string PathName { get { return Path.Combine(Program.Home, "shortcut-diagnostics.log"); } }
        internal static void Write(string message)
        {
            lock (gate) try
            {
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 262144)
                { File.Copy(PathName, PathName + ".previous", true); File.Delete(PathName); }
                File.AppendAllText(PathName, DateTime.UtcNow.ToString("o") + " v" + Program.Version + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }
    static class ShortcutPolicy
    {
        internal static bool Renew(bool enabled, bool stopping, bool keysDown, long age, bool requested)
        { return enabled && !stopping && !keysDown && (requested || age >= 60000); }
        internal static int Flags(bool ctrl, bool shift) { return 1 | (ctrl ? 2 : 0) | (shift ? 4 : 0); }
    }
    sealed class KeyboardHook : IDisposable
    {
        internal const int ActionMessage = 0x8051;
        public volatile bool Enabled = true;
        public Action<bool, bool> Pressed;
        public Action Released;
        IntPtr target;
        readonly Native.HookProc callback;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly bool[] modifiers = new bool[256];
        readonly Stopwatch clock = Stopwatch.StartNew();
        Thread worker;
        readonly object workerGate = new object();
        IntPtr hook;
        volatile bool stopping, installed, repairRequested;
        int queued, registrations;
        long registeredAt, callbacks;
        bool swallowTabUp, session;
        internal KeyboardHook(IntPtr target)
        {
            this.target = target; callback = OnKey;
            StartWorker(); ready.WaitOne(2000);
        }
        public bool Installed { get { return installed; } }
        internal string Status
        {
            get { return "Alt+Tab preference: " + (Enabled ? "enabled" : "temporarily/explicitly disabled") +
                "\r\nHook registration: " + (installed ? "present (not proof Windows retained it)" : "unavailable") +
                "\r\nSuccessful registrations: " + Volatile.Read(ref registrations) +
                "\r\nCallback count: " + Interlocked.Read(ref callbacks) +
                "\r\nHook thread: " + (worker.IsAlive ? "running" : "stopped") +
                "\r\nIdle re-registration: every 60 seconds; never while keys are held."; }
        }
        internal long CallbackCount { get { return Interlocked.Read(ref callbacks); } }
        internal int RegistrationCount { get { return Volatile.Read(ref registrations); } }
        internal bool ThreadAlive { get { return worker != null && worker.IsAlive; } }
        internal void UpdateTarget(IntPtr handle) { target = handle; }
        internal void TestDetachRegistration() { if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook); }
        void StartWorker()
        {
            lock (workerGate)
            {
                if (stopping || worker != null && worker.IsAlive) return;
                worker = new Thread(Run) { IsBackground = true, Name = "Taskbar Tiles shortcut input" };
                worker.SetApartmentState(ApartmentState.MTA); worker.Start();
            }
        }
        internal void RequestRepair() { repairRequested = true; if (Enabled) StartWorker(); }
        void Register()
        {
            if (stopping) return;
            IntPtr next = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
            registeredAt = clock.ElapsedMilliseconds;
            if (next == IntPtr.Zero)
            { ShortcutLog.Write("hook registration rejected; win32=" + Marshal.GetLastWin32Error()); return; }
            IntPtr old = hook; hook = next; installed = true;
            if (old != IntPtr.Zero) Native.UnhookWindowsHookEx(old);
            Array.Clear(modifiers, 0, modifiers.Length); session = swallowTabUp = false;
            Interlocked.Increment(ref registrations); repairRequested = false;
        }
        static bool AnyKeyDown()
        {
            for (int k = 1; k < 256; k++) if (Native.Down(k)) return true;
            return false;
        }
        void Run()
        {
            try
            {
                using (var queue = new Control())
                using (var timer = new System.Windows.Forms.Timer { Interval = 1000 })
                {
                    var handle = queue.Handle;
                    Register(); ready.Set();
                    timer.Tick += delegate
                    {
                        if (stopping) { Application.ExitThread(); return; }
                        if (ShortcutPolicy.Renew(Enabled, stopping, AnyKeyDown(), clock.ElapsedMilliseconds - registeredAt, repairRequested)) Register();
                    };
                    timer.Start(); if (!stopping) Application.Run();
                }
            }
            catch (Exception ex) { ShortcutLog.Write("hook thread stopped: " + ex.GetType().Name); try { ready.Set(); } catch { } }
            finally { installed = false; if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }
        bool Post(int value)
        {
            if (Interlocked.Increment(ref queued) > 16) { Interlocked.Decrement(ref queued); return false; }
            if (WindowNative.PostMessage(target, (uint)ActionMessage, new IntPtr(value), IntPtr.Zero)) return true;
            Interlocked.Decrement(ref queued); return false;
        }
        internal void Deliver(int value)
        {
            Interlocked.Decrement(ref queued);
            if (stopping) return;
            if (value == 8) { if (Released != null) Released(); }
            else if (Enabled && Pressed != null) Pressed((value & 2) != 0, (value & 4) != 0);
        }
        IntPtr OnKey(int code, IntPtr wp, IntPtr lp)
        {
            try
            {
                if (code >= 0 && !stopping)
                {
                    Interlocked.Increment(ref callbacks);
                    var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lp, typeof(Native.KBDLLHOOKSTRUCT));
                    int message = wp.ToInt32(), vk = (int)k.vkCode;
                    bool down = message == 0x100 || message == 0x104, up = message == 0x101 || message == 0x105;
                    if (vk >= 0 && vk < 256 && (vk == 0x10 || vk == 0x11 || vk == 0x12 || vk >= 0xA0 && vk <= 0xA5)) modifiers[vk] = down;
                    bool alt = modifiers[0x12] || modifiers[0xA4] || modifiers[0xA5] || (k.flags & 0x20) != 0;
                    bool ctrl = modifiers[0x11] || modifiers[0xA2] || modifiers[0xA3];
                    bool shift = modifiers[0x10] || modifiers[0xA0] || modifiers[0xA1];
                    if (vk == 9)
                    {
                        if (up && swallowTabUp) { swallowTabUp = false; return new IntPtr(1); }
                        if (down && Enabled && alt && Post(ShortcutPolicy.Flags(ctrl, shift)))
                        { swallowTabUp = true; session = true; return new IntPtr(1); }
                    }
                    if (up && session && (vk == 0x12 || vk == 0xA4 || vk == 0xA5))
                    { session = false; Post(8); }
                }
            }
            catch { }
            return Native.CallNextHookEx(IntPtr.Zero, code, wp, lp);
        }
        public void Dispose() { Enabled = false; stopping = true; }
    }
    sealed partial class Switcher
    {
        readonly System.Windows.Forms.Timer shortcutRecoveryTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        DateTime shortcutFaultWindow, resumeShortcutsAt, lastHookRetry;
        int shortcutFaultCount;
        bool shortcutRecoveryReady;
        internal static Switcher Resident;
        void SetupShortcutRecovery()
        {
            Resident = this; shortcutRecoveryReady = true;
            ShortcutLog.Write("resident started; pid=" + Process.GetCurrentProcess().Id);
            shortcutRecoveryTimer.Tick += delegate
            {
                if (closing) return;
                if (options.InterceptAltTab && !hook.ThreadAlive && resumeShortcutsAt == DateTime.MinValue && (DateTime.UtcNow - lastHookRetry).TotalSeconds >= 5)
                { lastHookRetry = DateTime.UtcNow; hook.RequestRepair(); }
                if (resumeShortcutsAt == DateTime.MinValue || DateTime.UtcNow < resumeShortcutsAt) return;
                resumeShortcutsAt = DateTime.MinValue;
                RepairShortcuts(false);
            };
            shortcutRecoveryTimer.Start();
        }
        internal void RepairShortcuts(bool explicitAction)
        {
            if (closing || !shortcutRecoveryReady) return;
            if (explicitAction) { resumeShortcutsAt = DateTime.MinValue; shortcutFaultCount = 0; }
            hook.Enabled = options.InterceptAltTab; interceptItem.Checked = options.InterceptAltTab;
            hook.UpdateTarget(Handle); hook.RequestRepair();
            Native.UnregisterHotKey(Handle, 10);
            if (!Native.RegisterHotKey(Handle, 10, 0x4000 | 0x1 | 0x2, 0x20)) ShortcutLog.Write("Ctrl+Alt+Space registration unavailable; win32=" + Marshal.GetLastWin32Error());
            ShortcutLog.Write(explicitAction ? "explicit shortcut repair requested" : "shortcut handling restored after temporary cooldown");
        }
        internal void RecoverUiFault(Exception ex)
        {
            if (closing || IsDisposed) return;
            Program.Log("Recovered UI error: " + ex);
            ShortcutLog.Write("UI error: " + ex.GetType().Name + "; retained saved Alt+Tab preference");
            try { CancelPendingLaunch(); CancelActivation(); Dismiss(); } catch { }
            if ((DateTime.UtcNow - shortcutFaultWindow).TotalSeconds > 60) { shortcutFaultWindow = DateTime.UtcNow; shortcutFaultCount = 0; }
            shortcutFaultCount++;
            if (hook == null) return;
            if (shortcutFaultCount >= 3)
            {
                hook.Enabled = false; resumeShortcutsAt = DateTime.UtcNow.AddSeconds(10);
                Notify("Taskbar Tiles hit repeated interface errors. Native Alt+Tab is available for 10 seconds, then handling will recover. Settings are unchanged. See shortcut diagnostics.");
            }
            else
            {
                hook.Enabled = options.InterceptAltTab;
                Notify("Taskbar Tiles closed a failed menu safely. Try your shortcut again. Your preferences are unchanged.");
            }
        }
        internal string ShortcutStatus { get { return hook == null ? "Not started" : hook.Status; } }
        internal void OpenShortcutHealth()
        {
            if (transient != null) return;
            CancelPassiveLaunchObservation(); Dismiss();
            using (var form = new Form { Text = "Taskbar Tiles - Shortcut health", StartPosition = FormStartPosition.CenterScreen,
                Size = new System.Drawing.Size(720, 440), MinimumSize = new System.Drawing.Size(560, 340), BackColor = Theme.Background, ForeColor = Theme.Text, ShowInTaskbar = false })
            {
                var text = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BackColor = Theme.Background, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50 };
                var repair = Theme.Button("Repair shortcuts", 155); var refresh = Theme.Button("Refresh", 95); var logs = Theme.Button("Open diagnostics", 160); var close = Theme.Button("Close", 85);
                Action show = delegate { text.Text = "Taskbar Tiles " + Program.Version + "\r\n\r\n" + hook.Status + "\r\n\r\nCtrl+Alt+Space and the X-Mouse --toggle command do not depend on the Alt+Tab hook.\r\n\r\nWindows can silently remove a timed-out hook. Registration is renewed only when keys are released; key events are never logged or injected for testing."; };
                repair.Click += delegate { RepairShortcuts(true); show(); }; refresh.Click += delegate { show(); };
                logs.Click += delegate { if (!File.Exists(ShortcutLog.PathName)) ShortcutLog.Write("diagnostics opened"); OpenFile(ShortcutLog.PathName); };
                close.Click += delegate { form.Close(); };
                buttons.Controls.AddRange(new Control[] { repair, refresh, logs, close }); form.Controls.Add(text); form.Controls.Add(buttons); show();
                transient = form; try { form.ShowDialog(); } finally { transient = null; }
            }
        }
        void DisposeShortcutRecovery()
        { if (ReferenceEquals(Resident, this)) Resident = null; shortcutRecoveryTimer.Stop(); shortcutRecoveryTimer.Dispose(); }
    }
}
