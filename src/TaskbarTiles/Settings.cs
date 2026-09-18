// Taskbar Tiles 0.6 - editable options and the settings window.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class Options
    {
        public int ConfigVersion = 7;
        public int TileSize = 120;
        public int PreviewScale = 120;
        public int MaxPanelWidth = 1500;
        public int WindowRows = 2;
        public int WindowColumns = 6; // Maximum columns; capacity is columns x maximum rows.
        public int WindowTitleFontSize = 22;
        public int AppLabelFontSize = 22;
        public bool ShowWindowTitleIcons = true;
        public int WindowTitleIconSize = 28;
        public bool MinimizeFullscreenOnOpen = true;
        public bool SearchInstalledApps = true;
        public bool SearchOpenWindows = true;
        public bool SearchFavourites = true;
        public bool SearchSettings = true;
        public bool SearchIndexedFiles = true;
        public int SearchPanelWidth = 660;
        public int SearchVisibleRows = 7;
        public int SearchRowHeight = 54;
        public int AppRows = 2;
        public bool InterceptAltTab = true;
        public bool StickyAltTab = true;
        public bool ShowCloseButtons = true;
        public bool ShowMonitorBadges = true;
        public bool ShowAppLabels = true;
        public bool ShowLivePreviews = true;
        public bool QuickSizeButtons = true;
        public bool EnableSearch = true;
        public bool CurrentMonitorOnly = false;
        public bool HideOnFocusLoss = true;
        public bool RightClickZones = true;
        public bool ReadFancyZones = true;
        public bool RespectZoneSpacing = true;
        public bool ShowBasicFallback = true;
        public int BasicLayout = 1; // 0 = halves, 1 = thirds, 2 = quarters.
        public bool ShowZoneNumbers = true;
        public bool ShowZoneButtons = true;
        public bool KeepOpenAfterMove = false;
        public bool EnableUndoMove = true;
        public bool ReuseSingleInstance = true;
        public bool DirectAppLaunch = true;
        public bool TerminalNewWindow = true;
        public int PickerWidth = 1050;
        public int PickerHeight = 650;
        public int FullScreenButtonHeight = 52;
        public int FullScreenButtonMinWidth = 190;
        public int ZoneLabelSize = 15;
        public int ZoneInset = 0;
        public int LaunchTimeoutSeconds = 15;
        public string FancyZonesFolder = "";
        public string MonitorOverrides = "";
        public bool WindowsSearchButton = true;
        public bool FavouritesButton = true;
        public bool DesktopButton = true;
        public bool ClipboardButton = false;
        public bool LocalHotkeys = true;
        public bool MiddleClickClose = false;
        public bool LiveSettingsPreview = true;
        public int FooterButtonHeight = 40;
        public int FavouriteWidth = 460;
        public int FavouriteRowHeight = 56;
        public int FavouriteVisibleRows = 8;
        public bool FavouriteGroups = true;
        public bool FavouriteDetails = true;
        public bool FavouriteAlphabetical = false;
        public bool FavouriteNumberKeys = true;

        static readonly Dictionary<string, int[]> Limits = new Dictionary<string, int[]> {
            { "TileSize", new[] { 56, 256 } }, { "PreviewScale", new[] { 70, 220 } },
            { "MaxPanelWidth", new[] { 760, 3600 } }, { "WindowRows", new[] { 1, 3 } },
            { "WindowColumns", new[] { 1, 12 } },
            { "WindowTitleFontSize", new[] { 9, 32 } }, { "AppLabelFontSize", new[] { 9, 32 } },
            { "WindowTitleIconSize", new[] { 12, 64 } },
            { "SearchPanelWidth", new[] { 420, 1100 } }, { "SearchVisibleRows", new[] { 3, 12 } },
            { "SearchRowHeight", new[] { 40, 84 } }, { "AppRows", new[] { 1, 3 } }, { "BasicLayout", new[] { 0, 2 } },
            { "PickerWidth", new[] { 640, 2200 } }, { "PickerHeight", new[] { 420, 1200 } },
            { "FullScreenButtonHeight", new[] { 36, 100 } }, { "FullScreenButtonMinWidth", new[] { 150, 400 } },
            { "ZoneLabelSize", new[] { 10, 28 } }, { "ZoneInset", new[] { 0, 60 } },
            { "LaunchTimeoutSeconds", new[] { 5, 60 } },
            { "FooterButtonHeight", new[] { 32, 64 } }, { "FavouriteWidth", new[] { 360, 850 } },
            { "FavouriteRowHeight", new[] { 40, 90 } }, { "FavouriteVisibleRows", new[] { 3, 18 } }
        };
        internal static string FilePath { get { return Path.Combine(Program.Home, "settings.ini"); } }
        internal Options Clone() { return (Options)MemberwiseClone(); }
        internal void Validate()
        {
            foreach (var pair in Limits)
            {
                var field = typeof(Options).GetField(pair.Key);
                int n = (int)field.GetValue(this);
                field.SetValue(this, Math.Max(pair.Value[0], Math.Min(pair.Value[1], n)));
            }
            FancyZonesFolder = (FancyZonesFolder ?? "").Trim();
            MonitorOverrides = MonitorOverrides ?? "";
        }
        internal static Options Parse(IEnumerable<string> lines)
        {
            var o = new Options(); bool sawColumns = false;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith("#") || line.StartsWith(";") || !line.Contains("=")) continue;
                string[] parts = line.Split(new[] { '=' }, 2);
                var f = typeof(Options).GetFields().FirstOrDefault(x => x.Name.Equals(parts[0].Trim(), StringComparison.OrdinalIgnoreCase));
                if (f == null) continue; // Retired LockWindowPageSize / WindowsPerPage keys are ignored.
                if (f.Name == "WindowColumns") sawColumns = true;
                string v = parts[1].Trim(); int n; bool b;
                if (f.FieldType == typeof(int) && int.TryParse(v, out n)) f.SetValue(o, n);
                else if (f.FieldType == typeof(bool) && bool.TryParse(v, out b)) f.SetValue(o, b);
                else if (f.FieldType == typeof(string)) f.SetValue(o, v);
            }
            // Translate former Auto (0) once; preserve explicitly selected column/row limits.
            if (o.WindowColumns == 0 || (!sawColumns && o.ConfigVersion < 6))
                o.WindowColumns = Math.Max(1, Math.Min(12, (Math.Max(760, Math.Min(3600, o.MaxPanelWidth)) - 32) / (280 * Math.Max(70, Math.Min(220, o.PreviewScale)) / 100 + 12)));
            o.ConfigVersion = Math.Max(7, o.ConfigVersion);
            o.Validate(); return o;
        }
        internal static Options Load() { return File.Exists(FilePath) ? Parse(File.ReadAllLines(FilePath)) : new Options(); }
        internal static void SaveValue(string key, string value)
        { SaveValues(new Dictionary<string, string> { { key, value } }); }
        static void SaveValues(Dictionary<string, string> changes)
        {
            var remaining = new Dictionary<string, string>(changes, StringComparer.OrdinalIgnoreCase);
            var lines = File.Exists(FilePath) ? new List<string>(File.ReadAllLines(FilePath)) : new List<string>();
            lines.RemoveAll(raw => {
                string key = raw.Trim().Split('=')[0].Trim();
                return key.Equals("LockWindowPageSize", StringComparison.OrdinalIgnoreCase) || key.Equals("WindowsPerPage", StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("#") || line.StartsWith(";") || !line.Contains("=")) continue;
                string key = line.Split('=')[0].Trim(); string value;
                if (changes.TryGetValue(key, out value) || remaining.TryGetValue(key, out value))
                { lines[i] = key + "=" + value; remaining.Remove(key); }
            }
            foreach (var pair in remaining) lines.Add(pair.Key + "=" + pair.Value);
            string temp = FilePath + ".tmp";
            File.WriteAllLines(temp, lines.ToArray(), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
            else File.Move(temp, FilePath);
        }
        internal void Save()
        {
            Validate();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in typeof(Options).GetFields(BindingFlags.Public | BindingFlags.Instance))
                values[f.Name] = Convert.ToString(f.GetValue(this), System.Globalization.CultureInfo.InvariantCulture).Replace("\r", " ").Replace("\n", " ");
            SaveValues(values);
        }
        internal static int StoredVersion(IEnumerable<string> lines)
        {
            int version = 0;
            foreach (var raw in lines)
            {
                var pair = raw.Split(new[] { '=' }, 2); int value;
                if (pair.Length == 2 && pair[0].Trim().Equals("ConfigVersion", StringComparison.OrdinalIgnoreCase) && int.TryParse(pair[1].Trim(), out value)) version = value;
            }
            return version;
        }
        internal static Options UpgradeToCurrent(string[] lines)
        {
            var o = Parse(lines);
            // The user requested both labels at 22 on this upgrade. Do it once,
            // not on every start or reinstall; all other preferences survive.
            if (StoredVersion(lines) < 7) { o.WindowTitleFontSize = 22; o.AppLabelFontSize = 22; }
            o.ConfigVersion = Math.Max(7, o.ConfigVersion); return o;
        }
        internal static void Migrate()
        {
            try
            {
                var raw = File.Exists(FilePath) ? File.ReadAllLines(FilePath) : new string[0];
                if (StoredVersion(raw) >= 7) return;
                if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".pre-v062", true);
                UpgradeToCurrent(raw).Save();
            }
            catch (Exception ex) { Program.Log("Settings migration: " + ex.Message); }
        }
        internal string OverrideFor(string key)
        {
            foreach (var item in (MonitorOverrides ?? "").Split(';'))
            {
                var p = item.Split(new[] { '|' }, 2);
                if (p.Length == 2 && Uri.UnescapeDataString(p[0]) == key) return Uri.UnescapeDataString(p[1]);
            }
            return "auto";
        }
        internal void SetOverride(string key, string value)
        {
            var entries = (MonitorOverrides ?? "").Split(';').Where(x => !string.IsNullOrEmpty(x) && x.Split('|')[0] != Uri.EscapeDataString(key)).ToList();
            if (value != "auto") entries.Add(Uri.EscapeDataString(key) + "|" + Uri.EscapeDataString(value));
            MonitorOverrides = string.Join(";", entries.ToArray());
        }
    }

    static class Theme
    {
        internal static readonly Color Background = Color.FromArgb(20, 27, 38), Card = Color.FromArgb(31, 40, 54),
            Text = Color.FromArgb(235, 239, 245), Muted = Color.FromArgb(160, 177, 199),
            Accent = Color.FromArgb(111, 193, 250), Border = Color.FromArgb(61, 78, 102);
        internal static Button Button(string text, int width)
        {
            return new Button { Text = text, Width = width, Height = 34, FlatStyle = FlatStyle.Flat,
                BackColor = Card, ForeColor = Text, Margin = new Padding(5), UseVisualStyleBackColor = false };
        }
        internal static Label Label(string text, int height)
        { return new Label { Text = text, ForeColor = Text, AutoSize = false, Height = height, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft }; }
    }

    sealed partial class SettingsWindow : Form
    {
        Options edit;
        readonly Action<Options> applied;
        readonly Dictionary<string, Control> fields = new Dictionary<string, Control>();
        readonly List<Tuple<MonitorData, ComboBox>> monitorChoices = new List<Tuple<MonitorData, ComboBox>>();
        readonly TabControl tabs = new TabControl();
        readonly CheckBox startup;
        readonly Font sectionFont = new Font("Segoe UI", 11, FontStyle.Bold);
        bool loading;
        Label pageCapacityLabel;
        public SettingsWindow(Options current, Action<Options> onApply, Func<Options, Bitmap> capture = null, IEnumerable<AppButton> taskbar = null, string initialTab = null)
        {
            edit = current.Clone(); applied = onApply; SetupSettingsDismissal(current.HideOnFocusLoss); previewCapture = capture; taskbarChoices = taskbar == null ? new List<AppButton>() : taskbar.ToList(); InitialiseFavouriteDraft();
            Text = "Taskbar Tiles " + Program.Version + " - Settings"; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(700, 520); ClientSize = new Size(1260, 800);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10); BackColor = Theme.Background; ForeColor = Theme.Text;
            var header = Theme.Label("  Taskbar Tiles   /   Settings     v" + Program.Version, 48);
            header.Font = new Font("Segoe UI", 16, FontStyle.Bold);
            tabs.Dock = DockStyle.Fill; tabs.Padding = new Point(10, 6); tabs.Multiline = true;
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var save = Theme.Button("Save && close", 125); save.Click += delegate { if (ApplyEdit()) Close(); };
            var apply = Theme.Button("Apply", 90); apply.Click += delegate { ApplyEdit(); };
            var cancel = Theme.Button("Cancel", 90); cancel.Click += delegate { Close(); };
            var reset = Theme.Button("Restore defaults", 145); reset.Click += delegate {
                if (MessageBox.Show(this, "Reset appearance and navigation options? Startup and your monitor mappings will be kept. Click Apply to save.", "Restore defaults", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                string mapping = edit.MonitorOverrides; edit = new Options { MonitorOverrides = mapping }; LoadControls();
            };
            bottom.Controls.AddRange(new Control[] { save, apply, cancel, reset });
            var content = new Panel { Dock = DockStyle.Fill };
            content.Controls.Add(tabs); Controls.Add(content); Controls.Add(bottom); Controls.Add(header);
            CreateLivePreview(content);
            var appearance = Page("Appearance");
            Section(appearance, "Tile sizes", "App tiles and open-window previews have separate size controls.");
            Number(appearance, "TileSize", "App tile size", "Square launcher tiles, in logical pixels. Default: 120.", 56, 256, 8);
            Number(appearance, "PreviewScale", "Open-window preview size", "Percentage of the original preview size. Default: 120% (larger).", 70, 220, 10);
            Number(appearance, "MaxPanelWidth", "App-strip width preference", "Logical pixels. A wider open-window row can make the menu wider than this preference.", 760, 3600, 100);
            Section(appearance, "Text sizes", "Independent text controls. The live preview shows draft changes; Apply saves them.");
            Number(appearance, "WindowTitleFontSize", "Open-window title text size", "Default: 22 logical pixels before display scaling. Changes the title above each active-window preview, not the app itself.", 9, 32, 1);
            Number(appearance, "AppLabelFontSize", "App-launch tile text size", "Default: 22 logical pixels for the names under the taskbar app icons. Labels have room for two lines; hover shows long names.", 9, 32, 1);
            Section(appearance, "Window title icons", "An app icon sits immediately before each window title. Its size is independent of the text.");
            Check(appearance, "ShowWindowTitleIcons", "Show app icons before open-window titles");
            Number(appearance, "WindowTitleIconSize", "Open-window title icon size", "Default: 28 logical pixels. Range: 12-64. Larger icons expand the title band; the X stays at the far right.", 12, 64, 2);
            Section(appearance, "Open-window layout", "These are maximums. The page limit is calculated automatically; unused rows are not reserved.");
            Number(appearance, "WindowColumns", "Maximum columns", "Fill one row first. When another is needed, balance the windows and centre each row.", 1, 12, 1);
            Number(appearance, "WindowRows", "Maximum rows", "Maximum windows per page = columns x rows. Any extra window goes in the lower row.", 1, 3, 1);
            pageCapacityLabel = new Label { ForeColor = Theme.Accent, Height = 38, Width = 700, Padding = new Padding(8, 4, 8, 4), AutoSize = false };
            appearance.Controls.Add(pageCapacityLabel);
            HintTree(pageCapacityLabel, "Calculated from maximum columns x maximum rows, not a separate setting. The live preview reports the effective limit when the chosen sizes cannot fit this monitor.");
            Number(appearance, "AppRows", "Maximum app rows", "Balances tiles across rows rather than leaving one tile on its own.", 1, 3, 1);
            Check(appearance, "ShowAppLabels", "Show app names under icons");
            Check(appearance, "ShowLivePreviews", "Show live window previews");
            Check(appearance, "ShowMonitorBadges", "Show each window's monitor number");
            Check(appearance, "QuickSizeButtons", "Show +/- size buttons in the menu");

            var navigation = Page("Navigation");
            Section(navigation, "Choosing and finding windows", "Your existing X-Mouse Run Application command is unchanged.");
            Check(navigation, "EnableSearch", "Enable search across window titles and app names");
            Check(navigation, "CurrentMonitorOnly", "Only list windows on the monitor where the menu opens");
            Check(navigation, "ShowCloseButtons", "Show a close X on each open-window preview");
            Check(navigation, "HideOnFocusLoss", "Close the menu and Settings when clicking outside (discard unapplied edits)");
            Check(navigation, "StickyAltTab", "Keep the menu open when Alt is released");
            Check(navigation, "InterceptAltTab", "Replace Alt+Tab while Taskbar Tiles is running");
            Check(navigation, "MinimizeFullscreenOnOpen", "Minimise the foreground fullscreen app when opening the switcher");
            Section(navigation, "Window placement", "Right-click selects a destination; ordinary left-click behaviour is unchanged.");
            Check(navigation, "RightClickZones", "Enable right-click monitor and zone picker");
            Check(navigation, "KeepOpenAfterMove", "Reopen Taskbar Tiles after moving an existing window");
            Check(navigation, "EnableUndoMove", "Remember the last window move for Undo");
            Check(navigation, "DirectAppLaunch", "Launch verified shortcuts directly instead of clicking the taskbar");
            Check(navigation, "TerminalNewWindow", "Open plain Terminal launchers in a new window");
            Check(navigation, "ReuseSingleInstance", "Move a matching existing window if an app reuses it");
            Number(navigation, "LaunchTimeoutSeconds", "Wait for a new app window", "Seconds. Waits for a stable new window before considering reuse. Ambiguous matches let you refresh, wait longer or choose.", 5, 60, 5);

            var zones = Page("Screens & zones");
            Section(zones, "Destination picker", "Full screen means maximise on that monitor, with the normal Windows taskbar. It does not send F11.");
            Number(zones, "PickerWidth", "Picker width", "Logical pixels; automatically limited to the available screen.", 640, 2200, 50);
            Number(zones, "PickerHeight", "Picker height", "All screens fit in one view. Increasing this makes the map larger; it never scrolls.", 420, 1200, 50);
            Number(zones, "FullScreenButtonHeight", "Full-screen button height", "Preferred height above each monitor. Reduced only when needed to keep every screen visible.", 36, 100, 4);
            Number(zones, "FullScreenButtonMinWidth", "Full-screen button minimum width", "Preferred minimum width for portrait monitors; the complete map auto-fits when space is tight.", 150, 400, 10);
            Number(zones, "ZoneLabelSize", "Zone number text size", "Logical pixels in the monitor map.", 10, 28, 1);
            Check(zones, "ShowZoneNumbers", "Show zone numbers inside the map");
            Check(zones, "ShowZoneButtons", "Show numbered buttons below each monitor (useful for overlapping zones)");
            Check(zones, "ReadFancyZones", "Read saved PowerToys FancyZones layouts");
            Check(zones, "RespectZoneSpacing", "Use the spacing saved in FancyZones");
            Number(zones, "ZoneInset", "Extra inset inside a selected zone", "Additional pixels on each edge. Zero follows the saved layout.", 0, 60, 2);
            Check(zones, "ShowBasicFallback", "Offer clearly labelled basic zones when a FancyZones layout is unavailable");
            Choice(zones, "BasicLayout", "Basic fallback layout", "Only used when labelled Basic, not as a substitute for an identified FancyZones layout.", new[] { "Two halves", "Three columns", "Four quarters" });
            var folder = new TextBox { Width = 265, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            fields["FancyZonesFolder"] = folder; Row(zones, "FancyZones folder (optional)", "Leave blank to use %LOCALAPPDATA%\\Microsoft\\PowerToys\\FancyZones.", folder);
            var browse = Theme.Button("Browse folder...", 150); browse.Click += delegate { using (var d = new FolderBrowserDialog { Description = "Folder containing applied-layouts.json and custom-layouts.json" }) if (d.ShowDialog(this) == DialogResult.OK) folder.Text = d.SelectedPath; };
            zones.Controls.Add(browse);

            var monitors = Page("Monitor layouts");
            Section(monitors, "Layout assigned to each screen", "Automatic reads the current desktop's saved FancyZones layout. A manual choice only affects Taskbar Tiles; it never modifies PowerToys files. Reopen Settings after changing the folder.");
            BuildMonitorChoices(monitors);
            var system = Page("Startup & tools");
            Section(system, "System tray and startup", "Runs as a normal user. No service or administrator access is required.");
            startup = new CheckBox { Text = "Start Taskbar Tiles when I sign in", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(8, 12, 8, 12), Checked = File.Exists(Switcher.StartupPath) };
            startup.Text = ""; startup.AutoSize = false;
            Row(system, "Start Taskbar Tiles when I sign in", "Keep the tray app available after signing in.", startup);
            var xmouse = Theme.Button("Copy X-Mouse command", 235); xmouse.Click += delegate { Clipboard.SetText("\"" + Application.ExecutablePath + "\" --toggle"); MessageBox.Show(this, "Copied. Keep X-Mouse Button 5 set to Run Application.", "X-Mouse"); };
            var display = Theme.Button("Windows display settings", 235); display.Click += delegate { try { System.Diagnostics.Process.Start("ms-settings:display"); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } };
            var diag = Theme.Button("Write zone diagnostics", 235); diag.Click += delegate { try { string file = ZoneCatalog.WriteDiagnostics(edit); System.Diagnostics.Process.Start("notepad.exe", "\"" + file + "\""); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } };
            var appFolder = Theme.Button("Open app folder", 235); appFolder.Click += delegate { System.Diagnostics.Process.Start("explorer.exe", "\"" + Program.Home.TrimEnd('\\') + "\""); };
            var launchLog = Theme.Button("Open launch diagnostics", 235); launchLog.Click += delegate
            {
                try { if (!File.Exists(LaunchLog.FilePath)) LaunchLog.Write("info", "No launches recorded yet."); System.Diagnostics.Process.Start("notepad.exe", "\"" + LaunchLog.FilePath + "\""); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message); }
            };
            var updates = Theme.Button("Check for updates...", 235); updates.Click += delegate { using (var dialog = new UpdatesWindow()) dialog.ShowDialog(this); };
            var switching = Theme.Button("Open switching diagnostics", 235); switching.Click += delegate
            {
                try { if (!File.Exists(ActivationLog.PathName)) ActivationLog.Write("No switch requests recorded yet."); System.Diagnostics.Process.Start("notepad.exe", "\"" + ActivationLog.PathName + "\""); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message); }
            };
            system.Controls.AddRange(new Control[] { xmouse, display, diag, launchLog, switching, updates, appFolder });
            Section(system, "About this integration", "Zone placement reads saved layout geometry and uses normal Windows move/resize requests. It does not register a window in FancyZones' internal zone history. Apps can enforce minimum sizes; elevated, fullscreen or non-resizable windows may not accept placement.");
            AddQuickAccessPage();
            AddFavouritesPage();
            AddSearchSettingsPage();
            LoadControls(); HookLiveChanges(); InstallSettingHints();
            if (!string.IsNullOrEmpty(initialTab))
                foreach (TabPage page in tabs.TabPages) if (page.Text == initialTab) tabs.SelectedTab = page;
            Shown += delegate
            {
                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 24), Math.Min(MinimumSize.Height, area.Height - 24));
                Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
                QueuePreview();
            };
        }
        FlowLayoutPanel Page(string title)
        {
            var page = new TabPage(title) { BackColor = Theme.Background, ForeColor = Theme.Text, Padding = Padding.Empty };
            var flow = new SettingsPage(); page.Controls.Add(flow); tabs.TabPages.Add(page); return flow;
        }
        void Section(FlowLayoutPanel p, string title, string note)
        { var section = new SettingSection(title, note); p.Controls.Add(section); HintTree(section, title + "\n\n" + note); }
        void Row(FlowLayoutPanel p, string title, string note, Control input)
        { var row = new SettingRow(title, note, input); p.Controls.Add(row); HintTree(row, title + "\n\n" + note); }
        void Number(FlowLayoutPanel p, string key, string title, string note, int min, int max, int increment)
        {
            var n = new NumericUpDown { Width = 125, Minimum = min, Maximum = max, Increment = increment, BackColor = Theme.Card, ForeColor = Theme.Text, TextAlign = HorizontalAlignment.Right };
            fields[key] = n; Row(p, title, note, n);
        }
        void Check(FlowLayoutPanel p, string key, string title)
        {
            var b = new CheckBox { Text = "", AutoSize = false, Size = new Size(28, 24), ForeColor = Theme.Text, CheckAlign = ContentAlignment.MiddleLeft };
            fields[key] = b; var row = new SettingRow(title, "", b); p.Controls.Add(row); HintTree(row, SettingsHelp.For(key));
        }
        void Choice(FlowLayoutPanel p, string key, string title, string note, string[] values)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 195 };
            c.Items.AddRange(values); fields[key] = c; Row(p, title, note, c);
        }
        sealed class LayoutChoice
        {
            public string Key, Name;
            public override string ToString() { return Name; }
        }
        void BuildMonitorChoices(FlowLayoutPanel p)
        {
            try
            {
                var catalog = ZoneCatalog.Load(edit, Guid.Empty);
                foreach (var m in catalog.Monitors)
                {
                    var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 265, DropDownWidth = 440 };
                    c.Items.Add(new LayoutChoice { Key = "auto", Name = "Automatic (active FancyZones)" });
                    c.Items.Add(new LayoutChoice { Key = "basic", Name = "Basic layout" });
                    c.Items.Add(new LayoutChoice { Key = "none", Name = "Full screen only" });
                    foreach (var choice in catalog.Choices)
                        c.Items.Add(new LayoutChoice { Key = choice.Key, Name = choice.Value });
                    string current = edit.OverrideFor(m.Key); int index = 0;
                    for (int i = 0; i < c.Items.Count; i++) if (((LayoutChoice)c.Items[i]).Key == current) index = i;
                    c.SelectedIndex = index;
                    monitorChoices.Add(Tuple.Create(m, c));
                    Row(p, m.Label + (m.Primary ? " (primary)" : ""), m.Bounds.Width + " x " + m.Bounds.Height + "  /  " + m.Model + "\n" + m.Status, c);
                }
            }
            catch (Exception ex) { Section(p, "Monitor information unavailable", ex.Message); }
        }
        void LoadControls()
        {
            loading = true;
            foreach (var pair in fields)
            {
                object v = typeof(Options).GetField(pair.Key).GetValue(edit);
                var numeric = pair.Value as NumericUpDown; var check = pair.Value as CheckBox; var choice = pair.Value as ComboBox;
                if (numeric != null) numeric.Value = Math.Max(numeric.Minimum, Math.Min(numeric.Maximum, Convert.ToDecimal(v)));
                else if (check != null) check.Checked = (bool)v;
                else if (choice != null) choice.SelectedIndex = (int)v;
                else pair.Value.Text = Convert.ToString(v);
            }
            loading = false; UpdatePageControls(); QueuePreview();
        }
        bool ApplyEdit()
        {
            if (loading || DismissedByFocusLoss) return false;
            try
            {
                ReadDraft();
                if (DismissedByFocusLoss) return false;
                edit.Validate(); CommitDraft(); applied(edit.Clone());
                dismissOnFocusLoss = edit.HideOnFocusLoss;
                QueuePreview(); return true;
            }
            catch (Exception ex) { if (!DismissedByFocusLoss) MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        }
        protected override void Dispose(bool disposing) { if (disposing) { DisposeSettingsDismissal(); DisposeExtras(); settingHints.Dispose(); sectionFont.Dispose(); } base.Dispose(disposing); }
    }
}
