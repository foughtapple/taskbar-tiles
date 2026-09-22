[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'build\app'
$exe = Join-Path $folder 'TaskbarTiles.exe'
$test = Start-Process -FilePath $exe -ArgumentList '--test-launcher-experience' -WorkingDirectory $folder -PassThru
try {
    if (-not $test.WaitForExit(90000)) { $test.Kill(); throw 'Launcher experience tests timed out. Release rejected.' }
    $log = Join-Path $folder 'launcher-experience-test.log'
    if (Test-Path $log) { Get-Content $log }
    if ($test.ExitCode -ne 0) { throw 'Launcher experience tests failed. Release rejected.' }
    $assembly = [Reflection.Assembly]::LoadFrom($exe)
    $type = $assembly.GetType('TaskbarTiles.LauncherDescriptor', $true)
    $method = $type.GetMethod('StableId', [Reflection.BindingFlags]'NonPublic,Static')
    $one = $method.Invoke($null, @('C:\Fixture\App.exe', 'Profile.One'))
    $again = $method.Invoke($null, @('c:/fixture/app.exe', 'profile.one'))
    $other = $method.Invoke($null, @('C:\Fixture\App.exe', 'Profile.Two'))
    if ($one -cne $again -or $one -ceq $other) { throw 'Fallback identity stability/profile regression. Release rejected.' }
    Write-Output 'PASS: actual compiled fallback IDs remain stable across refresh/path casing and distinct for different profiles.'
} finally { $test.Dispose() }
