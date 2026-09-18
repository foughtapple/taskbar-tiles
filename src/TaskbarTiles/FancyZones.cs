// Read-only adapter for PowerToys' saved FancyZones geometry.
// No writes to PowerToys files, window properties, registry or private IPC.
// Geometry compatibility references and attribution: SOURCES.md / THIRD-PARTY-NOTICES.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class ZoneRect
    {
        public int Number;
        public Rectangle Bounds;
    }
    sealed class MonitorData
    {
        public string DeviceName, Model = "", Instance = "", Status = "", Key;
        public int Number;
        public bool Primary;
        public Rectangle Bounds, WorkArea;
        public List<ZoneRect> Zones = new List<ZoneRect>();
        public string Label { get { return "Monitor " + Number; } }
    }
    sealed class ZoneDestination
    {
        public MonitorData Monitor;
        public Rectangle Bounds;
        public bool Maximise;
        public int Number;
        public override string ToString() { return Maximise ? "Full screen - " + Monitor.Label : Monitor.Label + " / Zone " + Number; }
    }
    static class JsonData
    {
        public static Dictionary<string, object> Object(object o) { return o as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(Dictionary<string, object> o, string key) { object v; return o.TryGetValue(key, out v) ? v : null; }
        public static string Text(Dictionary<string, object> o, string key, string fallback = "") { object v = Get(o, key); return v == null ? fallback : Convert.ToString(v); }
        public static int Int(Dictionary<string, object> o, string key, int fallback = 0) { int n; return int.TryParse(Text(o, key), out n) ? n : fallback; }
        public static bool Bool(Dictionary<string, object> o, string key, bool fallback = false) { bool b; return bool.TryParse(Text(o, key), out b) ? b : fallback; }
        public static object[] Array(object o) { var a = o as IEnumerable; return a == null || o is string || o is IDictionary ? new object[0] : a.Cast<object>().ToArray(); }
        public static Dictionary<string, object> Read(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, object>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("Layout file is unexpectedly large: " + Path.GetFileName(path));
                using (var reader = new StreamReader(stream)) return Parse(reader.ReadToEnd());
            }
        }
        public static Dictionary<string, object> Parse(string json)
        { return Object(new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024, RecursionLimit = 100 }.DeserializeObject(json)); }
    }
    sealed class LayoutDefinition
    {
        public string Key, Name, Type, Id;
        public int Count, Spacing;
        public bool ShowSpacing;
        public Dictionary<string, object> Custom;
    }
    sealed class AppliedLayout
    {
        public string Model, Instance, Legacy;
        public int Number;
        public Guid Desktop;
        public LayoutDefinition Layout;
    }
    sealed class ZoneCatalog
    {
        public List<MonitorData> Monitors = new List<MonitorData>();
        public Dictionary<string, string> Choices = new Dictionary<string, string>();
        public string Error = "", Folder;
        readonly Dictionary<string, LayoutDefinition> definitions = new Dictionary<string, LayoutDefinition>();
        readonly Dictionary<string, Dictionary<string, object>> customs = new Dictionary<string, Dictionary<string, object>>();
        readonly List<AppliedLayout> applied = new List<AppliedLayout>();
        static string Id(string text) { Guid g; return Guid.TryParse(text, out g) ? g.ToString("D") : (text ?? "").ToLowerInvariant(); }
        internal static string DefaultFolder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\PowerToys\FancyZones"); } }
        public static ZoneCatalog Load(Options options, Guid desktop)
        {
            var result = new ZoneCatalog { Monitors = DisplayNative.Monitors(), Folder = string.IsNullOrWhiteSpace(options.FancyZonesFolder) ? DefaultFolder : Environment.ExpandEnvironmentVariables(options.FancyZonesFolder) };
            if (options.ReadFancyZones)
            {
                try
                {
                    result.Parse(JsonData.Read(Path.Combine(result.Folder, "applied-layouts.json")), JsonData.Read(Path.Combine(result.Folder, "custom-layouts.json")));
                    var settings = JsonData.Read(Path.Combine(result.Folder, "settings.json"));
                    var props = JsonData.Object(JsonData.Get(settings, "properties"));
                    var span = JsonData.Object(JsonData.Get(props, "fancyzones_spanZonesAcrossMonitors"));
                    if (JsonData.Bool(span, "value")) result.Error = "PowerToys is spanning zones across monitors. Automatic per-monitor import is unavailable; select a manual layout in Settings.";
                }
                catch (Exception ex) { result.Error = "Could not read FancyZones: " + ex.Message; Program.Log(result.Error); }
            }
            foreach (var monitor in result.Monitors) result.Resolve(monitor, options, desktop);
            return result;
        }
        internal void Parse(Dictionary<string, object> appliedRoot, Dictionary<string, object> customRoot)
        {
            foreach (var obj in JsonData.Array(JsonData.Get(customRoot, "custom-layouts")))
            {
                var c = JsonData.Object(obj); string id = Id(JsonData.Text(c, "uuid"));
                if (string.IsNullOrEmpty(id)) continue;
                customs[id] = c;
                var info = JsonData.Object(JsonData.Get(c, "info"));
                var def = new LayoutDefinition { Key = "custom:" + id, Id = id, Name = JsonData.Text(c, "name", "Custom layout"), Type = "custom", Custom = c,
                    ShowSpacing = JsonData.Bool(info, "show-spacing", true), Spacing = JsonData.Int(info, "spacing", 16), Count = 0 };
                definitions[def.Key] = def; Choices[def.Key] = def.Name + " (custom)";
            }
            foreach (var obj in JsonData.Array(JsonData.Get(appliedRoot, "applied-layouts")))
            {
                var a = JsonData.Object(obj); var d = JsonData.Object(JsonData.Get(a, "device"));
                var l = JsonData.Object(JsonData.Get(a, "applied-layout"));
                if (l.Count == 0) continue;
                var def = new LayoutDefinition { Id = Id(JsonData.Text(l, "uuid")), Type = JsonData.Text(l, "type"), Count = JsonData.Int(l, "zone-count", 3),
                    Spacing = JsonData.Int(l, "spacing", 16), ShowSpacing = JsonData.Bool(l, "show-spacing", true) };
                if (def.Type == "custom") customs.TryGetValue(def.Id, out def.Custom);
                def.Name = def.Custom != null ? JsonData.Text(def.Custom, "name", "Custom layout") : def.Type.Replace('-', ' ');
                def.Key = "applied:" + def.Id + ":" + def.Type + ":" + def.Count + ":" + def.Spacing + ":" + def.ShowSpacing;
                definitions[def.Key] = def; Choices[def.Key] = def.Name + " / " + def.Count + " zones (saved)";
                Guid vd; Guid.TryParse(JsonData.Text(d, "virtual-desktop"), out vd);
                string model = JsonData.Text(d, "monitor"), instance = JsonData.Text(d, "monitor-instance");
                if (string.IsNullOrEmpty(instance) && model.Contains("#")) { var p = model.Split('#'); model = p[0]; instance = p.Length > 1 ? p[1] : ""; }
                applied.Add(new AppliedLayout { Model = model, Instance = instance, Number = JsonData.Int(d, "monitor-number"),
                    Desktop = vd, Legacy = JsonData.Text(a, "device-id"), Layout = def });
            }
        }
        internal LayoutDefinition Automatic(MonitorData m, Guid desktop, out string reason)
        {
            reason = "No saved FancyZones layout matched this monitor.";
            var exact = applied.Where(a => Equal(a.Model, m.Model) && !string.IsNullOrEmpty(m.Instance) && Equal(a.Instance, m.Instance)).ToList();
            if (exact.Count == 0)
            {
                // Only fall back to the display number when no conflicting hardware identity is present.
                exact = applied.Where(a => a.Number == m.Number && a.Number > 0 && (string.IsNullOrEmpty(a.Model) || Equal(a.Model, m.Model) || Equal(a.Model, m.DeviceName)) && (string.IsNullOrEmpty(a.Instance) || string.IsNullOrEmpty(m.Instance) || Equal(a.Instance, m.Instance))).ToList();
            }
            if (exact.Count == 0 && !string.IsNullOrEmpty(m.Model) && Monitors.Count(x => Equal(x.Model, m.Model)) == 1)
                exact = applied.Where(a => Equal(a.Model, m.Model) && string.IsNullOrEmpty(a.Instance)).ToList();
            if (exact.Count == 0)
                exact = applied.Where(a => !string.IsNullOrEmpty(a.Legacy) && !string.IsNullOrEmpty(m.Instance) && a.Legacy.IndexOf(m.Model + "#" + m.Instance, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (exact.Count == 0) return null;
            if (desktop != Guid.Empty)
            {
                var current = exact.Where(a => a.Desktop == desktop).ToList();
                if (current.Count > 0) exact = current;
                else
                {
                    var generic = exact.Where(a => a.Desktop == Guid.Empty).ToList();
                    if (generic.Count > 0) exact = generic;
                    else { reason = "No saved layout for the current virtual desktop. Choose a manual layout in Settings."; return null; }
                }
            }
            var choices = exact.Select(a => a.Layout.Key).Distinct().ToList();
            if (choices.Count != 1) { reason = "Multiple desktop layouts match. Choose one in Settings > Monitor layouts."; return null; }
            reason = ""; return exact[0].Layout;
        }
        static bool Equal(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        void Resolve(MonitorData monitor, Options options, Guid desktop)
        {
            string preference = options.OverrideFor(monitor.Key), reason = Error;
            if (preference == "none") { monitor.Status = "Full screen only (manual)"; return; }
            LayoutDefinition def = null;
            if (preference != "auto" && preference != "basic")
            {
                definitions.TryGetValue(preference, out def);
                if (def == null) reason = "The manually selected layout no longer exists.";
            }
            else if (preference == "auto" && options.ReadFancyZones && string.IsNullOrEmpty(Error)) def = Automatic(monitor, desktop, out reason);
            else if (!options.ReadFancyZones) reason = "FancyZones import is off.";
            if (def != null)
            {
                try
                {
                    monitor.Zones = ZoneGeometry.Build(def, monitor.WorkArea, options.RespectZoneSpacing);
                    if (monitor.Zones.Count > 0) { monitor.Status = "FancyZones: " + def.Name + (preference == "auto" ? "" : " (manual)"); return; }
                    reason = def.Type == "blank" ? "The saved FancyZones layout has no zones." : "The saved layout contains no valid zones.";
                }
                catch (Exception ex) { reason = "Layout unavailable: " + ex.Message; }
            }
            if (preference == "basic" || options.ShowBasicFallback)
            {
                var basic = new LayoutDefinition { Type = options.BasicLayout == 2 ? "grid" : "columns", Count = options.BasicLayout == 0 ? 2 : options.BasicLayout == 1 ? 3 : 4, Spacing = 8, ShowSpacing = true };
                monitor.Zones = ZoneGeometry.Build(basic, monitor.WorkArea, true);
                monitor.Status = "Basic zones" + (preference == "basic" ? " (manual)" : " - " + (string.IsNullOrEmpty(reason) ? "FancyZones not available" : reason));
            }
            else monitor.Status = string.IsNullOrEmpty(reason) ? "No FancyZones layout. Full screen is available." : reason;
        }
        public static string WriteDiagnostics(Options options)
        {
            var c = Load(options, DisplayNative.CurrentDesktop(Native.GetForegroundWindow()));
            var b = new StringBuilder("Taskbar Tiles " + Program.Version + " zone diagnostics - local only\r\n");
            b.AppendLine("Folder: " + c.Folder); b.AppendLine("Error: " + c.Error);
            foreach (var m in c.Monitors)
            {
                b.AppendLine(m.Label + " | " + m.DeviceName + " | " + m.Model + " | " + m.Instance);
                b.AppendLine("Bounds: " + m.Bounds + "  Work: " + m.WorkArea + "  Override: " + options.OverrideFor(m.Key));
                b.AppendLine(m.Status);
                foreach (var z in m.Zones) b.AppendLine("  Zone " + z.Number + " " + z.Bounds);
            }
            string path = Path.Combine(Program.Home, "zone-diagnostics.txt"); File.WriteAllText(path, b.ToString()); return path;
        }
    }
    static class ZoneGeometry
    {
        static void Add(List<ZoneRect> result, int index, int l, int t, int r, int b, Rectangle work)
        {
            if (r <= l || b <= t) throw new InvalidDataException("A zone has zero or negative size.");
            result.Add(new ZoneRect { Number = index + 1, Bounds = Rectangle.FromLTRB(work.Left + l, work.Top + t, work.Left + r, work.Top + b) });
        }
        public static List<ZoneRect> Build(LayoutDefinition d, Rectangle work, bool respectSpacing)
        {
            if (work.Width < 1 || work.Height < 1) throw new InvalidDataException("Invalid monitor work area.");
            int n = d.Count, gap = d.ShowSpacing && respectSpacing ? Math.Max(0, Math.Min(1000, d.Spacing)) : 0;
            var output = new List<ZoneRect>();
            if (d.Type == "blank") return output;
            if (d.Type == "custom")
            {
                if (d.Custom == null) throw new InvalidDataException("The custom layout definition is missing.");
                var info = JsonData.Object(JsonData.Get(d.Custom, "info"));
                if (JsonData.Text(d.Custom, "type") == "canvas")
                {
                    int rw = JsonData.Int(info, "ref-width"), rh = JsonData.Int(info, "ref-height");
                    if (rw < 1 || rh < 1) throw new InvalidDataException("Invalid canvas reference size.");
                    var zones = JsonData.Array(JsonData.Get(info, "zones"));
                    if (zones.Length > 128) throw new InvalidDataException("More than 128 zones is not supported.");
                    for (int i = 0; i < zones.Length; i++)
                    {
                        var z = JsonData.Object(zones[i]);
                        double x = JsonData.Int(z, "X") * (double)work.Width / rw, y = JsonData.Int(z, "Y") * (double)work.Height / rh;
                        double width = JsonData.Int(z, "width") * (double)work.Width / rw, height = JsonData.Int(z, "height") * (double)work.Height / rh;
                        Add(output, i, (int)x, (int)y, (int)(x + width), (int)(y + height), work);
                    }
                    return output;
                }
                if (JsonData.Text(d.Custom, "type") != "grid") throw new InvalidDataException("Unknown custom layout type.");
                int rows = JsonData.Int(info, "rows"), cols = JsonData.Int(info, "columns");
                var rp = JsonData.Array(JsonData.Get(info, "rows-percentage")).Select(x => Convert.ToInt32(x)).ToArray();
                var cp = JsonData.Array(JsonData.Get(info, "columns-percentage")).Select(x => Convert.ToInt32(x)).ToArray();
                var cells = JsonData.Array(JsonData.Get(info, "cell-child-map")).Select(row => JsonData.Array(row).Select(x => Convert.ToInt32(x)).ToArray()).ToArray();
                if (rp.Length != rows || cp.Length != cols) throw new InvalidDataException("Grid dimensions do not match its percentages.");
                return Grid(work, rp, cp, cells, gap);
            }
            if (n < 1 || n > 128) throw new InvalidDataException("The zone count must be between 1 and 128.");
            if (d.Type == "columns" || d.Type == "rows")
            {
                bool vertical = d.Type == "rows";
                int available = (vertical ? work.Height : work.Width) - gap * (n + 1), position = gap;
                for (int i = 0; i < n; i++)
                {
                    int end = position + (i + 1) * available / n - i * available / n;
                    if (vertical) Add(output, i, gap, position, work.Width - gap, end, work);
                    else Add(output, i, position, gap, end, work.Height - gap, work);
                    position = end + gap;
                }
                return output;
            }
            if (d.Type == "focus")
            {
                for (int i = 0; i < n; i++) Add(output, i, 100 + 50 * i, 100 + 50 * i, 100 + 50 * i + (int)(work.Width * .4), 100 + 50 * i + (int)(work.Height * .4), work);
                return output;
            }
            if (d.Type == "priority-grid" && n < 11)
            {
                int[] rp, cp; int[][] cells;
                Priority(n, out rp, out cp, out cells); return Grid(work, rp, cp, cells, gap);
            }
            if (d.Type != "grid" && d.Type != "priority-grid") throw new InvalidDataException("Unknown layout type: " + d.Type);
            int nr = Math.Max(1, (int)Math.Sqrt(n)), nc = (n + nr - 1) / nr, id = 0;
            var map = new int[nr][];
            for (int r = 0; r < nr; r++) { map[r] = new int[nc]; for (int c = 0; c < nc; c++) map[r][c] = Math.Min(id++, n - 1); }
            return Grid(work, Percentages(nr), Percentages(nc), map, gap);
        }
        static int[] Percentages(int n) { return Enumerable.Range(0, n).Select(i => 10000 * (i + 1) / n - 10000 * i / n).ToArray(); }
        internal static List<ZoneRect> Grid(Rectangle work, int[] rp, int[] cp, int[][] map, int gap)
        {
            int rows = rp.Length, cols = cp.Length;
            if (rows < 1 || cols < 1 || rows > 128 || cols > 128 || rp.Sum() != 10000 || cp.Sum() != 10000 || rp.Any(x => x <= 0) || cp.Any(x => x <= 0) || map.Length != rows || map.Any(row => row.Length != cols))
                throw new InvalidDataException("Invalid grid percentages or cell map.");
            int[] xs = new int[cols + 1], ys = new int[rows + 1]; int total = 0;
            for (int c = 0; c < cols; c++) { total += cp[c]; xs[c + 1] = (int)((long)total * work.Width / 10000); }
            total = 0; for (int r = 0; r < rows; r++) { total += rp[r]; ys[r + 1] = (int)((long)total * work.Height / 10000); }
            var ids = map.SelectMany(x => x).Distinct().OrderBy(x => x).ToList();
            if (ids.Count > 128 || ids.Any(x => x < 0 || x > 127)) throw new InvalidDataException("Invalid zone indices.");
            var result = new List<ZoneRect>();
            foreach (int id in ids)
            {
                int minR = rows, maxR = -1, minC = cols, maxC = -1;
                for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) if (map[r][c] == id) { minR = Math.Min(minR, r); maxR = Math.Max(maxR, r); minC = Math.Min(minC, c); maxC = Math.Max(maxC, c); }
                for (int r = minR; r <= maxR; r++) for (int c = minC; c <= maxC; c++) if (map[r][c] != id) throw new InvalidDataException("A merged zone is not rectangular.");
                Add(result, id, xs[minC] + (minC == 0 ? gap : gap / 2), ys[minR] + (minR == 0 ? gap : gap / 2),
                    xs[maxC + 1] - (maxC == cols - 1 ? gap : gap / 2), ys[maxR + 1] - (maxR == rows - 1 ? gap : gap / 2), work);
            }
            return result;
        }
        static void Priority(int n, out int[] rp, out int[] cp, out int[][] map)
        {
            if (n <= 3)
            {
                rp = new[] { 10000 }; cp = n == 1 ? new[] { 10000 } : n == 2 ? new[] { 6667, 3333 } : new[] { 2500, 5000, 2500 };
                map = new[] { Enumerable.Range(0, n).ToArray() }; return;
            }
            rp = n <= 5 ? new[] { 5000, 5000 } : new[] { 3333, 3334, 3333 };
            cp = n <= 7 ? new[] { 2500, 5000, 2500 } : new[] { 2500, 2500, 2500, 2500 };
            switch (n)
            {
                case 4: map = new[] { new[] { 0, 1, 2 }, new[] { 0, 1, 3 } }; break;
                case 5: map = new[] { new[] { 0, 1, 2 }, new[] { 3, 1, 4 } }; break;
                case 6: map = new[] { new[] { 0, 1, 2 }, new[] { 0, 1, 3 }, new[] { 4, 1, 5 } }; break;
                case 7: map = new[] { new[] { 0, 1, 2 }, new[] { 3, 1, 4 }, new[] { 5, 1, 6 } }; break;
                case 8: map = new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 1, 2, 5 }, new[] { 6, 1, 2, 7 } }; break;
                case 9: map = new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 1, 2, 5 }, new[] { 6, 1, 7, 8 } }; break;
                case 10: map = new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 1, 5, 6 }, new[] { 7, 1, 8, 9 } }; break;
                default: map = new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 1, 5, 6 }, new[] { 7, 8, 9, 10 } }; break;
            }
        }
    }
    static class DisplayNative
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE data, uint flags);
        [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IVirtualDesktopManager
        {
            [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool current);
            [PreserveSig] int GetWindowDesktopId(IntPtr window, out Guid desktop);
            [PreserveSig] int MoveWindowToDesktop(IntPtr window, ref Guid desktop);
        }
        public static Guid CurrentDesktop(IntPtr window)
        {
            object manager = null;
            try
            {
                manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")));
                Guid id; if (((IVirtualDesktopManager)manager).GetWindowDesktopId(window, out id) == 0) return id;
            }
            catch { }
            finally { if (manager != null && Marshal.IsComObject(manager)) Marshal.ReleaseComObject(manager); }
            return Guid.Empty;
        }
        public static List<MonitorData> Monitors()
        {
            var result = new List<MonitorData>();
            foreach (Screen screen in Screen.AllScreens)
            {
                int number; var match = Regex.Match(screen.DeviceName, @"DISPLAY(\d+)", RegexOptions.IgnoreCase);
                if (!int.TryParse(match.Groups[1].Value, out number)) number = result.Count + 1;
                var m = new MonitorData { DeviceName = screen.DeviceName, Number = number, Bounds = screen.Bounds, WorkArea = screen.WorkingArea, Primary = screen.Primary };
                for (uint i = 0; i < 32; i++)
                {
                    var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                    if (!EnumDisplayDevices(screen.DeviceName, i, ref d, 1)) break;
                    if ((d.StateFlags & 1) == 0 || (d.StateFlags & 8) != 0) continue;
                    string[] pieces = (d.DeviceID ?? "").Split('#');
                    if (pieces.Length >= 3) { m.Model = pieces[1]; m.Instance = pieces[2]; }
                    break;
                }
                m.Key = !string.IsNullOrEmpty(m.Instance) ? m.Model + "#" + m.Instance : screen.DeviceName;
                result.Add(m);
            }
            return result.OrderBy(m => m.Number).ToList();
        }
    }
}
