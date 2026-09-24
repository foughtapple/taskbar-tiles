// Inventory is not a click target. Hidden taskbar entries remain discoverable;
// only the separate, freshly revalidated click fallback requires screen geometry.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;

namespace TaskbarTiles
{
    sealed class LauncherKey
    {
        public string AppId = "", Exe = "", Target = "", Arguments = "";
        internal static LauncherKey FromEntry(FavouriteEntry entry, bool resolve)
        {
            var key = new LauncherKey { AppId = LaunchIdentity.CleanId(entry.AppId), Target = entry.ExpandedTarget, Arguments = entry.Arguments ?? "" };
            if (key.Target.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase) && key.AppId.Length == 0)
                key.AppId = LaunchIdentity.CleanId(key.Target);
            if (key.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) key.Exe = key.Target;
            if (resolve && key.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(key.Target))
            {
                var receipt = new LaunchReceipt { ExpectedAppId = key.AppId };
                LaunchResolution.ResolveShellTarget(key.Target, receipt);
                key.AppId = receipt.ExpectedAppId; key.Exe = receipt.ExpectedExe;
                string args = ShellIcons.FileProperty(key.Target, "System.Link.Arguments");
                if (key.Arguments.Length == 0) key.Arguments = args ?? "";
            }
            return key;
        }
        internal static LauncherKey FromApp(AppButton app, bool resolve)
        {
            if (app.Favourite != null)
            {
                var key = FromEntry(app.Favourite, resolve);
                if (key.Exe.Length == 0) key.Exe = app.LaunchExe ?? "";
                return key;
            }
            var result = new LauncherKey { AppId = LaunchIdentity.CleanId(app.AppId), Exe = app.LaunchExe ?? "" };
            if (app.VerifiedShortcut && !string.IsNullOrWhiteSpace(app.ShortcutPath))
            {
                result = FromEntry(new FavouriteEntry { AppId = app.AppId, Target = app.ShortcutPath }, resolve);
                if (result.Exe.Length == 0) result.Exe = app.LaunchExe ?? "";
            }
            else if (LaunchIdentity.FullPath(result.AppId)) { result.Exe = result.AppId; result.Target = result.AppId; }
            return result;
        }
        internal static bool Same(LauncherKey a, LauncherKey b)
        {
            if (a == null || b == null) return false;
            string ai = LaunchIdentity.CleanId(a.AppId), bi = LaunchIdentity.CleanId(b.AppId);
            if (ai.Length > 0 && ai.Equals(bi, StringComparison.OrdinalIgnoreCase)) return true;
            if (LaunchIdentity.ConflictingIds(ai, bi)) return false;
            if (!string.IsNullOrWhiteSpace(a.Target) && LaunchIdentity.SamePath(a.Target, b.Target) &&
                string.Equals(a.Arguments ?? "", b.Arguments ?? "", StringComparison.Ordinal)) return true;
            if (LaunchResolution.ProfileScoped(ai) || LaunchResolution.ProfileScoped(bi)) return false;
            return LaunchIdentity.SamePath(a.Exe, b.Exe) && string.Equals(a.Arguments ?? "", b.Arguments ?? "", StringComparison.Ordinal);
        }
    }
    static class LauncherDescriptor
    {
        internal static AppButton Copy(AppButton app)
        {
            return new AppButton { Id = app.Id, Name = app.Name, DisplayName = app.DisplayName, ClassName = app.ClassName,
                Taskbar = app.Taskbar, Bounds = app.Bounds, LaunchExe = app.LaunchExe, ShortcutPath = app.ShortcutPath,
                VerifiedShortcut = app.VerifiedShortcut, Favourite = app.Favourite == null ? null : app.Favourite.Clone(),
                LauncherIdentity = app.LauncherIdentity };
        }
        internal static bool ApplicationTarget(string target)
        {
            target = (target ?? "").Trim();
            return target.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase) ||
                target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        }
        internal static FavouriteEntry FromApp(AppButton app)
        {
            if (app.Favourite != null) return ApplicationTarget(app.Favourite.ExpandedTarget) ? app.Favourite.Clone() : null;
            string target = app.VerifiedShortcut ? app.ShortcutPath : "";
            if (string.IsNullOrWhiteSpace(target))
            {
                if (LaunchIdentity.FullPath(app.AppId) && File.Exists(app.AppId)) target = app.AppId;
                else if (LaunchResolution.ExplicitId(app.AppId) && ShellIcons.CanResolve(@"shell:AppsFolder\" + app.AppId)) target = @"shell:AppsFolder\" + app.AppId;
            }
            if (!ApplicationTarget(target)) return null;
            return new FavouriteEntry { Id = StableId(target, app.AppId), Name = app.DisplayName ?? app.Name ?? "App", Target = target, AppId = app.AppId, Group = "Recent app" };
        }
        internal static string StableId(string target, string appId)
        {
            // An inventory refresh must not manufacture new GUIDs and invalidate a
            // click-in-progress on an otherwise unchanged fallback row.
            return "inventory:" + (target ?? "").Replace('/', '\\').ToUpperInvariant() + "|" + (appId ?? "").ToUpperInvariant();
        }
        internal static FavouriteEntry FromWindow(WindowRecord window)
        {
            if (window == null || window.IdentityAmbiguous) return null;
            var declared = WindowRelaunch.Read(window);
            if (declared != null) return declared;
            var client = SteamReopen.FromWindow(window);
            if (client != null) return client;
            string exe = window.Exe ?? "", id = LaunchIdentity.CleanId(window.AppId), target = "", name = "";
            if (LaunchResolution.ExplicitId(id) && ShellIcons.CanResolve(@"shell:AppsFolder\" + id))
            {
                target = @"shell:AppsFolder\" + id;
                name = ShellIcons.FileProperty(target, "System.ItemNameDisplay");
            }
            else if (!LaunchResolution.ProfileScoped(id) && LaunchIdentity.FullPath(exe) &&
                exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !LaunchIdentity.GenericHost(exe) && File.Exists(exe)) target = exe;
            if (target.Length == 0) return null;
            if (string.IsNullOrWhiteSpace(name) && File.Exists(exe))
                try { name = FileVersionInfo.GetVersionInfo(exe).FileDescription; } catch { }
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(exe);
            if (string.IsNullOrWhiteSpace(name)) return null;
            return new FavouriteEntry { Id = StableId(target, id), Name = name, Target = target, AppId = id, IconPath = target, Group = "Recent app" };
        }
    }
    static class TaskbarScanPolicy
    {
        internal static bool KnownApp(string id, string cls, bool legacy, string name)
        {
            return !string.IsNullOrWhiteSpace(name) && (legacy || (id ?? "").StartsWith("Appid:", StringComparison.OrdinalIgnoreCase) ||
                (cls ?? "").IndexOf("TaskListButton", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        internal static bool Accept(bool inventory, bool offscreen, bool enabled, Rectangle bounds, Rectangle desktop)
        { return inventory || (!offscreen && enabled && bounds.Width >= 12 && bounds.Height >= 12 && desktop.IntersectsWith(bounds)); }
        internal static Rectangle Bounds(System.Windows.Rect rect)
        {
            if (rect.IsEmpty || double.IsNaN(rect.X) || double.IsNaN(rect.Y) || double.IsNaN(rect.Width) || double.IsNaN(rect.Height) ||
                double.IsInfinity(rect.X) || double.IsInfinity(rect.Y) || double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height) ||
                Math.Abs(rect.X) > 1000000 || Math.Abs(rect.Y) > 1000000 || rect.Width < 1 || rect.Width > 100000 || rect.Height < 1 || rect.Height > 100000) return Rectangle.Empty;
            return new Rectangle((int)Math.Round(rect.X), (int)Math.Round(rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
        }
    }
    static class TaskbarFallback
    {
        internal static List<AppButton> Read(IEnumerable<AppButton> previous)
        {
            var pins = new List<AppButton>();
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (Directory.Exists(folder))
                foreach (string path in Directory.GetFiles(folder, "*.lnk").OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).Take(256))
                {
                    var entry = new FavouriteEntry { Name = Path.GetFileNameWithoutExtension(path), Target = path, Group = "Taskbar" };
                    var key = LauncherKey.FromEntry(entry, true); entry.AppId = key.AppId; entry.Id = LauncherDescriptor.StableId(path, key.AppId);
                    var app = FavouriteLaunch.AsApp(entry); app.ClassName = "TaskbarPinnedShortcut"; app.VerifiedShortcut = true; app.LaunchExe = key.Exe; app.LauncherIdentity = key; pins.Add(app);
                }
            var running = new List<AppButton>();
            foreach (var window in WindowInventory.Read())
            {
                var entry = LauncherDescriptor.FromWindow(window); if (entry == null) continue;
                var app = FavouriteLaunch.AsApp(entry);
                app.LauncherIdentity = LauncherKey.FromEntry(entry, true);
                app.LaunchExe = app.LauncherIdentity.Exe; // Keep the app relauncher, not its UI helper.
                running.Add(app);
            }
            return Merge(previous, pins, running);
        }
        internal static List<AppButton> Merge(IEnumerable<AppButton> previous, IEnumerable<AppButton> pins, IEnumerable<AppButton> running)
        {
            var pool = new List<AppButton>();
            foreach (var item in pins.Concat(running))
            {
                var key = item.LauncherIdentity ?? LauncherKey.FromApp(item, false);
                if (!pool.Any(a => LauncherKey.Same(a.LauncherIdentity ?? LauncherKey.FromApp(a, false), key))) pool.Add(item);
            }
            var result = new List<AppButton>();
            foreach (var old in previous ?? new AppButton[0])
            {
                var key = old.LauncherIdentity ?? LauncherKey.FromApp(old, false);
                var match = pool.FirstOrDefault(a => LauncherKey.Same(a.LauncherIdentity ?? LauncherKey.FromApp(a, false), key));
                if (match != null) { result.Add(match); pool.Remove(match); }
            }
            result.AddRange(pool); return result;
        }
    }
}
