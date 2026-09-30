# Core-only Setup preserves optional module state and never installs/activates it.
# Writes only to disposable GitHub Actions accounts.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InstallRoot,[Parameter(Mandatory=$true)][string]$Setup)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Restricted to disposable CI runner.'}
$root=Split-Path $PSScriptRoot -Parent
$active=Join-Path $env:APPDATA 'HotSpot\StreamDock\plugins'
$activeBoundary=[IO.Path]::GetFullPath((Join-Path $env:APPDATA 'HotSpot\StreamDock'))+[IO.Path]::DirectorySeparatorChar
$active=[IO.Path]::GetFullPath($active)
if(-not $active.StartsWith($activeBoundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture plugin path is outside the disposable Stream Dock profile.'}
if(Test-Path -LiteralPath $active){throw 'Refusing to change pre-existing Stream Dock plugins on runner.'}
$data=Join-Path $InstallRoot 'StreamDockData'
$utf8=New-Object Text.UTF8Encoding($false)
function Install-Version {
 $p=Start-Process -FilePath $Setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=""') -PassThru -WindowStyle Hidden
 try{if(-not $p.WaitForExit(120000)){$p.Kill();throw 'Upgrade timeout'};if($p.ExitCode-ne0){throw 'Upgrade failed'}}finally{$p.Dispose()}
}
try{
 if(Test-Path -LiteralPath (Join-Path $InstallRoot 'streamdock\packages')){throw 'Core installer unexpectedly includes optional worker packages.'}
 if(Test-Path -LiteralPath (Join-Path $data 'state.json')){throw 'Core installer unexpectedly seeds optional module choices.'}
 if(-not(Test-Path -LiteralPath (Join-Path $InstallRoot 'streamdock\catalog.json'))){throw 'Core installer omitted module metadata.'}
 if(-not(Test-Path -LiteralPath (Join-Path $InstallRoot 'streamdock\module-index.json'))){throw 'Core installer omitted module release metadata.'}
 # Existing live plugin, scene and downloaded/private module data remain intact.
 $unified=Join-Path $active 'com.foughtapple.taskbartiles.sdPlugin'
 New-Item -ItemType Directory -Path $unified -Force | Out-Null
 [IO.File]::WriteAllText((Join-Path $unified 'manifest.json'),'{"Version":"1.0.0","Actions":[{"UUID":"com.foughtapple.controls.clipboard"}]}',$utf8)
 [IO.File]::WriteAllText((Join-Path $unified 'private-settings.json'),'{"sentinel":"retain-private-settings"}',$utf8)
 $foreign=Join-Path $active 'com.unrelated.test.sdPlugin';New-Item -ItemType Directory -Path $foreign -Force | Out-Null
 [IO.File]::WriteAllText((Join-Path $foreign 'keep.txt'),'retain-unrelated-plugin',$utf8)
 New-Item -ItemType Directory -Path $data -Force | Out-Null
 $state=[ordered]@{Schema=1;AutoUpdate=$true;EnabledActions=@('com.foughtapple.controls.clipboard');ManagedPackages=@('taskbartiles')}
 [IO.File]::WriteAllText((Join-Path $data 'state.json'),($state|ConvertTo-Json -Depth 10),$utf8)
 $cached=Join-Path $data 'Modules\1.0.0';New-Item -ItemType Directory -Path $cached -Force | Out-Null
 [IO.File]::WriteAllText((Join-Path $cached 'private-sentinel.txt'),'retain-downloaded-module',$utf8)
 $scene=Join-Path $data 'scene-uuid-fixture.json';[IO.File]::WriteAllText($scene,'{"action":"com.foughtapple.controls.clipboard"}',$utf8)
 $files=@((Join-Path $data 'state.json'),(Join-Path $unified 'manifest.json'),(Join-Path $unified 'private-settings.json'),(Join-Path $foreign 'keep.txt'),(Join-Path $cached 'private-sentinel.txt'),$scene)
 $before=@{};foreach($file in $files){$before[$file]=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash}
 Install-Version
 foreach($file in $files){if(-not(Test-Path -LiteralPath $file)-or(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash-ne$before[$file]){throw ('Core upgrade changed optional/private state: '+$file)}}
 if(Test-Path -LiteralPath (Join-Path $InstallRoot 'streamdock\packages')){throw 'Core upgrade installed optional module code.'}
 if(Test-Path -LiteralPath (Join-Path $data 'active-module.json')){throw 'Core upgrade activated/downloaded a module.'}
 'PASS: core Setup includes metadata only; no seeding, module download, activation or live plugin replacement; action IDs, scenes, choices, cached module data and private settings preserved.' | Set-Content -LiteralPath (Join-Path $root 'build\streamdock-installer-tests.txt')
}finally{
 if(-not([IO.Path]::GetFullPath($active).StartsWith($activeBoundary,[StringComparison]::OrdinalIgnoreCase))){throw 'Refusing fixture cleanup outside Stream Dock profile.'}
 if(Test-Path -LiteralPath $active){Remove-Item -LiteralPath $active -Recurse -Force}
}
