using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
namespace DockUtilities
{
    internal static class LayoutResolver
    {
        internal static Dictionary<string, object> ReadJson(string path)
        {
            Exception last = null;
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (stream.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Unexpectedly large layout file: " + Path.GetFileName(path));
                        using (var reader = new StreamReader(stream, Encoding.UTF8, true)) return Json.Parse(reader.ReadToEnd());
                    }
                }
                catch (Exception e) { last = e; Thread.Sleep(50); }
            }
            throw new InvalidOperationException("Cannot read " + Path.GetFileName(path) + ". Open FancyZones, apply your layout, and retry. " + last.Message);
        }
        internal static int MonitorScore(Dictionary<string, object> device, string name, string model, string instance, int number)
        {
            string mon = Json.Str(device, "monitor");
            string ins = Json.Str(device, "monitor-instance");
            if (String.IsNullOrEmpty(ins) && mon.Contains("#"))
            { string[] s = mon.Split('#'); mon = s[0]; ins = s.Length > 1 ? s[1] : ""; }
            if (String.Equals(mon, model, StringComparison.OrdinalIgnoreCase))
            {
                if (!String.IsNullOrEmpty(ins) && !String.IsNullOrEmpty(instance))
                    return String.Equals(ins, instance, StringComparison.OrdinalIgnoreCase) ? 100 : -1;
                int n = Json.Int(device, "monitor-number");
                return n > 0 && n != number ? -1 : (n == number ? 60 : 40);
            }
            if (String.Equals(mon, name, StringComparison.OrdinalIgnoreCase)) return 30;
            return -1; // A matching DISPLAY number alone must not select a stale, different monitor.
        }
        internal static Dictionary<string, object> ChooseApplied(object[] entries, string name, string model, string instance, int number, string desktop)
        {
            var matching = new List<KeyValuePair<int, Dictionary<string, object>>>();
            foreach (object entry in entries)
            {
                var e = Json.Obj(entry);
                var d = Json.Get(e, "device") as Dictionary<string, object>;
                if (d == null) continue;
                int score = MonitorScore(d, name, model, instance, number);
                if (score < 0) continue;
                string vd = Json.GuidKey(Json.Str(d, "virtual-desktop"));
                string current = Json.GuidKey(desktop);
                if (!String.IsNullOrEmpty(current))
                {
                    if (vd == current) score += 1000;
                    else if (vd == Guid.Empty.ToString("D") || vd == "") score += 0;
                    else continue;
                }
                matching.Add(new KeyValuePair<int, Dictionary<string, object>>(score, e));
            }
            if (matching.Count == 0) throw new InvalidOperationException("No active FancyZones layout matched this monitor and virtual desktop. Open the FancyZones editor on this monitor, select your layout, and press 05 again. Nothing was captured.");
            int best = matching.Max(x => x.Key);
            var layouts = matching.Where(x => x.Key == best).Select(x => Json.Obj(Json.Get(x.Value, "applied-layout"))).ToList();
            string[] signatures = layouts.Select(x => String.Join("|", new[] {
                Json.GuidKey(Json.Str(x, "uuid")), Json.Str(x, "type"), Json.Str(x, "zone-count"),
                Json.Str(x, "show-spacing"), Json.Str(x, "spacing") })).Distinct().ToArray();
            if (signatures.Length != 1) throw new InvalidOperationException("Several different layouts matched this monitor. The helper will not guess. Reapply the intended layout in FancyZones on the current virtual desktop, then retry.");
            return layouts[0];
        }
        internal static Zone Resolve(Point point, IntPtr foreground, out string report)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\PowerToys\FancyZones");
            string settingsPath = Path.Combine(folder, "settings.json");
            if (File.Exists(settingsPath))
            {
                var settings = ReadJson(settingsPath);
                var props = Json.Get(settings, "properties") as Dictionary<string, object>;
                if (props != null)
                {
                    var spanning = Json.Get(props, "fancyzones_span_zones_across_monitors") as Dictionary<string, object>;
                    if (spanning != null && Json.Bool(spanning, "value"))
                        throw new InvalidOperationException("This helper supports per-monitor FancyZones layouts, not the 'span across monitors' option. Nothing was captured.");
                }
            }
            Screen screen = Screen.FromPoint(point);
            if (!screen.WorkingArea.Contains(point)) throw new InvalidOperationException("Move the pointer inside the monitor work area, not over the taskbar, then press 05 again.");
            string[] identity = Native.MonitorIdentity(screen.DeviceName);
            int number; Int32.TryParse(Regex.Replace(screen.DeviceName, "[^0-9]", ""), out number);
            string desktop = Native.DesktopId(foreground);
            var applied = ReadJson(Path.Combine(folder, "applied-layouts.json"));
            var layout = ChooseApplied(Json.Arr(Json.Get(applied, "applied-layouts")), screen.DeviceName, identity[0], identity[1], number, desktop);
            Dictionary<string, object> custom = null;
            if (Json.Str(layout, "type").Equals("custom", StringComparison.OrdinalIgnoreCase))
            {
                var customs = ReadJson(Path.Combine(folder, "custom-layouts.json"));
                string id = Json.GuidKey(Json.Str(layout, "uuid"));
                var found = Json.Arr(Json.Get(customs, "custom-layouts")).Select(Json.Obj)
                    .Where(x => Json.GuidKey(Json.Str(x, "uuid")) == id).ToList();
                if (found.Count != 1) throw new InvalidOperationException("The active custom layout is missing or duplicated. Reapply it in FancyZones and retry.");
                custom = found[0];
            }
            List<Zone> zones = Geometry.Build(layout, custom, screen.WorkingArea);
            Zone selected = Geometry.Select(zones, point);
            report = String.Format("Monitor={0}; Layout={1}; Zone={2}; X={3}; Y={4}; W={5}; H={6}; Pointer={7},{8}",
                screen.DeviceName, Json.Str(layout, "type"), selected.Id + 1,
                selected.Bounds.X, selected.Bounds.Y, selected.Bounds.Width, selected.Bounds.Height, point.X, point.Y);
            return selected;
        }
    }

}
