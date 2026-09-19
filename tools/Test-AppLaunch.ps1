# Explicit CI integration test. Launches only nonce-bound copies of the test fixture.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'build\app\TaskbarTiles.exe'
$test = Start-Process -FilePath $exe -ArgumentList '--test-launch-outcome' -WorkingDirectory (Split-Path $exe) -PassThru
try {
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw 'Launch fixture tests timed out. Release rejected.' }
    $log = Join-Path $root 'build\app\launch-outcome-test.log'
    if (Test-Path $log) { Get-Content $log }
    if ($test.ExitCode -ne 0) { throw 'Native app-managed launch tests failed. Release rejected.' }
} finally { $test.Dispose() }
