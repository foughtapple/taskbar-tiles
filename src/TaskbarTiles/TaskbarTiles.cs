// Taskbar Tiles 0.8.0 - Windows utility. C# 5 / .NET Framework.
// No telemetry, keyboard logging, taskbar registry edits or process injection.
// Network access is limited to explicit, user-initiated GitHub update checks/downloads.
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.CSharp;

namespace TaskbarTiles
{
    static class Program
    {
        internal static readonly string Home = AppDomain.CurrentDomain.BaseDirectory;
        internal const string EventName = "Local\\TaskbarTiles.Exit.v01";
        internal const string ToggleEventName = "Local\\TaskbarTiles.Toggle.v02";
        internal const string Version = "0.8.0";
        static bool SignalToggle()
        {
            try
            {
                // The process launched by X-Mouse may grant foreground permission to
                // the already-running tray process. Do not send synthetic Alt keys.
                using (var e = EventWaitHandle.OpenExisting(ToggleEventName))
                {
                    foreach (var process in Process.GetProcessesByName("TaskbarTiles"))
                    {
                        using (process)
                        {
                            try
                            {
                                if (process.Id != Process.GetCurrentProcess().Id &&
                                    process.SessionId == Process.GetCurrentProcess().SessionId)
                                    Native.AllowSetForegroundWindow((uint)process.Id);
                            }
                            catch { }
                        }
                    }
                    e.Set(); return true;
                }
            }
            catch { return false; }
        }
        internal static void Log(string text)
        {
            try { File.AppendAllText(Path.Combine(Home, "TaskbarTiles.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine); }
            catch { }
        }
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Contains("--test-clickaway")) { Environment.Exit(OutsideClickTests.RunNative()); return; }
            if (args.Contains("--test-clickaway-target")) { Environment.Exit(OutsideClickTests.RunTarget(args)); return; }
            if (args.Contains("--test-touch-shortcuts")) { Environment.Exit(TouchSupportTests.RunNative()); return; }
            if (args.Contains("--test-update-https")) { Environment.Exit(UpdateTlsTests.RunNetwork()); return; }
            if (args.Contains("--test-switcher-layer")) { Environment.Exit(SwitcherLayerTests.RunNative()); return; }
            if (args.Contains("--test-launch-fixture")) { Environment.Exit(LaunchOutcomeTests.Fixture(args)); return; }
            if (args.Contains("--test-launch-outcome")) { Environment.Exit(LaunchOutcomeTests.RunNative()); return; }
            if (args.Contains("--test-rendering")) { Environment.Exit(Switcher.RunRenderingRegressionTests()); return; }
            if (args.Contains("--self-test")) { Environment.Exit(SelfTests.Run()); return; }
            if (args.Contains("--exit"))
            {
                try { using (var e = EventWaitHandle.OpenExisting(EventName)) e.Set(); } catch { }
                return;
            }
            bool first;
            using (var mutex = new Mutex(true, "Local\\TaskbarTiles.Instance.v01", out first))
            {
                if (!first)
                {
                    if (args.Length == 0 || args.Contains("--toggle") || args.Contains("--show"))
                    {
                        // Covers a second launch while the resident copy is starting.
                        for (int i = 0; i < 25 && !SignalToggle(); i++) Thread.Sleep(40);
                    }
                    return;
                }
                try
                {
                    try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
                    catch { try { Native.SetProcessDPIAware(); } catch { } }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                    {
                        Log(e.Exception.ToString());
                        var resident = Application.OpenForms.OfType<Switcher>().FirstOrDefault();
                        if (resident != null) resident.HandleUiException(e.Exception);
                    };
                    AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                    { Log(Convert.ToString(e.ExceptionObject)); ShortcutDiagnostics.Write("unhandled exception; terminating=" + e.IsTerminating); };
                    Options.Migrate();
                    using (var popup = new Switcher())
                    using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, EventName))
                    using (var toggle = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName))
                    {
                        var wait = ThreadPool.RegisterWaitForSingleObject(quit, delegate(object s, bool timedOut)
                        {
                            try { popup.BeginInvoke(new Action(popup.Shutdown)); } catch { }
                        }, null, -1, false);
                        var toggleWait = ThreadPool.RegisterWaitForSingleObject(toggle, delegate(object s, bool timedOut)
                        {
                            try { popup.BeginInvoke(new Action(popup.ToggleMenu)); } catch { }
                        }, null, -1, false);
                        if (args.Contains("--toggle") || args.Contains("--show"))
                            popup.BeginInvoke(new Action(popup.ToggleMenu));
                        try { Application.Run(); }
                        finally { wait.Unregister(null); toggleWait.Unregister(null); }
                    }
                }
                catch (Exception ex)
                {
                    Log(ex.ToString());
                    MessageBox.Show("Taskbar Tiles could not start.\n\n" + ex.Message + "\n\nSee TaskbarTiles.log in:\n" + Home,
                        "Taskbar Tiles", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    sealed class AppButton
    {
        public string Id, Name, ClassName;
        public string DisplayName, ImageSource, LaunchExe, ShortcutPath;
        public FavouriteEntry Favourite;
        public bool VerifiedShortcut; // Launch metadata must not be inferred from an icon match alone.
        public string AppId { get { return (Id ?? "").StartsWith("Appid:", StringComparison.OrdinalIgnoreCase) ? Id.Substring(6).Trim() : ""; } }
        public IntPtr Taskbar;
        public Rectangle Bounds;
        public Bitmap Image;
        public string Key { get { return !string.IsNullOrEmpty(Id) && Id.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase) ? "id:" + Id : "name:" + Id + "|" + Name; } }
    }

    // UI Automation runs on its own MTA thread. The keyboard hook never waits for it.
    sealed class TaskbarReader : IDisposable
    {
        readonly BlockingCollection<Action> jobs = new BlockingCollection<Action>();
        readonly IconWorker icons = new IconWorker();
        public TaskbarReader()
        {
            var t = new Thread(delegate()
            {
                foreach (Action job in jobs.GetConsumingEnumerable())
                    try { job(); } catch (Exception ex) { Program.Log("Taskbar worker: " + ex); }
            });
            t.IsBackground = true; t.Name = "Taskbar accessibility reader";
            t.SetApartmentState(ApartmentState.MTA); t.Start();
        }
        public void Read(Point monitorPoint, Action<List<AppButton>, string> completed)
        {
            if (jobs.IsAddingCompleted) return;
            jobs.Add(delegate()
            {
                try
                {
                    var list = Scan(monitorPoint, true);
                    string status = list.Count == 0 ? "No visible taskbar apps found. Show the real taskbar, then refresh from the tray." : "";
                    icons.Resolve(list, delegate { completed(list, status); });
                }
                catch (Exception ex) { Program.Log(ex.ToString()); completed(new List<AppButton>(), "Taskbar scan failed: " + ex.Message); }
            });
        }
        public void Launch(AppButton original, LaunchOperation operation, Action<string> completed)
        {
            // Keep a slow UI Automation scan out of the normal taskbar/icon queue.
            var launchThread = new Thread(delegate()
            {
                try
                {
                    if (operation.Cancelled) return;
                    // Re-resolve the button before clicking: never trust old coordinates.
                    Point point = new Point(original.Bounds.Left + original.Bounds.Width / 2, original.Bounds.Top + original.Bounds.Height / 2);
                    var fresh = Scan(point, false);
                    var matches = fresh.Where(a => a.Key == original.Key).ToList();
                    AppButton item = matches.FirstOrDefault(a => a.Name == original.Name) ?? matches.FirstOrDefault();
                    if (item == null) { completed("The taskbar changed, or that app is no longer visible. Reopen the menu and try again."); return; }
                    var target = new Native.POINT(item.Bounds.Left + item.Bounds.Width / 2, item.Bounds.Top + item.Bounds.Height / 2);
                    IntPtr underneath = Native.WindowFromPoint(target);
                    if (Native.GetAncestor(underneath, 2) != item.Taskbar && underneath != item.Taskbar)
                    { completed("The real taskbar button is hidden or covered. Show the taskbar and try again. No click was sent."); return; }
                    if (Native.LaunchModifiersDown() || Native.Down(0x10) || Native.Down(1) || Native.Down(2))
                    { completed("Release your mouse buttons and keyboard modifiers, then try again. No click was sent."); return; }
                    if (!operation.TryDispatch()) return;
                    Native.ShiftClick(target, item.Taskbar);
                    completed(null);
                }
                catch (Exception ex) { LaunchLog.Write(operation.Id, "taskbar action failed: " + ex.GetType().Name); completed("Could not launch the taskbar app: " + ex.Message); }
            });
            launchThread.IsBackground = true; launchThread.Name = "Taskbar launch fallback";
            launchThread.SetApartmentState(ApartmentState.MTA); launchThread.Start();
        }
        public void Diagnostics(Action<string> done)
        {
            if (jobs.IsAddingCompleted) return;
            jobs.Add(delegate()
            {
                var sb = new StringBuilder();
                sb.AppendLine("Taskbar Tiles 0.2 accessibility diagnostics - local only");
                sb.AppendLine(DateTime.Now.ToString("s"));
                foreach (IntPtr root in Roots())
                {
                    sb.AppendLine("ROOT " + root + " " + Native.Class(root));
                    try
                    {
                        var elements = AutomationElement.FromHandle(root).FindAll(TreeScope.Descendants, Condition.TrueCondition);
                        for (int i = 0; i < Math.Min(elements.Count, 400); i++)
                        {
                            try
                            {
                                var a = elements[i].Current;
                                sb.AppendLine(a.ControlType.ProgrammaticName + " | " + a.ClassName + " | " + a.AutomationId + " | " + a.Name + " | " + a.BoundingRectangle + " | offscreen=" + a.IsOffscreen);
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex) { sb.AppendLine(ex.ToString()); }
                }
                string path = Path.Combine(Program.Home, "taskbar-diagnostics.txt");
                File.WriteAllText(path, sb.ToString()); done(path);
            });
        }
        static List<IntPtr> Roots()
        {
            var roots = new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                string c = Native.Class(h);
                if (c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") roots.Add(h);
                return true;
            }, IntPtr.Zero);
            return roots;
        }
        static List<AppButton> Scan(Point prefer, bool images)
        {
            Rectangle screen = Screen.FromPoint(prefer).Bounds;
            var roots = Roots().OrderByDescending(h =>
            {
                Native.RECT r; Native.GetWindowRect(h, out r);
                return screen.IntersectsWith(r.Rectangle) ? 1 : 0;
            }).ToList();
            foreach (IntPtr root in roots)
            {
                var result = new List<AppButton>();
                try
                {
                    IntPtr legacy = IntPtr.Zero;
                    Native.EnumChildWindows(root, delegate(IntPtr h, IntPtr p)
                    { if (Native.Class(h) == "MSTaskListWClass") legacy = h; return true; }, IntPtr.Zero);
                    // Windows 10 legacy list first, then the Windows 11 taskbar subtree.
                    var scopes = new List<IntPtr>();
                    if (legacy != IntPtr.Zero) scopes.Add(legacy);
                    if (legacy != root) scopes.Add(root);
                    foreach (IntPtr scope in scopes)
                    {
                        var condition = new OrCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                        var elements = AutomationElement.FromHandle(scope).FindAll(TreeScope.Descendants, condition);
                        var seen = new HashSet<string>();
                        for (int i = 0; i < elements.Count; i++)
                        {
                            try
                            {
                                var a = elements[i].Current;
                                string id = a.AutomationId ?? "", cls = a.ClassName ?? "", name = a.Name ?? "";
                                bool app = scope == legacy || id.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase)
                                    || cls.IndexOf("TaskListButton", StringComparison.OrdinalIgnoreCase) >= 0;
                                if (!app || a.IsOffscreen || !a.IsEnabled || string.IsNullOrWhiteSpace(name)) continue;
                                var r = a.BoundingRectangle;
                                if (r.IsEmpty || r.Width < 12 || r.Height < 12 || r.Width > 2000 || r.Height > 500) continue;
                                var bounds = new Rectangle((int)Math.Round(r.Left), (int)Math.Round(r.Top), (int)Math.Round(r.Width), (int)Math.Round(r.Height));
                                if (!SystemInformation.VirtualScreen.IntersectsWith(bounds)) continue;
                                // Deduplicate only identical bounding boxes, not different ungrouped app buttons.
                                if (!seen.Add(bounds.ToString())) continue;
                                var item = new AppButton { Id = id, Name = name, ClassName = cls, Bounds = bounds, Taskbar = root };
                                item.DisplayName = TextTools.CleanAppName(name);
                                // Never take screenshots of taskbar buttons. Their UIA bounds
                                // are not necessarily the icon bounds (and can move/scale).
                                result.Add(item);
                            }
                            catch (ElementNotAvailableException) { }
                            catch (Exception ex) { Program.Log("Skipped taskbar element: " + ex.Message); }
                        }
                        if (result.Count != 0) break;
                    }
                }
                catch (Exception ex) { Program.Log("Taskbar root: " + ex.Message); }
                if (result.Count > 0) return result.OrderBy(a => a.Bounds.Top).ThenBy(a => a.Bounds.Left).ToList();
            }
            return new List<AppButton>();
        }
        public void Dispose() { jobs.CompleteAdding(); icons.Dispose(); }
    }

    static class TextTools
    {
        public static string CleanAppName(string raw)
        {
            string s = Regex.Replace(raw ?? "", @"\s+", " ").Trim();
            s = Regex.Replace(s, @"\s*(?:[-,;:]\s*)?\b\d+\s+running\s+windows?\b.*$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*(?:[-,;:]\s*)?\bpinned\b(?:\s+to\s+taskbar)?\s*(?:button)?\s*$", "", RegexOptions.IgnoreCase);
            s = s.Trim(' ', '-', ',', ':', ';');
            return string.IsNullOrWhiteSpace(s) ? "App" : s;
        }
        public static string Key(string s)
        { return Regex.Replace(CleanAppName(s).ToLowerInvariant(), @"[^\p{L}\p{N}]", ""); }
        public static string Initials(string s)
        {
            var parts = CleanAppName(s).Split(new char[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            return (parts[0].Substring(0, 1) + (parts.Length > 1 ? parts[1].Substring(0, 1) : "")).ToUpperInvariant();
        }
    }

    // Shell extraction is isolated from both UI Automation (MTA) and the UI.
    // A pumping STA is used for Shell COM objects; it owns its icon cache.
    sealed class IconWorker : IDisposable
    {
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly List<ShortcutInfo> shortcuts = new List<ShortcutInfo>();
        Control dispatcher;
        volatile bool stopping;
        DateTime catalogTime = DateTime.MinValue;
        sealed class ShortcutInfo { public string Path, Name, AppId; public bool Pinned; }
        sealed class RunningInfo { public IntPtr Window; public string Title, AppId, Exe; }
        public IconWorker()
        {
            var thread = new Thread(delegate()
            {
                using (var control = new Control())
                {
                    var handle = control.Handle; dispatcher = control; ready.Set();
                    try { Application.Run(); }
                    finally { foreach (var b in cache.Values) if (b != null) b.Dispose(); cache.Clear(); }
                }
            });
            thread.IsBackground = true; thread.Name = "Taskbar icon extraction";
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        public void Resolve(List<AppButton> apps, Action completed)
        {
            if (stopping) { completed(); return; }
            if (!ready.WaitOne(3000)) { completed(); return; }
            try
            {
                dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        RefreshCatalog();
                        var running = ReadRunning();
                        foreach (var app in apps)
                        {
                            if (stopping) break;
                            try { ResolveOne(app, running); }
                            catch (Exception ex) { Program.Log("Icon: " + app.DisplayName + ": " + ex.Message); }
                        }
                    }
                    catch (Exception ex) { Program.Log("Icon reader: " + ex.Message); }
                    finally { completed(); }
                }));
            }
            catch (InvalidOperationException) { completed(); }
        }
        void RefreshCatalog()
        {
            if ((DateTime.UtcNow - catalogTime).TotalSeconds < 60) return;
            catalogTime = DateTime.UtcNow; shortcuts.Clear();
            foreach (var image in cache.Values) if (image != null) image.Dispose();
            cache.Clear();
            string pinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            ReadShortcuts(pinned, true);
            ReadShortcuts(Environment.GetFolderPath(Environment.SpecialFolder.Programs), false);
            ReadShortcuts(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), false);
            ReadAppsFolder();
        }
        void ReadAppsFolder()
        {
            object shell = null, folder = null, items = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic api = shell; folder = api.NameSpace("shell:AppsFolder");
                if (folder == null) return;
                dynamic namespaceFolder = folder; items = namespaceFolder.Items();
                dynamic all = items; int count = Math.Min(2000, (int)all.Count);
                for (int i = 0; i < count; i++)
                {
                    object item = null;
                    try
                    {
                        item = all.Item(i); dynamic entry = item;
                        string name = Convert.ToString(entry.Name), path = Convert.ToString(entry.Path), id = "";
                        try { id = Convert.ToString(entry.ExtendedProperty("System.AppUserModel.ID")); } catch { }
                        if (!string.IsNullOrEmpty(id)) path = @"shell:AppsFolder\" + id;
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                        shortcuts.Add(new ShortcutInfo { Name = name, Path = path, AppId = id ?? "", Pinned = false });
                    }
                    catch { }
                    finally { if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item); }
                }
            }
            catch (Exception ex) { Program.Log("AppsFolder icon catalogue: " + ex.Message); }
            finally
            {
                if (items != null && Marshal.IsComObject(items)) Marshal.ReleaseComObject(items);
                if (folder != null && Marshal.IsComObject(folder)) Marshal.ReleaseComObject(folder);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
            }
        }
        void ReadShortcuts(string folder, bool pinned)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            // Walk one directory at a time so one inaccessible folder does not
            // discard all accessible entries. These files supply icons, NOT tile order.
            var pending = new Queue<string>(); pending.Enqueue(folder);
            int visited = 0;
            while (pending.Count > 0 && visited++ < 600 && shortcuts.Count < 2000)
            {
                string dir = pending.Dequeue();
                try
                {
                    foreach (string path in Directory.GetFiles(dir, "*.lnk"))
                    {
                        var item = new ShortcutInfo { Path = path, Name = Path.GetFileNameWithoutExtension(path), Pinned = pinned, AppId = "" };
                        if (pinned) item.AppId = ShellIcons.FileProperty(path, "System.AppUserModel.ID");
                        shortcuts.Add(item);
                    }
                    if (!pinned)
                        foreach (string sub in Directory.GetDirectories(dir))
                            if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0) pending.Enqueue(sub);
                }
                catch { }
            }
        }
        Bitmap Cached(string parsingName)
        {
            if (string.IsNullOrWhiteSpace(parsingName)) return null;
            Bitmap value;
            if (!cache.TryGetValue(parsingName, out value))
            { value = ShellIcons.Extract(parsingName, 128); cache[parsingName] = value; }
            return value;
        }
        bool Assign(AppButton app, string source, string description)
        {
            var image = Cached(source);
            if (image == null) return false;
            app.Image = (Bitmap)image.Clone(); app.ImageSource = description; return true;
        }
        void ResolveOne(AppButton app, List<RunningInfo> running)
        {
            string name = TextTools.Key(app.DisplayName), id = app.AppId;
            // Exact shortcut name disambiguates separately pinned Chrome profiles.
            var candidates = shortcuts.Where(s => !LaunchIdentity.ConflictingIds(id, s.AppId) &&
                (TextTools.Key(s.Name) == name || (!string.IsNullOrEmpty(id) && string.Equals(s.AppId, id, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(s => (s.Pinned ? 10 : 0) + (TextTools.Key(s.Name) == name ? 5 : 0) +
                    (!string.IsNullOrEmpty(id) && string.Equals(s.AppId, id, StringComparison.OrdinalIgnoreCase) ? 20 : 0)).ToList();
            var exact = candidates.Where(s => !string.IsNullOrEmpty(id) && string.Equals(s.AppId, id, StringComparison.OrdinalIgnoreCase)).ToList();
            var pinnedNames = candidates.Where(s => s.Pinned && TextTools.Key(s.Name) == name).ToList();
            ShortcutInfo launch = exact.FirstOrDefault() ?? (pinnedNames.Count == 1 ? pinnedNames[0] : null);
            if (launch != null)
            {
                app.ShortcutPath = launch.Path; app.VerifiedShortcut = true;
                app.LaunchExe = ShellIcons.FileProperty(launch.Path, "System.Link.TargetParsingPath");
            }
            foreach (var shortcut in candidates)
                if (Assign(app, shortcut.Path, "shortcut: " + shortcut.Path))
                {
                    app.DisplayName = shortcut.Name;
                    // An icon-only name match is never promoted to a trusted launcher.
                    if (string.IsNullOrEmpty(app.ShortcutPath)) app.ShortcutPath = shortcut.Path;
                    // Unverified name-only icon lookups do not supply executable identity.
                    return;
                }
            if (!string.IsNullOrEmpty(id))
            {
                if (Assign(app, @"shell:AppsFolder\" + id, "AppsFolder: " + id)) return;
                if (File.Exists(id) && Assign(app, id, "application: " + id)) { app.LaunchExe = id; return; }
            }
            var window = running.FirstOrDefault(w => !string.IsNullOrEmpty(id) &&
                string.Equals(w.AppId, id, StringComparison.OrdinalIgnoreCase));
            if (window == null) window = running.FirstOrDefault(w =>
                TextTools.Key(w.Title) == name || (!string.IsNullOrEmpty(w.Exe) && TextTools.Key(Path.GetFileNameWithoutExtension(w.Exe)) == name));
            if (window == null && name.Length >= 4)
                window = running.FirstOrDefault(w => TextTools.Key(w.Title).EndsWith(name, StringComparison.OrdinalIgnoreCase));
            if (window != null)
            {
                if ((!string.IsNullOrEmpty(id) && string.Equals(id, window.AppId, StringComparison.OrdinalIgnoreCase)) || LaunchIdentity.SamePath(id, window.Exe)) app.LaunchExe = window.Exe;
                // Respect an app-supplied per-window icon before the generic host EXE.
                Bitmap image = ShellIcons.WindowIcon(window.Window);
                if (image != null) { app.Image = image; app.ImageSource = "window icon"; return; }
                if (Assign(app, window.Exe, "executable: " + window.Exe)) return;
            }
            // A neutral initials tile is preferable to the wrong icon or a clipped screenshot.
            app.ImageSource = "initials fallback";
        }
        static List<RunningInfo> ReadRunning()
        {
            var result = new List<RunningInfo>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                if (!Native.IsWindowVisible(h)) return true;
                var title = new StringBuilder(1024); Native.GetWindowText(h, title, title.Capacity);
                if (title.Length == 0) return true;
                result.Add(new RunningInfo { Window = h, Title = title.ToString(),
                    AppId = ShellIcons.WindowAppId(h), Exe = ShellIcons.ProcessFile(h) });
                return true;
            }, IntPtr.Zero);
            return result;
        }
        public void Dispose()
        {
            stopping = true;
            try { if (dispatcher != null) dispatcher.BeginInvoke(new Action(Application.ExitThread)); } catch { }
        }
    }

    static class ShellIcons
    {
        [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        { [PreserveSig] int GetImage(Native.SIZE size, uint flags, out IntPtr bitmap); }
        [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid format; public uint id; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] struct PROPVARIANT
        { [FieldOffset(0)] public ushort type; [FieldOffset(8)] public IntPtr pointer; }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
            [PreserveSig] int Commit();
        }
        [StructLayout(LayoutKind.Sequential)] struct NATIVEBITMAP
        { public int type, width, height, widthBytes; public ushort planes, bitsPixel; public IntPtr bits; }
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER
        {
            public uint size; public int width, height; public ushort planes, bitCount;
            public uint compression, imageSize; public int xPels, yPels; public uint clrUsed, clrImportant;
        }
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFO { public BITMAPINFOHEADER header; public uint colors; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        static extern int SHCreateItemFromParsingName(string name, IntPtr bind, ref Guid iid, out IShellItemImageFactory factory);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        static extern int SHGetPropertyStoreFromParsingName(string name, IntPtr bind, uint flags, ref Guid iid, out IPropertyStore store);
        [DllImport("shell32.dll", PreserveSig = true)]
        static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, out IPropertyStore store);
        [DllImport("propsys.dll", CharSet = CharSet.Unicode)] static extern int PSGetPropertyKeyFromName(string name, out PROPERTYKEY key);
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT value);
        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] static extern int GetObject(IntPtr h, int size, out NATIVEBITMAP bitmap);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFO info, uint usage);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
        public static string FileProperty(string path, string name)
        {
            IPropertyStore store = null;
            try
            {
                Guid id = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref id, out store) < 0 || store == null) return "";
                return ReadProperty(store, name);
            }
            catch { return ""; }
            finally { if (store != null) Marshal.ReleaseComObject(store); }
        }
        static string ReadProperty(IPropertyStore store, string name)
        {
            PROPERTYKEY key; if (PSGetPropertyKeyFromName(name, out key) < 0) return "";
            PROPVARIANT value = new PROPVARIANT();
            try
            {
                if (store.GetValue(ref key, out value) < 0 || value.pointer == IntPtr.Zero) return "";
                if (value.type == 31) return Marshal.PtrToStringUni(value.pointer) ?? "";
                if (value.type == 8) return Marshal.PtrToStringBSTR(value.pointer) ?? "";
                return "";
            }
            finally { PropVariantClear(ref value); }
        }
        public static string WindowAppId(IntPtr h)
        {
            IPropertyStore store = null;
            try
            {
                Guid id = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreForWindow(h, ref id, out store) < 0 || store == null) return "";
                return ReadProperty(store, "System.AppUserModel.ID");
            }
            catch { return ""; }
            finally { if (store != null) Marshal.ReleaseComObject(store); }
        }
        public static string ProcessFile(IntPtr window)
        {
            uint pid; GetWindowThreadProcessId(window, out pid);
            IntPtr handle = OpenProcess(0x1000, false, pid);
            if (handle == IntPtr.Zero) return "";
            try { int size = 32768; var text = new StringBuilder(size); return QueryFullProcessImageName(handle, 0, text, ref size) ? text.ToString() : ""; }
            finally { CloseHandle(handle); }
        }
        public static Bitmap WindowIcon(IntPtr h)
        {
            foreach (int type in new int[] { 1, 0, 2 })
            {
                IntPtr icon;
                if (SendMessageTimeout(h, 0x007F, new IntPtr(type), IntPtr.Zero, 0x2, 70, out icon) == IntPtr.Zero || icon == IntPtr.Zero) continue;
                try
                {
                    // WM_GETICON returns a borrowed handle. Clone it; do not destroy it.
                    using (var borrowed = Icon.FromHandle(icon))
                    using (var own = (Icon)borrowed.Clone())
                    using (var image = own.ToBitmap()) return Trim(image);
                }
                catch { }
            }
            return null;
        }
        internal static bool CanResolve(string parsingName)
        {
            IShellItemImageFactory factory = null;
            try { Guid iid = typeof(IShellItemImageFactory).GUID; return SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory) >= 0 && factory != null; }
            catch { return false; }
            finally { if (factory != null) Marshal.ReleaseComObject(factory); }
        }
        public static Bitmap Extract(string parsingName, int pixels)
        {
            IShellItemImageFactory factory = null; IntPtr handle = IntPtr.Zero;
            try
            {
                Guid id = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref id, out factory) < 0 || factory == null) return null;
                // ICONONLY | BIGGERSIZEOK. Preserve aspect ratio; no screenshots.
                if (factory.GetImage(new Native.SIZE { cx = pixels, cy = pixels }, 0x4 | 0x1, out handle) < 0 || handle == IntPtr.Zero) return null;
                using (var bitmap = FromNativeBitmap(handle)) return bitmap == null ? null : Trim(bitmap);
            }
            catch { return null; }
            finally
            {
                if (handle != IntPtr.Zero) DeleteObject(handle);
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }
        static Bitmap FromNativeBitmap(IntPtr handle)
        {
            NATIVEBITMAP data;
            if (GetObject(handle, Marshal.SizeOf(typeof(NATIVEBITMAP)), out data) == 0) return null;
            int width = data.width, height = Math.Abs(data.height);
            if (width < 1 || height < 1 || width > 2048 || height > 2048) return null;
            var info = new BITMAPINFO { header = new BITMAPINFOHEADER {
                size = 40, width = width, height = -height, planes = 1, bitCount = 32, compression = 0 } };
            byte[] pixels = new byte[checked(width * height * 4)];
            IntPtr dc = GetDC(IntPtr.Zero);
            try { if (GetDIBits(dc, handle, 0, (uint)height, pixels, ref info, 0) != height) return null; }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { hasAlpha = true; break; }
            if (!hasAlpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            var locked = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, IntPtr.Add(locked.Scan0, y * locked.Stride), width * 4);
            }
            finally { result.UnlockBits(locked); }
            return result;
        }
        internal static Bitmap Trim(Bitmap image)
        {
            // Ignore transparent padding (not the picture itself), then fit at draw time.
            int left = image.Width, top = image.Height, right = -1, bottom = -1;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (image.GetPixel(x, y).A > 8)
                    { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            if (right < left) return null;
            return image.Clone(Rectangle.FromLTRB(left, top, right + 1, bottom + 1), PixelFormat.Format32bppPArgb);
        }
    }

    sealed class WindowItem
    {
        public IntPtr Handle;
        public string Title;
        public uint ProcessId;
    }

    sealed partial class Switcher : Form
    {
        readonly NotifyIcon tray;
        readonly TaskbarReader reader;
        readonly KeyboardHook hook;
        readonly ToolStripMenuItem interceptItem, startupItem;
        readonly System.Windows.Forms.Timer settingsTimer, launchTimer;
        readonly ToolTip tip = new ToolTip();
        readonly List<IntPtr> thumbnails = new List<IntPtr>();
        readonly HashSet<int> previewReady = new HashSet<int>();
        readonly List<Rectangle> cardRects = new List<Rectangle>(), tileRects = new List<Rectangle>();
        List<AppButton> apps = new List<AppButton>();
        List<WindowItem> windows = new List<WindowItem>();
        Options options = Options.Load();
        DateTime configStamp;
        Rectangle area, closeRect, winPrev, winNext, appPrev, appNext, sizeDown, sizeUp;
        Point monitorPoint;
        float scale = 1;
        int selected, windowPage, appPage, perWindowPage = 1, perAppPage = 1, columns, rows, tileColumns, tileRows, appTop, footerTop;
        int lastMouseHit = -100, pressedMouseHit = -100;
        bool closing, stickySession, suppressDeactivate;
        string taskbarStatus = "Reading taskbar apps...";
        AppButton pending;
        LaunchOperation launchDispatch;
        DateTime dispatchStarted;
        DateTime launchStart;
        Font headingFont, uiFont, tileFont, windowTitleFont;
        Icon trayIcon;
        bool refreshInProgress;

        readonly SwitcherLayer switcherLayer;

        public Switcher() : this(false) { }
        internal Switcher(bool renderingTest)
        {
            Text = "Taskbar Tiles"; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; DoubleBuffered = true; KeyPreview = true;
            BackColor = Color.FromArgb(20, 27, 38); ForeColor = Color.FromArgb(235, 239, 245);
            TopMost = true;
            var h = Handle; // Create the message target without showing the menu.
            if (renderingTest) { Controls.Add(searchBox); return; } // Explicit isolated rendering tests: no hooks/tray/workers.
            reader = new TaskbarReader();
            hook = new KeyboardHook();
            hook.Enabled = options.InterceptAltTab;
            hook.Pressed = delegate(bool control, bool reverse)
            {
                Post(delegate { OpenOrCycle(control, reverse); });
            };
            hook.Released = delegate { Post(AltReleased); };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Taskbar Tiles", null, delegate { ToggleMenu(); });
            menu.Items.Add("Copy X-Mouse command", null, delegate
            {
                Clipboard.SetText("\"" + Application.ExecutablePath + "\" --toggle");
                Notify("Command copied. In X-Mouse, set Mouse Button 5 to Run Application, paste it, then OK and Apply.");
            });
            menu.Items.Add("Settings", null, delegate { ShowSettings(); });
            menu.Items.Add("Edit settings.ini (advanced)", null, delegate { OpenFile(Path.Combine(Program.Home, "settings.ini")); });
            menu.Items.Add("Undo last window move", null, delegate { UndoMove(); });
            menu.Items.Add("Refresh taskbar apps", null, delegate { RefreshApps(); });
            menu.Items.Add("Repair shortcuts", null, delegate { RepairShortcuts(); });
            menu.Items.Add("Open shortcut diagnostics", null, delegate { OpenFile(ShortcutDiagnostics.PathName); });
            var touchMenu = new ToolStripMenuItem("Touch screen monitor support");
            var pauseTouch = new ToolStripMenuItem("Pause automatic return") { CheckOnClick = true };
            pauseTouch.Click += delegate { if (touchService != null) touchService.SetPaused(pauseTouch.Checked); };
            var stayTouch = new ToolStripMenuItem("Stay here") { CheckOnClick = true };
            stayTouch.Click += delegate { if (touchService != null) touchService.SetStay(stayTouch.Checked); };
            touchMenu.DropDownOpening += delegate { if (touchService != null) { pauseTouch.Checked = touchService.Paused; stayTouch.Checked = touchService.Stay; } };
            touchMenu.DropDownItems.Add(pauseTouch); touchMenu.DropDownItems.Add(stayTouch);
            touchMenu.DropDownItems.Add("Return now (when safe)", null, delegate { if (touchService != null) touchService.ReturnNow(); });
            touchMenu.DropDownItems.Add("Monitors, test and settings...", null, delegate { ShowTouchSupport(); });
            menu.Items.Add(touchMenu);
            menu.Items.Add(new ToolStripSeparator());
            interceptItem = new ToolStripMenuItem("Replace Alt+Tab (uncheck to restore Windows)") { Checked = hook.Enabled, CheckOnClick = true };
            interceptItem.Click += delegate
            {
                hook.Enabled = interceptItem.Checked; options.InterceptAltTab = hook.Enabled;
                try { Options.SaveValue("InterceptAltTab", interceptItem.Checked ? "true" : "false"); } catch (Exception ex) { Notify(ex.Message); }
            };
            menu.Items.Add(interceptItem);
            startupItem = new ToolStripMenuItem("Start when I sign in") { Checked = File.Exists(StartupPath), CheckOnClick = true };
            startupItem.Click += delegate
            {
                try { SetStartup(startupItem.Checked); }
                catch (Exception ex) { startupItem.Checked = File.Exists(StartupPath); Notify(ex.Message); }
            };
            menu.Items.Add(startupItem);
            menu.Items.Add("Write taskbar diagnostics", null, delegate
            {
                reader.Diagnostics(delegate(string file) { Post(delegate { OpenFile(file); }); });
            });
            menu.Items.Add("Open launch diagnostics", null, delegate { OpenLaunchDiagnostics(); });
            menu.Items.Add("Open switching diagnostics", null, delegate { OpenSwitchingDiagnostics(); });
            menu.Items.Add("Open rendering diagnostics", null, delegate { OpenRenderingDiagnostics(); });
            menu.Items.Add("Refresh menu graphics", null, delegate { RefreshMenuGraphics(); });
            menu.Items.Add("Check for updates...", null, delegate { ShowUpdates(); });
            menu.Items.Add("About Taskbar Tiles", null, delegate { ShowAbout(); });
            menu.Items.Add("Cancel pending launch / placement", null, delegate { CancelPendingLaunch(); });
            menu.Items.Add("Write icon diagnostics", null, delegate
            {
                var report = new StringBuilder("Taskbar Tiles " + Program.Version + " icon sources (local only)" + Environment.NewLine);
                foreach (var app in apps)
                    report.AppendLine(app.DisplayName + " | " + app.Id + " | " + app.ImageSource + " | " + (app.Image == null ? "no icon" : app.Image.Width + "x" + app.Image.Height));
                string file = Path.Combine(Program.Home, "icon-diagnostics.txt");
                try { File.WriteAllText(file, report.ToString()); OpenFile(file); } catch (Exception ex) { Notify(ex.Message); }
            });
            menu.Items.Add("Open app folder", null, delegate { Process.Start("explorer.exe", "\"" + Program.Home.TrimEnd('\\') + "\""); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { Shutdown(); });
            trayIcon = ApplicationIcon();
            tray = new NotifyIcon { Icon = trayIcon, Text = "Taskbar Tiles " + Program.Version, Visible = true, ContextMenuStrip = menu };
            tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleMenu(); };
            if (!Native.RegisterHotKey(Handle, 10, 0x4000 | 0x1 | 0x2, 0x20))
                Program.Log("Ctrl+Alt+Space is already registered by another application.");
            if (!hook.Installed) Notify("Alt+Tab interception is unavailable. Try Ctrl+Alt+Space or double-click the tray icon.");
            configStamp = File.GetLastWriteTimeUtc(Path.Combine(Program.Home, "settings.ini"));
            settingsTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            settingsTimer.Tick += delegate { ReloadSettings(false); }; settingsTimer.Start();
            launchTimer = new System.Windows.Forms.Timer { Interval = 40 };
            launchTimer.Tick += LaunchTick;
            monitorPoint = Cursor.Position;
            SetupFeatures(); SetupQuickAccess(); SetupFullscreen(); SetupActivation();
            switcherLayer = new SwitcherLayer(this, delegate
            { return !closing && transient == null && activation == null && !fullscreenOpening; });
            SetupOutsideDismissal(); SetupShortcutRecovery(); SetupTouchSupport();
            RefreshApps();
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } // Tool window: exclude our menu from Alt+Tab.
        }
        void Post(Action action)
        {
            if (closing || IsDisposed) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    try { action(); }
                    catch (Exception ex)
                    {
                        Program.Log("UI action: " + ex);
                        try { NavigationFailed(ex); } catch (Exception recovery) { Program.Log("Navigation recovery: " + recovery); }
                    }
                }));
            }
            catch (InvalidOperationException) { }
        }
        static Icon ApplicationIcon()
        { try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; } catch { return SystemIcons.Application; } }
        void Notify(string message)
        {
            if (tray == null || closing) return;
            tray.ShowBalloonTip(7000, "Taskbar Tiles", message, ToolTipIcon.Info);
        }
        static void OpenFile(string path)
        { Process.Start("notepad.exe", "\"" + path + "\""); }
        void ReloadSettings(bool force)
        {
            try
            {
                DateTime stamp = File.GetLastWriteTimeUtc(Path.Combine(Program.Home, "settings.ini"));
                if (!force && stamp == configStamp) return;
                configStamp = stamp; options = Options.Load();
                hook.Enabled = options.InterceptAltTab; interceptItem.Checked = hook.Enabled;
                if (touchService != null) touchService.Configure(options);
                ApplyFilter(false);
            }
            catch (Exception ex) { Program.Log("Settings: " + ex.Message); }
        }
        void RefreshApps()
        {
            if (refreshInProgress || closing) return;
            refreshInProgress = true;
            reader.Read(monitorPoint, delegate(List<AppButton> found, string status)
            {
                if (closing) { DisposeImages(found); return; }
                Post(delegate
                {
                    refreshInProgress = false;
                    var old = allApps;
                    var filtered = found.Where(a => MatchesQuery(a.DisplayName)).ToList();
                    bool unchangedOrder = apps.Count == filtered.Count && apps.Select(a => a.Key).SequenceEqual(filtered.Select(a => a.Key));
                    allApps = found; apps = filtered; taskbarStatus = status;
                    appPage = Math.Min(appPage, Math.Max(0, (apps.Count - 1) / Math.Max(1, perAppPage)));
                    // Updating only the icons must not discard a click-in-progress,
                    // move the grid or re-register every DWM preview.
                    if (Visible) { if (unchangedOrder) Invalidate(); else LayoutMenu(); }
                    DisposeImages(old);
                });
            });
        }
        static void DisposeImages(IEnumerable<AppButton> list)
        { foreach (var a in list) if (a.Image != null) a.Image.Dispose(); }
        public void ToggleMenu()
        {
            RepairShortcuts();
            if (touchService != null) touchService.Cancel("switcher command");
            CancelPassiveLaunchObservation();
            if (fullscreenOpening) { CancelFullscreenOpen(); return; }
            if (launchPlacement != null && launchPlacement.ActiveDialog != null) { launchPlacement.ActiveDialog.Activate(); return; }
            if (transient != null) { if (transient is ZonePicker || transient is FavouritesWindow) transient.Close(); else transient.Activate(); return; }
            if (Visible) Dismiss(); else OpenOrCycle(true, false);
        }
        void ChangeSize(int delta)
        {
            try
            {
                Options.SaveValue("TileSize", Math.Max(56, Math.Min(256, options.TileSize + delta)).ToString());
                ReloadSettings(true);
            }
            catch (Exception ex) { Notify("Could not save tile size: " + ex.Message); }
        }
        void OpenOrCycle(bool forceSticky, bool reverse, bool minimiseFullscreen = true)
        {
            if (touchService != null) touchService.Cancel("switcher navigation");
            CancelPassiveLaunchObservation();
            if (closing || pending != null || transient != null) return;
            if (launchPlacement != null && launchPlacement.ActiveDialog != null) { launchPlacement.ActiveDialog.Activate(); return; }
            if (Visible)
            {
                switcherLayer.RaiseNow();
                if (integratedSearch != null && integratedSearch.Visible) { integratedSearch.FocusInput(); return; }
                stickySession |= forceSticky;
                MoveSelection(reverse ? -1 : 1); return;
            }
            if (fullscreenOpening)
            { stickySession |= forceSticky; if (stickySession) acceptAfterFullscreenOpen = false; openingCycles += reverse ? -1 : 1; return; }
            CancelActivation();
            ReloadSettings(false);
            stickySession = forceSticky || options.StickyAltTab;
            monitorPoint = Cursor.Position;
            IntPtr foreground = Native.GetForegroundWindow();
            desktopAtOpen = DisplayNative.CurrentDesktop(foreground);
            if (minimiseFullscreen && StartFullscreenOpen(foreground, reverse)) return;
            ShowMenuCore(reverse, foreground);
        }
        void ShowMenuCore(bool reverse, IntPtr foregroundBeforeOpen)
        {
            CancelActivation();
            foregroundBeforeMenu = foregroundBeforeOpen;
            updatingSearch = true; searchBox.Text = ""; updatingSearch = false;
            allWindows = GetWindows();
            var original = allWindows.FirstOrDefault(w => w.Handle == foregroundBeforeOpen);
            if (original != null) { allWindows.Remove(original); allWindows.Insert(0, original); }
            ApplyFilter(false);
            selected = windows.Count > 1 ? (reverse ? windows.Count - 1 : 1) : 0;
            windowPage = 0; appPage = 0;
            area = Screen.FromPoint(monitorPoint).WorkingArea;
            scale = Native.ScaleAt(monitorPoint);
            // Constrain extreme accessibility scaling only when the two sections
            // otherwise cannot fit on the chosen monitor's working area.
            scale = MenuGeometry.ScaleFor(options, area.Size, scale);
            ResetPaintRecovery();
            EnsureMenuFonts();
            LayoutMenu();
            suppressDeactivate = true;
            try
            {
                switcherLayer.Begin();
                Show(); switcherLayer.RaiseNow();
                Activate(); Native.SetForegroundWindow(Handle);
                // Activation and topmost order are different operations. Reassert our
                // own HWND after activation; never promote the selected app to topmost.
                switcherLayer.RaiseNow();
                try { int corner = 2; Native.DwmSetWindowAttribute(Handle, 33, ref corner, 4); } catch { }
            }
            finally { suppressDeactivate = false; }
            UpdateThumbnails();
            RefreshApps();
        }
        List<WindowItem> GetWindows()
        {
            var list = new List<WindowItem>(); var seen = new HashSet<IntPtr>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                if (h == Handle || !Native.IsWindowVisible(h)) return true;
                long style = Native.GetWindowLongPtr(h, -20).ToInt64();
                if ((style & 0x80) != 0 || (style & 0x08000000) != 0) return true;
                if (Native.GetWindow(h, 4) != IntPtr.Zero && (style & 0x40000) == 0) return true;
                string cls = Native.Class(h);
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "Progman" || cls == "WorkerW") return true;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                var title = new StringBuilder(1024); Native.GetWindowText(h, title, title.Capacity);
                if (title.Length == 0) return true;
                // Only a genuinely blocked owner is redirected to its owned modal.
                // An enabled window is never substituted by its last active palette.
                h = ActivationPolicy.Resolve(new WindowsActivationApi(), h);
                if (h == IntPtr.Zero || !Native.IsWindow(h)) return true;
                if (!seen.Add(h)) return true;
                var actualTitle = new StringBuilder(1024); Native.GetWindowText(h, actualTitle, actualTitle.Capacity);
                list.Add(new WindowItem { Handle = h, ProcessId = WindowNative.ProcessId(h), Title = actualTitle.Length > 0 ? actualTitle.ToString() : title.ToString() });
                return true;
            }, IntPtr.Zero);
            return list;
        }
        int S(int n) { return Math.Max(1, (int)Math.Round(n * scale)); }
        MenuGeometry menuGeometry;
        Rectangle pageInfoRect;
        void RefreshLabelFonts()
        {
            EnsureMenuFonts();
        }
        void LayoutMenu()
        {
            RefreshLabelFonts();
            ClearThumbnails(); cardRects.Clear(); tileRects.Clear(); cardCloseRects.Clear();
            lastMouseHit = pressedMouseHit = -100; tip.Hide(this);
            int pad = S(22), gap = S(12);
            menuGeometry = MenuGeometry.Build(options, area.Size, scale, windows.Count, apps.Count, selected);
            int width = menuGeometry.Width, height = menuGeometry.Height;
            int available = Math.Max(1, width - pad * 2), tile = menuGeometry.Tile;
            int maxColumns = menuGeometry.Columns, maxTileColumns = menuGeometry.TileColumns;
            int requestedWidth = menuGeometry.CardWidth, previewHeight = menuGeometry.CardHeight;
            rows = menuGeometry.Rows; tileRows = menuGeometry.TileRows; headerHeight = menuGeometry.Header;
            perWindowPage = menuGeometry.WindowsPerPage; perAppPage = menuGeometry.AppsPerPage;
            selected = Math.Max(0, Math.Min(selected, windows.Count - 1));
            windowPage = windows.Count == 0 ? 0 : selected / perWindowPage;
            appPage = Math.Min(appPage, Math.Max(0, (apps.Count - 1) / perAppPage));
            Bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + Math.Max(0, (area.Height - height) / 2), width, height);
            closeRect = new Rectangle(width - pad - S(32), S(12), S(32), S(32));
            gearRect = new Rectangle(width - pad - S(72), S(12), S(32), S(32));
            undoRect = new Rectangle(width - pad - S(112), S(12), S(32), S(32));
            winPrev = new Rectangle(width - pad - S(232), S(12), S(32), S(32));
            winNext = new Rectangle(width - pad - S(194), S(12), S(32), S(32));
            searchBox.Visible = options.EnableSearch;
            if (options.EnableSearch) { searchBox.Font = Font; searchBox.SetBounds(pad, S(56), available, S(28)); }
            int windowCount = menuGeometry.VisibleWindows; columns = maxColumns;
            cardRects.AddRange(BalancedGrid.Cards(windowCount, columns, width, headerHeight, requestedWidth, previewHeight, gap));
            foreach (Rectangle card in cardRects)
                cardCloseRects.Add(WindowHeaderGeometry.Build(card, options, scale).Close);
            if (options.ShowWindowTitleIcons && headerIcons != null)
                headerIcons.Request(windows.Skip(windowPage * perWindowPage).Take(windowCount));
            appTop = headerHeight + rows * previewHeight + (rows - 1) * gap + S(22);
            appPrev = new Rectangle(width - pad - S(70), appTop, S(32), S(32));
            appNext = new Rectangle(width - pad - S(32), appTop, S(32), S(32));
            int appCount = Math.Min(perAppPage, apps.Count - appPage * perAppPage);
            tileColumns = Math.Min(maxTileColumns, Math.Max(1, (appCount + tileRows - 1) / tileRows));
            for (int i = 0; i < appCount; i++)
            {
                int row = i / tileColumns, inRow = Math.Min(tileColumns, appCount - row * tileColumns);
                int x = (width - (inRow * tile + (inRow - 1) * gap)) / 2;
                tileRects.Add(new Rectangle(x + i % tileColumns * (tile + gap), appTop + S(42) + row * (tile + gap), tile, tile));
            }
            footerTop = height - S(40) - QuickAccessExtra;
            sizeDown = new Rectangle(width - pad - S(64), footerTop, S(28), S(28));
            sizeUp = new Rectangle(width - pad - S(28), footerTop, S(28), S(28));
            previewDown = new Rectangle(width - pad - S(284), footerTop, S(28), S(28));
            previewUp = new Rectangle(width - pad - S(248), footerTop, S(28), S(28));
            ArrangeQuickAccess();
            pageInfoRect = new Rectangle(S(204), S(14), Math.Max(1, winPrev.Left - S(280)), S(28));
            if (integratedSearch != null && integratedSearch.Visible) ArrangeIntegratedSearch();
            if (Visible && !renderingPreview) UpdateThumbnails(); Invalidate();
        }
        Rectangle PreviewBox(Rectangle r)
        {
            int titleBand = S(MenuTextMetrics.TitleBand(options));
            return new Rectangle(r.Left + S(10), r.Top + titleBand, Math.Max(1, r.Width - S(20)), Math.Max(1, r.Height - titleBand - S(options.ShowMonitorBadges ? 32 : 10)));
        }
        void UpdateThumbnails()
        {
            ClearThumbnails();
            if (!options.ShowLivePreviews || (integratedSearch != null && integratedSearch.Visible)) { Invalidate(); return; }
            for (int i = 0; i < cardRects.Count; i++)
            {
                IntPtr thumb;
                if (Native.DwmRegisterThumbnail(Handle, windows[windowPage * perWindowPage + i].Handle, out thumb) != 0) continue;
                thumbnails.Add(thumb);
                Native.SIZE source;
                if (Native.DwmQueryThumbnailSourceSize(thumb, out source) != 0 || source.cx < 1 || source.cy < 1) continue;
                Rectangle dest = DrawingUtil.Fit(PreviewBox(cardRects[i]), source.cx, source.cy);
                var props = new Native.THUMBNAILPROPERTIES { dwFlags = 1 | 4 | 8 | 16,
                    rcDestination = new Native.RECT(dest), opacity = 255, fVisible = true, fSourceClientAreaOnly = false };
                if (Native.DwmUpdateThumbnailProperties(thumb, ref props) == 0) previewReady.Add(i);
            }
            Invalidate();
        }
        void ClearThumbnails()
        { foreach (IntPtr h in thumbnails) Native.DwmUnregisterThumbnail(h); thumbnails.Clear(); previewReady.Clear(); }
        void Label(Graphics g, string text, Rectangle r, bool heading, Color color, bool center)
        {
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            if (center) flags |= TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(g, text, heading ? headingFont : Font, r, color, flags);
        }
        void PaintAction(Graphics g, Rectangle r, int hit, string kind)
        {
            Color ink = Color.FromArgb(189, 200, 217);
            if (lastMouseHit == hit && hit < 2000) DrawingUtil.Round(g, r, S(8), Color.FromArgb(46, 55, 71), Color.FromArgb(75, 89, 109), 1);
            float x = r.Left + r.Width / 2f, y = r.Top + r.Height / 2f, d = S(4);
            using (var pen = new Pen(ink, Math.Max(1.4f, scale * 1.5f)))
            {
                if (kind == "close") { g.DrawLine(pen, x - d, y - d, x + d, y + d); g.DrawLine(pen, x + d, y - d, x - d, y + d); }
                else if (kind == "gear")
                {
                    g.DrawEllipse(pen, x - d, y - d, d * 2, d * 2);
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(pen, x + (float)Math.Cos(a) * d * 1.2f, y + (float)Math.Sin(a) * d * 1.2f, x + (float)Math.Cos(a) * d * 1.8f, y + (float)Math.Sin(a) * d * 1.8f); }
                }
                else if (kind == "undo")
                {
                    g.DrawArc(pen, x - d, y - d, d * 2.6f, d * 2.6f, 180, 220);
                    g.DrawLines(pen, new[] { new PointF(x - d * 2, y), new PointF(x - d, y - d), new PointF(x, y) });
                }
                else if (kind == "plus" || kind == "minus")
                { g.DrawLine(pen, x - d, y, x + d, y); if (kind == "plus") g.DrawLine(pen, x, y - d, x, y + d); }
                else
                { float sign = kind == "prev" ? -1 : 1; g.DrawLines(pen, new PointF[] { new PointF(x - sign * d / 2, y - d), new PointF(x + sign * d / 2, y), new PointF(x - sign * d / 2, y + d) }); }
            }
        }
        void PaintMenu(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            paintPhase = "menu header";
            Color light = ForeColor, muted = Color.FromArgb(156, 171, 192), accent = Color.FromArgb(111, 193, 250);
            DrawingUtil.Round(g, new Rectangle(0, 0, Width - 1, Height - 1), S(15), BackColor, Color.FromArgb(62, 74, 93), 1);
            if (renderingPreview && options.EnableSearch) PaintPreviewSearch(g);
            Label(g, "Open windows", new Rectangle(S(22), S(14), S(136), S(26)), true, light, false);
            var count = new Rectangle(S(157), S(16), S(35), S(22));
            DrawingUtil.Round(g, count, S(7), Color.FromArgb(38, 49, 66), Color.Transparent, 0);
            Label(g, windows.Count.ToString(), count, false, accent, true);
            if (menuGeometry != null)
                Label(g, menuGeometry.Notice.Length == 0 ? perWindowPage + " max/page · up to " + columns + " x " + menuGeometry.MaximumRows : "Screen limit · " + perWindowPage + "/page", pageInfoRect, false, menuGeometry.Notice.Length == 0 ? muted : Color.FromArgb(245, 195, 108), false);
            PaintAction(g, closeRect, -1, "close"); PaintAction(g, gearRect, -8, "gear");
            if (options.EnableUndoMove && mover.HasUndo) PaintAction(g, undoRect, -11, "undo");
            if (windows.Count > perWindowPage)
            {
                Label(g, (windowPage + 1) + " / " + PageCount(windows.Count, perWindowPage), new Rectangle(winPrev.Left - S(70), winPrev.Top, S(65), winPrev.Height), false, muted, true);
                PaintAction(g, winPrev, -2, "prev"); PaintAction(g, winNext, -3, "next");
            }
            for (int i = 0; i < cardRects.Count; i++)
            {
                Rectangle r = cardRects[i]; int index = windowPage * perWindowPage + i;
                paintPhase = "window card";
                bool hover = lastMouseHit == i, active = index == selected;
                DrawingUtil.Round(g, r, S(10), hover ? Color.FromArgb(40, 50, 65) : Color.FromArgb(31, 39, 51),
                    active ? accent : hover ? Color.FromArgb(89, 111, 140) : Color.FromArgb(53, 65, 83), active ? Math.Max(1.5f, scale * 1.5f) : 1);
                var header = WindowHeaderGeometry.Build(r, options, scale);
                if (!header.Icon.IsEmpty)
                {
                    var icon = headerIcons == null ? null : headerIcons.Get(windows[index].Handle);
                    if (!DrawMenuImage(g, icon, header.Icon, "window title icon"))
                        WindowHeaderGeometry.PaintFallback(g, header.Icon, accent);
                }
                TextRenderer.DrawText(g, windows[index].Title, windowTitleFont ?? Font, header.Title, light,
                    TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (options.ShowCloseButtons)
                {
                    if (lastMouseHit == 2000 + i) DrawingUtil.Round(g, cardCloseRects[i], S(5), Color.FromArgb(155, 51, 65), Color.Transparent, 0);
                    PaintAction(g, cardCloseRects[i], 2000 + i, "close");
                }
                if (options.ShowMonitorBadges) Label(g, MonitorLabel(windows[index].Handle), new Rectangle(r.Left + S(10), r.Bottom - S(24), r.Width - S(20), S(20)), false, muted, false);
                Rectangle box = PreviewBox(r);
                DrawingUtil.Round(g, box, S(5), Color.FromArgb(17, 22, 31), Color.Transparent, 0);
                // Do not paint placeholder words behind valid, narrow live previews.
                if (renderingPreview && options.ShowLivePreviews) PaintExampleWindow(g, box, i);
                else if (!previewReady.Contains(i)) Label(g, options.ShowLivePreviews ? "Preview unavailable" : windows[index].Title, box, false, muted, true);
            }
            if (windows.Count == 0) Label(g, "No switchable windows", new Rectangle(S(22), headerHeight + S(10), Width - S(44), S(80)), false, muted, true);
            using (var divider = new Pen(Color.FromArgb(45, 56, 73)))
                g.DrawLine(divider, S(22), appTop - S(10), Width - S(22), appTop - S(10));
            Label(g, "Taskbar apps", new Rectangle(S(22), appTop, S(138), S(24)), true, light, false);
            Label(g, "Open another window", new Rectangle(S(164), appTop + S(1), Width - S(300), S(24)), false, muted, false);
            if (apps.Count > perAppPage)
            {
                Label(g, (appPage + 1) + " / " + PageCount(apps.Count, perAppPage), new Rectangle(appPrev.Left - S(70), appPrev.Top, S(65), appPrev.Height), false, muted, true);
                PaintAction(g, appPrev, -4, "prev"); PaintAction(g, appNext, -5, "next");
            }
            for (int i = 0; i < tileRects.Count; i++)
            {
                Rectangle r = tileRects[i]; AppButton app = apps[appPage * perAppPage + i];
                bool hover = lastMouseHit == 1000 + i;
                DrawingUtil.Round(g, r, S(11), hover ? Color.FromArgb(47, 62, 82) : Color.FromArgb(31, 40, 54),
                    hover ? accent : Color.FromArgb(49, 62, 81), hover ? Math.Max(1.2f, scale) : 1);
                int labelHeight = options.ShowAppLabels ? S(MenuTextMetrics.AppLabelBand(options)) : 0;
                int iconSize = Math.Max(S(14), Math.Min((int)(r.Width * .58), r.Height - labelHeight - S(18)));
                Rectangle box = new Rectangle(r.Left + (r.Width - iconSize) / 2, options.ShowAppLabels ? r.Top + S(8) : r.Top + (r.Height - iconSize) / 2, iconSize, iconSize);
                if (!DrawMenuImage(g, app.Image, box, "app launch icon"))
                {
                    DrawingUtil.Round(g, box, S(9), Color.FromArgb(45, 65, 89), Color.Transparent, 0);
                    Label(g, TextTools.Initials(app.DisplayName), box, true, accent, true);
                }
                var label = new Rectangle(r.Left + S(5), r.Bottom - labelHeight - S(5), r.Width - S(10), labelHeight);
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                flags |= TextFormatFlags.WordBreak;
                if (options.ShowAppLabels) TextRenderer.DrawText(g, app.DisplayName, tileFont, label, light, flags);
            }
            if (apps.Count == 0) Label(g, string.IsNullOrEmpty(Query) ? taskbarStatus : "No matching apps", new Rectangle(S(22), appTop + S(42), Width - S(44), S(72)), false, muted, true);
            paintPhase = "footer";
            PaintQuickAccess(g);
            Label(g, options.RightClickZones ? "Left-click: choose   /   Right-click: place   /   Esc: back" : "Click to choose   /   Esc to close", new Rectangle(S(22), footerTop, Width - S(options.QuickSizeButtons ? 490 : 44), S(28)), false, muted, false);
            if (options.QuickSizeButtons)
            {
                Label(g, "Previews  " + options.PreviewScale + "%", new Rectangle(previewDown.Left - S(145), footerTop, S(140), S(28)), false, muted, true);
                Label(g, "Apps  " + options.TileSize + " px", new Rectangle(sizeDown.Left - S(135), footerTop, S(130), S(28)), false, muted, true);
                PaintAction(g, previewDown, -9, "minus"); PaintAction(g, previewUp, -10, "plus");
                PaintAction(g, sizeDown, -6, "minus"); PaintAction(g, sizeUp, -7, "plus");
            }
        }
        static int PageCount(int count, int size) { return Math.Max(1, (count + size - 1) / size); }
        int Hit(Point p)
        {
            if (pageInfoRect.Contains(p)) return -17;
            int quick = HitQuickAccess(p); if (quick != -100) return quick;
            if (closeRect.Contains(p)) return -1;
            if (gearRect.Contains(p)) return -8;
            if (options.EnableUndoMove && mover.HasUndo && undoRect.Contains(p)) return -11;
            if (options.QuickSizeButtons)
            {
                if (sizeDown.Contains(p)) return -6;
                if (sizeUp.Contains(p)) return -7;
                if (previewDown.Contains(p)) return -9;
                if (previewUp.Contains(p)) return -10;
            }
            if (windows.Count > perWindowPage && winPrev.Contains(p)) return -2;
            if (windows.Count > perWindowPage && winNext.Contains(p)) return -3;
            if (apps.Count > perAppPage && appPrev.Contains(p)) return -4;
            if (apps.Count > perAppPage && appNext.Contains(p)) return -5;
            if (options.ShowCloseButtons) for (int i = 0; i < cardCloseRects.Count; i++) if (cardCloseRects[i].Contains(p)) return 2000 + i;
            for (int i = 0; i < cardRects.Count; i++) if (cardRects[i].Contains(p)) return i;
            for (int i = 0; i < tileRects.Count; i++) if (tileRects[i].Contains(p)) return 1000 + i;
            return -100;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); int hit = Hit(e.Location);
            Cursor = hit == -100 ? Cursors.Default : Cursors.Hand;
            if (hit == lastMouseHit) return;
            lastMouseHit = hit;
            string text = "";
            if (hit == -17) text = menuGeometry == null ? "" : menuGeometry.Summary(options) + "\nAdjust this in Settings > Appearance > Open-window pages.";
            else if (hit >= 2000) text = "Close " + windows[windowPage * perWindowPage + hit - 2000].Title;
            else if (hit >= 1000) text = apps[appPage * perAppPage + hit - 1000].DisplayName + "\nLeft-click: open another. Right-click: choose a screen or zone.";
            else if (hit >= 0) text = windows[windowPage * perWindowPage + hit].Title + "\nRight-click to choose a screen or zone.";
            else if (hit == -6) text = "Smaller tiles";
            else if (hit == -7) text = "Larger app tiles";
            else if (hit == -8) text = "Settings";
            else if (hit == -9) text = "Smaller open-window previews";
            else if (hit == -10) text = "Larger open-window previews";
            else if (hit == -11) text = "Undo last window move";
            if (hit <= -12 && hit >= -16) text = QuickAccessTip(hit);
            tip.SetToolTip(this, text); Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (integratedSearch != null && integratedSearch.Visible)
            { HideIntegratedSearch(); pressedMouseHit = -100; return; }
            pressedMouseHit = Hit(e.Location);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int hit = Hit(e.Location); if (hit != pressedMouseHit) return;
            if (e.Button == MouseButtons.Middle && options.MiddleClickClose && hit >= 0 && hit < 1000)
            { CloseWindowCard(hit); return; }
            if (e.Button == MouseButtons.Right)
            {
                if (!options.RightClickZones) return;
                if (hit >= 2000) ChooseZone(windows[windowPage * perWindowPage + hit - 2000], null);
                else if (hit >= 1000) ChooseZone(null, apps[appPage * perAppPage + hit - 1000]);
                else if (hit >= 0) ChooseZone(windows[windowPage * perWindowPage + hit], null);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (hit <= -12 && hit >= -16) ExecuteQuickAccess(hit);
            else if (hit == -1) Dismiss();
            else if (hit >= 2000) CloseWindowCard(hit - 2000);
            else if (hit == -8) ShowSettings();
            else if (hit == -9 || hit == -10) ChangePreviewSize(hit == -9 ? -10 : 10);
            else if (hit == -11) UndoMove();
            else if (hit == -6 || hit == -7) ChangeSize(hit == -6 ? -8 : 8);
            else if (hit == -2 || hit == -3)
            {
                int pages = PageCount(windows.Count, perWindowPage);
                int page = (windowPage + (hit == -2 ? -1 : 1) + pages) % pages;
                selected = page * perWindowPage; LayoutMenu();
            }
            else if (hit == -4 || hit == -5)
            {
                int pages = PageCount(apps.Count, perAppPage);
                appPage = (appPage + (hit == -4 ? -1 : 1) + pages) % pages; LayoutMenu();
            }
            else if (hit >= 1000)
            {
                QueueLaunch(apps[appPage * perAppPage + hit - 1000], null);
            }
            else if (hit >= 0) { selected = windowPage * perWindowPage + hit; AcceptWindow(); }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0)
            { if (e.Y >= appTop) ChangeSize(e.Delta < 0 ? -8 : 8); else ChangePreviewSize(e.Delta < 0 ? -10 : 10); return; }
            if (e.Y >= appTop && apps.Count > perAppPage)
            {
                int pages = PageCount(apps.Count, perAppPage);
                appPage = (appPage + (e.Delta < 0 ? 1 : -1) + pages) % pages; LayoutMenu();
            }
            else MoveSelection(e.Delta < 0 ? 1 : -1);
        }
        void MoveSelection(int delta)
        {
            if (windows.Count == 0) return;
            selected = (selected + delta % windows.Count + windows.Count) % windows.Count;
            if (windowPage != selected / perWindowPage) LayoutMenu(); else Invalidate();
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (integratedSearch != null && integratedSearch.Visible)
            {
                if (integratedSearch.HandleKey(keyData)) return true;
                // Let the embedded text box receive typing/caret keys, never the window grid.
                return base.ProcessCmdKey(ref msg, keyData);
            }
            if (QuickAccessKey(keyData)) return true;
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Escape) { if (!string.IsNullOrEmpty(Query)) searchBox.Clear(); else Dismiss(); return true; }
            if (code == Keys.F5) { RefreshMenuGraphics(); RefreshWindows(); RefreshApps(); return true; }
            if (keyData == (Keys.Control | Keys.F) && options.EnableSearch) { searchBox.Focus(); searchBox.SelectAll(); return true; }
            if (code == Keys.Enter) { if (windows.Count == 0 && apps.Count > 0) QueueLaunch(apps[0], null); else AcceptWindow(); return true; }
            if (searchBox.Focused && (code == Keys.Left || code == Keys.Right || code == Keys.Home || code == Keys.End)) return base.ProcessCmdKey(ref msg, keyData);
            if (code == Keys.Tab) { MoveSelection((keyData & Keys.Shift) != 0 ? -1 : 1); return true; }
            if (code == Keys.Left) { MoveSelection(-1); return true; }
            if (code == Keys.Right) { MoveSelection(1); return true; }
            if (code == Keys.Up) { MoveVerticalSelection(-1); return true; }
            if (code == Keys.Down) { MoveVerticalSelection(1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        void AcceptWindow()
        {
            if (windows.Count == 0 || selected < 0 || selected >= windows.Count) { Dismiss(); return; }
            ActivateWindow(windows[selected]);
        }
        void LaunchTick(object sender, EventArgs e)
        {
            if (pending == null) { launchTimer.Stop(); return; }
            if (launchDispatch != null)
            {
                // UI remains responsive even if an Explorer/Shell extension stalls.
                if ((DateTime.UtcNow - dispatchStarted).TotalSeconds > Math.Max(20, options.LaunchTimeoutSeconds))
                {
                    bool prevented = launchDispatch.CancelBeforeDispatch();
                    CancelPendingLaunch();
                    Notify(!prevented ? "Windows has not returned from the launch request. It may still open; do not repeatedly launch it. See launch diagnostics." : "Launch preparation timed out. No launch was sent. Try again or use the original taskbar.");
                }
                return;
            }
            if (Native.LaunchModifiersDown() || Native.Down(0x10) || Native.Down(1) || Native.Down(2))
            {
                if ((DateTime.UtcNow - launchStart).TotalSeconds > 4)
                { pending = null; pendingDestination = null; launchTimer.Stop(); Notify("Release the mouse buttons and keyboard modifiers before launching an app."); }
                return;
            }
            AppButton app = pending; ZoneDestination destination = pendingDestination;
            var operation = new LaunchOperation(); launchDispatch = operation; dispatchStarted = DateTime.UtcNow;
            LaunchLog.Write(operation.Id, "prepare; target=" + (app.Favourite == null ? "taskbar" : "favourite/search") + "; zone=" + (destination != null));
            // Track ordinary launches too: Shell acceptance is not foreground success.
            LaunchPlacement tracking = null;
            {
                try
                {
                    tracking = new LaunchPlacement(app, options.Clone(), operation.Id, delegate(IntPtr window, string error)
                    {
                        if (!ReferenceEquals(launchPlacement, tracking)) return;
                        var resolved = tracking.SelectedWindow;
                        launchPlacement = null; tracking.Dispose();
                        if (closing) return;
                        if (error != null) Notify(error);
                        else if (window != IntPtr.Zero && resolved != null)
                        {
                            if (destination != null) MoveTo(window, destination, false);
                            else ActivateWindow(new WindowItem { Handle = window, ProcessId = resolved.ProcessId, Title = resolved.Title });
                        }
                    }, destination != null);
                    launchPlacement = tracking;
                }
                catch (Exception ex) { launchDispatch = null; pending = null; pendingDestination = null; launchTimer.Stop(); Notify("Could not prepare app placement: " + ex.Message); return; }
            }
            DispatchAppLaunch(app, operation, delegate(LaunchReceipt receipt, string error)
            {
                Post(delegate
                {
                    // Ignore late callbacks belonging to a cancelled/timed-out request.
                    if (!ReferenceEquals(launchDispatch, operation)) return;
                    launchDispatch = null; pending = null; pendingDestination = null; launchTimer.Stop();
                    if (tracking != null && ReferenceEquals(launchPlacement, tracking))
                    { if (error != null) tracking.Fail(error); else tracking.Begin(receipt); }
                    else if (error != null) Notify(error);
                });
            });
        }
        void OpenLaunchDiagnostics()
        {
            if (!File.Exists(LaunchLog.FilePath)) LaunchLog.Write("info", "No launch diagnostics yet. Try opening an app first.");
            OpenFile(LaunchLog.FilePath);
        }
        void CancelPendingLaunch()
        {
            var dispatch = launchDispatch; launchDispatch = null;
            if (dispatch != null)
            {
                bool prevented = dispatch.CancelBeforeDispatch() || dispatch.Cancelled;
                LaunchLog.Write(dispatch.Id, prevented ? "cancelled before dispatch" : "wait cancelled; the already-sent request cannot be recalled");
            }
            pending = null; pendingDestination = null; launchTimer.Stop();
            var tracking = launchPlacement; launchPlacement = null;
            if (tracking != null) tracking.Dispose();
        }
        void Dismiss()
        { if (outsideClicks != null) outsideClicks.Suspend(); Capture = false; if (switcherLayer != null) switcherLayer.Suspend(); if (fullscreenOpening) CancelFullscreenOpen(); HideIntegratedSearch(); ClearThumbnails(); tip.Hide(this); Hide(); }
        protected override void OnDeactivate(EventArgs e)
        { base.OnDeactivate(e); if (!suppressDeactivate && options.HideOnFocusLoss && Visible && transient == null) Dismiss(); }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!closing) { e.Cancel = true; Dismiss(); }
            base.OnFormClosing(e);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x312 && touchService != null && touchService.HandleHotkey(m.WParam.ToInt32())) return;
            if (m.Msg == 0x312 && m.WParam.ToInt32() == 10) { ToggleMenu(); return; }
            base.WndProc(ref m);
        }
        public void Shutdown()
        {
            if (closing) return; closing = true;
            DisposeShortcutRecovery();
            if (touchService != null) touchService.Dispose();
            DisposeOutsideDismissal();
            if (switcherLayer != null) switcherLayer.Dispose();
            CancelPendingLaunch(); DisposeActivation(); ShutdownFullscreen(); ShutdownQuickAccess(); ShutdownFeatures();
            hook.Dispose(); Native.UnregisterHotKey(Handle, 10);
            settingsTimer.Stop(); launchTimer.Stop(); reader.Dispose(); ClearThumbnails();
            tray.Visible = false; tray.Dispose(); trayIcon.Dispose(); DisposeImages(allApps); allApps.Clear(); apps.Clear();
            Close(); Application.ExitThread();
        }
        public static string StartupPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Taskbar Tiles.lnk"); } }
        public static void SetStartup(bool enabled)
        {
            if (!enabled) { if (File.Exists(StartupPath)) File.Delete(StartupPath); return; }
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic s = shell; shortcut = s.CreateShortcut(StartupPath);
                dynamic link = shortcut;
                link.TargetPath = Application.ExecutablePath; link.WorkingDirectory = Program.Home;
                link.Description = "Taskbar Tiles - mouse-first app switcher"; link.Save();
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tip.Dispose();
                if (settingsTimer != null) settingsTimer.Dispose();
                if (launchTimer != null) launchTimer.Dispose();
            }
            base.Dispose(disposing);
            // Inherited child-control fonts must outlive the controls themselves.
            if (disposing && menuFonts != null) { menuFonts.Dispose(); menuFonts = null; }
            if (disposing) rejectedPaintImages.Clear();
        }
    }

    static class DrawingUtil
    {
        public static Rectangle Fit(Rectangle box, int width, int height)
        {
            if (width < 1 || height < 1 || box.Width < 1 || box.Height < 1) return Rectangle.Empty;
            double factor = Math.Min(box.Width / (double)width, box.Height / (double)height);
            int w = Math.Min(box.Width, Math.Max(1, (int)Math.Round(width * factor)));
            int h = Math.Min(box.Height, Math.Max(1, (int)Math.Round(height * factor)));
            return new Rectangle(box.Left + (box.Width - w) / 2, box.Top + (box.Height - h) / 2, w, h);
        }
        public static void Round(Graphics g, Rectangle r, int radius, Color fill, Color border, float thickness)
        {
            if (r.Width < 1 || r.Height < 1) return;
            float inset = thickness > 0 ? thickness / 2f : 0;
            var f = new RectangleF(r.Left + inset, r.Top + inset, Math.Max(.1f, r.Width - 2 * inset), Math.Max(.1f, r.Height - 2 * inset));
            float d = Math.Max(.1f, Math.Min(2 * radius, Math.Min(f.Width, f.Height)));
            using (var path = new GraphicsPath())
            {
                path.AddArc(f.Left, f.Top, d, d, 180, 90);
                path.AddArc(f.Right - d, f.Top, d, d, 270, 90);
                path.AddArc(f.Right - d, f.Bottom - d, d, d, 0, 90);
                path.AddArc(f.Left, f.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (thickness > 0 && border.A > 0)
                    using (var pen = new Pen(border, thickness)) g.DrawPath(pen, path);
            }
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        public static Icon TrayIcon()
        {
            using (var image = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(image))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Round(g, new Rectangle(0, 0, 32, 32), 8, Color.FromArgb(22, 31, 45), Color.Transparent, 0);
                    for (int i = 0; i < 4; i++)
                        Round(g, new Rectangle(6 + i % 2 * 11, 6 + i / 2 * 11, 9, 9), 2,
                            i == 0 ? Color.FromArgb(210, 235, 255) : Color.FromArgb(99, 186, 247), Color.Transparent, 0);
                }
                IntPtr handle = image.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }

    static class SelfTests
    {
        static void Check(bool value, string name)
        { if (!value) throw new InvalidOperationException("FAILED: " + name); }
        public static int Run()
        {
            var log = new StringBuilder();
            try
            {
                string[,] names = new string[,] {
                    { "Steam - 1 running window", "Steam" }, { "Codex - 2 running windows", "Codex" },
                    { "Spotify pinned", "Spotify" }, { "Your Chrome - 1 running window", "Your Chrome" },
                    { "File Explorer", "File Explorer" }, { "Work - Chrome", "Work - Chrome" },
                    { "X-Mouse Button Control - 1 running window", "X-Mouse Button Control" } };
                for (int i = 0; i < names.GetLength(0); i++) Check(TextTools.CleanAppName(names[i, 0]) == names[i, 1], "label " + names[i, 0]);
                log.AppendLine("PASS: label cleanup preserves profile names and strips accessibility status text.");
                int fits = 0;
                foreach (int side in new int[] { 24, 40, 48, 64, 96, 192, 288 })
                    foreach (int w in new int[] { 16, 32, 64, 128, 256, 1920 })
                        foreach (int h in new int[] { 16, 32, 64, 128, 256, 1080 })
                        {
                            var box = new Rectangle(30, 50, side, side);
                            Rectangle r = DrawingUtil.Fit(box, w, h);
                            Check(box.Contains(r) && r.Width > 0 && r.Height > 0, "image bounds");
                            Check(Math.Abs((r.Left + r.Width / 2.0) - (box.Left + box.Width / 2.0)) <= .51, "horizontal centring");
                            Check(Math.Abs((r.Top + r.Height / 2.0) - (box.Top + box.Height / 2.0)) <= .51, "vertical centring");
                            fits++;
                        }
                log.AppendLine("PASS: " + fits + " aspect-fit / centred-icon cases.");
                var input = typeof(Native).GetNestedType("INPUT", System.Reflection.BindingFlags.NonPublic);
                Check(Marshal.SizeOf(input) == (IntPtr.Size == 8 ? 40 : 28), "Win32 INPUT packing");
                Check(Marshal.SizeOf(typeof(Native.KBDLLHOOKSTRUCT)) == (IntPtr.Size == 8 ? 24 : 20), "keyboard structure packing");
                Check(Marshal.SizeOf(typeof(Native.THUMBNAILPROPERTIES)) == 48, "thumbnail structure packing");
                log.AppendLine("PASS: native INPUT, keyboard and DWM thumbnail structure sizes.");
                FeatureTests.Run(log);
                LauncherTests.Run(log);
                SearchPageTests.Run(log);
                LayoutRegressionTests.Run(log);
                LaunchReliabilityTests.Run(log);
                LaunchOutcomeTests.Run(log);
                LaunchResolutionTests.Run(log);
                InterfacePolishTests.Run(log);
                ActivationTests.Run(log);
                SwitcherLayerTests.Run(log);
                OutsideClickTests.Run(log);
                TouchSupportTests.Run(log);
                UpdateTests.Run(log);
                log.AppendLine("These are unit/interop-layout tests, not live Windows, FancyZones or X-Mouse integration tests.");
                File.WriteAllText(Path.Combine(Program.Home, "self-test.log"), log.ToString());
                return 0;
            }
            catch (Exception ex)
            {
                log.AppendLine(ex.ToString());
                try { File.WriteAllText(Path.Combine(Program.Home, "self-test.log"), log.ToString()); } catch { }
                return 1;
            }
        }
    }

    static class Native
    {
        public delegate bool EnumProc(IntPtr h, IntPtr p);
        public delegate IntPtr HookProc(int code, IntPtr wp, IntPtr lp);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; public POINT(int a, int b) { x = a; y = b; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT
        {
            public int left, top, right, bottom;
            public RECT(Rectangle r) { left = r.Left; top = r.Top; right = r.Right; bottom = r.Bottom; }
            public Rectangle Rectangle { get { return System.Drawing.Rectangle.FromLTRB(left, top, right, bottom); } }
        }
        [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] public struct THUMBNAILPROPERTIES
        {
            public uint dwFlags; public RECT rcDestination, rcSource; public byte opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
        }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mouse; [FieldOffset(0)] public KEYBDINPUT key; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION data; }
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc fn, IntPtr p);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc fn, IntPtr p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int length);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int length);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint processId);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint command);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetLastActivePopup(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetLong64(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetLong32(IntPtr h, int i);
        public static IntPtr GetWindowLongPtr(IntPtr h, int i) { return IntPtr.Size == 8 ? GetLong64(h, i) : new IntPtr(GetLong32(h, i)); }
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        public static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        public static bool LaunchModifiersDown() { return Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C); }
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int type, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wp, IntPtr lp);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        public static float ScaleAt(Point p)
        {
            try { uint x, y; if (GetDpiForMonitor(MonitorFromPoint(new POINT(p.X, p.Y), 2), 0, out x, out y) == 0) return x / 96f; } catch { }
            return 1;
        }
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
        [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
        [DllImport("dwmapi.dll")] public static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out SIZE size);
        [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref THUMBNAILPROPERTIES properties);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] input, int size);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        public static string Class(IntPtr h) { var b = new StringBuilder(256); GetClassName(h, b, b.Capacity); return b.ToString(); }
        static INPUT Key(ushort key, bool up)
        { return new INPUT { type = 1, data = new INPUTUNION { key = new KEYBDINPUT { vk = key, flags = up ? 2u : 0u } } }; }
        static INPUT Mouse(uint flags, POINT p, bool position)
        {
            var m = new MOUSEINPUT { flags = flags };
            if (position)
            {
                Rectangle v = SystemInformation.VirtualScreen;
                m.dx = (int)Math.Round((p.x - v.Left) * 65535.0 / Math.Max(1, v.Width - 1));
                m.dy = (int)Math.Round((p.y - v.Top) * 65535.0 / Math.Max(1, v.Height - 1));
                m.flags |= 0x8000 | 0x4000 | 0x0001;
            }
            return new INPUT { type = 0, data = new INPUTUNION { mouse = m } };
        }
        public static void WindowsShortcut(ushort key)
        {
            if (LaunchModifiersDown() || Down(0x10) || Down(key)) throw new InvalidOperationException("Release your keyboard modifiers before using this shortcut.");
            var events = new[] { Key(0x5B, false), Key(key, false), Key(key, true), Key(0x5B, true) };
            uint sent = SendInput((uint)events.Length, events, Marshal.SizeOf(typeof(INPUT)));
            if (sent != events.Length)
            {
                SendInput(2, new[] { Key(key, true), Key(0x5B, true) }, Marshal.SizeOf(typeof(INPUT)));
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the shortcut. You can still use the Windows key shortcut directly.");
            }
        }
        public static void ShiftClick(POINT target, IntPtr expectedTaskbar)
        {
            POINT restore; if (!GetCursorPos(out restore)) throw new Win32Exception();
            if (LaunchModifiersDown() || Down(0x10) || Down(1) || Down(2))
                throw new InvalidOperationException("Release the mouse buttons and modifiers first. No click was sent.");
            bool shiftAttempted = false, mouseAttempted = false;
            int inputSize = Marshal.SizeOf(typeof(INPUT));
            try
            {
                shiftAttempted = true;
                var prepare = new[] { Key(0xA0, false), Mouse(0, target, true) };
                if (SendInput((uint)prepare.Length, prepare, inputSize) != prepare.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the launch input. No app click was sent.");
                // Keep Shift down while Explorer handles the click, rather than
                // pressing/releasing Shift and restoring the cursor in one batch.
                Thread.Sleep(35);
                POINT current;
                if (!GetCursorPos(out current) || Math.Abs(current.x - target.x) > 3 || Math.Abs(current.y - target.y) > 3)
                    throw new InvalidOperationException("The pointer moved before the taskbar action. No click was sent; try again.");
                IntPtr under = WindowFromPoint(target);
                if (under != expectedTaskbar && GetAncestor(under, 2) != expectedTaskbar)
                    throw new InvalidOperationException("The taskbar became covered. No click was sent.");
                mouseAttempted = true;
                var click = new[] { Mouse(0x0002, target, false), Mouse(0x0004, target, false) };
                if (SendInput((uint)click.Length, click, inputSize) != click.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected part of the taskbar click. The app may still open; do not repeatedly launch it.");
                mouseAttempted = false;
                Thread.Sleep(90);
            }
            finally
            {
                if (mouseAttempted) SendInput(1, new[] { Mouse(0x0004, target, false) }, inputSize);
                if (shiftAttempted) SendInput(1, new[] { Key(0xA0, true) }, inputSize);
                // Do not snap the pointer back if the user has moved it meanwhile.
                POINT current;
                if (GetCursorPos(out current) && Math.Abs(current.x - target.x) <= 3 && Math.Abs(current.y - target.y) <= 3)
                    SendInput(1, new[] { Mouse(0, restore, true) }, inputSize);
            }
        }
    }
}
