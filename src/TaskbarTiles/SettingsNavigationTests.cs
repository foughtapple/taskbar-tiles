// Settings navigation regression coverage. Pure checks plus a real WinForms paint.
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class SettingsNavigationTests
    {
        static int checks;
        static void Require(bool ok, string name)
        { checks++; if (!ok) throw new InvalidOperationException("FAILED: " + name); }

        internal static void Run(StringBuilder log)
        {
            checks = 0;
            var pages = new[] {
                "Appearance","Navigation","Screens & zones","Monitor layouts","Startup & tools","Updates",
                "Stream Dock","Quick access","Favourites","Search","Recent apps","Touch screen monitor support","Shortcut health"
            };
            var ordered = SettingsNavigationModel.OrderedPages(pages).ToArray();
            Require(ordered.Length == pages.Length, "every settings page appears exactly once in navigation");
            Require(ordered.Distinct(StringComparer.OrdinalIgnoreCase).Count() == pages.Length, "settings navigation has no duplicate pages");
            Require(ordered.Take(3).SequenceEqual(new[] { "Appearance","Navigation","Quick access" }), "general pages are grouped first");
            Require(Array.IndexOf(ordered,"Favourites") < Array.IndexOf(ordered,"Screens & zones"), "launchers are grouped before display/input");
            Require(Array.IndexOf(ordered,"Stream Dock") > Array.IndexOf(ordered,"Touch screen monitor support"), "integrations are grouped after display/input");

            using (var tabs = new HeaderlessSettingsTabs())
            {
                Require(SettingsNavigationModel.Groups.SelectMany(g => g.Pages).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 13, "navigation model covers every expected settings page exactly once");
                Require(tabs.Appearance == TabAppearance.FlatButtons, "native white tab styling is not used");
                Require(tabs.DrawMode == TabDrawMode.OwnerDrawFixed, "hidden tab strip is owner-drawn");
                Require(tabs.ItemSize.Height <= 1 && tabs.ItemSize.Width <= 1, "native tab strip is collapsed to a one-pixel host");
            }
            log.AppendLine("PASS: dark sidebar settings navigation groups every page once and suppresses the native white tab strip.");
            log.AppendLine("Settings navigation helper assertions: " + checks + ".");
        }

        internal static int RunNative()
        {
            var log = new StringBuilder();
            try
            {
                Run(log);
                using (var form = new SettingsWindow(new Options(), delegate(Options o) { }))
                {
                    form.Show(); Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
                    Require(form.SettingsNavigationReady, "visible settings window has one dark navigation button per page");
                    using (var image = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                        string path = Path.Combine(Program.Home, "settings-navigation-test.png");
                        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    form.Close();
                }
                log.AppendLine("PASS: real Settings window painted with the dark navigation rail.");
                File.WriteAllText(Path.Combine(Program.Home, "settings-navigation-test.log"), log.ToString());
                return 0;
            }
            catch (Exception ex)
            {
                log.AppendLine(ex.ToString());
                try { File.WriteAllText(Path.Combine(Program.Home, "settings-navigation-test.log"), log.ToString()); } catch { }
                return 1;
            }
        }
    }
}
