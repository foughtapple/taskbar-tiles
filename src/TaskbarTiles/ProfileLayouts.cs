// Named, local launcher layouts. Every destination and app identity is checked
// before dispatch; an uncertain launch is never retried or mapped to another app.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class ProfileRectangle
    {
        public int X, Y, Width, Height;
        internal Rectangle Rectangle { get { return new Rectangle(X, Y, Width, Height); } }
        internal static ProfileRectangle From(Rectangle r) { return new ProfileRectangle { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height }; }
        internal bool Valid { get { return Math.Abs((long)X) <= 1000000 && Math.Abs((long)Y) <= 1000000 && Width >= 32 && Height >= 32 && Width <= 100000 && Height <= 100000; } }
    }
    sealed class ProfileLayoutEntry
    {
        public FavouriteEntry Launcher;
        public string MonitorKey = "", MonitorModel = "", MonitorInstance = "", MonitorLabel = "", LayoutSignature = "";
        public ProfileRectangle MonitorBounds, WorkArea, Bounds;
        public bool Maximise, Captured;
        public int ZoneNumber;
        internal static ProfileLayoutEntry From(FavouriteEntry launcher, ZoneDestination destination, bool captured = false)
        {
            var m = destination.Monitor;
            return new ProfileLayoutEntry { Launcher = launcher.Clone(), MonitorKey = m.Key, MonitorModel = m.Model, MonitorLabel = m.Label,
                MonitorInstance = m.Instance, MonitorBounds = ProfileRectangle.From(m.Bounds), WorkArea = ProfileRectangle.From(m.WorkArea),
                Bounds = ProfileRectangle.From(destination.Bounds), Maximise = destination.Maximise, Captured = captured,
                ZoneNumber = destination.Number, LayoutSignature = ProfileLayoutPolicy.ZoneSignature(m) };
        }
        internal string DestinationLabel { get { return (string.IsNullOrWhiteSpace(MonitorLabel) ? "Saved monitor" : MonitorLabel) + " / " + (Maximise ? "Full monitor" : Captured ? "Captured position" : "Zone " + ZoneNumber); } }
        public override string ToString() { return (Launcher == null ? "Invalid app" : Launcher.Name) + "  →  " + DestinationLabel; }
    }
    sealed class ProfileLayout
    {
        public string Id = Guid.NewGuid().ToString("N"), Name = "New layout";
        public List<ProfileLayoutEntry> Entries = new List<ProfileLayoutEntry>();
        public override string ToString() { return Name + "  (" + Entries.Count + " apps)"; }
    }
    sealed class ProfileLayoutDocument
    {
        public int Version = 1;
        public List<ProfileLayout> Layouts = new List<ProfileLayout>();
    }
    static class ProfileLayoutStore
    {
        internal const int MaxBytes = 2097152, MaxLayouts = 32, MaxEntries = 32;
        internal static string FilePath { get { return Path.Combine(Program.Home, "profile-layouts.json"); } }
        static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 30 }; }
        internal static ProfileLayoutDocument Parse(string text)
        {
            if (text == null || text.Length > MaxBytes) throw new InvalidDataException("Profile layouts file is too large.");
            var doc = Json().Deserialize<ProfileLayoutDocument>(text);
            Validate(doc); return doc;
        }
        internal static string Serialize(ProfileLayoutDocument doc) { Validate(doc); string text = Json().Serialize(doc); if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("Profile layouts file is too large."); return text; }
        internal static ProfileLayoutDocument Clone(ProfileLayoutDocument doc) { return Parse(Serialize(doc)); }
        internal static ProfileLayoutDocument Read(string path)
        {
            if (!File.Exists(path)) return new ProfileLayoutDocument();
            if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("Profile layouts file is too large.");
            return Parse(File.ReadAllText(path));
        }
        internal static void Save(ProfileLayoutDocument doc, string path = null)
        {
            path = path ?? FilePath;
            string text = Serialize(doc), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static void Validate(ProfileLayoutDocument doc)
        {
            if (doc == null || doc.Version != 1 || doc.Layouts == null || doc.Layouts.Count > MaxLayouts) throw new InvalidDataException("Invalid or unsupported profile layouts file.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var layout in doc.Layouts)
            {
                if (layout == null || string.IsNullOrWhiteSpace(layout.Id) || layout.Id.Length > 128 || !ids.Add(layout.Id) ||
                    string.IsNullOrWhiteSpace(layout.Name) || layout.Name.Length > 100 || layout.Name.Any(char.IsControl) || !names.Add(layout.Name.Trim()) ||
                    layout.Entries == null || layout.Entries.Count > MaxEntries) throw new InvalidDataException("Layouts need unique names and at most 32 apps each.");
                var launchers = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in layout.Entries)
                {
                    if (entry == null || entry.Launcher == null) throw new InvalidDataException("A profile layout app is missing its shortcut.");
                    string error = entry.Launcher.ValidateEntry();
                    if (error != null || !LauncherDescriptor.ApplicationTarget(entry.Launcher.ExpandedTarget)) throw new InvalidDataException(error ?? "Profile layouts require an app or application shortcut.");
                    if (entry.Launcher.AppId.Length > 4096 || entry.Launcher.WorkingDirectory.Length > 4096 || entry.Launcher.IconPath.Length > 4096) throw new InvalidDataException("Profile layout shortcut metadata is too long.");
                    string identity = entry.Launcher.ExpandedTarget.ToUpperInvariant() + "\n" + entry.Launcher.Arguments + "\n" + entry.Launcher.AppId.ToUpperInvariant();
                    if (!launchers.Add(identity)) throw new InvalidDataException("Each app/profile shortcut may appear only once in a layout.");
                    if (string.IsNullOrWhiteSpace(entry.MonitorKey) || entry.MonitorKey.Length > 4096 || entry.MonitorModel == null || entry.MonitorInstance == null || entry.MonitorLabel == null || entry.MonitorLabel.Length > 256 || entry.MonitorLabel.Any(char.IsControl) || entry.LayoutSignature == null ||
                        entry.MonitorModel.Length > 4096 || entry.MonitorInstance.Length > 4096 || entry.LayoutSignature.Length > 16384 ||
                        entry.MonitorBounds == null || !entry.MonitorBounds.Valid || entry.WorkArea == null || !entry.WorkArea.Valid || entry.Bounds == null || !entry.Bounds.Valid ||
                        !entry.MonitorBounds.Rectangle.Contains(entry.WorkArea.Rectangle) || !entry.WorkArea.Rectangle.Contains(entry.Bounds.Rectangle) ||
                        (!entry.Maximise && !entry.Captured && (entry.ZoneNumber < 1 || entry.ZoneNumber > 256))) throw new InvalidDataException("A saved monitor or destination is invalid. Choose its destination again.");
                }
            }
        }
    }
    static class ProfileLayoutPolicy
    {
        internal static string ZoneSignature(MonitorData monitor)
        { return string.Join(";", monitor.Zones.OrderBy(z => z.Number).Select(z => z.Number + ":" + z.Bounds.X + "," + z.Bounds.Y + "," + z.Bounds.Width + "," + z.Bounds.Height).ToArray()); }
        internal static bool TryDestination(ProfileLayoutEntry entry, IEnumerable<MonitorData> monitors, out ZoneDestination destination, out string error)
        {
            destination = null; error = null;
            var matches = monitors.Where(candidate => string.Equals(candidate.Key, entry.MonitorKey, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1) { error = "Saved monitor is disconnected or ambiguous. Edit this layout and choose its destination again."; return false; }
            var m = matches[0];
            if (!string.Equals(m.Model, entry.MonitorModel, StringComparison.OrdinalIgnoreCase) || !string.Equals(m.Instance, entry.MonitorInstance, StringComparison.OrdinalIgnoreCase) ||
                m.Bounds != entry.MonitorBounds.Rectangle || m.WorkArea != entry.WorkArea.Rectangle)
            { error = "Saved monitor geometry or identity changed. Choose its destination again."; return false; }
            if (!entry.Maximise && !entry.Captured && (ZoneSignature(m) != entry.LayoutSignature || !m.Zones.Any(z => z.Number == entry.ZoneNumber && z.Bounds == entry.Bounds.Rectangle)))
            { error = "Saved zone layout changed. Choose its destination again."; return false; }
            if (!m.WorkArea.Contains(entry.Bounds.Rectangle)) { error = "Saved destination is outside the monitor's working area."; return false; }
            destination = new ZoneDestination { Monitor = m, Bounds = entry.Maximise ? m.WorkArea : entry.Bounds.Rectangle, Maximise = entry.Maximise, Number = entry.ZoneNumber };
            return true;
        }
        internal static bool UnscopedProfile(AppButton app)
        { return !LaunchResolution.ProfileScoped(app.AppId) && app.LauncherIdentity != null &&
            (app.LauncherIdentity.Arguments.IndexOf("--profile-directory", StringComparison.OrdinalIgnoreCase) >= 0 || app.LauncherIdentity.Arguments.IndexOf("--user-data-dir", StringComparison.OrdinalIgnoreCase) >= 0); }
        internal static ProfileWindowMatches MatchWindows(AppButton app, IEnumerable<WindowRecord> windows)
        {
            var result = new ProfileWindowMatches(); bool unscopedProfile = UnscopedProfile(app);
            foreach (var window in windows)
            {
                bool sameExecutable = LaunchIdentity.SamePath(app.LaunchExe, window.Exe);
                if (!unscopedProfile && LaunchIdentity.Matches(app, window, null)) result.Windows.Add(window);
                else if (sameExecutable && !LaunchIdentity.ConflictingIds(app.AppId, window.AppId)) result.Uncertain = true;
            }
            return result;
        }
    }
    sealed class ProfileWindowMatches
    {
        internal readonly List<WindowRecord> Windows = new List<WindowRecord>();
        internal bool Uncertain;
    }
    interface IProfileLayoutApi : IDisposable
    {
        bool DestinationAvailable(ProfileLayoutEntry entry, out string error);
        ProfileWindowMatches Find(ProfileLayoutEntry entry);
        void Launch(ProfileLayoutEntry entry, Action<WindowRecord, string> completed);
        void Move(ProfileLayoutEntry entry, WindowRecord window, Action<string> completed);
        void Cancel();
    }
    // Injectable sequential orchestration: cancelled callbacks cannot advance the
    // queue, and a missing/ambiguous app never triggers a repeated dispatch.
    sealed class ProfileLayoutExecution : IDisposable
    {
        readonly IProfileLayoutApi api;
        readonly Action<string> changed, finished;
        readonly List<string> report = new List<string>();
        ProfileLayout layout;
        bool moveOnly, active;
        int index, generation;
        internal bool Busy { get { return active; } }
        internal string Report { get { return string.Join(Environment.NewLine, report.ToArray()); } }
        internal ProfileLayoutExecution(IProfileLayoutApi api, Action<string> changed, Action<string> finished)
        { this.api = api; this.changed = changed ?? delegate { }; this.finished = finished ?? delegate { }; }
        internal void Begin(ProfileLayout requested, bool existingOnly)
        {
            if (active) throw new InvalidOperationException("A profile layout is already running.");
            layout = ProfileLayoutStore.Clone(new ProfileLayoutDocument { Layouts = new List<ProfileLayout> { requested } }).Layouts[0];
            moveOnly = existingOnly; index = 0; report.Clear(); active = true; generation++; Next();
        }
        void Next()
        {
            if (!active) return;
            if (index >= layout.Entries.Count) { Complete(); return; }
            var entry = layout.Entries[index++]; int token = ++generation;
            changed(Report + (report.Count == 0 ? "" : Environment.NewLine) + "(" + index + "/" + layout.Entries.Count + ") " + entry.Launcher.Name);
            try
            {
                string error;
                if (!api.DestinationAvailable(entry, out error)) { Record(entry, "Skipped: " + error); Next(); return; }
                var matches = api.Find(entry);
                if (matches.Uncertain || matches.Windows.Count > 1) { Record(entry, "Skipped: app/window identity is ambiguous; close extra windows or use a specific profile shortcut."); Next(); return; }
                if (matches.Windows.Count == 1) { Move(entry, matches.Windows[0], token); return; }
                if (moveOnly) { Record(entry, "Skipped: app is not open."); Next(); return; }
                api.Launch(entry, delegate(WindowRecord window, string launchError)
                {
                    if (!active || token != generation) return;
                    if (window == null || launchError != null) { Record(entry, "Skipped: " + (launchError ?? "launch accepted, but no unambiguous window appeared. No retry was sent.")); Next(); return; }
                    Move(entry, window, token);
                });
            }
            catch (Exception ex) { if (active && token == generation) { Record(entry, "Skipped: " + ex.Message); Next(); } }
        }
        void Move(ProfileLayoutEntry entry, WindowRecord window, int token)
        {
            if (!active || token != generation) return;
            try
            {
                api.Move(entry, window, delegate(string error)
                { if (!active || token != generation) return; Record(entry, error == null ? "Arranged" : "Placement warning: " + error); Next(); });
            }
            catch (Exception ex) { if (active && token == generation) { Record(entry, "Skipped: " + ex.Message); Next(); } }
        }
        void Record(ProfileLayoutEntry entry, string text) { report.Add(entry.Launcher.Name + ": " + text); changed(Report); }
        void Complete() { active = false; finished(Report.Length == 0 ? "This layout has no apps yet." : Report); }
        internal void Cancel()
        {
            if (!active) return; active = false; generation++; api.Cancel();
            report.Add("Cancelled. Completed moves remain; already launched apps stay open. Remaining apps were skipped."); finished(Report);
        }
        public void Dispose() { Cancel(); api.Dispose(); }
    }
    sealed class WindowsProfileLayoutApi : IProfileLayoutApi
    {
        readonly Control owner;
        readonly Options options;
        readonly TaskbarReader reader;
        readonly Guid desktop;
        WindowMover mover = new WindowMover();
        LaunchPlacement observation;
        LaunchOperation operation;
        readonly Timer dispatchTimer = new Timer { Interval = 200 };
        DateTime dispatchDeadline;
        Action<WindowRecord, string> awaiting;
        bool disposed;
        internal WindowsProfileLayoutApi(Control owner, Options settings, TaskbarReader reader, Guid desktop)
        {
            this.owner = owner; options = settings.Clone(); options.TerminalNewWindow = false; options.ReuseSingleInstance = true;
            this.reader = reader; this.desktop = desktop;
            dispatchTimer.Tick += delegate
            {
                if (awaiting == null || DateTime.UtcNow < dispatchDeadline) return;
                if (operation != null) operation.CancelBeforeDispatch();
                FinishLaunch(null, "Launch preparation/dispatch timed out; the app may still open. No retry was sent.");
            };
        }
        void Post(Action action)
        {
            if (disposed || owner.IsDisposed || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke(new Action(delegate { if (!disposed && !owner.IsDisposed) action(); })); }
            catch (InvalidOperationException) { }
        }
        internal static AppButton App(ProfileLayoutEntry entry)
        {
            var app = FavouriteLaunch.AsApp(entry.Launcher);
            var key = LauncherKey.FromEntry(entry.Launcher, true);
            app.LaunchExe = key.Exe;
            if (key.AppId.Length > 0) app.Id = "Appid:" + key.AppId;
            app.LauncherIdentity = key; return app;
        }
        public bool DestinationAvailable(ProfileLayoutEntry entry, out string error)
        { ZoneDestination destination; return ProfileLayoutPolicy.TryDestination(entry, ZoneCatalog.Load(options, desktop).Monitors, out destination, out error); }
        public ProfileWindowMatches Find(ProfileLayoutEntry entry)
        { return ProfileLayoutPolicy.MatchWindows(App(entry), WindowInventory.Read()); }
        public void Launch(ProfileLayoutEntry entry, Action<WindowRecord, string> completed)
        {
            if (disposed) { completed(null, "Profile layout was cancelled."); return; }
            // Missing scoped identity cannot safely observe/move a browser profile.
            var app = App(entry);
            if (ProfileLayoutPolicy.UnscopedProfile(app))
            { completed(null, "Browser profile shortcut has no verified profile identity. Add it from an open profile window."); return; }
            operation = new LaunchOperation(); var current = operation;
            awaiting = completed; dispatchDeadline = DateTime.UtcNow.AddSeconds(options.LaunchTimeoutSeconds); dispatchTimer.Start();
            observation = new LaunchPlacement(app, options, current.Id, delegate(IntPtr handle, string error)
            {
                var chosen = observation == null ? null : observation.SelectedWindow;
                FinishLaunch(chosen, error);
            }, false);
            ReliableLauncher.Start(app, options, reader, current, delegate(LaunchReceipt receipt, string error)
            {
                Post(delegate
                {
                    if (awaiting == null || !ReferenceEquals(operation, current)) return;
                    dispatchTimer.Stop();
                    if (error != null) { FinishLaunch(null, error); return; }
                    observation.Begin(receipt);
                });
            });
        }
        void FinishLaunch(WindowRecord window, string error)
        {
            dispatchTimer.Stop(); var callback = awaiting; awaiting = null;
            if (observation != null) { observation.Dispose(); observation = null; }
            operation = null; if (callback != null) callback(window, error);
        }
        public void Move(ProfileLayoutEntry entry, WindowRecord window, Action<string> completed)
        {
            ZoneDestination destination; string error;
            if (!ProfileLayoutPolicy.TryDestination(entry, ZoneCatalog.Load(options, desktop).Monitors, out destination, out error)) { completed(error); return; }
            if (!Native.IsWindow(window.Handle) || WindowNative.ProcessId(window.Handle) != window.ProcessId ||
                (window.ProcessStartTicks != 0 && PackageIdentity.StartTicks(window.ProcessId) != window.ProcessStartTicks))
            { completed("Selected window closed or changed."); return; }
            var placementOptions = options.Clone(); if (entry.Captured) placementOptions.ZoneInset = 0;
            mover.Place(window.Handle, destination, placementOptions, completed);
        }
        public void Cancel()
        {
            dispatchTimer.Stop(); awaiting = null;
            if (operation != null) operation.CancelBeforeDispatch(); operation = null;
            if (observation != null) { observation.Dispose(); observation = null; }
            mover.Dispose(); mover = new WindowMover();
        }
        public void Dispose() { if (disposed) return; disposed = true; Cancel(); mover.Dispose(); dispatchTimer.Dispose(); }
    }
    sealed class ProfileLayoutsWindow : Form
    {
        readonly ListBox list;
        internal ProfileLayout Selected;
        internal bool MoveOnly, Manage;
        internal ProfileLayoutsWindow(ProfileLayoutDocument document, bool existingOnly)
        {
            Text = "Profile layouts"; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 460); MinimumSize = new Size(520, 300); BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 11);
            var note = Theme.Label(existingOnly ? "Choose a layout to arrange currently open apps. Missing apps stay closed." : "Left-click a layout to open missing apps and arrange. Right-click to arrange open apps only.", 70);
            note.Padding = new Padding(12); note.AutoSize = false;
            list = new ListBox { Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text, IntegralHeight = false, ItemHeight = 34 };
            foreach (var layout in document.Layouts) list.Items.Add(layout);
            list.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                int index = list.IndexFromPoint(e.Location);
                if (index < 0 || (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)) return;
                Choose(index, existingOnly || e.Button == MouseButtons.Right);
            };
            list.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter && list.SelectedIndex >= 0) { e.Handled = true; Choose(list.SelectedIndex, existingOnly || e.Shift); } };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft };
            var cancel = Theme.Button("Cancel", 100); cancel.Click += delegate { Close(); };
            var manage = Theme.Button("Manage layouts", 155); manage.Click += delegate { Manage = true; Close(); };
            footer.Controls.Add(cancel); footer.Controls.Add(manage); Controls.Add(list); Controls.Add(footer); Controls.Add(note); CancelButton = cancel;
            Shown += delegate { FormFit.Fit(this); list.Focus(); };
        }
        void Choose(int index, bool moveOnly) { Selected = (ProfileLayout)list.Items[index]; MoveOnly = moveOnly; DialogResult = DialogResult.OK; Close(); }
    }
    sealed class ProfileLayoutProgress : Form
    {
        readonly TextBox report;
        readonly Button cancel;
        internal Action CancelRequested;
        bool completed;
        internal ProfileLayoutProgress(string name)
        {
            Text = "Profile layout — " + name; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 420); BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10);
            report = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Card, ForeColor = Theme.Text };
            cancel = Theme.Button("Cancel remaining", 175); cancel.Dock = DockStyle.Bottom;
            cancel.Click += delegate { if (completed) Close(); else if (CancelRequested != null) CancelRequested(); };
            Controls.Add(report); Controls.Add(cancel);
            FormClosing += delegate { if (!completed && CancelRequested != null) CancelRequested(); };
            Shown += delegate { FormFit.Fit(this); };
        }
        internal void UpdateReport(string text) { if (!IsDisposed) report.Text = text; }
        internal void Finish(string text) { completed = true; UpdateReport(text); cancel.Text = "Close"; }
    }
    sealed partial class Switcher
    {
        ProfileLayoutExecution profileLayoutRun;
        ProfileLayoutProgress profileLayoutProgress;
        internal bool ProfileLayoutBusy { get { return profileLayoutRun != null && profileLayoutRun.Busy; } }
        internal void ShowProfileLayouts(bool moveOnly = false)
        {
            if (ProfileLayoutBusy) { if (profileLayoutProgress != null && !profileLayoutProgress.IsDisposed) profileLayoutProgress.Activate(); return; }
            if (pending != null || launchPlacement != null || mover.Busy || transient != null) { Notify("Finish the current app launch, move or dialog first."); return; }
            ProfileLayoutDocument document;
            try { document = ProfileLayoutStore.Read(ProfileLayoutStore.FilePath); }
            catch (Exception ex) { Notify("Profile layouts could not be read: " + ex.Message); return; }
            CancelPassiveLaunchObservation(); Dismiss();
            using (var picker = new ProfileLayoutsWindow(document, moveOnly))
            {
                transient = picker;
                try { picker.ShowDialog(); } finally { transient = null; }
                if (picker.Manage) { SettingsCore("Profile layouts"); return; }
                if (picker.Selected == null || closing) return;
                StartProfileLayout(picker.Selected, picker.MoveOnly);
            }
        }
        void StartProfileLayout(ProfileLayout layout, bool moveOnly)
        {
            DisposeProfileLayouts();
            profileLayoutProgress = new ProfileLayoutProgress(layout.Name);
            var progress = profileLayoutProgress;
            profileLayoutRun = new ProfileLayoutExecution(new WindowsProfileLayoutApi(this, options, reader, DisplayNative.CurrentDesktop(Native.GetForegroundWindow())),
                progress.UpdateReport, delegate(string report) { progress.Finish(report); });
            progress.CancelRequested = delegate { if (profileLayoutRun != null) profileLayoutRun.Cancel(); };
            progress.Show();
            try { profileLayoutRun.Begin(layout, moveOnly); }
            catch (Exception ex) { profileLayoutRun.Dispose(); profileLayoutRun = null; progress.Finish("Could not run layout: " + ex.Message); }
        }
        internal void CancelProfileLayout() { if (profileLayoutRun != null) profileLayoutRun.Cancel(); }
        internal void DisposeProfileLayouts()
        {
            if (profileLayoutRun != null) { profileLayoutRun.Dispose(); profileLayoutRun = null; }
            if (profileLayoutProgress != null) { profileLayoutProgress.Close(); profileLayoutProgress.Dispose(); profileLayoutProgress = null; }
        }
    }
    sealed class ProfileLayoutEditor : Form
    {
        readonly Options options;
        readonly TextBox name;
        readonly ListBox list;
        readonly Label status;
        internal ProfileLayout Result;
        internal ProfileLayoutEditor(ProfileLayout original, Options options)
        {
            this.options = options.Clone(); Result = ProfileLayoutStore.Clone(new ProfileLayoutDocument { Layouts = new List<ProfileLayout> { original } }).Layouts[0];
            Text = "Edit profile layout"; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(860, 570); MinimumSize = new Size(620, 380);
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10);
            name = new TextBox { Text = Result.Name, Dock = DockStyle.Top, MaxLength = 100, BackColor = Theme.Card, ForeColor = Theme.Text };
            list = new ListBox { Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text, IntegralHeight = false };
            status = Theme.Label("Choose exact apps and destinations. Nothing opens or moves while editing.", 56); status.Dock = DockStyle.Bottom;
            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 88, WrapContents = true };
            AddTool(tools, "Installed apps…", 150, AddInstalled); AddTool(tools, "Add shortcut…", 150, AddShortcut);
            AddTool(tools, "Capture open apps", 155, Capture); AddTool(tools, "Destination…", 145, ChangeDestination);
            AddTool(tools, "Edit shortcut…", 150, EditShortcut); AddTool(tools, "Remove", 100, Remove);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft };
            AddTool(footer, "Use draft", 130, delegate
            {
                Result.Name = name.Text.Trim();
                try { ProfileLayoutStore.Validate(new ProfileLayoutDocument { Layouts = new List<ProfileLayout> { Result } }); DialogResult = DialogResult.OK; Close(); }
                catch (Exception ex) { status.Text = ex.Message; }
            });
            var cancel = Theme.Button("Cancel", 100); cancel.Click += delegate { Close(); }; footer.Controls.Add(cancel); CancelButton = cancel;
            Controls.Add(list); Controls.Add(status); Controls.Add(footer); Controls.Add(tools); Controls.Add(name);
            Shown += delegate { FormFit.Fit(this); }; RefreshEntries();
        }
        static void AddTool(FlowLayoutPanel panel, string text, int width, Action action) { var button = Theme.Button(text, width); button.Click += delegate { action(); }; panel.Controls.Add(button); }
        void RefreshEntries() { int selected = list.SelectedIndex; list.Items.Clear(); foreach (var entry in Result.Entries) list.Items.Add(entry); if (list.Items.Count > 0) list.SelectedIndex = Math.Max(0, Math.Min(selected, list.Items.Count - 1)); }
        ProfileLayoutEntry Selected { get { return list.SelectedItem as ProfileLayoutEntry; } }
        ZoneDestination PickDestination(string app)
        {
            using (var picker = new ZonePicker(options, DisplayNative.CurrentDesktop(Native.GetForegroundWindow()), Cursor.Position, app, false))
            {
                if (picker.ShowDialog(this) == DialogResult.OK) return picker.Destination;
                if (picker.SettingsRequested) status.Text = "Close this editor and adjust Screens & zones in Settings, then choose the destination again.";
                return null;
            }
        }
        void AddEntry(FavouriteEntry launcher)
        {
            if (Result.Entries.Count >= ProfileLayoutStore.MaxEntries) { status.Text = "A layout supports at most 32 apps."; return; }
            if (!LauncherDescriptor.ApplicationTarget(launcher.ExpandedTarget)) { status.Text = "Choose an app executable, app shortcut or AppsFolder app."; return; }
            var destination = PickDestination(launcher.Name); if (destination == null) return;
            Result.Entries.Add(ProfileLayoutEntry.From(launcher, destination)); RefreshEntries();
        }
        void AddInstalled() { using (var picker = new InstalledAppPicker()) if (picker.ShowDialog(this) == DialogResult.OK) foreach (var entry in picker.SelectedApps) AddEntry(entry); }
        void AddShortcut() { using (var editor = new FavouriteEditor(new FavouriteEntry { Name = "App", Group = "Profile layout" })) if (editor.ShowDialog(this) == DialogResult.OK) AddEntry(editor.Result); }
        void ChangeDestination() { var entry = Selected; if (entry == null) return; var destination = PickDestination(entry.Launcher.Name); if (destination == null) return; Result.Entries[Result.Entries.IndexOf(entry)] = ProfileLayoutEntry.From(entry.Launcher, destination); RefreshEntries(); }
        void EditShortcut() { var entry = Selected; if (entry == null) return; using (var editor = new FavouriteEditor(entry.Launcher)) if (editor.ShowDialog(this) == DialogResult.OK) { entry.Launcher = editor.Result; RefreshEntries(); } }
        void Remove() { var entry = Selected; if (entry != null) { Result.Entries.Remove(entry); RefreshEntries(); } }
        void Capture()
        {
            int added = 0, skipped = 0;
            var monitors = ZoneCatalog.Load(options, DisplayNative.CurrentDesktop(Native.GetForegroundWindow())).Monitors;
            var windows = WindowInventory.Read();
            foreach (var window in windows)
            {
                if (Result.Entries.Count >= ProfileLayoutStore.MaxEntries) { skipped++; continue; }
                var launcher = LauncherDescriptor.FromWindow(window); if (launcher == null) { skipped++; continue; }
                var key = LauncherKey.FromEntry(launcher, true);
                if (Result.Entries.Any(e => LauncherKey.Same(LauncherKey.FromEntry(e.Launcher, true), key))) { skipped++; continue; }
                var monitor = monitors.FirstOrDefault(m => m.DeviceName == Screen.FromHandle(window.Handle).DeviceName);
                Rectangle bounds = WindowNative.VisibleBounds(window.Handle);
                bool maximised = WindowNative.IsZoomed(window.Handle);
                if (monitor == null || Native.IsIconic(window.Handle) || bounds.Width < 32 || bounds.Height < 32 || (!maximised && !monitor.WorkArea.Contains(bounds))) { skipped++; continue; }
                Result.Entries.Add(ProfileLayoutEntry.From(launcher, new ZoneDestination { Monitor = monitor, Bounds = maximised ? monitor.WorkArea : bounds, Maximise = maximised }, true)); added++;
            }
            RefreshEntries(); status.Text = "Captured " + added + " apps. Skipped " + skipped + " duplicate, minimised or unidentified windows. Verify profile shortcuts and destinations before Apply.";
        }
    }
    sealed partial class SettingsWindow
    {
        ProfileLayoutDocument profileLayoutDraft;
        ListBox profileLayoutsList;
        Label profileLayoutsStatus;
        bool profileLayoutsTouched, profileLayoutsReadFailed;
        internal void AddProfileLayoutsPage()
        {
            var page = Page("Profile layouts");
            Section(page, "Named app layouts", "Save app/profile shortcuts with destinations. Left-click a layout to open missing apps and arrange them; right-click arranges currently open matching apps only. Ambiguous windows are skipped. Apply saves this draft.");
            try { profileLayoutDraft = ProfileLayoutStore.Read(ProfileLayoutStore.FilePath); }
            catch (Exception ex) { profileLayoutsReadFailed = true; profileLayoutDraft = new ProfileLayoutDocument(); Program.Log("Profile layouts: " + ex.Message); }
            profileLayoutsList = new ListBox { Width = 700, Height = 230, BackColor = Theme.Card, ForeColor = Theme.Text, IntegralHeight = false };
            page.Controls.Add(profileLayoutsList);
            var tools = new FlowLayoutPanel { Width = 700, Height = 60 };
            AddTool(tools, "Add layout…", 145, delegate { EditProfileLayout(null); });
            AddTool(tools, "Edit…", 110, EditSelectedProfileLayout);
            AddTool(tools, "Delete", 110, DeleteProfileLayout); page.Controls.Add(tools);
            profileLayoutsList.DoubleClick += delegate { EditSelectedProfileLayout(); };
            profileLayoutsStatus = new Label { Width = 700, Height = 100, ForeColor = Theme.Muted };
            page.Controls.Add(profileLayoutsStatus); RefreshProfileLayoutsDraft();
        }
        void RefreshProfileLayoutsDraft()
        {
            profileLayoutsList.Items.Clear(); foreach (var layout in profileLayoutDraft.Layouts) profileLayoutsList.Items.Add(layout);
            profileLayoutsStatus.Text = profileLayoutsReadFailed ? "Existing profile layouts file could not be read. It is preserved; repair or rename the file before editing layouts." : "Up to 32 layouts, with 32 apps per layout. Capture open apps preserves verified app/profile launch commands and current positions. Apply saves; Cancel discards this draft.";
        }
        void EditSelectedProfileLayout()
        {
            var selected = profileLayoutsList.SelectedItem as ProfileLayout;
            if (selected != null) EditProfileLayout(selected);
        }
        void EditProfileLayout(ProfileLayout layout)
        {
            if (profileLayoutsReadFailed) return;
            bool existing = layout != null;
            if (!existing && profileLayoutDraft.Layouts.Count >= ProfileLayoutStore.MaxLayouts) { profileLayoutsStatus.Text = "The maximum is 32 layouts."; return; }
            ReadDraft();
            if (layout == null)
            {
                int number = 1; while (profileLayoutDraft.Layouts.Any(p => p.Name.Equals("Layout " + number, StringComparison.OrdinalIgnoreCase))) number++;
                layout = new ProfileLayout { Name = "Layout " + number };
            }
            using (var editor = new ProfileLayoutEditor(layout, edit))
            {
                if (editor.ShowDialog(this) != DialogResult.OK || DismissedByFocusLoss) return;
                var candidate = ProfileLayoutStore.Clone(profileLayoutDraft);
                if (existing) candidate.Layouts[candidate.Layouts.FindIndex(p => p.Id == layout.Id)] = editor.Result; else candidate.Layouts.Add(editor.Result);
                try { ProfileLayoutStore.Validate(candidate); profileLayoutDraft = candidate; profileLayoutsTouched = true; RefreshProfileLayoutsDraft(); }
                catch (Exception ex) { profileLayoutsStatus.Text = ex.Message; }
            }
        }
        void DeleteProfileLayout()
        {
            var selected = profileLayoutsList.SelectedItem as ProfileLayout; if (selected == null || profileLayoutsReadFailed) return;
            if (MessageBox.Show(this, "Delete layout '" + selected.Name + "'? Apps stay installed and open. Apply saves this draft.", "Delete profile layout", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            profileLayoutDraft.Layouts.Remove(selected); profileLayoutsTouched = true; RefreshProfileLayoutsDraft();
        }
        internal void CommitProfileLayoutsDraft()
        {
            if (!profileLayoutsTouched || profileLayoutsReadFailed) return;
            if (DismissedByFocusLoss) throw new OperationCanceledException("Settings were dismissed without saving.");
            ProfileLayoutStore.Save(profileLayoutDraft);
        }
        internal void MarkProfileLayoutsCommitted() { profileLayoutsTouched = false; }
    }
}
