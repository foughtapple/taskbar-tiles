// Separate layout rectangles, not overlapping DockStyle.Fill siblings.
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class SettingsWorkspaceGeometry
    {
        internal Rectangle Navigation, Editor, Preview;
        internal bool PreviewBelow;
        internal static SettingsWorkspaceGeometry Build(Size size, float dpi, bool preview)
        {
            var g = new SettingsWorkspaceGeometry();
            Func<int,int> s = value => Math.Max(1, (int)Math.Round(value * dpi));
            int w = Math.Max(1, size.Width), h = Math.Max(1, size.Height), gap = s(10);
            int rail = Math.Min(s(170), Math.Max(s(100), w / 4));
            rail = Math.Min(rail, Math.Max(1, w - gap - s(160)));
            g.Navigation = new Rectangle(0, 0, rail, h);
            int x = Math.Min(w - 1, rail + gap), remaining = Math.Max(1, w - x);
            g.Editor = new Rectangle(x, 0, remaining, h);
            if (!preview) return g;
            g.PreviewBelow = remaining < s(920);
            if (g.PreviewBelow)
            {
                int ph = Math.Min(s(210), Math.Max(s(90), h / 3));
                ph = Math.Min(ph, Math.Max(1, h - gap - s(180)));
                g.Editor.Height = Math.Max(1, h - ph - gap);
                g.Preview = new Rectangle(x, h - ph, remaining, ph);
            }
            else
            {
                int pw = Math.Min(s(370), remaining / 2);
                g.Editor.Width = Math.Max(1, remaining - pw - gap);
                g.Preview = new Rectangle(w - pw, 0, pw, h);
            }
            return g;
        }
    }

    sealed partial class SettingsWindow
    {
        Panel settingsContentHost;
        bool arrangingWorkspace;
        void AttachSettingsWorkspace(Panel content)
        {
            AttachSettingsNavigation(content);
            settingsContentHost = content;
            tabs.Dock = DockStyle.None;
            settingsNavigationPanel.Dock = DockStyle.None;
            previewPanel.Dock = DockStyle.None;
            content.Layout += delegate { ArrangeSettingsWorkspace(); };
            previewPanel.VisibleChanged += delegate { ArrangeSettingsWorkspace(); };
            Shown += delegate { ArrangeSettingsWorkspace(); };
            ArrangeSettingsWorkspace();
        }
        void ArrangeSettingsWorkspace()
        {
            if (arrangingWorkspace || settingsContentHost == null || previewPanel == null || IsDisposed) return;
            arrangingWorkspace = true;
            try
            {
                float dpi = Math.Max(.5f, DeviceDpi / 96f);
                bool showPreview = tabs.SelectedTab == null || tabs.SelectedTab.Text != "Stream Dock";
                var g = SettingsWorkspaceGeometry.Build(settingsContentHost.ClientSize, dpi, showPreview);
                tabs.Dock = settingsNavigationPanel.Dock = previewPanel.Dock = DockStyle.None;
                settingsNavigationPanel.Bounds = g.Navigation;
                tabs.Bounds = g.Editor;
                previewPanel.Visible = showPreview;
                if (showPreview)
                {
                    previewPanel.Bounds = g.Preview;
                    previewCaption.Height = SettingLineGeometry.Px(g.PreviewBelow ? 54 : 115, dpi);
                }
                ResizeSettingsNavigation();
            }
            finally { arrangingWorkspace = false; }
        }
        internal void AssertSettingsWorkspace()
        {
            ArrangeSettingsWorkspace();
            if (settingsContentHost == null) throw new InvalidOperationException("Missing settings content host.");
            Rectangle content = settingsContentHost.ClientRectangle;
            if (!content.Contains(tabs.Bounds) || !content.Contains(settingsNavigationPanel.Bounds) ||
                tabs.Bounds.IntersectsWith(settingsNavigationPanel.Bounds))
                throw new InvalidOperationException("Sidebar overlaps or clips the settings editor: " + SettingsNavigationDebug);
            if (previewPanel.Visible && (!content.Contains(previewPanel.Bounds) ||
                previewPanel.Bounds.IntersectsWith(tabs.Bounds) || previewPanel.Bounds.IntersectsWith(settingsNavigationPanel.Bounds)))
                throw new InvalidOperationException("Preview overlaps another settings column.");
            foreach (var button in settingsNavigationButtons.Values) button.UseMnemonic = false;
            if (tabs.SelectedTab != null)
            {
                Rectangle page = tabs.SelectedTab.RectangleToScreen(tabs.SelectedTab.ClientRectangle);
                Rectangle host = settingsContentHost.RectangleToScreen(settingsContentHost.ClientRectangle);
                Rectangle nav = settingsNavigationPanel.RectangleToScreen(settingsNavigationPanel.ClientRectangle);
                if (!host.Contains(page))
                    throw new InvalidOperationException("Settings page extends outside the settings workspace.");
                if (page.IntersectsWith(nav))
                    throw new InvalidOperationException("Settings page overlaps the navigation rail.");
                if (previewPanel.Visible)
                {
                    Rectangle preview = previewPanel.RectangleToScreen(previewPanel.ClientRectangle);
                    if (page.IntersectsWith(preview))
                        throw new InvalidOperationException("Settings page overlaps the live preview.");
                }
            }
        }
    }
}
