// Regression tests for layout containment, window priorities and tray separation.
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
namespace TaskbarTiles
{
    static class OrganisationTests
    {
        static int checks;
        static void Require(bool value,string name){checks++;if(!value)throw new InvalidOperationException("FAILED: "+name);}
        internal static void Run(StringBuilder log)
        {
            checks=0;
            var slots=WindowPriorityModel.Read("");
            Require(slots.Length==50&&slots.All(a=>a==null),"50 empty priority slots");
            var chrome=new PriorityApp{Key="chrome",Name="Google Chrome"};
            var discord=new PriorityApp{Key="discord",Name="Discord"};
            var steam=new PriorityApp{Key="steam",Name="Steam"};
            slots=WindowPriorityModel.Assign(slots,chrome,1);
            slots=WindowPriorityModel.Assign(slots,discord,1);
            Require(slots[0].Key=="discord"&&slots[1].Key=="chrome","occupied rank inserts and shifts down");
            slots=WindowPriorityModel.Assign(slots,steam,50);
            slots=WindowPriorityModel.Assign(slots,chrome,1);
            Require(slots[0].Key=="chrome"&&slots[1].Key=="discord"&&WindowPriorityModel.Rank(slots,"steam")>0,"moving an assigned app preserves all other entries");
            var roundtrip=WindowPriorityModel.Read(WindowPriorityModel.Write(slots));
            Require(WindowPriorityModel.Rank(roundtrip,"CHROME")==1,"priority serialization and case-insensitive identity");
            var full=Enumerable.Range(1,50).Select(n=>new PriorityApp{Key="app"+n,Name="App "+n}).ToArray();
            bool rejected=false;try{WindowPriorityModel.Assign(full,new PriorityApp{Key="new",Name="New"},1);}catch(InvalidOperationException){rejected=true;}
            Require(rejected&&full.Last().Key=="app50","full list rejects insertion instead of losing number 50");
            var moved=WindowPriorityModel.Assign(full,full[49],1);
            Require(moved.Length==50&&moved[0].Key=="app50"&&moved[49].Key=="app49","full list can reorder existing apps");
            rejected=false;try{WindowPriorityModel.Assign(slots,steam,51);}catch(ArgumentException){rejected=true;}
            Require(rejected,"priority range enforced");
            var windows=new[]{new WindowItem{Title="Zulu",PriorityKey="chrome"},new WindowItem{Title="Alpha",PriorityKey="unknown"},new WindowItem{Title="Beta",PriorityKey="chrome"},new WindowItem{Title="Delta",PriorityKey="discord"}};
            Require(WindowPriorityModel.Sort(windows,0,slots).SequenceEqual(windows),"Recent preserves Windows order");
            Require(WindowPriorityModel.Sort(windows,1,slots).Select(w=>w.Title).SequenceEqual(new[]{"Alpha","Beta","Delta","Zulu"}),"A-Z sorts active window titles");
            Require(WindowPriorityModel.Sort(windows,2,slots).Select(w=>w.Title).SequenceEqual(new[]{"Zulu","Beta","Delta","Alpha"}),"Priority groups assigned apps and keeps same-app ordering stable");
            Require(new Options().WindowSortMode==0&&new Options().ShowPriorityButtons,"Recent default with P buttons enabled");
            Require(SystemQuickActions.Target(-30)=="ms-settings:display"&&SystemQuickActions.Target(-31)=="ms-settings:bluetooth"&&Path.GetFileName(SystemQuickActions.Target(-32)).Equals("Taskmgr.exe",StringComparison.OrdinalIgnoreCase),"bottom shortcuts use exact system targets without a command shell");
            Require(NotificationRoots.IsOverflowClass("TopLevelWindowForOverflowXamlIsland")&&NotificationRoots.IsOverflowClass("NotifyIconOverflowWindow"),"Windows 11 and classic overflow roots recognised");
            Require(!NotificationRoots.IsSystem("","SystemTray.NormalIconView","Discord",false),"promoted app remains in the middle tray group");
            Require(!NotificationRoots.IsSystem("SystemTrayIcon","SystemTray.NormalIconView","Steam",true),"overflow items are tray apps");
            Require(NotificationRoots.IsSystem("SystemTray.Clock","","8:13 AM",false),"clock belongs at far right");
            Require(NotificationRoots.IsChevron("OverflowChevron","","",false)==false,"placeholder");
            foreach(float dpi in new[]{.75f,1f,1.5f,2.25f})
            foreach(int logicalWidth in new[]{640,900,1380,2200})
            {
                int width=(int)Math.Round(logicalWidth*dpi),height=(int)Math.Round(680*dpi);
                foreach(bool preview in new[]{false,true})
                {
                    var g=SettingsWorkspaceGeometry.Build(new Size(width,height),dpi,preview);
                    Rectangle bounds=new Rectangle(0,0,width,height);
                    Require(bounds.Contains(g.Navigation)&&bounds.Contains(g.Editor)&&!g.Navigation.IntersectsWith(g.Editor),"settings sidebar never covers text");
                    if(preview)Require(bounds.Contains(g.Preview)&&!g.Preview.IntersectsWith(g.Editor)&&!g.Preview.IntersectsWith(g.Navigation),"live preview owns a separate rectangle");
                }
                var footer=QuickAccessLayout.Build(width,height,dpi,new Options());
                var buttons=footer.Buttons().ToArray();
                for(int i=0;i<buttons.Length;i++)
                {
                    Require(new Rectangle(0,0,width,height).Contains(buttons[i]),"every bottom shortcut fits");
                    for(int j=i+1;j<buttons.Length;j++)Require(!buttons[i].IntersectsWith(buttons[j]),"bottom buttons do not overlap");
                }
                foreach(int count in new[]{0,1,8,30,60})
                {
                    var tray=NotificationStripGeometry.Build(width,100,(int)(26*dpi),(int)(8*dpi),count,5,0,dpi);
                    Require(tray.System.Count==5,"all five system controls retained");
                    var rects=tray.Apps.Concat(tray.System).Concat(new[]{tray.Previous,tray.Next}).Where(r=>!r.IsEmpty).ToArray();
                    for(int i=0;i<rects.Length;i++)
                    {
                        Require(rects[i].Left>=0&&rects[i].Right<=width,"tray groups fit width");
                        for(int j=i+1;j<rects.Length;j++)Require(!rects[i].IntersectsWith(rects[j]),"tray apps, arrows and right-hand system controls do not overlap");
                    }
                    if(tray.Apps.Count>0){var total=tray.Apps.Aggregate(Rectangle.Union);Require(Math.Abs((total.Left+total.Right)/2.0-width/2.0)<=1,"tray apps centre on the window");}
                }
            }
            log.AppendLine("PASS: "+checks+" priority, sorting, separate settings columns, footer shortcuts and split-tray geometry assertions.");
        }
        internal static int RunNative()
        {
            var log=new StringBuilder();
            try
            {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                Run(log);
                using(var form=new SettingsWindow(new Options{HideOnFocusLoss=false},delegate(Options o){}))
                {
                    form.Show();Application.DoEvents();
                    foreach(var size in new[]{new Size(1380,840),new Size(900,650)})
                    {
                        form.ClientSize=size;form.PerformLayout();Application.DoEvents();form.AssertSettingsWorkspace();
                        using(var image=new Bitmap(form.Width,form.Height))
                        {form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(Program.Home,"settings-workspace-"+size.Width+".png"),System.Drawing.Imaging.ImageFormat.Png);}
                    }
                    form.Close();
                }
                var slots=Enumerable.Range(1,50).Select(n=>new PriorityApp{Key="test"+n,Name="Example app "+n}).ToArray();
                using(var picker=new PriorityPicker(slots,"Example app",1,new Point(30,30)))
                {
                    picker.Show();Application.DoEvents();
                    Require(picker.Choices.Items.Count==50,"native dropdown contains all 50 named slots");
                    Require(picker.Choices.ClientSize.Height/picker.Choices.ItemHeight<=10,"native dropdown requires scrolling after ten choices");
                    using(var image=new Bitmap(picker.Width,picker.Height))
                    {picker.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(Program.Home,"priority-picker.png"),System.Drawing.Imaging.ImageFormat.Png);}
                    picker.Close();
                }
                log.AppendLine("PASS: real Settings editor containment at wide/narrow sizes and ten-row native priority picker.");
                File.WriteAllText(Path.Combine(Program.Home,"organisation-test.log"),log.ToString());return 0;
            }
            catch(Exception ex){log.AppendLine(ex.ToString());File.WriteAllText(Path.Combine(Program.Home,"organisation-test.log"),log.ToString());return 1;}
        }
    }
}
