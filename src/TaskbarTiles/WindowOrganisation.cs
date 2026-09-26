// Persistent app priorities for active-window organisation. No launch/close actions.
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
    sealed class PriorityApp
    {
        public string Key = "", Name = "";
        internal PriorityApp Clone() { return new PriorityApp { Key = Key, Name = Name }; }
    }
    static class WindowPriorityModel
    {
        internal const int Capacity = 50;
        internal static PriorityApp[] Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new PriorityApp[Capacity];
            var slots = new JavaScriptSerializer { MaxJsonLength = 262144 }.Deserialize<PriorityApp[]>(json);
            if (slots == null || slots.Length != Capacity) throw new InvalidDataException("Priority list must contain 50 slots.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in slots)
                if (app != null && (string.IsNullOrWhiteSpace(app.Key) || app.Key.Length > 4096 || string.IsNullOrWhiteSpace(app.Name) || app.Name.Length > 256 || !seen.Add(app.Key)))
                    throw new InvalidDataException("Priority list contains an invalid or duplicate application.");
            return slots;
        }
        internal static string Write(PriorityApp[] slots)
        {
            string text = new JavaScriptSerializer { MaxJsonLength = 262144 }.Serialize(slots);
            Read(text); return text;
        }
        internal static int Rank(PriorityApp[] slots, string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && string.Equals(slots[i].Key, key, StringComparison.OrdinalIgnoreCase)) return i + 1;
            return 0;
        }
        internal static PriorityApp[] Assign(PriorityApp[] original, PriorityApp app, int number)
        {
            if (number < 1 || number > Capacity || app == null || string.IsNullOrEmpty(app.Key))
                throw new ArgumentException("Choose a priority from 1 to 50 for an identified application.");
            var slots = original.Select(a => a == null ? null : a.Clone()).ToList();
            int old = Rank(original, app.Key) - 1, target = number - 1;
            if (old == target) { slots[target] = app.Clone(); return slots.ToArray(); }
            if (old >= 0) slots[old] = null;
            if (slots[target] == null) slots[target] = app.Clone();
            else
            {
                int gap = slots.FindIndex(target, a => a == null);
                if (gap >= 0)
                {
                    for (int i = gap; i > target; i--) slots[i] = slots[i - 1];
                    slots[target] = app.Clone();
                }
                else if (old >= 0 && old < target)
                {
                    for (int i = old; i < target; i++) slots[i] = slots[i + 1];
                    slots[target] = app.Clone();
                }
                else throw new InvalidOperationException("There is no free priority slot at or below " + number + ". Remove or move an entry first; no priority has been dropped.");
            }
            return slots.ToArray();
        }
        internal static List<WindowItem> Sort(IEnumerable<WindowItem> source, int mode, PriorityApp[] slots)
        {
            var list = source.ToList();
            if (mode == 1) return list.OrderBy(w => w.Title ?? "", StringComparer.CurrentCultureIgnoreCase).ToList();
            if (mode == 2) return list.OrderBy(w => { int n = Rank(slots, w.PriorityKey); return n == 0 ? Capacity + 1 : n; }).ToList();
            return list;
        }
        internal static string Label(int number, PriorityApp app)
        { return number.ToString("00") + "   " + (app == null ? "Unassigned" : app.Name); }
    }
    static class WindowPriorityIdentity
    {
        static readonly Dictionary<string,string> names = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        internal static void Populate(WindowItem window)
        {
            if (window.Handle == IntPtr.Zero) return;
            try
            {
                string exe = ShellIcons.ProcessFile(window.Handle), id = ShellIcons.WindowAppId(window.Handle);
                window.PriorityKey = !string.IsNullOrWhiteSpace(id) ? "appid:" + id : string.IsNullOrWhiteSpace(exe) ? "" : "exe:" + Path.GetFullPath(exe);
                string name;
                if (!names.TryGetValue(exe, out name))
                {
                    name = string.IsNullOrEmpty(exe) ? "" : FileVersionInfo.GetVersionInfo(exe).ProductName;
                    if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(exe);
                    if (names.Count >= 256) names.Clear();
                    names[exe] = name ?? "";
                }
                window.PriorityName = string.IsNullOrWhiteSpace(name) ? "Application" : name.Substring(0, Math.Min(256,name.Length));
            }
            catch { window.PriorityKey = ""; window.PriorityName = "Application"; }
        }
    }
    sealed class PriorityPicker : Form
    {
        readonly ListBox list;
        readonly PriorityApp[] slots;
        bool armed;
        internal int Chosen;
        internal bool RemoveRequested, OutsideDismissed;
        internal ListBox Choices { get { return list; } }
        internal PriorityPicker(PriorityApp[] current, string name, int rank, Point anchor)
        {
            slots = current;
            Text = "Priority - " + name; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; FormBorderStyle = FormBorderStyle.FixedToolWindow;
            AutoScaleDimensions = new SizeF(96,96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10); BackColor = Theme.Background; ForeColor = Theme.Text;
            ClientSize = new Size(430, 426);
            var heading = new Label { Dock = DockStyle.Top, Height = 50, Padding = new Padding(10,6,10,4), ForeColor = Theme.Text,
                Text = "Priority for " + name + "\r\nChoose a number. Occupied slots shift down." };
            list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 32, ScrollAlwaysVisible = true, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text };
            for (int i = 0; i < WindowPriorityModel.Capacity; i++) list.Items.Add(WindowPriorityModel.Label(i + 1, slots[i]));
            list.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var b = new SolidBrush(selected ? Color.FromArgb(39,68,94) : Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                var r = Rectangle.Inflate(e.Bounds, -8, 0);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(list.Items[e.Index]), e.Font, r, selected ? Color.White : Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
            list.SelectedIndex = Math.Max(0, rank - 1);
            list.MouseUp += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left && list.IndexFromPoint(e.Location) >= 0) Choose(); };
            list.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; Choose(); } };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var cancel = Theme.Button("Cancel", 90); cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            var remove = Theme.Button("Remove priority", 150); remove.Enabled = rank > 0;
            remove.Click += delegate { RemoveRequested = true; DialogResult = DialogResult.OK; Close(); };
            footer.Controls.Add(cancel); footer.Controls.Add(remove);
            Controls.Add(list); Controls.Add(footer); Controls.Add(heading); CancelButton = cancel;
            Shown += delegate
            {
                list.ItemHeight = SettingLineGeometry.Px(32, DeviceDpi / 96f);
                ClientSize = new Size(Width - (Width - ClientSize.Width), heading.Height + footer.Height + 10 * list.ItemHeight);
                Rectangle work = Screen.FromPoint(anchor).WorkingArea;
                Height = Math.Min(Height, work.Height - 12); Width = Math.Min(Width, work.Width - 12);
                Location = new Point(Math.Max(work.Left, Math.Min(anchor.X, work.Right - Width)), Math.Max(work.Top, Math.Min(anchor.Y, work.Bottom - Height)));
                list.Focus(); armed = true;
            };
            Deactivate += delegate
            {
                if (!armed || !Visible) return;
                IntPtr foreground = Native.GetForegroundWindow();
                if (foreground != IntPtr.Zero && WindowNative.ProcessId(foreground) != (uint)Process.GetCurrentProcess().Id)
                { OutsideDismissed = true; DialogResult = DialogResult.Cancel; Close(); }
            };
        }
        void Choose() { if (list.SelectedIndex >= 0) { Chosen = list.SelectedIndex + 1; DialogResult = DialogResult.OK; Close(); } }
    }
    sealed partial class Switcher
    {
        Rectangle organisationRect;
        string priorityJson;
        PriorityApp[] prioritySlots = new PriorityApp[50];
        PriorityApp[] Priorities()
        {
            if (priorityJson != options.WindowPrioritySlots)
            {
                try { prioritySlots = WindowPriorityModel.Read(options.WindowPrioritySlots); }
                catch (Exception ex) { prioritySlots = new PriorityApp[50]; Program.Log("Priority list unavailable: " + ex.Message); }
                priorityJson = options.WindowPrioritySlots;
            }
            return prioritySlots;
        }
        List<WindowItem> OrganiseWindows(IEnumerable<WindowItem> source)
        { return WindowPriorityModel.Sort(source, options.WindowSortMode, Priorities()); }
        void ArrangeOrganisation()
        {
            int width = Math.Max(1, Math.Min(S(190), winPrev.Left - S(220)));
            organisationRect = new Rectangle(S(204), S(12), width, S(32));
            pageInfoRect = new Rectangle(organisationRect.Right + S(10), S(14), Math.Max(1, winPrev.Left - organisationRect.Right - S(90)), S(28));
        }
        void PaintOrganisation(Graphics g)
        {
            string label = options.WindowSortMode == 1 ? "Order: A-Z" : options.WindowSortMode == 2 ? "Order: Priority" : "Order: Recent";
            DrawingUtil.Round(g, organisationRect, S(6), Theme.Card, lastMouseHit == -21 ? Theme.Accent : Theme.Border, 1);
            Label(g, label, organisationRect, false, Theme.Text, true);
        }
        void CycleOrganisation()
        {
            try { Options.SaveValue("WindowSortMode", ((options.WindowSortMode + 1) % 3).ToString()); ReloadSettings(true); }
            catch (Exception ex) { Notify("Could not save window order: " + ex.Message); }
        }
        int HitPriority(Point p)
        {
            if (!options.ShowPriorityButtons) return -100;
            for (int i = 0; i < cardRects.Count; i++)
                if (WindowHeaderGeometry.Build(cardRects[i], options, scale).Priority.Contains(p)) return 4000 + i;
            return -100;
        }
        void PaintPriority(Graphics g, WindowHeaderGeometry header, WindowItem window, int pageIndex)
        {
            if (header.Priority.IsEmpty) return;
            int rank = WindowPriorityModel.Rank(Priorities(), window.PriorityKey);
            DrawingUtil.Round(g, header.Priority, S(4), rank > 0 ? Color.FromArgb(37, 60, 83) : Theme.Card,
                lastMouseHit == 4000 + pageIndex ? Theme.Accent : Theme.Border, 1);
            Label(g, rank > 0 ? "P" + rank : "P", header.Priority, false, rank > 0 ? Theme.Accent : Theme.Muted, true);
        }
        void ChooseWindowPriority(int pageIndex)
        {
            int i = windowPage * perWindowPage + pageIndex;
            if (i < 0 || i >= windows.Count || transient != null) return;
            var window = windows[i];
            if (string.IsNullOrWhiteSpace(window.PriorityKey)) WindowPriorityIdentity.Populate(window);
            if (string.IsNullOrWhiteSpace(window.PriorityKey)) { Notify("Windows did not expose a stable application identity for this window."); return; }
            PriorityApp[] slots;
            try { slots = WindowPriorityModel.Read(options.WindowPrioritySlots); }
            catch (Exception ex) { Notify(ex.Message + " The saved list has not been overwritten."); return; }
            int rank = WindowPriorityModel.Rank(slots, window.PriorityKey);
            using (var picker = new PriorityPicker(slots, window.PriorityName, rank, PointToScreen(cardRects[pageIndex].Location)))
            {
                transient = picker;
                try { picker.ShowDialog(this); } finally { transient = null; }
                if (picker.OutsideDismissed) { Dismiss(); return; }
                if (picker.DialogResult != DialogResult.OK) return;
                try
                {
                    if (picker.RemoveRequested) { if (rank > 0) slots[rank - 1] = null; }
                    else slots = WindowPriorityModel.Assign(slots, new PriorityApp { Key = window.PriorityKey, Name = window.PriorityName }, picker.Chosen);
                    Options.SaveValue("WindowPrioritySlots", WindowPriorityModel.Write(slots)); ReloadSettings(true);
                }
                catch (Exception ex) { Notify(ex.Message); }
            }
        }
    }
    sealed partial class SettingsWindow
    {
        ListView priorityList;
        Label priorityMessage;
        bool priorityReadFailed;
        void AddWindowOrganisationPage()
        {
            var p = Page("Window organisation");
            Section(p, "Active-window order", "Recent keeps Windows' current switching order. A-Z sorts visible titles. Priority puts assigned apps first; windows of the same app retain their recent order. Launcher tiles are unchanged.");
            Choice(p, "WindowSortMode", "Default window order", "The Order button in the main menu cycles these three modes and remembers the last one.", new[] { "Recent (Windows order)", "Alphabetical (A-Z)", "Priority (1-50)" });
            Check(p, "ShowPriorityButtons", "Show the P priority button on each active window");
            Section(p, "Saved application priorities", "Use P on an active window to assign an app. Priorities survive closing its windows and restarting Taskbar Tiles. Assigning an occupied number inserts it there and shifts entries down, without dropping an app.");
            priorityList = new ListView { Width = 700, Height = 325, View = View.Details, FullRowSelect = true,
                MultiSelect = false, HideSelection = false, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
            priorityList.Columns.Add("Priority", 80); priorityList.Columns.Add("Application", 460);
            priorityList.DoubleClick += delegate { ChangePriorityNumber(); };
            p.Controls.Add(priorityList);
            var tools = new FlowLayoutPanel { Width = 700, Height = 90, WrapContents = true };
            AddTool(tools, "Move up", 100, delegate { MovePriority(-1); });
            AddTool(tools, "Move down", 110, delegate { MovePriority(1); });
            AddTool(tools, "Assign number...", 150, ChangePriorityNumber);
            AddTool(tools, "Remove priority", 145, RemovePriority);
            p.Controls.Add(tools);
            priorityMessage = new Label { Width = 700, Height = 56, ForeColor = Theme.Muted, Text = "Apply saves this draft. Cancel or clicking outside discards unapplied priority changes." };
            p.Controls.Add(priorityMessage); PopulatePriorityList(-1);
        }
        void PopulatePriorityList(int selected)
        {
            if (priorityList == null) return;
            PriorityApp[] slots;
            try { slots = WindowPriorityModel.Read(edit.WindowPrioritySlots); priorityReadFailed = false; }
            catch (Exception ex) { priorityReadFailed = true; priorityMessage.Text = ex.Message + " Existing data is retained."; return; }
            priorityList.BeginUpdate();
            try
            {
                priorityList.Items.Clear();
                for (int i = 0; i < 50; i++)
                {
                    var item = new ListViewItem((i + 1).ToString("00")); item.SubItems.Add(slots[i] == null ? "Unassigned" : slots[i].Name);
                    item.Tag = i; priorityList.Items.Add(item);
                    if (i == selected) { item.Selected = true; item.EnsureVisible(); }
                }
            }
            finally { priorityList.EndUpdate(); }
        }
        int SelectedPriority() { return priorityList == null || priorityList.SelectedItems.Count == 0 ? -1 : (int)priorityList.SelectedItems[0].Tag; }
        void SavePriorityDraft(PriorityApp[] slots, int selected)
        { edit.WindowPrioritySlots = WindowPriorityModel.Write(slots); PopulatePriorityList(selected); QueuePreview(); }
        void MovePriority(int delta)
        {
            int index = SelectedPriority(), other = index + delta;
            if (priorityReadFailed || index < 0 || other < 0 || other >= 50) return;
            var slots = WindowPriorityModel.Read(edit.WindowPrioritySlots); var app = slots[index]; slots[index] = slots[other]; slots[other] = app;
            SavePriorityDraft(slots, other);
        }
        void RemovePriority()
        {
            int index = SelectedPriority(); if (priorityReadFailed || index < 0) return;
            var slots = WindowPriorityModel.Read(edit.WindowPrioritySlots); slots[index] = null; SavePriorityDraft(slots, index);
        }
        void ChangePriorityNumber()
        {
            int index = SelectedPriority(); if (priorityReadFailed || index < 0) return;
            var slots = WindowPriorityModel.Read(edit.WindowPrioritySlots); var app = slots[index]; if (app == null) return;
            using (var picker = new PriorityPicker(slots, app.Name, index + 1, Cursor.Position))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (picker.RemoveRequested) { slots[index] = null; SavePriorityDraft(slots, index); }
                    else SavePriorityDraft(WindowPriorityModel.Assign(slots, app, picker.Chosen), picker.Chosen - 1);
                }
                catch (Exception ex) { priorityMessage.Text = ex.Message; }
            }
        }
    }
}
