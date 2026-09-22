[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'build\app'
$test = Start-Process -FilePath (Join-Path $folder 'TaskbarTiles.exe') -ArgumentList '--test-launcher-experience' -WorkingDirectory $folder -PassThru
try {
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw 'Launcher experience tests timed out. Release rejected.' }
    $log = Join-Path $folder 'launcher-experience-test.log'
    if (Test-Path $log) { Get-Content $log }
    if ($test.ExitCode -ne 0) { throw 'Launcher experience tests failed. Release rejected.' }
} finally { $test.Dispose() }
