[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'build\app'
$exe = Join-Path $folder 'TaskbarTiles.exe'
$test = Start-Process -FilePath $exe -ArgumentList '--test-notification-area' -WorkingDirectory $folder -PassThru
try {
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw 'Notification-area tests timed out. Release rejected.' }
    $log = Join-Path $folder 'notification-area-test.log'
    if (Test-Path $log) { Get-Content $log }
    if ($test.ExitCode -ne 0) { throw 'Notification-area tests failed. Release rejected.' }
} finally {
    $test.Dispose()
}
