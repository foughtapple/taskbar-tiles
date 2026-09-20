// Dedicated hook pump. The low-level callback only updates bounded state/queues;
// it never calls UI delegates, logs to disk, runs Shell code, or waits for the UI.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class ShortcutDiagnostics
    {
        internal static string PathName { get { return Path.Combine(Program.Home, "shortcut-diagnostics.log"); } }
        internal static void Write(string message)
        {
            try
            {
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 262144)
                { File.Copy(PathName, PathName + ".previous", true); File.Delete(PathName); }
                File.AppendAllText(PathName, DateTime.UtcNow.ToString("o") + " version=" + Program.Version + " pid=" + Process.GetCurrentProcess().Id + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }
    sealed class KeyboardHook : IDisposable
    {
        public volatile bool Enabled = true;
        public Action<bool, bool> Pressed;
        public Action Released;
        readonly ConcurrentQueue<int> signals = new ConcurrentQueue<int>();
        readonly bool[] modifiers = new bool[256];
        readonly Native.HookProc callback;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly System.Windows.Forms.Timer delivery = new System.Windows.Forms.Timer { Interval = 15 };
        IntPtr hook;
        uint threadId;
        bool swallowTabUp, session;
        volatile bool stopped;
        int queued, repairRequested, generation, fault;
        long lastInstalled, lastEvent;
        internal int Generation { get { return Volatile.Read(ref generation); } }
        public bool Installed { get { return !stopped && Volatile.Read(ref generation) > 0 && hook != IntPtr.Zero; } }
        internal string Status { get { return (Enabled ? "Enabled" : "Disabled / temporary recovery pause") + "; hook registration " + Generation + ". Registration is not proof Windows has retained a hook."; } }
        public KeyboardHook()
        {
            callback = OnKey;
            delivery.Tick += delegate { Drain(); }; delivery.Start();
            var worker = new Thread(Run) { IsBackground = true, Name = "Taskbar Tiles shortcut pump" };
            worker.SetApartmentState(ApartmentState.MTA); worker.Start();
            if (ready.WaitOne(2000)) ready.Dispose();
        }
        void Ready() { try { ready.Set(); } catch (ObjectDisposedException) { } }
        void Run()
        {
            try
            {
                threadId = Native.GetCurrentThreadId();
                using (var queue = new Control())
                using (var maintenance = new System.Windows.Forms.Timer { Interval = 1000 })
                {
                    var hwnd = queue.Handle;
                    Install(); Ready();
                    maintenance.Tick += delegate
                    {
                        // Windows has no hook-revocation notification. Renew conservatively
                        // between gestures, never by injecting a probe key into user apps.
                        bool due = Volatile.Read(ref repairRequested) != 0 || hook == IntPtr.Zero ||
                            (Stopwatch.GetTimestamp() - lastInstalled) / (double)Stopwatch.Frequency >= 30;
                        if (!stopped && due && IdleKeys()) Install();
                    };
                    maintenance.Start(); if (!stopped) Application.Run();
                }
            }
            catch (Exception ex) { ShortcutDiagnostics.Write("hook-thread failed: " + ex.GetType().Name); Ready(); }
            finally { if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }
        bool IdleKeys()
        {

            foreach (int key in new[] { 9, 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
                if (Native.Down(key)) return false;
            if (swallowTabUp || session)
            {
                if ((Stopwatch.GetTimestamp() - lastEvent) / (double)Stopwatch.Frequency < 2) return false;
                swallowTabUp = session = false; Array.Clear(modifiers, 0, modifiers.Length);
            }
            return true;
        }
        void Install()
        {
            if (stopped) return;
            // Make-before-break on this pump thread; no key can run between these calls.
            IntPtr next = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
            if (next == IntPtr.Zero) { Interlocked.Exchange(ref fault, Marshal.GetLastWin32Error()); return; }
            IntPtr old = hook; hook = next;
            if (old != IntPtr.Zero) Native.UnhookWindowsHookEx(old);
            Array.Clear(modifiers, 0, modifiers.Length);
            swallowTabUp = session = false;
            lastInstalled = Stopwatch.GetTimestamp(); Interlocked.Exchange(ref repairRequested, 0);
            Interlocked.Increment(ref generation);
        }
        public void Repair() { Interlocked.Exchange(ref repairRequested, 1); }
        void Enqueue(int signal)
        {
            if (Interlocked.Increment(ref queued) > 64) { Interlocked.Decrement(ref queued); return; }
            signals.Enqueue(signal);
        }
        internal void Drain()
        {
            int error = Interlocked.Exchange(ref fault, 0);
            if (error != 0) ShortcutDiagnostics.Write("hook registration error=" + error);
            int signal;
            while (!stopped && signals.TryDequeue(out signal))
            {
                Interlocked.Decrement(ref queued);
                try
                {
                    if (signal == 4) { if (Released != null) Released(); }
                    else if (Pressed != null) Pressed((signal & 1) != 0, (signal & 2) != 0);
                }
                catch (Exception ex) { ShortcutDiagnostics.Write("shortcut delivery failed: " + ex.GetType().Name); Repair(); }
            }
        }
        IntPtr OnKey(int code, IntPtr wp, IntPtr lp)
        {
            try
            {
                if (!stopped && code >= 0)
                {
                    var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lp, typeof(Native.KBDLLHOOKSTRUCT));
                    lastEvent = Stopwatch.GetTimestamp();
                    int msg = wp.ToInt32(), vk = (int)k.vkCode;
                    bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                    if ((down || up) && (vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5))) modifiers[vk] = down;
                    bool alt = modifiers[0x12] || modifiers[0xA4] || modifiers[0xA5] || (k.flags & 0x20) != 0;
                    if (vk == 9)
                    {
                        if (up && swallowTabUp) { swallowTabUp = false; return new IntPtr(1); }
                        if (down && Enabled && alt)
                        {
                            swallowTabUp = session = true;
                            bool ctrl = modifiers[0x11] || modifiers[0xA2] || modifiers[0xA3];
                            bool shift = modifiers[0x10] || modifiers[0xA0] || modifiers[0xA1];
                            Enqueue((ctrl ? 1 : 0) | (shift ? 2 : 0)); return new IntPtr(1);
                        }
                    }
                    if (up && session && (vk == 0x12 || vk == 0xA4 || vk == 0xA5))
                    { session = false; Enqueue(4); }
                }
            }
            catch { Interlocked.Exchange(ref repairRequested, 1); }
            return Native.CallNextHookEx(IntPtr.Zero, code, wp, lp);
        }
        internal void TestDeliver() { Enqueue(0); Drain(); }
        internal void TestRevoke() { if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook); Repair(); }
        public void Dispose()
        {
            if (stopped) return; stopped = true; Enabled = false; delivery.Stop(); delivery.Dispose();
            if (threadId != 0) Native.PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero);
        }
    }
    sealed partial class Switcher
    {
        readonly System.Windows.Forms.Timer shortcutRecoveryTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        DateTime shortcutFaultWindow;
        int shortcutFaults;
        void SetupShortcutRecovery()
        {
            ShortcutDiagnostics.Write("resident started; " + hook.Status);
            Microsoft.Win32.SystemEvents.PowerModeChanged += ShortcutPowerChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch += ShortcutSessionChanged;
            shortcutRecoveryTimer.Tick += delegate
            {
                shortcutRecoveryTimer.Stop(); if (closing) return;
                hook.Enabled = options.InterceptAltTab; interceptItem.Checked = options.InterceptAltTab; hook.Repair();
                ShortcutDiagnostics.Write("temporary navigation recovery ended; requested Alt+Tab=" + options.InterceptAltTab);
            };
        }
        void ShortcutPowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e) { Post(delegate { RepairShortcuts(); }); }
        void ShortcutSessionChanged(object sender, Microsoft.Win32.SessionSwitchEventArgs e) { Post(delegate { RepairShortcuts(); }); }
        void DisposeShortcutRecovery()
        {
            shortcutRecoveryTimer.Stop(); shortcutRecoveryTimer.Dispose();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= ShortcutPowerChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch -= ShortcutSessionChanged;
            ShortcutDiagnostics.Write("resident exiting normally");
        }
        void NavigationFailed(Exception ex)
        {
            ShortcutDiagnostics.Write("navigation fault: " + ex.GetType().Name + Environment.NewLine + ex.StackTrace);
            if ((DateTime.UtcNow - shortcutFaultWindow).TotalSeconds > 30) { shortcutFaultWindow = DateTime.UtcNow; shortcutFaults = 0; }
            shortcutFaults++;
            // Preserve the user's preference. A transient UI error must not silently
            // turn replacement off for the rest of the process lifetime.
            if (hook != null) hook.Enabled = false;
            CancelPendingLaunch(); Dismiss();
            shortcutRecoveryTimer.Stop(); shortcutRecoveryTimer.Interval = shortcutFaults >= 3 ? 30000 : 1500; shortcutRecoveryTimer.Start();
            if (shortcutFaults == 1) Notify("Taskbar Tiles recovered from a menu error. Shortcuts are being repaired; your settings are unchanged.");
        }
        void RepairShortcuts()
        {
            if (closing || hook == null) return;
            shortcutRecoveryTimer.Stop(); shortcutFaults = 0;
            hook.Enabled = options.InterceptAltTab; interceptItem.Checked = options.InterceptAltTab; hook.Repair();
            ShortcutDiagnostics.Write("explicit shortcut repair; " + hook.Status);
        }
    }
}
