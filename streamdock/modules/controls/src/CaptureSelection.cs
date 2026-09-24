// Screenshot 05 v1.1: resolve a FancyZone, otherwise capture ONE whole monitor.
// This class is deliberately independent of live Windows APIs for regression tests.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security;

namespace DockUtilities
{
    internal delegate Zone ResolveZone(out string report);

    internal sealed class CaptureSelection
    {
        internal Rectangle Bounds;
        internal bool FullMonitor;
        internal string Report;

        internal static CaptureSelection Select(Point pointer, Rectangle monitor, string name, ResolveZone resolve)
        {
            if (monitor.Width <= 0 || monitor.Height <= 0)
                throw new InvalidOperationException("Windows did not return usable monitor bounds. No screenshot was taken.");
            if (resolve == null) throw new ArgumentNullException("resolve");
            try
            {
                string report;
                Zone zone = resolve(out report);
                if (zone == null || zone.Bounds.Width <= 0 || zone.Bounds.Height <= 0 ||
                    !monitor.Contains(zone.Bounds) || !zone.Bounds.Contains(pointer))
                    throw new InvalidOperationException("No valid FancyZone contains the mouse on this monitor.");
                return new CaptureSelection { Bounds = zone.Bounds, FullMonitor = false, Report = report ?? "FancyZone selected." };
            }
            catch (Exception error)
            {
                // Limit fallback to resolution/configuration errors. We do NOT catch
                // capture, bitmap-allocation or clipboard errors here, since they occur
                // later. In particular, out-of-memory/fatal errors must remain errors.
                if (!RecoverableLayoutError(error)) throw;
                string reason = (error.Message ?? error.GetType().Name).Replace("\r", " ").Replace("\n", " ");
                reason = reason.Replace("Nothing was captured.", "").Replace("Nothing was captured", "").Trim();
                if (reason.Length > 700) reason = reason.Substring(0, 700);
                return new CaptureSelection {
                    Bounds = monitor, FullMonitor = true,
                    Report = String.Format("Full-monitor fallback; Monitor={0}; X={1}; Y={2}; W={3}; H={4}; Pointer={5},{6}. Reason: {7}",
                        name, monitor.X, monitor.Y, monitor.Width, monitor.Height, pointer.X, pointer.Y, reason)
                };
            }
        }

        internal static bool RecoverableLayoutError(Exception error)
        {
            return error is InvalidOperationException || error is IOException ||
                error is UnauthorizedAccessException || error is ArgumentException ||
                error is FormatException || error is OverflowException ||
                error is InvalidCastException || error is KeyNotFoundException ||
                error is NotSupportedException || error is SecurityException;
        }
    }
}
