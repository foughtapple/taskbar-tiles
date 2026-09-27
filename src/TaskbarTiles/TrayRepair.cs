// Tray artwork and menu lifetime repair. Windows icon cache access is READ-ONLY.
// Entries still come exclusively from the live accessibility inventory.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarTiles
{
    static class TrayMenuLifetime
    {
        // Keep one menu alive across shows. ToolStrip still accesses it after Closed.
        internal static ContextMenuStrip Create(Control owner)
        {
            var menu = new ContextMenuStrip { BackColor = Theme.Card, ForeColor = Theme.Text };
            owner.Disposed += delegate { if (!menu.IsDisposed) menu.Dispose(); };
            return menu;
        }
        internal static void Queue(Control owner, Action action)
        {
            if (owner.IsDisposed || owner.Disposing || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke(new Action(delegate { if (!owner.IsDisposed && !owner.Disposing) action(); })); }
            catch (InvalidOperationException) { /* Owner is already shutting down. */ }
        }
        internal static void Release(Control owner, ContextMenuStrip menu)
        {
            if (menu == null || menu.IsDisposed) return;
            menu.Close();
            // Never dispose during Closed/ItemClicked/auto-close's call stack.
            Queue(owner, delegate { if (!menu.IsDisposed) menu.Dispose(); });
            // Owner.Disposed also releases it if the message loop ends first.
        }
    }

    sealed class TraySnapshotRecord
    {
        internal string Tooltip = "", Executable = "", Guid = "";
        internal byte[] Bytes;
    }
    sealed class WindowsTrayArt
    {
        internal const int MaxBytes = 262144, MaxDimension = 256;
        List<TraySnapshotRecord> records = new List<TraySnapshotRecord>();
        DateTime refreshed;
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
        internal static string Normal(string text)
        { return Regex.Replace((text ?? "").Trim(), @"\s+", " "); }
        static string LocalExecutable(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            try
            {
                if (path.StartsWith("{"))
                {
                    int end = path.IndexOf('}'); Guid id;
                    if (end < 0 || !Guid.TryParse(path.Substring(0, end + 1), out id)) return "";
                    IntPtr buffer;
                    if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out buffer) != 0) return "";
                    try { path = Path.Combine(Marshal.PtrToStringUni(buffer), path.Substring(end + 1).TrimStart('\\', '/')); }
                    finally { Marshal.FreeCoTaskMem(buffer); }
                }
                // An icon lookup must not contact network shares or execute anything.
                if (path.StartsWith(@"\\") || !Path.IsPathRooted(path) || !File.Exists(path)) return "";
                return Path.GetFullPath(path);
            }
            catch { return ""; }
        }
        internal void Refresh()
        {
            if ((DateTime.UtcNow - refreshed).TotalSeconds < 5) return;
            refreshed = DateTime.UtcNow;
            var found = new List<TraySnapshotRecord>(); int bytes = 0;
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", false))
                {
                    if (root != null)
                    foreach (string name in root.GetSubKeyNames().Take(1024))
                    {
                        try
                        {
                            using (var key = root.OpenSubKey(name, false))
                            {
                                if (key == null) continue;
                                string tooltip = key.GetValue("InitialTooltip") as string ?? "";
                                string exe = LocalExecutable(key.GetValue("ExecutablePath") as string);
                                byte[] data = key.GetValue("IconSnapshot") as byte[];
                                if (tooltip.Length == 0 || tooltip.Length > 2048 || exe.Length == 0) continue;
                                if (data == null || data.Length > MaxBytes || data.Length < 24) continue;
                                bytes += data.Length; if (bytes > 16777216) break;
                                found.Add(new TraySnapshotRecord { Tooltip = tooltip, Executable = exe,
                                    Guid = Convert.ToString(key.GetValue("IconGuid")), Bytes = data });
                            }
                        }
                        catch (System.Security.SecurityException) { }
                        catch (IOException) { }
                    }
                }
            }
            catch (System.Security.SecurityException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            records = found;
        }
        internal static TraySnapshotRecord Match(IEnumerable<TraySnapshotRecord> source, string name, string automationId)
        {
            string title = Normal(name);
            if (title.Length == 0) return null;
            var exact = source.Where(r => string.Equals(Normal(r.Tooltip), title, StringComparison.OrdinalIgnoreCase)).ToList();
            // Some providers expose a GUID; use only a complete GUID, never a numeric runtime ID.
            var guid = Regex.Match(automationId ?? "", @"\{[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\}");
            if (guid.Success)
            {
                var sameGuid = source.Where(r => string.Equals(r.Guid, guid.Value, StringComparison.OrdinalIgnoreCase)).ToList();
                if (sameGuid.Count > 0) exact = sameGuid;
            }
            if (exact.Count == 0) return null;
            var first = exact[0];
            // Duplicate old registry records must never pick a different program/image at random.
            if (exact.Any(r => !string.Equals(r.Executable, first.Executable, StringComparison.OrdinalIgnoreCase) ||
                r.Bytes == null || first.Bytes == null || !r.Bytes.SequenceEqual(first.Bytes))) return null;
            return first;
        }
        internal static Bitmap Decode(byte[] data)
        {
            if (data == null || data.Length < 24 || data.Length > MaxBytes) return null;
            byte[] png = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (int i = 0; i < png.Length; i++) if (data[i] != png[i]) return null;
            if (data[12] != 73 || data[13] != 72 || data[14] != 68 || data[15] != 82) return null;
            uint width = BigEndian(data, 16), height = BigEndian(data, 20);
            if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension) return null;
            try
            {
                using (var stream = new MemoryStream(data, false))
                using (var image = Image.FromStream(stream, false, true))
                {
                    if (image.Width != width || image.Height != height) return null;
                    return new Bitmap(image); // Detach from the stream before it closes.
                }
            }
            catch (ArgumentException) { return null; }
            catch (ExternalException) { return null; }
            catch (OutOfMemoryException) { return null; } // GDI+ also reports invalid images this way.
        }
        static uint BigEndian(byte[] b, int at)
        { return ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3]; }
        internal Bitmap Get(string title, string id)
        {
            Refresh(); var record = Match(records, title, id);
            return record == null ? null : Decode(record.Bytes);
        }
    }

    static class TrayImageFallback
    {
        internal static void Paint(Graphics graphics, Rectangle box)
        {
            // A neutral Windows application symbol, not an invented letter badge or unread count.
            graphics.DrawIcon(SystemIcons.Application, box);
        }
    }

    sealed partial class Switcher
    {
        NotificationItem notificationMenuTarget;
        bool NotificationMenuOpen { get { return notificationMenu != null && !notificationMenu.IsDisposed && notificationMenu.Visible; } }
        void EnsureNotificationMenu()
        {
            if (notificationMenu != null && !notificationMenu.IsDisposed) return;
            notificationMenu = TrayMenuLifetime.Create(this);
            notificationMenu.Items.Add("Open / default action", null, delegate
            {
                var target = notificationMenuTarget;
                TrayMenuLifetime.Queue(this, delegate { InvokeNotification(target); });
            });
            notificationMenu.Items.Add("Load / refresh Windows tray images", null, delegate
            { TrayMenuLifetime.Queue(this, LoadWindowsTray); });
            notificationMenu.Items.Add("Refresh tray items", null, delegate
            { TrayMenuLifetime.Queue(this, RefreshNotificationArea); });
            notificationMenu.Items.Add("Copy name", null, delegate
            {
                var target = notificationMenuTarget;
                if (target != null) try { Clipboard.SetText(target.Name); } catch (ExternalException) { }
            });
            notificationMenu.Items.Add("Copy tray diagnostics", null, delegate
            {
                var report = new StringBuilder("Taskbar Tiles " + Program.Version + " - tray diagnostics (local)\r\n");
                foreach (var item in notificationItems)
                    report.AppendLine(item.Name + " | " + item.ClassName + " | " + item.AutomationId + " | " +
                        (item.SystemItem ? "system" : "app") + " | offscreen=" + item.Offscreen + " | " + item.ImageSource);
                try { Clipboard.SetText(report.ToString()); } catch (ExternalException) { }
            });
            // Closed deliberately does NOT dispose, clear items, or mutate menu state.
        }
        internal ContextMenuStrip ShowNotificationMenuForTest(Point point)
        {
            ShowNotificationActions(new NotificationItem { Name = "Fixture tray entry" }, point);
            return notificationMenu;
        }
        internal void ReleaseNotificationMenuForTest()
        {
            var menu = notificationMenu; notificationMenu = null;
            TrayMenuLifetime.Release(this, menu);
        }
    }
}
