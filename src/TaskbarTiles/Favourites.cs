// Taskbar Tiles 0.6 - local, user-managed favourites. No downloaded icon assets.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class FavouriteEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Target { get; set; }
        public string Arguments { get; set; }
        public string WorkingDirectory { get; set; }
        public string IconPath { get; set; }
        public string Group { get; set; }
        public string AppId { get; set; }
        public bool Enabled { get; set; }
        public FavouriteEntry()
        {
            Id = Guid.NewGuid().ToString("N"); Name = ""; Target = ""; Arguments = "";
            WorkingDirectory = ""; IconPath = ""; Group = "Apps"; AppId = ""; Enabled = true;
        }
        internal FavouriteEntry Clone() { return (FavouriteEntry)MemberwiseClone(); }
        internal void Normalise()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
            Name = (Name ?? "").Trim(); Target = (Target ?? "").Trim().Trim('"');
            Arguments = Arguments ?? ""; WorkingDirectory = (WorkingDirectory ?? "").Trim().Trim('"');
            IconPath = (IconPath ?? "").Trim().Trim('"'); Group = (Group ?? "").Trim(); AppId = AppId ?? "";
            if (Group.Length == 0) Group = "Apps";
        }
        internal string ValidateEntry()
        {
            Normalise();
            if (Name.Length == 0) return "Enter a name for the shortcut.";
            if (Target.Length == 0) return "Choose an app, file, folder or web address.";
            if (Name.Length > 180 || Group.Length > 80) return "Use a shorter name (180 characters) or group (80 characters).";
            if ((Name + Target + Arguments + WorkingDirectory + IconPath + Group).Any(c => c == '\r' || c == '\n' || c == '\0'))
                return "Shortcut fields must contain single-line text.";
            if (Target.Length > 4096 || Arguments.Length > 8192) return "The target or arguments are too long.";
            if (Target.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || Target.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return "Use an application path or a normal web address, not a script/data URL.";
            return null;
        }
        internal string ExpandedTarget { get { return Environment.ExpandEnvironmentVariables(Target ?? ""); } }
        internal string Detail
        {
            get
            {
                string t = Target ?? "";
                if (t.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
                { Uri u; return Uri.TryCreate(t, UriKind.Absolute, out u) ? u.Host : "Website"; }
                return string.IsNullOrWhiteSpace(Group) ? "App or shortcut" : Group;
            }
        }
        internal static List<FavouriteEntry> Defaults()
        {
            return new List<FavouriteEntry> {
                new FavouriteEntry { Id = "builtin-explorer", Name = "File Explorer", Target = "%WINDIR%\\explorer.exe", Group = "Places" },
                new FavouriteEntry { Id = "builtin-downloads", Name = "Downloads", Target = "shell:Downloads", Group = "Places", IconPath = "shell:Downloads" },
                new FavouriteEntry { Id = "builtin-settings", Name = "Windows Settings", Target = "ms-settings:", Group = "Tools", IconPath = "%WINDIR%\\ImmersiveControlPanel\\SystemSettings.exe" }
            };
        }
        public override string ToString() { return Name; }
    }
    sealed class FavouritesDocument
    {
        public int Version { get; set; }
        public List<FavouriteEntry> Items { get; set; }
        public FavouritesDocument() { Version = 1; Items = new List<FavouriteEntry>(); }
    }
    static class FavouriteStore
    {
        internal static string FilePath { get { return Path.Combine(Program.Home, "favourites.json"); } }
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 2097152, RecursionLimit = 30 }; }
        internal static List<FavouriteEntry> Parse(string json)
        {
            var root = Json().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("Version") || !root.ContainsKey("Items"))
                throw new InvalidDataException("Choose a Taskbar Tiles favourites export, not a different JSON/settings file.");
            var document = Json().Deserialize<FavouritesDocument>(json);
            if (document == null || document.Items == null) throw new InvalidDataException("This is not a Taskbar Tiles favourites file.");
            if (document.Version != 1) throw new InvalidDataException("This favourites file uses an unsupported format version.");
            if (document.Items.Count > 500) throw new InvalidDataException("A favourites collection can contain up to 500 entries.");
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in document.Items)
            {
                if (e == null) throw new InvalidDataException("The favourites file contains an empty entry.");
                string error = e.ValidateEntry(); if (error != null) throw new InvalidDataException(error);
                if (!used.Add(e.Id)) { e.Id = Guid.NewGuid().ToString("N"); used.Add(e.Id); }
            }
            return document.Items;
        }
        internal static List<FavouriteEntry> Load()
        {
            if (!File.Exists(FilePath)) return FavouriteEntry.Defaults();
            try { return Read(FilePath); }
            catch (Exception ex) { Program.Log("Favourites: " + ex.Message); return new List<FavouriteEntry>(); }
        }
        internal static List<FavouriteEntry> Read(string path)
        {
            if (new FileInfo(path).Length > 2097152) throw new InvalidDataException("The favourites file is larger than 2 MB.");
            return Parse(File.ReadAllText(path));
        }
        internal static string Serialise(IEnumerable<FavouriteEntry> entries)
        {
            var copy = entries.Select(e => e.Clone()).ToList();
            foreach (var e in copy) { string error = e.ValidateEntry(); if (error != null) throw new InvalidDataException(error); }
            if (copy.Count > 500) throw new InvalidDataException("The maximum is 500 favourites.");
            return Json().Serialize(new FavouritesDocument { Items = copy });
        }
        internal static void Save(IEnumerable<FavouriteEntry> entries) { Write(FilePath, entries); }
        internal static void Write(string path, IEnumerable<FavouriteEntry> entries)
        {
            string text = Serialise(entries), temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true); else File.Move(temp, path);
        }
        internal static List<FavouriteEntry> Filter(IEnumerable<FavouriteEntry> entries, string query, string group, bool alphabetic)
        {
            string[] words = (query ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var filtered = entries.Where(e => e.Enabled && (string.IsNullOrEmpty(group) || e.Group.Equals(group, StringComparison.OrdinalIgnoreCase)) &&
                words.All(w => (e.Name + " " + e.Group).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
            return (alphabetic ? filtered.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase) : filtered).ToList();
        }
        internal static List<FavouriteEntry> Merge(List<FavouriteEntry> existing, IEnumerable<FavouriteEntry> incoming)
        {
            var result = existing.Select(e => e.Clone()).ToList();
            foreach (var e in incoming)
            {
                bool same = result.Any(x => string.Equals(x.Target, e.Target, StringComparison.OrdinalIgnoreCase) && x.Arguments == e.Arguments && x.Name == e.Name);
                if (same) continue;
                var item = e.Clone(); if (result.Any(x => x.Id == item.Id)) item.Id = Guid.NewGuid().ToString("N"); result.Add(item);
            }
            if (result.Count > 500) throw new InvalidDataException("This import would exceed 500 favourites.");
            return result;
        }
    }
    static class FavouriteLaunch
    {
        internal static AppButton AsApp(FavouriteEntry e)
        {
            string target = e.ExpandedTarget;
            string exe = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target : "";
            if (Directory.Exists(target) || (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !target.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase)))
                exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            return new AppButton { Id = string.IsNullOrEmpty(e.AppId) ? "favourite:" + e.Id : "Appid:" + e.AppId,
                Name = e.Name, DisplayName = e.Name, Favourite = e.Clone(), LaunchExe = exe, ShortcutPath = target };
        }
        internal static ProcessStartInfo StartInfo(FavouriteEntry entry)
        {
            string error = entry.ValidateEntry(); if (error != null) throw new InvalidDataException(error);
            string target = entry.ExpandedTarget;
            var start = new ProcessStartInfo { UseShellExecute = true, FileName = target,
                Arguments = Environment.ExpandEnvironmentVariables(entry.Arguments), WorkingDirectory = Environment.ExpandEnvironmentVariables(entry.WorkingDirectory) };
            if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || Directory.Exists(target))
            {
                start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                start.Arguments = "\"" + target.Replace("\"", "") + "\"";
            }
            // No command interpreter, execution-policy changes or elevation verb is inserted.
            return start;
        }
        internal static void Start(FavouriteEntry entry)
        { using (var process = Process.Start(StartInfo(entry))) { } }
    }
    // Each view owns its returned bitmap copies. Shell calls run on one background STA,
    // never inside painting, a keyboard hook or a mouse hook.
    sealed class FavouriteIcons : IDisposable
    {
        readonly Control owner;
        readonly Dictionary<string, Bitmap> images = new Dictionary<string, Bitmap>();
        readonly HashSet<string> requested = new HashSet<string>();
        readonly System.Collections.Concurrent.BlockingCollection<FavouriteEntry> queue = new System.Collections.Concurrent.BlockingCollection<FavouriteEntry>();
        volatile bool disposed;
        internal FavouriteIcons(Control target)
        {
            owner = target;
            var t = new Thread(Work) { IsBackground = true, Name = "Favourite icon reader" };
            t.SetApartmentState(ApartmentState.STA); t.Start();
        }
        static string Key(FavouriteEntry e) { return e.Target + "|" + e.IconPath; }
        internal Bitmap Get(FavouriteEntry e)
        {
            Bitmap b; string key = Key(e); if (images.TryGetValue(key, out b)) return b;
            if (!disposed && requested.Add(key)) { try { queue.Add(e.Clone()); } catch (InvalidOperationException) { } }
            return null;
        }
        void Work()
        {
            foreach (var e in queue.GetConsumingEnumerable())
            {
                if (disposed) break;
                Bitmap image = null;
                try
                {
                    string source = Environment.ExpandEnvironmentVariables(string.IsNullOrWhiteSpace(e.IconPath) ? e.Target : e.IconPath);
                    if (source.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Website previews never request a favicon or navigate to the URL.
                        image = new Bitmap(96, 96);
                        using (var g = Graphics.FromImage(image))
                        using (var pen = new Pen(Theme.Accent, 5))
                        { g.Clear(Color.Transparent); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                          g.DrawEllipse(pen, 13, 13, 70, 70); g.DrawEllipse(pen, 32, 13, 32, 70);
                          g.DrawLine(pen, 14, 48, 82, 48); g.DrawArc(pen, 13, 24, 70, 35, 0, 180); }
                    }
                    else if (!string.IsNullOrWhiteSpace(e.IconPath) && File.Exists(source) && new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(source).ToLowerInvariant()))
                    {
                        using (var raw = Image.FromFile(source))
                        { image = new Bitmap(96, 96); using (var g = Graphics.FromImage(image)) { g.Clear(Color.Transparent); g.DrawImage(raw, DrawingUtil.Fit(new Rectangle(0, 0, 96, 96), raw.Width, raw.Height)); } }
                    }
                    else image = ShellIcons.Extract(source, 96);
                }
                catch (Exception ex) { Program.Log("Favourite icon: " + ex.Message); }
                Bitmap result = image;
                if (disposed || owner.IsDisposed || !owner.IsHandleCreated) { if (result != null) result.Dispose(); continue; }
                try
                {
                    owner.BeginInvoke(new Action(delegate
                    {
                        if (disposed || owner.IsDisposed) { if (result != null) result.Dispose(); return; }
                        images[Key(e)] = result; owner.Invalidate(true);
                        var callback = Changed; if (callback != null) callback();
                    }));
                }
                catch (InvalidOperationException) { if (result != null) result.Dispose(); }
            }
        }
        internal Action Changed;
        public void Dispose()
        {
            disposed = true; queue.CompleteAdding();
            foreach (var b in images.Values) if (b != null) b.Dispose(); images.Clear();
        }
    }
    static class InstalledApps
    {
        internal static List<FavouriteEntry> Read()
        {
            var result = new List<FavouriteEntry>();
            foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
            {
                var pending = new Queue<string>(); if (Directory.Exists(root)) pending.Enqueue(root); int count = 0;
                while (pending.Count > 0 && count++ < 500 && result.Count < 2500)
                {
                    string dir = pending.Dequeue();
                    try
                    {
                        foreach (string file in Directory.GetFiles(dir, "*.lnk")) result.Add(new FavouriteEntry { Name = Path.GetFileNameWithoutExtension(file), Target = file });
                        foreach (string sub in Directory.GetDirectories(dir)) if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0) pending.Enqueue(sub);
                    }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            object shell = null, folder = null, items = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")); dynamic s = shell;
                folder = s.NameSpace("shell:AppsFolder");
                if (folder != null)
                {
                    dynamic f = folder; items = f.Items(); dynamic all = items; int count = Math.Min(2500, (int)all.Count);
                    for (int i = 0; i < count; i++)
                    {
                        object raw = null;
                        try
                        {
                            raw = all.Item(i); dynamic item = raw;
                            string name = Convert.ToString(item.Name), target = Convert.ToString(item.Path), id = "";
                            try { id = Convert.ToString(item.ExtendedProperty("System.AppUserModel.ID")); } catch { }
                            if (!string.IsNullOrWhiteSpace(id)) target = @"shell:AppsFolder\" + id;
                            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(target))
                                result.Add(new FavouriteEntry { Name = name, Target = target, AppId = id });
                        }
                        catch { }
                        finally { if (raw != null && Marshal.IsComObject(raw)) Marshal.ReleaseComObject(raw); }
                    }
                }
            }
            catch (Exception ex) { Program.Log("Installed apps: " + ex.Message); }
            finally
            {
                foreach (var c in new[] { items, folder, shell }) if (c != null && Marshal.IsComObject(c)) Marshal.ReleaseComObject(c);
            }
            // Keep same-name shortcuts to different targets/profiles; only exact duplicates are removed.
            return result.GroupBy(e => e.Name + "\n" + e.Target, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
