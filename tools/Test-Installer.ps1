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
& (Join-Path $PSScriptRoot 'Test-StreamDockInstaller.ps1') -InstallRoot $dest -Setup $setup
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

# Fresh-install defaults are tested separately from the upgrade-preservation path:
# Stream Dock integration ON; Touch Return master OFF.
Remove-Item $dest -Recurse -Force
$activeDock = Join-Path $env:APPDATA 'HotSpot\StreamDock\plugins'
if (Test-Path $activeDock) { throw 'Stream Dock installer test did not clean its disposable plugin root.' }
$defaultLog = Join-Path $root 'build\installer-defaults.log'
$p = Start-Process $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/LOG="' + $defaultLog + '"')) -PassThru
try {
    if (-not $p.WaitForExit(120000)) { $p.Kill(); throw 'Default installer test timed out.' }
    if ($p.ExitCode -ne 0) { throw "Default installer test failed: $($p.ExitCode)" }
} finally { $p.Dispose() }
$exe = Join-Path $dest 'TaskbarTiles.exe'
$settings = Join-Path $dest 'settings.ini'
if (-not ((Get-Content -LiteralPath $settings) -contains 'TouchSupportEnabled=false')) { throw 'Touch Return was not off by default on a fresh install.' }
$statePath = Join-Path $dest 'StreamDockData\state.json'
if (-not (Test-Path $statePath)) { throw 'Fresh install did not create the default Stream Dock state.' }
$state = Get-Content $statePath -Raw | ConvertFrom-Json
$catalog = Get-Content (Join-Path $dest 'streamdock\catalog.json') -Raw | ConvertFrom-Json
$expected = @($catalog.Packages | ForEach-Object { $_.Actions } | ForEach-Object { $_.Id } | Sort-Object)
$actual = @($state.EnabledActions | Sort-Object)
if (@(Compare-Object $expected $actual).Count) { throw 'Fresh install did not enable every bundled Stream Dock action.' }
if (-not $state.AutoUpdate) { throw 'Fresh Stream Dock integration did not default auto-update on.' }
$unified = Join-Path $activeDock 'com.foughtapple.taskbartiles.sdPlugin\manifest.json'
if (-not (Test-Path $unified)) { throw 'Fresh default install did not activate the unified Stream Dock plugin.' }
$manifest = Get-Content $unified -Raw | ConvertFrom-Json
if (@(Compare-Object $expected @($manifest.Actions.UUID | Sort-Object)).Count) { throw 'Installed Stream Dock manifest does not contain all default actions.' }

# Prove the optional installer backend can enable the Touch Return master only
# when selected. It still has no verified monitor rule, so automatic return cannot run.
$p = Start-Process $exe -ArgumentList '--installer-enable-touch' -WorkingDirectory $dest -PassThru
try {
    if (-not $p.WaitForExit(30000)) { $p.Kill(); throw 'Touch Return installer-option backend timed out.' }
    if ($p.ExitCode -ne 0) { throw 'Touch Return installer-option backend failed.' }
} finally { $p.Dispose() }
if (-not ((Get-Content -LiteralPath $settings) -contains 'TouchSupportEnabled=true')) { throw 'Touch Return installer option could not enable its master switch.' }

$uninstall = Join-Path $dest 'unins000.exe'
$p = Start-Process $uninstall -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -PassThru
try {
    if (-not $p.WaitForExit(120000)) { $p.Kill(); throw 'Default-install uninstaller timed out.' }
    if ($p.ExitCode -ne 0) { throw "Default-install uninstaller failed: $($p.ExitCode)" }
} finally { $p.Dispose() }
if (Test-Path $activeDock) { Remove-Item $activeDock -Recurse -Force }
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'Taskbar Tiles.lnk'
if (Test-Path $startupLink) { Remove-Item $startupLink -Force }
'PASS: per-user registration, upgrade preservation, fresh Stream Dock-on / Touch Return-off installer defaults, optional Touch Return enablement, uninstall cleanup and retained user data.' | Set-Content (Join-Path $root 'build\installer-test-results.txt')
