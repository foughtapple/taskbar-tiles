# Build the per-user Inno Setup installer. Inno Setup 6 must already be installed.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Build.ps1')
$version = (Get-Content (Join-Path $root 'version.txt') -Raw).Trim()
$candidates = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe")
$compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { $found = Get-Command ISCC.exe -ErrorAction SilentlyContinue; if ($found) { $compiler = $found.Source } }
if (-not $compiler) { throw 'Inno Setup 6 is required to package Setup.exe. Download from https://jrsoftware.org/isdl.php. GitHub windows-2022 runners include it.' }
& $compiler ("/DAppVersion=$version") (Join-Path $root 'installer\TaskbarTiles.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed: exit $LASTEXITCODE" }
$dist = Join-Path $root 'dist'
$setup = Join-Path $dist "TaskbarTiles-$version-Setup.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw 'Expected installer was not created.' }
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), "$hash  $([IO.Path]::GetFileName($setup))`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "Packaged: $setup" -ForegroundColor Green
