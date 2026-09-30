// No real tray actions or registry writes. Production menu and image paths in fixtures.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class TrayRepairTests
    {
        static void Require(bool ok, string message)
        { if (!ok) throw new InvalidOperationException("Tray repair: " + message); }
        internal static void Run(StringBuilder log)
        {
            byte[] data;
            using (var icon = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(icon))
            using (var stream = new MemoryStream())
            {
                g.Clear(Color.Transparent); g.FillRectangle(Brushes.Lime, 8, 8, 16, 16);
                icon.Save(stream, ImageFormat.Png); data = stream.ToArray();
            }
            using (var decoded = WindowsTrayArt.Decode(data))
            {
                Require(decoded != null && decoded.Width == 32, "Windows-style PNG decoded independently of visibility");
                Require(decoded.GetPixel(16,16).G == 255 && decoded.GetPixel(0,0).A == 0, "image pixels and transparency retained");
            }
            Require(WindowsTrayArt.Decode(new byte[12]) == null, "short snapshot rejected");
            var huge = (byte[])data.Clone(); huge[16] = 127;
            Require(WindowsTrayArt.Decode(huge) == null, "oversized dimensions rejected before GDI decode");
            Require(WindowsTrayArt.Decode(new byte[WindowsTrayArt.MaxBytes + 1]) == null, "encoded size bounded");
            var a = new TraySnapshotRecord { Tooltip = "Fixture tray\nitem", Executable = @"C:\Fixture\a.exe", Bytes = data };
            Require(WindowsTrayArt.Match(new[] { a }, " Fixture tray item ", "") == a, "exact tooltip match normalises whitespace");
            Require(WindowsTrayArt.Match(new[] { a }, "Fixture", "") == null, "partial title does not guess artwork");
            var b = new TraySnapshotRecord { Tooltip = a.Tooltip, Executable = @"C:\Other\b.exe", Bytes = data };
            Require(WindowsTrayArt.Match(new[] { a,b }, "Fixture tray item", "") == null, "ambiguous historical records not selected");
            Require(WindowsTrayArt.Match(new[] { a,a }, "Fixture tray item", "") == a, "identical duplicate cache entries collapse safely");
            Require(NotificationRoots.IsSystem("", "SystemTray.TextIconContent", "ENG US", false), "language system control classified");
            Require(!NotificationRoots.IsSystem("network agent", "SystemTray.NormalIconView", "Fixture app", false), "real app icon wins over incidental system keywords");
            log.AppendLine("PASS: cached tray PNG pixels/transparency, bounds, tooltip matching and ambiguous records; no registry writes or invented unread values.");
        }
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
        internal static void RunNative(StringBuilder log)
        {
            Run(log);
            var errors = new List<Exception>();
            System.Threading.ThreadExceptionEventHandler onError = delegate(object sender, System.Threading.ThreadExceptionEventArgs e) { errors.Add(e.Exception); };
            Application.ThreadException += onError;
            Point cursor = Cursor.Position;
            try
            {
                using (var owner = new Switcher(true))
                {
                    owner.Size = new Size(640, 420); owner.Location = new Point(30,30); owner.Show(); Application.DoEvents();
                    ContextMenuStrip first = null;
                    for (int i = 0; i < 30; i++)
                    {
                        var menu = owner.ShowNotificationMenuForTest(new Point(40,40));
                        Application.DoEvents();
                        if (first == null) first = menu;
                        Require(ReferenceEquals(first, menu), "context menu reused rather than freed from Closed");
                        menu.Close(i % 2 == 0 ? ToolStripDropDownCloseReason.AppClicked : ToolStripDropDownCloseReason.Keyboard);
                        Application.DoEvents();
                        Require(!menu.IsDisposed && !owner.IsDisposed, "closing/cancelling never disposes menu or switcher");
                    }
                    var shown = owner.ShowNotificationMenuForTest(new Point(40,40)); Application.DoEvents();
                    // Click inside this fixture, outside its menu. This is ToolStrip's actual auto-close path.
                    Cursor.Position = owner.PointToScreen(new Point(600,370));
                    mouse_event(2,0,0,0,UIntPtr.Zero); mouse_event(4,0,0,0,UIntPtr.Zero);
                    for(int i=0;i<10;i++){Application.DoEvents();System.Threading.Thread.Sleep(20);}
                    Require(!shown.Visible && !shown.IsDisposed && !owner.IsDisposed, "real outside click does not crash the resident menu");
                    owner.Show();
                    var actionMenu = owner.ShowNotificationMenuForTest(new Point(40,40));
                    // Default action sees no reader in the isolated switcher; it cannot launch anything.
                    actionMenu.Items[0].PerformClick(); actionMenu.Close(ToolStripDropDownCloseReason.ItemClicked);
                    Application.DoEvents();
                    Require(!owner.IsDisposed && !actionMenu.IsDisposed, "menu command completes without early disposal");
                    owner.ReleaseNotificationMenuForTest(); Application.DoEvents();
                    Require(actionMenu.IsDisposed, "explicit release disposes after the close call returns");
                    owner.Show();
                    var reopened = owner.ShowNotificationMenuForTest(new Point(40,40)); reopened.Close();
                    owner.Dispose();
                    Require(reopened.IsDisposed, "owner teardown releases the final reusable menu");
                }
                Exception settingsError = null;
                var settingsThread = new System.Threading.Thread(delegate()
                {
                    try { RunSettingsMenuFixture(); } catch (Exception ex) { settingsError = ex; }
                    finally { Application.ExitThread(); }
                });
                settingsThread.IsBackground = true; settingsThread.SetApartmentState(System.Threading.ApartmentState.STA);
                settingsThread.Start(); Require(settingsThread.Join(15000), "Settings menu fixture apartment completed");
                if (settingsError != null) throw new InvalidOperationException("Settings menu fixture failed.", settingsError);
                // These fixtures pump with DoEvents rather than Application.Run.
                // Release its native thread-context windows before later RPC tests.
                Application.ExitThread();
                Require(errors.Count == 0, "no UI-thread exception during close, selection or teardown: " + string.Join("; ", errors.Select(e=>e.Message).ToArray()));
            }
            finally { Cursor.Position = cursor; Application.ThreadException -= onError; }
            log.AppendLine("PASS: production tray context menu; 30 cancellation/reopen cycles, actual outside click, queued item action and final disposal; switcher survives.");
        }
        static void RunSettingsMenuFixture()
        {
            using (var settings = new SettingsWindow(new Options { HideOnFocusLoss = false }, delegate { }, null,
                new[] { new AppButton { DisplayName = "Fixture shortcut", Favourite = new FavouriteEntry { Name = "Fixture shortcut", Target = @"C:\Fixture\app.exe" } } }))
            {
                settings.Show(); Application.DoEvents();
                ContextMenuStrip first = null;
                for (int i = 0; i < 10; i++)
                {
                    var menu = settings.BuildTaskbarFavouriteMenu();
                    Require(menu != null && menu.Items.Count == 1, "production From taskbar menu contains fixture shortcut");
                    if (first == null) first = menu;
                    Require(ReferenceEquals(first, menu), "From taskbar reuses its menu across cancellations");
                    menu.Show(settings, new Point(40, 40)); Application.DoEvents();
                    menu.Close(i % 2 == 0 ? ToolStripDropDownCloseReason.AppClicked : ToolStripDropDownCloseReason.Keyboard);
                    Application.DoEvents();
                    Require(!menu.IsDisposed && !settings.IsDisposed, "From taskbar cancellation leaves ToolStrip alive after Closed");
                }
                settings.Close();
                Require(first.IsDisposed, "Settings teardown releases From taskbar menu");
            }
        }
    }
}
