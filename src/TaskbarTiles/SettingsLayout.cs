// Compact, DPI-aware setting rows: title and input align, help stays directly below.
using System;
using System.Drawing;
using System.Windows.Forms;
namespace TaskbarTiles
{
    sealed class SettingLineGeometry
    {
        internal Rectangle Title, Note, Input;
        internal int Height, TextWidth;
        internal bool Stacked;
        internal static int Px(int value, float scale) { return Math.Max(1, (int)Math.Round(value * scale)); }
        internal static int ColumnWidth(int width, float scale) { return Math.Max(1, Math.Min(Px(208, scale), (width - 2 * Px(8, scale)) / 3)); }
        internal static int MeasureWidth(int width, float scale, bool hasInput)
        {
            bool stack = hasInput && width < Px(470, scale);
            return Math.Max(1, width - 2 * Px(8, scale) - (hasInput && !stack ? ColumnWidth(width, scale) + Px(18, scale) : 0));
        }
        internal static SettingLineGeometry Build(int width, float scale, int titleHeight, int noteHeight, int inputHeight, bool hasInput, bool checkbox)
        {
            var g = new SettingLineGeometry(); width = Math.Max(1, width);
            int pad = Px(8, scale), gap = Px(3, scale), column = ColumnWidth(width, scale);
            g.Stacked = hasInput && width < Px(470, scale); g.TextWidth = MeasureWidth(width, scale, hasInput);
            int line = Math.Max(titleHeight, hasInput && !g.Stacked ? inputHeight : 0);
            g.Title = new Rectangle(pad, pad + (line - titleHeight) / 2, g.TextWidth, Math.Max(1, titleHeight));
            g.Note = new Rectangle(pad, pad + line + (noteHeight > 0 ? gap : 0), g.TextWidth, Math.Max(0, noteHeight));
            int bottom = noteHeight > 0 ? g.Note.Bottom : pad + line;
            if (hasInput)
            {
                int x = g.Stacked ? pad : width - pad - column;
                int y = g.Stacked ? bottom + Px(6, scale) : pad + (line - inputHeight) / 2;
                int inputWidth = checkbox ? Px(28, scale) : column;
                g.Input = new Rectangle(x, y, Math.Min(inputWidth, Math.Max(1, width - x - pad)), Math.Max(1, inputHeight));
                bottom = Math.Max(bottom, g.Input.Bottom);
            }
            g.Height = bottom + pad; return g;
        }
    }
    sealed class SettingRow : Panel
    {
        readonly Label titleLabel, noteLabel;
        readonly Control input;
        readonly Font noteFont;
        bool arranging;
        internal SettingRow(string title, string note, Control editor)
        {
            DoubleBuffered = true; Width = 700; Margin = new Padding(0, 1, 0, 1); BackColor = Theme.Background;
            input = editor; noteFont = new Font("Segoe UI", 9);
            titleLabel = new Label { Text = title, ForeColor = Theme.Text, AutoSize = false, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
            noteLabel = new Label { Text = note, ForeColor = Theme.Muted, AutoSize = false, UseMnemonic = false, Font = noteFont };
            Controls.Add(titleLabel); Controls.Add(noteLabel);
            if (input != null)
            {
                input.Dock = DockStyle.None; input.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                input.AccessibleName = title; input.AccessibleDescription = note; Controls.Add(input);
                var checkbox = input as CheckBox;
                titleLabel.Click += delegate { if (input.Enabled) { if (checkbox != null) checkbox.Checked = !checkbox.Checked; else input.Focus(); } };
                titleLabel.Cursor = Cursors.Hand;
            }
        }
        int TextHeight(Label label, int width)
        {
            if (string.IsNullOrEmpty(label.Text)) return 0;
            var proposed = new Size(Math.Max(1, width), 100000);
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
            if (IsHandleCreated) using (var graphics = CreateGraphics()) return TextRenderer.MeasureText(graphics, label.Text, label.Font, proposed, flags).Height;
            return TextRenderer.MeasureText(label.Text, label.Font, proposed, flags).Height;
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PerformLayout(); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (Disposing || IsDisposed || arranging || titleLabel == null) { base.OnLayout(e); return; }
            arranging = true;
            try
            {
                float dpi = Math.Max(.5f, DeviceDpi / 96f);
                int tw = SettingLineGeometry.MeasureWidth(ClientSize.Width, dpi, input != null);
                int th = Math.Max(Font.Height, TextHeight(titleLabel, tw)), nh = TextHeight(noteLabel, tw);
                int ih = input == null ? 0 : input is CheckBox ? SettingLineGeometry.Px(24, dpi) : Math.Max(input.PreferredSize.Height, SettingLineGeometry.Px(26, dpi));
                var g = SettingLineGeometry.Build(ClientSize.Width, dpi, th, nh, ih, input != null, input is CheckBox);
                titleLabel.Bounds = g.Title; noteLabel.Bounds = g.Note; noteLabel.Visible = nh > 0;
                if (input != null) input.Bounds = g.Input;
                if (Height != g.Height) Height = g.Height;
                base.OnLayout(e);
            }
            finally { arranging = false; }
        }
        protected override void OnPaint(PaintEventArgs e)
        { base.OnPaint(e); using (var pen = new Pen(Color.FromArgb(30, 42, 57))) e.Graphics.DrawLine(pen, 8, Height - 1, Math.Max(8, Width - 8), Height - 1); }
        protected override void Dispose(bool disposing) { if (disposing) noteFont.Dispose(); base.Dispose(disposing); }
    }
    sealed class SettingSection : Panel
    {
        readonly Label titleLabel, noteLabel;
        readonly Font titleFont, noteFont;
        bool arranging;
        internal SettingSection(string title, string note)
        {
            DoubleBuffered = true; Width = 700; Margin = new Padding(0, 10, 0, 4); BackColor = Theme.Background;
            titleFont = new Font("Segoe UI", 11, FontStyle.Bold); noteFont = new Font("Segoe UI", 9);
            titleLabel = new Label { Text = title, Font = titleFont, ForeColor = Theme.Accent, UseMnemonic = false };
            noteLabel = new Label { Text = note, Font = noteFont, ForeColor = Theme.Muted, UseMnemonic = false };
            Controls.Add(titleLabel); Controls.Add(noteLabel);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PerformLayout(); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (Disposing || IsDisposed || arranging || titleLabel == null) { base.OnLayout(e); return; }
            arranging = true;
            try
            {
                float dpi = Math.Max(.5f, DeviceDpi / 96f); int pad = SettingLineGeometry.Px(8, dpi), width = Math.Max(1, ClientSize.Width - 2 * pad);
                int th, nh; var proposed = new Size(width, 100000); var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
                if (IsHandleCreated) using (var graphics = CreateGraphics())
                {
                    th = TextRenderer.MeasureText(graphics, titleLabel.Text, titleFont, proposed, flags).Height;
                    nh = string.IsNullOrEmpty(noteLabel.Text) ? 0 : TextRenderer.MeasureText(graphics, noteLabel.Text, noteFont, proposed, flags).Height;
                }
                else
                {
                    th = TextRenderer.MeasureText(titleLabel.Text, titleFont, proposed, flags).Height;
                    nh = string.IsNullOrEmpty(noteLabel.Text) ? 0 : TextRenderer.MeasureText(noteLabel.Text, noteFont, proposed, flags).Height;
                }
                titleLabel.SetBounds(pad, 0, width, th); noteLabel.SetBounds(pad, th + SettingLineGeometry.Px(4, dpi), width, nh); noteLabel.Visible = nh > 0;
                int height = (nh > 0 ? noteLabel.Bottom : titleLabel.Bottom) + SettingLineGeometry.Px(4, dpi);
                if (Height != height) Height = height; base.OnLayout(e);
            }
            finally { arranging = false; }
        }
        protected override void Dispose(bool disposing) { if (disposing) { titleFont.Dispose(); noteFont.Dispose(); } base.Dispose(disposing); }
    }
    sealed class SettingsPage : FlowLayoutPanel
    {
        bool sizing;
        internal SettingsPage()
        { Dock = DockStyle.Fill; AutoScroll = true; FlowDirection = FlowDirection.TopDown; WrapContents = false; Padding = new Padding(12, 6, 12, 12); DoubleBuffered = true; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (Disposing || IsDisposed || sizing) { base.OnLayout(e); return; }
            sizing = true;
            try
            {
                int width = Math.Max(80, ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 3);
                foreach (Control child in Controls)
                {
                    if (child is Button) continue;
                    int w = Math.Max(40, width - child.Margin.Horizontal);
                    if (child is CheckBox) child.MaximumSize = new Size(w, 0);
                    else if (child.Width != w) child.Width = w;
                }
                base.OnLayout(e);
            }
            finally { sizing = false; }
        }
    }
}
