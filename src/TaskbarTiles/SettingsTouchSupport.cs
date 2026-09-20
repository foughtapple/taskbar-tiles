using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarTiles
{
    sealed partial class SettingsWindow
    {
        void AddTouchSupportPage()
        {
            var p=Page("Touch screen monitor support");
            Section(p,"Touch Return — experimental, detection first","Use a selected touchscreen temporarily, then return to the previous window and mouse position. Input is observed, never intercepted or replayed. Unsupported input paths stay inactive.");
            Check(p,"TouchSupportEnabled","Enable Touch Return on explicitly tested monitors");
            var setup=Theme.Button("Monitors & input detection test...",340);
            setup.Click+=delegate
            {
                using(var dialog=new TouchMonitorDialog(edit.TouchMonitorRules))
                    if(dialog.ShowDialog(this)==DialogResult.OK) { edit.TouchMonitorRules=dialog.Result; QueuePreview(); }
            };
            p.Controls.Add(setup);
            settingHints.SetToolTip(setup,"First select a display, run the passive test in another app, then associate its touch/pen device. This edits a draft. Main Settings Apply saves; click-away discards.");
            Number(p,"TouchReturnDelayMs","Touch return delay","Milliseconds after the final explicit contact UP. Further contact restarts the delay. Default: 1000.",250,60000,250);
            Number(p,"PenReturnDelayMs","Pen return delay","Milliseconds after the pen finishes. Default: 2000. Mixed touch/pen uses the longer applicable delay.",250,60000,250);
            Check(p,"TouchWaitForHover","Wait until the pen leaves hover range (requires detectable hover)");
            Number(p,"TouchReturnAction","Return action","0 = focus and cursor; 1 = focus only; 2 = cursor only. No synthetic click is used.",0,2,1);
            Check(p,"TouchTypingCancels","Typing cancels pending return");
            Check(p,"TouchPauseForMenus","Wait while Windows reports an open menu or standard dialog");
            Section(p,"Mouse always wins","Physical mouse/trackpad movement, clicks and scrolling always cancel. Unknown input is never treated as touch merely because of its location. Alt+Tab and other deliberate navigation also cancel.");
            TouchText(p,"TouchExcludedApps","Never auto-return from these apps","Exact executable names separated by semicolons, for example drawing.exe;editor.exe. Empty by default. No application settings are changed.");
            Section(p,"Optional global shortcuts","Leave blank to avoid replacing existing shortcuts. Example syntax: Ctrl+Alt+F8. Conflicts are reported, not overwritten. Tray Pause / Stay here / Return now are also available.");
            TouchText(p,"TouchPauseShortcut","Pause / resume","Optional shortcut. A paused session is discarded.");
            TouchText(p,"TouchStayShortcut","Stay here","Optional shortcut. Turning Stay here off starts a fresh countdown; it never runs an overdue timer.");
            TouchText(p,"TouchReturnShortcut","Return now","Optional shortcut. Waits for contacts and modifiers to end. Cannot override an unknown input state.");
            Section(p,"Compatibility and limitations","The background reader requires complete HID digitizer reports and a corroborating pre-touch snapshot. Some spacedesk/Apollo/Moonlight modes expose only a virtual mouse or omit these signals. In that case this release provides diagnostics but leaves automatic return unavailable. Test each input path separately. A successful local test does not certify every app or driver.");
        }
        void TouchText(FlowLayoutPanel panel,string key,string label,string help)
        {
            Section(panel,label,help);
            var box=new TextBox {Width=650,Text=Convert.ToString(typeof(Options).GetField(key).GetValue(edit)),BackColor=Theme.Card,ForeColor=Theme.Text};
            fields[key]=box;panel.Controls.Add(box);settingHints.SetToolTip(box,help);
        }
        void AddShortcutRecoveryPage()
        {
            var p=Page("Shortcut health");
            Section(p,"Shortcut recovery","A transient menu error no longer leaves Alt+Tab permanently disabled. The input hook is renewed between gestures, and launching the installed app again repairs the resident copy rather than requiring a second process.");
            var repair=Theme.Button("Repair shortcuts now",240);
            repair.Click+=delegate{var form=Application.OpenForms.OfType<Switcher>().FirstOrDefault();if(form!=null)form.RequestShortcutRepair();};p.Controls.Add(repair);
            var log=Theme.Button("Open shortcut diagnostics",240);
            log.Click+=delegate{try{System.Diagnostics.Process.Start("notepad.exe","\""+ShortcutDiagnostics.PathName+"\"");}catch(Exception ex){MessageBox.Show(this,ex.Message);}};p.Controls.Add(log);
            Section(p,"Mouse button setup","Use the X-Mouse Run Application command ending in --toggle, or Ctrl+Alt+Space. These invoke Taskbar Tiles directly, independently of Alt+Tab interception. A game-specific X-Mouse profile set to Windows ALT-TAB can still invoke the Windows switcher instead.");
        }
    }
    sealed class TouchMonitorMap:Control
    {
        internal List<MonitorData> Monitors=new List<MonitorData>();
        internal string Selected="";
        internal Action<string> Choose;
        readonly Dictionary<string,Rectangle> cells=new Dictionary<string,Rectangle>();
        internal TouchMonitorMap(){DoubleBuffered=true;Height=160;BackColor=Theme.Background;}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);cells.Clear();if(Monitors.Count==0)return;
            Rectangle union=Monitors.Select(m=>m.Bounds).Aggregate(Rectangle.Union);
            double scale=Math.Min(Math.Max(1,Width-24)/(double)Math.Max(1,union.Width),Math.Max(1,Height-24)/(double)Math.Max(1,union.Height));
            foreach(var m in Monitors)
            {
                var r=new Rectangle((Width-(int)(union.Width*scale))/2+(int)((m.Bounds.Left-union.Left)*scale),12+(int)((m.Bounds.Top-union.Top)*scale),Math.Max(30,(int)(m.Bounds.Width*scale)-5),Math.Max(28,(int)(m.Bounds.Height*scale)-5));
                cells[m.Key]=r;DrawingUtil.Round(e.Graphics,r,7,Theme.Card,m.Key==Selected?Theme.Accent:Theme.Border,2);
                TextRenderer.DrawText(e.Graphics,m.Label,Font,r,Theme.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);foreach(var c in cells)if(c.Value.Contains(e.Location)){Selected=c.Key;Invalidate();if(Choose!=null)Choose(c.Key);break;}}
    }
    sealed class TouchMonitorDialog:Form
    {
        readonly List<TouchMonitorRule> rules;
        readonly List<MonitorData> monitors;
        readonly TouchMonitorMap map=new TouchMonitorMap {Dock=DockStyle.Top};
        readonly ComboBox monitorList=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=470};
        readonly TextBox nickname=new TextBox {Width=240};
        readonly CheckBox enabled=new CheckBox {Text="Automatic return on this screen",AutoSize=true};
        readonly CheckBox touch=new CheckBox {Text="Touch",AutoSize=true},pen=new CheckBox {Text="Pen",AutoSize=true};
        readonly NumericUpDown touchDelay=new NumericUpDown {Minimum=-1,Maximum=60000,Increment=250,Width=115},penDelay=new NumericUpDown {Minimum=-1,Maximum=60000,Increment=250,Width=115};
        readonly ComboBox devices=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=690};
        readonly CheckBox confirmation=new CheckBox {AutoSize=true,Text="I tested touch, dragging, multitouch and pen drawing in another app; input stayed normal."};
        readonly TextBox status=new TextBox {Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BackColor=Theme.Card,ForeColor=Theme.Text};
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=350};
        readonly ToolTip hints=new ToolTip();
        IDisposable lease;
        TouchMonitorRule selected;
        bool loading;
        PopupClickWatcher outside;
        internal string Result;
        internal TouchMonitorDialog(string json)
        {
            rules=TouchRules.Parse(json);monitors=DisplayNative.Monitors();
            foreach(var m in monitors)if(!rules.Any(r=>r.Key==m.Key))rules.Add(new TouchMonitorRule {Key=m.Key,Nickname=m.Label});
            Text="Touch screen monitor support — monitors & passive test";ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;
            ClientSize=new Size(940,760);MinimumSize=new Size(760,580);BackColor=Theme.Background;ForeColor=Theme.Text;Font=new Font("Segoe UI",10);
            var top=new FlowLayoutPanel {Dock=DockStyle.Top,Height=330,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(10),AutoScroll=true};
            top.Controls.Add(new Label {Text="Test first: keep another app active, then touch/draw on the selected display. No returns happen while this test is open.",AutoSize=true});
            foreach(var r in rules)monitorList.Items.Add(r);monitorList.SelectedIndexChanged+=delegate{Store();SelectRule(monitorList.SelectedItem as TouchMonitorRule);};top.Controls.Add(monitorList);
            var row=new FlowLayoutPanel {Width=870,Height=35,WrapContents=false};row.Controls.Add(new Label {Text="Nickname",AutoSize=true,Margin=new Padding(3,7,3,3)});row.Controls.Add(nickname);row.Controls.Add(enabled);row.Controls.Add(touch);row.Controls.Add(pen);top.Controls.Add(row);
            var timing=new FlowLayoutPanel {Width=870,Height=36,WrapContents=false};timing.Controls.Add(new Label {Text="Touch ms (-1 = global)",AutoSize=true});timing.Controls.Add(touchDelay);timing.Controls.Add(new Label {Text="Pen ms (-1 = global)",AutoSize=true});timing.Controls.Add(penDelay);top.Controls.Add(timing);
            top.Controls.Add(new Label {Text="Input devices — associate only after testing on the correct monitor",AutoSize=true});top.Controls.Add(devices);
            top.Controls.Add(confirmation);
            var actions=new FlowLayoutPanel {Width=880,Height=44};
            Add(actions,"Associate tested device",220,Associate);Add(actions,"Restart test",135,delegate{if(TouchReturnService.Current!=null)TouchReturnService.Current.ResetDetection();confirmation.Checked=false;});
            Add(actions,"Identify screens",150,Identify);top.Controls.Add(actions);
            top.Controls.Add(new Label {Text="Hold a contact still for at least 3 seconds, release it, and test two fingers together (touch). Test pen hover entry/exit separately.",AutoSize=true});
            var footer=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=52,FlowDirection=FlowDirection.RightToLeft};
            Add(footer,"Use draft",130,delegate{Store();Result=TouchRules.Save(rules);DialogResult=DialogResult.OK;Close();});Add(footer,"Cancel",100,delegate{DialogResult=DialogResult.Cancel;Close();});
            Controls.Add(status);Controls.Add(top);Controls.Add(map);Controls.Add(footer);
            map.Monitors=monitors;map.Choose=key=>{for(int i=0;i<rules.Count;i++)if(rules[i].Key==key)monitorList.SelectedIndex=i;};
            hints.SetToolTip(enabled,"Disconnected/ambiguous identities and unverified devices never trigger return. This is a draft until Apply in main Settings.");
            hints.SetToolTip(devices,"Only complete passive HID reports qualify. A virtual mouse is not a touchscreen. No supported device means automatic return remains unavailable.");
            hints.SetToolTip(touchDelay,"-1 uses the global touch delay; otherwise milliseconds. No timer starts until all contacts explicitly release.");
            hints.SetToolTip(penDelay,"-1 uses the global pen delay. Hover protection requires actual in-range reports; it is not inferred from mouse motion.");
            Shown+=delegate{FormFit.Fit(this);if(TouchReturnService.Current!=null)lease=TouchReturnService.Current.DetectionTest();outside=new PopupClickWatcher(this,delegate{return true;},delegate{DialogResult=DialogResult.Cancel;Close();});outside.Arm();timer.Start();};
            timer.Tick+=delegate{RefreshStatus();};
            FormClosed+=delegate{timer.Stop();if(outside!=null)outside.Dispose();if(lease!=null)lease.Dispose();};
            if(monitorList.Items.Count>0)monitorList.SelectedIndex=0;
        }
        void Add(FlowLayoutPanel p,string text,int width,Action action){var b=Theme.Button(text,width);b.Click+=delegate{action();};p.Controls.Add(b);}
        void Store(){if(loading||selected==null)return;selected.Nickname=nickname.Text;selected.Enabled=enabled.Checked;selected.Touch=touch.Checked;selected.Pen=pen.Checked;selected.TouchDelay=(int)touchDelay.Value;selected.PenDelay=(int)penDelay.Value;}
        void SelectRule(TouchMonitorRule rule)
        {
            loading=true;selected=rule;
            if(rule!=null){nickname.Text=rule.Nickname;enabled.Checked=rule.Enabled;touch.Checked=rule.Touch;pen.Checked=rule.Pen;touchDelay.Value=rule.TouchDelay;penDelay.Value=rule.PenDelay;map.Selected=rule.Key;map.Invalidate();}
            loading=false;RefreshStatus();
        }
        void RefreshStatus()
        {
            var service=TouchReturnService.Current;var old=devices.SelectedItem as TouchDeviceEvidence;
            var available=service==null?new TouchDeviceEvidence[0]:service.Devices.ToArray();
            if(devices.Items.Count!=available.Length || available.Any(d=>!devices.Items.Contains(d)))
            {devices.Items.Clear();foreach(var d in available)devices.Items.Add(d);if(old!=null)devices.SelectedItem=available.FirstOrDefault(d=>d.Key==old.Key);if(devices.SelectedIndex<0&&devices.Items.Count>0)devices.SelectedIndex=0;}
            string monitor=selected==null?"Select a monitor":monitors.Any(m=>m.Key==selected.Key)?"Selected display connected":"Selected display DISCONNECTED — mapping retained, automatic return inactive";
            status.Text=monitor+Environment.NewLine+(selected==null?"":"Touch association: "+(selected.TouchVerified?"tested "+selected.TouchDevice.Substring(0,Math.Min(8,selected.TouchDevice.Length)):"not verified")+"; pen association: "+(selected.PenVerified?"tested":"not verified"))+Environment.NewLine+
                (service==null?"Service not available":service.Report)+Environment.NewLine+"No HID touch/pen traffic? This input path may expose only mouse events. Do not enable automatic return; keep using touch normally.";
        }
        void Associate()
        {
            var d=devices.SelectedItem as TouchDeviceEvidence;var screen=selected==null?null:monitors.FirstOrDefault(m=>m.Key==selected.Key);
            if(d==null||screen==null||string.IsNullOrEmpty(screen.Instance)||!d.Ready||!confirmation.Checked)
            {MessageBox.Show(this,"Select a connected monitor with a stable identity and a supported device. Hold/release for 3 seconds, test two contacts for touch, and confirm unchanged input. No association was saved.","Detection not yet verified");return;}
            if(TouchReturnService.Current==null||!TouchReturnService.Current.HasProbeAnchor(d.Key,screen.Key))
            {MessageBox.Show(this,"A matching pre-touch foreground/cursor snapshot has not been observed on that display. Test with a different app active first. This input path may not support safe background return; no association was saved.","Pre-touch capture not verified");return;}
            if(d.Kind=="Pen"){selected.PenDevice=d.Key;selected.PenVerified=true;pen.Checked=true;}
            else{selected.TouchDevice=d.Key;selected.TouchVerified=true;touch.Checked=true;}
            RefreshStatus();
        }
        void Identify()
        {
            foreach(var m in monitors)
            {
                var f=new Form {FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,TopMost=true,StartPosition=FormStartPosition.Manual,BackColor=Theme.Card,Bounds=new Rectangle(m.Bounds.Left+40,m.Bounds.Top+40,180,90)};
                f.Controls.Add(new Label {Dock=DockStyle.Fill,Text=m.Label,ForeColor=Theme.Text,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",22,FontStyle.Bold)});
                var t=new System.Windows.Forms.Timer {Interval=1500};t.Tick+=delegate{t.Stop();f.Close();t.Dispose();};f.Show(this);t.Start();
            }
        }
        protected override void Dispose(bool disposing){if(disposing){timer.Dispose();hints.Dispose();}base.Dispose(disposing);}
    }
    sealed partial class Switcher
    {
        TouchReturnService touchService;
        internal void RequestShortcutRepair(){RepairShortcuts();}
        void SetupTouchSupport(){touchService=new TouchReturnService(this,options);}
        void ShowTouchSupport(){CancelPassiveLaunchObservation();if(touchService!=null)touchService.Cancel("Settings opened");if(transient!=null){transient.Activate();return;}Dismiss();SettingsCore("Touch screen monitor support");}
    }
}
