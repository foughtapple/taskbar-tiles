# ONLY for a disposable GitHub Actions Windows runner. Installs/uninstalls the app
# in the runner's account and proves upgrade/uninstall keep its sentinel user data.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Installer smoke tests are restricted to disposable GitHub Actions runners.' }
$root = Split-Path $PSScriptRoot -Parent
$version = (Get-Content (Join-Path $root 'version.txt') -Raw).Trim()
$setup = Join-Path $root "dist\TaskbarTiles-$version-Setup.exe"
$dest = Join-Path $env:LOCALAPPDATA 'TaskbarTiles'
if (Test-Path $dest) { throw 'Runner contains an existing installation; refusing destructive smoke tests.' }
function Run-Setup {
    $log = Join-Path $root 'build\installer-smoke.log'
    $p = Start-Process $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=""',('/LOG="' + $log + '"')) -PassThru
    try {
        if (-not $p.WaitForExit(120000)) { $p.Kill(); throw 'Installer timed out on the runner.' }
        if ($p.ExitCode -ne 0) { throw "Installer failed: $($p.ExitCode)" }
    } finally { $p.Dispose() }
}
Run-Setup
$exe = Join-Path $dest 'TaskbarTiles.exe'
if (-not (Test-Path $exe)) { throw 'Installer did not create its executable.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne ($version + '.0')) { throw 'Installed version mismatch.' }
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TaskbarTiles_is1'
if (-not (Test-Path $key)) { throw 'Installed Apps registration missing.' }
$settings = Join-Path $dest 'settings.ini'
Add-Content $settings '# INSTALLER-SMOKE-KEEP-ME'
$favourites = Join-Path $dest 'favourites.json'
[IO.File]::WriteAllText($favourites, '{"smoke_test":"preserve"}')
$before = (Get-FileHash $settings -Algorithm SHA256).Hash
Run-Setup
if ((Get-FileHash $settings -Algorithm SHA256).Hash -ne $before) { throw 'Upgrade modified existing settings.' }
if ((Get-Content $favourites -Raw) -ne '{"smoke_test":"preserve"}') { throw 'Upgrade modified favourites.' }
if (-not @(Get-ChildItem (Join-Path $dest 'Backups') -Filter TaskbarTiles.exe -Recurse).Count) { throw 'Upgrade did not back up the old executable.' }
$uninstall = Join-Path $dest 'unins000.exe'
$p = Start-Process $uninstall -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -PassThru
try {
    if (-not $p.WaitForExit(120000)) { $p.Kill(); throw 'Uninstaller timed out.' }
    if ($p.ExitCode -ne 0) { throw "Uninstaller failed: $($p.ExitCode)" }
} finally { $p.Dispose() }
if (Test-Path $exe) { throw 'Executable remained after uninstall.' }
if (Test-Path $key) { throw 'Installed Apps registration remained after uninstall.' }
if ((Get-FileHash $settings -Algorithm SHA256).Hash -ne $before) { throw 'Uninstall removed or modified user settings.' }
if (-not (Test-Path $favourites)) { throw 'Uninstall removed favourites.' }
'PASS: per-user registration, payload version, upgrade backup, configuration preservation, uninstall cleanup and retained user data.' | Set-Content (Join-Path $root 'build\installer-test-results.txt')
