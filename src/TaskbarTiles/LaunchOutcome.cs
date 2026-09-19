// Observe one normal launch. The target application, not a product-name list,
// decides whether that request creates a window or reuses an existing window.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class LaunchCandidate
    {
        internal WindowRecord Window;
        internal bool IsNew, Ready, Responded, Foreground;
    }
    enum LaunchOutcomeKind { Waiting, NewWindow, ReusedWindow, Ambiguous, Unidentified }
    sealed class LaunchOutcomeDecision
    {
        internal LaunchOutcomeKind Kind;
        internal WindowRecord Window;
        internal LaunchOutcomeDecision(LaunchOutcomeKind kind, WindowRecord window = null) { Kind = kind; Window = window; }
    }
    // Pure policy shared by ordinary launches and zone launches. Elapsed time is
    // injected; no product-name heuristics or remembered single-instance labels.
    sealed class LaunchOutcomeSelector
    {
        sealed class Seen { internal uint Pid; internal long Start; internal int Since, ResponseSince; internal bool Responded; }
        readonly Dictionary<IntPtr, Seen> seen = new Dictionary<IntPtr, Seen>();
        internal LaunchOutcomeDecision Step(IList<LaunchCandidate> matching, int elapsedMs, int timeoutMs,
            bool allowReuse, bool requireNew, bool forPlacement)
        {
            var handles = new HashSet<IntPtr>(matching.Select(c => c.Window.Handle));
            foreach (var key in seen.Keys.Where(h => !handles.Contains(h)).ToArray()) seen.Remove(key);
            foreach (var c in matching)
            {
                Seen state;
                if (!seen.TryGetValue(c.Window.Handle, out state) || state.Pid != c.Window.ProcessId || state.Start != c.Window.ProcessStartTicks)
                { state = new Seen { Pid = c.Window.ProcessId, Start = c.Window.ProcessStartTicks, Since = elapsedMs, ResponseSince = elapsedMs }; seen[c.Window.Handle] = state; }
                if (!c.Ready) state.Since = elapsedMs;
                if (!c.Responded || !state.Responded) state.ResponseSince = elapsedMs;
                state.Responded = c.Responded;
            }
            var fresh = matching.Where(c => c.IsNew).ToList();
            var selected = fresh.Count == 1 ? fresh[0] : fresh.FirstOrDefault(c => c.Foreground);
            if (selected != null && selected.Ready && elapsedMs >= 1200 && elapsedMs - seen[selected.Window.Handle].Since >= 800)
                return new LaunchOutcomeDecision(LaunchOutcomeKind.NewWindow, selected.Window);
            // A slow/new construction window always takes priority over old windows.
            if (fresh.Count > 0) return new LaunchOutcomeDecision(elapsedMs >= timeoutMs ? LaunchOutcomeKind.Ambiguous : LaunchOutcomeKind.Waiting);
            var existing = matching.Where(c => !c.IsNew && c.Ready).ToList();
            if (allowReuse && !requireNew)
            {
                // Early completion is safe for ordinary launches only when the app
                // itself exposed/focused its old window; no old window is moved early.
                var responses = existing.Where(c => c.Responded).ToList();
                var foreground = responses.FirstOrDefault(c => c.Foreground);
                var reuse = foreground ?? (responses.Count == 1 ? responses[0] : null);
                bool deadline = elapsedMs >= timeoutMs;
                if (reuse != null && elapsedMs >= 1200 && (deadline || !forPlacement) &&
                    elapsedMs - seen[reuse.Window.Handle].ResponseSince >= 800)
                    return new LaunchOutcomeDecision(LaunchOutcomeKind.ReusedWindow, reuse.Window);
                // Some single-instance programs ignore a second launch while their
                // window is covered/minimised. At the deadline only ONE verified
                // existing match is safe to restore, even if it never changed focus.
                if (deadline && existing.Count == 1 && elapsedMs - seen[existing[0].Window.Handle].Since >= 800)
                    return new LaunchOutcomeDecision(LaunchOutcomeKind.ReusedWindow, existing[0].Window);
            }
            return new LaunchOutcomeDecision(elapsedMs < timeoutMs ? LaunchOutcomeKind.Waiting :
                matching.Count > 1 ? LaunchOutcomeKind.Ambiguous : LaunchOutcomeKind.Unidentified);
        }
        internal void Reset() { seen.Clear(); }
    }

    sealed class LaunchPlacement : IDisposable
    {
        readonly AppButton app;
        readonly Options options;
        readonly string requestId;
        readonly Action<IntPtr, string> finished;
        readonly Dictionary<IntPtr, WindowRecord> cache = new Dictionary<IntPtr, WindowRecord>();
        readonly Dictionary<IntPtr, uint> before;
        readonly Dictionary<IntPtr, bool> beforeMinimized;
        readonly IntPtr foregroundBefore;
        readonly Timer timer = new Timer { Interval = 180 };
        readonly Stopwatch clock = new Stopwatch();
        readonly LaunchOutcomeSelector selector = new LaunchOutcomeSelector();
        readonly WindowsActivationApi input = new WindowsActivationApi();
        bool inputKnown;
        uint initialInput;
        bool completed;
        int lastNew = -1, lastMatches = -1, lastExisting = -1;
        readonly HashSet<string> evidenceLogged = new HashSet<string>();
        LaunchReceipt receipt;
        public Form ActiveDialog { get; private set; }
        internal bool ForPlacement { get; private set; }
        internal WindowRecord SelectedWindow { get; private set; }
        public LaunchPlacement(AppButton requested, Options settings, string id, Action<IntPtr, string> callback, bool forPlacement = true)
        {
            app = requested; options = settings; finished = callback; requestId = id; ForPlacement = forPlacement;
            var initial = WindowInventory.Read(cache);
            before = initial.ToDictionary(w => w.Handle, w => w.ProcessId);
            beforeMinimized = initial.ToDictionary(w => w.Handle, w => Native.IsIconic(w.Handle));
            foregroundBefore = Native.GetForegroundWindow();
            inputKnown = input.TryInputStamp(out initialInput);
            timer.Tick += Tick;
        }
        public void Begin(LaunchReceipt accepted)
        {
            if (completed) return;
            receipt = accepted ?? new LaunchReceipt(); clock.Restart();
            LaunchLog.Write(requestId, "tracking outcome; version=" + Program.Version + "; zone=" + ForPlacement +
                "; initial windows=" + before.Count + "; require new=" + receipt.RequireNewWindow +
                "; expected=" + LaunchResolution.Describe(receipt.ExpectedAppId, receipt.ExpectedExe));
            timer.Start();
        }
        public void Fail(string error) { Complete(null, error, "dispatch failed"); }
        bool New(WindowRecord w) { return LaunchIdentity.IsNew(w, before); }
        bool Match(WindowRecord w) { return LaunchIdentity.Matches(app, w, receipt); }
        bool UserMovedElsewhere(IntPtr foreground, IList<WindowRecord> matching)
        {
            uint stamp;
            // Mouse motion alone while waiting in the same foreground does not cancel.
            // But never pull the user back after they interact with another app.
            return inputKnown && input.TryInputStamp(out stamp) && stamp != initialInput && foreground != IntPtr.Zero &&
                foreground != foregroundBefore && WindowNative.ProcessId(foreground) != (uint)Process.GetCurrentProcess().Id &&
                !matching.Any(w => w.Handle == foreground || SwitcherLayerPolicy.OwnedBy(foreground, w.Handle));
        }
        void Tick(object sender, EventArgs e)
        {
            try
            {
                var now = WindowInventory.Read(cache);
                var matching = now.Where(Match).ToList();
                int newCount = now.Count(New), matchingNew = matching.Count(New), existingMatches = matching.Count - matchingNew;
                if (newCount != lastNew || matchingNew != lastMatches || existingMatches != lastExisting)
                {
                    lastNew = newCount; lastMatches = matchingNew; lastExisting = existingMatches;
                    LaunchLog.Write(requestId, "new windows=" + newCount + "; matching new=" + matchingNew + "; matching existing=" + existingMatches);
                }
                foreach (var observed in now.Where(w => New(w) || Match(w)))
                {
                    string evidence = "hwnd=" + observed.Handle.ToInt64().ToString("X") + "; owner pid=" + observed.ProcessId +
                        "; app pid=" + observed.AppProcessId + "; " + LaunchResolution.Describe(observed.AppId, observed.Exe) +
                        "; " + LaunchResolution.Reason(app, observed, receipt);
                    if (evidenceLogged.Count < 96 && evidenceLogged.Add(evidence)) LaunchLog.Write(requestId, evidence);
                }
                IntPtr foreground = Native.GetForegroundWindow();
                if (UserMovedElsewhere(foreground, matching)) { Complete(null, null, "observation cancelled: user changed app"); return; }
                var candidates = new List<LaunchCandidate>();
                foreach (var w in matching)
                {
                    bool minimized = Native.IsIconic(w.Handle), wasMinimized;
                    Rectangle bounds = WindowNative.VisibleBounds(w.Handle);
                    bool active = foreground == w.Handle || SwitcherLayerPolicy.OwnedBy(foreground, w.Handle);
                    bool responded = (active && foreground != foregroundBefore) ||
                        (beforeMinimized.TryGetValue(w.Handle, out wasMinimized) && wasMinimized && !minimized);
                    candidates.Add(new LaunchCandidate { Window = w, IsNew = New(w), Foreground = active, Responded = responded,
                        Ready = minimized || (bounds.Width >= 64 && bounds.Height >= 40) });
                }
                var result = selector.Step(candidates, (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds),
                    options.LaunchTimeoutSeconds * 1000, options.ReuseSingleInstance, receipt.RequireNewWindow, ForPlacement);
                if (result.Window != null) { Complete(result.Window, null, result.Kind.ToString()); return; }
                if (result.Kind == LaunchOutcomeKind.Waiting) return;
                // Ordinary shortcuts can intentionally open a URL/document/background
                // task. Do not interrupt a successful launch with an unsolicited modal.
                if (!ForPlacement) { Complete(null, null, "request sent; no unambiguous window, no forced switch"); return; }
                Ask(Shortlist(now), result.Kind == LaunchOutcomeKind.Ambiguous
                    ? "Several windows match this app. Choose the window to place."
                    : "The launch request was sent. The app may have opened or reused a window. Select it, refresh, or wait longer.");
            }
            catch (Exception ex) { Complete(null, "Could not identify the launched window: " + ex.Message, "tracking error"); }
        }
        List<WindowRecord> Shortlist(List<WindowRecord> now) { return now.Where(w => New(w) || Match(w)).ToList(); }
        void Ask(List<WindowRecord> windows, string reason)
        {
            timer.Stop(); LaunchLog.Write(requestId, "manual choice required; candidates=" + windows.Count);
            WindowRecord selected = null; bool wait = false; DialogResult result;
            bool all = windows.Count == 0;
            if (all)
            {
                windows = WindowInventory.Read(cache);
                reason += "\nNo identity match: showing all open windows for manual selection, not an automatic move.";
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
                selector.Reset(); clock.Restart(); inputKnown = input.TryInputStamp(out initialInput);
                LaunchLog.Write(requestId, "wait extended without re-launch"); timer.Start(); return;
            }
            if (result == DialogResult.OK && selected != null) Complete(selected, null, "manual selection");
            else Complete(null, null, "placement cancelled; launched apps stay open");
        }
        void Complete(WindowRecord window, string error, string reason)
        {
            if (completed) return;
            completed = true; timer.Stop(); clock.Stop();
            if (window != null && (!Native.IsWindow(window.Handle) || WindowNative.ProcessId(window.Handle) != window.ProcessId ||
                (window.ProcessStartTicks != 0 && PackageIdentity.StartTicks(window.ProcessId) != window.ProcessStartTicks)))
            { window = null; error = "The identified window closed or changed before it could be selected."; }
            SelectedWindow = window;
            LaunchLog.Write(requestId, "outcome=" + reason + (window == null ? "" : "; hwnd=" + window.Handle.ToInt64().ToString("X") + "; pid=" + window.ProcessId));
            finished(window == null ? IntPtr.Zero : window.Handle, error);
        }
        public void Dispose()
        { completed = true; timer.Stop(); clock.Stop(); timer.Dispose(); if (ActiveDialog != null && !ActiveDialog.IsDisposed) ActiveDialog.Close(); }
    }
    sealed partial class Switcher
    {
        // A new deliberate navigation action supersedes a passive launch observation.
        // It never retracts/repeats a launch request already given to the app.
        void CancelPassiveLaunchObservation()
        {
            if (launchPlacement == null || launchPlacement.ForPlacement) return;
            var old = launchPlacement; launchPlacement = null; old.Dispose();
        }
    }
}
