// Taskbar Tiles 0.6 - pure launcher/option/geometry tests, run before installation.
// Does not open apps, inject keys, scan installed apps, create settings or modify windows.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace TaskbarTiles
{
    static class LauncherTests
    {
        static int checks;
        static void Require(bool pass, string name)
        { checks++; if (!pass) throw new InvalidOperationException("FAILED: " + name); }
        static void Reject(Action test, string name)
        {
            bool rejected = false;
            try { test(); } catch (InvalidDataException) { rejected = true; } catch (ArgumentException) { rejected = true; }
            Require(rejected, name);
        }
        internal static void Run(StringBuilder log)
        {
            checks = 0;
            var defaults = new Options();
            Require(defaults.TileSize == 120 && defaults.PreviewScale == 120, "120px apps and 120% previews preserved");
            Require(defaults.WindowsSearchButton && defaults.FavouritesButton && defaults.LiveSettingsPreview, "new requested features default enabled");
            Require(defaults.DesktopButton && !defaults.ClipboardButton && !defaults.MiddleClickClose, "optional intrusive actions off by default");
            var old = Options.Parse(new[] { "ConfigVersion=3", "TileSize=136", "PreviewScale=170", "InterceptAltTab=false", "FavouriteWidth=540", "ClipboardButton=true" });
            Require(old.TileSize == 136 && old.PreviewScale == 170 && !old.InterceptAltTab, "upgraded preferences not silently reset");
            Require(old.FavouriteWidth == 540 && old.ClipboardButton, "new preference fields parsed");
            var bad = Options.Parse(new[] { "FooterButtonHeight=900", "FavouriteWidth=1", "FavouriteRowHeight=0", "FavouriteVisibleRows=100" });
            Require(bad.FooterButtonHeight == 64 && bad.FavouriteWidth == 360 && bad.FavouriteRowHeight == 40 && bad.FavouriteVisibleRows == 18, "new size limits enforced");
            var clone = defaults.Clone(); clone.TileSize = 256; clone.FavouritesButton = false;
            Require(defaults.TileSize == 120 && defaults.FavouritesButton, "preview draft cannot mutate live options");

            var entries = new List<FavouriteEntry> {
                new FavouriteEntry { Id="work-a", Name="Editor - work", Target=@"C:\Apps With Spaces\editor.exe", Arguments="--profile \"Work & notes\"", Group="Work", AppId="Example.Profile.Work" },
                new FavouriteEntry { Id="work-b", Name="Editor - personal", Target=@"C:\Apps With Spaces\editor.exe", Arguments="--profile personal", Group="Home" },
                new FavouriteEntry { Id="web", Name="Caf\u00e9 \u2605", Target="https://example.invalid/path?a=1&b=2", Group="Research" },
                new FavouriteEntry { Id="hidden", Name="Hidden tool", Target="%WINDIR%\\notepad.exe", Enabled=false, Group="Work" }
            };
            string json = FavouriteStore.Serialise(entries);
            var copy = FavouriteStore.Parse(json);
            Require(copy.Count == 4 && copy[2].Name == entries[2].Name, "Unicode JSON round trip");
            Require(copy[0].Arguments == entries[0].Arguments && !copy[3].Enabled && copy[0].AppId == entries[0].AppId, "arguments, identity and disabled state round trip");
            copy[0].Name = "Changed draft";
            Require(entries[0].Name == "Editor - work", "favourites draft owns its entries");
            Require(FavouriteStore.Filter(entries, "editor work", "", false).Single().Id == "work-a", "multi-word filtering");
            Require(FavouriteStore.Filter(entries, "", "WORK", false).Single().Id == "work-a", "case-insensitive groups exclude disabled items");
            Require(FavouriteStore.Filter(entries, "", "", false).Select(e => e.Id).SequenceEqual(new[] { "work-a", "work-b", "web" }), "custom order preserved");
            Require(FavouriteStore.Filter(entries, "", "", true).First().Id == "web", "optional alphabetical order");
            Require(entries[2].Detail == "example.invalid", "website detail uses host without navigation");
            Require(FavouriteStore.Merge(entries, entries).Count == 4, "exact duplicate import skipped");
            var variant = entries[0].Clone(); variant.Name = "Another profile shortcut";
            var merged = FavouriteStore.Merge(entries, new[] { variant });
            Require(merged.Count == 5 && merged[4].Id != merged[0].Id, "same executable may retain multiple named profiles");
            Require(merged[0].Name == entries[0].Name, "merge does not mutate source collection");
            var duplicateIds = entries.Take(2).Select(e => e.Clone()).ToList(); duplicateIds[1].Id = duplicateIds[0].Id;
            var unique = FavouriteStore.Parse(FavouriteStore.Serialise(duplicateIds));
            Require(unique.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2, "duplicate imported IDs repaired");
            Reject(delegate { FavouriteStore.Parse("{}"); }, "unrelated JSON cannot masquerade as an empty export");
            Reject(delegate { FavouriteStore.Parse("{\"Version\":99,\"Items\":[]}"); }, "unknown format rejected");
            Reject(delegate { FavouriteStore.Parse("{\"Version\":1,\"Items\":[null]}"); }, "null item rejected");
            Reject(delegate { FavouriteStore.Serialise(new[] { new FavouriteEntry { Name="Bad", Target="javascript:alert(1)" } }); }, "script URI rejected");
            Reject(delegate { FavouriteStore.Serialise(new[] { new FavouriteEntry { Name="Bad", Target="data:text/plain,something" } }); }, "data URI rejected");
            Reject(delegate { FavouriteStore.Serialise(new[] { new FavouriteEntry { Name="Bad\nname", Target="notepad.exe" } }); }, "multiline shortcut rejected");
            Reject(delegate { FavouriteStore.Serialise(Enumerable.Range(0, 501).Select(n => new FavouriteEntry { Name="App " + n, Target="notepad.exe" })); }, "collection cap enforced");
            foreach (var item in FavouriteEntry.Defaults()) Require(item.ValidateEntry() == null, "built-in favourite validates");
            var info = FavouriteLaunch.StartInfo(entries[0].Clone());
            Require(info.FileName == entries[0].Target && info.Arguments == entries[0].Arguments, "filename and arguments kept separate");
            Require(info.UseShellExecute && string.IsNullOrEmpty(info.Verb), "normal shell launch; no automatic elevation verb");
            Require(FavouriteLaunch.StartInfo(entries[2].Clone()).FileName == entries[2].Target, "website target retained without opening it");
            var app = FavouriteLaunch.AsApp(entries[0]);
            Require(app.Favourite != entries[0] && app.AppId == entries[0].AppId && app.LaunchExe == entries[0].Target, "favourite adapts into existing zone placement identity");
            Require(WindowInventory.Matches(app, new WindowRecord { AppId=entries[0].AppId, Exe=entries[0].Target }), "matching app/profile is eligible for placement");
            Require(!WindowInventory.Matches(app, new WindowRecord { AppId="Different.Profile", Exe=entries[0].Target }), "another profile is not guessed for placement");

            int layouts = 0;
            foreach (float scale in new[] { .5f, .65f, 1f, 1.25f, 1.5f, 2f, 3f })
                foreach (int logicalWidth in new[] { 492, 640, 1000, 1500, 2500 })
                    foreach (int height in new[] { 32, 40, 64 })
                        for (int flags = 0; flags < 16; flags++)
                        {
                            var o = new Options { FooterButtonHeight=height, WindowsSearchButton=(flags&1)!=0, FavouritesButton=(flags&2)!=0, DesktopButton=(flags&4)!=0, ClipboardButton=(flags&8)!=0 };
                            int width = (int)Math.Round(logicalWidth * scale), panelHeight = (int)Math.Round(600 * scale);
                            var layout = QuickAccessLayout.Build(width, panelHeight, scale, o);
                            var buttons = layout.Buttons().ToList(); var bounds = new Rectangle(0, 0, width, panelHeight);
                            Require(QuickAccessLayout.Enabled(o) == (flags != 0), "disabled action bar takes no space");
                            Require(buttons.All(r => r.Width > 0 && r.Height > 0 && bounds.Contains(r)), "all action buttons inside main window");
                            for (int i=0; i<buttons.Count; i++)
                                for (int j=i+1; j<buttons.Count; j++) Require(!buttons[i].IntersectsWith(buttons[j]), "action buttons do not overlap");
                            foreach (var button in buttons) Require(layout.Hit(new Point(button.Left+button.Width/2, button.Top+button.Height/2)) <= -12, "action button centre hit-tests");
                            if (!layout.Search.IsEmpty) Require(layout.Hit(new Point(layout.Search.Left+1, layout.Search.Top+1)) == -12, "left Search route");
                            if (!layout.Favourites.IsEmpty) Require(layout.Hit(new Point(layout.Favourites.Left+1, layout.Favourites.Top+1)) == -13, "right Favourites route");
                            Require(layout.Hit(new Point(-100, -100)) == -100, "outside quick action bounds inert");
                            layouts++;
                        }
            int palettes = 0;
            foreach (var work in new[] { new Size(640, 440), new Size(1280, 680), new Size(1920, 1040), new Size(5120, 1400) })
                foreach (float dpi in new[] { 1f, 1.25f, 1.5f, 2f, 3f })
                    foreach (int rowHeight in new[] { 40, 56, 90 })
                        foreach (int preferredRows in new[] { 3, 8, 18 })
                            foreach (int count in new[] { 0, 1, 3, 8, 20, 500 })
                            {
                                var o = new Options { FavouriteRowHeight=rowHeight, FavouriteVisibleRows=preferredRows, FavouriteWidth=850 };
                                float scale = FavouriteGeometry.ScaleFor(work, dpi);
                                var size = FavouriteGeometry.PopupSize(o, count, work, scale);
                                int capacity = FavouriteGeometry.PageSize(o, size.Height, scale);
                                Require(size.Width > 0 && size.Width <= work.Width && size.Height > 0 && size.Height <= work.Height, "favourites popup fits work area");
                                Require(capacity > 0, "favourites always retains one usable row");
                                Func<int,int> s = n => FavouriteGeometry.Scaled(n, scale);
                                int shown = Math.Min(capacity, count);
                                if (shown > 0)
                                {
                                    int lastBottom = s(99) + (shown-1) * (s(rowHeight)+s(6)) + s(rowHeight);
                                    Require(lastBottom <= size.Height-s(40)-1, "rows cannot cover the launcher footer");
                                }
                                palettes++;
                            }
            log.AppendLine("PASS: " + palettes + " favourites palette sizes and paging cases; settings preview shares this layout helper.");
            log.AppendLine("PASS: favourites JSON, filtering, grouping, ordering, identity, explicit launch descriptors and setting defaults.");
            log.AppendLine("PASS: " + layouts + " bottom action-bar layouts across flags, sizes and DPI scales.");
            log.AppendLine("Launcher checks passed: " + checks + ". No apps, Windows shortcuts or settings files were activated by these checks.");
        }
    }
}
