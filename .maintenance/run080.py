from pathlib import Path
import runpy, ast
p=Path('.maintenance/apply080.py')
s=p.read_text()
old='                    if (args.Contains("--toggle") || args.Contains("--show"))'
new='                    if (args.Length == 0 || args.Contains("--toggle") || args.Contains("--show"))'
lines=s.splitlines()
found=[i for i,line in enumerate(lines) if line.startswith('edit(main,') and 'args.Length == 0' in line]
assert len(found)==1
lines[found[0]]='edit(main,'+repr('\n'+old)+','+repr('\n'+new)+')'
for i,line in enumerate(lines):
 if line.startswith("edit('CHANGELOG.md',"):
  args=[ast.literal_eval(n) for n in ast.parse(line).body[0].value.args]
  assert args[1]=='# Changelog\n'
  args[1]+='\n## 0.7.6';args[2]+='\n## 0.7.6'
  lines[i]='edit('+','.join(repr(a) for a in args)+')'
# Execute without dirtying the tracked one-time helper, so git rm is safe later.
exec(compile('\n'.join(lines)+'\n',str(p),'exec'),{'__name__':'__main__'})
runpy.run_path('.maintenance/finalize080.py')
B=Path('src/TaskbarTiles')
def edit(path,old,new):
 p=Path(path);s=p.read_text(encoding='utf-8-sig');assert s.count(old)==1,(str(path),old[:100],s.count(old));p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
r=B/'TouchRawInput.cs'
edit(r,'internal int PhysicalVersion, TypingVersion;','internal int PhysicalVersion, TypingVersion;\n        internal uint LastDigitizerTick;')
edit(r,'IntPtr handle=Marshal.ReadIntPtr(buffer,8); var d=GetDevice(handle); if(d==null) return;','''IntPtr handle=Marshal.ReadIntPtr(buffer,8); var d=GetDevice(handle); if(d==null) return;
                    if(!d.Evidence.Supported){invalid("unclassified digitizer activity; automatic return blocked");return;}''')
edit(r,'if(f!=null) { d.Evidence.Observe(f,Environment.TickCount & int.MaxValue); frame(f); }','if(f!=null) { LastDigitizerTick=f.Tick; d.Evidence.Observe(f,Environment.TickCount & int.MaxValue); frame(f); }')
r=B/'TouchReturnRuntime.cs'
edit(r,'internal Point Cursor,Contact; internal bool Pen;','internal Point Cursor,Contact; internal bool Pen,Corroborated,Rejected;')
edit(r,'        Point lastMouse;','        Point lastMouse;\n        internal volatile uint LastPromotedTick;\n        internal volatile int PromotedButtons;')
edit(r,'                    if(promoted)\n                    {','''                    if(promoted)
                    {
                        LastPromotedTick=m.Time;
                        if(msg==0x201) PromotedButtons|=((extra & 0x80)==0?2:1);
                        if(msg==0x202) PromotedButtons&=~((extra & 0x80)==0?2:1);''')
edit(r,'            while(Recent.Count>32) Recent.RemoveAt(0);','            while(Recent.Count>32) { if(!Recent[0].Corroborated)Interlocked.Increment(ref physicalVersion); Recent.RemoveAt(0); }')
edit(r,'                    var pre=observer.Recent.LastOrDefault(s=>s.Pen==f.Pen && Math.Abs(unchecked((int)(f.Tick-s.Tick)))<=150);','''                    var pre=observer.Recent.LastOrDefault(s=>s.Pen==f.Pen && Math.Abs(unchecked((int)(f.Tick-s.Tick)))<=150);
                    if(pre!=null && f.Valid)pre.Corroborated=true;''')
edit(r,'                bool blocked=delayed.Count>0 ||','''                foreach(var unknown in observer.Recent.Where(a=>!a.Corroborated&&!a.Rejected&&unchecked((int)(now-a.Tick))>250))
                { unknown.Rejected=true;Cancel("touch/pen promotion without a complete background input path"); }
                bool unknownPromotion=observer.LastPromotedTick!=0 && unchecked((int)(now-observer.LastPromotedTick))<300 &&
                    Math.Abs(unchecked((int)(observer.LastPromotedTick-source.LastDigitizerTick)))>180;
                if(unknownPromotion && unchecked((int)(now-observer.LastPromotedTick))>180)Cancel("unclassified touch/pen input");
                bool blocked=observer.PromotedButtons!=0 || unknownPromotion || observer.Recent.Any(a=>!a.Corroborated&&!a.Rejected) || delayed.Count>0 ||''')
# Existing raw input can arrive after physical movement. The observer's explicit
# cancellation version and pending frames are both checked immediately before return.
edit(r,'                int input=observer.PhysicalVersion,key=observer.TypingVersion;','                int input=observer.PhysicalVersion,key=observer.TypingVersion;\n                if(input!=mouseVersion || (options.TouchTypingCancels&&key!=keyVersion) || observer.PromotedButtons!=0 || delayed.Count!=0){engine.Status="Cancelled: new input before return";return;}')
edit(r,'            fault=""; observer=new TouchInputObserver();\n            source=new RawTouchSource(OnFrame,Block);','''            fault=""; observer=new TouchInputObserver();
            try { source=new RawTouchSource(OnFrame,Block); }
            catch { observer.Dispose();observer=null;throw; }''')
# Once manual return is cancelled by real input, do not apply it to the next touch.
edit(r,'                    engine.Cancel(moved?','                    manualReturn=false;engine.Cancel(moved?')
print('Unknown touch/pen promotion and raw-input cleanup safety checks integrated.')
