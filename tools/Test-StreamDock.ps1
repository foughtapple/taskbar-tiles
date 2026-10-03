[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dir=Join-Path $root 'build\app'
$priorBundle=$env:TASKBARTILES_MODULE_TEST_BUNDLE
$priorArchive=$env:TASKBARTILES_MODULE_TEST_ARCHIVE
$moduleBundle=Join-Path $root 'build\modules\streamdock'
if(Test-Path -LiteralPath (Join-Path $moduleBundle 'catalog.json')){$env:TASKBARTILES_MODULE_TEST_BUNDLE=$moduleBundle}
$index=Get-Content (Join-Path $root 'streamdock\module-index.json') -Raw | ConvertFrom-Json
$archive=Join-Path $root ('dist\'+$index.AssetName)
if(Test-Path -LiteralPath $archive){$env:TASKBARTILES_MODULE_TEST_ARCHIVE=$archive}
$p=Start-Process (Join-Path $dir 'TaskbarTiles.exe') -ArgumentList '--test-streamdock' -WorkingDirectory $dir -PassThru -WindowStyle Hidden
try { if(-not $p.WaitForExit(120000)){$p.Kill();throw 'Stream Dock manager tests timed out.'};if($p.ExitCode -ne 0){throw 'Stream Dock manager tests failed.'} } finally {$env:TASKBARTILES_MODULE_TEST_BUNDLE=$priorBundle;$env:TASKBARTILES_MODULE_TEST_ARCHIVE=$priorArchive;$p.Dispose();if(Test-Path (Join-Path $dir 'streamdock-test.log')){Get-Content (Join-Path $dir 'streamdock-test.log')}}
