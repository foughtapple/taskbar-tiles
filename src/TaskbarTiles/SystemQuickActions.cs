using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
namespace TaskbarTiles
{
    sealed class QuickAccessLayout
    {
        internal Rectangle Search, Favourites, Recent, Desktop, Clipboard, Help, Display, Bluetooth, TaskManager;
        internal static bool Enabled(Options o)
        { return o.WindowsSearchButton || o.FavouritesButton || o.RecentAppsButton || o.DesktopButton || o.ClipboardButton || o.DisplaySettingsButton || o.BluetoothSettingsButton || o.TaskManagerButton; }
        internal static bool IsHit(int hit)
        { return (hit <= -12 && hit >= -16) || hit == -18 || (hit <= -30 && hit >= -32); }
        internal static QuickAccessLayout Build(int width, int height, float scale, Options o)
        {
            var g = new QuickAccessLayout(); if (!Enabled(o)) return g;
            Func<int,int> s = n => Math.Max(1,(int)Math.Round(n*scale));
            int pad = Math.Min(s(22),Math.Max(1,width/12)), gap=s(8), h=s(o.FooterButtonHeight), y=height-h-s(12);
            var ids = new List<int>(); var preferred=new List<int>(); var minimum=new List<int>();
            Action<int,int,int> add = (id,p,m) => {ids.Add(id);preferred.Add(s(p));minimum.Add(s(m));};
            if(o.WindowsSearchButton)add(-12,o.SearchButtonWidth,120);
            if(o.DesktopButton)add(-14,36,26);
            if(o.ClipboardButton)add(-15,36,26);
            add(-16,36,26);
            if(o.DisplaySettingsButton)add(-30,148,38);
            if(o.BluetoothSettingsButton)add(-31,157,38);
            if(o.TaskManagerButton)add(-32,145,38);
            if(o.RecentAppsButton)add(-18,156,66);
            if(o.FavouritesButton)add(-13,156,66);
            int available=Math.Max(ids.Count,width-2*pad), n=ids.Count;
            gap=Math.Min(gap,Math.Max(0,(available-n)/(Math.Max(1,n-1)*6)));
            int budget=Math.Max(n,available-gap*(n-1));
            int[] sizes=preferred.ToArray();
            if(sizes.Sum()>budget)
            {
                sizes=minimum.ToArray(); int total=sizes.Sum();
                if(total>budget)
                {
                    double factor=budget/(double)total;
                    for(int i=0;i<n;i++)sizes[i]=Math.Max(1,(int)Math.Floor(sizes[i]*factor));
                }
                else
                {
                    int spare=budget-total;
                    // Spend remaining room on the search field first, then labels.
                    for(int i=0;i<n;i++){int take=Math.Min(spare,preferred[i]-sizes[i]);sizes[i]+=take;spare-=take;}
                }
            }
            int x=pad, right=width-pad;
            // Recent apps and Favourites remain anchored at the far right.
            for(int i=n-1;i>=0;i--)
            {
                if(ids[i]!=-13&&ids[i]!=-18)continue;
                var rect=new Rectangle(right-sizes[i],y,sizes[i],h);right=rect.Left-gap;g.Set(ids[i],rect);
            }
            for(int i=0;i<n;i++)
            {
                if(ids[i]==-13||ids[i]==-18)continue;
                var rect=new Rectangle(x,y,sizes[i],h);g.Set(ids[i],rect);x=rect.Right+gap;
            }
            return g;
        }
        void Set(int id,Rectangle r)
        {
            switch(id){case -12:Search=r;break;case -13:Favourites=r;break;case -18:Recent=r;break;case -14:Desktop=r;break;
                case -15:Clipboard=r;break;case -16:Help=r;break;case -30:Display=r;break;case -31:Bluetooth=r;break;case -32:TaskManager=r;break;}
        }
        internal IEnumerable<Rectangle> Buttons()
        { return new[]{Search,Favourites,Recent,Desktop,Clipboard,Help,Display,Bluetooth,TaskManager}.Where(r=>!r.IsEmpty); }
        internal int Hit(Point p)
        {
            var rects=new[]{Search,Favourites,Recent,Desktop,Clipboard,Help,Display,Bluetooth,TaskManager};
            int[] ids={-12,-13,-18,-14,-15,-16,-30,-31,-32};
            for(int i=0;i<rects.Length;i++)if(!rects[i].IsEmpty&&rects[i].Contains(p))return ids[i];
            return -100;
        }
    }
    static class SystemQuickActions
    {
        internal static string Target(int hit)
        {
            if(hit==-30)return "ms-settings:display";
            if(hit==-31)return "ms-settings:bluetooth";
            if(hit==-32)return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"Taskmgr.exe");
            throw new ArgumentException("Unknown system shortcut.");
        }
        internal static void Open(int hit)
        { Process.Start(new ProcessStartInfo(Target(hit)){UseShellExecute=true}); }
    }
}
