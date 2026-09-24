# Apply the same reviewed edits from the immutable preparation commit, with
# an explicitly anchored first-heading insertion for the existing changelog.
import subprocess
from pathlib import Path
source = subprocess.check_output(['git', 'show', 'd7e860c74dd3d89fc1608f90c8a9ccf22d18fcf7:.maintenance/apply091.py'], encoding='utf-8')
old = '    if text.count(old) != 1:'
assert source.count(old) == 1
source = source.replace(old, '    if text.count(old) != 1 and not (name == "CHANGELOG.md" and old == "# Changelog\\n" and text.startswith(old)):')
old = 'text.replace(old, new), encoding='
assert source.count(old) == 1
source = source.replace(old, 'text.replace(old, new, 1), encoding=')
exec(compile(source, '<reviewed-0.9.1-edits>', 'exec'))
# Publish the fixture ready marker atomically, never read a half-written handle.
p = Path('src/TaskbarTiles/AppReopenTests.cs')
t = p.read_text(encoding='utf-8')
old = 'launcher.Shown+=delegate {File.WriteAllText(Path.Combine(directory,"ready.txt"),button.Handle.toInt64()+"|"+hidden.ToInt64());};'.replace('toInt64', 'ToInt64')
new = 'launcher.Shown+=delegate {File.WriteAllText(Path.Combine(directory,"ready.tmp"),button.Handle.ToInt64()+"|"+hidden.ToInt64());File.Move(Path.Combine(directory,"ready.tmp"),Path.Combine(directory,"ready.txt"));};'
assert t.count(old) == 1
p.write_text(t.replace(old, new), encoding='utf-8', newline='\n')
