# Build reviewed Stream Dock modules into the normal application payload.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'streamdock\modules'
$work = Join-Path $root 'build\dock'
$bundle = Join-Path $root 'build\app\streamdock'
foreach ($tool in @('go','node')) { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is required for developer builds. Users should install the release Setup executable." } }
foreach ($p in @($work,$bundle)) { if (Test-Path $p) { Remove-Item $p -Recurse -Force }; New-Item -ItemType Directory -Path $p -Force | Out-Null }
New-Item -ItemType Directory -Path (Join-Path $bundle 'packages') -Force | Out-Null
foreach ($name in @('System.Windows.Forms','System.Drawing','System.Web.Extensions','System.IO.Compression','System.IO.Compression.FileSystem')) { Add-Type -AssemblyName $name }
$utf8 = New-Object Text.UTF8Encoding($false)
function Run-Checked([string]$file,[string]$arguments,[string]$cwd,[int]$seconds=60) {
    $p = Start-Process -FilePath $file -ArgumentList $arguments -WorkingDirectory $cwd -PassThru
    try { if (-not $p.WaitForExit($seconds*1000)) { $p.Kill(); throw "Build test timed out: $([IO.Path]::GetFileName($file))" }; if ($p.ExitCode -ne 0) { throw "Build test failed ($($p.ExitCode)): $([IO.Path]::GetFileName($file)) $arguments" } } finally { $p.Dispose() }
}
function Build-CSharp([string[]]$files,[string]$out,[string]$options) {
    $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
    $p = New-Object System.CodeDom.Compiler.CompilerParameters
    $p.GenerateExecutable=$true; $p.GenerateInMemory=$false; $p.OutputAssembly=$out
    $p.CompilerOptions='/target:winexe /platform:anycpu /optimize+ /codepage:65001 /langversion:5 '+$options
    foreach ($ref in @('System.dll','System.Core.dll',[Windows.Forms.Form].Assembly.Location,[Drawing.Bitmap].Assembly.Location,[Web.Script.Serialization.JavaScriptSerializer].Assembly.Location)) { [void]$p.ReferencedAssemblies.Add($ref) }
    try { $r=$provider.CompileAssemblyFromFile($p,$files); if ($r.Errors.HasErrors) { throw (($r.Errors | ForEach-Object {$_.ToString()}) -join "`n") } } finally {$provider.Dispose()}
}
function Build-Go([string]$dir,[string]$out) {
    Push-Location $dir
    try { & go test -count=1 -timeout 120s ./...; if ($LASTEXITCODE -ne 0) { throw "Go tests failed: $dir" }; & go build -trimpath -ldflags '-s -w -H=windowsgui' -o $out .; if ($LASTEXITCODE -ne 0) { throw "Go build failed: $dir" } } finally { Pop-Location }
}
$catalog = Get-Content (Join-Path $root 'streamdock\catalog-source.json') -Raw | ConvertFrom-Json
$packages = @()
foreach ($entry in $catalog.Packages) {
    $module = Join-Path $source $entry.Id
    $package = Join-Path $work $entry.Id
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    Copy-Item (Join-Path $module 'package\*') $package -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $package 'plugin') -Force | Out-Null
    switch ($entry.Id) {
      'controls' {
        $src = Join-Path $module 'src'
        $cs = [string[]]@(Get-ChildItem $src -Filter '*.cs' | Where-Object {$_.Name -ne 'ControlsHost.cs'} | ForEach-Object {$_.FullName})
        $buttons = @(@(1,'01 - Rocket League - Open Close'),@(2,'02 - Overwatch - Open Close'),@(5,'05 - FancyZone Screenshot'),@(6,'06 - Clipboard History'),@(7,'07 - GPT Voice - Pet'))
        foreach ($b in $buttons) {
          $exe = Join-Path $package ('plugin\'+$b[1]+'.exe')
          Build-CSharp $cs $exe ('/define:BUTTON'+([int]$b[0]).ToString('00')+' /win32manifest:"'+(Join-Path $src 'app.manifest')+'"')
          Run-Checked $exe '--check' (Split-Path $exe)
        }
        $capture = Join-Path $package 'plugin\05 - FancyZone Screenshot.exe'
        Run-Checked $capture '--self-test' (Split-Path $capture)
        Get-Content (Join-Path $package 'plugin\fallback-selftest.txt')
        $hostExe=Join-Path $package 'plugin\ControlsHost.exe'
        Build-CSharp @((Join-Path $src 'ControlsHost.cs')) $hostExe ''
        Run-Checked $hostExe '--validate' (Split-Path $hostExe)
        foreach ($script in Get-ChildItem (Join-Path $package 'plugin\Engine') -Recurse -Filter '*.ps1') {
          $tok=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile($script.FullName,[ref]$tok,[ref]$errors)
          if ($errors.Count) {throw "PowerShell parse failed: $($script.Name): $errors"}
        }
      }
      'steam' {
        $exe=Join-Path $package 'plugin\SteamSession.exe'
        Build-Go (Join-Path $module 'native') $exe
        Run-Checked $exe '--validate' (Split-Path $exe)
        Push-Location (Join-Path $module 'tests')
        try { & node --test; if ($LASTEXITCODE -ne 0) {throw 'Steam policy/lifecycle tests failed.'} } finally {Pop-Location}
        & node (Join-Path $package 'plugin\index.js') --validate
        if ($LASTEXITCODE -ne 0) {throw 'Steam plugin resource validation failed.'}
      }
      'desk' {
        $exe=Join-Path $package 'plugin\DeskStatus.exe'
        Build-Go (Join-Path $module 'src') $exe
        Run-Checked $exe '--validate' (Split-Path $exe)
      }
      'orders' {
        $exe=Join-Path $package 'plugin\NickNacksOrders.exe'
        Build-Go (Join-Path $module 'src') $exe
        Run-Checked $exe '--validate' (Split-Path $exe)
      }
      default {throw "No reviewed build command for $($entry.Id)."}
    }
    $m=Get-Content (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
    if ($m.Version -ne $entry.Version) {throw "Module version mismatch: $($entry.Id)"}
    $ids=@($m.Actions | ForEach-Object {$_.UUID})
    if (@(Compare-Object $ids @($entry.Actions | ForEach-Object {$_.Id})).Count) {throw "Action mapping mismatch: $($entry.Id)"}
    # Prove every manifest/inspector resource points inside this package and exists.
    $refs=@($m.Icon,$m.CategoryIcon)
    if ($m.PSObject.Properties.Name -contains 'CodePath') {$refs+=@($m.CodePath)}
    foreach($a in $m.Actions){$refs+=@($a.Icon,$a.PropertyInspectorPath);foreach($s in $a.States){$refs+=@($s.Image)}}
    foreach($r in $refs){if([string]::IsNullOrWhiteSpace($r)){continue};if($r -match '(^[\\/]|\.\.|:)'){throw 'Unsafe manifest resource'};if(-not(Test-Path -LiteralPath (Join-Path $package $r) -PathType Leaf)){throw "Missing resource: $($entry.Id)/$r"}}
    $zip=Join-Path $bundle ('packages\'+$entry.Id+'.zip')
    [IO.Compression.ZipFile]::CreateFromDirectory($package,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
    $packages += [ordered]@{Id=$entry.Id;Name=$entry.Name;Folder=$entry.Folder;Version=$entry.Version;Payload=('packages/'+$entry.Id+'.zip');SHA256=(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant();Actions=@($entry.Actions)}
}
$version=(Get-Content (Join-Path $root 'version.txt') -Raw).Trim()
$result=[ordered]@{Schema=1;BundleVersion=$version;Packages=@($packages)}
[IO.File]::WriteAllText((Join-Path $bundle 'catalog.json'),($result|ConvertTo-Json -Depth 12),$utf8)
Copy-Item (Join-Path $root 'docs\STREAM-DOCK.md') (Join-Path $bundle 'README.md')
Write-Host ('Stream Dock bundle ready: '+$packages.Count+' packages; '+(@($catalog.Packages.Actions).Count)+' actions.') -ForegroundColor Green
