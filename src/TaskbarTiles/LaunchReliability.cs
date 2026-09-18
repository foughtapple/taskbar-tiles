// Taskbar Tiles 0.6.1 - launch dispatch and identity helpers.
// No command rewriting for user-supplied arguments; no automatic re-launch after
// dispatch. A successful ShellExecute request is NOT proof that a window opened.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TaskbarTiles
{
    sealed class LaunchReceipt
    {
        internal string Method = "", ExpectedExe = "", ExpectedAppId = "";
        internal uint ProcessId;
        internal long ProcessStartTicks;
        internal bool RequireNewWindow;
    }

    sealed class LaunchOperation
    {
        // 0: preparing, 1: dispatched, 2: cancelled before dispatch.
        // Abandoning a wait cannot retract a Shell request already handed to Windows.
        int state;
        internal readonly string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        internal bool Cancelled { get { return Interlocked.CompareExchange(ref state, 0, 0) == 2; } }
        internal bool TryDispatch() { return Interlocked.CompareExchange(ref state, 1, 0) == 0; }
        internal bool CancelBeforeDispatch() { return Interlocked.CompareExchange(ref state, 2, 0) == 0; }
        internal bool Dispatched { get { return Interlocked.CompareExchange(ref state, 0, 0) == 1; } }
    }

    static class LaunchLog
    {
        static readonly object Gate = new object();
        internal static string FilePath { get { return Path.Combine(Program.Home, "launch-diagnostics.log"); } }
        internal static void Write(string id, string text)
        {
            // Local only. Do not log command arguments, window titles or search queries.
            lock (Gate)
                try
                {
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024)
                    { File.Copy(FilePath, FilePath + ".previous", true); File.Delete(FilePath); }
                    File.AppendAllText(FilePath, DateTime.Now.ToString("s") + " [" + id + "] " + text + Environment.NewLine);
                }
                catch { }
        }
    }

    static class LaunchIdentity
    {
        internal static string CleanId(string id)
        {
            id = (id ?? "").Trim();
            if (id.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase)) id = id.Substring(6).Trim();
            const string prefix = @"shell:AppsFolder\";
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) id = id.Substring(prefix.Length);
            return id;
        }
        internal static string Leaf(string path)
        {
            path = (path ?? "").Trim().Trim('"').Replace('/', '\\');
            int i = path.LastIndexOf('\\'); return (i >= 0 ? path.Substring(i + 1) : path).ToLowerInvariant();
        }
        internal static bool SamePath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            return string.Equals(left.Trim().Trim('"').Replace('/', '\\'), right.Trim().Trim('"').Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
        }
        internal static bool FullPath(string path)
        {
            path = (path ?? "").Trim().Trim('"');
            return path.StartsWith(@"\\", StringComparison.Ordinal) || (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'));
        }
        internal static bool ConflictingIds(string requested, string actual)
        {
            requested = CleanId(requested); actual = CleanId(actual);
            return requested.Length != 0 && actual.Length != 0 && !requested.Equals(actual, StringComparison.OrdinalIgnoreCase);
        }
        internal static bool GenericHost(string exe)
        {
            string name = Leaf(exe);
            return new[] { "explorer.exe", "rundll32.exe", "dllhost.exe", "applicationframehost.exe", "runtimebroker.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "conhost.exe", "wscript.exe", "cscript.exe" }.Contains(name);
        }
        internal static bool Matches(AppButton app, WindowRecord w, LaunchReceipt receipt)
        {
            string requested = CleanId(receipt != null && !string.IsNullOrWhiteSpace(receipt.ExpectedAppId) ? receipt.ExpectedAppId : app.AppId);
            string actual = CleanId(w.AppId);
            // Never move another browser profile just because both use chrome.exe.
            if (ConflictingIds(requested, actual)) return false;
            if (requested.Length > 0 && actual.Length > 0) return true;
            if (receipt != null && receipt.ProcessId != 0 && receipt.ProcessId == w.ProcessId &&
                receipt.ProcessStartTicks != 0 && receipt.ProcessStartTicks == w.ProcessStartTicks && !GenericHost(w.Exe)) return true;
            string expected = receipt != null && !string.IsNullOrWhiteSpace(receipt.ExpectedExe) ? receipt.ExpectedExe : app.LaunchExe;
            if (FullPath(expected) && SamePath(expected, w.Exe)) return true;
            if (FullPath(requested) && SamePath(requested, w.Exe)) return true;
            // A CLI alias has a different name from the window-owning executable.
            // Only use this equivalence for an explicit, recognised Terminal launch.
            if (receipt != null && receipt.RequireNewWindow && Leaf(w.Exe) == "windowsterminal.exe")
            {
                if (FullPath(expected)) return false; // A known install/preview build must match its path.
                return IsTerminalExe(expected) && requested.Length == 0;
            }
            // Friendly names and window titles are not reliable launch identities.
            return false;
        }
        internal static bool IsTerminalExe(string target)
        { string name = Leaf(target); return name == "wt.exe" || name == "windowsterminal.exe" || name == "wtd.exe"; }
        internal static string TerminalFamily(string id)
        {
            id = CleanId(id);
            foreach (string family in new[] { "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe" })
                if (id.Equals(family + "!App", StringComparison.OrdinalIgnoreCase)) return family;
            return "";
        }
        internal static bool PlainTerminal(string id, string target, string arguments)
        {
            if (!string.IsNullOrWhiteSpace(arguments)) return false;
            if (IsTerminalExe(target)) return true;
            if (!string.IsNullOrWhiteSpace(target))
                return target.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase) && TerminalFamily(target).Length > 0;
            return TerminalFamily(id).Length != 0;
        }
        internal static bool CanReuse(bool enabled, bool requireNew, double elapsed, double timeout, bool hasNew,
            bool foregroundChanged, double foregroundStableMs)
        { return enabled && !requireNew && elapsed >= timeout && !hasNew && foregroundChanged && foregroundStableMs >= 800; }
        internal static bool IsNew(WindowRecord w, IDictionary<IntPtr, uint> before)
        { uint pid; return !before.TryGetValue(w.Handle, out pid) || pid != w.ProcessId; }
    }

    static class PackageIdentity
    {
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int GetApplicationUserModelId(IntPtr h, ref uint length, StringBuilder id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder path);
        // Window AppUserModel properties are optional; packaged apps also expose a process ID.
        internal static string ForProcess(uint pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid); if (h == IntPtr.Zero) return "";
            try
            {
                uint length = 0;
                if (GetApplicationUserModelId(h, ref length, null) != 122 || length < 2 || length > 32768) return "";
                var text = new StringBuilder((int)length);
                return GetApplicationUserModelId(h, ref length, text) == 0 ? text.ToString() : "";
            }
            catch { return ""; }
            finally { CloseHandle(h); }
        }
        internal static long StartTicks(uint pid)
        { try { using (var p = Process.GetProcessById((int)pid)) return p.StartTime.ToUniversalTime().Ticks; } catch { return 0; } }
        internal static string TerminalExecutable(string family)
        {
            if (string.IsNullOrEmpty(family)) return "";
            IntPtr names = IntPtr.Zero, buffer = IntPtr.Zero;
            try
            {
                uint count = 0, chars = 0;
                int code = GetPackagesByPackageFamily(family, ref count, IntPtr.Zero, ref chars, IntPtr.Zero);
                if ((code != 0 && code != 122) || count == 0 || count > 128 || chars == 0 || chars > 262144) return "";
                names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
                buffer = Marshal.AllocHGlobal(checked((int)chars * 2));
                if (GetPackagesByPackageFamily(family, ref count, names, ref chars, buffer) != 0) return "";
                for (int i = 0; i < count; i++)
                {
                    string full = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, i * IntPtr.Size));
                    uint length = 0;
                    if (GetPackagePathByFullName(full, ref length, null) != 122 || length < 2 || length > 32768) continue;
                    var path = new StringBuilder((int)length);
                    if (GetPackagePathByFullName(full, ref length, path) != 0) continue;
                    string exe = Path.Combine(path.ToString(), "WindowsTerminal.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }
            finally { if (names != IntPtr.Zero) Marshal.FreeHGlobal(names); if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
            return "";
        }
    }

    static class ReliableLauncher
    {
        internal static void Start(AppButton app, Options settings, TaskbarReader reader, LaunchOperation operation, Action<LaunchReceipt, string> completed)
        {
            var t = new Thread(delegate()
            {
                try
                {
                    if (operation.Cancelled) return;
                    if (app.Favourite != null)
                    {
                        LaunchFavourite(app, settings, operation, completed); return;
                    }
                    if (!settings.DirectAppLaunch)
                    { Fallback(app, reader, operation, completed); return; }
                    string target = app.VerifiedShortcut ? app.ShortcutPath : "";
                    if (!string.IsNullOrEmpty(target) && LaunchIdentity.FullPath(target) && !File.Exists(target)) target = "";
                    // Resolve launch identity independently of whether an icon could be extracted.
                    if (string.IsNullOrEmpty(target) && LaunchIdentity.FullPath(app.AppId) && File.Exists(app.AppId)) target = app.AppId;
                    if (string.IsNullOrEmpty(target) && !string.IsNullOrWhiteSpace(app.AppId))
                    {
                        string item = @"shell:AppsFolder\" + app.AppId;
                        if (ShellIcons.CanResolve(item)) target = item;
                    }
                    ProcessStartInfo start = null;
                    var receipt = new LaunchReceipt { ExpectedAppId = app.AppId, ExpectedExe = app.LaunchExe };
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        start = new ProcessStartInfo { FileName = target, UseShellExecute = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
                        SetShortcutIdentity(app, target, receipt);
                    }
                    if (settings.TerminalNewWindow && TryTerminal(app, target, "", receipt, ref start)) receipt.Method = "Terminal new window";
                    else receipt.Method = "shell target";
                    if (start == null) { Fallback(app, reader, operation, completed); return; }
                    // Pass the actual .lnk to the shell. Never discard its profile arguments.
                    Dispatch(start, receipt, operation, completed);
                }
                catch (Exception ex) { LaunchLog.Write(operation.Id, "launch failed: " + ex.GetType().Name + "; HRESULT=0x" + ex.HResult.ToString("X8")); completed(null, "Could not open " + app.DisplayName + ": " + ex.Message); }
            }) { IsBackground = true, Name = "Taskbar Tiles app launch" };
            t.SetApartmentState(ApartmentState.STA); t.Start();
        }
        static void SetShortcutIdentity(AppButton app, string target, LaunchReceipt receipt)
        {
            if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
            {
                string id = ShellIcons.FileProperty(target, "System.AppUserModel.ID");
                if (string.IsNullOrEmpty(receipt.ExpectedAppId)) receipt.ExpectedAppId = id;
                string exe = ShellIcons.FileProperty(target, "System.Link.TargetParsingPath");
                if (!string.IsNullOrWhiteSpace(exe)) receipt.ExpectedExe = exe;
            }
            else if (LaunchIdentity.FullPath(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) receipt.ExpectedExe = target;
            else if (target.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(receipt.ExpectedAppId)) receipt.ExpectedAppId = LaunchIdentity.CleanId(target);
        }
        static void LaunchFavourite(AppButton app, Options settings, LaunchOperation operation, Action<LaunchReceipt, string> completed)
        {
            FavouriteEntry entry = app.Favourite;
            ProcessStartInfo start = FavouriteLaunch.StartInfo(entry);
            var receipt = new LaunchReceipt { Method = "favourite", ExpectedAppId = app.AppId, ExpectedExe = app.LaunchExe };
            SetShortcutIdentity(app, entry.ExpandedTarget, receipt);
            // AppsFolder is a shell object, not a document passed through explorer.exe.
            // Let ShellExecute activate the actual item and keep any explicit arguments.
            if (entry.ExpandedTarget.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
            { start.FileName = entry.ExpandedTarget; start.Arguments = Environment.ExpandEnvironmentVariables(entry.Arguments); }
            if (settings.TerminalNewWindow && TryTerminal(app, entry.ExpandedTarget, entry.Arguments, receipt, ref start)) receipt.Method = "Terminal new window";
            Dispatch(start, receipt, operation, completed);
        }
        static bool TryTerminal(AppButton app, string target, string arguments, LaunchReceipt receipt, ref ProcessStartInfo start)
        {
            string executable = target;
            if ((target ?? "").EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                // An argument-bearing .lnk is an intentional custom command; do not rewrite it.
                string linkArguments;
                if (!ShortcutArguments.TryRead(target, out linkArguments) || !string.IsNullOrWhiteSpace(linkArguments)) return false;
                executable = receipt.ExpectedExe;
            }
            if (!LaunchIdentity.PlainTerminal(receipt.ExpectedAppId, executable, arguments)) return false;
            string family = LaunchIdentity.TerminalFamily(receipt.ExpectedAppId);
            string resolved = family.Length > 0 ? PackageIdentity.TerminalExecutable(family) : "";
            if (resolved.Length == 0 && LaunchIdentity.IsTerminalExe(executable))
            {
                // A user's explicit exe/alias is respected. Never substitute stable for Preview.
                if (family.Length == 0) resolved = executable;
                else if (LaunchIdentity.FullPath(executable) && File.Exists(executable) &&
                    executable.IndexOf(family, StringComparison.OrdinalIgnoreCase) >= 0) resolved = executable;
            }
            if (resolved.Length == 0) return false;
            if (start == null) start = new ProcessStartInfo { UseShellExecute = true };
            start.FileName = resolved; start.Arguments = "-w new";
            if (string.IsNullOrEmpty(start.WorkingDirectory)) start.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            receipt.ExpectedExe = LaunchIdentity.Leaf(resolved) == "windowsterminal.exe" ? resolved : "wt.exe";
            receipt.RequireNewWindow = true; return true;
        }
        static void Dispatch(ProcessStartInfo start, LaunchReceipt receipt, LaunchOperation operation, Action<LaunchReceipt, string> completed)
        {
            if (!operation.TryDispatch()) return;
            LaunchLog.Write(operation.Id, "dispatch: " + receipt.Method);
            // No retries on exceptions here: partial shell activation may already have happened.
            using (var process = Process.Start(start))
            {
                if (process != null)
                {
                    try
                    {
                        string exe = process.MainModule.FileName;
                        if (!LaunchIdentity.GenericHost(exe))
                        { receipt.ProcessId = (uint)process.Id; receipt.ProcessStartTicks = process.StartTime.ToUniversalTime().Ticks; }
                    }
                    catch { }
                }
            }
            LaunchLog.Write(operation.Id, "request accepted; pid=" + receipt.ProcessId + "; window not yet verified");
            completed(receipt, null);
        }
        static void Fallback(AppButton app, TaskbarReader reader, LaunchOperation operation, Action<LaunchReceipt, string> completed)
        {
            LaunchLog.Write(operation.Id, "using revalidated taskbar action (no verified launch target)");
            reader.Launch(app, operation, delegate(string error)
            {
                var receipt = new LaunchReceipt { Method = "taskbar Shift+click", ExpectedAppId = app.AppId, ExpectedExe = app.LaunchExe };
                completed(receipt, error);
            });
        }
    }

    static class ShortcutArguments
    {
        internal static bool TryRead(string path, out string arguments)
        {
            arguments = ""; object shell = null, link = null;
            try
            {
                if (!File.Exists(path)) return false;
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic api = shell; link = api.CreateShortcut(path); dynamic item = link;
                arguments = Convert.ToString(item.Arguments); return true;
            }
            catch { return false; }
            finally
            {
                if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
