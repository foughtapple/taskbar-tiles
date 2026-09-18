#!/usr/bin/env python3
"""Offline package checks. These do NOT compile C# or run Windows/PowerShell.
Requires PyYAML and Pygments for the developer-only static checks.
"""
from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
import yaml
from pygments import lex
from pygments.lexers import CSharpLexer, PowerShellLexer
from pygments.token import Comment, String, Error

ROOT = Path(__file__).resolve().parent.parent
checks = []
def require(value, description):
    if not value:
        raise AssertionError(description)
    checks.append(description)

sources = sorted((ROOT/'src/TaskbarTiles').glob('*.cs'))
for path in sources:
    stack = []
    for kind, text in lex(path.read_text(encoding='utf-8-sig'), CSharpLexer()):
        require(kind not in Error, f'C# lexical token accepted: {path.name}') if kind in Error else None
        if kind in Comment or kind in String:
            continue
        for char in text:
            if char in '({[':
                stack.append(char)
            elif char in ')}]':
                if not stack or stack.pop() != {')':'(', '}':'{', ']':'['}[char]:
                    raise AssertionError(f'Unbalanced C# delimiters in {path.name}')
    require(not stack, f'C# lexical delimiters balanced: {path.name}')
for path in sorted((ROOT/'tools').glob('*.ps1')):
    stack = []
    for kind, text in lex(path.read_text(encoding='utf-8-sig'), PowerShellLexer()):
        if kind in Error:
            raise AssertionError('PowerShell lexical error: '+path.name)
        if kind in Comment or kind in String:
            continue
        for char in text:
            if char in '({[':
                stack.append(char)
            elif char in ')}]':
                if not stack or stack.pop() != {')':'(', '}':'{', ']':'['}[char]:
                    raise AssertionError('Unbalanced PowerShell delimiters: '+path.name)
    require(not stack, 'PowerShell lexical delimiters balanced (not executed): '+path.name)
for path in ROOT.rglob('*.json'):
    if '.git' not in path.parts and path.name != 'PREPARATION-CHECKS.json':
        json.loads(path.read_text(encoding='utf-8-sig'))
        checks.append('JSON parsed: '+str(path.relative_to(ROOT)))
for path in (ROOT/'.github').rglob('*.yml'):
    data = yaml.load(path.read_text(), Loader=yaml.BaseLoader)
    require(isinstance(data, dict), 'YAML mapping parsed: '+str(path.relative_to(ROOT)))
    if path.parent.name == 'workflows':
        require('on' in data and 'jobs' in data and 'permissions' in data, 'Workflow trigger/jobs/permissions: '+path.name)
        for job in data['jobs'].values():
            for step in job['steps']:
                if 'uses' in step:
                    require(bool(re.fullmatch(r'[\w.-]+/[\w.-]+@[0-9a-f]{40}', step['uses'])), 'Action pinned: '+step['uses'])
ns={'m':'http://schemas.microsoft.com/developer/msbuild/2003'}
project = ET.parse(ROOT/'src/TaskbarTiles/TaskbarTiles.csproj')
includes = {node.attrib['Include'] for node in project.findall('.//m:Compile',ns)}
require(includes == {p.name for p in sources}, 'Visual Studio project includes every production and test source')
for name in ['app.config','app.manifest']:
    ET.parse(ROOT/'src/TaskbarTiles'/name)
    checks.append('XML parsed: '+name)
ET.parse(ROOT/'assets/mark.svg')
version=(ROOT/'version.txt').read_text().strip()
core=(ROOT/'src/TaskbarTiles/TaskbarTiles.cs').read_text()
require(f'internal const string Version = "{version}";' in core, 'Runtime version matches version.txt')
require(f'AssemblyFileVersion("{version}.0")' in (ROOT/'src/TaskbarTiles/AssemblyInfo.cs').read_text(), 'File version matches')
require(f'version="{version}.0"' in (ROOT/'src/TaskbarTiles/app.manifest').read_text(), 'Manifest version matches')
require((ROOT/f'docs/releases/v{version}.md').exists(), 'Release notes match version')
activation=(ROOT/'src/TaskbarTiles/WindowActivation.cs').read_text()
require(activation.index('activation.Start();') < activation.index('Dismiss();\n                if (activation.State'), 'Initial activation precedes picker hide')
require('attempts < 4' in activation and 'elapsed >= 2200' in activation, 'Foreground verification bounded')
require('State = ActivationState.Cancelled' in activation, 'New input can cancel activation')
require('ProcessId(selected) != expectedPid' in activation, 'Selected process identity validated')
require('ActivateWindow(windows[selected])' in core, 'Preview selection uses shared activation')
require('ActivateWindow(item.Window)' in (ROOT/'src/TaskbarTiles/IntegratedSearch.cs').read_text(), 'Open-window search uses shared activation')
require('ActivationTests.Run(log);' in core and 'UpdateTests.Run(log);' in core, 'New test suites are wired into --self-test')
setup=(ROOT/'installer/TaskbarTiles.iss').read_text()
require('PrivilegesRequired=lowest' in setup and 'DefaultDirName={localappdata}\\TaskbarTiles' in setup, 'Per-user path keeps legacy shortcut compatibility')
require('onlyifdoesntexist uninsneveruninstall' in setup and '[UninstallDelete]' not in setup, 'Installer preserves configuration and avoids recursive data deletion')
for match in re.finditer(r'^Source: "([^"]+)";',setup,re.M):
    value=match.group(1).replace('\\','/')
    if value.startswith('../build/'):
        continue
    require((ROOT/'installer'/value).resolve().exists(), 'Installer source exists: '+value)
release=(ROOT/'.github/workflows/release.yml').read_text()
require(release.index('./tools/Test-Installer.ps1') < release.index('gh release create'), 'Installer tests precede draft creation/publication')
require('--draft=false' in release and '--verify-tag --draft' in release, 'Release assets uploaded before publication')
updates=(ROOT/'src/TaskbarTiles/Updates.cs').read_text()
require('foughtapple/taskbar-tiles' in updates and 'SHA256SUMS.txt' in updates, 'Updater points to intended repository and checksums')
require('ReleaseInfo.Hash(file) != expected' in updates, 'Installer hash rechecked before execution')
require('UseShellExecute = true' in updates and 'Process.Start' in updates, 'Installer opened through normal Windows shell')
report={
    'created_utc':datetime.now(timezone.utc).isoformat(),
    'environment':'Linux; no Windows/.NET Framework compiler or runtime execution',
    'static_checks_passed':len(checks), 'csharp_files_checked':len(sources),
    'checks':checks,
    'not_executed':['C# compilation','C# --self-test','PowerShell execution','Inno Setup compilation','Windows installer smoke tests','live window activation','GitHub publication'],
    'note':'Lexical/schema/wiring checks are not compiler or desktop integration tests.'
}
(ROOT/'PREPARATION-CHECKS.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({k:v for k,v in report.items() if k!='checks'},indent=2))
