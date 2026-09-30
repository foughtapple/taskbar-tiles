using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed partial class SettingsWindow
    {
        DataGridView dockGrid;
        CheckBox dockAuto, dockEnabled;
        Label dockStatus;
        Button dockInstall, dockApply, dockRepair, dockRemove;
        bool dockBusy;
        readonly CancellationTokenSource dockStop = new CancellationTokenSource();
        void SetDockWorkspace()
        {
            bool dock = tabs.SelectedTab != null && tabs.SelectedTab.Text == "Modules";
            if (previewPanel == null) return;
            previewPanel.Visible = !dock; if (dock) previewTimer.Stop();
            if (previewPanel.Parent != null) previewPanel.Parent.PerformLayout();
        }
        internal void ValidateStreamDockView(string output)
        {
            var page = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Modules"); tabs.SelectedTab = page;
            Show(); Application.DoEvents(); SetDockWorkspace(); PerformLayout(); Application.DoEvents();
            if (dockGrid.Rows.Count != DockManager.Open().Catalog.Packages.Sum(p => p.Actions.Length) || !dockAuto.Visible || !dockEnabled.Visible) throw new InvalidOperationException("Modules settings did not load the complete action catalogue.");
            if (!dockGrid.Visible || dockGrid.ClientSize.Height < 140 || dockGrid.GetCellDisplayRectangle(0, 0, true).Height < 16 || previewPanel.Visible) throw new InvalidOperationException("Module action list is collapsed or covered by the preview: grid=" + dockGrid.ClientSize + ", visible=" + dockGrid.Visible + ", preview=" + previewPanel.Visible);
            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(p => p.Text == "Appearance"); Application.DoEvents();
            if (!previewPanel.Visible) throw new InvalidOperationException("Taskbar preview did not return on other tabs.");
            tabs.SelectedTab = page; Application.DoEvents();
            using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new Rectangle(Point.Empty, Size)); image.Save(output, System.Drawing.Imaging.ImageFormat.Png); }
            Close();
        }
        void AddStreamDockPage()
        {
            var page = new TabPage("Modules") { BackColor = Theme.Background, ForeColor = Theme.Text };
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (float height in new[] { 28f, 32f }) body.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (float height in new[] { 26f, 44f, 44f, 32f, 36f }) body.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            dockEnabled = new CheckBox { Text = "Stream Dock — optional module", Dock = DockStyle.Fill, ForeColor = Theme.Text };
            dockEnabled.CheckedChanged += delegate { SetDockButtons(); }; body.Controls.Add(dockEnabled, 0, 0);
            body.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Text = "Select functions, then Install / Update. Module updates are independent of app updates.\r\nClose Stream Dock before installing or changing its available actions." }, 0, 1);
            dockGrid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Theme.Background, BorderStyle = BorderStyle.None, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, EnableHeadersVisualStyles = false, GridColor = Theme.Border };
            dockGrid.DefaultCellStyle.BackColor = Theme.Card; dockGrid.DefaultCellStyle.ForeColor = Theme.Text;
            dockGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(43, 66, 88); dockGrid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            dockGrid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Background; dockGrid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Accent;
            dockGrid.RowTemplate.Height = 26; dockGrid.ColumnHeadersHeight = 30;
            dockGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Available", HeaderText = "On", Width = 44 });
            dockGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Module", HeaderText = "Function", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 65 });
            dockGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "Use", ReadOnly = true, Width = 66 });
            dockGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", HeaderText = "Module", ReadOnly = true, Width = 85 });
            dockGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Installed", HeaderText = "Status", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 35 });
            dockGrid.CurrentCellDirtyStateChanged += delegate { if (dockGrid.IsCurrentCellDirty) dockGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            body.Controls.Add(dockGrid, 0, 2);
            dockAuto = new CheckBox { Dock = DockStyle.Fill, Text = "Automatically update this module (checks daily while Taskbar Tiles runs)", ForeColor = Theme.Text };
            body.Controls.Add(dockAuto, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, AutoScroll = true, WrapContents = false };
            dockInstall = Theme.Button("Install / Update", 146); dockInstall.Click += delegate { RunDockOperation(true, false); };
            dockApply = Theme.Button("Apply function choices", 184); dockApply.Click += delegate { RunDockOperation(false, false); };
            dockRepair = Theme.Button("Repair module", 132); dockRepair.Click += delegate { RunDockOperation(false, true); };
            actions.Controls.AddRange(new Control[] { dockInstall, dockApply, dockRepair }); body.Controls.Add(actions, 0, 4);
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, AutoScroll = true, WrapContents = false };
            dockRemove = Theme.Button("Remove module", 143); dockRemove.Click += delegate { dockEnabled.Checked = false; RunDockOperation(false, false); };
            var folder = Theme.Button("Plugin folder", 117); folder.Click += delegate { try { Directory.CreateDirectory(DockManager.DefaultRoot); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + DockManager.DefaultRoot + "\"") { UseShellExecute = true }); } catch (Exception ex) { dockStatus.Text = ex.Message; } };
            var refresh = Theme.Button("Refresh", 93); refresh.Click += delegate { if (!dockBusy) LoadDockChoices(); };
            tools.Controls.AddRange(new Control[] { dockRemove, folder, refresh }); body.Controls.Add(tools, 0, 5);
            dockStatus = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Accent, AutoEllipsis = true }; body.Controls.Add(dockStatus, 0, 6);
            body.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Text = "Touch Return — Developing / unavailable. Installation and activation are disabled.\r\nStream Dock removal preserves private settings and scenes. General Apply/Cancel does not install modules." }, 0, 7);
            page.Controls.Add(body); tabs.TabPages.Add(page); LoadDockChoices();
            tabs.SelectedIndexChanged += delegate { SetDockWorkspace(); }; Shown += delegate { SetDockWorkspace(); };
            FormClosed += delegate { dockStop.Cancel(); };
        }
        void SetDockButtons()
        {
            if (dockGrid == null || dockApply == null) return;
            dockGrid.Enabled = dockAuto.Enabled = !dockBusy && dockEnabled.Checked;
            dockInstall.Enabled = !dockBusy && dockEnabled.Checked;
            dockApply.Enabled = !dockBusy; dockRepair.Enabled = !dockBusy && dockEnabled.Checked;
            dockEnabled.Enabled = !dockBusy;
            if (dockRemove != null) dockRemove.Enabled = !dockBusy;
        }
        DockManager DockViewManager()
        {
            try { return DockManager.Open(); }
            catch { return new DockManager(Path.Combine(Program.Home, "streamdock"), DockManager.DefaultRoot, Path.Combine(Program.Home, "StreamDockData"), null); }
        }
        void LoadDockChoices()
        {
            dockGrid.Rows.Clear();
            try {
                var m = DockViewManager(); var state = m.State(true); dockAuto.Checked = state.AutoUpdate;
                foreach (var row in m.Rows()) { int i = dockGrid.Rows.Add(row.Selected, row.Action.Name, row.Action.Type, row.Package.Version, row.Status); dockGrid.Rows[i].Tag = row.Action.Id; }
                dockEnabled.Checked = state.EnabledActions.Length != 0;
                string last = Path.Combine(m.Store, "last-result.txt");
                dockStatus.Text = File.Exists(last) ? DockManager.ReadBounded(last, 8192) : StreamDockModuleService.Downloaded(m) ? "Downloaded module " + m.Catalog.BundleVersion + ". Select functions and Apply." : "Stream Dock is optional. Its code is downloaded only when you install it.";
                SetDockButtons();
            } catch (Exception ex) { dockStatus.Text = "Module needs attention: " + ex.Message; dockInstall.Enabled = true; dockApply.Enabled = dockRepair.Enabled = false; }
        }
        void RunDockOperation(bool install, bool repair)
        {
            if (dockBusy) return; dockGrid.EndEdit();
            string[] selected = dockEnabled.Checked ? dockGrid.Rows.Cast<DataGridViewRow>().Where(r => Convert.ToBoolean(r.Cells[0].Value)).Select(r => Convert.ToString(r.Tag)).ToArray() : new string[0];
            if (install && selected.Length == 0) { dockStatus.Text = "Select at least one Stream Dock function before installing."; return; }
            var state = new DockState { AutoUpdate = dockAuto.Checked, EnabledActions = selected };
            dockBusy = true; SetDockButtons(); dockStatus.Text = install ? "Checking the module release, downloading and verifying..." : "Applying Stream Dock function choices...";
            ThreadPool.QueueUserWorkItem(delegate {
                string result;
                try {
                    if (install) result = StreamDockModuleService.InstallLatest(state, dockStop.Token);
                    else {
                        var manager = DockViewManager();
                        if (selected.Length != 0 && !StreamDockModuleService.Downloaded(manager)) throw new IOException("Choose Install / Update to download this optional module first.");
                        result = StreamDockModuleService.ApplyChoices(manager, state, repair);
                    }
                } catch (Exception ex) { result = "Needs attention: " + ex.Message; }
                Program.Log("Optional Stream Dock module: " + result);
                try { if (!IsDisposed) BeginInvoke(new Action(delegate { dockBusy = false; LoadDockChoices(); dockStatus.Text = result; })); } catch { }
            });
        }
    }
}
