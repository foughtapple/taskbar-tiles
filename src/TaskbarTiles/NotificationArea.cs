// Compact notification-area mirror for the main switcher.
// Discovery and actions use Windows accessibility metadata; no Explorer memory scraping,
// taskbar registry editing, hidden-coordinate clicks, or notification payload reading.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed class NotificationItem
    {
        internal string Key="", Name="", AutomationId="", ClassName="", RuntimeId="";
        internal IntPtr Root;
        internal Rectangle Bounds;
        internal bool Offscreen, Enabled, SystemItem;
        internal Bitmap Image;
    }

    static class NotificationAreaMetrics
    {
        internal static int LogicalHeight(Options o)
        { return o.ShowNotificationArea ? Math.Max(38, o.NotificationIconSize + 24) : 0; }

        internal static List<Rectangle> Cells(int count, int page, int width, int top, int iconSize, int spacing,
            out Rectangle previous, out Rectangle next, out int perPage)
        {
            previous=Rectangle.Empty; next=Rectangle.Empty;
            int cell=Math.Max(20,iconSize+10), gap=Math.Max(0,spacing), pad=22, arrow=28;
            int available=Math.Max(cell,width-pad*2);
            perPage=Math.Max(1,(available+gap)/(cell+gap));
            bool paged=count>perPage;
            if(paged) perPage=Math.Max(1,(available-arrow*2-gap*4+gap)/(cell+gap));
            int pages=Math.Max(1,(Math.Max(0,count)+perPage-1)/perPage);
            page=Math.Max(0,Math.Min(page,pages-1));
            int shown=Math.Min(perPage,Math.Max(0,count-page*perPage));
            int rowWidth=shown==0?0:shown*cell+(shown-1)*gap;
            int left=(width-rowWidth)/2;
            var result=new List<Rectangle>();
            for(int i=0;i<shown;i++) result.Add(new Rectangle(left+i*(cell+gap),top,cell,cell));
            if(paged)
            {
                previous=new Rectangle(Math.Max(pad,left-arrow-gap),top+(cell-arrow)/2,arrow,arrow);
                next=new Rectangle(Math.Min(width-pad-arrow,left+rowWidth+gap),top+(cell-arrow)/2,arrow,arrow);
            }
            return result;
        }
    }

    static class NotificationAreaPolicy
    {
        static readonly string[] ContainerTokens={ "traynotify","systemtray","notificationarea","notifyicon","syspager" };
        internal static bool IsContainer(string id,string cls)
        {
            string s=((id??"")+" "+(cls??"")).ToLowerInvariant();
            return cls=="TrayNotifyWnd" || cls=="SysPager" || cls=="ToolbarWindow32" || cls=="NotifyIconOverflowWindow" ||
                ContainerTokens.Any(t=>s.Contains(t));
        }
        internal static bool IsExcluded(string id,string cls,string name)
        {
            string s=((id??"")+" "+(cls??"")+" "+(name??"")).ToLowerInvariant();
            if(s.Contains("tasklistbutton")||s.Contains("startbutton")||s.Contains("searchbutton")||s.Contains("taskview")) return true;
            if((name??"").Equals("Show desktop",StringComparison.OrdinalIgnoreCase)) return true;
            if((name??"").IndexOf("Show hidden icons",StringComparison.OrdinalIgnoreCase)>=0) return true;
            if(NotificationRoots.IsChevron(id,cls,name)) return true;
            return false;
        }
        internal static bool Candidate(AutomationElement element,AutomationElement root,bool overflow)
        {
            try
            {
                var c=element.Current;
                if(string.IsNullOrWhiteSpace(c.Name)||IsExcluded(c.AutomationId,c.ClassName,c.Name)) return false;
                if(c.ControlType!=ControlType.Button && c.ControlType!=ControlType.ListItem && c.ControlType!=ControlType.MenuItem &&
                    !(c.ControlType==ControlType.Custom && (c.ClassName??"").EndsWith("IconView",StringComparison.Ordinal))) return false;
                if(overflow||IsContainer(c.AutomationId,c.ClassName)) return true;
                var walker=TreeWalker.RawViewWalker; AutomationElement p=element;
                for(int i=0;i<14;i++)
                {
                    p=walker.GetParent(p); if(p==null) break;
                    if(p==root) break;
                    var a=p.Current;
                    if(IsContainer(a.AutomationId,a.ClassName)) return true;
                }
            }
            catch { }
            return false;
        }
        internal static string Runtime(AutomationElement element)
        {
            try { return string.Join(".",element.GetRuntimeId().Select(x=>x.ToString()).ToArray()); }
            catch { return ""; }
        }
        internal static Rectangle Bounds(System.Windows.Rect rect)
        {
            if(rect.IsEmpty||double.IsNaN(rect.X)||double.IsNaN(rect.Y)||double.IsNaN(rect.Width)||double.IsNaN(rect.Height)||
                double.IsInfinity(rect.X)||double.IsInfinity(rect.Y)||double.IsInfinity(rect.Width)||double.IsInfinity(rect.Height)||
                Math.Abs(rect.X)>1000000||Math.Abs(rect.Y)>1000000||rect.Width<1||rect.Height<1||rect.Width>100000||rect.Height>100000) return Rectangle.Empty;
            return new Rectangle((int)Math.Round(rect.X),(int)Math.Round(rect.Y),(int)Math.Round(rect.Width),(int)Math.Round(rect.Height));
        }
        internal static string Key(string runtime,string id,string cls,string name)
        {
            if(!string.IsNullOrEmpty(runtime)) return "runtime:"+runtime;
            return "meta:"+(id??"")+"|"+(cls??"")+"|"+(name??"");
        }
        internal static bool Same(NotificationItem item,AutomationElement element)
        {
            try
            {
                var c=element.Current; string runtime=Runtime(element);
                if(item.RuntimeId.Length>0&&runtime==item.RuntimeId) return true;
                return string.Equals(item.AutomationId,c.AutomationId,StringComparison.Ordinal)&&
                    string.Equals(item.ClassName,c.ClassName,StringComparison.Ordinal)&&
                    string.Equals(item.Name,c.Name,StringComparison.Ordinal);
            }
            catch { return false; }
        }
        internal static bool StrongNameMatch(string trayName,string appName)
        {
            string tray=TextTools.Key(trayName), app=TextTools.Key(appName);
            if(app.Length<4||tray.Length<4) return false;
            return tray==app||tray.StartsWith(app,StringComparison.Ordinal)||app.StartsWith(tray,StringComparison.Ordinal);
        }
    }

    static class TrayArtwork
    {
        internal static Rectangle InnerBounds(Rectangle bounds)
        {
            if (bounds.Width < 8 || bounds.Height < 8) return Rectangle.Empty;
            int side = Math.Max(8, (int)Math.Round(Math.Min(bounds.Width, bounds.Height) * 0.62));
            side = Math.Min(side, Math.Min(bounds.Width, bounds.Height));
            return new Rectangle(bounds.Left + (bounds.Width - side) / 2, bounds.Top + (bounds.Height - side) / 2, side, side);
        }

        internal static Bitmap Capture(Rectangle bounds, bool offscreen)
        {
            try
            {
                if (offscreen) return null;
                Rectangle box = InnerBounds(bounds);
                Rectangle visible = Rectangle.Intersect(box, SystemInformation.VirtualScreen);
                if (visible.Width != box.Width || visible.Height != box.Height || box.IsEmpty) return null;
                var image = new Bitmap(box.Width, box.Height);
                using (var g = Graphics.FromImage(image))
                    g.CopyFromScreen(box.Location, Point.Empty, box.Size, CopyPixelOperation.SourceCopy);
                return image;
            }
            catch { return null; }
        }
    }

    static class DiscordNotificationVisual
    {
        static readonly Regex CountByWord = new Regex(@"(?i)(?<n>\d{1,4})\s*(?:new\s+|unread\s+)?(?:notification(?:s)?|message(?:s)?|mention(?:s)?)\b", RegexOptions.Compiled);
        static readonly Regex CountInParens = new Regex(@"(?i)\bdiscord(?:\s+(?:canary|ptb))?\b[^\r\n]*?\((?<n>\d{1,4})\)", RegexOptions.Compiled);
        static readonly Regex CountAfterDiscord = new Regex(@"(?i)\bdiscord(?:\s+(?:canary|ptb))?\b[^\r\n]*?(?:[-,:]\s*)(?<n>\d{1,4})\s*$", RegexOptions.Compiled);

        internal static bool TryCount(string name, out int count)
        {
            count = 0;
            if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, @"(?i)\bdiscord(?:\s+(?:canary|ptb))?\b")) return false;
            foreach (var regex in new[] { CountByWord, CountInParens, CountAfterDiscord })
            {
                var match = regex.Match(name);
                int parsed;
                if (match.Success && int.TryParse(match.Groups["n"].Value, out parsed))
                { count = Math.Max(0, Math.Min(9999, parsed)); return true; }
            }
            // Discord's tray provider commonly omits an explicit zero. If the item
            // is definitely Discord but exposes no count, zero is the safe visual.
            return true;
        }

        internal static Color Background(int count)
        { return count > 0 ? Color.FromArgb(237, 66, 69) : Color.Black; }

        internal static string CountText(int count)
        { return count > 999 ? "999+" : Math.Max(0, count).ToString(); }

        internal static float FontPixels(int iconPixels, string text)
        {
            float factor = text.Length >= 4 ? .46f : text.Length == 3 ? .54f : .68f;
            return Math.Max(9f, iconPixels * factor);
        }

        internal static void Paint(Graphics g, Rectangle box, int count)
        {
            string text = CountText(count);
            Color fill = Background(count);
            DrawingUtil.Round(g, box, Math.Max(3, box.Height / 5), fill,
                count > 0 ? Color.FromArgb(255, 103, 106) : Color.FromArgb(68, 68, 68), 1);
            float px = FontPixels(Math.Min(box.Width, box.Height), text);
            using (var font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, text, font, box, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    static class NotificationAreaAction
    {
        internal static string InvokeDefault(AutomationElement element)
        {
            try
            {
                object pattern;
                if(element.TryGetCurrentPattern(InvokePattern.Pattern,out pattern))
                { ((InvokePattern)pattern).Invoke(); return null; }
                return "Windows does not expose an Invoke action for this notification item.";
            }
            catch(ElementNotAvailableException) { return "That notification item changed. Reopen Taskbar Tiles and try again."; }
            catch(Exception ex) { return "Could not run the notification item action: "+ex.Message; }
        }

    }

    sealed class NotificationIconWorker : IDisposable
    {
        readonly BlockingCollection<Action> jobs=new BlockingCollection<Action>();
        readonly Dictionary<string,Bitmap> cache=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,Bitmap> artworkCache=new Dictionary<string,Bitmap>(StringComparer.Ordinal);
        List<FavouriteEntry> catalog;
        volatile bool disposed;
        internal NotificationIconWorker()
        {
            var thread=new Thread(new ThreadStart(delegate
            {
                foreach(var job in jobs.GetConsumingEnumerable())
                    try { job(); } catch(Exception ex) { Program.Log("Notification icon worker: "+ex.GetType().Name); }
                foreach(var image in cache.Values) if(image!=null) image.Dispose();
                foreach(var image in artworkCache.Values) if(image!=null) image.Dispose();
            }));
            thread.IsBackground=true; thread.Name="Notification area icon reader"; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        internal void Resolve(List<NotificationItem> items,Action done)
        {
            if(disposed){done();return;}
            try
            {
                jobs.Add(delegate
                {
                    if(catalog==null) try { catalog=InstalledApps.Read(); } catch { catalog=new List<FavouriteEntry>(); }
                    foreach(var item in items)
                    {
                        if(disposed) break;
                        try
                        {
                            Bitmap previous;
                            if(item.Image!=null)
                            {
                                if(artworkCache.TryGetValue(item.Key,out previous)){previous.Dispose();artworkCache.Remove(item.Key);}
                                if(artworkCache.Count>=256){string first=artworkCache.Keys.First();artworkCache[first].Dispose();artworkCache.Remove(first);}
                                artworkCache[item.Key]=(Bitmap)item.Image.Clone();continue;
                            }
                            if(artworkCache.TryGetValue(item.Key,out previous)){item.Image=(Bitmap)previous.Clone();continue;}
                            string target=TargetFor(item.Name);
                            if(target.Length==0) continue;
                            Bitmap cached;
                            if(!cache.TryGetValue(target,out cached))
                            { cached=ShellIcons.Extract(Environment.ExpandEnvironmentVariables(target),64); cache[target]=cached; }
                            if(cached!=null) item.Image=(Bitmap)cached.Clone();
                        }
                        catch { }
                    }
                    done();
                });
            }
            catch(InvalidOperationException){done();}
        }
        string TargetFor(string name)
        {
            var candidates=catalog.Where(e=>NotificationAreaPolicy.StrongNameMatch(name,e.Name))
                .Select(e=>new{Entry=e,Length=TextTools.Key(e.Name).Length}).OrderByDescending(x=>x.Length).ToList();
            if(candidates.Count==0) return "";
            if(candidates.Count>1&&candidates[0].Length==candidates[1].Length&&
                !string.Equals(candidates[0].Entry.Target,candidates[1].Entry.Target,StringComparison.OrdinalIgnoreCase)) return "";
            var best=candidates[0].Entry;
            return string.IsNullOrWhiteSpace(best.IconPath)?best.ExpandedTarget:Environment.ExpandEnvironmentVariables(best.IconPath);
        }
        public void Dispose()
        { if(disposed)return;disposed=true;jobs.CompleteAdding(); }
    }

    sealed class NotificationAreaReader : IDisposable
    {
        readonly BlockingCollection<Action> jobs=new BlockingCollection<Action>();
        readonly NotificationIconWorker icons=new NotificationIconWorker();
        volatile bool disposed;
        internal NotificationAreaReader()
        {
            var thread=new Thread(new ThreadStart(delegate
            {
                foreach(var job in jobs.GetConsumingEnumerable())
                    try { job(); } catch(Exception ex) { Program.Log("Notification area worker: "+ex); }
            }));
            thread.IsBackground=true; thread.Name="Notification area accessibility reader"; thread.SetApartmentState(ApartmentState.MTA); thread.Start();
        }
        static List<IntPtr> Roots()
        {
            var primary=new List<IntPtr>(); var secondary=new List<IntPtr>(); var overflow=new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr h,IntPtr p)
            {
                string cls=Native.Class(h);
                if(cls=="Shell_TrayWnd") primary.Add(h);
                else if(cls=="Shell_SecondaryTrayWnd") secondary.Add(h);
                else if(NotificationRoots.IsOverflowClass(cls))
                {
                    overflow.Add(h);
                    IntPtr bridge=NotificationRoots.FindWindowEx(h,IntPtr.Zero,"Windows.UI.Composition.DesktopWindowContentBridge",null);
                    if(bridge!=IntPtr.Zero)overflow.Add(bridge);
                }
                return true;
            },IntPtr.Zero);
            primary.AddRange(secondary); primary.AddRange(overflow); return primary;
        }
        static List<NotificationItem> Scan(bool includeHidden)
        {
            var result=new List<NotificationItem>(); var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(IntPtr rootHandle in Roots())
            {
                try
                {
                    var root=AutomationElement.FromHandle(rootHandle); bool overflow=NotificationRoots.IsOverflow(rootHandle);
                    var condition=new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Custom));
                    var elements=root.FindAll(TreeScope.Descendants,condition);
                    for(int i=0;i<elements.Count;i++)
                    {
                        var element=elements[i];
                        if(!NotificationAreaPolicy.Candidate(element,root,overflow)) continue;
                        var c=element.Current;
                        if(!includeHidden && (overflow || c.IsOffscreen)) continue;
                        string runtime=NotificationAreaPolicy.Runtime(element);
                        string key=NotificationAreaPolicy.Key(runtime,c.AutomationId,c.ClassName,c.Name);
                        if(!seen.Add(key)) continue;
                        Rectangle bounds=NotificationAreaPolicy.Bounds(c.BoundingRectangle);
                        Bitmap artwork=!c.IsOffscreen && NotificationRoots.Uncovered(rootHandle,TrayArtwork.InnerBounds(bounds)) ? TrayArtwork.Capture(bounds,false) : null;
                        result.Add(new NotificationItem{Key=key,Name=c.Name,AutomationId=c.AutomationId??"",ClassName=c.ClassName??"",
                            RuntimeId=runtime,Root=rootHandle,Bounds=bounds,
                            Offscreen=c.IsOffscreen,Enabled=c.IsEnabled,Image=artwork,SystemItem=NotificationRoots.IsSystem(c.AutomationId,c.ClassName,c.Name,overflow)});
                    }
                }
                catch(Exception ex){Program.Log("Notification area root: "+ex.GetType().Name);}
            }
            return result;
        }
        static AutomationElement Find(NotificationItem item)
        {
            foreach(IntPtr rootHandle in Roots())
            {
                try
                {
                    var root=AutomationElement.FromHandle(rootHandle); bool overflow=NotificationRoots.IsOverflow(rootHandle);
                    var condition=new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Custom));
                    var elements=root.FindAll(TreeScope.Descendants,condition);
                    for(int i=0;i<elements.Count;i++)
                        if(NotificationAreaPolicy.Candidate(elements[i],root,overflow)&&NotificationAreaPolicy.Same(item,elements[i])) return elements[i];
                }
                catch { }
            }
            return null;
        }
        internal void Read(bool includeHidden,Action<List<NotificationItem>,string> completed)
        {
            if(disposed||jobs.IsAddingCompleted)return;
            jobs.Add(delegate
            {
                var list=Scan(includeHidden); string status=list.Count==0?
                    "Windows has not exposed notification-area items yet. Refresh after the taskbar/Explorer is ready.":"";
                icons.Resolve(list,delegate{completed(list,status);});
            });
        }
        internal void Act(NotificationItem item,Action<string> completed)
        {
            if(disposed||jobs.IsAddingCompleted){completed("Notification-area reader is not available.");return;}
            jobs.Add(delegate
            {
                var element=Find(item);
                if(element==null){completed("That notification item is no longer available. Refresh and try again.");return;}
                completed(NotificationAreaAction.InvokeDefault(element));
            });
        }
        public void Dispose()
        { if(disposed)return;disposed=true;jobs.CompleteAdding();icons.Dispose(); }
    }

}
