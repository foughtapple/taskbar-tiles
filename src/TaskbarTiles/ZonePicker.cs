// Taskbar Tiles 0.3.1 - fit-all destination picker.
// The map uses CLIENT coordinates only. Display/zone coordinates are never scaled
// in the selected destination: only the diagram is fitted to the available space.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class MapCard
    {
        public MonitorData Monitor;
        public Rectangle Card, Screen, FullButton, Caption;
        public int ColumnGroup, RowGroup;
        public float DrawScale;
        public List<Tuple<Rectangle, ZoneDestination>> ZoneButtons = new List<Tuple<Rectangle, ZoneDestination>>();
        public void Offset(int dx, int dy)
        {
            Card.Offset(dx, dy); Screen.Offset(dx, dy); FullButton.Offset(dx, dy); Caption.Offset(dx, dy);
            for (int i = 0; i < ZoneButtons.Count; i++)
            {
                var r = ZoneButtons[i].Item1; r.Offset(dx, dy);
                ZoneButtons[i] = Tuple.Create(r, ZoneButtons[i].Item2);
            }
        }
    }
    static class MapLayout
    {
        // Fit the COMPLETE cards, including full-screen buttons and numbered
        // shortcuts. v0.3 fitted only the screen faces before adding decorations,
        // which made the completed diagram taller than its viewport.
        internal static List<MapCard> Build(List<MonitorData> monitors, Options options, Size viewport, float dpi)
        {
            if (monitors == null || monitors.Count == 0 || viewport.Width < 4 || viewport.Height < 4)
                return new List<MapCard>();
            monitors = monitors.Where(m => m.Bounds.Width > 0 && m.Bounds.Height > 0).ToList();
            if (monitors.Count == 0) return new List<MapCard>();
            dpi = Math.Max(.25f, dpi);
            int pad = Math.Min((int)Math.Round(14 * dpi), Math.Min(viewport.Width, viewport.Height) / 12);
            var available = new Size(Math.Max(1, viewport.Width - 2 * pad), Math.Max(1, viewport.Height - 2 * pad));
            Rectangle desktop = monitors.Select(m => m.Bounds).Aggregate(Rectangle.Union);
            // Keep a useful diagram at the user's preferred button size first.
            // For a very small viewport or oversized controls, scale everything
            // together rather than making the screens vanish or adding scrolling.
            double minimum = Math.Max(72 * dpi / monitors.Min(m => m.Bounds.Width),
                                      40 * dpi / monitors.Min(m => m.Bounds.Height));
            var best = BuildAt(monitors, options, minimum, dpi);
            if (Fits(best, available))
            {
                double lo = minimum;
                double hi = Math.Max(minimum, Math.Min(available.Width / (double)desktop.Width,
                                                      available.Height / (double)desktop.Height));
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    double factor = (lo + hi) / 2;
                    var candidate = BuildAt(monitors, options, factor, dpi);
                    if (Fits(candidate, available)) { best = candidate; lo = factor; }
                    else hi = factor;
                }
            }
            FitAndCentre(best, viewport, available);
            return best;
        }
        static bool Fits(List<MapCard> cards, Size available)
        {
            Rectangle bounds = Extent(cards);
            return bounds.Width <= available.Width && bounds.Height <= available.Height;
        }
        static Rectangle Extent(List<MapCard> cards)
        { return cards.Select(c => c.Card).Aggregate(Rectangle.Union); }
        // Group genuinely aligned columns and nearly aligned rows. Inserting room
        // above one monitor moves its row with it, instead of leaving a neighbour
        // stranded halfway up the screen. Original small offsets are retained.
        static Dictionary<MonitorData, int> Groups(List<MonitorData> monitors, bool columns)
        {
            Func<MonitorData, double> anchor = m => columns ? m.Bounds.Left + m.Bounds.Width / 2.0 : m.Bounds.Top;
            var result = new Dictionary<MonitorData, int>();
            MonitorData first = null; int group = -1;
            foreach (var m in monitors.OrderBy(anchor).ThenBy(m => m.Number))
            {
                double tolerance = first == null ? 0 : (columns ? .025 * Math.Min(first.Bounds.Width, m.Bounds.Width) : .10 * Math.Min(first.Bounds.Height, m.Bounds.Height));
                if (first == null || anchor(m) - anchor(first) > tolerance) { first = m; group++; }
                result[m] = group;
            }
            return result;
        }
        static void ShiftGroup(List<MapCard> cards, MapCard target, bool horizontal, int distance)
        {
            if (distance <= 0) return;
            foreach (var c in cards)
                if (horizontal ? c.ColumnGroup == target.ColumnGroup : c.RowGroup == target.RowGroup)
                    c.Offset(horizontal ? distance : 0, horizontal ? 0 : distance);
        }
        static List<MapCard> BuildAt(List<MonitorData> monitors, Options options, double factor, float dpi)
        {
            var cards = new List<MapCard>();
            Func<int, int> s = n => Math.Max(1, (int)Math.Round(n * dpi));
            Rectangle desktop = monitors.Select(m => m.Bounds).Aggregate(Rectangle.Union);
            var columns = Groups(monitors, true); var rows = Groups(monitors, false);
            int gap = s(18), header = s(options.FullScreenButtonHeight), above = s(8);
            int chip = s(26), chipGap = s(4), line = s(30), below = s(4);
            foreach (var m in monitors.OrderBy(m => m.Bounds.Top).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Number))
            {
                int sw = Math.Max(2, (int)Math.Round(m.Bounds.Width * factor));
                int sh = Math.Max(2, (int)Math.Round(m.Bounds.Height * factor));
                int cw = Math.Max(s(options.FullScreenButtonMinWidth), sw);
                int x = (int)Math.Round((m.Bounds.Left - desktop.Left) * factor) - (cw - sw) / 2;
                int y = (int)Math.Round((m.Bounds.Top - desktop.Top) * factor) - header - above;
                int count = options.ShowZoneButtons ? m.Zones.Count : 0;
                // Put short source text and the first few numbered shortcuts on
                // ONE line. Extra shortcuts use centred rows, never overflow.
                int firstCols = Math.Max(0, (cw - s(82) - s(8) + chipGap) / (chip + chipGap));
                int firstCount = Math.Min(count, firstCols);
                int fullCols = Math.Max(1, (cw + chipGap) / (chip + chipGap));
                int remaining = count - firstCount;
                int extraRows = (remaining + fullCols - 1) / fullCols;
                int footerY = y + header + above + sh + below;
                int captionWidth = firstCount == 0 ? cw : cw - firstCount * (chip + chipGap) + chipGap - s(8);
                var c = new MapCard { Monitor = m, ColumnGroup = columns[m], RowGroup = rows[m], DrawScale = dpi,
                    Card = new Rectangle(x, y, cw, header + above + sh + below + line * (1 + extraRows)),
                    FullButton = new Rectangle(x, y, cw, header),
                    Screen = new Rectangle(x + (cw - sw) / 2, y + header + above, sw, sh),
                    Caption = new Rectangle(x, footerY, Math.Max(1, captionWidth), line) };
                for (int i = 0; i < count; i++)
                {
                    int bx, by;
                    if (i < firstCount)
                    {
                        bx = c.Card.Right - firstCount * (chip + chipGap) + chipGap + i * (chip + chipGap);
                        by = footerY + (line - chip) / 2;
                    }
                    else
                    {
                        int n = i - firstCount, row = n / fullCols, col = n % fullCols;
                        int inRow = Math.Min(fullCols, remaining - row * fullCols);
                        bx = x + (cw - inRow * (chip + chipGap) + chipGap) / 2 + col * (chip + chipGap);
                        by = footerY + (row + 1) * line + (line - chip) / 2;
                    }
                    var z = m.Zones[i];
                    c.ZoneButtons.Add(Tuple.Create(new Rectangle(bx, by, chip, chip),
                        new ZoneDestination { Monitor = m, Bounds = z.Bounds, Number = z.Number }));
                }
                cards.Add(c);
            }
            // Add only the gaps needed to separate cards; physical monitor order
            // determines the direction. Moving aligned groups retains the familiar
            // above/below/left/right arrangement while reserving room for controls.
            for (int attempt = 0; attempt < 128; attempt++)
            {
                bool changed = false;
                for (int i = 0; i < cards.Count; i++) for (int j = i + 1; j < cards.Count; j++)
                {
                    var a = cards[i]; var b = cards[j]; Rectangle expanded = a.Card; expanded.Inflate(gap, gap);
                    if (!expanded.IntersectsWith(b.Card)) continue;
                    Rectangle ma = a.Monitor.Bounds, mb = b.Monitor.Bounds;
                    if (mb.Left >= ma.Right && a.ColumnGroup != b.ColumnGroup) ShiftGroup(cards, b, true, a.Card.Right + gap - b.Card.Left);
                    else if (ma.Left >= mb.Right && a.ColumnGroup != b.ColumnGroup) ShiftGroup(cards, a, true, b.Card.Right + gap - a.Card.Left);
                    else if (ma.Top >= mb.Bottom && a.RowGroup != b.RowGroup) ShiftGroup(cards, a, false, b.Card.Bottom + gap - a.Card.Top);
                    else if (a.RowGroup != b.RowGroup) ShiftGroup(cards, b, false, a.Card.Bottom + gap - b.Card.Top);
                    else if (a.ColumnGroup != b.ColumnGroup)
                    {
                        if (ma.Left <= mb.Left) ShiftGroup(cards, b, true, a.Card.Right + gap - b.Card.Left);
                        else ShiftGroup(cards, a, true, b.Card.Right + gap - a.Card.Left);
                    }
                    else
                    {
                        // Mirrored/overlapping display reports: still provide a
                        // separate destination, without an endless group shift.
                        b.RowGroup = cards.Max(c => c.RowGroup) + 1;
                        b.Offset(0, a.Card.Bottom + gap - b.Card.Top);
                    }
                    changed = true;
                }
                if (!changed) break;
            }
            return cards;
        }
        static void FitAndCentre(List<MapCard> cards, Size viewport, Size available)
        {
            Rectangle bounds = Extent(cards);
            double shrink = Math.Min(1, Math.Min(available.Width / (double)Math.Max(1, bounds.Width),
                                                 available.Height / (double)Math.Max(1, bounds.Height)));
            int width = (int)Math.Round(bounds.Width * shrink), height = (int)Math.Round(bounds.Height * shrink);
            int ox = (viewport.Width - width) / 2, oy = (viewport.Height - height) / 2;
            Func<Rectangle, Rectangle> fit = r => Rectangle.FromLTRB(
                ox + (int)Math.Round((r.Left - bounds.Left) * shrink),
                oy + (int)Math.Round((r.Top - bounds.Top) * shrink),
                ox + (int)Math.Round((r.Right - bounds.Left) * shrink),
                oy + (int)Math.Round((r.Bottom - bounds.Top) * shrink));
            foreach (var c in cards)
            {
                c.Card = fit(c.Card); c.Screen = fit(c.Screen); c.FullButton = fit(c.FullButton); c.Caption = fit(c.Caption);
                c.DrawScale = (float)(c.DrawScale * shrink);
                for (int i = 0; i < c.ZoneButtons.Count; i++)
                    c.ZoneButtons[i] = Tuple.Create(fit(c.ZoneButtons[i].Item1), c.ZoneButtons[i].Item2);
            }
        }
        internal static Rectangle Project(Rectangle source, Rectangle monitor, Rectangle box)
        {
            if (monitor.Width < 1 || monitor.Height < 1 || box.Width < 1 || box.Height < 1) return Rectangle.Empty;
            int l = box.Left + (int)Math.Round((source.Left - monitor.Left) * (double)box.Width / monitor.Width);
            int t = box.Top + (int)Math.Round((source.Top - monitor.Top) * (double)box.Height / monitor.Height);
            int r = box.Left + (int)Math.Round((source.Right - monitor.Left) * (double)box.Width / monitor.Width);
            int b = box.Top + (int)Math.Round((source.Bottom - monitor.Top) * (double)box.Height / monitor.Height);
            return Rectangle.Intersect(box, Rectangle.FromLTRB(l, t, Math.Max(l + 1, r), Math.Max(t + 1, b)));
        }
        // Rendering and hit-testing share these exact fitted coordinates. The
        // returned destination always retains its original DESKTOP coordinates.
        internal static ZoneDestination Hit(List<MapCard> cards, Point point)
        {
            foreach (var c in cards)
            {
                if (c.FullButton.Contains(point)) return new ZoneDestination { Monitor = c.Monitor, Maximise = true, Bounds = c.Monitor.WorkArea };
                foreach (var b in c.ZoneButtons) if (b.Item1.Contains(point)) return b.Item2;
                foreach (var z in c.Monitor.Zones.OrderBy(z => (long)z.Bounds.Width * z.Bounds.Height))
                    if (Project(z.Bounds, c.Monitor.Bounds, c.Screen).Contains(point))
                        return new ZoneDestination { Monitor = c.Monitor, Bounds = z.Bounds, Number = z.Number };
            }
            return null;
        }
        internal static string SourceLabel(MonitorData m)
        {
            if ((m.Status ?? "").StartsWith("FancyZones", StringComparison.OrdinalIgnoreCase)) return "FancyZones";
            if ((m.Status ?? "").StartsWith("Basic zones", StringComparison.OrdinalIgnoreCase)) return "Basic zones";
            return m.Zones.Count == 0 ? "Full screen only" : "Zones";
        }
    }
    // A non-scrollable canvas, not a ScrollableControl with hidden scrollbars.
    // Every resize computes a new fit, so wheel/keyboard focus cannot pan it offscreen.
    sealed class ScreenMap : Control
    {
        internal const string IdleText = "Click a zone to place the window, or Full screen to maximise.  Esc goes back.";
        readonly Options options;
        readonly float scale;
        readonly ToolTip tip = new ToolTip { InitialDelay = 500, ReshowDelay = 150, AutoPopDelay = 10000 };
        List<MonitorData> monitors = new List<MonitorData>();
        List<MapCard> cards = new List<MapCard>();
        Font zoneFont, titleFont, captionFont, chipFont;
        ZoneDestination hover, pressed;
        string hoverInfo = "";
        bool arranging;
        public Action<ZoneDestination> Chosen;
        public Action<string> HoverText;
        public ScreenMap(Options settings, float dpi)
        {
            options = settings; scale = dpi;
            BackColor = Theme.Background; DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            TabStop = false;
            SetFonts(dpi);
        }
        void SetFonts(float effective)
        {
            DisposeFonts();
            zoneFont = new Font("Segoe UI", Math.Max(6, options.ZoneLabelSize * effective), FontStyle.Bold, GraphicsUnit.Pixel);
            titleFont = new Font("Segoe UI", Math.Max(6, 13 * effective), FontStyle.Bold, GraphicsUnit.Pixel);
            captionFont = new Font("Segoe UI", Math.Max(5, 11 * effective), FontStyle.Regular, GraphicsUnit.Pixel);
            chipFont = new Font("Segoe UI", Math.Max(5, 11 * effective), FontStyle.Regular, GraphicsUnit.Pixel);
        }
        void DisposeFonts()
        {
            if (zoneFont != null) zoneFont.Dispose(); if (titleFont != null) titleFont.Dispose();
            if (captionFont != null) captionFont.Dispose(); if (chipFont != null) chipFont.Dispose();
        }
        public void SetMonitors(List<MonitorData> value) { monitors = value ?? new List<MonitorData>(); Arrange(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }
        void Arrange()
        {
            if (arranging || options == null || ClientSize.Width < 4 || ClientSize.Height < 4) return;
            arranging = true;
            try
            {
                cards = MapLayout.Build(monitors, options, ClientSize, scale);
                SetFonts(cards.Count == 0 ? scale : cards[0].DrawScale);
                // Never act on a mouse-down whose target changed during a resize.
                pressed = null; hover = null; hoverInfo = ""; tip.Hide(this);
                if (HoverText != null) HoverText(IdleText);
                Invalidate();
            }
            finally { arranging = false; }
        }
        static bool Same(ZoneDestination a, ZoneDestination b)
        { return a != null && b != null && a.Monitor.Key == b.Monitor.Key && a.Maximise == b.Maximise && a.Number == b.Number; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var c in cards)
            {
                Func<int, int> s = n => Math.Max(1, (int)Math.Round(n * c.DrawScale));
                var full = new ZoneDestination { Monitor = c.Monitor, Maximise = true, Bounds = c.Monitor.WorkArea };
                DrawingUtil.Round(g, c.FullButton, s(8), Same(hover, full) ? Color.FromArgb(47, 89, 126) : Theme.Card,
                    Same(hover, full) ? Theme.Accent : Theme.Border, 1.2f);
                Text(g, "Full screen - " + c.Monitor.Label, c.FullButton, titleFont, Theme.Text, true);
                DrawingUtil.Round(g, c.Screen, s(6), Color.FromArgb(12, 18, 27), Theme.Border, 1.2f);
                Rectangle work = MapLayout.Project(c.Monitor.WorkArea, c.Monitor.Bounds, c.Screen);
                DrawingUtil.Round(g, work, s(4), Color.FromArgb(24, 32, 45), Color.Transparent, 0);
                foreach (var z in c.Monitor.Zones)
                {
                    var target = new ZoneDestination { Monitor = c.Monitor, Bounds = z.Bounds, Number = z.Number };
                    Rectangle r = MapLayout.Project(z.Bounds, c.Monitor.Bounds, c.Screen);
                    if (r.Width < 1 || r.Height < 1) continue;
                    if (r.Width > 4 && r.Height > 4) r.Inflate(-1, -1);
                    DrawingUtil.Round(g, r, s(4), Same(hover, target) ? Color.FromArgb(62, 112, 154) : Color.FromArgb(40, 58, 81),
                        Same(hover, target) ? Theme.Accent : Color.FromArgb(85, 113, 148), Same(hover, target) ? 2 : 1);
                    if (options.ShowZoneNumbers) Text(g, z.Number.ToString(), r, zoneFont, Theme.Text, true);
                }
                if (c.Monitor.Zones.Count == 0) Text(g, "Use Full screen above", c.Screen, captionFont, Theme.Muted, true);
                string source = MapLayout.SourceLabel(c.Monitor);
                Text(g, source, c.Caption, captionFont, source == "Basic zones" ? Color.FromArgb(221, 185, 119) : Theme.Muted, false);
                foreach (var b in c.ZoneButtons)
                {
                    DrawingUtil.Round(g, b.Item1, s(5), Same(hover, b.Item2) ? Color.FromArgb(47, 89, 126) : Theme.Card,
                        Same(hover, b.Item2) ? Theme.Accent : Theme.Border, 1);
                    Text(g, b.Item2.Number.ToString(), b.Item1, chipFont, Theme.Text, true);
                }
            }
            if (cards.Count == 0) Text(g, "No monitors detected. Refresh layouts to try again.", ClientRectangle, Font, Theme.Muted, true);
        }
        static void Text(Graphics g, string text, Rectangle r, Font font, Color color, bool centre)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var f = new StringFormat { Alignment = centre ? StringAlignment.Center : StringAlignment.Near,
                LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var b = new SolidBrush(color)) g.DrawString(text, font, b, r, f);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); var h = MapLayout.Hit(cards, e.Location);
            var card = cards.FirstOrDefault(c => c.Card.Contains(e.Location));
            string info = h == null ? (card == null ? "" : card.Monitor.Label + "\n" + card.Monitor.Status) :
                h + "\n" + h.Bounds.Width + " x " + h.Bounds.Height + "\n" + h.Monitor.Status;
            Cursor = h == null ? Cursors.Default : Cursors.Hand;
            if ((Same(h, hover) || h == null && hover == null) && info == hoverInfo) return;
            hover = h; hoverInfo = info;
            if (HoverText != null) HoverText(h != null ? h + "   /   " + h.Bounds.Width + " x " + h.Bounds.Height :
                card == null ? IdleText : card.Monitor.Label + "   /   " + card.Monitor.Status);
            tip.SetToolTip(this, info); Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e); hover = null; hoverInfo = ""; tip.Hide(this); Cursor = Cursors.Default;
            if (HoverText != null) HoverText(IdleText); Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) pressed = MapLayout.Hit(cards, e.Location); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (e.Button != MouseButtons.Left) return;
            var h = MapLayout.Hit(cards, e.Location); bool accept = Same(h, pressed); pressed = null;
            if (accept && Chosen != null) Chosen(h);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { tip.Dispose(); DisposeFonts(); } base.Dispose(disposing); }
    }
    sealed class ZonePicker : Form
    {
        readonly Options options;
        readonly Guid desktop;
        readonly ScreenMap map;
        readonly Label status, title, instruction;
        readonly Panel top;
        readonly Button[] tools;
        readonly ToolTip toolTips = new ToolTip();
        readonly float scale;
        readonly Font titleFont, smallFont;
        bool arrangingHeader;
        public ZoneDestination Destination;
        public bool SettingsRequested;
        public ZonePicker(Options settings, Guid currentDesktop, Point point, string target, bool launch)
        {
            options = settings; desktop = currentDesktop;
            Text = launch ? "Open app in a zone" : "Move window to a zone";
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.Sizable; AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background; ForeColor = Theme.Text;
            scale = Math.Max(.7f, Native.ScaleAt(point));
            Rectangle area = Screen.FromPoint(point).WorkingArea;
            MinimumSize = new Size(Math.Min(S(560), area.Width - 24), Math.Min(S(380), area.Height - 24));
            Size = new Size(Math.Min(S(options.PickerWidth), area.Width - 24), Math.Min(S(options.PickerHeight), area.Height - 24));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            Font = new Font("Segoe UI", 12 * scale, GraphicsUnit.Pixel);
            titleFont = new Font("Segoe UI", 16 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            smallFont = new Font("Segoe UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            top = new Panel { Dock = DockStyle.Top, Height = S(76), BackColor = Theme.Background };
            title = new Label { Text = (launch ? "Open: " : "Move: ") + target, AutoEllipsis = true,
                ForeColor = Theme.Text, Font = titleFont, TextAlign = ContentAlignment.MiddleLeft };
            instruction = new Label { Text = "Choose a zone or a full-screen destination", AutoEllipsis = true,
                ForeColor = Theme.Muted, Font = smallFont, TextAlign = ContentAlignment.MiddleLeft };
            var refresh = Theme.Button("Refresh", 100); refresh.Click += delegate { Reload(); };
            var identify = Theme.Button("Identify", 100); identify.Click += delegate { IdentifyScreens.ShowAll(); };
            var settingsButton = Theme.Button("Settings", 92); settingsButton.Click += delegate { SettingsRequested = true; Close(); };
            var cancel = Theme.Button("Back", 72); cancel.Click += delegate { Close(); };
            tools = new[] { refresh, identify, settingsButton, cancel };
            foreach (var b in tools) { b.FlatAppearance.BorderColor = Theme.Border; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 61, 83); }
            toolTips.SetToolTip(refresh, "Refresh saved monitor layouts (F5)");
            toolTips.SetToolTip(identify, "Show the monitor number on each physical screen");
            toolTips.SetToolTip(settingsButton, "Open Taskbar Tiles settings"); toolTips.SetToolTip(cancel, "Back without moving anything (Esc)");
            top.Controls.Add(title); top.Controls.Add(instruction); top.Controls.AddRange(tools);
            status = Theme.Label(ScreenMap.IdleText, S(32)); status.Dock = DockStyle.Bottom;
            status.Padding = new Padding(S(16), 0, S(16), 0); status.ForeColor = Theme.Muted; status.Font = smallFont; status.AutoEllipsis = true;
            map = new ScreenMap(options, scale) { Dock = DockStyle.Fill, Font = Font };
            map.HoverText = text => status.Text = text;
            map.Chosen = d => { Destination = d; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(map); Controls.Add(status); Controls.Add(top);
            top.Resize += delegate { ArrangeHeader(); };
            ArrangeHeader();
            Shown += delegate { Reload(); };
            KeyPreview = true;
        }
        int S(int n) { return Math.Max(1, (int)Math.Round(n * scale)); }
        void ArrangeHeader()
        {
            if (arrangingHeader || tools == null || top == null) return;
            arrangingHeader = true;
            try
            {
                int pad = S(16), gap = S(8), width = top.ClientSize.Width;
                int[] desired = { S(100), S(100), S(92), S(72) };
                int total = desired.Sum() + 3 * gap;
                bool oneLine = width >= S(800);
                int desiredHeight = S(oneLine ? 76 : 90);
                if (top.Height != desiredHeight) top.Height = desiredHeight;
                instruction.Visible = oneLine;
                title.Bounds = new Rectangle(pad, S(10), Math.Max(1, width - 2 * pad - (oneLine ? total + gap : 0)), S(28));
                instruction.Bounds = new Rectangle(pad, S(40), title.Width, S(22));
                int x = oneLine ? width - pad - total : pad;
                if (!oneLine && total > width - 2 * pad)
                {
                    int w = Math.Max(1, (width - 2 * pad - 3 * gap) / 4);
                    for (int i = 0; i < desired.Length; i++) desired[i] = w;
                }
                for (int i = 0; i < tools.Length; i++)
                {
                    tools[i].Bounds = new Rectangle(x, S(oneLine ? 21 : 45), desired[i], S(32)); x += desired[i] + gap;
                }
            }
            finally { arrangingHeader = false; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Optional title-bar styling: ignored on Windows versions that do not
            // support these attributes. It cannot affect sizing or placement.
            try
            {
                int dark = 1, caption = ColorTranslator.ToWin32(Theme.Background), text = ColorTranslator.ToWin32(Theme.Text);
                Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                Native.DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                Native.DwmSetWindowAttribute(Handle, 36, ref text, 4);
            }
            catch { }
        }
        void Reload()
        {
            try { map.SetMonitors(ZoneCatalog.Load(options, desktop).Monitors); }
            catch (Exception ex) { status.Text = "Could not load layouts: " + ex.Message; }
        }
        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Escape) { Close(); return true; }
            if (k == Keys.F5) { Reload(); return true; }
            return base.ProcessCmdKey(ref m, k);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { toolTips.Dispose(); if (titleFont != null) titleFont.Dispose(); if (smallFont != null) smallFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
    sealed class IdentifyScreens : Form
    {
        readonly Timer timer = new Timer { Interval = 1800 };
        IdentifyScreens(Screen screen, int number)
        {
            ShowInTaskbar = false; TopMost = true; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; BackColor = Theme.Background;
            Bounds = new Rectangle(screen.WorkingArea.Left + 35, screen.WorkingArea.Top + 35, 220, 150);
            var label = new Label { Dock = DockStyle.Fill, Text = number.ToString(), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Accent, Font = new Font("Segoe UI", 64, FontStyle.Bold) };
            Controls.Add(label); timer.Tick += delegate { timer.Stop(); Close(); }; timer.Start();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000080; return cp; } }
        internal static void ShowAll() { foreach (var m in DisplayNative.Monitors()) new IdentifyScreens(Screen.FromRectangle(m.Bounds), m.Number).Show(); }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
