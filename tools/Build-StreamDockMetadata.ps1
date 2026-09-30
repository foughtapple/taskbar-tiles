# The application payload contains only action/release metadata, never workers.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$metadata=Join-Path $root 'build\app\streamdock'
$boundary=[IO.Path]::GetFullPath((Join-Path $root 'build')).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
$metadata=[IO.Path]::GetFullPath($metadata)
if(-not $metadata.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Metadata path leaves the workspace build directory.'}
if(Test-Path -LiteralPath $metadata){Remove-Item -LiteralPath $metadata -Recurse -Force}
New-Item -ItemType Directory -Path $metadata -Force | Out-Null
$index=Get-Content -LiteralPath (Join-Path $root 'streamdock\module-index.json') -Raw | ConvertFrom-Json
$catalog=Get-Content -LiteralPath (Join-Path $root 'streamdock\catalog-source.json') -Raw | ConvertFrom-Json
$packages=@()
foreach($entry in $catalog.Packages){
 if($entry.Version -ne $index.Version){throw 'Module catalog and release index versions differ.'}
 $packages += [ordered]@{Id=$entry.Id;Name=$entry.Name;Folder=$entry.Folder;Version=$entry.Version;Payload=('packages/'+$entry.Id+'.zip');SHA256=('0'*64);LegacyFolders=@($entry.LegacyFolders);LegacyPackageIds=@($entry.LegacyPackageIds);Actions=@($entry.Actions)}
}
$result=[ordered]@{Schema=1;BundleVersion=$index.Version;Packages=@($packages)}
$utf8=New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $metadata 'catalog.json'),($result|ConvertTo-Json -Depth 12),$utf8)
Copy-Item -LiteralPath (Join-Path $root 'streamdock\module-index.json') -Destination (Join-Path $metadata 'module-index.json') -Force
Write-Host 'Stream Dock metadata ready; optional worker code is excluded from the app.'
