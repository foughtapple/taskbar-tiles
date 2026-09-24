using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DockUtilities
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; internal Rectangle Rectangle { get { return System.Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom); } } }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct DISPLAY_DEVICE
        {
            internal int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceString;
            internal int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string DeviceKey;
        }
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr w, out RECT r);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr w, int a, out RECT r, int s);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplayDevices(string device, uint n, ref DISPLAY_DEVICE d, uint flags);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] data, int size);
        [StructLayout(LayoutKind.Sequential)] internal struct INPUT { internal uint type; internal UNION data; }
        [StructLayout(LayoutKind.Explicit)] internal struct UNION
        {
            [FieldOffset(0)] internal MOUSEINPUT mouse;
            [FieldOffset(0)] internal KEYBDINPUT key;
            [FieldOffset(0)] internal HARDWAREINPUT hardware;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { internal int dx, dy; internal uint mouseData, flags, time; internal UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { internal ushort vk, scan; internal uint flags, time; internal UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct HARDWAREINPUT { internal uint message; internal ushort low, high; }
        [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVirtualDesktopManager
        {
            [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr w, [MarshalAs(UnmanagedType.Bool)] out bool current);
            [PreserveSig] int GetWindowDesktopId(IntPtr w, out Guid id);
            [PreserveSig] int MoveWindowToDesktop(IntPtr w, ref Guid id);
        }
        internal static void Dpi()
        { try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { SetProcessDPIAware(); } }
        internal static Rectangle WindowBounds(IntPtr w)
        {
            RECT r;
            if (w == IntPtr.Zero) throw new InvalidOperationException("No active window was found.");
            if (DwmGetWindowAttribute(w, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0 && !GetWindowRect(w, out r))
                throw new InvalidOperationException("Windows did not return the active window's bounds.");
            return r.Rectangle;
        }
        internal static string DesktopId(IntPtr w)
        {
            object instance = null;
            try
            {
                instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")));
                var manager = (IVirtualDesktopManager)instance; Guid id;
                if (manager.GetWindowDesktopId(w, out id) == 0 && id != Guid.Empty) return id.ToString("D");
            }
            catch { }
            finally { if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance); }
            string[] paths = {
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\" + Process.GetCurrentProcess().SessionId + @"\VirtualDesktops"
            };
            foreach (string path in paths)
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
                {
                    byte[] b = key == null ? null : key.GetValue("CurrentVirtualDesktop") as byte[];
                    if (b != null && b.Length == 16) return new Guid(b).ToString("D");
                }
            }
            return "";
        }
        internal static string[] MonitorIdentity(string deviceName)
        {
            for (uint i = 0; i < 32; i++)
            {
                DISPLAY_DEVICE d = new DISPLAY_DEVICE(); d.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (!EnumDisplayDevices(deviceName, i, ref d, 1)) break;
                if ((d.StateFlags & 1) == 0 || (d.StateFlags & 8) != 0) continue;
                string[] pieces = (d.DeviceID ?? "").Split('#');
                if (pieces.Length >= 3) return new[] { pieces[1], pieces[2] };
                pieces = (d.DeviceID ?? "").Split('\\');
                if (pieces.Length >= 3) return new[] { pieces[1], pieces[2] };
            }
            return new[] { deviceName, "" };
        }
        internal static ushort[] ParseHotkey(string text)
        {
            var mods = new HashSet<ushort>(); ushort main = 0;
            foreach (string piece in text.Split('+'))
            {
                string s = piece.Trim().ToUpperInvariant();
                switch (s)
                {
                    case "CTRL": case "CONTROL": mods.Add(0x11); continue;
                    case "ALT": mods.Add(0x12); continue;
                    case "SHIFT": mods.Add(0x10); continue;
                    case "WIN": case "WINDOWS": mods.Add(0x5B); continue;
                }
                ushort key = 0; int f;
                if (s.Length == 1 && ((s[0] >= 'A' && s[0] <= 'Z') || (s[0] >= '0' && s[0] <= '9'))) key = (ushort)s[0];
                else if (s.StartsWith("F") && Int32.TryParse(s.Substring(1), out f) && f >= 1 && f <= 24) key = (ushort)(0x70 + f - 1);
                else if (s == "SPACE") key = 0x20;
                if (key == 0 || main != 0) throw new InvalidOperationException("Use modifiers plus ONE key, for example Ctrl+Alt+Shift+F. Supported keys: A-Z, 0-9, F1-F24, Space.");
                main = key;
            }
            if (main == 0 || mods.Count == 0) throw new InvalidOperationException("The hotkey needs at least one modifier and one main key.");
            var keys = new List<ushort>();
            foreach (ushort mod in new ushort[] { 0x11, 0x12, 0x10, 0x5B }) if (mods.Contains(mod)) keys.Add(mod);
            keys.Add(main); return keys.ToArray();
        }
        internal static void Hotkey(string text)
        {
            ushort[] keys = ParseHotkey(text);
            int[] checks = new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Concat(keys.Select(x => (int)x)).Distinct().ToArray();
            for (int i = 0; i < 30 && checks.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0); i++) Thread.Sleep(30);
            if (checks.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0)) throw new InvalidOperationException("Release the keyboard modifiers, then press the dock button again.");
            INPUT[] down = keys.Select(k => new INPUT { type = 1, data = new UNION { key = new KEYBDINPUT { vk = k } } }).ToArray();
            INPUT[] up = keys.Reverse().Select(k => new INPUT { type = 1, data = new UNION { key = new KEYBDINPUT { vk = k, flags = 2 } } }).ToArray();
            int size = Marshal.SizeOf(typeof(INPUT));
            try
            {
                if (SendInput((uint)down.Length, down, size) != (uint)down.Length)
                    throw new InvalidOperationException("Windows blocked the shortcut. Run the dock and the target app normally, not at different administrator levels.");
                Thread.Sleep(60);
            }
            finally { SendInput((uint)up.Length, up, size); }
        }
    }
}
