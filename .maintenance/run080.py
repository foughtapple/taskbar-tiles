from pathlib import Path
import runpy
runpy.run_path('.maintenance/run080base.py')
for p in sorted(Path('.maintenance').glob('review080*.py')):
 runpy.run_path(str(p))
# Remove one-time helpers from the tested source commit, not from its history.
p=Path('.github/workflows/prepare-touch080.yml')
s=p.read_text().replace('git rm -- .maintenance/apply080.py .maintenance/finalize080.py .maintenance/run080.py','git rm -r -- .maintenance')
p.write_text(s,encoding='utf-8',newline='\n')
