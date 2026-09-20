// Explicit input-topology and setup evidence policies. No input injection or settings writes.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace TaskbarTiles
{
    enum TouchInputKind { Unknown, Mouse, Keyboard, Digitizer, Other }
    enum TouchChangeAction { Ignore, Cancel, Retest }
    static class TouchSetupPolicy
    {
        internal static TouchChangeAction DeviceChange(int change, bool known, TouchInputKind kind)
        {
            if (change != 1 && change != 2) return TouchChangeAction.Retest;
            if (kind == TouchInputKind.Mouse || kind == TouchInputKind.Keyboard || kind == TouchInputKind.Other)
                return TouchChangeAction.Cancel;
            // Enumeration can precede the queued arrival. An already-enumerated
            // digitizer is not a newly disconnected input path.
            if (kind == TouchInputKind.Digitizer && change == 1 && known) return TouchChangeAction.Ignore;
            return TouchChangeAction.Retest; // Genuine/unknown digitizer changes stay fail-closed.
        }
        internal static bool PumpGap(uint before, uint now)
        { return unchecked(now - before) > 1500; }
        internal static TouchInputKind Kind(IntPtr device)
        {
            if (device == IntPtr.Zero) return TouchInputKind.Unknown;
            // RID_DEVICE_INFO: cbSize, dwType, then a 24-byte union.
            uint size = 32; IntPtr data = Marshal.AllocHGlobal(32);
            try
            {
                for (int i = 0; i < 32; i += 4) Marshal.WriteInt32(data, i, 0);
                Marshal.WriteInt32(data, 32);
                uint read = TouchHidNative.GetRawInputDeviceInfo(device, 0x2000000b, data, ref size);
                if (read == uint.MaxValue || read < 8) return TouchInputKind.Unknown;
                int type = Marshal.ReadInt32(data, 4);
                if (type == 0) return TouchInputKind.Mouse;
                if (type == 1) return TouchInputKind.Keyboard;
                if (type != 2 || read < 24) return TouchInputKind.Unknown;
                int page = (ushort)Marshal.ReadInt16(data, 20), usage = (ushort)Marshal.ReadInt16(data, 22);
                return page == 13 && (usage == 1 || usage == 2 || usage == 4) ? TouchInputKind.Digitizer : TouchInputKind.Other;
            }
            finally { Marshal.FreeHGlobal(data); }
        }
    }
    static class TouchSetupChecklist
    {
        static string Check(bool ok, string name) { return (ok ? "PASS: " : "WAIT: ") + name; }
        internal static List<string> Lines(TouchDeviceEvidence device, bool connected, bool stable, bool anchor,
            bool requireHover, string block)
        {
            var lines = new List<string>();
            lines.Add(Check(connected && stable, "selected input screen is connected with a stable identity"));
            lines.Add(string.IsNullOrEmpty(block) ? "PASS: no blocking input fault" : "BLOCKED: " + block + "; let the connection settle, then Restart test");
            if (device == null) { lines.Add("WAIT: select the Touch device for finger testing, or Pen for pen testing"); return lines; }
            lines.Add(Check(device.Supported, "input report format recognised (not proof of working input)"));
            lines.Add(Check(device.Frames > 0, "actual reports received: " + device.Frames));
            lines.Add(Check(device.SawUp && device.HeldMilliseconds >= 2500,
                "hold for 3 seconds, then release; longest completed hold: " + device.HeldMilliseconds + " ms"));
            if (device.Kind != "Pen") lines.Add(Check(device.MaxContacts >= 2,
                "two simultaneous fingers observed; maximum: " + device.MaxContacts));
            lines.Add(Check(device.Frames > 0 && device.Complete && device.Contacts == 0 && device.SawUp,
                "complete reports confirm all contacts released"));
            if (device.Kind == "Pen" && requireHover)
                lines.Add(Check(device.HoverKnown && device.SawHoverExit && !device.CurrentHover,
                    "pen hover exit observed (descriptor support alone is not enough)"));
            lines.Add(Check(anchor, "pre-touch window/cursor verified for this screen; start with another screen's app active"));
            return lines;
        }
        internal static bool Ready(TouchDeviceEvidence d, bool connected, bool stable, bool anchor, bool requireHover, string block)
        {
            return d != null && connected && stable && string.IsNullOrEmpty(block) && d.Ready &&
                d.Complete && d.Contacts == 0 && anchor &&
                (d.Kind != "Pen" || !requireHover || (d.HoverKnown && d.SawHoverExit && !d.CurrentHover));
        }
    }
}
