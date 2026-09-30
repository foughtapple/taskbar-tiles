// Opt-in stable updates. All UI/operation decisions and installer dispatch stay
// on the UI thread; network and checksum work run in one cancellable worker.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class AutomaticUpdateSnapshot
    {
        internal bool Enabled, Installed, Hidden, Busy;
        internal double IdleSeconds;
    }
    static class AutomaticUpdatePolicy
    {
        internal static bool CanInstall(AutomaticUpdateSnapshot state)
        { return state != null && state.Enabled && state.Installed && state.Hidden && !state.Busy && state.IdleSeconds >= 300; }
        internal static bool InstalledHome(string home, string localAppData)
        {
            if (!LaunchIdentity.FullPath(home) || !LaunchIdentity.FullPath(localAppData)) return false;
            try { return string.Equals(Path.GetFullPath(home).TrimEnd('\\'), Path.GetFullPath(Path.Combine(localAppData, "TaskbarTiles")).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        internal static bool Newer(AvailableUpdate update, string current)
        { Version version; return update != null && update.Version != null && Version.TryParse(current, out version) && update.Version > version; }
        internal const string SilentArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TTLAUTOUPDATE=1";
    }
    sealed class AutomaticUpdateSchedule
    {
        internal DateTime Next;
        int failures;
        internal AutomaticUpdateSchedule(DateTime now) { Next = now.AddMinutes(10); }
        internal bool Due(DateTime now) { return now >= Next; }
        internal void Success(DateTime now) { failures = 0; Next = now.AddHours(24); }
        internal void Failure(DateTime now)
        { failures = Math.Min(8, failures + 1); Next = now.AddMinutes(Math.Min(1440, 15 * Math.Pow(2, failures - 1))); }
    }
    sealed class AutomaticUpdateGate : IDisposable
    {
        int state; // 0 available, 1 one worker, 2 disposed.
        internal bool Begin() { return Interlocked.CompareExchange(ref state, 1, 0) == 0; }
        internal void End() { Interlocked.CompareExchange(ref state, 0, 1); }
        public void Dispose() { Interlocked.Exchange(ref state, 2); }
    }
    static class AutomaticUpdateStatus
    {
        internal static string Text = "Automatic updates are off until enabled.";
    }
    static class AutomaticUpdateInput
    {
        [StructLayout(LayoutKind.Sequential)] struct LastInput { internal uint Size, Tick; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput input);
        internal static double IdleSeconds()
        {
            var input = new LastInput { Size = (uint)Marshal.SizeOf(typeof(LastInput)) };
            return GetLastInputInfo(ref input) ? unchecked((uint)Environment.TickCount - input.Tick) / 1000.0 : 0;
        }
    }
    sealed class AutomaticUpdater : IDisposable
    {
        readonly Control owner;
        readonly Func<AutomaticUpdateSnapshot> snapshot;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 60000 };
        readonly AutomaticUpdateGate gate = new AutomaticUpdateGate();
        readonly AutomaticUpdateSchedule schedule = new AutomaticUpdateSchedule(DateTime.UtcNow);
        CancellationTokenSource cancellation = new CancellationTokenSource();
        AvailableUpdate pending;
        string downloaded, hash;
        Process installer;
        bool disposed, installing, previouslyEnabled;
        internal AutomaticUpdater(Control owner, Func<AutomaticUpdateSnapshot> snapshot)
        { this.owner = owner; this.snapshot = snapshot; timer.Tick += delegate { Tick(); }; timer.Start(); }
        void Status(string text) { AutomaticUpdateStatus.Text = text; }
        bool UI(Action action)
        {
            if (disposed || owner.IsDisposed || !owner.IsHandleCreated) return false;
            try { owner.BeginInvoke(new Action(delegate { if (!disposed && !owner.IsDisposed) action(); })); return true; }
            catch (InvalidOperationException) { return false; }
        }
        void CompleteOnUI(Action action)
        { if (!UI(delegate { try { action(); } finally { gate.End(); } })) gate.End(); }
        void Tick()
        {
            if (disposed) return;
            if (installing)
            {
                try
                {
                    if (installer == null || !installer.HasExited) return;
                    int code = installer.ExitCode; installer.Dispose(); installer = null; installing = false;
                    Program.Log("Automatic update Setup exit code: " + code);
                    if (code == 0) { schedule.Success(DateTime.UtcNow); pending = null; downloaded = hash = null; Status("Setup completed. Restart Taskbar Tiles if the version has not changed."); }
                    else Failed(new IOException("Setup did not complete; exit code " + code + "."));
                }
                catch (Exception ex) { installing = false; if (installer != null) { installer.Dispose(); installer = null; } Failed(ex); }
                return;
            }
            var state = snapshot();
            if (!state.Enabled || !state.Installed)
            {
                if (previouslyEnabled) { cancellation.Cancel(); pending = null; downloaded = hash = null; }
                previouslyEnabled = false;
                Status(state.Enabled ? "Automatic installation is available in the installed per-user copy." : "Automatic updates are off.");
                return;
            }
            if (!previouslyEnabled)
            {
                // A cancelled HTTP operation may still be unwinding. The gate
                // prevents its replacement from overlapping with it.
                cancellation.Dispose(); cancellation = new CancellationTokenSource(); previouslyEnabled = true;
                Status("Automatic updates enabled. Stable releases are checked daily.");
            }
            if (pending != null)
            {
                if (!AutomaticUpdatePolicy.CanInstall(state)) { Status(pending.Tag + " is ready; waiting for five minutes of idle time and no active app operations."); return; }
                if (!schedule.Due(DateTime.UtcNow)) return;
                if (!gate.Begin()) return;
                var update = pending; var token = cancellation.Token;
                Status("Verifying " + update.Tag + " for automatic installation...");
                Task.Factory.StartNew(delegate
                {
                    try
                    {
                        string file = downloaded, expected = hash;
                        if (file == null) file = UpdateTransport.Download(update, token, null, out expected);
                        token.ThrowIfCancellationRequested();
                        UpdateTransport.VerifyInstaller(file, update, expected);
                        CompleteOnUI(delegate
                        {
                            if (token.IsCancellationRequested) return;
                            downloaded = file; hash = expected;
                            if (!AutomaticUpdatePolicy.CanInstall(snapshot())) { Status("Verified update is waiting for idle time."); return; }
                            // Recheck immediately before dispatch; never remove the
                            // Internet marker or replace OS certificate policy.
                            try
                            {
                                UpdateTransport.VerifyInstaller(file, update, expected);
                                if (!AutomaticUpdatePolicy.CanInstall(snapshot()) || token.IsCancellationRequested) return;
                                installer = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true,
                                    Arguments = AutomaticUpdatePolicy.SilentArguments, WorkingDirectory = Path.GetDirectoryName(file) });
                                if (installer == null) throw new IOException("Windows did not start the updater.");
                                installing = true; Status("Installing " + update.Tag + ". Setup preserves your settings and shortcuts.");
                            }
                            catch (Exception ex) { Failed(ex); }
                        });
                    }
                    catch (Exception ex)
                    { if (!token.IsCancellationRequested) CompleteOnUI(delegate { downloaded = hash = null; Failed(ex); }); else gate.End(); }
                });
                return;
            }
            if (!schedule.Due(DateTime.UtcNow) || state.Busy || !state.Hidden || !gate.Begin()) return;
            var checkToken = cancellation.Token;
            Task.Factory.StartNew(delegate
            {
                try
                {
                    var update = UpdateTransport.Check(checkToken); checkToken.ThrowIfCancellationRequested();
                    CompleteOnUI(delegate
                    {
                        if (checkToken.IsCancellationRequested) return;
                        schedule.Success(DateTime.UtcNow);
                        if (AutomaticUpdatePolicy.Newer(update, Program.Version))
                        { pending = update; schedule.Next = DateTime.UtcNow; Status(update.Tag + " available; waiting for idle time."); }
                        else Status("Checked stable releases: this installation is current.");
                    });
                }
                catch (Exception ex) { if (!checkToken.IsCancellationRequested) CompleteOnUI(delegate { Failed(ex); }); else gate.End(); }
            });
        }
        void Failed(Exception error)
        {
            schedule.Failure(DateTime.UtcNow);
            Status("Automatic update did not complete. Next attempt: " + schedule.Next.ToLocalTime().ToString("g") + ". Use Check for updates for details.");
            Program.Log("Automatic update: " + error.GetType().Name + "; installed files unchanged unless Setup already started.");
        }
        public void Dispose()
        { if (disposed) return; disposed = true; timer.Stop(); timer.Dispose(); gate.Dispose(); cancellation.Cancel(); cancellation.Dispose(); if (installer != null) installer.Dispose(); }
    }
    sealed partial class Switcher
    {
        AutomaticUpdater automaticUpdater;
        void SetupAutomaticUpdates(Func<bool> additionalBusy)
        {
            automaticUpdater = new AutomaticUpdater(this, delegate
            {
                return new AutomaticUpdateSnapshot { Enabled = options.AutoUpdateApp,
                    Installed = AutomaticUpdatePolicy.InstalledHome(Program.Home, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
                    Hidden = !Visible, IdleSeconds = AutomaticUpdateInput.IdleSeconds(),
                    Busy = closing || transient != null || pending != null || launchDispatch != null || launchPlacement != null ||
                        activation != null || fullscreenOpening || mover.Busy || (additionalBusy != null && additionalBusy()) };
            });
        }
        void DisposeAutomaticUpdates() { if (automaticUpdater != null) { automaticUpdater.Dispose(); automaticUpdater = null; } }
    }
}
