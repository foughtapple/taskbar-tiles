# Actual 0.10.0 -> unified-plugin installer migration; disposable CI only.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InstallRoot,[Parameter(Mandatory=$true)][string]$Setup)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Restricted to disposable CI runner.'}
$root=Split-Path $PSScriptRoot -Parent
$active=Join-Path $env:APPDATA 'HotSpot\StreamDock\plugins'
if(Test-Path $active){throw 'Refusing to change pre-existing Stream Dock plugins on runner.'}
$data=Join-Path $InstallRoot 'StreamDockData'
$utf8=New-Object Text.UTF8Encoding($false)
$chosen=@('com.foughtapple.controls.clipboard','com.foughtapple.deskstatus.pc','com.foughtapple.steamsmarttoggle.toggle')
$folders=@('com.foughtapple.controls.sdPlugin','com.foughtapple.steamsmarttoggle.sdPlugin','com.foughtapple.deskstatus.sdPlugin','com.foughtapple.nicknacksorders.sdPlugin')
$ids=@('controls','steam','desk','orders')
$unified=Join-Path $active 'com.foughtapple.taskbartiles.sdPlugin'
$external=@()
function Save-State { [IO.File]::WriteAllText((Join-Path $data 'state.json'),($script:state|ConvertTo-Json -Depth 10),$utf8) }
function Sync-Dock {
 $p=Start-Process (Join-Path $InstallRoot 'TaskbarTiles.exe') -ArgumentList '--sync-streamdock' -WorkingDirectory $InstallRoot -PassThru
 try{if(-not$p.WaitForExit(60000)){$p.Kill();throw 'Sync timeout'};if($p.ExitCode-ne0){throw ('Sync failed: '+(Get-Content (Join-Path $data 'last-result.txt') -Raw))}}finally{$p.Dispose()}
}
function Install-Version([string]$file) {
 $p=Start-Process $file -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=""') -PassThru
 try{if(-not$p.WaitForExit(120000)){$p.Kill();throw 'Upgrade timeout'};if($p.ExitCode-ne0){throw 'Upgrade failed'}}finally{$p.Dispose()}
}
function Assert-Actions([string[]]$expected) {
 $m=Get-Content (Join-Path $unified 'manifest.json') -Raw|ConvertFrom-Json
 if($m.Name-ne'Taskbar Tiles'-or$m.Category-ne'Taskbar Tiles'){throw 'Unified category name is wrong'}
 if(@(Compare-Object @($m.Actions.UUID) $expected).Count){throw 'Unified actions differ from saved choices'}
 foreach($f in $folders){if(Test-Path (Join-Path $active $f)){throw 'Legacy category remains discoverable'}}
}
try{
 # Pin the published baseline, not a mutable latest release.
 $oldSetup=Join-Path $root 'build\baseline-0.10.0-Setup.exe'
 [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
 Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/foughtapple/taskbar-tiles/releases/download/v0.10.0/TaskbarTiles-0.10.0-Setup.exe' -OutFile $oldSetup
 if((Get-FileHash $oldSetup -Algorithm SHA256).Hash.ToLowerInvariant()-ne'c5b5e6f3b031231f6ea349fbfa64a5d25e208629a94f537230e9b42b67a90e72'){throw 'Published 0.10.0 baseline checksum mismatch'}
 Install-Version $oldSetup
 if([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $InstallRoot 'TaskbarTiles.exe')).FileVersion-ne'0.10.0.0'){throw 'Baseline did not install'}
 New-Item -ItemType Directory -Path $data -Force|Out-Null
 $state=[ordered]@{Schema=1;AutoUpdate=$true;EnabledActions=@($chosen+'com.foughtapple.nicknacksorders.processing');ManagedPackages=@()}
 Save-State;Sync-Dock
 for($i=0;$i-lt$folders.Count;$i++){
  $dir=Join-Path $active $folders[$i]
  if(-not(Test-Path $dir)){throw ('Baseline worker missing: '+$ids[$i])}
  [IO.File]::WriteAllText((Join-Path $dir 'private-settings-sentinel.json'),('{"sentinel":"'+$ids[$i]+'"}'),$utf8)
 }
 # Preserve an old disabled worker's data too, without re-enabling its action.
 $state=Get-Content (Join-Path $data 'state.json') -Raw|ConvertFrom-Json
 $state.EnabledActions=$chosen;Save-State;Sync-Dock
 foreach($name in @('DeskStatus','SteamSmartSwitch','NickNacksOrders','StreamDockControls\Games')){
  $dir=Join-Path $env:LOCALAPPDATA ('FoughtApple\'+$name);New-Item -ItemType Directory -Path $dir -Force|Out-Null
  $file=Join-Path $dir 'unified-upgrade-sentinel.txt';if(Test-Path $file){throw 'Sentinel already exists'}
  [IO.File]::WriteAllText($file,'external-private-state-unchanged',$utf8);$external+=@($file)
 }
 $foreign=Join-Path $active 'com.unrelated.test.sdPlugin';New-Item -ItemType Directory -Path $foreign|Out-Null;Set-Content (Join-Path $foreign 'keep.txt') 'unchanged'
 $scene=Join-Path $root 'build\scene-uuid-fixture.json';[IO.File]::WriteAllText($scene,($chosen|ConvertTo-Json),$utf8);$sceneHash=(Get-FileHash $scene).Hash
 # This is the actual ordinary app installer, not a stand-alone plugin installer.
 Install-Version $Setup;Assert-Actions $chosen
 for($i=0;$i-lt$ids.Count;$i++){
  $p=Join-Path $unified ('workers\'+$ids[$i]+'\private-settings-sentinel.json')
  if((Get-Content $p -Raw)-ne('{"sentinel":"'+$ids[$i]+'"}')){throw ('Worker settings lost: '+$ids[$i])}
 }
 foreach($file in $external){if((Get-Content $file -Raw)-ne'external-private-state-unchanged'){throw 'External private state was modified'}}
 if((Get-FileHash $scene).Hash-ne$sceneHash){throw 'UUID fixture was modified'}
 $state=Get-Content (Join-Path $data 'state.json') -Raw|ConvertFrom-Json
 if(@(Compare-Object @($state.EnabledActions) $chosen).Count-or @($state.ManagedPackages)[0]-ne'taskbartiles'){throw 'Migration changed choices or ownership'}
 if(@(Get-ChildItem (Join-Path $data 'Backups') -Directory -Recurse -Filter 'legacy-*.sdPlugin').Count-lt3){throw 'Old active folders were not archived'}
 # Opt-out is observable even when a receipt is missing and repair would be needed.
 $state.AutoUpdate=$false;Save-State
 $receipt=Join-Path $unified '.taskbar-tiles-package.json';Remove-Item $receipt -Force
 Install-Version $Setup
 if(Test-Path $receipt){throw 'Opt-out still applied plugin update'}
 Assert-Actions $chosen
 $state.AutoUpdate=$true;Save-State;Sync-Dock
 if(-not(Test-Path $receipt)){throw 'Opt-in failed to reconcile'}
 $state.EnabledActions=@('com.foughtapple.controls.clipboard');Save-State;Sync-Dock;Assert-Actions @('com.foughtapple.controls.clipboard')
 $state.EnabledActions=@();Save-State;Sync-Dock
 if(Test-Path $unified){throw 'All-off leaves unified plugin discoverable'}
 if(-not(Test-Path (Join-Path $data 'Disabled\com.foughtapple.taskbartiles.sdPlugin'))){throw 'Unified disabled copy missing'}
 $state.EnabledActions=$chosen;Save-State;Sync-Dock;Assert-Actions $chosen
 for($i=0;$i-lt$ids.Count;$i++){if(-not(Test-Path (Join-Path $unified ('workers\'+$ids[$i]+'\private-settings-sentinel.json')))){throw 'Re-enable lost worker settings'}}
 if(-not(Test-Path (Join-Path $foreign 'keep.txt'))){throw 'Unrelated plugin was touched'}
 $state.EnabledActions=@();Save-State;Sync-Dock
 'PASS: published 0.10.0 -> current Setup migration; active and disabled worker settings retained; one category; exact action IDs and On/Off choices; external data unchanged; opt-out; disable/re-enable; archived legacy folders; unrelated plugin retained. Real physical Stream Dock layout resolution remains user-side validation.'|Set-Content (Join-Path $root 'build\streamdock-installer-tests.txt')
}finally{
 foreach($file in $external){if(Test-Path $file){Remove-Item $file -Force}}
 if(Test-Path $active){Remove-Item $active -Recurse -Force}
}
