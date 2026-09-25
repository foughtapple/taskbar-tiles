// Shared by the live menu, its settings preview and pure installer checks.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
namespace TaskbarTiles
{
    static class MenuTextMetrics
    {
        internal static int TitleBand(Options o) { return Math.Max(Math.Max(36, (int)Math.Ceiling(o.WindowTitleFontSize * 1.4) + 10), o.ShowWindowTitleIcons ? o.WindowTitleIconSize + 12 : 0); }
        internal static int AppLabelBand(Options o) { return o.ShowAppLabels ? Math.Max(30, (int)Math.Ceiling(o.AppLabelFontSize * 3.0) + 4) : 0; }
        internal static int MinimumCard(Options o) { return TitleBand(o) + (o.ShowMonitorBadges ? 32 : 10) + 44; }
        internal static int MinimumTile(Options o) { return o.ShowAppLabels ? AppLabelBand(o) + 36 : 36; }
    }
    static class BalancedGrid
    {
        internal static int[] RowCounts(int count, int maximumColumns)
        {
            count = Math.Max(0, count); maximumColumns = Math.Max(1, maximumColumns);
            if (count == 0) return new int[0];
            int rows = (count + maximumColumns - 1) / maximumColumns;
            int each = count / rows, extra = count % rows;
            return Enumerable.Range(0, rows).Select(r => each + (r >= rows - extra ? 1 : 0)).ToArray();
        }
        internal static List<Rectangle> Cards(int count, int maximumColumns, int width, int top, int cw, int ch, int gap)
        {
            var result = new List<Rectangle>(); int[] counts = RowCounts(count, maximumColumns);
            for (int row = 0; row < counts.Length; row++)
            {
                int left = (width - counts[row] * cw - (counts[row] - 1) * gap) / 2;
                for (int col = 0; col < counts[row]; col++) result.Add(new Rectangle(left + col * (cw + gap), top + row * (ch + gap), cw, ch));
            }
            return result;
        }
        internal static int NearestOnRow(IList<Rectangle> cards, int y, double x)
        {
            return Enumerable.Range(0, cards.Count).Where(i => cards[i].Top == y)
                .OrderBy(i => Math.Abs(cards[i].Left + cards[i].Width / 2.0 - x)).ThenBy(i => i).DefaultIfEmpty(-1).First();
        }
        internal static int VerticalNeighbour(IList<Rectangle> cards, int index, int direction)
        {
            if (index < 0 || index >= cards.Count) return -1;
            var ys = cards.Select(r => r.Top).Distinct().OrderBy(y => y).ToList();
            int row = ys.IndexOf(cards[index].Top) + Math.Sign(direction);
            return row < 0 || row >= ys.Count ? -1 : NearestOnRow(cards, ys[row], cards[index].Left + cards[index].Width / 2.0);
        }
    }
    sealed class MenuGeometry
    {
        internal int Width, Height, CardWidth, CardHeight, Tile, Columns, Rows, MaximumRows, TileRows, TileColumns;
        internal int WindowsPerPage, AppsPerPage, Header, Overhead, PageStart, VisibleWindows;
        internal int[] WindowRowCounts = new int[0];
        internal string Notice = "";
        internal static int Ceiling(int n, int d) { return Math.Max(1, (Math.Max(0, n) + Math.Max(1, d) - 1) / Math.Max(1, d)); }
        internal static float ScaleFor(Options o, Size work, float requested)
        {
            int overhead = (o.EnableSearch ? 98 : 56) + 22 + 42 + 54 + NotificationAreaMetrics.LogicalHeight(o) + (QuickAccessLayout.Enabled(o) ? o.FooterButtonHeight + 16 : 0);
            int minimumHeight = overhead + MenuTextMetrics.MinimumCard(o) + MenuTextMetrics.MinimumTile(o) + 44;
            return Math.Max(.35f, Math.Min(requested, Math.Min(work.Width / 520f, work.Height / (float)minimumHeight)));
        }
        internal static MenuGeometry Build(Options o, Size work, float scale, int windows, int apps, int selectedIndex = 0)
        {
            var g = new MenuGeometry(); var notes = new List<string>();
            Func<int, int> s = n => Math.Max(1, (int)Math.Round(n * scale));
            int pad = s(22), gap = s(12), maxW = Math.Max(1, work.Width - 2 * s(14)), maxH = Math.Max(1, work.Height - 2 * s(14));
            int desiredCW = s(280 * o.PreviewScale / 100), desiredCH = s(164 * o.PreviewScale / 100);
            int minCard = s(MenuTextMetrics.MinimumCard(o)), minTile = s(MenuTextMetrics.MinimumTile(o));
            g.CardWidth = Math.Min(desiredCW, Math.Max(1, maxW - 2 * pad)); g.CardHeight = Math.Max(desiredCH, minCard);
            g.Tile = Math.Min(Math.Max(s(o.TileSize), minTile), Math.Max(1, maxW - 2 * pad));
            if (g.CardHeight > desiredCH) notes.Add("Card height expanded to fit the window-title text and icon");
            if (g.Tile > s(o.TileSize)) notes.Add("App tiles expanded to keep the requested label size readable");
            g.Header = s(o.EnableSearch ? 98 : 56);
            g.Overhead = g.Header + s(22) + s(42) + s(54) + s(NotificationAreaMetrics.LogicalHeight(o)) + (QuickAccessLayout.Enabled(o) ? s(o.FooterButtonHeight + 16) : 0);
            int fitColumns = Math.Max(1, (maxW - 2 * pad + gap) / (g.CardWidth + gap));
            g.Columns = Math.Max(1, Math.Min(o.WindowColumns, fitColumns));
            // Capacity is independent of the number of windows on the current page.
            // Reserve one launcher row. Additional launcher rows yield to window capacity.
            int fitRows = Math.Max(1, (maxH - g.Overhead - g.Tile + gap) / (g.CardHeight + gap));
            g.MaximumRows = Math.Max(1, Math.Min(o.WindowRows, fitRows));
            g.WindowsPerPage = g.Columns * g.MaximumRows;
            if (g.Columns < o.WindowColumns) notes.Add(o.WindowColumns + " columns requested; " + g.Columns + " fit this monitor");
            if (g.MaximumRows < o.WindowRows) notes.Add(o.WindowRows + " rows requested; " + g.MaximumRows + " fit at this size");
            selectedIndex = Math.Max(0, Math.Min(selectedIndex, Math.Max(0, windows - 1)));
            g.PageStart = selectedIndex / g.WindowsPerPage * g.WindowsPerPage;
            g.VisibleWindows = Math.Min(g.WindowsPerPage, Math.Max(0, windows - g.PageStart));
            g.WindowRowCounts = BalancedGrid.RowCounts(g.VisibleWindows, g.Columns);
            g.Rows = Math.Max(1, g.WindowRowCounts.Length);
            int visibleColumns = g.WindowRowCounts.Length == 0 ? 1 : g.WindowRowCounts.Max();
            int minimum = Math.Min(maxW, s(760));
            int windowWidth = 2 * pad + visibleColumns * g.CardWidth + (visibleColumns - 1) * gap;
            int appWidth = Math.Min(s(o.MaxPanelWidth), 2 * pad + Math.Max(1, apps) * g.Tile + Math.Max(0, apps - 1) * gap);
            g.Width = Math.Max(1, Math.Min(maxW, Math.Max(minimum, Math.Max(windowWidth, appWidth))));
            int available = Math.Max(1, g.Width - 2 * pad);
            g.TileColumns = Math.Max(1, (available + gap) / (g.Tile + gap));
            g.TileRows = Math.Min(o.AppRows, Ceiling(apps, g.TileColumns));
            Func<int> height = () => g.Overhead + g.Rows * g.CardHeight + (g.Rows - 1) * gap + g.TileRows * g.Tile + (g.TileRows - 1) * gap;
            while (height() > maxH && g.TileRows > 1) g.TileRows--;
            if (height() > maxH) g.CardHeight -= Math.Min(height() - maxH, Math.Max(0, g.CardHeight - minCard));
            if (height() > maxH) g.Tile -= Math.Min(height() - maxH, Math.Max(0, g.Tile - minTile));
            if (height() > maxH) g.CardHeight = Math.Max(1, (maxH - g.Overhead - g.TileRows * g.Tile - (g.TileRows - 1) * gap - (g.Rows - 1) * gap) / g.Rows);
            if (g.CardWidth < desiredCW || g.CardHeight < desiredCH) notes.Add("Preview constrained by screen size; saved size is unchanged");
            g.Height = Math.Max(1, height());
            g.TileColumns = Math.Max(1, (available + gap) / (g.Tile + gap)); g.AppsPerPage = g.TileRows * g.TileColumns;
            g.Notice = string.Join(". ", notes.ToArray()); return g;
        }
        internal string Summary(Options o)
        {
            string result = "Maximum " + o.WindowColumns + " columns x " + o.WindowRows + " rows = " + (o.WindowColumns * o.WindowRows) + " windows/page.";
            if (Notice.Length > 0) result += "\nThis screen: " + WindowsPerPage + " windows/page. " + Notice + ".";
            result += "\nVisible rows: " + (WindowRowCounts.Length == 0 ? "none" : string.Join(" + ", WindowRowCounts.Select(n => n.ToString()).ToArray())) + ". Each row is centred.";
            return result;
        }
    }
}
