@echo off
setlocal
cd /d "%~dp0"
echo This builds and installs Taskbar Tiles locally. The GitHub Setup.exe is preferred for releases.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0tools\Install-Local.ps1"
if errorlevel 1 (echo Installation failed; review the error above. & pause & exit /b 1)
pause
