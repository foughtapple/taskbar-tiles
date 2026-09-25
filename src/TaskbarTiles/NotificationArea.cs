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
        internal bool Offscreen, Enabled;
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
            if((name??"").StartsWith("Taskbar Tiles",StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        internal static bool Candidate(AutomationElement element,AutomationElement root,bool overflow)
        {
            try
            {
                var c=element.Current;
                if(string.IsNullOrWhiteSpace(c.Name)||IsExcluded(c.AutomationId,c.ClassName,c.Name)) return false;
                if(c.ControlType!=ControlType.Button && c.ControlType!=ControlType.ListItem && c.ControlType!=ControlType.MenuItem) return false;
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

    static class NotificationAreaInput
    {
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { internal ushort vk,scan; internal uint flags,time; internal UIntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] internal KEYBDINPUT key; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { internal uint type; internal UNION data; }
        [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] input,int size);
        static INPUT Key(ushort key,bool up)
        { return new INPUT{type=1,data=new UNION{key=new KEYBDINPUT{vk=key,flags=up?2u:0u}}}; }
        internal static void ContextMenuKey()
        {
            if(Native.LaunchModifiersDown()||Native.Down(0x10)||Native.Down(0x5D))
                throw new InvalidOperationException("Release keyboard modifiers before opening the native tray menu.");
            var input=new[]{Key(0x5D,false),Key(0x5D,true)};
            if(SendInput((uint)input.Length,input,Marshal.SizeOf(typeof(INPUT)))!=input.Length)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Windows rejected the context-menu key.");
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
        internal static string OpenNativeMenu(AutomationElement element)
        {
            try
            {
                bool focused=false;
                try { element.SetFocus(); focused=true; } catch { }
                if(!focused)
                {
                    object pattern;
                    if(element.TryGetCurrentPattern(SelectionItemPattern.Pattern,out pattern))
                    { ((SelectionItemPattern)pattern).Select(); focused=true; }
                }
                if(!focused) return "Windows does not expose keyboard focus for this notification item.";
                Thread.Sleep(80);
                NotificationAreaInput.ContextMenuKey();
                return null;
            }
            catch(ElementNotAvailableException) { return "That notification item changed. Reopen Taskbar Tiles and try again."; }
            catch(Exception ex) { return "Could not open the native tray menu: "+ex.Message; }
        }
    }

    sealed class NotificationIconWorker : IDisposable
    {
        readonly BlockingCollection<Action> jobs=new BlockingCollection<Action>();
        readonly Dictionary<string,Bitmap> cache=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);
        List<FavouriteEntry> catalog;
        volatile bool disposed;
        internal NotificationIconWorker()
        {
            var thread=new Thread(new ThreadStart(delegate
            {
                foreach(var job in jobs.GetConsumingEnumerable())
                    try { job(); } catch(Exception ex) { Program.Log("Notification icon worker: "+ex.GetType().Name); }
                foreach(var image in cache.Values) if(image!=null) image.Dispose();
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
                else if(cls=="NotifyIconOverflowWindow") overflow.Add(h);
                return true;
            },IntPtr.Zero);
            primary.AddRange(secondary); primary.AddRange(overflow); return primary;
        }
        static List<NotificationItem> Scan()
        {
            var result=new List<NotificationItem>(); var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(IntPtr rootHandle in Roots())
            {
                try
                {
                    var root=AutomationElement.FromHandle(rootHandle); bool overflow=Native.Class(rootHandle)=="NotifyIconOverflowWindow";
                    var condition=new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem));
                    var elements=root.FindAll(TreeScope.Descendants,condition);
                    for(int i=0;i<elements.Count;i++)
                    {
                        var element=elements[i];
                        if(!NotificationAreaPolicy.Candidate(element,root,overflow)) continue;
                        var c=element.Current; string runtime=NotificationAreaPolicy.Runtime(element);
                        string key=NotificationAreaPolicy.Key(runtime,c.AutomationId,c.ClassName,c.Name);
                        if(!seen.Add(key)) continue;
                        result.Add(new NotificationItem{Key=key,Name=c.Name,AutomationId=c.AutomationId??"",ClassName=c.ClassName??"",
                            RuntimeId=runtime,Root=rootHandle,Bounds=NotificationAreaPolicy.Bounds(c.BoundingRectangle),
                            Offscreen=c.IsOffscreen,Enabled=c.IsEnabled});
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
                    var root=AutomationElement.FromHandle(rootHandle); bool overflow=Native.Class(rootHandle)=="NotifyIconOverflowWindow";
                    var condition=new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
                        new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem));
                    var elements=root.FindAll(TreeScope.Descendants,condition);
                    for(int i=0;i<elements.Count;i++)
                        if(NotificationAreaPolicy.Candidate(elements[i],root,overflow)&&NotificationAreaPolicy.Same(item,elements[i])) return elements[i];
                }
                catch { }
            }
            return null;
        }
        internal void Read(Action<List<NotificationItem>,string> completed)
        {
            if(disposed||jobs.IsAddingCompleted)return;
            jobs.Add(delegate
            {
                var list=Scan(); string status=list.Count==0?
                    "Windows has not exposed notification-area items yet. Refresh after the taskbar/Explorer is ready.":"";
                icons.Resolve(list,delegate{completed(list,status);});
            });
        }
        internal void Act(NotificationItem item,bool nativeMenu,Action<string> completed)
        {
            if(disposed||jobs.IsAddingCompleted){completed("Notification-area reader is not available.");return;}
            jobs.Add(delegate
            {
                var element=Find(item);
                if(element==null){completed("That notification item is no longer available. Refresh and try again.");return;}
                completed(nativeMenu?NotificationAreaAction.OpenNativeMenu(element):NotificationAreaAction.InvokeDefault(element));
            });
        }
        public void Dispose()
        { if(disposed)return;disposed=true;jobs.CompleteAdding();icons.Dispose(); }
    }

    sealed partial class Switcher
    {
        NotificationAreaReader notificationReader;
        readonly List<NotificationItem> notificationItems=new List<NotificationItem>();
        readonly List<Rectangle> notificationRects=new List<Rectangle>();
        Rectangle notificationBand=Rectangle.Empty,notificationPrev=Rectangle.Empty,notificationNext=Rectangle.Empty;
        int notificationPage,notificationPerPage=1;
        string notificationStatus="Reading notification area...";
        bool notificationRefresh;
        ContextMenuStrip notificationMenu;

        void SetupNotificationArea()
        {
            notificationReader=new NotificationAreaReader();
            RefreshNotificationArea();
        }
        void RefreshNotificationArea()
        {
            if(renderingPreview||notificationReader==null||notificationRefresh||closing)return;
            if(!options.ShowNotificationArea)
            {
                DisposeNotificationImages(); notificationItems.Clear(); notificationStatus=""; notificationPage=0;
                if(Visible)LayoutMenu(); return;
            }
            notificationRefresh=true;
            notificationReader.Read(delegate(List<NotificationItem> found,string status)
            {
                if(closing){DisposeNotificationImages(found);return;}
                Post(delegate
                {
                    notificationRefresh=false; DisposeNotificationImages();
                    notificationItems.Clear(); notificationItems.AddRange(found); notificationStatus=status; notificationPage=0;
                    if(Visible)LayoutMenu(); else Invalidate();
                });
            });
        }
        static void DisposeNotificationImages(IEnumerable<NotificationItem> items)
        { foreach(var item in items)if(item.Image!=null)item.Image.Dispose(); }
        void DisposeNotificationImages(){DisposeNotificationImages(notificationItems);}
        void LayoutNotificationArea(int width,int footer,int pad)
        {
            notificationRects.Clear(); notificationPrev=notificationNext=notificationBand=Rectangle.Empty;
            if(!options.ShowNotificationArea)return;
            int icon=S(options.NotificationIconSize), cell=icon+S(10);
            int count=renderingPreview?6:notificationItems.Count;
            int top=footer-cell-S(6);
            notificationRects.AddRange(NotificationAreaMetrics.Cells(count,notificationPage,width,top,icon,S(options.NotificationIconSpacing),
                out notificationPrev,out notificationNext,out notificationPerPage));
            int pages=Math.Max(1,(count+notificationPerPage-1)/notificationPerPage);
            notificationPage=Math.Max(0,Math.Min(notificationPage,pages-1));
            notificationBand=new Rectangle(pad,top-S(4),Math.Max(1,width-pad*2),cell+S(8));
        }
        void PaintNotificationArea(Graphics g,Color text,Color muted,Color accent)
        {
            if(!options.ShowNotificationArea||notificationBand.IsEmpty)return;
            paintPhase="notification area";
            int start=notificationPage*notificationPerPage;
            for(int i=0;i<notificationRects.Count;i++)
            {
                Rectangle r=notificationRects[i]; bool hover=lastMouseHit==3000+i;
                if(hover)DrawingUtil.Round(g,r,S(8),Color.FromArgb(45,59,78),Color.FromArgb(76,95,121),1);
                int size=S(options.NotificationIconSize);
                var box=new Rectangle(r.Left+(r.Width-size)/2,r.Top+(r.Height-size)/2,size,size);
                Bitmap image=null; string name="Notification item "+(start+i+1);
                if(!renderingPreview&&start+i<notificationItems.Count){image=notificationItems[start+i].Image;name=notificationItems[start+i].Name;}
                if(!DrawMenuImage(g,image,box,"notification icon"))
                {
                    DrawingUtil.Round(g,box,S(7),Color.FromArgb(39,52,70),Color.FromArgb(61,78,101),1);
                    Label(g,TextTools.Initials(name),box,false,accent,true);
                }
            }
            int count=renderingPreview?6:notificationItems.Count;
            if(count==0&&!string.IsNullOrEmpty(notificationStatus))
                Label(g,notificationStatus,notificationBand,false,muted,true);
            if(count>notificationPerPage)
            {
                PaintAction(g,notificationPrev,-19,"prev"); PaintAction(g,notificationNext,-20,"next");
            }
        }
        int HitNotificationArea(Point p)
        {
            if(!options.ShowNotificationArea||notificationBand.IsEmpty)return -100;
            int count=renderingPreview?6:notificationItems.Count;
            if(count>notificationPerPage&&notificationPrev.Contains(p))return -19;
            if(count>notificationPerPage&&notificationNext.Contains(p))return -20;
            for(int i=0;i<notificationRects.Count;i++)if(notificationRects[i].Contains(p))return 3000+i;
            return -100;
        }
        NotificationItem NotificationAtHit(int hit)
        {
            int index=notificationPage*notificationPerPage+hit-3000;
            return index>=0&&index<notificationItems.Count?notificationItems[index]:null;
        }
        string NotificationTip(int hit)
        {
            if(hit==-19||hit==-20)return hit==-19?"Previous notification items":"Next notification items";
            var item=NotificationAtHit(hit);
            return item==null?"":item.Name+"\nLeft-click: normal tray action · Right-click: actions and native tray menu";
        }
        void InvokeNotification(NotificationItem item,bool nativeMenu)
        {
            if(item==null||notificationReader==null)return;
            Dismiss();
            notificationReader.Act(item,nativeMenu,delegate(string error)
            { if(error!=null)Post(delegate{Notify(error);}); });
        }
        void ShowNotificationActions(NotificationItem item,Point clientPoint)
        {
            if(item==null)return;
            if(notificationMenu!=null){notificationMenu.Close();notificationMenu.Dispose();}
            var menu=new ContextMenuStrip();
            var open=menu.Items.Add("Open / default action"); open.Click+=delegate{InvokeNotification(item,false);};
            var native=menu.Items.Add("Open native tray menu"); native.Click+=delegate{InvokeNotification(item,true);};
            menu.Items.Add(new ToolStripSeparator());
            var copy=menu.Items.Add("Copy name"); copy.Click+=delegate{try{Clipboard.SetText(item.Name);}catch{}};
            var settings=menu.Items.Add("Windows taskbar settings"); settings.Click+=delegate
            { Dismiss();try{Process.Start("ms-settings:taskbar");}catch(Exception ex){Notify(ex.Message);} };
            menu.Items.Add(new ToolStripSeparator());
            var refresh=menu.Items.Add("Refresh notification area"); refresh.Click+=delegate{RefreshNotificationArea();};
            menu.Closed+=delegate{if(ReferenceEquals(notificationMenu,menu))notificationMenu=null;menu.Dispose();};
            notificationMenu=menu; menu.Show(this,clientPoint);
        }
        bool NotificationAreaWheel(MouseEventArgs e)
        {
            if(!options.ShowNotificationArea||notificationBand.IsEmpty||!notificationBand.Contains(e.Location))return false;
            int count=notificationItems.Count, pages=Math.Max(1,(count+notificationPerPage-1)/notificationPerPage);
            if((ModifierKeys&Keys.Control)!=0)
            {
                int next=Math.Max(16,Math.Min(48,options.NotificationIconSize+(e.Delta<0?-2:2)));
                try{Options.SaveValue("NotificationIconSize",next.ToString());ReloadSettings(true);}catch(Exception ex){Notify(ex.Message);}
                return true;
            }
            if(pages>1){notificationPage=(notificationPage+(e.Delta<0?1:-1)+pages)%pages;LayoutMenu();return true;}
            return true;
        }
        void ShutdownNotificationArea()
        {
            if(notificationMenu!=null){notificationMenu.Close();notificationMenu.Dispose();notificationMenu=null;}
            DisposeNotificationImages(); notificationItems.Clear();
            if(notificationReader!=null){notificationReader.Dispose();notificationReader=null;}
        }
    }
}
