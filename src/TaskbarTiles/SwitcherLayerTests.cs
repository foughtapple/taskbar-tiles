using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class SwitcherLayerTests
    {
        static int checks;
        static void Require(bool ok, string label)
        { checks++; if (!ok) throw new InvalidOperationException("FAILED: " + label); }
        internal static void Run(StringBuilder log)
        {
            checks = 0;
            foreach (bool presenting in new[] { false, true })
                foreach (bool visible in new[] { false, true })
                    foreach (bool allowed in new[] { false, true })
                        foreach (bool disposed in new[] { false, true })
                            Require(SwitcherLayerPolicy.CanRaise(presenting, visible, allowed, disposed) ==
                                (presenting && visible && allowed && !disposed), "presentation gate");
            uint flags = SwitcherLayerPolicy.RaiseFlags;
            Require((flags & 0x0010) != 0, "maintenance never activates another window");
            Require((flags & (0x0001 | 0x0002)) == (0x0001 | 0x0002), "maintenance does not move or resize");
            Require((flags & 0x0200) != 0, "owner ordering is preserved");
            Require((flags & (0x0004 | 0x0040 | 0x0080 | 0x4000)) == 0, "no no-z-order/show/hide/late async requests");
            log.AppendLine("PASS: " + checks + " switcher topmost lifetime/flag assertions; no desktop changes in helper tests.");
        }
        static bool Above(IntPtr first, IntPtr second)
        {
            IntPtr current = Native.GetWindow(second, 3);
            for (int i = 0; i < 512 && current != IntPtr.Zero; i++)
            { if (current == first) return true; current = Native.GetWindow(current, 3); }
            return false;
        }
        static void PlaceTop(Form form)
        { Require(WindowNative.SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, SwitcherLayerPolicy.RaiseFlags), "arrange competing topmost test window"); }
        static Form TestForm(string title, bool topmost, Rectangle bounds)
        {
            return new Form { Text = title, TopMost = topmost, ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Bounds = bounds, FormBorderStyle = FormBorderStyle.FixedToolWindow };
        }
        // Explicit CI mode only. These are disposable test windows, not the tray app.
        // No hooks, launches, settings writes, other-app moves, or synthetic keyboard input.
        internal static int RunNative()
        {
            var log = new StringBuilder(); checks = 0;
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                var bounds = new Rectangle(work.Left + 30, work.Top + 30, 300, 160);
                bool allowed = true;
                using (var normal = TestForm("Layer test - normal", false, bounds))
                using (var popup = TestForm("Layer test - popup", true, bounds))
                using (var competitor = TestForm("Layer test - competing topmost", true, bounds))
                using (var layer = new SwitcherLayer(popup, delegate { return allowed; }))
                {
                    normal.Show(); layer.Begin(); popup.Show(); competitor.Show();
                    PlaceTop(competitor);
                    Require(Above(competitor.Handle, popup.Handle), "reproduce cached TopMost popup covered by another topmost window");
                    Require(SwitcherLayerPolicy.NeedsRepair(popup.Handle), "detect overlapping window above popup");
                    IntPtr foreground = Native.GetForegroundWindow();
                    Rectangle popupBefore = popup.Bounds, competitorBefore = competitor.Bounds;
                    Require(layer.RaiseNow(), "explicit topmost repair succeeds");
                    Require(Above(popup.Handle, competitor.Handle), "popup restored above competing topmost window");
                    Require(Native.GetForegroundWindow() == foreground, "raising preserves current keyboard foreground");
                    Require(popup.Bounds == popupBefore && competitor.Bounds == competitorBefore, "popup and competitor geometry unchanged");
                    Require(SwitcherLayerPolicy.HasTopmostStyle(competitor.Handle) && !SwitcherLayerPolicy.HasTopmostStyle(normal.Handle), "other windows keep their original topmost status");
                    Require(WindowNative.SetWindowPos(popup.Handle, new IntPtr(-2), 0, 0, 0, 0, SwitcherLayerPolicy.RaiseFlags), "simulate lost native topmost style");
                    Require(popup.TopMost && !SwitcherLayerPolicy.HasTopmostStyle(popup.Handle), "reproduce stale WinForms TopMost cache");
                    Require(layer.RaiseNow() && SwitcherLayerPolicy.HasTopmostStyle(popup.Handle), "repair native style despite true cached property");
                    for (int cycle = 0; cycle < 12; cycle++)
                    {
                        layer.Suspend(); popup.Hide(); PlaceTop(competitor);
                        Require(!layer.RaiseNow() && !popup.Visible, "hidden popup cannot reappear from layer callback");
                        layer.Begin(); popup.Show(); PlaceTop(competitor);
                        Require(layer.RaiseNow() && Above(popup.Handle, competitor.Handle), "repeated open repairs head of topmost band");
                    }
                    allowed = false; PlaceTop(competitor);
                    Require(!layer.RaiseNow() && Above(competitor.Handle, popup.Handle), "activation/modal gate prevents reassertion");
                    allowed = true; layer.Suspend();
                    Require(!layer.RaiseNow() && Above(competitor.Handle, popup.Handle), "suspend before selected-window handoff prevents fighting activation");
                    layer.Begin(); layer.RaiseNow();
                    using (var child = TestForm("Layer test - owned dialog", true, bounds))
                    {
                        child.Show(popup);
                        Require(Above(child.Handle, popup.Handle), "owned dialog initially above popup");
                        Require(!layer.RaiseNow() && Above(child.Handle, popup.Handle), "parent does not cover its own dialog");
                    }
                    layer.Suspend(); popup.Hide();
                    Require(!layer.RaiseNow(), "post-dismiss callback cannot raise a hidden popup");
                }
                log.AppendLine("PASS: " + checks + " real Win32/Form Z-order checks on disposable test windows.");
                log.AppendLine("Covered-topmost, stale native style, twelve hide/reopen cycles, no-focus-steal, modal and handoff gates passed.");
                log.AppendLine("Not a test of the user's desktop, exclusive fullscreen games, secure desktop or every third-party overlay.");
                File.WriteAllText(Path.Combine(Program.Home, "switcher-layer-test.log"), log.ToString());
                return 0;
            }
            catch (Exception ex)
            {
                log.AppendLine(ex.ToString());
                try { File.WriteAllText(Path.Combine(Program.Home, "switcher-layer-test.log"), log.ToString()); } catch { }
                return 1;
            }
        }
    }
}
