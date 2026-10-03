# CI-only publication of the separately versioned module, after validation.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REPOSITORY -cne 'foughtapple/taskbar-tiles') { throw 'Publication requires the repository CI job.' }
$root = Split-Path $PSScriptRoot -Parent
$index = Get-Content (Join-Path $root 'streamdock/module-index.json') -Raw | ConvertFrom-Json
$tag = $index.ReleaseTag
if ($tag -cne ('streamdock-v' + $index.Version) -or $index.AssetName -cne ('TaskbarTiles-StreamDock-' + $index.Version + '.zip')) { throw 'Invalid module release identity.' }
$known = & git tag --list $tag
if ($known) {
    & git diff --quiet $tag HEAD -- streamdock tools/Build-StreamDock.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Published module source changed without a new module version.' }
}
$oldPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$existing = & gh release view $tag --repo $env:GITHUB_REPOSITORY --json isDraft,assets 2>$null
$exists = $LASTEXITCODE -eq 0
$ErrorActionPreference = $oldPreference
if ($exists -and -not ($existing | ConvertFrom-Json).isDraft) {
    $assets = @(($existing | ConvertFrom-Json).assets | ForEach-Object { $_.name })
    foreach ($required in @($index.AssetName, 'SHA256SUMS.txt', 'build-info.json')) {
        if (@($assets | Where-Object { $_ -ceq $required }).Count -ne 1) { throw 'Published module assets are incomplete. Publish a new module version.' }
    }
    Write-Host "Reusing immutable published module $tag."
    return
}
if (-not $known) {
    & gh api "repos/$env:GITHUB_REPOSITORY/git/refs" --method POST -f "ref=refs/tags/$tag" -f "sha=$env:GITHUB_SHA"
    if ($LASTEXITCODE -ne 0) { throw 'Module tag creation failed.' }
}
$notes=Join-Path $root ('docs/releases/'+$tag+'.md')
if(-not(Test-Path -LiteralPath $notes)){$notes=Join-Path $root 'docs/STREAM-DOCK.md'}
if (-not $exists) {
    & gh release create $tag --repo $env:GITHUB_REPOSITORY --verify-tag --draft --title "Stream Dock module $($index.Version)" --notes-file $notes
    if ($LASTEXITCODE -ne 0) { throw 'Module draft creation failed.' }
}
$staged = Join-Path $root 'dist/module-release'
New-Item -ItemType Directory -Path $staged -Force | Out-Null
Copy-Item (Join-Path $root 'dist/module-SHA256SUMS.txt') (Join-Path $staged 'SHA256SUMS.txt') -Force
Copy-Item (Join-Path $root 'dist/module-build-info.json') (Join-Path $staged 'build-info.json') -Force
& gh release upload $tag (Join-Path $root ('dist/' + $index.AssetName)) (Join-Path $staged 'SHA256SUMS.txt') (Join-Path $staged 'build-info.json') --repo $env:GITHUB_REPOSITORY --clobber
if ($LASTEXITCODE -ne 0) { throw 'Module draft upload failed.' }
& gh release edit $tag --repo $env:GITHUB_REPOSITORY --draft=false --latest=false
if ($LASTEXITCODE -ne 0) { throw 'Module publication failed.' }
