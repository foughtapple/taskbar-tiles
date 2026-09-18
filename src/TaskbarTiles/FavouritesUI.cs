// Taskbar Tiles 0.6 - icon-and-name launcher, editor and installed-app picker.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    // Used by the live palette, settings preview and installer geometry checks.
    static class FavouriteGeometry
    {
        internal static int Scaled(int n, float scale) { return Math.Max(1, (int)Math.Round(n * scale)); }
        internal static float ScaleFor(Size workArea, float dpi)
        { return Math.Max(.5f, Math.Min(dpi, Math.Min(workArea.Width / 480f, workArea.Height / 440f))); }
        internal static Size PopupSize(Options o, int enabledCount, Size workArea, float scale)
        {
            int desiredRows = Math.Min(o.FavouriteVisibleRows, Math.Max(3, enabledCount));
            int height = Math.Min(workArea.Height - Scaled(24, scale), Scaled(148, scale) + desiredRows * Scaled(o.FavouriteRowHeight + 6, scale));
            return new Size(Math.Max(1, Math.Min(Scaled(o.FavouriteWidth, scale), workArea.Width - Scaled(24, scale))), Math.Max(1, height));
        }
        internal static int PageSize(Options o, int height, float scale)
        { return Math.Max(1, (height - Scaled(150, scale) + Scaled(6, scale)) / (Scaled(o.FavouriteRowHeight, scale) + Scaled(6, scale))); }
    }
    static class FavouriteDrawing
    {
        internal static void Row(Graphics g, Rectangle r, FavouriteEntry entry, Bitmap icon, Options options, bool selected, int number, float scale)
        {
            int inset = Math.Max(6, (int)(12 * scale));
            DrawingUtil.Round(g, r, Math.Max(4, (int)(9 * scale)), selected ? Color.FromArgb(45, 67, 92) : Theme.Card,
                selected ? Theme.Accent : Color.FromArgb(43, 56, 74), selected ? 1.5f : 1);
            int iconSize = Math.Max(16, Math.Min((int)(36 * scale), r.Height - inset));
            var box = new Rectangle(r.Left + inset, r.Top + (r.Height - iconSize) / 2, iconSize, iconSize);
            if (icon != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, DrawingUtil.Fit(box, icon.Width, icon.Height));
            }
            else
            {
                DrawingUtil.Round(g, box, Math.Max(3, (int)(7 * scale)), Color.FromArgb(46, 64, 88), Color.Transparent, 0);
                using (var font = new Font("Segoe UI", Math.Max(9, 12 * scale), FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, TextTools.Initials(entry.Name), font, box, Theme.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            bool details = options.FavouriteDetails && r.Height >= 44 * scale;
            int keyWidth = options.FavouriteNumberKeys && number > 0 && number <= 9 ? (int)(52 * scale) : 8;
            var text = new Rectangle(box.Right + inset, r.Top + (details ? (int)(7 * scale) : 0), Math.Max(1, r.Right - keyWidth - box.Right - inset * 2), details ? r.Height / 2 : r.Height);
            using (var font = new Font("Segoe UI", Math.Max(9, 13 * scale), FontStyle.Regular, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, entry.Name, font, text, Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (details)
            {
                var sub = new Rectangle(text.Left, r.Top + r.Height / 2, text.Width, r.Height / 2 - (int)(5 * scale));
                using (var font = new Font("Segoe UI", Math.Max(8, 10.5f * scale), GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, entry.Detail, font, sub, Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            if (keyWidth > 8)
                using (var font = new Font("Segoe UI", Math.Max(8, 10 * scale), GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, "Alt+" + number, font, new Rectangle(r.Right - keyWidth, r.Top, keyWidth - 5, r.Height), Theme.Muted,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);
        }
    }
    sealed class FavouritesWindow : Form
    {
        readonly Options options;
        readonly List<FavouriteEntry> entries;
        readonly TextBox filter = new TextBox();
        readonly ComboBox groups = new ComboBox();
        readonly Button manage, back, previous, next;
        readonly List<Rectangle> rows = new List<Rectangle>();
        readonly ToolTip tips = new ToolTip();
        readonly FavouriteIcons icons;
        readonly float scale;
        List<FavouriteEntry> visible = new List<FavouriteEntry>();
        int selected, page, perPage = 1, hover = -1;
        string pressed;
        bool ready, layingOut;
        internal FavouriteEntry Chosen;
        internal bool PlaceInZone, SettingsRequested, ReturnToMenu;
        internal FavouritesWindow(Options current, List<FavouriteEntry> favourites, Rectangle anchor)
        {
            options = current.Clone(); entries = favourites.Select(e => e.Clone()).ToList();
            Text = "Taskbar Tiles - Favourites"; ShowInTaskbar = false; TopMost = true;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Background; ForeColor = Theme.Text; DoubleBuffered = true; KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            var monitor = Screen.FromRectangle(anchor).WorkingArea;
            scale = FavouriteGeometry.ScaleFor(monitor.Size, Native.ScaleAt(anchor.Location));
            Font = new Font("Segoe UI", 12 * scale, GraphicsUnit.Pixel);
            Size = FavouriteGeometry.PopupSize(options, entries.Count(e => e.Enabled), monitor.Size, scale);
            Location = new Point(Math.Max(monitor.Left + S(12), Math.Min(anchor.Right - Width, monitor.Right - Width - S(12))),
                Math.Max(monitor.Top + S(12), Math.Min(anchor.Bottom - Height, monitor.Bottom - Height - S(12))));
            filter.BorderStyle = BorderStyle.FixedSingle; filter.BackColor = Theme.Card; filter.ForeColor = Theme.Text; filter.Font = Font;
            filter.TextChanged += delegate { ApplyFilter(); };
            groups.DropDownStyle = ComboBoxStyle.DropDownList; groups.Font = Font;
            groups.Items.Add("All groups");
            foreach (string group in entries.Where(e => e.Enabled).Select(e => e.Group).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)) groups.Items.Add(group);
            groups.SelectedIndex = 0; groups.SelectedIndexChanged += delegate { ApplyFilter(); };
            manage = Theme.Button("Manage", 92); back = Theme.Button("Back", 70); previous = Theme.Button("<", 36); next = Theme.Button(">", 36);
            foreach (var b in new[] { manage, back, previous, next }) { b.Font = Font; b.FlatAppearance.BorderColor = Theme.Border; }
            manage.Click += delegate { SettingsRequested = true; Close(); }; back.Click += delegate { ReturnToMenu = true; Close(); };
            previous.Click += delegate { Page(-1); }; next.Click += delegate { Page(1); };
            Controls.AddRange(new Control[] { filter, groups, manage, back, previous, next });
            var handle = Handle; icons = new FavouriteIcons(this);
            WindowNative.SendMessage(filter.Handle, 0x1501, IntPtr.Zero, "Find a favourite...");
            Resize += delegate { Arrange(); };
            Shown += delegate { Arrange(); ApplyFilter(); filter.Focus(); ready = true; try { int c = 2; Native.DwmSetWindowAttribute(Handle, 33, ref c, 4); } catch { } };
            Arrange(); ApplyFilter();
        }
        int S(int n) { return Math.Max(1, (int)Math.Round(n * scale)); }
        void ApplyFilter()
        {
            visible = FavouriteStore.Filter(entries, filter.Text, groups.SelectedIndex > 0 ? Convert.ToString(groups.SelectedItem) : "", options.FavouriteAlphabetical);
            selected = page = 0; Arrange();
        }
        void Arrange()
        {
            if (layingOut || manage == null) return; layingOut = true;
            try
            {
                int pad = S(16), gap = S(6), rowHeight = S(options.FavouriteRowHeight);
                back.SetBounds(Width - pad - S(66), S(12), S(66), S(30)); manage.SetBounds(back.Left - S(92), S(12), S(84), S(30));
                bool grouped = options.FavouriteGroups && groups.Items.Count > 2;
                int groupWidth = grouped ? Math.Min(S(150), Width / 3) : 0;
                filter.SetBounds(pad, S(57), Width - pad * 2 - (grouped ? groupWidth + S(8) : 0), S(28));
                groups.Visible = grouped; groups.SetBounds(Width - pad - groupWidth, S(56), groupWidth, S(30));
                perPage = FavouriteGeometry.PageSize(options, Height, scale);
                selected = Math.Max(0, Math.Min(selected, visible.Count - 1)); page = visible.Count == 0 ? 0 : selected / perPage;
                rows.Clear();
                for (int i = 0; i < Math.Min(perPage, visible.Count - page * perPage); i++)
                    rows.Add(new Rectangle(pad, S(99) + i * (rowHeight + gap), Width - pad * 2, rowHeight));
                previous.SetBounds(Width - pad - S(84), Height - S(40), S(36), S(28));
                next.SetBounds(Width - pad - S(36), Height - S(40), S(36), S(28));
                previous.Visible = next.Visible = visible.Count > perPage;
                pressed = null; hover = -1; Invalidate();
            }
            finally { layingOut = false; }
        }
        int Hit(Point p) { for (int i = 0; i < rows.Count; i++) if (rows[i].Contains(p)) return i; return -1; }
        void Select(int delta)
        {
            if (visible.Count == 0) return; selected = (selected + delta % visible.Count + visible.Count) % visible.Count;
            if (selected / perPage != page) Arrange(); else Invalidate();
        }
        void Page(int delta)
        { if (visible.Count == 0) return; int pages = (visible.Count + perPage - 1) / perPage; selected = ((page + delta + pages) % pages) * perPage; Arrange(); }
        void Accept(int index, bool zone)
        {
            if (index < 0 || index >= visible.Count || zone && !options.RightClickZones) return;
            Chosen = visible[index].Clone(); PlaceInZone = zone; DialogResult = DialogResult.OK; Close();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawingUtil.Round(g, new Rectangle(0, 0, Width - 1, Height - 1), S(14), BackColor, Theme.Border, 1);
            using (var font = new Font("Segoe UI", 16 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, "Favourites", font, new Rectangle(S(17), S(14), Math.Max(1, manage.Left - S(27)), S(28)), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            for (int i = 0; i < rows.Count; i++)
            {
                var entry = visible[page * perPage + i];
                FavouriteDrawing.Row(g, rows[i], entry, icons.Get(entry), options, hover == i || selected == page * perPage + i, i + 1, scale);
            }
            if (rows.Count == 0)
                TextRenderer.DrawText(g, entries.Any(x => x.Enabled) ? "No matching favourites." : "Add your apps with Manage.\nYou can include folders and websites too.", Font,
                    new Rectangle(S(20), S(113), Width - S(40), Math.Max(30, Height - S(180))), Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            string text = options.RightClickZones ? "Right-click: choose a zone" : "Enter: open selected";
            if (visible.Count > perPage) text = (page + 1) + "/" + ((visible.Count + perPage - 1) / perPage) + "  ·  " + text;
            TextRenderer.DrawText(g, text, Font, new Rectangle(S(16), Height - S(40), Width - S(previous.Visible ? 130 : 32), S(28)), Theme.Muted,
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); int index = Hit(e.Location); if (index == hover) return; hover = index;
            Cursor = index < 0 ? Cursors.Default : Cursors.Hand;
            tips.SetToolTip(this, index < 0 ? "" : visible[page * perPage + index].Name + "\n" + visible[page * perPage + index].Target);
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); int i = Hit(e.Location); pressed = i < 0 ? null : visible[page * perPage + i].Id; }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); int i = Hit(e.Location);
            if (i < 0 || visible[page * perPage + i].Id != pressed) return;
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) Accept(page * perPage + i, e.Button == MouseButtons.Right);
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Page(e.Delta < 0 ? 1 : -1); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (groups.Focused && (groups.DroppedDown || key == Keys.Up || key == Keys.Down || key == Keys.Enter || key == Keys.Space)) return base.ProcessCmdKey(ref msg, keyData);
            if (ActiveControl is Button && (key == Keys.Enter || key == Keys.Space)) return base.ProcessCmdKey(ref msg, keyData);
            if (key == Keys.Escape) { if (filter.TextLength > 0) filter.Clear(); else { ReturnToMenu = true; Close(); } return true; }
            if (key == Keys.Enter) { Accept(selected, (keyData & Keys.Shift) != 0); return true; }
            if (key == Keys.Down) { Select(1); return true; } if (key == Keys.Up) { Select(-1); return true; }
            if (key == Keys.PageDown) { Page(1); return true; } if (key == Keys.PageUp) { Page(-1); return true; }
            if (keyData == (Keys.Control | Keys.F)) { filter.Focus(); filter.SelectAll(); return true; }
            if (options.FavouriteNumberKeys && (keyData & Keys.Alt) != 0 && key >= Keys.D1 && key <= Keys.D9)
            { int row = (int)key - (int)Keys.D1; if (row < rows.Count) Accept(page * perPage + row, false); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnDeactivate(EventArgs e)
        { base.OnDeactivate(e); if (ready && options.HideOnFocusLoss && !IsDisposed) Close(); }
        protected override void Dispose(bool disposing)
        { if (disposing) { if (icons != null) icons.Dispose(); tips.Dispose(); } base.Dispose(disposing); }
    }
    sealed class FavouriteEditor : Form
    {
        readonly ToolTip editorHints = new ToolTip { ShowAlways = true, InitialDelay = 550, AutoPopDelay = 20000 };
        readonly Dictionary<string, TextBox> fields = new Dictionary<string, TextBox>();
        readonly CheckBox enabled;
        internal FavouriteEntry Result;
        internal FavouriteEditor(FavouriteEntry original)
        {
            Result = original.Clone(); Text = "Favourite shortcut"; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(500, 470); ClientSize = new Size(720, 560);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10); BackColor = Theme.Background; ForeColor = Theme.Text;
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 3, AutoScroll = true };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            AddField(body, "Name", "Name", Result.Name, false);
            AddField(body, "Group", "Group", Result.Group, false);
            AddField(body, "Target", "App / path / URL", Result.Target, true);
            AddField(body, "Arguments", "Arguments", Result.Arguments, false);
            AddField(body, "WorkingDirectory", "Working folder", Result.WorkingDirectory, true);
            AddField(body, "IconPath", "Custom icon", Result.IconPath, true);
            enabled = new CheckBox { Text = "Show in Favourites", Checked = Result.Enabled, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(4, 14, 4, 10) };
            int r = body.RowCount++; body.Controls.Add(enabled, 1, r);
            var note = new Label { Text = "Use Browse for an app or shortcut. For a website, enter https://… in the target. Keep arguments separate from the app path. Leave Custom icon blank for automatic detection. Environment variables such as %USERPROFILE% work. Nothing launches from this editor.", Dock = DockStyle.Fill, ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(610, 0), Padding = new Padding(0, 10, 0, 12) };
            r = body.RowCount++; body.Controls.Add(note, 0, r); body.SetColumnSpan(note, 3);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var ok = Theme.Button("Save shortcut", 130); var cancel = Theme.Button("Cancel", 90);
            ok.Click += delegate
            {
                Result.Name = fields["Name"].Text; Result.Group = fields["Group"].Text; Result.Target = fields["Target"].Text;
                Result.Arguments = fields["Arguments"].Text; Result.WorkingDirectory = fields["WorkingDirectory"].Text; Result.IconPath = fields["IconPath"].Text; Result.Enabled = enabled.Checked;
                if (!string.Equals(Result.Target, original.Target, StringComparison.OrdinalIgnoreCase)) Result.AppId = "";
                string error = Result.ValidateEntry(); if (error != null) { MessageBox.Show(this, error, "Check shortcut"); return; }
                DialogResult = DialogResult.OK; Close();
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            SettingsHelp.Tree(editorHints, ok, "Save this shortcut into the Settings draft. Apply in Settings commits it to disk; no application is launched here.");
            SettingsHelp.Tree(editorHints, cancel, "Discard changes made in this shortcut editor.");
            SettingsHelp.Tree(editorHints, enabled, "Show this entry in Favourites and, when enabled, in integrated Search. Untick to hide it without deleting the entry.");
            footer.Controls.AddRange(new Control[] { ok, cancel }); Controls.Add(body); Controls.Add(footer); AcceptButton = ok; CancelButton = cancel;
            Shown += delegate { FormFit.Fit(this); fields["Name"].Focus(); };
        }
        void AddField(TableLayoutPanel table, string key, string label, string value, bool browse)
        {
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            var title = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text };
            var box = new TextBox { Text = value, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Card, ForeColor = Theme.Text, Margin = new Padding(3, 12, 6, 8) };
            string help = key == "Name" ? "The friendly name shown in the launcher. Does not rename the actual app or file." :
                key == "Group" ? "A category of your choice, such as Work, Games or Places. Used by the favourites group filter." :
                key == "Target" ? "Executable, shortcut, file, folder, shell location or web address. Keep command-line arguments in the separate Arguments field. No command interpreter is added." :
                key == "Arguments" ? "Optional command-line arguments passed to the app. Leave blank unless required. Shortcut files may already include their own arguments." :
                key == "WorkingDirectory" ? "Optional folder the app starts from. This does not move the program. Leave blank for normal launch behaviour." :
                "Optional local icon/image source. Leave blank for the target's normal app/file icon. PNG, JPG, ICO, EXE and shortcut sources are supported.";
            SettingsHelp.Tree(editorHints, title, help); SettingsHelp.Tree(editorHints, box, help);
            fields[key] = box; table.Controls.Add(title, 0, row); table.Controls.Add(box, 1, row);
            if (browse)
            {
                var button = Theme.Button("Browse", 76); table.Controls.Add(button, 2, row); SettingsHelp.Tree(editorHints, button, "Choose a local source. " + help);
                button.Click += delegate
                {
                    if (key == "WorkingDirectory")
                    { using (var d = new FolderBrowserDialog()) if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath; return; }
                    using (var d = new OpenFileDialog { Title = key == "IconPath" ? "Choose an icon source" : "Choose an app, file or shortcut", Filter = key == "IconPath" ? "Icons and images|*.ico;*.png;*.jpg;*.exe;*.lnk|All files|*.*" : "Apps and shortcuts|*.exe;*.lnk;*.url|All files|*.*", CheckFileExists = true })
                        if (d.ShowDialog(this) == DialogResult.OK) { box.Text = d.FileName; if (key == "Target" && fields["Name"].TextLength == 0) fields["Name"].Text = Path.GetFileNameWithoutExtension(d.FileName); }
                };
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) editorHints.Dispose(); base.Dispose(disposing); }
    }
    static class FormFit
    {
        internal static void Fit(Form form)
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, Math.Max(100, area.Width - 24)), Math.Min(form.MinimumSize.Height, Math.Max(100, area.Height - 24)));
            form.Size = new Size(Math.Min(form.Width, area.Width - 24), Math.Min(form.Height, area.Height - 24));
            form.Location = new Point(area.Left + (area.Width - form.Width) / 2, area.Top + (area.Height - form.Height) / 2);
        }
    }
    sealed class InstalledAppPicker : Form
    {
        readonly ListView list = new ListView();
        readonly TextBox query = new TextBox();
        readonly Label status;
        List<FavouriteEntry> catalog = new List<FavouriteEntry>();
        readonly HashSet<string> checkedTargets = new HashSet<string>();
        bool populating;
        internal List<FavouriteEntry> SelectedApps = new List<FavouriteEntry>();
        internal InstalledAppPicker()
        {
            Text = "Add installed apps"; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(710, 540); MinimumSize = new Size(460, 340); Font = new Font("Segoe UI", 10);
            BackColor = Theme.Background; ForeColor = Theme.Text; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            var header = new Panel { Dock = DockStyle.Top, Height = 68, Padding = new Padding(12) };
            query.Dock = DockStyle.Top; query.BackColor = Theme.Card; query.ForeColor = Theme.Text; query.TextChanged += delegate { Populate(); };
            status = Theme.Label("Reading installed apps…", 24); status.Dock = DockStyle.Bottom; status.ForeColor = Theme.Muted;
            header.Controls.Add(query); header.Controls.Add(status);
            list.View = View.Details; list.CheckBoxes = true; list.FullRowSelect = true; list.HideSelection = false; list.Dock = DockStyle.Fill; list.BackColor = Theme.Card; list.ForeColor = Theme.Text;
            list.Columns.Add("App", 270); list.Columns.Add("Shortcut / source", 400);
            list.ItemChecked += delegate(object sender, ItemCheckedEventArgs e)
            { if (populating) return; var app = e.Item.Tag as FavouriteEntry; if (app == null) return; if (e.Item.Checked) checkedTargets.Add(app.Target); else checkedTargets.Remove(app.Target); };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var add = Theme.Button("Add checked apps", 160); var cancel = Theme.Button("Cancel", 90);
            add.Click += delegate { SelectedApps = catalog.Where(e => checkedTargets.Contains(e.Target)).Select(e => e.Clone()).ToList(); DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { Close(); }; CancelButton = cancel;
            footer.Controls.AddRange(new Control[] { add, cancel }); Controls.Add(list); Controls.Add(footer); Controls.Add(header);
            Shown += delegate
            {
                FormFit.Fit(this); WindowNative.SendMessage(query.Handle, 0x1501, IntPtr.Zero, "Search installed apps…"); query.Focus();
                var t = new Thread(delegate()
                {
                    List<FavouriteEntry> found; try { found = InstalledApps.Read(); } catch (Exception ex) { Program.Log(ex.Message); found = new List<FavouriteEntry>(); }
                    if (IsDisposed || !IsHandleCreated) return;
                    try { BeginInvoke(new Action(delegate { if (!IsDisposed) { catalog = found; Populate(); } })); } catch (InvalidOperationException) { }
                }) { IsBackground = true, Name = "Installed app catalogue" };
                t.SetApartmentState(ApartmentState.STA); t.Start();
            };
        }
        void Populate()
        {
            populating = true; list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var app in catalog.Where(e => e.Name.IndexOf(query.Text, StringComparison.OrdinalIgnoreCase) >= 0))
                { var item = new ListViewItem(app.Name) { Tag = app, Checked = checkedTargets.Contains(app.Target) }; item.SubItems.Add(app.Target); list.Items.Add(item); }
                status.Text = list.Items.Count + " apps shown · tick one or more to add";
            }
            finally { list.EndUpdate(); populating = false; }
        }
    }
}
