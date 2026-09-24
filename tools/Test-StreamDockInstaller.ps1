# Runs only inside the existing disposable-runner installer lifecycle test.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InstallRoot,[Parameter(Mandatory=$true)][string]$Setup)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Restricted to disposable CI runner.'}
$root=Split-Path $PSScriptRoot -Parent
$active=Join-Path $env:APPDATA 'HotSpot\StreamDock\plugins'
if(Test-Path $active){throw 'Refusing to change pre-existing Stream Dock plugins on runner.'}
$data=Join-Path $InstallRoot 'StreamDockData';New-Item -ItemType Directory -Path $data -Force|Out-Null
$utf8=New-Object Text.UTF8Encoding($false)
$chosen=@('com.foughtapple.controls.clipboard','com.foughtapple.deskstatus.pc')
$folders=@('com.foughtapple.controls.sdPlugin','com.foughtapple.deskstatus.sdPlugin')
$state=[ordered]@{Schema=1;AutoUpdate=$true;EnabledActions=$chosen;ManagedPackages=@()}
function Save-State { [IO.File]::WriteAllText((Join-Path $data 'state.json'),($state|ConvertTo-Json -Depth 8),$utf8) }
function Sync-Dock {
 $p=Start-Process (Join-Path $InstallRoot 'TaskbarTiles.exe') -ArgumentList '--sync-streamdock' -WorkingDirectory $InstallRoot -PassThru
 try{if(-not$p.WaitForExit(60000)){$p.Kill();throw 'Sync timeout'};if($p.ExitCode-ne0){throw 'Sync failed'}}finally{$p.Dispose()}
}
function Upgrade-App {
 $p=Start-Process $Setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=""') -PassThru
 try{if(-not$p.WaitForExit(120000)){$p.Kill();throw 'Upgrade timeout'};if($p.ExitCode-ne0){throw 'Upgrade failed'}}finally{$p.Dispose()}
}
try{
 Save-State;Sync-Dock
 foreach($folder in $folders){$dir=Join-Path $active $folder;if(-not(Test-Path $dir)){throw 'Enabled module not installed'};[IO.File]::WriteAllText((Join-Path $dir 'private-settings-sentinel.json'),'{"sentinel":"keep"}',$utf8);Remove-Item (Join-Path $dir '.taskbar-tiles-package.json') -Force}
 $foreign=Join-Path $active 'com.unrelated.test.sdPlugin';New-Item -ItemType Directory -Path $foreign|Out-Null;Set-Content (Join-Path $foreign 'keep.txt') 'unchanged'
 Upgrade-App
 foreach($folder in $folders){$dir=Join-Path $active $folder;if((Get-Content (Join-Path $dir 'private-settings-sentinel.json') -Raw)-ne'{"sentinel":"keep"}'){throw 'Upgrade lost private settings'};$m=Get-Content (Join-Path $dir 'manifest.json') -Raw|ConvertFrom-Json;if(@($m.Actions).Count-ne1){throw 'Disabled action re-enabled by upgrade'}}
 $state=Get-Content (Join-Path $data 'state.json') -Raw|ConvertFrom-Json
 if(@(Compare-Object @($state.EnabledActions) $chosen).Count){throw 'Upgrade changed choices'}
 $state.AutoUpdate=$false;Save-State
 $manifest=Join-Path $active ($folders[0]+'\manifest.json');$before=(Get-FileHash $manifest).Hash
 Upgrade-App
 if((Get-FileHash $manifest).Hash-ne$before){throw 'Opt-out still updated module'}
 $state.AutoUpdate=$true;$state.EnabledActions=@();Save-State;Sync-Dock
 foreach($folder in $folders){if(Test-Path (Join-Path $active $folder)){throw 'Off module remains discoverable'};if(-not(Test-Path (Join-Path $data ('Disabled\'+$folder)))){throw 'Disabled backup missing'}}
 if(-not(Test-Path (Join-Path $foreign 'keep.txt'))){throw 'Unrelated plugin was touched'}
 $state.EnabledActions=@('com.foughtapple.controls.clipboard');Save-State;Sync-Dock
 if((Get-Content (Join-Path $active ($folders[0]+'\private-settings-sentinel.json')) -Raw)-ne'{"sentinel":"keep"}'){throw 'Re-enable lost settings'}
 $state.EnabledActions=@();Save-State;Sync-Dock
 'PASS: actual Setup upgrade synchronises enabled modules, preserves private settings and opt-outs, disables/re-enables independently and leaves unrelated plugins untouched.'|Set-Content (Join-Path $root 'build\streamdock-installer-tests.txt')
}finally{if(Test-Path $active){Remove-Item $active -Recurse -Force}}
