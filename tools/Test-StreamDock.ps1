[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dir=Join-Path $root 'build\app'
$priorBundle=$env:TASKBARTILES_MODULE_TEST_BUNDLE
$moduleBundle=Join-Path $root 'build\modules\streamdock'
if(Test-Path -LiteralPath (Join-Path $moduleBundle 'catalog.json')){$env:TASKBARTILES_MODULE_TEST_BUNDLE=$moduleBundle}
$p=Start-Process (Join-Path $dir 'TaskbarTiles.exe') -ArgumentList '--test-streamdock' -WorkingDirectory $dir -PassThru -WindowStyle Hidden
try { if(-not $p.WaitForExit(120000)){$p.Kill();throw 'Stream Dock manager tests timed out.'};if($p.ExitCode -ne 0){throw 'Stream Dock manager tests failed.'} } finally {$env:TASKBARTILES_MODULE_TEST_BUNDLE=$priorBundle;$p.Dispose();if(Test-Path (Join-Path $dir 'streamdock-test.log')){Get-Content (Join-Path $dir 'streamdock-test.log')}}
