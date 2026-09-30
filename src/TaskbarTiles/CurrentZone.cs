using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class CurrentZonePolicy
    {
        internal static ZoneDestination Select(ZoneCatalog catalog, Rectangle window, Point fallback)
        {
            if (catalog == null || catalog.Monitors.Count == 0) return null;
            Point anchor = window.IsEmpty ? fallback : new Point(window.Left + window.Width / 2, window.Top + window.Height / 2);
            var monitor = catalog.Monitors.FirstOrDefault(m => m.Bounds.Contains(anchor)) ??
                catalog.Monitors.OrderByDescending(m => Area(Rectangle.Intersect(m.Bounds, window))).First();
            // Basic fallback diagrams are useful in the picker, but are not an active FancyZone.
            ZoneRect zone = null;
            if ((monitor.Status ?? "").StartsWith("FancyZones:", StringComparison.Ordinal))
                zone = monitor.Zones.Where(z => z.Bounds.Contains(anchor)).OrderBy(z => Area(z.Bounds)).FirstOrDefault() ??
                    monitor.Zones.Where(z => Area(Rectangle.Intersect(z.Bounds, window)) > 0)
                        .OrderByDescending(z => Area(Rectangle.Intersect(z.Bounds, window))).ThenBy(z => z.Number).FirstOrDefault();
            return new ZoneDestination { Monitor = monitor, Bounds = zone == null ? monitor.WorkArea : zone.Bounds,
                Maximise = zone == null, Number = zone == null ? 0 : zone.Number };
        }
        static long Area(Rectangle r) { return (long)Math.Max(0, r.Width) * Math.Max(0, r.Height); }
        internal static Rectangle Button(Rectangle card, float scale)
        {
            int side = Math.Min(Math.Min(card.Width, card.Height), Math.Max(18, (int)Math.Round(26 * scale)));
            int pad = Math.Max(2, (int)Math.Round(5 * scale));
            return new Rectangle(card.Right - side - pad, card.Bottom - side - pad, side, side);
        }
        internal static void Test(StringBuilder log)
        {
            var monitor = new MonitorData { Bounds = new Rectangle(-1000, 0, 1000, 800), WorkArea = new Rectangle(-1000, 0, 1000, 760), Status = "FancyZones: columns" };
            monitor.Zones.Add(new ZoneRect { Number = 1, Bounds = new Rectangle(-990, 10, 480, 740) });
            monitor.Zones.Add(new ZoneRect { Number = 2, Bounds = new Rectangle(-490, 10, 480, 740) });
            var catalog = new ZoneCatalog(); catalog.Monitors.Add(monitor);
            var selected = Select(catalog, new Rectangle(-900, 20, 300, 600), Point.Empty);
            if (selected.Maximise || selected.Number != 1 || selected.Bounds != monitor.Zones[0].Bounds) throw new InvalidOperationException("Current-zone desktop coordinates changed.");
            monitor.Status = "Basic zones";
            if (!Select(catalog, new Rectangle(-900, 20, 300, 600), Point.Empty).Maximise) throw new InvalidOperationException("No FancyZones must use whole screen.");
            if (Select(new ZoneCatalog(), Rectangle.Empty, Point.Empty) != null) throw new InvalidOperationException("Missing monitor must not invent a destination.");
            foreach (float scale in new[] { .5f, 1f, 1.5f, 2f })
            {
                var card = new Rectangle(10, 20, 120, 120);
                if (!card.Contains(Button(card, scale))) throw new InvalidOperationException("Destination button escaped its card.");
            }
            log.AppendLine("PASS: current-zone source-window selection, full-monitor fallback and destination button bounds.");
        }
    }
    sealed partial class Switcher
    {
        ZoneDestination zoneAtOpen;
        Rectangle profileLayoutButton;
        void CaptureCurrentZone(IntPtr foreground)
        {
            try
            {
                Rectangle source = foreground != IntPtr.Zero && Native.IsWindow(foreground) ? WindowNative.VisibleBounds(foreground) : Rectangle.Empty;
                zoneAtOpen = CurrentZonePolicy.Select(ZoneCatalog.Load(options, desktopAtOpen), source, monitorPoint);
            }
            catch (Exception ex) { zoneAtOpen = null; Program.Log("Current destination: " + ex.GetType().Name); }
        }
        void PlaceInCurrentZone(WindowItem window, AppButton app)
        {
            CancelPassiveLaunchObservation();
            if (!options.RightClickZones || transient != null) return;
            if (pending != null || launchPlacement != null || mover.Busy || ProfileLayoutBusy) { Notify("Finish the current launch or layout first."); return; }
            if (zoneAtOpen == null) { Notify("The current screen is unavailable. Use the corner button to choose a destination."); return; }
            Dismiss();
            if (app != null) QueueLaunch(app, zoneAtOpen);
            else if (window != null) MoveTo(window.Handle, zoneAtOpen, true);
        }
        int HitDestinationButton(Point point)
        {
            if (!options.RightClickZones) return -100;
            for (int i = 0; i < cardRects.Count; i++) if (CurrentZonePolicy.Button(cardRects[i], scale).Contains(point)) return -1000 - i;
            for (int i = 0; i < tileRects.Count; i++) if (CurrentZonePolicy.Button(tileRects[i], scale).Contains(point)) return -2000 - i;
            return -100;
        }
        void PaintDestinationButton(Graphics g, Rectangle card, int hit)
        {
            if (!options.RightClickZones) return;
            Rectangle r = CurrentZonePolicy.Button(card, scale);
            DrawingUtil.Round(g, r, S(4), lastMouseHit == hit ? Color.FromArgb(45, 65, 90) : Theme.Card, Theme.Border, 1);
            using (var pen = new Pen(lastMouseHit == hit ? Theme.Accent : Theme.Text, Math.Max(1, scale)))
            {
                Rectangle icon = Rectangle.Inflate(r, -S(6), -S(7));
                g.DrawRectangle(pen, icon);
                g.DrawLine(pen, icon.Left + icon.Width / 2, icon.Top, icon.Left + icon.Width / 2, icon.Bottom);
            }
        }
    }
}
