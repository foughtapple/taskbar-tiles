[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'build\app'
$exe = Join-Path $folder 'TaskbarTiles.exe'
$log = Join-Path $folder 'organisation-test.log'
if (Test-Path $log) { Remove-Item $log -Force }
$test = Start-Process -FilePath $exe -ArgumentList '--test-navigation-update' -WorkingDirectory $folder -PassThru
try {
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw 'Settings and organisation tests timed out. Release rejected.' }
    if (Test-Path $log) { Get-Content $log } else { throw 'Missing organisation test log.' }
    if ($test.ExitCode -ne 0) { throw 'Settings and organisation tests failed. Release rejected.' }
} finally { $test.Dispose() }
