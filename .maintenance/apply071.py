from pathlib import Path
r=Path.cwd()
def edit(rel, old, new, count=1):
 p=r/rel;s=p.read_text(encoding='utf-8-sig'); assert s.count(old)==count,(rel,old[:90],s.count(old)); p.write_text(s.replace(old,new),encoding='utf-8')
src='src/TaskbarTiles/'
edit(src+'TaskbarTiles.cs','internal const string Version = "0.7.0";','internal const string Version = "0.7.1";')
edit(src+'TaskbarTiles.cs','LaunchReliabilityTests.Run(log);','LaunchReliabilityTests.Run(log);\n                LaunchResolutionTests.Run(log);')
edit('version.txt','0.7.0','0.7.1')
edit(src+'AssemblyInfo.cs','0.7.0','0.7.1',3)
edit(src+'app.manifest','0.7.0.0','0.7.1.0')
edit(src+'LaunchReliability.cs','return requested.Length != 0 && actual.Length != 0 && !requested.Equals(actual, StringComparison.OrdinalIgnoreCase);','return LaunchResolution.ExplicitId(requested) && LaunchResolution.ExplicitId(actual) && !requested.Equals(actual, StringComparison.OrdinalIgnoreCase);')
edit(src+'LaunchReliability.cs','''            string actual = CleanId(w.AppId);
            // Never move another browser profile just because both use chrome.exe.''','''            string actual = CleanId(w.AppId);
            if (w.IdentityAmbiguous) return false;
            // Never move another browser profile just because both use chrome.exe.''')
edit(src+'LaunchReliability.cs','''            if (requested.Length > 0 && actual.Length > 0) return true;
            if (receipt != null && receipt.ProcessId != 0 && receipt.ProcessId == w.ProcessId &&
                receipt.ProcessStartTicks != 0 && receipt.ProcessStartTicks == w.ProcessStartTicks && !GenericHost(w.Exe)) return true;''','''            if (requested.Length > 0 && requested.Equals(actual, StringComparison.OrdinalIgnoreCase)) return true;
            // Browser/profile-scoped IDs may temporarily be missing while starting.
            // Do not collapse two profiles into a shared executable or PID.
            if (LaunchResolution.ProfileScoped(requested)) return false;
            uint appPid = w.AppProcessId != 0 ? w.AppProcessId : w.ProcessId;
            long appStart = w.AppProcessId != 0 ? w.AppProcessStartTicks : w.ProcessStartTicks;
            if (receipt != null && receipt.ProcessId != 0 && receipt.ProcessId == appPid &&
                receipt.ProcessStartTicks != 0 && receipt.ProcessStartTicks == appStart && !GenericHost(w.Exe)) return true;''')
edit(src+'LaunchReliability.cs','''            else if (target.StartsWith(@"shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(receipt.ExpectedAppId)) receipt.ExpectedAppId = LaunchIdentity.CleanId(target);
        }''','''            else if (target.StartsWith(@"shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(receipt.ExpectedAppId)) receipt.ExpectedAppId = LaunchIdentity.CleanId(target);
            LaunchResolution.ResolveShellTarget(target, receipt);
        }''')
edit(src+'LaunchReliability.cs','''            LaunchLog.Write(operation.Id, "dispatch: " + receipt.Method);''','''            LaunchLog.Write(operation.Id, "dispatch: " + receipt.Method + "; version=" + Program.Version + "; expected=" + LaunchResolution.Describe(receipt.ExpectedAppId, receipt.ExpectedExe));''')
edit(src+'LaunchReliability.cs','''                        { receipt.ProcessId = (uint)process.Id; receipt.ProcessStartTicks = process.StartTime.ToUniversalTime().Ticks; }''','''                        {
                            receipt.ProcessId = (uint)process.Id; receipt.ProcessStartTicks = process.StartTime.ToUniversalTime().Ticks;
                            if (string.IsNullOrWhiteSpace(receipt.ExpectedExe)) receipt.ExpectedExe = exe;
                        }''')
edit(src+'WindowPlacement.cs','''        public long ProcessStartTicks;
        internal DateTime NextIdentityRefresh;''','''        public long ProcessStartTicks;
        internal uint AppProcessId;
        internal long AppProcessStartTicks;
        internal bool IdentityAmbiguous;
        internal DateTime NextIdentityRefresh;''')
edit(src+'WindowPlacement.cs','''                    entry.ProcessStartTicks = PackageIdentity.StartTicks(pid);
                    entry.NextIdentityRefresh = DateTime.UtcNow.AddSeconds(1);''','''                    entry.ProcessStartTicks = PackageIdentity.StartTicks(pid);
                    entry.AppProcessId = pid; entry.AppProcessStartTicks = entry.ProcessStartTicks;
                    entry.IdentityAmbiguous = false;
                    HostedWindowIdentity.Refresh(h, entry);
                    entry.NextIdentityRefresh = DateTime.UtcNow.AddMilliseconds(500);''')
edit(src+'WindowPlacement.cs','''        int lastNew = -1, lastMatches = -1;''','''        int lastNew = -1, lastMatches = -1, lastExisting = -1;
        readonly HashSet<string> evidenceLogged = new HashSet<string>();''')
edit(src+'WindowPlacement.cs','''            LaunchLog.Write(requestId, "tracking; initial windows=" + before.Count + "; require new=" + receipt.RequireNewWindow);''','''            LaunchLog.Write(requestId, "tracking; version=" + Program.Version + "; initial windows=" + before.Count + "; require new=" + receipt.RequireNewWindow + "; expected=" + LaunchResolution.Describe(receipt.ExpectedAppId, receipt.ExpectedExe));''')
edit(src+'WindowPlacement.cs','''                if (newCount != lastNew || matches.Count != lastMatches)
                {
                    lastNew = newCount; lastMatches = matches.Count;
                    LaunchLog.Write(requestId, "new windows=" + newCount + "; matching new=" + matches.Count + "; matching existing=" + now.Count(w => !New(w) && Match(w)));
                }''','''                int existingMatches = now.Count(w => !New(w) && Match(w));
                if (newCount != lastNew || matches.Count != lastMatches || existingMatches != lastExisting)
                {
                    lastNew = newCount; lastMatches = matches.Count; lastExisting = existingMatches;
                    LaunchLog.Write(requestId, "new windows=" + newCount + "; matching new=" + matches.Count + "; matching existing=" + existingMatches);
                }
                foreach (var observed in now.Where(w => New(w) || Match(w)))
                {
                    string evidence = "hwnd=" + observed.Handle.ToInt64().ToString("X") + "; owner pid=" + observed.ProcessId + "; app pid=" + observed.AppProcessId + "; " + LaunchResolution.Describe(observed.AppId, observed.Exe) + "; " + LaunchResolution.Reason(app, observed, receipt);
                    if (evidenceLogged.Count < 96 && evidenceLogged.Add(evidence)) LaunchLog.Write(requestId, evidence);
                }''')
edit(src+'WindowPlacement.cs','''            using (var picker = new WindowChoiceWindow(app.DisplayName, reason, windows, delegate { return Shortlist(WindowInventory.Read(cache)); }))''','''            bool all = windows.Count == 0;
            if (all)
            {
                windows = WindowInventory.Read(cache);
                reason += "\\nNo identity match: showing all open windows for manual selection, not an automatic move.";
                LaunchLog.Write(requestId, "manual fallback lists all eligible windows=" + windows.Count);
            }
            using (var picker = new WindowChoiceWindow(app.DisplayName, reason, windows, delegate { return Shortlist(WindowInventory.Read(cache)); }, all))''')
edit(src+'WindowPlacement.cs','''        public WindowChoiceWindow(string app, string reason, List<WindowRecord> windows, Func<List<WindowRecord>> reload)
        {
            refresh = reload;''','''        public WindowChoiceWindow(string app, string reason, List<WindowRecord> windows, Func<List<WindowRecord>> reload, bool initiallyShowAll = false)
        {
            refresh = reload; showAll = initiallyShowAll;''')
edit(src+'WindowPlacement.cs','''var all = Theme.Button("Show all windows", 150);''','''var all = Theme.Button(showAll ? "Show candidates" : "Show all windows", 150);''')
edit(src+'Settings.cs','''            AddQuickAccessPage();''','''            AddUpdatesPage();
            AddQuickAccessPage();''')
edit(src+'Updates.cs','''        internal UpdatesWindow()
''','''        internal UpdatesWindow(bool checkOnOpen = false)
''')
edit(src+'Updates.cs','''            Controls.Add(body); CancelButton = close; FormClosing += delegate { stop.Cancel(); };
''','''            Controls.Add(body); CancelButton = close; FormClosing += delegate { stop.Cancel(); };
            if (checkOnOpen) Shown += delegate { Check(); };
''')
edit(src+'TaskbarTiles.csproj', '    <Compile Include="LaunchReliability.cs" />', '    <Compile Include="LaunchReliability.cs" />\n    <Compile Include="LaunchResolution.cs" />\n    <Compile Include="LaunchResolutionTests.cs" />')
