using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class UiReliabilityTests
    {
        static int checks;
        static void Require(bool ok, string label) { checks++; if (!ok) throw new InvalidOperationException("FAILED: " + label); }
        internal static void Run(StringBuilder log)
        {
            checks = 0;
            for (int mask = 0; mask < 64; mask++)
            {
                bool armed = (mask & 1) != 0, visible = (mask & 2) != 0, enabled = (mask & 4) != 0,
                    disposing = (mask & 8) != 0, inside = (mask & 16) != 0, current = (mask & 32) != 0;
                Require(OutsideClickPolicy.Dismiss(current ? 2 : 1, 2, armed, visible, enabled, disposing, inside) ==
                    (current && armed && visible && enabled && !disposing && !inside), "outside-down session and ownership gate");
            }
            Require(OutsideClickPolicy.IsDown(0x201) && OutsideClickPolicy.IsDown(0x204) && OutsideClickPolicy.IsDown(0x207), "left right middle observed");
            Require(!OutsideClickPolicy.IsDown(0x20B) && !OutsideClickPolicy.IsDown(0x200) && !OutsideClickPolicy.IsDown(0x202), "toggle/move/up not intercepted");
            bool failed = false;
            Require(!RenderSafety.Paint(delegate { throw new ArgumentException("fixture"); }, delegate { failed = true; }) && failed, "drawing failure contained");
            Require(RenderSafety.Paint(delegate { }, delegate { }), "next paint still runs");
            log.AppendLine("PASS: " + checks + " paint-boundary and click-away policy assertions; no input was generated.");
        }
        sealed class PaintFixture : Form
        {
            internal bool FailNext;
            internal Image BadImage;
            internal int Failures, Paints;
            protected override void OnPaint(PaintEventArgs e)
            {
                RenderSafety.Paint(delegate
                {
                    Paints++;
                    if (FailNext) { FailNext = false; throw new InvalidOperationException("injected one-frame failure"); }
                    e.Graphics.Clear(Color.FromArgb(20, 27, 38));
                    RenderSafety.DrawImage(e.Graphics, BadImage, new Rectangle(8, 8, 32, 32));
                    TextRenderer.DrawText(e.Graphics, "Menu remains drawable", Font, ClientRectangle, Color.White);
                }, delegate { Failures++; });
            }
        }
        sealed class NoActivateFixture : Form
        {
            internal int Clicks;
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }
            protected override void WndProc(ref Message m)
            { if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } base.WndProc(ref m); }
            protected override void OnMouseDown(MouseEventArgs e)
            { base.OnMouseDown(e); Clicks++; Text = "TT075 clicks " + Clicks; }
        }
        // Explicit native CI mode only. Run before the tray app's mutex/hooks are created.
        internal static int RunTarget(string[] args)
        {
            if (args.Length != 5) return 2;
            using (var form = new NoActivateFixture())
            using (var expiry = new System.Windows.Forms.Timer { Interval = 45000 })
            {
                form.StartPosition = FormStartPosition.Manual; form.FormBorderStyle = FormBorderStyle.None;
                form.Bounds = new Rectangle(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));
                form.Text = "TT075 clicks 0"; form.TopMost = true; form.ShowInTaskbar = false;
                expiry.Tick += delegate { form.Close(); }; expiry.Start(); Application.Run(form); return 0;
            }
        }
        [StructLayout(LayoutKind.Sequential)] struct MouseInput
        { public int x, y; public uint data, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct Input { public uint type; public MouseInput mouse; }
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] input, int size);
        static void Pump(int milliseconds)
        { var w = Stopwatch.StartNew(); while (w.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); } }
        static IntPtr FindTarget(uint pid)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr h, IntPtr p)
            { if (WindowNative.ProcessId(h) == pid && Native.IsWindowVisible(h)) { var title = new StringBuilder(100); Native.GetWindowText(h, title, title.Capacity); if (title.ToString().StartsWith("TT075 clicks ", StringComparison.Ordinal)) { found = h; return false; } } return true; }, IntPtr.Zero);
            return found;
        }
        static void ClickOnlyFixture(IntPtr window, Point point)
        {
            Cursor.Position = point;
            var root = Native.GetAncestor(Native.WindowFromPoint(new Native.POINT(point.X, point.Y)), 2);
            Require(root == window, "test click is limited to its disposable fixture window");
            var events = new[] { new Input { mouse = new MouseInput { flags = 2 } }, new Input { mouse = new MouseInput { flags = 4 } } };
            Require(SendInput(2, events, Marshal.SizeOf(typeof(Input))) == 2, "native fixture mouse click dispatched");
        }
        internal static int RunNative()
        {
            var log = new StringBuilder(); checks = 0; Process target = null; Point pointer = Cursor.Position;
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var surface = new Bitmap(500, 220))
                using (var g = Graphics.FromImage(surface))
                using (var form = new Form())
                using (var input = new TextBox())
                using (var live = new Font("Segoe UI", 12, GraphicsUnit.Pixel))
                {
                    form.Controls.Add(input); FontBinding.Assign(form, live); FontBinding.Assign(input, live);
                    using (var equal = new Font("Segoe UI", 12, GraphicsUnit.Pixel))
                    {
                        form.Font = equal;
                        log.AppendLine("Observed legacy equal-font assignment retains original reference: " + ReferenceEquals(form.Font, live));
                        FontBinding.Assign(form, live);
                    }
                    for (int i = 0; i < 300; i++)
                    {
                        using (var draft = new Font("Segoe UI", i % 3 == 0 ? 18 : 12, GraphicsUnit.Pixel))
                        {
                            FontBinding.Assign(form, draft); FontBinding.Assign(input, draft);
                            Require(ReferenceEquals(form.Font, draft) && ReferenceEquals(input.Font, draft), "draft owns actual installed font references");
                            TextRenderer.DrawText(g, "Preview", form.Font, Point.Empty, Color.White);
                            FontBinding.Assign(form, live); FontBinding.Assign(input, live);
                        }
                        Require(ReferenceEquals(form.Font, live) && ReferenceEquals(input.Font, live), "no disposed preview font remains installed");
                        TextRenderer.DrawText(g, "Reopened menu", form.Font, Point.Empty, Color.White);
                        IntPtr hfont = input.Font.ToHfont(); DeleteObject(hfont);
                    }
                    var dead = new Bitmap(16, 16); dead.Dispose();
                    Require(!RenderSafety.DrawImage(g, dead, new Rectangle(0, 0, 32, 32)), "disposed optional icon falls back");
                    Require(!RenderSafety.DrawImage(g, dead, new Rectangle(0, 0, 32, 32)), "bad icon is not retried each paint");
                    using (var valid = new Bitmap(16, 16)) Require(RenderSafety.DrawImage(g, valid, new Rectangle(0, 0, 32, 32)), "replacement image is accepted");
                    FontBinding.Assign(input, SystemFonts.MessageBoxFont); FontBinding.Assign(form, SystemFonts.MessageBoxFont);
                }
                using (var paint = new PaintFixture { ShowInTaskbar = false, Size = new Size(300, 180) })
                {
                    paint.Show(); Pump(70); paint.FailNext = true; paint.Invalidate(); paint.Update();
                    int previous = paint.Paints; paint.Invalidate(); paint.Update();
                    Require(paint.Failures == 1 && paint.Paints > previous, "native WM_PAINT continues after injected error; no red-X latch");
                    var dead = new Bitmap(24, 24); dead.Dispose(); paint.BadImage = dead;
                    paint.Invalidate(); paint.Update(); Require(paint.Failures == 1, "bad image cannot abort the form paint"); paint.Hide();
                }
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                var external = new Rectangle(work.Left + 350, work.Top + 80, 220, 170);
                target = Process.Start(new ProcessStartInfo(Application.ExecutablePath,
                    "--test-ui-target " + external.X + " " + external.Y + " " + external.Width + " " + external.Height) { UseShellExecute = false });
                IntPtr h = IntPtr.Zero;
                for (int i = 0; i < 150 && h == IntPtr.Zero; i++) { Pump(20); h = FindTarget((uint)target.Id); }
                Require(h != IntPtr.Zero, "out-of-process nonactivating fixture opened");
                using (var popup = new NoActivateFixture { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    FormBorderStyle = FormBorderStyle.None, Bounds = new Rectangle(work.Left + 40, work.Top + 80, 230, 170) })
                {
                    bool enabled = true; int dismissed = 0;
                    using (var watcher = new PopupClickWatcher(popup, delegate { return enabled; }, delegate { dismissed++; popup.Hide(); }))
                    {
                        Require(watcher.Installed, "real observation hook installed");
                        popup.Show(); Pump(100); Require(Native.GetForegroundWindow() != popup.Handle, "popup is visible without foreground activation");
                        int oldSession = watcher.Session; watcher.Suspend(); watcher.Arm();
                        watcher.Observe(oldSession, h, external.Location); Require(dismissed == 0, "old click cannot dismiss a reopened session");
                        watcher.Observe(watcher.Session, popup.Handle, popup.Location); Require(dismissed == 0, "inside click ignored");
                        using (var owned = new NoActivateFixture { Owner = popup, Size = new Size(100, 70) })
                        { var ownedHandle = owned.Handle; watcher.Observe(watcher.Session, ownedHandle, external.Location); Require(dismissed == 0, "owned popup remains usable"); }
                        enabled = false;
                        ClickOnlyFixture(h, new Point(external.Left + 80, external.Top + 70)); Pump(180);
                        Require(dismissed == 0 && popup.Visible, "click-away preference off is respected");
                        enabled = true;
                        ClickOnlyFixture(h, new Point(external.Left + 80, external.Top + 70)); Pump(220);
                        Require(dismissed == 1 && !popup.Visible, "outside click dismisses a popup that was never active");
                        var title = new StringBuilder(100); Native.GetWindowText(h, title, title.Capacity);
                        Require(title.ToString() == "TT075 clicks 2", "both clicks reached the other app unchanged");
                        watcher.Observe(watcher.Session, h, external.Location); Require(dismissed == 1, "hidden popup stays dismissed");
                        popup.Show(); Pump(70); ClickOnlyFixture(popup.Handle, new Point(popup.Left + 80, popup.Top + 70)); Pump(100);
                        Require(popup.Visible && dismissed == 1, "real inside click is not dismissed");
                        watcher.Suspend(); popup.Hide();
                    }
                }
                log.AppendLine("PASS: " + checks + " native font/preview/paint-fault/click-away assertions, including 300 font preview/reopen cycles.");
                log.AppendLine("Disposable test windows only. Not a reproduction of the reporter's desktop, display driver or exact original exception.");
                return 0;
            }
            catch (Exception ex) { log.AppendLine(ex.ToString()); return 1; }
            finally
            {
                if (target != null) try { if (!target.HasExited) { var h = FindTarget((uint)target.Id); if (h != IntPtr.Zero) WindowNative.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero); if (!target.WaitForExit(2000)) target.Kill(); } target.Dispose(); } catch { }
                Cursor.Position = pointer;
                File.WriteAllText(Path.Combine(Program.Home, "ui-reliability-test.log"), log.ToString());
            }
        }
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    }
}
