# Only removes files owned by the optional local source installer; leaves user data.
$ErrorActionPreference = 'Stop'
$inno = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TaskbarTiles_is1'
if (Test-Path $inno) { throw 'Use the registered Taskbar Tiles uninstaller in Windows Settings > Apps. This old local-build remover will not touch the newer Setup installation.' }
Add-Type -AssemblyName System.Windows.Forms
$answer = [Windows.Forms.MessageBox]::Show('Remove the Taskbar Tiles local build? Settings, favourites and backups will be kept.', 'Uninstall Taskbar Tiles', 'YesNo', 'Question')
if ($answer -ne 'Yes') { exit 0 }
$dest = Join-Path $env:LOCALAPPDATA 'TaskbarTiles'
$exe = Join-Path $dest 'TaskbarTiles.exe'
if (Test-Path $exe) {
    Start-Process $exe -ArgumentList '--exit' -Wait
    for ($i = 0; $i -lt 60; $i++) {
        $running = @(Get-Process TaskbarTiles -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
        if ($running.Count -eq 0) { break }; Start-Sleep -Milliseconds 100
    }
    if ($running.Count) { throw 'Exit Taskbar Tiles from the tray before uninstalling.' }
}
foreach ($name in @('TaskbarTiles.exe','TaskbarTiles.exe.config','README.md','LICENSE','THIRD-PARTY-NOTICES.txt','CHANGELOG.md','XMOUSE-SETUP.txt')) {
    $file = Join-Path $dest $name; if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
}
$links = @((Join-Path ([Environment]::GetFolderPath('Startup')) 'Taskbar Tiles.lnk'), (Join-Path ([Environment]::GetFolderPath('Programs')) 'Taskbar Tiles\Taskbar Tiles.lnk'))
foreach ($link in $links) { if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link } }
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TaskbarTiles-LocalBuild'
if (Test-Path $key) { Remove-Item $key -Recurse }
[Windows.Forms.MessageBox]::Show('Removed. Your settings and favourites remain in ' + $dest, 'Taskbar Tiles') | Out-Null
