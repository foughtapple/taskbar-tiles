using System;
using System.Collections.Generic;
using System.Text;

namespace TaskbarTiles
{
    static class ActivationTests
    {
        sealed class Fake : IActivationApi
        {
            internal readonly HashSet<IntPtr> Deleted = new HashSet<IntPtr>(), Disabled = new HashSet<IntPtr>(), Min = new HashSet<IntPtr>();
            internal readonly Dictionary<IntPtr, IntPtr> Owners = new Dictionary<IntPtr, IntPtr>(), Popups = new Dictionary<IntPtr, IntPtr>();
            internal IntPtr Current = new IntPtr(90), Requested;
            internal uint Input = 100, Pid = 42;
            internal bool Refuse, TrackInput = true;
            internal int Calls, Restores;
            public bool Exists(IntPtr h) { return h != IntPtr.Zero && !Deleted.Contains(h); }
            public bool Visible(IntPtr h) { return Exists(h); }
            public bool Enabled(IntPtr h) { return !Disabled.Contains(h); }
            public bool Minimized(IntPtr h) { return Min.Contains(h); }
            public uint ProcessId(IntPtr h) { return Pid; }
            public IntPtr Owner(IntPtr h) { IntPtr value; return Owners.TryGetValue(h, out value) ? value : IntPtr.Zero; }
            public IntPtr LastPopup(IntPtr h) { IntPtr value; return Popups.TryGetValue(h, out value) ? value : h; }
            public IntPtr Foreground { get { return Current; } }
            public bool TryInputStamp(out uint stamp) { stamp = Input; return TrackInput; }
            public void Restore(IntPtr h) { Restores++; }
            public void Raise(IntPtr h) { }
            public bool Activate(IntPtr h) { Calls++; Requested = h; if (!Refuse && !Min.Contains(h)) Current = h; return !Refuse; }
            public void GrantForeground(uint pid) { }
        }
        static int checks;
        static void Require(bool ok, string name) { checks++; if (!ok) throw new InvalidOperationException("FAILED: " + name); }
        static ActivationAttempt Attempt(Fake api) { return new ActivationAttempt(api, new IntPtr(10), 42, new IntPtr(90), new IntPtr(20), null); }
        internal static void Run(StringBuilder log)
        {
            checks = 0; IntPtr selected = new IntPtr(10), other = new IntPtr(20), popup = new IntPtr(30);
            var api = new Fake(); var a = Attempt(api); a.Start();
            Require(api.Requested == selected && api.Calls == 1, "initial request sent synchronously before UI hide");
            Require(a.Step(20) == ActivationState.Pending && a.Step(210) == ActivationState.Succeeded, "exact HWND stable before success");
            api = new Fake(); a = Attempt(api); a.Start(); api.Current = other;
            Require(a.Step(80) == ActivationState.Pending, "foreground bounce is not success");
            a.Step(150); a.Step(210);
            Require(a.Step(410) == ActivationState.Succeeded && api.Calls == 2, "bounded retry repairs hide/foreground bounce");
            api = new Fake(); api.Min.Add(selected); a = Attempt(api); a.Start();
            Require(api.Restores == 1 && a.Step(100) == ActivationState.Pending, "async restore is not assumed complete");
            api.Min.Remove(selected); a.Step(280); a.Step(340);
            Require(a.Step(550) == ActivationState.Succeeded, "restore completes before foreground success");
            api = new Fake { Refuse = true }; a = Attempt(api); a.Start(); api.Current = other;
            for (int t = 60; t <= 2280; t += 60) a.Step(t);
            Require(a.State == ActivationState.Failed && api.Calls <= 4, "denied activation bounded and reported");
            api = new Fake { Refuse = true }; a = Attempt(api); a.Start(); api.Current = other; api.Input++;
            Require(a.Step(160) == ActivationState.Cancelled && api.Calls == 1, "new user input cancels instead of stealing focus");
            api = new Fake(); a = Attempt(api); a.Start(); api.Pid++;
            Require(a.Step(60) == ActivationState.Failed, "reused HWND with changed PID is rejected");
            api = new Fake(); a = Attempt(api); a.Start(); api.Deleted.Add(selected);
            Require(a.Step(60) == ActivationState.Failed, "closed window is not replaced with a different window");
            api = new Fake(); api.Popups[selected] = popup; api.Owners[popup] = selected;
            Require(ActivationPolicy.Resolve(api, selected) == selected, "enabled window keeps exact identity despite owned palette");
            api.Disabled.Add(selected);
            Require(ActivationPolicy.Resolve(api, selected) == popup, "disabled owner directs to real owned modal");
            Require(ActivationPolicy.Matches(api, selected, popup, popup), "owned modal is an allowed foreground");
            Require(!ActivationPolicy.Matches(api, selected, popup, other), "same process unrelated window never counts as success");
            api.Owners[popup] = other;
            Require(ActivationPolicy.Resolve(api, selected) == selected, "unrelated popup rejected");
            api = new Fake { Refuse = true, TrackInput = false }; a = Attempt(api); a.Start();
            for (int t = 100; t <= 2400; t += 100) a.Step(t);
            Require(a.State == ActivationState.Failed && api.Calls == 1, "no blind retry without last-input tracking");
            api = new Fake(); a = Attempt(api); a.Start(); api.Input++;
            Require(a.Step(30) == ActivationState.Succeeded, "user input already directed to chosen window is success");
            log.AppendLine("PASS: " + checks + " activation state-machine assertions using an injected OS interface (no real focus changes).");
        }
    }
}
