// Installer tests: pure code only; no app launches, filesystem indexing or UI side effects.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace TaskbarTiles
{
    static class SearchPageTests
    {
        static int assertions;
        static void Require(bool pass, string name) { assertions++; if (!pass) throw new InvalidOperationException("FAILED: " + name); }
        internal static void Run(StringBuilder log)
        {
            assertions = 0;
            var parsed = Options.Parse(new[] { "ConfigVersion=5", "WindowColumns=6", "WindowRows=2", "LockWindowPageSize=true", "WindowsPerPage=3", "PreviewScale=150", "WindowsSearchButton=false", "SearchIndexedFiles=false", "WindowTitleFontSize=20", "AppLabelFontSize=18" });
            Require(parsed.WindowColumns == 6 && parsed.WindowRows == 2 && parsed.PreviewScale == 150, "explicit old size/row/column settings retained; obsolete count ignored");
            Require(parsed.WindowTitleFontSize == 20 && parsed.AppLabelFontSize == 18, "independent text sizes parsed");
            Require(!parsed.WindowsSearchButton && !parsed.SearchIndexedFiles, "search preferences retained");
            var limits = Options.Parse(new[] { "WindowColumns=999", "WindowRows=999", "SearchPanelWidth=9999", "SearchVisibleRows=0", "WindowTitleFontSize=0", "AppLabelFontSize=999" });
            Require(limits.WindowColumns == 12 && limits.WindowRows == 3 && limits.SearchPanelWidth == 1100 && limits.SearchVisibleRows == 3 && limits.WindowTitleFontSize == 9 && limits.AppLabelFontSize == 32, "settings limits enforced");
            var catalog = new[] {
                SearchLogic.FromEntry(new FavouriteEntry { Name = "Bambu Studio", Target = "bambu.exe" }, "Apps", "Installed app"),
                SearchLogic.FromEntry(new FavouriteEntry { Name = "Work browser", Target = "chrome.exe", Arguments = "--profile work" }, "Favourites", "Work"),
                new SearchItem { Name = "Bambu setup guide", Kind = "Windows", Detail = "Open window", Window = new WindowItem { Handle = new IntPtr(42) } },
                SearchLogic.FromEntry(new FavouriteEntry { Name = "Hidden.example", Target = @"C:\Files\example.txt" }, "Files", @"C:\Files\example.txt")
            };
            Require(SearchLogic.Filter(catalog, "bambu", "All").Count == 2, "apps and window titles searchable");
            Require(SearchLogic.Filter(catalog, "work brow", "All").Single().Name == "Work browser", "multiword and partial search");
            Require(SearchLogic.Filter(catalog, "bambu", "Apps").Single().Name == "Bambu Studio", "category filter");
            Require(SearchLogic.Filter(catalog, "bambu studio", "All").First().Name == "Bambu Studio", "exact names ranked first");
            Require(SearchLogic.Filter(catalog.Concat(catalog), "", "All").Count == 4, "identity deduplication");
            Require(SearchLogic.FileQuery("") == "", "empty index query not generated");
            Require(SearchLogic.SqlLikeLiteral("50%_[x] O'Brien") == "50[%][_][[]x] O''Brien", "quotes and wildcard literals escaped");
            string sql = SearchLogic.FileQuery("report 2026");
            Require(sql.StartsWith("SELECT TOP 100 ") && sql.Contains("FROM SystemIndex") && sql.Contains("LIKE '%report%'") && sql.Contains("LIKE '%2026%'"), "bounded local filename-only SQL");
            Require(SearchLogic.Words(string.Join(" ", Enumerable.Repeat("word", 100).ToArray())).Length == 12, "query complexity bounded");
            foreach (var f in typeof(Options).GetFields().Where(x => x.FieldType == typeof(bool)))
                Require(!SettingsHelp.For(f.Name).StartsWith("Change this preference"), "specific help for " + f.Name);
            Require(SettingsHelp.Action("Apply").Contains("save") || SettingsHelp.Action("Apply").Contains("Save"), "apply semantics explained");

            log.AppendLine("PASS: search ranking, source filters, literal escaping, query limits, option migration compatibility and boolean hover help.");
            log.AppendLine("Search/page helper assertions: " + assertions + ". No installed apps or Windows index were queried by these checks.");
        }
    }
}
