# Build and run deterministic helper tests. Windows PowerShell 5.1 / .NET Framework 4.8.
# Never writes into an existing installation and never downloads dependencies.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Run Build.cmd or Windows PowerShell 5.1, not PowerShell 7.' }
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'src\TaskbarTiles'
$output = Join-Path $root 'build\app'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$version = (Get-Content (Join-Path $root 'version.txt') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid version.txt' }
$core = Get-Content (Join-Path $source 'TaskbarTiles.cs') -Raw
if (-not $core.Contains('internal const string Version = "' + $version + '";')) { throw 'Version constants do not match version.txt.' }
foreach ($name in @('System.Windows.Forms','System.Drawing','WindowsBase','UIAutomationClient','UIAutomationTypes','Microsoft.CSharp','System.Web.Extensions','System.Data')) { Add-Type -AssemblyName $name }
$refs = @('System.dll','System.Core.dll', [System.Data.OleDb.OleDbConnection].Assembly.Location,
    [System.Windows.Forms.Form].Assembly.Location, [System.Drawing.Bitmap].Assembly.Location,
    [System.Windows.Rect].Assembly.Location, [System.Windows.Automation.AutomationElement].Assembly.Location,
    [System.Windows.Automation.ControlType].Assembly.Location, [Microsoft.CSharp.RuntimeBinder.Binder].Assembly.Location,
    [System.Web.Script.Serialization.JavaScriptSerializer].Assembly.Location) | Select-Object -Unique
$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$parameters = New-Object System.CodeDom.Compiler.CompilerParameters
$parameters.GenerateExecutable = $true
$parameters.GenerateInMemory = $false
$parameters.IncludeDebugInformation = $false
$parameters.OutputAssembly = Join-Path $output 'TaskbarTiles.exe'
$parameters.CompilerOptions = '/target:winexe /platform:anycpu /optimize+ /codepage:65001 /langversion:5 /win32manifest:"' + (Join-Path $source 'app.manifest') + '" /win32icon:"' + (Join-Path $root 'assets\TaskbarTiles.ico') + '"'
foreach ($reference in $refs) { [void]$parameters.ReferencedAssemblies.Add($reference) }
Write-Host "Building Taskbar Tiles $version..."
try {
    $paths = [string[]]@(Get-ChildItem -LiteralPath $source -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
    $result = $provider.CompileAssemblyFromFile($parameters, $paths)
} finally { $provider.Dispose() }
@($result.Output | ForEach-Object { [string]$_ }) | Set-Content (Join-Path $root 'build\build.log') -Encoding UTF8
if ($result.Errors.HasErrors) {
    $details = ($result.Errors | Where-Object { -not $_.IsWarning } | ForEach-Object { $_.ToString() }) -join "`r`n"
    throw "Compilation failed; your installed copy is unchanged.`r`n$details"
}
Copy-Item (Join-Path $source 'app.config') ($parameters.OutputAssembly + '.config') -Force
Copy-Item (Join-Path $root 'config\settings.ini') (Join-Path $output 'settings.ini') -Force
foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.txt','CHANGELOG.md')) { Copy-Item (Join-Path $root $name) (Join-Path $output $name) -Force }
Copy-Item (Join-Path $root 'docs\XMOUSE-SETUP.txt') (Join-Path $output 'XMOUSE-SETUP.txt') -Force
Write-Host 'Running isolated helper tests (no desktop switching or network access)...'
$test = Start-Process -FilePath $parameters.OutputAssembly -ArgumentList '--self-test' -WorkingDirectory $output -PassThru
try {
    if (-not $test.WaitForExit(120000)) { $test.Kill(); throw 'Helper tests exceeded two minutes. Build rejected.' }
    if ($test.ExitCode -ne 0) { throw "Helper tests failed. See $output\self-test.log. Build rejected." }
} finally { $test.Dispose() }
Get-Content (Join-Path $output 'self-test.log')
Write-Host "Build and helper tests passed: $output" -ForegroundColor Green
