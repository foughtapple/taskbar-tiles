// A close timestamp alone does not identify which digitizer/monitor produced a pointer.
// This provider accepts normal, unambiguous normalized-coordinate mappings only.
// Unsupported rotations/transforms fail the association test instead of being guessed.
using System;
using System.Drawing;
using System.Linq;
namespace TaskbarTiles
{
    static class TouchAnchorPolicy
    {
        internal static bool Matches(TouchFrame frame, Point promoted, Rectangle monitor)
        {
            if (frame == null || !frame.Valid || !frame.Complete || frame.Down == 0 ||
                monitor.Width <= 0 || monitor.Height <= 0 || !monitor.Contains(promoted)) return false;
            double tolerance = Math.Max(8, Math.Min(20, Math.Min(monitor.Width, monitor.Height) * .006));
            return frame.Contacts.Any(c => c.Down && c.Id >= 0 &&
                !double.IsNaN(c.X) && !double.IsInfinity(c.X) && !double.IsNaN(c.Y) && !double.IsInfinity(c.Y) &&
                c.X >= 0 && c.X <= 1 && c.Y >= 0 && c.Y <= 1 &&
                Math.Abs(monitor.Left + c.X * (monitor.Width - 1) - promoted.X) <= tolerance &&
                Math.Abs(monitor.Top + c.Y * (monitor.Height - 1) - promoted.Y) <= tolerance);
        }
    }
}
