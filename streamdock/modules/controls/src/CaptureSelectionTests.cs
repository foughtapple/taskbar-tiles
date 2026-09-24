using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DockUtilities
{
    internal static class CaptureSelectionTests
    {
        private static int checks;
        private static StringBuilder output;
        private static void Check(bool ok, string label)
        { if (!ok) throw new InvalidOperationException("Test failed: " + label); checks++; output.AppendLine("PASS " + label); }
        private static ResolveZone Missing(Exception e)
        { return delegate(out string report) { report = ""; throw e; }; }
        private static void ExpectError<T>(Action action, string label) where T : Exception
        { bool ok = false; try { action(); } catch (T) { ok = true; } Check(ok, label); }
        private static CaptureSelection Select(Rectangle monitor, Point point, ResolveZone r)
        { return CaptureSelection.Select(point, monitor, "TEST DISPLAY", r); }
        internal static string Run()
        {
            checks = 0; output = new StringBuilder();
            Rectangle m = new Rectangle(0, 0, 2560, 1440);
            Point p = new Point(700, 500);
            Rectangle z = new Rectangle(500, 20, 1000, 1300);
            CaptureSelection selected = Select(m, p, delegate(out string r) { r = "original layout"; return new Zone(0,z); });
            Check(!selected.FullMonitor && selected.Bounds == z && selected.Report == "original layout", "Valid zone remains unchanged");
            foreach (Exception ex in new Exception[] {
                new FileNotFoundException("No saved FancyZones files"), new DirectoryNotFoundException("No FancyZones directory"),
                new IOException("Layout temporarily locked"), new UnauthorizedAccessException("Layout access denied"),
                new InvalidOperationException("No active layout"), new InvalidOperationException("Pointer in gap"),
                new InvalidOperationException("Pointer on taskbar"), new InvalidOperationException("Unsupported spanning layout"),
                new ArgumentException("Malformed JSON"), new FormatException("Bad numeric value"),
                new OverflowException("Oversized numeric value"), new InvalidCastException("Wrong JSON type"),
                new KeyNotFoundException("Missing setting"), new NotSupportedException("Unsupported layout") })
            {
                selected = Select(m, p, Missing(ex));
                Check(selected.FullMonitor && selected.Bounds == m, "Fallback: " + ex.Message);
            }
            selected = Select(m, p, delegate(out string r) { r = ""; return null; });
            Check(selected.FullMonitor, "Null zone falls back");
            selected = Select(m, p, delegate(out string r) { r = ""; return new Zone(0, new Rectangle(3000,0,400,800)); });
            Check(selected.FullMonitor && selected.Bounds == m, "Zone on another monitor is rejected");
            selected = Select(m, p, delegate(out string r) { r = ""; return new Zone(0, new Rectangle(0,0,400,800)); });
            Check(selected.FullMonitor, "Zone not containing mouse falls back");
            Rectangle left = new Rectangle(-1920,-120,1920,1080);
            selected = Select(left, new Point(-1000,400), Missing(new InvalidOperationException("No layout")));
            Check(selected.Bounds == left, "Negative coordinates and a secondary monitor are preserved");
            Rectangle ultra = new Rectangle(1920,0,5120,1440);
            selected = Select(ultra, new Point(4000,500), Missing(new InvalidOperationException("No layout")));
            Check(selected.Bounds == ultra && selected.Bounds.Width == 5120, "5120-wide monitor only, not the combined desktop");
            selected = Select(m, new Point(1200,1430), Missing(new InvalidOperationException("Pointer on taskbar")));
            Check(selected.Bounds.Bottom == 1440, "Whole monitor includes taskbar area");
            selected = Select(new Rectangle(0,0,3840,2160), new Point(3500,1900), Missing(new InvalidOperationException("No layout")));
            Check(selected.Bounds.Width == 3840 && selected.Bounds.Height == 2160, "Physical-size rectangle is not rescaled");
            selected = Select(m,p,Missing(new InvalidOperationException("Nothing was captured.\r\nLayout not found")));
            Check(!selected.Report.Contains("Nothing was captured") && !selected.Report.Contains("\n"), "Fallback log describes the successful alternate capture");
            ExpectError<OutOfMemoryException>(delegate { Select(m,p,Missing(new OutOfMemoryException())); }, "Out-of-memory is not silently converted to fallback");
            ExpectError<NullReferenceException>(delegate { Select(m,p,Missing(new NullReferenceException())); }, "Unexpected programming errors are not hidden");
            ExpectError<InvalidOperationException>(delegate { Select(Rectangle.Empty,p,Missing(new IOException())); }, "Missing physical monitor remains an error");
            ExpectError<ArgumentNullException>(delegate { Select(m,p,null); }, "Missing resolver remains an error");
            // Exercise the original geometry code through the new wrapper.
            var columns = Json.Parse("{\"type\":\"columns\",\"zone-count\":2,\"show-spacing\":true,\"spacing\":20}");
            Rectangle work = new Rectangle(0,0,2560,1400);
            var zones = Geometry.Build(columns, null, work);
            selected = Select(m,new Point(500,500),delegate(out string r) { r = "columns"; return Geometry.Select(zones,new Point(500,500)); });
            Check(!selected.FullMonitor && selected.Bounds == zones[0].Bounds, "Original columns still resolve to the original rectangle");
            selected = Select(m,new Point(1280,500),delegate(out string r) { r = "gap"; return Geometry.Select(zones,new Point(1280,500)); });
            Check(selected.FullMonitor, "Real column-spacing gap uses full-monitor fallback");
            return "Screenshot 05 v1.1: " + checks + " regression checks passed. No screen was captured.\r\n" + output;
        }
    }
}
