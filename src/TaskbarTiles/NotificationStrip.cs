// App tray icons in the centre; taskbar system controls at the far right.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class NotificationRoots
    {
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        internal static bool IsOverflowClass(string cls)
        { return cls == "NotifyIconOverflowWindow" || cls == "TopLevelWindowForOverflowXamlIsland"; }
        internal static bool IsOverflow(IntPtr root)
        { return IsOverflowClass(Native.Class(root)) || IsOverflowClass(Native.Class(GetAncestor(root, 2))); }
        internal static bool VisibleOverflow()
        {
            foreach (string cls in new[] { "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland" })
            { var h = FindWindow(cls, null); if (h != IntPtr.Zero && Native.IsWindowVisible(h)) return true; }
            return false;
        }
        internal static bool Uncovered(IntPtr source, Rectangle rect)
        {
            IntPtr root = GetAncestor(source, 2);
            if (root == IntPtr.Zero) root = source;
            if (!Native.IsWindowVisible(root) || rect.Width < 2 || rect.Height < 2) return false;
            var points = new[] { new Point(rect.Left+1,rect.Top+1), new Point(rect.Right-2,rect.Top+1),
                new Point(rect.Left+1,rect.Bottom-2), new Point(rect.Right-2,rect.Bottom-2),
                new Point(rect.Left+rect.Width/2,rect.Top+rect.Height/2) };
            return points.All(p => GetAncestor(WindowFromPoint(p), 2) == root);
        }
        internal static bool IsSystem(string id, string cls, string name, bool overflow)
        {
            if (overflow) return false;
            string metadata = ((id ?? "") + " " + (cls ?? "")).ToLowerInvariant();
            string[] system = { "clock", "controlcenter", "actioncenter", "notificationcenter", "language", "inputindicator", "texticonview", "battery", "volume", "network", "touchkeyboard", "penmenu" };
            if (system.Any(metadata.Contains)) return true;
            // Real app icons in both promoted and overflow stacks are app entries.
            if (metadata.Contains("notifyicon") || metadata.Contains("normaliconview")) return false;
            string label = (name ?? "").Trim().ToLowerInvariant();
            return label == "quick settings" || label == "notification center" || label == "notification centre" ||
                label == "show desktop" || label.StartsWith("speakers:") || label.StartsWith("battery:");
        }
        internal static bool IsChevron(string id, string cls, string name)
        {
            string metadata = ((id ?? "") + " " + (cls ?? "")).ToLowerInvariant();
            return metadata.Contains("chevron") || metadata.Contains("overflowbutton") ||
                (name ?? "").IndexOf("Show hidden icons", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (name ?? "").Equals("Hidden icon menu", StringComparison.OrdinalIgnoreCase);
        }
        internal static string OpenOverflow()
        {
            var h = FindWindow("Shell_TrayWnd", null);
            if (h == IntPtr.Zero) return "Windows taskbar is not available.";
            try
            {
                var root = AutomationElement.FromHandle(h);
                var candidates = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i].Current;
                    if (IsChevron(c.AutomationId, c.ClassName, c.Name)) return NotificationAreaAction.InvokeDefault(candidates[i]);
                }
            }
            catch (Exception ex) { return "Could not open the Windows tray: " + ex.Message; }
            return "Open the ^ hidden-icons menu on your Windows taskbar once, then reopen Taskbar Tiles.";
        }
    }
    sealed class NotificationStripGeometry
    {
        internal readonly List<Rectangle> Apps = new List<Rectangle>(), System = new List<Rectangle>();
        internal Rectangle Previous, Next, Load;
        internal int PerPage, Page;
        internal static NotificationStripGeometry Build(int width, int top, int icon, int spacing, int apps, int system, int page, float dpi)
        {
            var g = new NotificationStripGeometry();
            int pad = Math.Max(1,(int)Math.Round(22*dpi)), gap = Math.Max(1,spacing), cell = Math.Max(16,icon+(int)Math.Round(10*dpi));
            int right = width-pad, systemCount = Math.Min(system, Math.Max(0,(width/3)/(cell+gap)));
            int systemWidth = systemCount == 0 ? 0 : systemCount*cell+(systemCount-1)*gap;
            for (int i=0;i<systemCount;i++) g.System.Add(new Rectangle(right-systemWidth+i*(cell+gap),top,cell,cell));
            int reserved = Math.Max(pad,systemWidth+pad+gap*2);
            int centreWidth = Math.Max(cell, width-2*reserved), centreLeft=(width-centreWidth)/2;
            bool paged = apps*(cell+gap)-gap > centreWidth;
            int arrows = paged ? 2*(cell+gap) : 0;
            g.PerPage = Math.Max(1,(centreWidth-arrows+gap)/(cell+gap));
            int pages = Math.Max(1,(apps+g.PerPage-1)/g.PerPage);
            g.Page = Math.Max(0,Math.Min(page,pages-1));
            int shown = Math.Min(g.PerPage,Math.Max(0,apps-g.Page*g.PerPage));
            int rowWidth = shown == 0 ? 0 : shown*cell+(shown-1)*gap;
            int x=(width-rowWidth)/2;
            for(int i=0;i<shown;i++) g.Apps.Add(new Rectangle(x+i*(cell+gap),top,cell,cell));
            if(paged)
            {
                g.Previous=new Rectangle(Math.Max(centreLeft,x-cell-gap),top,cell,cell);
                g.Next=new Rectangle(x+rowWidth+gap,top,cell,cell);
            }
            if(apps==0) g.Load=new Rectangle(centreLeft,top,centreWidth,cell);
            return g;
        }
    }
    sealed partial class Switcher
    {
        NotificationAreaReader notificationReader;
        readonly List<NotificationItem> notificationItems = new List<NotificationItem>();
        readonly List<NotificationItem> shownNotifications = new List<NotificationItem>();
        readonly List<Rectangle> notificationRects = new List<Rectangle>();
        readonly System.Windows.Forms.Timer notificationTimer = new System.Windows.Forms.Timer { Interval=2000 };
        Rectangle notificationBand, notificationPrev, notificationNext, loadNotificationRect;
        int notificationPage, notificationPerPage=1, notificationAppCount;
        string notificationStatus="Reading tray apps...";
        bool notificationRefresh;
        ContextMenuStrip notificationMenu;
        void SetupNotificationArea()
        {
            notificationReader = new NotificationAreaReader();
            notificationTimer.Tick += delegate
            {
                // A short scan when the user opens the native overflow also caches
                // its actual artwork. It does not open it automatically or move input.
                if ((Visible && lastMouseHit < 3000) || NotificationRoots.VisibleOverflow()) RefreshNotificationArea();
            };
            notificationTimer.Start(); RefreshNotificationArea();
        }
        void RefreshNotificationArea()
        {
            if(renderingPreview||notificationReader==null||notificationRefresh||closing||notificationMenu!=null)return;
            if(!options.ShowNotificationArea)
            {
                DisposeNotificationImages(notificationItems); notificationItems.Clear(); notificationStatus="";
                if(Visible)LayoutNotificationArea(Width,footerTop,S(22)); return;
            }
            notificationRefresh=true;
            notificationReader.Read(options.ShowAllNotificationItems,delegate(List<NotificationItem> found,string status)
            {
                if(closing){DisposeNotificationImages(found);return;}
                Post(delegate
                {
                    notificationRefresh=false;
                    if (pressedMouseHit >= 3000 || notificationMenu != null) { DisposeNotificationImages(found); return; }
                    var old=notificationItems.ToArray(); notificationItems.Clear(); notificationItems.AddRange(found);
                    notificationStatus=status;
                    if(Visible) { LayoutNotificationArea(Width,footerTop,S(22)); Invalidate(notificationBand); }
                    DisposeNotificationImages(old);
                });
            });
        }
        static void DisposeNotificationImages(IEnumerable<NotificationItem> items)
        { foreach(var item in items)if(item.Image!=null)item.Image.Dispose(); }
        void LayoutNotificationArea(int width,int footer,int pad)
        {
            notificationRects.Clear(); shownNotifications.Clear();
            notificationPrev=notificationNext=notificationBand=loadNotificationRect=Rectangle.Empty;
            if(!options.ShowNotificationArea)return;
            int icon=S(options.NotificationIconSize),cell=icon+S(10),top=footer-cell-S(6);
            var apps=notificationItems.Where(n=>!n.SystemItem).ToList();
            var system=notificationItems.Where(n=>n.SystemItem).ToList();
            if(renderingPreview)
            {
                apps=Enumerable.Range(1,8).Select(n=>new NotificationItem{Name="Tray app "+n}).ToList();
                system=new[]{new NotificationItem{Name="Language"},new NotificationItem{Name="Network"},new NotificationItem{Name="Volume"},new NotificationItem{Name="Clock"}}.ToList();
            }
            notificationAppCount=apps.Count;
            var g=NotificationStripGeometry.Build(width,top,icon,S(options.NotificationIconSpacing),apps.Count,system.Count,notificationPage,scale);
            notificationPerPage=g.PerPage; notificationPage=g.Page;
            for(int i=0;i<g.Apps.Count;i++){notificationRects.Add(g.Apps[i]);shownNotifications.Add(apps[g.Page*g.PerPage+i]);}
            for(int i=0;i<g.System.Count;i++){notificationRects.Add(g.System[i]);shownNotifications.Add(system[i]);}
            notificationPrev=g.Previous;notificationNext=g.Next;loadNotificationRect=g.Load;
            notificationBand=new Rectangle(pad,top-S(4),Math.Max(1,width-2*pad),cell+S(8));
        }
        void PaintNotificationArea(Graphics g,Color text,Color muted,Color accent)
        {
            if(!options.ShowNotificationArea||notificationBand.IsEmpty)return;
            paintPhase="tray and system icons";
            for(int i=0;i<notificationRects.Count;i++)
            {
                Rectangle r=notificationRects[i]; var item=shownNotifications[i];
                if(lastMouseHit==3000+i)DrawingUtil.Round(g,r,S(6),Theme.Card,Theme.Border,1);
                int size=S(options.NotificationIconSize);
                var box=new Rectangle(r.Left+(r.Width-size)/2,r.Top+(r.Height-size)/2,size,size);
                // Actual tray artwork wins over a reconstructed app/count tile.
                if(!DrawMenuImage(g,item.Image,box,"tray artwork"))
                {
                    DrawingUtil.Round(g,box,S(5),Theme.Card,Theme.Border,1);
                    Label(g,TextTools.Initials(item.Name),box,false,accent,true);
                }
            }
            if(!loadNotificationRect.IsEmpty)
            {
                DrawingUtil.Round(g,loadNotificationRect,S(5),Theme.Card,Theme.Border,1);
                Label(g,"Load tray apps...",loadNotificationRect,false,muted,true);
            }
            if(!notificationPrev.IsEmpty){PaintAction(g,notificationPrev,-19,"prev");PaintAction(g,notificationNext,-20,"next");}
        }
        int HitNotificationArea(Point p)
        {
            if(!options.ShowNotificationArea)return -100;
            if(!loadNotificationRect.IsEmpty&&loadNotificationRect.Contains(p))return -22;
            if(!notificationPrev.IsEmpty&&notificationPrev.Contains(p))return -19;
            if(!notificationNext.IsEmpty&&notificationNext.Contains(p))return -20;
            for(int i=0;i<notificationRects.Count;i++)if(notificationRects[i].Contains(p))return 3000+i;
            return -100;
        }
        NotificationItem NotificationAtHit(int hit)
        { int i=hit-3000;return i>=0&&i<shownNotifications.Count?shownNotifications[i]:null; }
        string NotificationTip(int hit)
        {
            if(hit==-19||hit==-20)return hit==-19?"Previous tray apps":"Next tray apps";
            if(hit==-22)return "Open the Windows hidden-icons menu once so its app icons can be read. Then reopen Taskbar Tiles. Your Windows taskbar settings are unchanged.";
            var item=NotificationAtHit(hit);
            return item==null?"":item.Name+"\n"+(item.SystemItem?"Taskbar system control":"Tray application")+" · Left-click: default action · Right-click: actions";
        }
        void PageNotifications(int direction)
        {
            int pages=Math.Max(1,(notificationAppCount+notificationPerPage-1)/notificationPerPage);
            notificationPage=(notificationPage+direction+pages)%pages;
            LayoutNotificationArea(Width,footerTop,S(22));Invalidate(notificationBand);
        }
        void LoadWindowsTray()
        {
            Dismiss();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string error=NotificationRoots.OpenOverflow();
                if(error!=null)Post(delegate{Notify(error);});
            });
        }
        void InvokeNotification(NotificationItem item)
        {
            if(item==null||notificationReader==null)return;
            Dismiss();
            notificationReader.Act(item,delegate(string error){if(error!=null)Post(delegate{Notify(error);});});
        }
        void ShowNotificationActions(NotificationItem item,Point point)
        {
            if(item==null)return;
            if(notificationMenu!=null)notificationMenu.Close();
            var menu=new ContextMenuStrip { BackColor=Theme.Card, ForeColor=Theme.Text };
            menu.Items.Add("Open / default action",null,delegate{InvokeNotification(item);});
            menu.Items.Add("Open Windows hidden tray",null,delegate{LoadWindowsTray();});
            menu.Items.Add("Refresh tray items",null,delegate{BeginInvoke(new Action(RefreshNotificationArea));});
            menu.Items.Add("Copy name",null,delegate{try{Clipboard.SetText(item.Name);}catch{}});
            menu.Closed+=delegate{if(ReferenceEquals(notificationMenu,menu))notificationMenu=null;menu.Dispose();};
            notificationMenu=menu;menu.Show(this,point);
        }
        bool NotificationAreaWheel(MouseEventArgs e)
        {
            if(!options.ShowNotificationArea||notificationBand.IsEmpty||!notificationBand.Contains(e.Location))return false;
            if((ModifierKeys&Keys.Control)!=0)
            {
                int next=Math.Max(16,Math.Min(48,options.NotificationIconSize+(e.Delta<0?-2:2)));
                try{Options.SaveValue("NotificationIconSize",next.ToString());ReloadSettings(true);}catch(Exception ex){Notify(ex.Message);}
            }
            else PageNotifications(e.Delta<0?1:-1);
            return true;
        }
        void ShutdownNotificationArea()
        {
            notificationTimer.Stop();notificationTimer.Dispose();
            if(notificationMenu!=null){var menu=notificationMenu;notificationMenu=null;menu.Close();menu.Dispose();}
            DisposeNotificationImages(notificationItems);notificationItems.Clear();shownNotifications.Clear();
            if(notificationReader!=null){notificationReader.Dispose();notificationReader=null;}
        }
    }
}
