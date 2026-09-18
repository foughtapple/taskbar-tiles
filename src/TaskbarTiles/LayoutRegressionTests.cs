// Pure helper tests: no real window is minimised, closed, restored or launched.
using System;
using System.Drawing;
using System.Linq;
using System.Text;
namespace TaskbarTiles
{
    static class LayoutRegressionTests
    {
        static int checks;
        static void Require(bool ok, string name) { checks++; if (!ok) throw new InvalidOperationException("FAILED: " + name); }
        internal static void Run(StringBuilder log)
        {
            checks = 0;
            var sample = new Options { WindowColumns = 6, WindowRows = 2, PreviewScale = 120 };
            var four = MenuGeometry.Build(sample, new Size(5120, 1400), 1, 4, 16);
            var seven = MenuGeometry.Build(sample, new Size(5120, 1400), 1, 7, 16);
            var full = MenuGeometry.Build(sample, new Size(5120, 1400), 1, 12, 16);
            var last = MenuGeometry.Build(sample, new Size(5120, 1400), 1, 13, 16, 12);
            Require(four.WindowsPerPage == 12 && seven.WindowsPerPage == 12 && last.WindowsPerPage == 12, "capacity derived from limits, not current count");
            Require(four.WindowRowCounts.SequenceEqual(new[] { 4 }), "four windows use one row");
            Require(seven.WindowRowCounts.SequenceEqual(new[] { 3, 4 }), "seven windows use three above four");
            Require(full.WindowRowCounts.SequenceEqual(new[] { 6, 6 }), "full page is six by two");
            Require(last.PageStart == 12 && last.WindowRowCounts.SequenceEqual(new[] { 1 }), "thirteenth window on one-row final page");
            Require(last.Height < full.Height && four.Height < seven.Height, "empty rows not reserved");
            Require(seven.CardWidth == 336 && seven.CardHeight == 196, "normal preview size retained");
            sample.WindowTitleFontSize = 28;
            Require(sample.AppLabelFontSize == 22 && sample.PreviewScale == 120 && sample.TileSize == 120, "window-title text is independent");
            sample.AppLabelFontSize = 24;
            Require(sample.WindowTitleFontSize == 28 && MenuTextMetrics.AppLabelBand(sample) >= 72, "app-label text independent with room for wrapped lines");
            var auto = Options.Parse(new[] { "ConfigVersion=5", "WindowColumns=0", "MaxPanelWidth=2000", "PreviewScale=150" });
            Require(auto.WindowColumns == 4, "old Auto columns translated once");
            for (int c = 1; c <= 12; c++) for (int r = 1; r <= 3; r++) for (int n = 0; n <= c * r; n++)
            {
                var counts = BalancedGrid.RowCounts(n, c);
                Require(counts.Sum() == n && counts.Length <= r && counts.All(v => v > 0 && v <= c), "every window fits row limits");
                if (counts.Length > 0) Require(counts.SequenceEqual(counts.OrderBy(v => v)) && counts.Last() - counts.First() <= 1, "lower rows receive extras");
                Require(n > c || counts.Length <= 1, "one row first");
                var cards = BalancedGrid.Cards(n, c, 6000, 98, 336, 196, 12);
                for (int i = 0; i < cards.Count; i++) foreach (int direction in new[] { -1, 1 })
                {
                    int next = BalancedGrid.VerticalNeighbour(cards, i, direction); if (next < 0) continue;
                    Require(Math.Sign(cards[next].Top - cards[i].Top) == direction, "vertical arrow direction");
                    double x = cards[i].Left + cards[i].Width / 2.0, distance = Math.Abs(cards[next].Left + cards[next].Width / 2.0 - x);
                    Require(cards.Where(k => k.Top == cards[next].Top).All(k => Math.Abs(k.Left + k.Width / 2.0 - x) >= distance), "vertical arrow chooses nearest visual centre");
                }
            }
            int layouts = 0, searchLayouts = 0;
            foreach (Size work in new[] { new Size(640, 440), new Size(1280, 680), new Size(1920, 1040), new Size(2560, 1400), new Size(5120, 1400) })
            foreach (float dpi in new[] { 1f, 1.25f, 1.5f, 2.25f, 3f })
            foreach (int preview in new[] { 70, 120, 220 })
            foreach (int columns in new[] { 1, 4, 6, 12 })
            foreach (int rows in new[] { 1, 2, 3 })
            foreach (int font in new[] { 9, 12, 20, 32 })
            foreach (int count in new[] { 0, 4, 7, 13, 73 })
            {
                var o = new Options { PreviewScale = preview, WindowColumns = columns, WindowRows = rows, WindowTitleFontSize = font, AppLabelFontSize = font, AppRows = 3 };
                float scale = MenuGeometry.ScaleFor(o, work, dpi); Func<int, int> s = n => Math.Max(1, (int)Math.Round(n * scale));
                int selected = count > 12 ? count - 1 : 0;
                var g = MenuGeometry.Build(o, work, scale, count, 50, selected);
                Require(g.Width > 0 && g.Height > 0 && g.Width <= work.Width && g.Height <= work.Height, "menu fits monitor with font scaling");
                Require(g.WindowsPerPage == g.Columns * g.MaximumRows && g.WindowsPerPage <= columns * rows, "effective capacity derived from fitted limits");
                Require(g.Rows <= g.MaximumRows && g.Rows >= 1 && g.WindowRowCounts.Sum() == g.VisibleWindows, "used rows match visible count");
                Require(g.CardHeight >= s(MenuTextMetrics.MinimumCard(o)) && g.Tile >= s(MenuTextMetrics.MinimumTile(o)), "large text retains minimum layout space");
                var cards = BalancedGrid.Cards(g.VisibleWindows, g.Columns, g.Width, g.Header, g.CardWidth, g.CardHeight, s(12));
                foreach (var row in cards.GroupBy(k => k.Top))
                {
                    Require(Math.Abs((row.Min(k => k.Left) + row.Max(k => k.Right)) / 2.0 - g.Width / 2.0) <= .5, "each row centred separately");
                    foreach (var card in row) Require(card.Left >= s(22) && card.Right <= g.Width - s(22) && card.Bottom < g.Height, "card fits menu");
                }
                if (g.WindowsPerPage != columns * rows) Require(g.Notice.Length > 0, "screen fallback explained");
                layouts++;
                if (columns == 6 && rows == 2 && count == 7 && font == 20)
                {
                    var q = QuickAccessLayout.Build(g.Width, g.Height, scale, o);
                    var b = SearchGeometry.Build(o, new Size(g.Width, g.Height), q.Search, scale);
                    Require(new Rectangle(0, 0, g.Width, g.Height).Contains(b.Bounds), "integrated search fits resized main menu");
                    var inside = new Rectangle(0, 0, b.Bounds.Width, b.Bounds.Height);
                    foreach (Rectangle rect in new[] { b.Input, b.Kind, b.Back, b.Refresh, b.Status, b.Previous, b.Next }) Require(inside.Contains(rect), "search controls fit");
                    searchLayouts++;
                }
            }
            int settingRows = 0;
            foreach (float dpi in new[] { .75f, 1f, 1.25f, 1.5f, 2f, 2.25f, 3f })
            foreach (int width in new[] { 280, 400, 500, 700, 900 })
            foreach (int th in new[] { 20, 40, 60 })
            foreach (int nh in new[] { 0, 16, 32, 48 })
            foreach (bool check in new[] { false, true })
            {
                Func<int, int> s = n => SettingLineGeometry.Px(n, dpi);
                var g = SettingLineGeometry.Build(s(width), dpi, s(th), nh == 0 ? 0 : s(nh), s(check ? 24 : 26), true, check);
                var bounds = new Rectangle(0, 0, s(width), g.Height);
                Require(bounds.Contains(g.Title) && bounds.Contains(g.Input) && (nh == 0 || bounds.Contains(g.Note)), "settings row contains its own label, help and input");
                Require(!g.Title.IntersectsWith(g.Input) && !g.Note.IntersectsWith(g.Input), "settings text and controls do not overlap");
                if (!g.Stacked) Require(Math.Abs(g.Title.Top + g.Title.Height / 2.0 - g.Input.Top - g.Input.Height / 2.0) <= .5, "label and input aligned");
                settingRows++;
            }
            var screen = new Rectangle(0, 0, 1920, 1080);
            Require(FullscreenLogic.ShouldMinimize(screen, screen, screen, false, false, false), "borderless fullscreen detected");
            Require(!FullscreenLogic.ShouldMinimize(screen, screen, screen, true, true, true), "ordinary maximised title-bar window excluded");
            Require(!FullscreenLogic.ShouldMinimize(new Rectangle(0, 0, 1920, 1040), new Rectangle(0, 0, 1920, 1040), screen, false, false, false), "work-area-only window excluded");
            Require(!FullscreenLogic.ShouldMinimize(new Rectangle(100, 100, 900, 600), Rectangle.Empty, screen, false, false, true), "small foreground window excluded");
            var left = new Rectangle(-1920, -200, 1920, 1080);
            Require(FullscreenLogic.ShouldMinimize(left, left, left, false, false, false), "negative monitor position supported");
            Require(new Options().MinimizeFullscreenOnOpen && !Options.Parse(new[] { "MinimizeFullscreenOnOpen=false" }).MinimizeFullscreenOnOpen, "fullscreen default and opt-out");
            log.AppendLine("PASS: " + layouts + " font-aware/balanced menu layouts, " + searchLayouts + " search bounds and " + settingRows + " aligned setting rows.");
            log.AppendLine("PASS: lower-row extras, nearest-column keyboard movement, settings migration, independent fonts and fullscreen decision rules.");
            log.AppendLine("Layout/text/fullscreen helper assertions: " + checks + ". No real app was minimised or launched.");
        }
    }
}
