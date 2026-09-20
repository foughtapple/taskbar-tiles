# Native shortcut test on a disposable Windows runner. Does not start the tray application.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'build\app'
$test = Start-Process -FilePath (Join-Path $folder 'TaskbarTiles.exe') -ArgumentList '--test-input-support' -WorkingDirectory $folder -PassThru
try {
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw 'Shortcut input tests timed out. Release rejected.' }
    $log = Join-Path $folder 'input-support-test.log'
    if (Test-Path $log) { Get-Content $log }
    if ($test.ExitCode -ne 0) { throw 'Shortcut input tests failed. Release rejected.' }
} finally { $test.Dispose() }
