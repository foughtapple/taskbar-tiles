# Isolated CI test only: creates disposable Forms, not the user's tray app.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'build\app\TaskbarTiles.exe'
$log = Join-Path $root 'build\app\switcher-layer-test.log'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build first.' }
$test = Start-Process -FilePath $exe -ArgumentList '--test-switcher-layer' -PassThru -WorkingDirectory (Split-Path $exe -Parent)
try {
    if (-not $test.WaitForExit(60000)) { $test.Kill(); throw 'Z-order tests timed out.' }
    if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log }
    if ($test.ExitCode -ne 0) { throw 'Native switcher Z-order tests failed. Release rejected.' }
} finally { $test.Dispose() }
