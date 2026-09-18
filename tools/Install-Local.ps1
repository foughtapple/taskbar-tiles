# Optional source-build install, for testing before publishing a GitHub release.
# The public release's Setup.exe is the preferred installation route.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Build.ps1')
$dest = Join-Path $env:LOCALAPPDATA 'TaskbarTiles'
$exe = Join-Path $dest 'TaskbarTiles.exe'
$inno = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TaskbarTiles_is1'
if (Test-Path $inno) { throw 'A Setup.exe installation is already registered. Upgrade it with the GitHub release installer, not this local-build script.' }
$startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'Taskbar Tiles.lnk'
$existing = Test-Path -LiteralPath $exe
$keepStartup = (-not $existing) -or (Test-Path -LiteralPath $startup)
New-Item -ItemType Directory -Path $dest -Force | Out-Null
if ($existing) {
    Start-Process -FilePath $exe -ArgumentList '--exit' -Wait
    for ($i = 0; $i -lt 60; $i++) {
        $running = @(Get-Process -Name TaskbarTiles -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($running.Count -ne 0) { throw 'Exit Taskbar Tiles from its tray icon, then retry. The installed executable is unchanged.' }
    $backup = Join-Path $dest ('Backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    foreach ($name in @('TaskbarTiles.exe','TaskbarTiles.exe.config','settings.ini','favourites.json')) {
        $path = Join-Path $dest $name
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $backup }
    }
    Write-Host "Previous copy backed up to $backup"
}
$payload = Join-Path $root 'build\app'
foreach ($name in @('TaskbarTiles.exe','TaskbarTiles.exe.config','README.md','LICENSE','THIRD-PARTY-NOTICES.txt','CHANGELOG.md','XMOUSE-SETUP.txt')) { Copy-Item -LiteralPath (Join-Path $payload $name) -Destination (Join-Path $dest $name) -Force }
if (-not (Test-Path (Join-Path $dest 'settings.ini'))) { Copy-Item (Join-Path $payload 'settings.ini') (Join-Path $dest 'settings.ini') }
Copy-Item (Join-Path $PSScriptRoot 'Uninstall-Local.ps1') (Join-Path $dest 'Uninstall-Local.ps1') -Force
$shell = New-Object -ComObject WScript.Shell
try {
    $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Taskbar Tiles'
    New-Item -ItemType Directory -Path $menu -Force | Out-Null
    $paths = @(Join-Path $menu 'Taskbar Tiles.lnk')
    if ($keepStartup) { $paths += $startup }
    foreach ($path in $paths) {
        $shortcut = $shell.CreateShortcut($path)
        try {
            $shortcut.TargetPath = $exe; $shortcut.WorkingDirectory = $dest
            if ($path -ne $startup) { $shortcut.Arguments = '--show' }
            $shortcut.Description = 'Taskbar Tiles'; $shortcut.Save()
        } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
    }
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
$uninstall = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TaskbarTiles-LocalBuild'
New-Item $uninstall -Force | Out-Null
$ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$properties = @{
    DisplayName = 'Taskbar Tiles (local source build)'; DisplayVersion = (Get-Content (Join-Path $root 'version.txt') -Raw).Trim();
    Publisher = 'Taskbar Tiles contributors'; DisplayIcon = $exe; InstallLocation = $dest;
    URLInfoAbout = 'https://github.com/foughtapple/taskbar-tiles';
    UninstallString = '"' + $ps + '" -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $dest 'Uninstall-Local.ps1') + '"'
}
foreach ($key in $properties.Keys) { New-ItemProperty $uninstall -Name $key -Value $properties[$key] -PropertyType String -Force | Out-Null }
New-ItemProperty $uninstall -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty $uninstall -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
Start-Process $exe -WorkingDirectory $dest
Write-Host 'Installed and started. Existing settings and mouse command retained. Windows Settings > Apps can remove this local build.' -ForegroundColor Green
