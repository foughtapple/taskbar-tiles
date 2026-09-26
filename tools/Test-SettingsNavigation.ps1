[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'build\app'
$exe = Join-Path $folder 'TaskbarTiles.exe'
$test = Start-Process -FilePath $exe -ArgumentList '--test-settings-navigation' -WorkingDirectory $folder -PassThru
try {
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw 'Settings navigation tests timed out. Release rejected.' }
    $log = Join-Path $folder 'settings-navigation-test.log'
    if (Test-Path $log) { Get-Content $log }
    if ($test.ExitCode -ne 0) { throw 'Settings navigation tests failed. Release rejected.' }
} finally {
    $test.Dispose()
}
