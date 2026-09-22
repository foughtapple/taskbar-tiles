// Taskbar Tiles 0.6 - favourites tab and a live, non-interactive settings preview.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed partial class SettingsWindow
    {
        Func<Options, Bitmap> previewCapture;
        List<AppButton> taskbarChoices;
        List<FavouriteEntry> favourites;
        ListView favouritesList;
        Label favouritesSummary;
        bool favouritesLoading, favouritesTouched, favouritesLoadFailed;
        Panel previewPanel;
        PictureBox previewPicture;
        ComboBox previewView;
        Label previewCaption;
        readonly Timer previewTimer = new Timer { Interval = 180 };
        FavouriteIcons previewIcons;
        bool previewBusy, extrasDisposed;

        void InitialiseFavouriteDraft()
        {
            try { favourites = File.Exists(FavouriteStore.FilePath) ? FavouriteStore.Read(FavouriteStore.FilePath) : FavouriteEntry.Defaults(); }
            catch (Exception ex)
            { favourites = new List<FavouriteEntry>(); favouritesLoadFailed = true; Program.Log("Favourites settings: " + ex.Message); }
        }
        void ReadDraft()
        {
            foreach (var pair in fields)
            {
                var f = typeof(Options).GetField(pair.Key);
                var n = pair.Value as NumericUpDown; var b = pair.Value as CheckBox; var c = pair.Value as ComboBox;
                object value = n != null ? (object)(int)n.Value : b != null ? (object)b.Checked : c != null ? (object)c.SelectedIndex : pair.Value.Text;
                f.SetValue(edit, value);
            }
            foreach (var pair in monitorChoices)
                if (pair.Item2.SelectedItem != null) edit.SetOverride(pair.Item1.Key, ((LayoutChoice)pair.Item2.SelectedItem).Key);
            edit.Validate();
        }
        void SaveFavouriteDraft()
        {
            // Preserve an unreadable file unless the user explicitly creates a replacement.
            if (favouritesLoadFailed && !favouritesTouched) return;
            if (favouritesLoadFailed && MessageBox.Show(this, "Your existing favourites file could not be read. Replace it with this list? A .bak copy is retained.", "Replace favourites", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                throw new InvalidOperationException("Favourites were not replaced.");
            if (DismissedByFocusLoss) throw new OperationCanceledException("Settings were dismissed without saving.");
            FavouriteStore.Save(favourites); favouritesTouched = false; favouritesLoadFailed = false;
        }
        void CommitDraft()
        {
            string settingsPath = Options.FilePath, favouritesPath = FavouriteStore.FilePath;
            byte[] previousSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            byte[] previousFavourites = File.Exists(favouritesPath) ? File.ReadAllBytes(favouritesPath) : null;
            bool oldStartup = File.Exists(Switcher.StartupPath), touched = favouritesTouched, failed = favouritesLoadFailed;
            try
            {
                SaveFavouriteDraft(); edit.Save(); Switcher.SetStartup(startup.Checked);
            }
            catch
            {
                // Restore the pre-Apply state on a file/startup failure, rather than leaving half an edit saved.
                try { RestoreDraftFile(settingsPath, previousSettings); } catch (Exception ex) { Program.Log("Settings rollback: " + ex.Message); }
                try { RestoreDraftFile(favouritesPath, previousFavourites); } catch (Exception ex) { Program.Log("Favourites rollback: " + ex.Message); }
                try { Switcher.SetStartup(oldStartup); } catch (Exception ex) { Program.Log("Startup rollback: " + ex.Message); }
                favouritesTouched = touched; favouritesLoadFailed = failed; throw;
            }
        }
        static void RestoreDraftFile(string path, byte[] data)
        {
            if (data == null) { if (File.Exists(path)) File.Delete(path); return; }
            string temp = path + ".rollback.tmp"; File.WriteAllBytes(temp, data);
            if (File.Exists(path)) File.Replace(temp, path, null, true); else File.Move(temp, path);
        }
        void AddQuickAccessPage()
        {
            var p = Page("Quick access");
            // Keep startup tools last; the new favourites collection has its own tab.
            var tab = p.Parent as TabPage; tabs.TabPages.Remove(tab); tabs.TabPages.Insert(2, tab);
            Section(p, "Bottom action bar", "Search opens an integrated launcher in this window. It does not open Windows Search. Recent apps and Favourites remain at the bottom-right.");
            Check(p, "WindowsSearchButton", "Show integrated Search on the bottom-left");
            Check(p, "FavouritesButton", "Show the Favourites launcher on the bottom-right");
            Check(p, "DesktopButton", "Show the desktop shortcut (Win+D)");
            Check(p, "ClipboardButton", "Show Windows clipboard history (Win+V; no clipboard contents are read)");
            Number(p, "FooterButtonHeight", "Bottom button height", "Search, Favourites and quick-action button height, in logical pixels.", 32, 64, 4);
            Section(p, "Navigation extras", "These shortcuts are local to Taskbar Tiles; they do not change other applications' keyboard shortcuts.");
            Check(p, "LocalHotkeys", "Enable Ctrl+Space for Favourites, Ctrl+Shift+S for Search, Ctrl+, for Settings");
            Check(p, "MiddleClickClose", "Middle-click an open-window preview to send a normal close request");
            Check(p, "LiveSettingsPreview", "Update the settings preview while I edit (no saving or launching)");
            Section(p, "Shortcut reference", "F1 shows all shortcuts. Favourites support arrows, Enter, Shift+Enter for a zone, and optional Alt+1 to Alt+9. Existing X-Mouse bindings stay unchanged.");
        }
        void AddFavouritesPage()
        {
            var p = Page("Favourites"); var tab = p.Parent as TabPage;
            tabs.TabPages.Remove(tab); tabs.TabPages.Insert(3, tab);
            Section(p, "Your quick-launch list", "Apps, shortcuts, folders and websites. Tick entries to show them. Order is kept unless alphabetical sorting is enabled. Apply saves changes; Cancel discards unapplied edits.");
            var buttons = new FlowLayoutPanel { Width = 700, Height = 92, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            AddTool(buttons, "Installed apps…", 140, AddInstalled);
            AddTool(buttons, "From taskbar…", 140, AddFromTaskbar);
            AddTool(buttons, "Add shortcut…", 140, delegate { EditFavourite(null); });
            AddTool(buttons, "Add folder…", 125, delegate
            {
                using (var d = new FolderBrowserDialog()) if (d.ShowDialog(this) == DialogResult.OK)
                    EditFavourite(new FavouriteEntry { Name = new DirectoryInfo(d.SelectedPath).Name, Target = d.SelectedPath, Group = "Places" });
            });
            AddTool(buttons, "Edit", 66, EditSelected); AddTool(buttons, "Remove", 85, RemoveSelected);
            AddTool(buttons, "Up", 60, delegate { MoveFavourite(-1); }); AddTool(buttons, "Down", 68, delegate { MoveFavourite(1); });
            buttons.SizeChanged += delegate
            {
                // A wrapped toolbar reserves its preferred height instead of clipping its second row.
                int h = buttons.GetPreferredSize(new Size(buttons.ClientSize.Width, 0)).Height;
                if (h > 0 && buttons.Height != h) buttons.Height = Math.Max(40, h);
            };
            p.Controls.Add(buttons);
            favouritesList = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, MultiSelect = false,
                Width = 700, Height = 210, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            favouritesList.Columns.Add("Name", 235); favouritesList.Columns.Add("Group", 115); favouritesList.Columns.Add("Target", 280);
            favouritesList.ItemChecked += delegate(object sender, ItemCheckedEventArgs e)
            { if (favouritesLoading) return; var item = e.Item.Tag as FavouriteEntry; if (item == null) return; item.Enabled = e.Item.Checked; favouritesTouched = true; UpdateFavouriteSummary(); QueuePreview(); };
            favouritesList.DoubleClick += delegate { EditSelected(); };
            p.Controls.Add(favouritesList);
            favouritesSummary = new Label { Width = 700, Height = 34, ForeColor = Theme.Muted, AutoEllipsis = true };
            p.Controls.Add(favouritesSummary);
            var files = new FlowLayoutPanel { Width = 700, Height = 44 };
            AddTool(files, "Import list…", 125, ImportFavourites); AddTool(files, "Export list…", 125, ExportFavourites); p.Controls.Add(files);
            Section(p, "Launcher appearance", "The rows use app icons followed by names, like Windows Search. Extra items are paged, not hidden below a scrolling panel.");
            Number(p, "FavouriteWidth", "Launcher width", "Logical pixels; kept inside the monitor's working area.", 360, 850, 20);
            Number(p, "FavouriteRowHeight", "Favourite row height", "Controls how large each icon-and-name row appears.", 40, 90, 4);
            Number(p, "FavouriteVisibleRows", "Preferred visible rows", "Automatically reduces on a smaller screen. Wheel or page buttons show the rest.", 3, 18, 1);
            Check(p, "FavouriteDetails", "Show a small group or website label beneath each name");
            Check(p, "FavouriteGroups", "Show a group filter when the list has multiple groups");
            Check(p, "FavouriteAlphabetical", "Sort visible favourites alphabetically instead of using my custom order");
            Check(p, "FavouriteNumberKeys", "Show and enable Alt+1 … Alt+9 for the first nine visible rows");
            PopulateFavouriteList(null);
        }
        void AddTool(FlowLayoutPanel p, string text, int width, Action callback)
        { var b = Theme.Button(text, width); b.Click += delegate { callback(); }; p.Controls.Add(b); }
        void UpdateFavouriteSummary()
        {
            if (favouritesSummary == null) return;
            favouritesSummary.Text = favouritesLoadFailed && !favouritesTouched ? "Existing favourites file could not be read; it will not be overwritten." :
                favourites.Count(e => e.Enabled) + " enabled · " + favourites.Count + " total · changes save with Apply";
        }
        void PopulateFavouriteList(string selectedId)
        {
            favouritesLoading = true; favouritesList.BeginUpdate();
            try
            {
                favouritesList.Items.Clear();
                foreach (var e in favourites)
                {
                    var item = new ListViewItem(e.Name) { Tag = e, Checked = e.Enabled };
                    item.SubItems.Add(e.Group); item.SubItems.Add(e.Target); favouritesList.Items.Add(item);
                    if (e.Id == selectedId) { item.Selected = true; item.EnsureVisible(); }
                }
                UpdateFavouriteSummary();
            }
            finally { favouritesList.EndUpdate(); favouritesLoading = false; }
            QueuePreview();
        }
        FavouriteEntry SelectedFavourite()
        { return favouritesList.SelectedItems.Count == 0 ? null : favouritesList.SelectedItems[0].Tag as FavouriteEntry; }
        void EditSelected() { var e = SelectedFavourite(); if (e != null) EditFavourite(e); }
        void EditFavourite(FavouriteEntry entry)
        {
            bool existing = entry != null && favourites.Contains(entry);
            using (var form = new FavouriteEditor(entry ?? new FavouriteEntry()))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                if (!existing && favourites.Count >= 500) { MessageBox.Show(this, "The maximum is 500 favourites."); return; }
                if (existing) favourites[favourites.IndexOf(entry)] = form.Result; else favourites.Add(form.Result);
                favouritesTouched = true; PopulateFavouriteList(form.Result.Id);
            }
        }
        void RemoveSelected()
        {
            var e = SelectedFavourite(); if (e == null) return;
            if (MessageBox.Show(this, "Remove '" + e.Name + "' from Favourites? This does not uninstall the app or delete its files.", "Remove favourite", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            favourites.Remove(e); favouritesTouched = true; PopulateFavouriteList(null);
        }
        void MoveFavourite(int delta)
        {
            var e = SelectedFavourite(); if (e == null) return; int index = favourites.IndexOf(e), next = index + delta;
            if (next < 0 || next >= favourites.Count) return;
            favourites.RemoveAt(index); favourites.Insert(next, e); favouritesTouched = true; PopulateFavouriteList(e.Id);
        }
        void AddInstalled()
        {
            using (var form = new InstalledAppPicker())
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.SelectedApps.Count == 0) return;
                try { favourites = FavouriteStore.Merge(favourites, form.SelectedApps); favouritesTouched = true; PopulateFavouriteList(null); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not add apps"); }
            }
        }
        void AddFromTaskbar()
        {
            var menu = new ContextMenuStrip();
            foreach (var app in taskbarChoices)
            {
                var a = app;
                string target = !string.IsNullOrWhiteSpace(a.ShortcutPath) ? a.ShortcutPath : !string.IsNullOrWhiteSpace(a.LaunchExe) ? a.LaunchExe :
                    !string.IsNullOrWhiteSpace(a.AppId) ? @"shell:AppsFolder\" + a.AppId : "";
                if (string.IsNullOrWhiteSpace(target)) continue;
                var item = new ToolStripMenuItem(a.DisplayName);
                item.Click += delegate
                {
                    // Wait until the native context menu finishes closing before opening an editor.
                    BeginInvoke(new Action(delegate { if (!IsDisposed && !DismissedByFocusLoss) EditFavourite(new FavouriteEntry { Name = a.DisplayName, Target = target, AppId = a.AppId }); }));
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0)
            { menu.Dispose(); MessageBox.Show(this, "No launchable taskbar shortcuts have been read yet. Reopen the switcher, or use Installed apps / Add shortcut."); return; }
            menu.Closed += delegate { menu.Dispose(); }; menu.Show(Cursor.Position);
        }
        void ImportFavourites()
        {
            using (var d = new OpenFileDialog { Filter = "Taskbar Tiles favourites|*.json", Title = "Import favourites", CheckFileExists = true })
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        var incoming = FavouriteStore.Read(d.FileName);
                        var answer = MessageBox.Show(this, "Merge this list into your favourites?\n\nYes: merge (skip exact duplicates)\nNo: replace the current draft list\nCancel: do nothing\n\nNothing launches. Click Apply afterwards to save.", "Import favourites", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                        if (answer == DialogResult.Cancel) return;
                        favourites = answer == DialogResult.Yes ? FavouriteStore.Merge(favourites, incoming) : incoming;
                        favouritesTouched = true; PopulateFavouriteList(null);
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not import favourites"); }
                }
        }
        void ExportFavourites()
        {
            using (var d = new SaveFileDialog { Filter = "Taskbar Tiles favourites|*.json", FileName = "TaskbarTiles-favourites.json", Title = "Export this favourites list" })
                if (d.ShowDialog(this) == DialogResult.OK)
                    try { FavouriteStore.Write(d.FileName, favourites); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not export"); }
        }
        void CreateLivePreview(Panel content)
        {
            previewPanel = new Panel { Dock = DockStyle.Right, Width = 370, BackColor = Color.FromArgb(15, 21, 30), Padding = new Padding(12) };
            var header = new Panel { Dock = DockStyle.Top, Height = 112 };
            var title = Theme.Label("Live preview", 29); title.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            var note = Theme.Label("Preview only. Apply saves; Cancel discards edits.", 37); note.ForeColor = Theme.Muted; note.Dock = DockStyle.Top;
            var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, WrapContents = false };
            previewView = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = new Padding(0, 6, 6, 0) };
            previewView.Items.AddRange(new object[] { "Main menu", "Favourites", "Screens & zones", "Search" }); previewView.SelectedIndex = 0;
            var full = Theme.Button("Full size", 86); var refresh = Theme.Button("Refresh", 86);
            full.Click += delegate { ShowFullPreview(); }; refresh.Click += delegate { RenderPreview(true); };
            previewView.SelectedIndexChanged += delegate { QueuePreview(); };
            tools.Controls.AddRange(new Control[] { previewView, full, refresh }); header.Controls.Add(tools); header.Controls.Add(note); header.Controls.Add(title);
            previewCaption = new Label { Dock = DockStyle.Bottom, Height = 115, ForeColor = Theme.Muted, Padding = new Padding(3, 8, 3, 0), AutoEllipsis = true };
            previewPicture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(11, 17, 25) };
            previewPanel.Controls.Add(previewPicture); previewPanel.Controls.Add(previewCaption); previewPanel.Controls.Add(header); content.Controls.Add(previewPanel);
            content.SizeChanged += delegate
            {
                float dpi = Math.Max(.75f, DeviceDpi / 96f);
                bool narrow = content.ClientSize.Width < 1020 * dpi;
                previewPanel.Dock = narrow ? DockStyle.Bottom : DockStyle.Right;
                if (narrow) { previewPanel.Height = Math.Min((int)(260 * dpi), Math.Max((int)(180 * dpi), content.Height / 3)); previewCaption.Height = (int)(54 * dpi); }
                else { previewPanel.Width = (int)(370 * dpi); previewCaption.Height = (int)(115 * dpi); }
                QueuePreview();
            };
            previewTimer.Tick += delegate { RenderPreview(false); };
            Shown += delegate
            {
                previewIcons = new FavouriteIcons(this); previewIcons.Changed = QueuePreview; QueuePreview();
            };
        }
        void HookLiveChanges()
        {
            foreach (Control c in fields.Values)
            {
                var n = c as NumericUpDown; var b = c as CheckBox; var combo = c as ComboBox;
                if (n != null) n.ValueChanged += delegate { QueuePreview(); };
                else if (b != null) b.CheckedChanged += delegate { QueuePreview(); };
                else if (combo != null) combo.SelectedIndexChanged += delegate { QueuePreview(); };
                else c.TextChanged += delegate { QueuePreview(); };
            }
            foreach (var pair in monitorChoices) pair.Item2.SelectedIndexChanged += delegate { QueuePreview(); };
            tabs.SelectedIndexChanged += delegate
            {
                string title = tabs.SelectedTab == null ? "" : tabs.SelectedTab.Text;
                previewView.SelectedIndex = title == "Favourites" ? 1 : title == "Screens & zones" || title == "Monitor layouts" ? 2 : title == "Search" ? 3 : 0;
                QueuePreview();
            };
        }
        void QueuePreview()
        {
            if (extrasDisposed || DismissedByFocusLoss || loading || previewPanel == null) return;
            UpdatePageControls();
            previewTimer.Stop(); previewTimer.Start();
        }
        void RenderPreview(bool force)
        {
            previewTimer.Stop(); if (extrasDisposed || DismissedByFocusLoss || loading || previewBusy || !IsHandleCreated) return;
            previewBusy = true;
            try
            {
                ReadDraft();
                if (!force && !edit.LiveSettingsPreview)
                { previewCaption.Text = "Live preview paused. Click Refresh or Full size to preview the current draft."; return; }
                Bitmap image;
                if (previewView.SelectedIndex == 1) image = FavouriteSnapshot();
                else if (previewView.SelectedIndex == 2) image = ZoneSnapshot();
                else if (previewView.SelectedIndex == 3) image = SearchSnapshot();
                else image = previewCapture == null ? null : previewCapture(edit.Clone());
                var old = previewPicture.Image; previewPicture.Image = image; if (old != null) old.Dispose();
                if (image == null) previewCaption.Text = "Main menu preview is not available.";
                else previewCaption.Text = image.Width + " × " + image.Height + " px · fitted into this preview\n" +
                    (previewView.SelectedIndex == 0 ? "Actual menu layout; example window contents. No actions run.\n" + PreviewPageSummary() : previewView.SelectedIndex == 1 ? "Your draft favourites. Icons load locally; rows are not clickable here." : previewView.SelectedIndex == 3 ? "Search layout with illustrative results; no queries or launches run." : "Your monitor map using draft settings. No windows are moved.");
            }
            catch (Exception ex) { previewCaption.Text = "Preview unavailable: " + ex.Message; Program.Log("Settings preview: " + ex.Message); }
            finally { settingHints.SetToolTip(previewCaption, previewCaption.Text); previewBusy = false; }
        }
        Bitmap FavouriteSnapshot()
        {
            var entries = FavouriteStore.Filter(favourites, "", "", edit.FavouriteAlphabetical);
            Rectangle work = Screen.FromHandle(Handle).WorkingArea;
            float scale = FavouriteGeometry.ScaleFor(work.Size, Native.ScaleAt(Location));
            Func<int, int> s = n => FavouriteGeometry.Scaled(n, scale);
            Size size = FavouriteGeometry.PopupSize(edit, entries.Count, work.Size, scale);
            int width = size.Width, height = size.Height, perPage = FavouriteGeometry.PageSize(edit, height, scale);
            var result = new Bitmap(width, height);
            using (var g = Graphics.FromImage(result))
            using (var title = new Font("Segoe UI", 16 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var normal = new Font("Segoe UI", 12 * scale, GraphicsUnit.Pixel))
            {
                g.Clear(Theme.Background); g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawingUtil.Round(g, new Rectangle(0, 0, width - 1, height - 1), s(14), Theme.Background, Theme.Border, 1);
                var back = new Rectangle(width - s(82), s(12), s(66), s(30));
                var manage = new Rectangle(back.Left - s(92), s(12), s(84), s(30));
                TextRenderer.DrawText(g, "Favourites", title, new Rectangle(s(17), s(14), Math.Max(1, manage.Left - s(27)), s(28)), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
                foreach (var button in new[] { Tuple.Create(manage, "Manage"), Tuple.Create(back, "Back") })
                { DrawingUtil.Round(g, button.Item1, s(3), Theme.Card, Theme.Border, 1); TextRenderer.DrawText(g, button.Item2, normal, button.Item1, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter); }
                bool grouped = edit.FavouriteGroups && entries.Select(e => e.Group).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
                int groupWidth = grouped ? Math.Min(s(150), width / 3) : 0;
                var search = new Rectangle(s(16), s(57), width - s(32) - (grouped ? groupWidth + s(8) : 0), s(28));
                DrawingUtil.Round(g, search, s(4), Theme.Card, Theme.Border, 1);
                TextRenderer.DrawText(g, "Find a favourite...", normal, Rectangle.Inflate(search, -s(8), 0), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (grouped) TextRenderer.DrawText(g, "All groups", normal, new Rectangle(width - s(16) - groupWidth, s(57), groupWidth, s(28)), Theme.Text, TextFormatFlags.VerticalCenter);
                int count = Math.Min(entries.Count, perPage);
                for (int i = 0; i < count; i++)
                {
                    var e = entries[i]; var icon = previewIcons == null ? null : previewIcons.Get(e);
                    FavouriteDrawing.Row(g, new Rectangle(s(16), s(99) + i * (s(edit.FavouriteRowHeight) + s(6)), width - s(32), s(edit.FavouriteRowHeight)), e, icon, edit, i == 0, i + 1, scale);
                }
                if (count == 0) TextRenderer.DrawText(g, "Add favourites in this tab to preview them.", normal, new Rectangle(s(18), s(130), width - s(36), s(100)), Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter);
                bool paging = entries.Count > perPage;
                string footer = edit.RightClickZones ? "Right-click: choose a zone" : "Enter: open selected";
                if (paging) footer = "1/" + ((entries.Count + perPage - 1) / perPage) + "  ·  " + footer;
                TextRenderer.DrawText(g, footer, normal, new Rectangle(s(16), height - s(40), width - s(paging ? 130 : 32), s(28)), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (paging)
                {
                    TextRenderer.DrawText(g, "<", normal, new Rectangle(width - s(100), height - s(40), s(36), s(28)), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                    TextRenderer.DrawText(g, ">", normal, new Rectangle(width - s(52), height - s(40), s(36), s(28)), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                }
            }
            return result;
        }
        Bitmap ZoneSnapshot()
        {
            var catalog = ZoneCatalog.Load(edit, DisplayNative.CurrentDesktop(Native.GetForegroundWindow()));
            using (var map = new ScreenMap(edit.Clone(), 1))
            {
                map.Size = new Size(edit.PickerWidth, Math.Max(220, edit.PickerHeight - 110)); map.SetMonitors(catalog.Monitors);
                var image = new Bitmap(map.Width, map.Height); map.DrawToBitmap(image, map.ClientRectangle); return image;
            }
        }
        void ShowFullPreview()
        {
            RenderPreview(true); if (previewPicture.Image == null) return;
            using (var form = new Form())
            using (var image = (Image)previewPicture.Image.Clone())
            {
                form.Text = "Preview only - " + Convert.ToString(previewView.SelectedItem) + " - no actions will run";
                form.StartPosition = FormStartPosition.CenterParent; form.ShowInTaskbar = false; form.BackColor = Theme.Background;
                form.ClientSize = image.Size;
                var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image };
                form.Controls.Add(picture); form.KeyPreview = true;
                form.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) form.Close(); };
                form.Shown += delegate { FormFit.Fit(form); }; form.ShowDialog(this); picture.Image = null;
            }
        }
        void DisposeExtras()
        {
            extrasDisposed = true; previewTimer.Stop(); previewTimer.Dispose();
            if (previewIcons != null) previewIcons.Dispose();
            if (previewPicture != null && previewPicture.Image != null) { previewPicture.Image.Dispose(); previewPicture.Image = null; }
        }
    }
}
