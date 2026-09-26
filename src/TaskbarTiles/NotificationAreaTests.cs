// Notification-area geometry/policy checks plus a disposable Windows accessibility fixture.
// The fixture tests default Invoke routing; it does not inspect
// or activate the runner's real notification area.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace TaskbarTiles
{
    static class NotificationAreaTests
    {
        static int checks;
        static void Require(bool value,string name)
        { checks++; if(!value)throw new InvalidOperationException("FAILED: "+name); }

        internal static void Run(StringBuilder log)
        {
            checks=0;
            Require(NotificationAreaPolicy.IsContainer("SystemTrayIconContainer",""),"Windows 11 system-tray container recognised");
            Require(NotificationAreaPolicy.IsContainer("","TrayNotifyWnd"),"legacy tray container recognised");
            Require(NotificationAreaPolicy.IsExcluded("","","Show hidden icons"),"overflow chevron excluded");
            Require(NotificationAreaPolicy.IsExcluded("","TaskListButton","Steam"),"taskbar app button excluded");
            Require(NotificationAreaPolicy.StrongNameMatch("Steam - 2 notifications","Steam"),"status suffix can resolve an icon");
            Require(!NotificationAreaPolicy.StrongNameMatch("OneDrive","Drive"),"weak substring does not guess an icon");
            int discord;
            Require(DiscordNotificationVisual.TryCount("Discord",out discord)&&discord==0,"Discord without an explicit count renders zero");
            Require(DiscordNotificationVisual.TryCount("Discord - 1 notification",out discord)&&discord==1,"Discord singular notification count parsed");
            Require(DiscordNotificationVisual.TryCount("Discord, 27 unread messages",out discord)&&discord==27,"Discord unread message count parsed");
            Require(DiscordNotificationVisual.TryCount("Discord (105)",out discord)&&discord==105,"Discord parenthesised count parsed");
            Require(DiscordNotificationVisual.Background(0)==Color.Black&&DiscordNotificationVisual.Background(1)==Color.FromArgb(237,66,69),"Discord zero black and positive count red");
            Require(DiscordNotificationVisual.FontPixels(48,"8")>DiscordNotificationVisual.FontPixels(48,"999+"),"Discord count text scales down only for wider counts");
            Require(!DiscordNotificationVisual.TryCount("Steam - 4 notifications",out discord),"non-Discord tray items keep their normal icons");

            int layouts=0;
            foreach(int width in new[]{520,760,1200,2200,3600})
                foreach(int size in new[]{16,26,36,48})
                    foreach(int spacing in new[]{2,8,18,24})
                        foreach(int count in new[]{0,1,4,10,20,50})
                        {
                            Rectangle prev,next; int per;
                            var cells=NotificationAreaMetrics.Cells(count,0,width,100,size,spacing,out prev,out next,out per);
                            Require(per>=1,"notification row has capacity");
                            Require(cells.All(r=>r.Left>=0&&r.Right<=width&&r.Width>0&&r.Height>0),"notification cells stay inside menu");
                            for(int i=1;i<cells.Count;i++)Require(!cells[i-1].IntersectsWith(cells[i]),"notification cells do not overlap");
                            if(count>per)Require(!prev.IsEmpty&&!next.IsEmpty&&prev.Left>=0&&next.Right<=width,"page buttons fit");
                            layouts++;
                        }
            var defaults=new Options();
            Require(defaults.ShowNotificationArea&&defaults.NotificationShowAllItems,"notification row and all-item inventory default on");
            var off=new Options{ShowNotificationArea=false};
            var on=new Options{ShowNotificationArea=true,NotificationIconSize=26};
            Require(NotificationAreaMetrics.LogicalHeight(off)==0,"disabled row takes no height");
            Require(NotificationAreaMetrics.LogicalHeight(on)>=50,"enabled row reserves compact band");
            using(var flat=new Bitmap(24,24))
            using(var flatG=Graphics.FromImage(flat))
            {
                flatG.Clear(Color.Black);
                Require(!NotificationVisualCapture.HasVisualSignal(flat),"uniform root crop rejected as a fake tray image");
                flatG.FillEllipse(Brushes.White,5,5,14,14);
                Require(NotificationVisualCapture.HasVisualSignal(flat),"real visual contrast accepted for tray-image mirroring");
            }
            log.AppendLine("PASS: "+layouts+" notification-row layouts and "+checks+" policy/geometry assertions. No tray actions were sent.");
        }

        internal static int Fixture(string[] args)
        {
            if(args.Length<3)return 2;
            EventWaitHandle ready=null,invoked=null;
            try
            {
                ready=EventWaitHandle.OpenExisting(args[1]); invoked=EventWaitHandle.OpenExisting(args[2]);
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using(var form=new Form{Text="Taskbar Tiles notification action fixture",Width=420,Height=220,StartPosition=FormStartPosition.CenterScreen})
                {
                    var button=new Button{Text="Fixture tray item",Name="FixtureTrayItem",Width=220,Height=60,Left=90,Top=60};
                    button.Click+=delegate{invoked.Set();};
                    form.Controls.Add(button);
                    form.Shown+=delegate{button.Focus();ready.Set();};
                    Application.Run(form);
                }
                return 0;
            }
            catch{return 3;}
            finally{if(ready!=null)ready.Dispose();if(invoked!=null)invoked.Dispose();}
        }

        internal static int RunNative()
        {
            var log=new StringBuilder();
            try
            {
                Run(log);
                string token=Guid.NewGuid().ToString("N");
                string readyName="Local\\TaskbarTiles.NotificationReady."+token;
                string invokedName="Local\\TaskbarTiles.NotificationInvoke."+token;
                using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,readyName))
                using(var invoked=new EventWaitHandle(false,EventResetMode.AutoReset,invokedName))
                using(var process=Process.Start(new ProcessStartInfo{
                    FileName=Application.ExecutablePath,
                    Arguments="--test-notification-target "+readyName+" "+invokedName,
                    UseShellExecute=false,WorkingDirectory=Program.Home}))
                {
                    Require(process!=null&&ready.WaitOne(10000),"fixture became ready");
                    IntPtr window=IntPtr.Zero;
                    var wait=Stopwatch.StartNew();
                    while(wait.ElapsedMilliseconds<5000)
                    {
                        process.Refresh(); window=process.MainWindowHandle;
                        if(window!=IntPtr.Zero)break; Thread.Sleep(40);
                    }
                    Require(window!=IntPtr.Zero,"fixture window handle found");
                    var root=AutomationElement.FromHandle(window);
                    var button=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"Fixture tray item"));
                    Require(button!=null,"fixture action element found through UI Automation");
                    using(var snapshot=NotificationVisualCapture.CaptureRoot(window))
                    {
                        Rectangle buttonBounds=NotificationAreaPolicy.Bounds(button.Current.BoundingRectangle);
                        using(var visual=NotificationVisualCapture.Crop(snapshot,buttonBounds))
                            Require(visual!=null&&visual.Width>4&&visual.Height>4,"PrintWindow tray-image path captures and crops a real cross-process control");
                    }
                    string error=NotificationAreaAction.InvokeDefault(button);
                    Require(error==null&&invoked.WaitOne(5000),"default notification action invoked cross-process");
                    try{if(!process.HasExited)process.Kill();}catch{}
                    process.WaitForExit(5000);
                }
                log.AppendLine("PASS: disposable cross-process Invoke routing. The real Windows tray was not touched.");
                File.WriteAllText(Path.Combine(Program.Home,"notification-area-test.log"),log.ToString());
                return 0;
            }
            catch(Exception ex)
            {
                log.AppendLine(ex.ToString());
                try{File.WriteAllText(Path.Combine(Program.Home,"notification-area-test.log"),log.ToString());}catch{}
                return 1;
            }
        }
    }
}
