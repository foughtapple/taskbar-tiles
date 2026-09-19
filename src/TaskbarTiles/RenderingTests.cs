using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed partial class Switcher
    {
        static int renderChecks;
        static void RenderRequire(bool ok, string label)
        { renderChecks++; if (!ok) throw new InvalidOperationException("FAILED: " + label); }
        static void AssertFontAlive(Font font)
        { RenderRequire(font != null && font.GetHeight() > 0, "font still owns a live GDI+ resource"); }
        static void DrawFixture(Switcher menu)
        {
            using (var image = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                RenderRequire(image.GetPixel(image.Width / 2, image.Height - 2).ToArgb() != Color.White.ToArgb(), "native repaint is not the white error surface");
            }
        }
        // Explicit Windows test mode. Uses the REAL menu paint/preview/binding code,
        // but the constructor skips hooks, tray, app inventory, launches and settings.
        internal static int RunRenderingRegressionTests()
        {
            var log = new StringBuilder(); renderChecks = 0;
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var control = new Control())
                using (var original = new Font("Segoe UI", 12, GraphicsUnit.Pixel))
                using (var equal = new Font("Segoe UI", 12, GraphicsUnit.Pixel))
                {
                    control.Font = original; control.Font = equal;
                    RenderRequire(ReferenceEquals(control.Font, original), "reproduce WinForms retaining equal old font");
                    original.Dispose(); bool rejected = false;
                    try { control.Font.GetHeight(); } catch (ArgumentException) { rejected = true; }
                    RenderRequire(rejected, "old replacement/disposal sequence leaves invalid font");
                    control.Font = null;
                }
                log.AppendLine("PASS: reproduced the old equal-font disposal defect on .NET Framework.");
                using (var menu = new Switcher(true))
                {
                    menu.options = new Options(); menu.options.ShowLivePreviews = false;
                    menu.options.MaxPanelWidth = 800;
                    menu.area = new Rectangle(0, 0, 1000, 750); menu.scale = 1;
                    for (int n = 0; n < 7; n++) menu.allWindows.Add(new WindowItem { Handle = IntPtr.Zero, Title = "Drawing fixture " + n });
                    using (var icon = new Bitmap(32, 32))
                    {
                        using (var g = Graphics.FromImage(icon)) g.Clear(Color.SteelBlue);
                        menu.allApps.Add(new AppButton { Name = "Fixture", DisplayName = "Fixture", Image = icon });
                        menu.windows = menu.allWindows; menu.apps = menu.allApps;
                        int paints = 0; menu.Paint += delegate { paints++; };
                        for (int i = 0; i < 90; i++)
                        {
                            menu.ResetPaintRecovery(); menu.EnsureMenuFonts(); menu.LayoutMenu();
                            MenuFonts same = menu.menuFonts; menu.EnsureMenuFonts();
                            RenderRequire(ReferenceEquals(same, menu.menuFonts), "unchanged menu reuses owned font set");
                            AssertFontAlive(menu.Font); AssertFontAlive(menu.searchBox.Font);
                            RenderRequire(ReferenceEquals(menu.Font, menu.uiFont) && ReferenceEquals(menu.searchBox.Font, menu.uiFont), "controls reference owned font");
                            DrawFixture(menu);
                            var draft = menu.options.Clone();
                            draft.WindowTitleFontSize = 9 + i % 24; draft.AppLabelFontSize = 32 - i % 24;
                            using (var preview = menu.CaptureMenuPreview(draft)) RenderRequire(preview.Width > 0, "real settings preview rendered");
                            RenderRequire(ReferenceEquals(menu.menuFonts, same), "preview restores live font ownership");
                            AssertFontAlive(menu.Font); AssertFontAlive(menu.searchBox.Font);
                            menu.area = new Rectangle(0, 0, 1000, 750);
                            menu.scale = new[] { 1f, 1.25f, 1.5f }[i % 3];
                            menu.LayoutMenu(); DrawFixture(menu);
                        }
                        RenderRequire(paints >= 270, "native and direct preview painting continue after repeated replacements");
                        log.AppendLine("PASS: 90 real-menu font/layout/preview cycles including equal metrics, independent text sizes and scale changes.");
                        var bad = new Bitmap(16, 16); bad.Dispose();
                        menu.allApps[0].Image = bad;
                        DrawFixture(menu);
                        RenderRequire(!menu.paintFault && menu.rejectedPaintImages.Contains(bad), "disposed icon isolated to its fallback");
                        menu.allApps[0].Image = icon;
                        menu.ResetPaintRecovery(); menu.LayoutMenu(); menu.Show();
                        int faults = 0;
                        PaintEventHandler once = delegate { if (faults++ == 0) throw new ExternalException("Injected rendering fault"); };
                        menu.Paint += once; DrawFixture(menu);
                        RenderRequire(menu.paintFault, "paint boundary catches injected managed failure");
                        menu.Paint -= once;
                        Application.DoEvents(); DrawFixture(menu);
                        RenderRequire(!menu.paintFault && menu.paintRecoveryAttempts == 1, "queued repair succeeds without a process restart");
                        int before = paints; DrawFixture(menu);
                        RenderRequire(paints > before, "WinForms did not latch exception-while-painting state");
                        menu.ResetPaintRecovery();
                        PaintEventHandler always = delegate { throw new ExternalException("Persistent injected fault"); };
                        menu.Paint += always;
                        for (int i = 0; i < 8; i++) { DrawFixture(menu); Application.DoEvents(); }
                        RenderRequire(menu.paintFault && menu.paintRecoveryAttempts == MaximumPaintRecoveries && !menu.paintRecoveryQueued, "persistent paint failure has bounded retry");
                        menu.Paint -= always; menu.RefreshMenuGraphics(); DrawFixture(menu);
                        RenderRequire(!menu.paintFault, "explicit graphics refresh leaves fallback without relaunch");
                        menu.ResetPaintRecovery(); menu.Paint += always; DrawFixture(menu); menu.Paint -= always;
                        menu.Dismiss(); Application.DoEvents();
                        RenderRequire(!menu.Visible && !menu.paintRecoveryQueued, "stale recovery cannot reopen dismissed popup");
                        menu.ResetPaintRecovery(); menu.LayoutMenu(); DrawFixture(menu);
                        RenderRequire(!menu.paintFault, "next explicit open remains paintable");
                    }
                    menu.allApps.Clear(); menu.apps.Clear();
                }
                log.AppendLine("PASS: invalid icon isolation, real WM_PRINT painting, injected failure recovery, bounded retries, manual refresh and dismissal gates.");
                log.AppendLine("Rendering regression assertions: " + renderChecks + ". No user apps, hooks, launches, configuration writes or network calls were used.");
                log.AppendLine("The supplied screenshot identifies a paint-failure symptom; the user's exact exception is not available in this fixture.");
                File.WriteAllText(Path.Combine(Program.Home, "rendering-test.log"), log.ToString()); return 0;
            }
            catch (Exception ex)
            {
                log.AppendLine(ex.ToString());
                try { File.WriteAllText(Path.Combine(Program.Home, "rendering-test.log"), log.ToString()); } catch { }
                return 1;
            }
        }
    }
}
