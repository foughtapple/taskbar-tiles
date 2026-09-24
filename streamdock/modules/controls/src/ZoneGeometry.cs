// Stream Dock Utilities 05-07. C# 5 / .NET Framework 4.x.
// Geometry follows the public PowerToys layout formats; see SOURCES.md and NOTICE.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;

namespace DockUtilities
{
    internal static class Json
    {
        internal static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024, RecursionLimit = 100 };
        }
        internal static Dictionary<string, object> Obj(object value)
        {
            var d = value as Dictionary<string, object>;
            if (d == null) throw new InvalidOperationException("An expected JSON object is missing.");
            return d;
        }
        internal static object Get(Dictionary<string, object> d, string key, object fallback = null)
        { object value; return d.TryGetValue(key, out value) ? value : fallback; }
        internal static string Str(Dictionary<string, object> d, string key, string fallback = "")
        { return Convert.ToString(Get(d, key, fallback), System.Globalization.CultureInfo.InvariantCulture); }
        internal static int Int(Dictionary<string, object> d, string key, int fallback = 0)
        { return Convert.ToInt32(Get(d, key, fallback), System.Globalization.CultureInfo.InvariantCulture); }
        internal static bool Bool(Dictionary<string, object> d, string key, bool fallback = false)
        { return Convert.ToBoolean(Get(d, key, fallback), System.Globalization.CultureInfo.InvariantCulture); }
        internal static object[] Arr(object o)
        {
            var a = o as IEnumerable;
            if (a == null || o is string || o is IDictionary) throw new InvalidOperationException("An expected JSON array is missing.");
            return a.Cast<object>().ToArray();
        }
        internal static int[] Ints(object o) { return Arr(o).Select(x => Convert.ToInt32(x)).ToArray(); }
        internal static Dictionary<string, object> Parse(string text) { return Obj(Serializer().DeserializeObject(text)); }
        internal static string GuidKey(string value)
        { Guid g; return Guid.TryParse(value, out g) ? g.ToString("D") : (value ?? "").Trim().ToLowerInvariant(); }
    }

    internal sealed class Zone
    {
        internal int Id;
        internal Rectangle Bounds;
        internal Zone(int id, Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException("A zone has zero or negative size. Check the FancyZones layout.");
            Id = id; Bounds = bounds;
        }
    }

    internal static class Geometry
    {
        internal static List<Zone> Build(Dictionary<string, object> layout, Dictionary<string, object> custom, Rectangle work)
        {
            if (work.Width <= 0 || work.Height <= 0) throw new InvalidOperationException("The monitor has no usable work area.");
            string type = Json.Str(layout, "type").ToLowerInvariant();
            int count = Json.Int(layout, "zone-count");
            int spacing = Json.Bool(layout, "show-spacing") ? Json.Int(layout, "spacing") : 0;
            if (spacing < 0 || spacing > 10000) throw new InvalidOperationException("Invalid FancyZones spacing.");
            var result = new List<Zone>();
            if (type == "custom")
            {
                if (custom == null) throw new InvalidOperationException("The active custom layout is not in custom-layouts.json. Reapply it in FancyZones.");
                var info = Json.Obj(Json.Get(custom, "info"));
                string kind = Json.Str(custom, "type").ToLowerInvariant();
                if (kind == "canvas")
                {
                    int rw = Json.Int(info, "ref-width"), rh = Json.Int(info, "ref-height");
                    if (rw <= 0 || rh <= 0) throw new InvalidOperationException("The Canvas layout has invalid reference dimensions.");
                    var items = Json.Arr(Json.Get(info, "zones"));
                    if (items.Length == 0 || items.Length > 128) throw new InvalidOperationException("Invalid Canvas zone count.");
                    foreach (var item in items)
                    {
                        var d = Json.Obj(item);
                        // Inverse-DPI and forward-DPI factors cancel. Scale reference coordinates
                        // into the physical work area, including non-100% display scaling.
                        double x = (double)Json.Int(d, "X") * work.Width / rw;
                        double y = (double)Json.Int(d, "Y") * work.Height / rh;
                        double w = (double)Json.Int(d, "width") * work.Width / rw;
                        double h = (double)Json.Int(d, "height") * work.Height / rh;
                        result.Add(new Zone(result.Count, Rectangle.FromLTRB(work.Left + (int)x, work.Top + (int)y,
                            work.Left + (int)(x + w), work.Top + (int)(y + h))));
                    }
                }
                else if (kind == "grid")
                {
                    int[] rp = Json.Ints(Json.Get(info, "rows-percentage"));
                    int[] cp = Json.Ints(Json.Get(info, "columns-percentage"));
                    int[][] map = Json.Arr(Json.Get(info, "cell-child-map")).Select(Json.Ints).ToArray();
                    if (Json.Int(info, "rows") != rp.Length || Json.Int(info, "columns") != cp.Length)
                        throw new InvalidOperationException("The custom Grid dimensions do not match its percentage arrays.");
                    result = Grid(work, rp, cp, map, spacing);
                }
                else throw new InvalidOperationException("Unsupported custom layout type: " + kind);
            }
            else
            {
                if (count < 1 || count > 128) throw new InvalidOperationException("FancyZones has no active zones, or its zone count is unsupported.");
                if (type == "columns" || type == "rows")
                {
                    bool columns = type == "columns";
                    int available = (columns ? work.Width : work.Height) - spacing * (count + 1);
                    if (available <= 0) throw new InvalidOperationException("The layout spacing leaves no usable zone area.");
                    for (int i = 0; i < count; i++)
                    {
                        int start = spacing * (i + 1) + (int)((long)i * available / count);
                        int end = spacing * (i + 1) + (int)((long)(i + 1) * available / count);
                        Rectangle r = columns ? Rectangle.FromLTRB(work.Left + start, work.Top + spacing, work.Left + end, work.Bottom - spacing)
                            : Rectangle.FromLTRB(work.Left + spacing, work.Top + start, work.Right - spacing, work.Top + end);
                        result.Add(new Zone(i, r));
                    }
                }
                else if (type == "focus")
                {
                    for (int i = 0; i < count; i++)
                        result.Add(new Zone(i, new Rectangle(work.Left + 100 + i * 50, work.Top + 100 + i * 50,
                            (int)(work.Width * 0.4), (int)(work.Height * 0.4))));
                }
                else if (type == "grid" || type == "priority-grid")
                {
                    int[] rp, cp; int[][] map;
                    // PowerToys currently uses its predefined priority table for counts 1..10;
                    // its source condition uses '< 11', so 11+ falls back to the regular grid.
                    if (type == "priority-grid" && count <= 10)
                        Priority(count, out rp, out cp, out map);
                    else
                    {
                        int rows = (int)Math.Floor(Math.Sqrt(count));
                        int cols = (count + rows - 1) / rows;
                        rp = Percents(rows); cp = Percents(cols); map = new int[rows][];
                        for (int r = 0; r < rows; r++)
                        {
                            map[r] = new int[cols];
                            for (int c = 0; c < cols; c++) map[r][c] = Math.Min(r * cols + c, count - 1);
                        }
                    }
                    result = Grid(work, rp, cp, map, spacing);
                }
                else throw new InvalidOperationException("Unsupported FancyZones layout type: " + type);
            }
            // Custom layouts use their own cell map / rectangles, not the template zone count.
            if (type != "custom" && count > 0 && result.Count != count) throw new InvalidOperationException("The active layout and custom layout disagree on the zone count. Reapply the layout in FancyZones.");
            foreach (Zone z in result)
                if (!work.Contains(z.Bounds)) throw new InvalidOperationException("A zone extends outside the monitor work area. The helper will not silently crop or capture a larger region.");
            return result;
        }

        internal static int[] Percents(int n)
        { return Enumerable.Range(0, n).Select(i => 10000 * (i + 1) / n - 10000 * i / n).ToArray(); }

        internal static List<Zone> Grid(Rectangle work, int[] rp, int[] cp, int[][] map, int spacing)
        {
            if (rp.Length == 0 || cp.Length == 0 || rp.Length > 128 || cp.Length > 128 ||
                rp.Sum() != 10000 || cp.Sum() != 10000 || rp.Any(p => p <= 0) || cp.Any(p => p <= 0) ||
                map.Length != rp.Length || map.Any(r => r.Length != cp.Length))
                throw new InvalidOperationException("Invalid Grid layout percentages or cell map.");
            int[] xs = Boundaries(cp, work.Width), ys = Boundaries(rp, work.Height);
            var ids = map.SelectMany(x => x).Distinct().OrderBy(x => x).ToArray();
            if (ids.Length > 128 || ids.Any(id => id < 0 || id > 127)) throw new InvalidOperationException("Invalid Grid zone IDs.");
            var result = new List<Zone>();
            foreach (int id in ids)
            {
                int minR = rp.Length, maxR = -1, minC = cp.Length, maxC = -1;
                for (int r = 0; r < rp.Length; r++) for (int c = 0; c < cp.Length; c++)
                    if (map[r][c] == id) { minR = Math.Min(minR, r); maxR = Math.Max(maxR, r); minC = Math.Min(minC, c); maxC = Math.Max(maxC, c); }
                for (int r = minR; r <= maxR; r++) for (int c = minC; c <= maxC; c++)
                    if (map[r][c] != id) throw new InvalidOperationException("A Grid zone is not rectangular. No screenshot was taken.");
                int left = xs[minC] + (minC == 0 ? spacing : spacing / 2);
                int right = xs[maxC + 1] - (maxC == cp.Length - 1 ? spacing : spacing / 2);
                int top = ys[minR] + (minR == 0 ? spacing : spacing / 2);
                int bottom = ys[maxR + 1] - (maxR == rp.Length - 1 ? spacing : spacing / 2);
                result.Add(new Zone(id, Rectangle.FromLTRB(work.Left + left, work.Top + top, work.Left + right, work.Top + bottom)));
            }
            return result;
        }
        private static int[] Boundaries(int[] p, int extent)
        {
            int[] b = new int[p.Length + 1]; int sum = 0;
            for (int i = 0; i < p.Length; i++) { sum += p[i]; b[i + 1] = (int)((long)sum * extent / 10000); }
            return b;
        }
        private static void Priority(int n, out int[] rp, out int[] cp, out int[][] map)
        {
            if (n == 1) { rp = new[] { 10000 }; cp = new[] { 10000 }; map = new[] { new[] { 0 } }; return; }
            if (n == 2) { rp = new[] { 10000 }; cp = new[] { 6667, 3333 }; map = new[] { new[] { 0, 1 } }; return; }
            cp = n <= 7 ? new[] { 2500, 5000, 2500 } : new[] { 2500, 2500, 2500, 2500 };
            rp = n == 3 ? new[] { 10000 } : (n <= 5 ? new[] { 5000, 5000 } : new[] { 3333, 3334, 3333 });
            switch (n)
            {
                case 3: map = new[] { new[] { 0, 1, 2 } }; break;
                case 4: map = new[] { new[] { 0, 1, 2 }, new[] { 0, 1, 3 } }; break;
                case 5: map = new[] { new[] { 0, 1, 2 }, new[] { 3, 1, 4 } }; break;
                case 6: map = new[] { new[] { 0, 1, 2 }, new[] { 0, 1, 3 }, new[] { 4, 1, 5 } }; break;
                case 7: map = new[] { new[] { 0, 1, 2 }, new[] { 3, 1, 4 }, new[] { 5, 1, 6 } }; break;
                case 8: map = new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 1, 2, 5 }, new[] { 6, 1, 2, 7 } }; break;
                case 9: map = new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 1, 2, 5 }, new[] { 6, 1, 7, 8 } }; break;
                default: map = new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 1, 5, 6 }, new[] { 7, 1, 8, 9 } }; break;
            }
        }
        internal static Zone Select(List<Zone> zones, Point p)
        {
            // Gaps are not zones. Never fall back to a full-screen screenshot.
            var matches = zones.Where(z => z.Bounds.Contains(p)).ToList();
            if (matches.Count == 0) throw new InvalidOperationException("The pointer is outside every FancyZone (possibly in a gap or on the taskbar). Move it inside the zone and press 05 again.");
            return matches.OrderBy(z => (long)z.Bounds.Width * z.Bounds.Height)
                .ThenBy(z => Distance(z.Bounds, p)).ThenBy(z => z.Id).First();
        }
        private static double Distance(Rectangle r, Point p)
        { double dx = r.Left + r.Width / 2.0 - p.X, dy = r.Top + r.Height / 2.0 - p.Y; return dx * dx + dy * dy; }
    }
}
