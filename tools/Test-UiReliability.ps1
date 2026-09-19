# Explicit native CI checks on disposable test windows, not the installed tray app.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'build\app'
$test = Start-Process -FilePath (Join-Path $folder 'TaskbarTiles.exe') -ArgumentList '--test-ui-reliability' -WorkingDirectory $folder -PassThru
try {
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw 'UI reliability tests timed out. Release rejected.' }
    $log = Join-Path $folder 'ui-reliability-test.log'
    if (Test-Path $log) { Get-Content $log }
    if ($test.ExitCode -ne 0) { throw 'Native paint/font/click-away tests failed. Release rejected.' }
} finally { $test.Dispose() }
