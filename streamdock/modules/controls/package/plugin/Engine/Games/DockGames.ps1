#requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('Setup','Status','RocketToggle','OverwatchToggle')][string]$Mode = 'Setup', [switch]$PlainRunner)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$script:HomePath = $PSScriptRoot
$script:DataPath = Join-Path $env:LOCALAPPDATA 'FoughtApple\StreamDockControls\Games'
# One-time migration. Original working helpers are never changed.
if (-not (Test-Path -LiteralPath (Join-Path $script:DataPath 'settings.json'))) {
    $oldData = Join-Path $env:LOCALAPPDATA 'FoughtApple\StreamDock-Games\Buttons-Plain\Engine\Games\UserData'
    if (Test-Path -LiteralPath (Join-Path $oldData 'settings.json')) {
        [void][IO.Directory]::CreateDirectory($script:DataPath)
        Copy-Item -LiteralPath (Join-Path $oldData 'settings.json') -Destination (Join-Path $script:DataPath 'settings.json')
    }
}
[void][IO.Directory]::CreateDirectory($script:DataPath)
$script:ConfigPath = Join-Path $script:DataPath 'settings.json'
$script:SessionId = (Get-Process -Id $PID).SessionId
$script:SteamKey = 'HKCU:\Software\Valve\Steam'
$script:SteamExe = $null
$script:SteamDirectory = $null
$script:LogPath = Join-Path $script:DataPath ('operation-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $PID + '.log')
$script:Form = $null
$script:ProgressLabel = $null
$script:ElapsedLabel = $null
$script:VerifyButton = $null
$script:Cancelled = $false
$script:ClosingForm = $false
$script:VerificationClicked = $false
$script:Stopwatch = [Diagnostics.Stopwatch]::StartNew()
$script:LastStatus = ''
$script:HeldLocks = New-Object 'System.Collections.Generic.List[object]'
$script:Games = @{
    RocketLeague = [pscustomobject]@{ Name='Rocket League'; Process='RocketLeague'; AppId='252950' }
    Overwatch = [pscustomobject]@{ Name='Overwatch'; Process='Overwatch'; AppId='2357570' }
}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
. (Join-Path $PSScriptRoot 'DockGames.Core.ps1')
. (Join-Path $PSScriptRoot 'DockGames.Steam.ps1')
. (Join-Path $PSScriptRoot 'DockGames.UI.ps1')
. (Join-Path $PSScriptRoot 'DockGames.Actions.ps1')

$exitCode = 0
try {
    Write-SwitchLog ('Plain7-1.0; request ' + $Mode)
    if ($Mode -eq 'Setup') {
        if (-not (Try-Lock 'Transition')) { throw 'An operation is running. Finish or cancel it before changing setup.' }
        Show-Setup
        Show-Message 'Setup saved. No game was closed and no account was switched.'
    } else {
        $config = Get-Configuration -RequestedMode $Mode
        if ($Mode -eq 'Status') {
            Show-Status -Config $config
        } else {
            $game = 'RocketLeague'
            if ($Mode -eq 'OverwatchToggle') { $game = 'Overwatch' }
            Invoke-GameToggle -Key $game -Config $config
        }
    }
} catch [System.OperationCanceledException] {
    $exitCode = 2
    Write-SwitchLog 'Cancelled. Already-sent close/launch requests and completed account changes are not undone.'
} catch {
    $exitCode = 1
    Write-SwitchLog ('ERROR: ' + $_.Exception.Message + ' | ' + $_.ScriptStackTrace)
    Close-ProgressWindow
    if ($PlainRunner) {
        Write-Output ('ACTION ERROR: ' + $_.Exception.Message)
        Write-Output ('At: ' + $_.ScriptStackTrace)
        Write-Output ('Game log: ' + $script:LogPath)
    } else {
        Show-Message ($_.Exception.Message + "`r`n`r`nLog: " + $script:LogPath) 'Stream Dock Games - needs attention' 'Warning'
    }
} finally {
    Close-ProgressWindow
    foreach ($held in $script:HeldLocks) {
        try { [void]$held.Handle.ReleaseMutex() } catch { }
        $held.Handle.Dispose()
    }
}
exit $exitCode
