# Explicit native CI checks on disposable windows, not the installed tray app.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'build\app'
$test = Start-Process -FilePath (Join-Path $folder 'TaskbarTiles.exe') -ArgumentList '--test-clickaway' -WorkingDirectory $folder -PassThru
try {
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw 'Outside-click tests timed out. Release rejected.' }
    $log = Join-Path $folder 'outside-click-test.log'
    if (Test-Path $log) { Get-Content $log }
    if ($test.ExitCode -ne 0) { throw 'Native outside-click tests failed. Release rejected.' }
} finally { $test.Dispose() }
