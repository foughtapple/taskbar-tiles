// Pure helper tests. --self-test does not move, close or launch any windows,
// change settings, edit FancyZones files, or require a particular monitor setup.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarTiles
{
    static class FeatureTests
    {
        static int checks;
        static void Require(bool condition, string name)
        { checks++; if (!condition) throw new InvalidOperationException("FAILED: " + name); }
        static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, name);
        }
        static MonitorData Monitor(int number, Rectangle bounds)
        { return new MonitorData { Number = number, Key = "test-monitor-" + number, DeviceName = @"\\.\DISPLAY" + number, Model = "MODEL" + number, Instance = "INSTANCE" + number, Bounds = bounds, WorkArea = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height - 40) }; }
        static LayoutDefinition Preset(string type, int count, int spacing)
        { return new LayoutDefinition { Type = type, Count = count, ShowSpacing = spacing != 0, Spacing = spacing }; }
        static string AppliedJson(Guid desktop, string type, int count)
        {
            return "{\"device\":{\"monitor\":\"MODEL1\",\"monitor-instance\":\"INSTANCE1\",\"monitor-number\":1,\"virtual-desktop\":\"" + desktop + "\"},\"applied-layout\":{\"uuid\":\"{11111111-1111-1111-1111-111111111111}\",\"type\":\"" + type + "\",\"zone-count\":" + count + ",\"show-spacing\":true,\"spacing\":16}}";
        }
        static Point Centre(Rectangle r) { return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2); }
        static void CheckMap(List<MapCard> cards, List<MonitorData> monitors, Options o, Size viewport)
        {
            Require(cards.Count == monitors.Count, "all monitors appear in picker");
            var visible = new Rectangle(Point.Empty, viewport);
            var total = cards.Select(c => c.Card).Aggregate(Rectangle.Union);
            Require(Math.Abs(total.Left + total.Width / 2.0 - viewport.Width / 2.0) <= .5, "whole map horizontally centred");
            Require(Math.Abs(total.Top + total.Height / 2.0 - viewport.Height / 2.0) <= .5, "whole map vertically centred");
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                Require(visible.Contains(c.Card), "ENTIRE monitor card fits viewport without scrolling");
                Require(c.Card.Contains(c.Screen) && c.Card.Contains(c.FullButton) && c.Card.Contains(c.Caption), "map controls stay in card");
                Require(c.Screen.Width > 0 && c.Screen.Height > 0 && c.FullButton.Width > 0 && c.FullButton.Height > 0, "positive fitted controls");
                Require(c.FullButton.Bottom < c.Screen.Top, "full-screen button stays above monitor");
                Require(Math.Abs(c.FullButton.Height - o.FullScreenButtonHeight * c.DrawScale) <= 2, "button preference honoured at fitted scale");
                double ratio = (double)c.Monitor.Bounds.Width / c.Monitor.Bounds.Height;
                Require(Math.Abs(c.Screen.Width - c.Screen.Height * ratio) <= 2 * (ratio + 1), "monitor aspect ratio retained within rounding");
                var full = MapLayout.Hit(cards, Centre(c.FullButton));
                Require(full != null && full.Maximise && full.Monitor.Key == c.Monitor.Key && full.Bounds == c.Monitor.WorkArea, "full-screen button selects original desktop monitor");
                Require(c.ZoneButtons.Count == (o.ShowZoneButtons ? c.Monitor.Zones.Count : 0), "all alternate zone targets retained");
                for (int z = 0; z < c.ZoneButtons.Count; z++)
                {
                    var b = c.ZoneButtons[z];
                    Require(c.Card.Contains(b.Item1) && b.Item1.Width > 0 && b.Item1.Height > 0, "numbered target wholly visible");
                    Require(!b.Item1.IntersectsWith(c.Screen) && !b.Item1.IntersectsWith(c.Caption), "zone shortcut does not overlap map or source label");
                    var chosen = MapLayout.Hit(cards, Centre(b.Item1));
                    Require(chosen != null && !chosen.Maximise && chosen.Monitor.Key == c.Monitor.Key && chosen.Number == b.Item2.Number, "zone shortcut selects displayed target");
                    Require(chosen.Bounds == c.Monitor.Zones[z].Bounds, "fit does not scale actual window destination");
                    for (int j = z + 1; j < c.ZoneButtons.Count; j++) Require(!b.Item1.IntersectsWith(c.ZoneButtons[j].Item1), "numbered shortcuts do not overlap");
                }
                for (int j = i + 1; j < cards.Count; j++) Require(!c.Card.IntersectsWith(cards[j].Card), "monitor cards do not overlap");
            }
        }
        public static void Run(StringBuilder log)
        {
            checks = 0;
            var o = new Options(); Require(o.TileSize == 120 && o.PreviewScale == 120, "larger independent defaults");
            o = Options.Parse(new[] { "TileSize=9999", "PreviewScale=-100", "RightClickZones=false", "EnableSearch=TRUE", "UnknownFutureOption=keep", "FullScreenButtonHeight=65", "#TileSize=88" });
            Require(o.TileSize == 256 && o.PreviewScale == 70 && !o.RightClickZones && o.EnableSearch && o.FullScreenButtonHeight == 65, "options parsing, comments, bounds and switches");
            Require(Options.Parse(new[] { "TileSize=120" }).TileSize == 120, "preserve preferred app tile size");
            string hardware = "MODEL#5&instance|with;separators";
            o.SetOverride(hardware, "custom:some-guid"); Require(o.OverrideFor(hardware) == "custom:some-guid", "monitor override escaping");
            o.SetOverride(hardware, "none"); Require(o.OverrideFor(hardware) == "none", "monitor override replace");
            o.SetOverride(hardware, "auto"); Require(o.OverrideFor(hardware) == "auto", "monitor override reset");
            log.AppendLine("PASS: settings defaults, validation and hardware-key override round trips.");

            var work = new Rectangle(-1920, 50, 1920, 1040);
            var columns = ZoneGeometry.Build(Preset("columns", 3, 16), work, true);
            Require(columns.Count == 3 && columns[0].Bounds.Left == work.Left + 16 && columns[2].Bounds.Right == work.Right - 16, "column outer gaps and negative monitor origin");
            Require(columns[1].Bounds.Left - columns[0].Bounds.Right == 16 && columns[2].Bounds.Left - columns[1].Bounds.Right == 16, "column inner gaps");
            var quarters = ZoneGeometry.Build(Preset("grid", 4, 16), work, true);
            Require(quarters.Count == 4 && quarters[0].Bounds.Right == work.Left + 960 - 8 && quarters[1].Bounds.Left == work.Left + 960 + 8, "grid half-spacing at interior boundary");
            var merged = ZoneGeometry.Grid(work, new[] { 5000, 5000 }, new[] { 5000, 5000 }, new[] { new[] { 0, 1 }, new[] { 0, 2 } }, 16);
            Require(merged.Count == 3 && merged[0].Bounds.Height == work.Height - 32, "merged rectangular grid zone");
            Reject(delegate { ZoneGeometry.Grid(work, new[] { 5000, 5000 }, new[] { 5000, 5000 }, new[] { new[] { 0, 0 }, new[] { 0, 1 } }, 16); }, "reject non-rectangular merged zone");
            Reject(delegate { ZoneGeometry.Grid(work, new[] { 5000 }, new[] { 10000 }, new[] { new[] { 0 } }, 0); }, "reject malformed percentages");
            string custom = "{\"uuid\":\"{22222222-2222-2222-2222-222222222222}\",\"name\":\"Canvas test\",\"type\":\"canvas\",\"info\":{\"ref-width\":1000,\"ref-height\":500,\"zones\":[{\"X\":100,\"Y\":50,\"width\":400,\"height\":250},{\"X\":200,\"Y\":75,\"width\":400,\"height\":250}]}}";
            var canvas = ZoneGeometry.Build(new LayoutDefinition { Type = "custom", Custom = JsonData.Parse(custom) }, new Rectangle(-2000, -100, 2000, 1000), true);
            Require(canvas.Count == 2 && canvas[0].Bounds == new Rectangle(-1800, 0, 800, 500), "scaled canvas coordinates and negative monitor positions");
            Require(canvas[0].Bounds.IntersectsWith(canvas[1].Bounds), "overlapping canvas zones retained");
            string grid = "{\"type\":\"grid\",\"info\":{\"rows\":2,\"columns\":2,\"rows-percentage\":[5000,5000],\"columns-percentage\":[2500,7500],\"cell-child-map\":[[0,1],[0,2]]}}";
            var customGrid = ZoneGeometry.Build(new LayoutDefinition { Type = "custom", Custom = JsonData.Parse(grid), ShowSpacing = true, Spacing = 8 }, work, true);
            Require(customGrid.Count == 3 && customGrid[0].Bounds.Right == work.Left + 480 - 4, "custom grid JSON parsing");
            int presets = 0;
            foreach (string type in new[] { "columns", "rows", "grid", "priority-grid" })
                for (int n = 1; n <= 32; n++)
                {
                    foreach (int gap in new[] { 0, 8 })
                    {
                        var zones = ZoneGeometry.Build(Preset(type, n, gap), new Rectangle(2560, -1440, 3840, 2120), true);
                        Require(zones.Count == n && zones.All(z => z.Bounds.Width > 0 && z.Bounds.Height > 0), "preset count and positive sizes: " + type + "/" + n);
                        Require(zones.Select(z => z.Number).SequenceEqual(Enumerable.Range(1, n)), "preset numbered zones");
                        for (int i = 0; i < zones.Count; i++) for (int j = i + 1; j < zones.Count; j++) Require(!zones[i].Bounds.IntersectsWith(zones[j].Bounds), "non-overlapping preset grid/rows/columns");
                        presets++;
                    }
                }
            Require(ZoneGeometry.Build(Preset("blank", 0, 0), work, true).Count == 0, "blank layout");
            Require(ZoneGeometry.Build(Preset("focus", 3, 0), work, true).Count == 3, "focus layout");
            var noGaps = ZoneGeometry.Build(Preset("columns", 3, 25), work, false);
            Require(noGaps[0].Bounds.Left == work.Left && noGaps.Last().Bounds.Right == work.Right, "spacing toggle");
            log.AppendLine("PASS: custom grid/canvas, merged cells, gaps, blank/focus and " + presets + " preset geometry cases.");

            Guid vd1 = new Guid("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"), vd2 = new Guid("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
            var catalog = new ZoneCatalog(); var monitor = Monitor(1, new Rectangle(0, 0, 1920, 1080)); catalog.Monitors.Add(monitor);
            catalog.Parse(JsonData.Parse("{\"applied-layouts\":[" + AppliedJson(vd1, "columns", 3) + "," + AppliedJson(vd2, "rows", 2) + "]}"), JsonData.Parse("{\"custom-layouts\":[" + custom + "]}"));
            string reason;
            Require(catalog.Automatic(monitor, vd1, out reason).Type == "columns", "active virtual desktop choice");
            Require(catalog.Automatic(monitor, vd2, out reason).Type == "rows", "other virtual desktop choice");
            Require(catalog.Automatic(monitor, Guid.Empty, out reason) == null, "ambiguous virtual desktop does not guess");
            Require(catalog.Automatic(monitor, Guid.NewGuid(), out reason) == null, "no cross-desktop fallback");
            Require(catalog.Choices.Keys.Any(k => k.StartsWith("custom:")), "manual custom layout choices");
            var other = Monitor(2, new Rectangle(-1920, 0, 1920, 1080));
            Require(catalog.Automatic(other, vd1, out reason) == null, "unmatched monitor does not guess");
            log.AppendLine("PASS: saved layout parsing, hardware identity, virtual-desktop selection and ambiguity safeguards.");

            o = new Options(); int arrangements = 0;
            var arrangementsToTest = new List<List<MonitorData>> {
                // User-like arrangement: ultrawide at lower-right, screen above,
                // and a landscape monitor immediately to its left.
                new List<MonitorData> { Monitor(1, new Rectangle(0, 0, 5120, 1440)), Monitor(2, new Rectangle(1600, -1080, 1920, 1080)), Monitor(3, new Rectangle(-2560, 0, 2560, 1440)) },
                new List<MonitorData> { Monitor(1, new Rectangle(0, 0, 5120, 1440)), Monitor(2, new Rectangle(-1080, -100, 1080, 1920)), Monitor(3, new Rectangle(640, -2160, 3840, 2160)) },
                new List<MonitorData> { Monitor(1, new Rectangle(-5120, -2160, 5120, 1440)) },
                new List<MonitorData> { Monitor(1, new Rectangle(0, 0, 1920, 1080)), Monitor(2, new Rectangle(0, -1080, 1920, 1080)) },
                Enumerable.Range(0, 6).Select(i => Monitor(i + 1, new Rectangle(i % 3 * 1920, i / 3 * 1080, 1920, 1080))).ToList()
            };
            foreach (var monitors in arrangementsToTest)
                foreach (float dpi in new[] { 1f, 1.25f, 1.5f, 2f, 3f })
                    foreach (var viewport in new[] { new Size(500, 260), new Size(1014, 454), new Size(1034, 512), new Size(1700, 900) })
                        foreach (int buttonHeight in new[] { 36, 52, 100 })
                            foreach (bool shortcuts in new[] { true, false })
                            {
                                o.FullScreenButtonHeight = buttonHeight; o.FullScreenButtonMinWidth = buttonHeight == 100 ? 400 : 190;
                                o.ShowZoneButtons = shortcuts;
                                foreach (var m in monitors) m.Zones = ZoneGeometry.Build(Preset("columns", 3, 8), m.WorkArea, true);
                                var cards = MapLayout.Build(monitors, o, viewport, dpi);
                                CheckMap(cards, monitors, o, viewport);
                                arrangements++;
                            }
            // Large overlapping/canvas-like collections keep EVERY shortcut and
            // its desktop destination even when the complete map must shrink.
            var crowded = new List<MonitorData> { Monitor(1, new Rectangle(-3840, 0, 3840, 2160)), Monitor(2, new Rectangle(0, 0, 1080, 1920)) };
            foreach (var m in crowded) m.Zones = ZoneGeometry.Build(Preset("focus", 32, 0), m.WorkArea, false);
            o.ShowZoneButtons = true; o.FullScreenButtonHeight = 100; o.FullScreenButtonMinWidth = 400;
            foreach (var size in new[] { new Size(500, 260), new Size(1014, 454), new Size(1600, 900) })
            { CheckMap(MapLayout.Build(crowded, o, size, 2), crowded, o, size); arrangements++; }
            Require(MapLayout.Build(new List<MonitorData>(), o, new Size(1000, 500), 1).Count == 0, "empty monitor list");
            Rectangle display = new Rectangle(-1920, -1080, 1920, 1080), mapBox = new Rectangle(40, 100, 480, 270);
            Require(MapLayout.Project(display, display, mapBox) == mapBox, "global-to-picker coordinate transformation");
            Require(MapLayout.Project(new Rectangle(-1920, -1080, 960, 1080), display, mapBox) == new Rectangle(40, 100, 240, 270), "half-width zone projection");
            Require(MapLayout.Project(display, Rectangle.Empty, mapBox) == Rectangle.Empty, "invalid display cannot divide by zero");
            monitor.Status = "Basic zones - The saved FancyZones layout has no zones.";
            Require(MapLayout.SourceLabel(monitor) == "Basic zones", "short fallback label remains truthful");
            Require(monitor.Status.Contains("no zones"), "full fallback reason retained for tooltip");
            log.AppendLine("PASS: " + arrangements + " complete fit-all map cases, bounds, centring, hit targets, labels and desktop-coordinate preservation.");

            var app = new AppButton { Id = "Appid:Chrome.Profile-A", DisplayName = "Chrome", LaunchExe = @"C:\Apps\chrome.exe" };
            Require(WindowInventory.Matches(app, new WindowRecord { AppId = "Chrome.Profile-A", Exe = app.LaunchExe }), "exact application identity");
            Require(!WindowInventory.Matches(app, new WindowRecord { AppId = "Chrome.Profile-B", Exe = app.LaunchExe }), "different browser profiles not conflated");
            Require(!WindowInventory.Matches(app, new WindowRecord { AppId = "Other.App", Title = "Chrome", Exe = @"C:\Apps\other.exe" }), "window title alone does not trigger placement");
            Require(Marshal.SizeOf(typeof(WindowNative.WINDOWPLACEMENT)) == 44, "WINDOWPLACEMENT structure packing");
            Require(Marshal.SizeOf(typeof(DisplayNative.DISPLAY_DEVICE)) == 840, "DISPLAY_DEVICEW structure packing");
            log.AppendLine("PASS: identity matching and additional native structure sizes.");
            log.AppendLine("Feature checks passed: " + checks + ". No live window movement was tested by these helper tests.");
        }
    }
}
