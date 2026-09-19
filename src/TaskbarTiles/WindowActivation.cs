// A selected HWND is the target, never "whichever window is in this process".
// No synthetic Alt keys, AttachThreadInput, focus-lock changes or topmost toggles.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskbarTiles
{
    interface IActivationApi
    {
        bool Exists(IntPtr h);
        bool Visible(IntPtr h);
        bool Enabled(IntPtr h);
        bool Minimized(IntPtr h);
        uint ProcessId(IntPtr h);
        IntPtr Owner(IntPtr h);
        IntPtr LastPopup(IntPtr h);
        IntPtr Foreground { get; }
        bool TryInputStamp(out uint stamp);
        void Restore(IntPtr h);
        void Raise(IntPtr h);
        bool Activate(IntPtr h);
        void GrantForeground(uint pid);
    }

    enum ActivationState { Pending, Succeeded, Cancelled, Failed }

    static class ActivationPolicy
    {
        internal static bool OwnedBy(IActivationApi api, IntPtr candidate, IntPtr owner)
        {
            var seen = new HashSet<IntPtr>();
            for (int depth = 0; depth < 32 && candidate != IntPtr.Zero && seen.Add(candidate); depth++)
            {
                candidate = api.Owner(candidate);
                if (candidate == owner) return true;
            }
            return false;
        }
        internal static IntPtr Resolve(IActivationApi api, IntPtr selected)
        {
            if (!api.Exists(selected)) return IntPtr.Zero;
            // An enabled window must not be replaced by an unrelated owned palette.
            if (api.Enabled(selected)) return selected;
            IntPtr current = selected;
            var seen = new HashSet<IntPtr>();
            for (int depth = 0; depth < 16 && seen.Add(current); depth++)
            {
                IntPtr popup = api.LastPopup(current);
                if (popup == IntPtr.Zero || popup == current || !api.Exists(popup) || !api.Visible(popup) ||
                    !OwnedBy(api, popup, selected)) break;
                if (api.Enabled(popup)) return popup;
                current = popup;
            }
            return selected;
        }
        internal static bool Matches(IActivationApi api, IntPtr selected, IntPtr effective, IntPtr foreground)
        {
            // Do NOT accept another top-level window merely because its PID matches.
            if (foreground == effective) return api.Exists(effective) && api.Visible(effective) && api.Enabled(effective);
            return !api.Enabled(selected) && foreground != IntPtr.Zero && api.Enabled(foreground) &&
                api.Visible(foreground) && OwnedBy(api, foreground, selected);
        }
    }

    // Deterministic state machine: elapsed time and the OS interface are injected.
    // Start() is called WHILE the switcher still owns the foreground. Only afterwards
    // does the UI hide. Step() then verifies the hand-off and any asynchronous restore.
    sealed class ActivationAttempt
    {
        readonly IActivationApi api;
        readonly IntPtr selected, switcher, previousForeground;
        readonly uint expectedPid;
        readonly Action<string> log;
        IntPtr effective;
        uint inputStamp;
        bool trackingInput;
        int stableSince = -1, nextAttempt = 140, attempts;
        internal ActivationState State { get; private set; }
        internal string Error { get; private set; }
        internal int Attempts { get { return attempts; } }
        internal ActivationAttempt(IActivationApi api, IntPtr selected, uint pid, IntPtr switcher,
            IntPtr previousForeground, Action<string> log)
        {
            this.api = api; this.selected = selected; expectedPid = pid; this.switcher = switcher;
            this.previousForeground = previousForeground; this.log = log ?? delegate { };
            State = ActivationState.Pending;
        }
        bool Validate()
        {
            if (expectedPid == 0 || !api.Exists(selected) || api.ProcessId(selected) != expectedPid)
            { Fail("That window has closed or changed. Reopen Taskbar Tiles and choose it again."); return false; }
            return true;
        }
        internal void Start()
        {
            if (!Validate()) return;
            trackingInput = api.TryInputStamp(out inputStamp);
            effective = ActivationPolicy.Resolve(api, selected);
            log("start hwnd=" + selected.ToInt64() + " pid=" + expectedPid + " target=" + effective.ToInt64());
            api.GrantForeground(api.ProcessId(effective));
            if (api.Minimized(selected)) api.Restore(selected);
            if (effective != selected && api.Minimized(effective)) api.Restore(effective);
            Request(); // Must precede hiding the picker.
        }
        void Request()
        {
            attempts++;
            if (!api.Minimized(effective)) api.Raise(effective);
            bool accepted = api.Activate(effective);
            log("request=" + attempts + " accepted=" + accepted + " foreground=" + api.Foreground.ToInt64());
        }
        internal ActivationState Step(int elapsed)
        {
            if (State != ActivationState.Pending || !Validate()) return State;
            IntPtr resolved = ActivationPolicy.Resolve(api, selected);
            if (resolved != effective) { effective = resolved; stableSince = -1; }
            IntPtr foreground = api.Foreground;
            uint stamp;
            bool inputChanged = trackingInput && api.TryInputStamp(out stamp) && stamp != inputStamp;
            bool matched = ActivationPolicy.Matches(api, selected, effective, foreground) &&
                !api.Minimized(selected) && !api.Minimized(effective);
            if (matched)
            {
                if (stableSince < 0) stableSince = elapsed;
                if (inputChanged || elapsed - stableSince >= 180)
                { State = ActivationState.Succeeded; log("foreground verified"); }
                return State;
            }
            stableSince = -1;
            // The user selected/typed into something else. Never steal focus back.
            if (inputChanged) { State = ActivationState.Cancelled; log("cancelled: new user input"); return State; }
            if (elapsed >= 2200)
            { Fail("Windows did not confirm the switch. The app may be busy or blocking activation. Use its taskbar button; switching details are in the local diagnostics."); return State; }
            bool safeForeground = foreground == IntPtr.Zero || foreground == switcher || foreground == previousForeground ||
                foreground == selected || foreground == effective;
            // A bounded retry only while no new input has arrived. No input timestamp
            // means we cannot safely distinguish a failed hand-off from user activity.
            if (trackingInput && safeForeground && elapsed >= nextAttempt && attempts < 4 && !api.Minimized(selected) && !api.Minimized(effective))
            {
                Request(); nextAttempt = elapsed + (attempts < 3 ? 220 : 420);
            }
            return State;
        }
        void Fail(string message) { State = ActivationState.Failed; Error = message; log("failed: " + message); }
    }

    sealed class WindowsActivationApi : IActivationApi
    {
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint size, time; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
        public bool Exists(IntPtr h) { return Native.IsWindow(h); }
        public bool Visible(IntPtr h) { return Native.IsWindowVisible(h); }
        public bool Enabled(IntPtr h) { return IsWindowEnabled(h); }
        public bool Minimized(IntPtr h) { return Native.IsIconic(h); }
        public uint ProcessId(IntPtr h) { return WindowNative.ProcessId(h); }
        public IntPtr Owner(IntPtr h) { return Native.GetWindow(h, 4); }
        public IntPtr LastPopup(IntPtr h) { return Native.GetLastActivePopup(h); }
        public IntPtr Foreground { get { return Native.GetForegroundWindow(); } }
        public bool TryInputStamp(out uint stamp)
        { var info = new LASTINPUTINFO { size = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) }; bool ok = GetLastInputInfo(ref info); stamp = info.time; return ok; }
        public void Restore(IntPtr h) { Native.ShowWindowAsync(h, 9); }
        public void Raise(IntPtr h)
        {
            // HWND_TOP, not HWND_TOPMOST. Preserve position, size and owner ordering.
            // ASYNCWINDOWPOS avoids attaching/waiting on another app's input queue.
            WindowNative.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x4000 | 0x0200 | 0x0010 | 0x0002 | 0x0001);
        }
        public bool Activate(IntPtr h) { return Native.SetForegroundWindow(h); }
        public void GrantForeground(uint pid) { if (pid != 0) Native.AllowSetForegroundWindow(pid); }
    }

    static class ActivationLog
    {
        internal static string PathName { get { return Path.Combine(Program.Home, "switching-diagnostics.log"); } }
        internal static void Write(string text)
        {
            try
            {
                if (File.Exists(PathName) && new FileInfo(PathName).Length > 131072) File.Delete(PathName);
                File.AppendAllText(PathName, DateTime.UtcNow.ToString("o") + " " + text + Environment.NewLine);
            }
            catch { }
        }
    }

    sealed partial class Switcher
    {
        readonly Timer activationTimer = new Timer { Interval = 60 };
        readonly Stopwatch activationClock = new Stopwatch();
        ActivationAttempt activation;
        IntPtr foregroundBeforeMenu;
        void SetupActivation()
        {
            activationTimer.Tick += delegate
            {
                if (activation == null) { activationTimer.Stop(); return; }
                try
                {
                    ActivationState result = activation.Step((int)activationClock.ElapsedMilliseconds);
                    if (result == ActivationState.Pending) return;
                    string error = activation.Error;
                    CancelActivation();
                    if (result == ActivationState.Failed && !closing) Notify(error);
                }
                catch (Exception ex) { CancelActivation(); ActivationLog.Write(ex.ToString()); if (!closing) Notify("Could not activate the selected window. See switching diagnostics."); }
            };
        }
        void ActivateWindow(WindowItem item)
        {
            CancelPassiveLaunchObservation();
            CancelActivation();
            if (item == null) { Dismiss(); return; }
            // Copy the identity before any hide, refresh, modal or asynchronous work.
            IntPtr handle = item.Handle;
            uint pid = item.ProcessId;
            if (pid == 0) pid = WindowNative.ProcessId(handle);
            activation = new ActivationAttempt(new WindowsActivationApi(), handle, pid, Handle, foregroundBeforeMenu, ActivationLog.Write);
            suppressDeactivate = true;
            try
            {
                // Suspend before the first selected-window activation, not after it.
                // Otherwise a queued layer callback could cover the app we just chose.
                switcherLayer.Suspend();
                activationClock.Restart();
                activation.Start();
                Dismiss();
                if (activation.State == ActivationState.Failed)
                { string error = activation.Error; CancelActivation(); Notify(error); }
                else activationTimer.Start();
            }
            catch (Exception ex) { CancelActivation(); Dismiss(); ActivationLog.Write(ex.ToString()); Notify("Could not activate that window. See switching diagnostics."); }
            finally { suppressDeactivate = false; }
        }
        void CancelActivation() { activationTimer.Stop(); activationClock.Stop(); activation = null; }
        void DisposeActivation() { CancelActivation(); activationTimer.Dispose(); }
        void OpenSwitchingDiagnostics()
        { if (!File.Exists(ActivationLog.PathName)) ActivationLog.Write("No switch requests recorded yet."); OpenFile(ActivationLog.PathName); }
    }
}
