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
        CheckBox dockAuto;
        Label dockStatus;
        Button dockApply, dockRepair;
        bool dockBusy;
        void SetDockWorkspace()
        {
            bool dock = tabs.SelectedTab != null && tabs.SelectedTab.Text == "Stream Dock";
            if (previewPanel == null) return;
            // The taskbar preview is unrelated to module installation and used to
            // consume the entire list's height on smaller screens.
            previewPanel.Visible = !dock;
            if (dock) previewTimer.Stop();
            if (previewPanel.Parent != null) previewPanel.Parent.PerformLayout();
        }
        internal void ValidateStreamDockView(string output)
        {
            var page = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Stream Dock");
            tabs.SelectedTab = page;
            Show(); Application.DoEvents(); SetDockWorkspace(); PerformLayout(); Application.DoEvents();
            if (dockGrid.Rows.Count != DockManager.Open().Catalog.Packages.Sum(p => p.Actions.Length) || !dockApply.Enabled || !dockAuto.Visible) throw new InvalidOperationException("Stream Dock settings did not load the complete catalogue.");
            if (!dockGrid.Visible || dockGrid.ClientSize.Height < 140 || dockGrid.GetCellDisplayRectangle(0, 0, true).Height < 16 || previewPanel.Visible) throw new InvalidOperationException("Stream Dock action list is collapsed or covered by the unrelated preview.");
            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(p => p.Text == "Appearance"); Application.DoEvents();
            if (!previewPanel.Visible) throw new InvalidOperationException("Taskbar preview did not return on other settings tabs.");
            tabs.SelectedTab = page; Application.DoEvents();
            using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new Rectangle(Point.Empty, Size)); image.Save(output, System.Drawing.Imaging.ImageFormat.Png); }
            Close();
        }
        void AddStreamDockPage()
        {
            var page = new TabPage("Stream Dock") { BackColor = Theme.Background, ForeColor = Theme.Text };
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            body.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = Theme.Text, Text = "Choose the actions available in Stream Dock.\r\nClose Stream Dock before applying. Your taskbar behaviour and other plugins stay unchanged." }, 0, 0);
            dockGrid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Theme.Background, BorderStyle = BorderStyle.None, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, EnableHeadersVisualStyles = false, GridColor = Theme.Border };
            dockGrid.DefaultCellStyle.BackColor = Theme.Card; dockGrid.DefaultCellStyle.ForeColor = Theme.Text;
            dockGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(43, 66, 88); dockGrid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            dockGrid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Background; dockGrid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Accent;
            dockGrid.RowTemplate.Height = 26; dockGrid.ColumnHeadersHeight = 30;
            dockGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Available", HeaderText = "On", Width = 44 });
            dockGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Module", HeaderText = "Module", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 65 });
            dockGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "Use", ReadOnly = true, Width = 66 });
            dockGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", HeaderText = "Bundled", ReadOnly = true, Width = 85 });
            dockGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Installed", HeaderText = "Stream Dock", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 35 });
            body.Controls.Add(dockGrid, 0, 1);
            dockAuto = new CheckBox { Dock = DockStyle.Fill, Text = "Update enabled modules when Taskbar Tiles updates", ForeColor = Theme.Text, AutoSize = false };
            body.Controls.Add(dockAuto, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
            dockApply = Theme.Button("Apply Stream Dock choices", 216); dockApply.Click += delegate { ApplyDockChoices(false); };
            dockRepair = Theme.Button("Repair modules", 126); dockRepair.Click += delegate { ApplyDockChoices(true); };
            var folder = Theme.Button("Plugin folder", 113); folder.Click += delegate { try { Directory.CreateDirectory(DockManager.DefaultRoot); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + DockManager.DefaultRoot + "\"") { UseShellExecute = true }); } catch (Exception ex) { dockStatus.Text = ex.Message; } };
            var refresh = Theme.Button("Refresh list", 104); refresh.Click += delegate { if (!dockBusy) LoadDockChoices(); };
            buttons.Controls.AddRange(new Control[] { dockApply, dockRepair, folder, refresh }); body.Controls.Add(buttons, 0, 3);
            dockStatus = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Accent, Text = "Loading local catalogue...", AutoEllipsis = true }; body.Controls.Add(dockStatus, 0, 4);
            body.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Text = "Apply here handles dock modules only. Restart Stream Dock, then add actions from FoughtApple under Key or Info board.\r\nOff keeps settings; existing disabled placements may show unavailable. General Settings Apply/Cancel does not install modules." }, 0, 5);
            page.Controls.Add(body); tabs.TabPages.Add(page); LoadDockChoices();
            tabs.SelectedIndexChanged += delegate { SetDockWorkspace(); };
            Shown += delegate { SetDockWorkspace(); };
        }
        void LoadDockChoices()
        {
            dockGrid.Rows.Clear();
            try {
                var m = DockManager.Open(); dockAuto.Checked = m.State(true).AutoUpdate;
                foreach (var row in m.Rows()) { int i = dockGrid.Rows.Add(row.Selected, row.Action.Name, row.Action.Type, row.Package.Version, row.Status); dockGrid.Rows[i].Tag = row.Action.Id; }
                string last = Path.Combine(m.Store, "last-result.txt");
                dockStatus.Text = File.Exists(last) ? DockManager.ReadBounded(last, 8192) : "Catalogue " + m.Catalog.BundleVersion + ". Existing modules are detected, but nothing is managed until you Apply. New modules default to Off.";
                dockApply.Enabled = true; dockRepair.Enabled = true;
            } catch (Exception ex) { dockStatus.Text = "Stream Dock module not ready: " + ex.Message; dockApply.Enabled = false; dockRepair.Enabled = false; }
        }
        void ApplyDockChoices(bool repair)
        {
            if (dockBusy) return; dockGrid.EndEdit();
            var selected = dockGrid.Rows.Cast<DataGridViewRow>().Where(r => Convert.ToBoolean(r.Cells[0].Value)).Select(r => Convert.ToString(r.Tag)).ToArray();
            var state = new DockState { AutoUpdate = dockAuto.Checked, EnabledActions = selected };
            if (MessageBox.Show(this, "Apply these Stream Dock availability choices now?\r\n\r\nFully exit Stream Dock first. Managed plugin files may be updated or moved out of the active folder. Your saved account/printer/store details and unrelated plugins are retained.", "Stream Dock modules", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            dockBusy = true; dockGrid.Enabled = dockAuto.Enabled = dockApply.Enabled = dockRepair.Enabled = false; dockStatus.Text = "Checking packages and applying changes...";
            ThreadPool.QueueUserWorkItem(delegate {
                string result;
                try { result = DockManager.Open().Apply(state, false, repair); }
                catch (Exception ex) { result = "Needs attention: " + ex.Message + " Completed packages are retained. Close Stream Dock, check your choices and Apply again."; }
                Program.Log("Stream Dock manager: " + result);
                try { if (!IsDisposed) BeginInvoke(new Action(delegate { dockBusy = false; dockGrid.Enabled = dockAuto.Enabled = true; LoadDockChoices(); dockStatus.Text = result; })); } catch { }
            });
        }
    }
}
