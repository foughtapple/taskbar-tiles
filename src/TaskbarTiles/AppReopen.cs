// Reopen application UI rather than executing a renderer/helper in isolation.
// App-authored relaunch metadata is preferred. Steam's explicit open-main route
// is narrowly scoped to its registered installation, never a friendly-name match.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace TaskbarTiles
{
    static class WindowRelaunch
    {
        internal static bool TryParse(string command, string appId, string displayResource, out FavouriteEntry entry)
        {
            entry = null;
            if (!LaunchResolution.ExplicitId(appId) || string.IsNullOrWhiteSpace(displayResource) ||
                string.IsNullOrWhiteSpace(command) || command.Length > 12288 || command.IndexOfAny(new[] {'\r','\n','\0'}) >= 0) return false;
            string text = Environment.ExpandEnvironmentVariables(command.Trim()), target, arguments;
            if (text[0] == '"')
            {
                int end = text.IndexOf('"', 1);
                if (end < 2 || (end + 1 < text.Length && !char.IsWhiteSpace(text[end + 1]))) return false;
                target = text.Substring(1, end - 1); arguments = text.Substring(end + 1).TrimStart();
            }
            else
            {
                int end = text.IndexOfAny(new[] {' ', '\t'});
                target = end < 0 ? text : text.Substring(0, end);
                arguments = end < 0 ? "" : text.Substring(end).TrimStart();
            }
            // Never guess an unquoted path containing spaces, resolve through PATH,
            // or manufacture a command-interpreter invocation from window metadata.
            if (!LaunchIdentity.FullPath(target) || target.StartsWith(@"\\", StringComparison.Ordinal) ||
                !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || LaunchIdentity.GenericHost(target) ||
                target.IndexOf('"') >= 0) return false;
            entry = new FavouriteEntry { Target = target, Arguments = arguments, AppId = appId,
                Name = Path.GetFileNameWithoutExtension(target), Group = "Recent app", IconPath = target };
            entry.Id = LauncherDescriptor.StableId(target + "\n" + arguments, appId);
            return true;
        }
        internal static FavouriteEntry Read(WindowRecord window)
        {
            if (window == null || window.IdentityAmbiguous || !Native.IsWindow(window.Handle) ||
                WindowNative.ProcessId(window.Handle) != window.ProcessId) return null;
            string id = ShellIcons.WindowAppId(window.Handle);
            if (LaunchIdentity.ConflictingIds(window.AppId, id)) return null;
            FavouriteEntry entry;
            if (!TryParse(ShellIcons.WindowProperty(window.Handle, "System.AppUserModel.RelaunchCommand"), id,
                ShellIcons.WindowProperty(window.Handle, "System.AppUserModel.RelaunchDisplayNameResource"), out entry) ||
                !File.Exists(entry.Target)) return null;
            try
            {
                string description = FileVersionInfo.GetVersionInfo(entry.Target).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) entry.Name = description;
            }
            catch { }
            // No document/window title is copied into the persistent app descriptor.
            return entry;
        }
    }

    static class SteamReopen
    {
        internal const string OpenMain = "steam://open/main";
        internal static string Canonical(string path)
        {
            try
            {
                path = Environment.ExpandEnvironmentVariables((path ?? "").Trim().Trim('"')).Replace('/', '\\');
                if (!LaunchIdentity.FullPath(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return "";
                return Path.GetFullPath(path).TrimEnd('\\');
            }
            catch { return ""; }
        }
        internal static bool BelongsToClient(string executable, string client)
        {
            executable = Canonical(executable); client = Canonical(client);
            if (executable.Length == 0 || client.Length == 0 || LaunchIdentity.Leaf(client) != "steam.exe") return false;
            if (LaunchIdentity.SamePath(executable, client)) return true;
            string bin = Path.GetDirectoryName(client) + @"\bin\";
            return LaunchIdentity.Leaf(executable) == "steamwebhelper.exe" && executable.StartsWith(bin, StringComparison.OrdinalIgnoreCase);
        }
        internal static bool PlainRequest(string arguments, bool taskbarSource)
        {
            arguments = (arguments ?? "").Trim();
            // A taskbar app click asks to show UI, not repeat the startup-only silent
            // state. Other switches, game URIs and all explicit favourite args survive.
            return arguments.Length == 0 || (taskbarSource && arguments.Equals("-silent", StringComparison.OrdinalIgnoreCase));
        }
        static IEnumerable<string> InstalledClients()
        {
            var paths = new List<string>();
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                    using (var key = root.OpenSubKey(@"Software\Valve\Steam", false))
                    {
                        if (key != null)
                        {
                            paths.Add(Convert.ToString(key.GetValue("SteamExe", "")));
                            string dir = Convert.ToString(key.GetValue("SteamPath", ""));
                            if (!string.IsNullOrWhiteSpace(dir)) paths.Add(Path.Combine(dir, "steam.exe"));
                        }
                    }
                }
                catch { }
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = root.OpenSubKey(@"Software\Valve\Steam", false))
                    {
                        string dir = key == null ? "" : Convert.ToString(key.GetValue("InstallPath", ""));
                        if (!string.IsNullOrWhiteSpace(dir)) paths.Add(Path.Combine(dir, "steam.exe"));
                    }
                }
                catch { }
            }
            return paths.Select(Canonical).Where(p => p.Length > 0 && File.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        static string Resolve(string executable)
        {
            string leaf = LaunchIdentity.Leaf(executable);
            if (leaf != "steam.exe" && leaf != "steamwebhelper.exe") return "";
            return InstalledClients().FirstOrDefault(c => BelongsToClient(executable, c)) ?? "";
        }
        internal static FavouriteEntry FromWindow(WindowRecord window)
        {
            if (window == null || window.IdentityAmbiguous || LaunchResolution.ProfileScoped(window.AppId)) return null;
            string client = Resolve(window.Exe); if (client.Length == 0) return null;
            return new FavouriteEntry { Id = LauncherDescriptor.StableId(client, ""), Name = "Steam", Target = client,
                IconPath = client, AppId = LaunchResolution.ExplicitId(window.AppId) ? window.AppId : "", Group = "Recent app" };
        }
        internal static bool TryPrepare(AppButton app, string target, string arguments, LaunchReceipt receipt, ref ProcessStartInfo start)
        {
            if (receipt == null || receipt.RequireNewWindow || LaunchResolution.ProfileScoped(receipt.ExpectedAppId)) return false;
            bool taskbar = app.Favourite == null || app.ClassName == "TaskbarPinnedShortcut";
            if (!PlainRequest(arguments, taskbar)) return false;
            if ((target ?? "").EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string linkArguments;
                if (!ShortcutArguments.TryRead(target, out linkArguments) || !PlainRequest(linkArguments, taskbar)) return false;
            }
            string client = Resolve(receipt.ExpectedExe);
            if (client.Length == 0 && (target ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) client = Resolve(target);
            if (client.Length == 0) return false;
            start = new ProcessStartInfo { FileName = client, Arguments = OpenMain, UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(client) };
            receipt.ExpectedExe = client; receipt.ReopenClient = client; receipt.Method = "Steam show main window";
            GrantForeground(client);
            return true;
        }
        static void GrantForeground(string client)
        {
            // Narrow permission grant only; never ASFW_ANY, a fake Alt key, an
            // account switch, process kill or an edit of Steam's preferences.
            int session = Process.GetCurrentProcess().SessionId;
            foreach (var process in Process.GetProcessesByName("steam"))
                using (process)
                    try
                    {
                        if (process.SessionId == session && LaunchIdentity.SamePath(process.MainModule.FileName, client))
                            Native.AllowSetForegroundWindow((uint)process.Id);
                    }
                    catch { }
        }
        internal static bool MatchesWindow(LaunchReceipt receipt, WindowRecord window)
        {
            return receipt != null && !string.IsNullOrWhiteSpace(receipt.ReopenClient) && window != null &&
                !window.IdentityAmbiguous && !LaunchResolution.ProfileScoped(window.AppId) &&
                BelongsToClient(window.Exe, receipt.ReopenClient);
        }
    }
}
