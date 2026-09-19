// Pure regression tests for v0.6.2. No native focus changes, app launches or saves.
using System;
using System.Drawing;
using System.Linq;
using System.Text;
namespace TaskbarTiles
{
    static class InterfacePolishTests
    {
        static int checks;
        static void Require(bool ok, string name) { checks++; if (!ok) throw new InvalidOperationException("FAILED: " + name); }
        internal static void Run(StringBuilder log)
        {
            checks = 0;
            var defaults = new Options();
            Require(defaults.WindowTitleFontSize == 22 && defaults.AppLabelFontSize == 22, "both main label defaults are 22");
            Require(defaults.ShowWindowTitleIcons && defaults.WindowTitleIconSize == 28, "title icons default on at 28");
            var upgrade = Options.UpgradeToCurrent(new[] { "ConfigVersion=6", "WindowTitleFontSize=12", "AppLabelFontSize=20", "TileSize=144", "PreviewScale=150", "WindowColumns=6", "WindowRows=2", "HideOnFocusLoss=false", "DirectAppLaunch=false", "TerminalNewWindow=false" });
            Require(upgrade.WindowTitleFontSize == 22 && upgrade.AppLabelFontSize == 22 && upgrade.ConfigVersion == 8, "requested font sizes adopted once");
            Require(upgrade.TileSize == 144 && upgrade.PreviewScale == 150 && upgrade.WindowColumns == 6 && upgrade.WindowRows == 2, "grid and size preferences kept");
            Require(!upgrade.HideOnFocusLoss && !upgrade.DirectAppLaunch && !upgrade.TerminalNewWindow, "other navigation choices kept");
            var again = Options.UpgradeToCurrent(new[] { "ConfigVersion=7", "WindowTitleFontSize=18", "AppLabelFontSize=24", "WindowTitleIconSize=48", "ShowWindowTitleIcons=false" });
            Require(again.WindowTitleFontSize == 18 && again.AppLabelFontSize == 24 && again.WindowTitleIconSize == 48 && !again.ShowWindowTitleIcons, "later edits survive restart and reinstall");
            var bounded = Options.Parse(new[] { "WindowTitleIconSize=999", "WindowTitleFontSize=999", "AppLabelFontSize=0" });
            Require(bounded.WindowTitleIconSize == 64 && bounded.WindowTitleFontSize == 32 && bounded.AppLabelFontSize == 9, "appearance values constrained safely");
            Require(Options.Parse(new[] { "WindowTitleIconSize=0" }).WindowTitleIconSize == 12, "icon minimum enforced");
            Require(SettingsFocusPolicy.IsOutside(true, true, false, true, 200, 100, false), "outside app dismisses enabled session");
            Require(!SettingsFocusPolicy.IsOutside(true, true, false, true, 100, 100, false), "settings and internal preview keep draft open");
            Require(!SettingsFocusPolicy.IsOutside(true, true, false, true, 200, 100, true), "owned shell picker is internal even if brokered");
            Require(!SettingsFocusPolicy.IsOutside(true, true, false, false, 0, 100, false), "null focus is not an outside click");
            Require(!SettingsFocusPolicy.IsOutside(true, false, false, true, 200, 100, false), "opening transition does not dismiss unarmed session");
            Require(!SettingsFocusPolicy.IsOutside(false, true, false, true, 200, 100, false), "saved opt-out respected");
            Require(!SettingsFocusPolicy.IsOutside(true, true, true, true, 200, 100, false), "closing session not dismissed twice");
            int headers = 0;
            foreach (float scale in new[] { .35f, .75f, 1f, 1.25f, 1.5f, 2.25f, 3f })
            foreach (int font in new[] { 9, 12, 22, 32 })
            foreach (int icon in new[] { 12, 28, 48, 64 })
            foreach (bool show in new[] { false, true })
            foreach (bool close in new[] { false, true })
            foreach (int width in new[] { 196, 280, 420, 616 })
            {
                Func<int, int> s = n => Math.Max(1, (int)Math.Round(n * scale));
                var o = new Options { WindowTitleFontSize = font, WindowTitleIconSize = icon, ShowWindowTitleIcons = show, ShowCloseButtons = close };
                var card = new Rectangle(20, 35, s(width), s(Math.Max(164, MenuTextMetrics.MinimumCard(o))));
                var g = WindowHeaderGeometry.Build(card, o, scale);
                Require(card.Contains(g.Title) && card.Contains(g.Close), "header controls inside card");
                Require(g.Title.Height > 0 && g.Title.Width > 0, "title remains visible");
                Require(!close || !g.Title.IntersectsWith(g.Close), "title does not overlap close X");
                if (show)
                {
                    Require(!g.Icon.IsEmpty && card.Contains(g.Icon) && g.Icon.Width == g.Icon.Height, "square icon fits");
                    Require(g.Icon.Right < g.Title.Left && !g.Icon.IntersectsWith(g.Close), "icon before title and clear of X");
                    Require(g.Icon.Width == s(icon), "requested icon size retained at normal card widths");
                    Require(g.Icon.Bottom <= card.Top + s(MenuTextMetrics.TitleBand(o)), "icon never overlaps preview");
                }
                else Require(g.Icon.IsEmpty, "icon hidden when disabled");
                headers++;
            }
            log.AppendLine("PASS: " + headers + " title-header combinations: adjustable icons, 22-pixel default labels, text/close spacing and live-preview shared geometry.");
            log.AppendLine("PASS: one-time font migration, subsequent preference preservation and external-vs-owned focus decision rules.");
            log.AppendLine("v0.6.2 helper assertions: " + checks + ". Native icon loading and focus transitions need Windows integration testing.");
        }
    }
}
