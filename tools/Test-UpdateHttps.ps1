# Explicit online integration test. Invokes the compiled EXE with its own .config.
# GETs a public release and verifies its installer download; never runs the installer.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'build\app'
$exe = Join-Path $app 'TaskbarTiles.exe'
$log = Join-Path $app 'update-network-test.log'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build the application before testing update HTTPS.' }
if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
$test = Start-Process -FilePath $exe -ArgumentList '--test-update-https' -WorkingDirectory $app -PassThru
try {
    if (-not $test.WaitForExit(240000)) { $test.Kill(); throw 'Updater HTTPS test exceeded four minutes.' }
    if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log }
    if ($test.ExitCode -ne 0) { throw 'The real executable could not check/download/verify a GitHub update. Release rejected.' }
    if (-not (Test-Path -LiteralPath $log)) { throw 'Missing updater HTTPS test record.' }
} finally { $test.Dispose() }
