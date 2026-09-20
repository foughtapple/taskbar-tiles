from pathlib import Path
import runpy
p=Path('.maintenance/apply080.py')
s=p.read_text()
old='                    if (args.Contains("--toggle") || args.Contains("--show"))'
new='                    if (args.Length == 0 || args.Contains("--toggle") || args.Contains("--show"))'
lines=s.splitlines()
found=[i for i,line in enumerate(lines) if line.startswith('edit(main,') and 'args.Length == 0' in line]
assert len(found)==1
# Anchor at a line start so the deeper first-start block is not changed.
lines[found[0]]='edit(main,'+repr('\n'+old)+','+repr('\n'+new)+')'
p.write_text('\n'.join(lines)+'\n',encoding='utf-8',newline='\n')
runpy.run_path(str(p))
runpy.run_path('.maintenance/finalize080.py')
