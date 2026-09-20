# Isolated native registration/recovery test. Does not start the resident app.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'build\app'
$exe = Join-Path $app 'TaskbarTiles.exe'
if (-not (Test-Path $exe)) { throw 'Build the app first.' }
$process = Start-Process -FilePath $exe -ArgumentList '--test-touch-shortcuts' -WorkingDirectory $app -PassThru
try {
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Touch/shortcut test timed out.' }
    $process.Refresh()
    $log = Join-Path $app 'touch-shortcut-test.log'
    if (Test-Path $log) { Get-Content $log }
    if ($process.ExitCode -ne 0) { throw 'Touch/shortcut tests failed. Publication rejected.' }
} finally { $process.Dispose() }
