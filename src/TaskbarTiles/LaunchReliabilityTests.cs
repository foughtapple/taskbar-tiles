// Pure regression tests. No Shell calls, launches, hooks, window moves or saves.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace TaskbarTiles
{
    static class LaunchReliabilityTests
    {
        static int count;
        static void Check(bool value, string name)
        { count++; if (!value) throw new InvalidOperationException("Launch regression: " + name); }
        internal static void Run(StringBuilder log)
        {
            count = 0;
            const string terminal = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
            const string preview = "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe!App";
            const string terminalPath = @"C:\Program Files\WindowsApps\Terminal\WindowsTerminal.exe";
            var app = new AppButton { Id="Appid:"+terminal, DisplayName="Terminal" };
            var window = new WindowRecord { Handle=new IntPtr(10), ProcessId=20, ProcessStartTicks=123, Title="Windows PowerShell", Exe=terminalPath, AppId=terminal };
            var receipt = new LaunchReceipt { ExpectedAppId=terminal, ExpectedExe=terminalPath, RequireNewWindow=true };
            Check(LaunchIdentity.CleanId("Appid:" + terminal) == terminal, "taskbar Appid prefix");
            Check(LaunchIdentity.CleanId(@"shell:AppsFolder\" + terminal) == terminal, "shell prefix");
            Check(LaunchIdentity.Matches(app, window, receipt), "Terminal recognised despite PowerShell window title");
            window.Title="A completely different tab title";
            Check(LaunchIdentity.Matches(app, window, receipt), "titles do not control identity");
            window.AppId=preview;
            Check(!LaunchIdentity.Matches(app, window, receipt), "stable and Preview never conflated");
            window.AppId="";
            Check(LaunchIdentity.Matches(app, window, receipt), "exact resolved executable covers temporarily absent app identity");
            receipt.ExpectedExe="";
            Check(!LaunchIdentity.Matches(app, window, receipt), "no match from Terminal label alone");
            receipt.ProcessId=20; receipt.ProcessStartTicks=123;
            Check(LaunchIdentity.Matches(app, window, receipt), "verified returned process identity");
            window.ProcessStartTicks=124;
            Check(!LaunchIdentity.Matches(app, window, receipt), "PID reuse cannot match by process");
            window.ProcessStartTicks=123; window.AppId=preview;
            Check(!LaunchIdentity.Matches(app, window, receipt), "conflicting explicit app ID beats process hint");
            window.AppId=""; window.Exe=@"C:\Windows\explorer.exe";
            Check(!LaunchIdentity.Matches(app, window, receipt), "shell broker process not an app identity");
            foreach (string exe in new[] { "cmd.exe", "powershell.exe", "rundll32.exe", "applicationframehost.exe", "runtimebroker.exe", "dllhost.exe" })
                Check(LaunchIdentity.GenericHost(@"C:\Windows\"+exe), "generic host " + exe);
            Check(!LaunchIdentity.GenericHost(terminalPath), "Terminal is an app, not generic broker");
            var chrome = new AppButton { Id="Appid:Chrome.Profile-A", DisplayName="Chrome", LaunchExe=@"C:\Apps\chrome.exe" };
            Check(!LaunchIdentity.Matches(chrome,new WindowRecord { Exe=chrome.LaunchExe, AppId="Chrome.Profile-B" },null),"Chrome profile conflict retained");
            Check(LaunchIdentity.Matches(chrome,new WindowRecord { Exe=chrome.LaunchExe, AppId="Chrome.Profile-A" },null),"matching profile retained");
            Check(!LaunchIdentity.Matches(new AppButton { DisplayName="Installer" }, new WindowRecord { Exe=@"C:\OtherVendor\installer.exe", Title="Installer" }, null),"friendly executable names do not authorise a move");
            Check(LaunchIdentity.SamePath(@"C:\Apps\EDITOR.EXE", "c:/apps/editor.exe"),"case and slash normalisation");
            Check(!LaunchIdentity.SamePath("", ""),"empty paths are not evidence");
            Check(LaunchIdentity.FullPath(@"C:\Apps\x.exe") && LaunchIdentity.FullPath(@"\\server\share\x.exe"),"Windows absolute paths");
            Check(!LaunchIdentity.FullPath("wt.exe") && !LaunchIdentity.FullPath("C:relative.exe"),"aliases not treated as exact full paths");

            foreach (string target in new[] { "wt.exe", "WindowsTerminal.exe", @"C:\Tools\WindowsTerminal.exe" })
                Check(LaunchIdentity.PlainTerminal("", target, ""),"plain Terminal executable recognised");
            Check(LaunchIdentity.PlainTerminal(terminal,"",""),"exact terminal package ID recognised");
            Check(LaunchIdentity.PlainTerminal(preview,@"shell:AppsFolder\"+preview,""),"Preview family retained");
            Check(!LaunchIdentity.PlainTerminal(terminal,@"C:\Apps\editor.exe",""),"stale favourite AppID cannot override explicit target");
            Check(!LaunchIdentity.PlainTerminal("","Terminal",""),"display name not executable evidence");
            foreach (string args in new[] { "-w 0", "--window my-work", "-p PowerShell", "new-tab cmd", "--help" })
                Check(!LaunchIdentity.PlainTerminal(terminal,"wt.exe",args),"custom Terminal arguments untouched: "+args);
            Check(LaunchIdentity.TerminalFamily("Contoso.Terminal!App") == "","similarly named app not treated as Microsoft Terminal");
            var alias = new AppButton { LaunchExe="wt.exe", DisplayName="My console" };
            var aliasReceipt = new LaunchReceipt { ExpectedExe="wt.exe", RequireNewWindow=true };
            Check(LaunchIdentity.Matches(alias,new WindowRecord { Exe=terminalPath },aliasReceipt),"explicit wt alias matches WindowsTerminal window owner");
            aliasReceipt.RequireNewWindow=false;
            Check(!LaunchIdentity.Matches(alias,new WindowRecord { Exe=terminalPath },aliasReceipt),"alias equivalence requires explicit Terminal launch strategy");

            var before = new Dictionary<IntPtr,uint> { { new IntPtr(10),20 } };
            Check(!LaunchIdentity.IsNew(new WindowRecord { Handle=new IntPtr(10),ProcessId=20 },before),"existing handle/process is not new");
            Check(LaunchIdentity.IsNew(new WindowRecord { Handle=new IntPtr(10),ProcessId=21 },before),"recycled handle in a new process is new");
            Check(LaunchIdentity.IsNew(new WindowRecord { Handle=new IntPtr(11),ProcessId=20 },before),"new window in existing process is new");
            Check(!LaunchIdentity.CanReuse(true,false,2.5,15,false,true,900),"old window not moved prematurely at 2.5 seconds");
            Check(LaunchIdentity.CanReuse(true,false,15,15,false,true,900),"reuse after full wait with changed stable foreground");
            Check(!LaunchIdentity.CanReuse(true,true,20,15,false,true,900),"Terminal new-window request cannot reuse old window");
            Check(!LaunchIdentity.CanReuse(false,false,20,15,false,true,900),"reuse setting respected");
            Check(!LaunchIdentity.CanReuse(true,false,20,15,true,true,900),"new candidate takes precedence over reuse");
            Check(!LaunchIdentity.CanReuse(true,false,20,15,false,false,900),"already-foreground app is not causal evidence");
            Check(!LaunchIdentity.CanReuse(true,false,20,15,false,true,200),"foreground must settle");

            var cancelled = new LaunchOperation();
            Check(cancelled.CancelBeforeDispatch(),"cancel during resolution");
            Check(!cancelled.TryDispatch() && cancelled.Cancelled,"late resolver cannot dispatch after timeout");
            var sent = new LaunchOperation();
            Check(sent.TryDispatch(),"one dispatch allowed");
            Check(!sent.TryDispatch(),"no duplicate dispatch");
            Check(!sent.CancelBeforeDispatch() && sent.Dispatched,"sent request never reported as prevented");
            int winners=0; var race=new LaunchOperation(); var threads=new List<Thread>();
            for(int i=0;i<8;i++)
            { var t=new Thread(delegate() { if(race.TryDispatch()) Interlocked.Increment(ref winners); }); threads.Add(t); t.Start(); }
            foreach(var t in threads)t.Join();
            Check(winners==1,"concurrent dispatch has one winner");
            var defaults=new Options();
            Check(defaults.DirectAppLaunch && defaults.TerminalNewWindow,"launch reliability defaults on");
            var migrated=Options.Parse(new[]{"ConfigVersion=6","TileSize=144","PreviewScale=170","WindowColumns=6","WindowRows=2","WindowTitleFontSize=18","AppLabelFontSize=20","DirectAppLaunch=false","TerminalNewWindow=false"});
            Check(migrated.TileSize==144 && migrated.PreviewScale==170 && migrated.WindowColumns==6 && migrated.WindowRows==2,"existing appearance preserved");
            Check(migrated.WindowTitleFontSize==18 && migrated.AppLabelFontSize==20,"independent fonts preserved");
            Check(!migrated.DirectAppLaunch && !migrated.TerminalNewWindow,"compatibility opt-outs parsed");
            log.AppendLine("PASS: " + count + " launch identity, Terminal, duplicate-dispatch, cancellation and reuse policy checks. No applications were launched.");
        }
    }
}
