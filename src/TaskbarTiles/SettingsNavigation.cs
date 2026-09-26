// Dark, compact settings navigation. The native TabControl pages remain the
// content host, but its system tab strip is hidden behind this predictable rail.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class HeaderlessSettingsTabs : TabControl
    {
        internal HeaderlessSettingsTabs()
        {
            Appearance = TabAppearance.FlatButtons;
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(1, 1);
            Multiline = true;
            Padding = Point.Empty;
            BackColor = Theme.Background;
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            using (var brush = new SolidBrush(Theme.Background)) e.Graphics.FillRectangle(brush, e.Bounds);
        }
    }

    static class SettingsNavigationModel
    {
        internal sealed class Group
        {
            internal string Name;
            internal string[] Pages;
            internal Group(string name, params string[] pages) { Name = name; Pages = pages; }
        }

        internal static readonly Group[] Groups = new[] {
            new Group("GENERAL", "Appearance", "Navigation", "Quick access"),
            new Group("LAUNCHERS", "Favourites", "Search", "Recent apps"),
            new Group("DISPLAY & INPUT", "Screens & zones", "Monitor layouts", "Touch screen monitor support"),
            new Group("INTEGRATIONS", "Stream Dock", "Updates", "Startup & tools", "Shortcut health")
        };

        internal static IEnumerable<string> OrderedPages(IEnumerable<string> actual)
        {
            var names = new HashSet<string>(actual ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in Groups)
                foreach (string page in group.Pages)
                    if (names.Contains(page) && emitted.Add(page)) yield return page;
            foreach (string page in names.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                if (emitted.Add(page)) yield return page;
        }
    }

    sealed partial class SettingsWindow
    {
        Panel settingsNavigationPanel;
        FlowLayoutPanel settingsNavigationFlow;
        readonly Dictionary<TabPage, Button> settingsNavigationButtons = new Dictionary<TabPage, Button>();
        Font settingsNavigationGroupFont;

        void AttachSettingsNavigation(Panel content)
        {
            settingsNavigationGroupFont = new Font("Segoe UI", 8, FontStyle.Bold);
            settingsNavigationPanel = new Panel {
                Dock = DockStyle.Left, Width = 205, BackColor = Color.FromArgb(14, 20, 29),
                Padding = new Padding(8, 10, 8, 10)
            };
            settingsNavigationFlow = new FlowLayoutPanel {
                Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false,
                FlowDirection = FlowDirection.TopDown, BackColor = settingsNavigationPanel.BackColor,
                Padding = new Padding(0), Margin = Padding.Empty
            };
            settingsNavigationPanel.Controls.Add(settingsNavigationFlow);
            content.Controls.Add(settingsNavigationPanel);
            settingsNavigationPanel.BringToFront();
            settingsNavigationPanel.SizeChanged += delegate { ResizeSettingsNavigation(); };
            tabs.SelectedIndexChanged += delegate { UpdateSettingsNavigationSelection(); };
        }

        void BuildSettingsNavigation()
        {
            if (settingsNavigationFlow == null) return;
            settingsNavigationFlow.SuspendLayout();
            try
            {
                settingsNavigationFlow.Controls.Clear();
                settingsNavigationButtons.Clear();
                var byName = tabs.TabPages.Cast<TabPage>().ToDictionary(p => p.Text, StringComparer.OrdinalIgnoreCase);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var group in SettingsNavigationModel.Groups)
                {
                    var pages = group.Pages.Where(byName.ContainsKey).ToArray();
                    if (pages.Length == 0) continue;
                    AddSettingsNavigationGroup(group.Name);
                    foreach (string title in pages)
                    {
                        AddSettingsNavigationButton(byName[title]);
                        used.Add(title);
                    }
                }

                var rest = tabs.TabPages.Cast<TabPage>()
                    .Where(p => !used.Contains(p.Text))
                    .OrderBy(p => p.Text, StringComparer.CurrentCultureIgnoreCase).ToArray();
                if (rest.Length > 0)
                {
                    AddSettingsNavigationGroup("OTHER");
                    foreach (var page in rest) AddSettingsNavigationButton(page);
                }
                ResizeSettingsNavigation();
                UpdateSettingsNavigationSelection();
            }
            finally { settingsNavigationFlow.ResumeLayout(true); }
        }

        void AddSettingsNavigationGroup(string text)
        {
            var label = new Label {
                Text = text, AutoSize = false, Height = 26, Width = 170,
                ForeColor = Color.FromArgb(118, 139, 166), Font = settingsNavigationGroupFont,
                TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(9, 0, 0, 4),
                Margin = new Padding(0, settingsNavigationFlow.Controls.Count == 0 ? 0 : 10, 0, 2)
            };
            settingsNavigationFlow.Controls.Add(label);
        }

        void AddSettingsNavigationButton(TabPage page)
        {
            var button = new Button {
                Text = page.Text, Tag = page, Height = 38, Width = 170,
                FlatStyle = FlatStyle.Flat, BackColor = settingsNavigationPanel.BackColor,
                ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 6, 0), Margin = new Padding(0, 1, 0, 1),
                UseVisualStyleBackColor = false, TabStop = true
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 39, 54);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(36, 50, 68);
            button.Click += delegate
            {
                var target = button.Tag as TabPage;
                if (target != null) tabs.SelectedTab = target;
            };
            settingsNavigationButtons[page] = button;
            settingsNavigationFlow.Controls.Add(button);
        }

        void ResizeSettingsNavigation()
        {
            if (settingsNavigationFlow == null) return;
            int width = Math.Max(120, settingsNavigationFlow.ClientSize.Width -
                (settingsNavigationFlow.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) - 2);
            foreach (Control control in settingsNavigationFlow.Controls)
                if (control.Width != width) control.Width = width;
        }

        void UpdateSettingsNavigationSelection()
        {
            if (settingsNavigationButtons.Count == 0) return;
            foreach (var pair in settingsNavigationButtons)
            {
                bool active = pair.Key == tabs.SelectedTab;
                pair.Value.BackColor = active ? Color.FromArgb(37, 53, 72) : settingsNavigationPanel.BackColor;
                pair.Value.ForeColor = active ? Color.White : Color.FromArgb(205, 216, 231);
                pair.Value.FlatAppearance.BorderSize = active ? 1 : 0;
                pair.Value.FlatAppearance.BorderColor = active ? Theme.Accent : settingsNavigationPanel.BackColor;
                if (active) pair.Value.Select();
            }
        }

        internal bool SettingsNavigationReady
        {
            get
            {
                var expected = SettingsNavigationModel.Groups.SelectMany(g => g.Pages).ToArray();
                var actual = new HashSet<string>(tabs.TabPages.Cast<TabPage>().Select(p => p.Text), StringComparer.OrdinalIgnoreCase);
                return settingsNavigationPanel != null && settingsNavigationPanel.Visible && settingsNavigationPanel.Width <= 175 &&
                    settingsNavigationButtons.Count == tabs.TabPages.Count &&
                    expected.All(actual.Contains) &&
                    settingsNavigationButtons.Values.All(b => b.BackColor != Color.White);
            }
        }

        void DisposeSettingsNavigation()
        {
            if (settingsNavigationGroupFont != null) { settingsNavigationGroupFont.Dispose(); settingsNavigationGroupFont = null; }
        }
    }
}
