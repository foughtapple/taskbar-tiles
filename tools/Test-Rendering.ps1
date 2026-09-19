# Uses a disposable test menu; never starts the tray app or installs software.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'build\app\TaskbarTiles.exe'
if (-not (Test-Path $exe)) { throw 'Build the app first.' }
$p = Start-Process -FilePath $exe -ArgumentList '--test-rendering' -PassThru -WorkingDirectory (Split-Path $exe)
try {
    if (-not $p.WaitForExit(120000)) { $p.Kill(); throw 'Rendering regression timed out.' }
    $log = Join-Path $root 'build\app\rendering-test.log'
    if (Test-Path $log) { Get-Content $log }
    if ($p.ExitCode -ne 0) { throw 'Rendering regression failed. Release rejected.' }
} finally { $p.Dispose() }
