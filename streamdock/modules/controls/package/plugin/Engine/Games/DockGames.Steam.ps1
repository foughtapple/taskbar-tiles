function Get-RegistryValue {
    param([string]$Path, [string]$Name)
    try { return Get-ItemPropertyValue -LiteralPath $Path -Name $Name -ErrorAction Stop }
    catch { return $null }
}

function Find-SteamExe {
    $candidates = New-Object 'System.Collections.Generic.List[string]'
    $registered = Get-RegistryValue -Path $script:SteamKey -Name 'SteamExe'
    if ($registered) { [void]$candidates.Add([string]$registered) }
    $steamPath = Get-RegistryValue -Path $script:SteamKey -Name 'SteamPath'
    if ($steamPath) { [void]$candidates.Add((Join-Path ([string]$steamPath) 'steam.exe')) }
    if (${env:ProgramFiles(x86)}) { [void]$candidates.Add((Join-Path ${env:ProgramFiles(x86)} 'Steam\steam.exe')) }
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { return (Get-Item -LiteralPath $path).FullName }
    }
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = 'Select your Steam.exe (not RocketLeague.exe)'
    $dialog.Filter = 'Steam executable (steam.exe)|steam.exe'
    try {
        if ($dialog.ShowDialog() -eq 'OK') { return $dialog.FileName }
        throw [System.OperationCanceledException]::new('Steam selection cancelled.')
    } finally { $dialog.Dispose() }
}

function Set-SteamLocation {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Steam.exe was not found. Run Setup again (inside the Helpers shortcut folder).' }
    if ([IO.Path]::GetFileName($Path) -ine 'steam.exe') { throw 'The selected executable must be Steam.exe.' }
    $script:SteamExe = (Get-Item -LiteralPath $Path).FullName
    $script:SteamDirectory = Split-Path -Parent $script:SteamExe
}

function Read-LoginFile {
    $path = Join-Path $script:SteamDirectory 'config\loginusers.vdf'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'Steam has no saved account list here. Sign into both accounts in Steam, remember them on this PC, and run setup again.'
    }
    $bytes = [IO.File]::ReadAllBytes($path)
    $offset = 0
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) { $offset = 3 }
    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $text = $utf8.GetString($bytes, $offset, $bytes.Length - $offset)
    $parsed = Read-SteamLoginText -Text $text
    return [pscustomobject]@{ Path = $path; Bytes = $bytes; Text = $text; HasBom = ($offset -eq 3); Accounts = @($parsed.Accounts) }
}

function Save-Json {
    param([object]$Value, [string]$Path)
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $json = $Value | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($Path, $json, $utf8)
}

function Get-ActiveSteamId {
    $key = Join-Path $script:SteamKey 'ActiveProcess'
    $clientPid = Get-RegistryValue -Path $key -Name 'pid'
    $accountId = Get-RegistryValue -Path $key -Name 'ActiveUser'
    if (-not $clientPid -or -not $accountId) { return $null }
    $process = Get-Process -Id ([int]$clientPid) -ErrorAction SilentlyContinue
    if (-not $process -or $process.ProcessName -ine 'steam' -or $process.SessionId -ne $script:SessionId) { return $null }
    try {
        if ([string]::IsNullOrEmpty($process.Path) -or -not [string]::Equals($process.Path, $script:SteamExe, [StringComparison]::OrdinalIgnoreCase)) { return $null }
    } catch { return $null }
    return (ConvertTo-SteamId64 -AccountId ([long]$accountId))
}

function Get-BytesHash {
    param([byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Bytes)) }
    finally { $sha.Dispose() }
}

function Set-RememberedAccount {
    param([string]$TargetSteamId)
    if (@(Get-ClientProcesses).Count -gt 0) { throw 'Steam restarted before the account edit. No login settings were changed.' }
    $document = Read-LoginFile
    $target = @($document.Accounts | Where-Object { $_.SteamId -eq $TargetSteamId })
    if ($target.Count -ne 1) { throw 'The selected account is not saved in Steam. Run setup again.' }
    $updatedText = New-SteamLoginText -Text $document.Text -TargetSteamId $TargetSteamId
    $encoding = [System.Text.UTF8Encoding]::new($document.HasBom, $true)
    [byte[]]$updatedBytes = $encoding.GetPreamble() + $encoding.GetBytes($updatedText)
    $backupRoot = Join-Path $script:DataPath 'Backups'
    [void][IO.Directory]::CreateDirectory($backupRoot)
    $backupId = (Get-Date).ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
    $backupPath = Join-Path $backupRoot ($backupId + '.loginusers.vdf.bak')
    $previous = Get-RegistryValue -Path $script:SteamKey -Name 'AutoLoginUser'
    $snapshot = [pscustomobject]@{
        Created = (Get-Date).ToString('o'); OriginalPath = $document.Path
        AutoLoginUserExisted = ($null -ne $previous); AutoLoginUser = $previous
    }
    [IO.File]::WriteAllBytes($backupPath, $document.Bytes)
    Save-Json -Value $snapshot -Path (Join-Path $backupRoot ($backupId + '.registry.json'))
    # Same-directory atomic replacement, after checking that the source is unchanged.
    $temporary = $document.Path + '.streamdock-' + [guid]::NewGuid().ToString('N') + '.tmp'
    $fileChanged = $false
    try {
        [IO.File]::WriteAllBytes($temporary, $updatedBytes)
        if (@(Get-ClientProcesses).Count -gt 0 -or (Get-BytesHash ([IO.File]::ReadAllBytes($document.Path))) -ne (Get-BytesHash $document.Bytes)) {
            throw 'Steam/account data changed while preparing the switch. Try again after exiting Steam.'
        }
        [IO.File]::Replace($temporary, $document.Path, $null)
        $fileChanged = $true
        [void](New-ItemProperty -LiteralPath $script:SteamKey -Name 'AutoLoginUser' -Value $target[0].AccountName -PropertyType String -Force)
        Write-SwitchLog ('Saved backup ' + [IO.Path]::GetFileName($backupPath) + '; selected remembered account.')
    } catch {
        # Roll back only if Steam is still stopped and the file is still our exact edit.
        if ($fileChanged -and @(Get-ClientProcesses).Count -eq 0) {
            try {
                if ((Get-BytesHash ([IO.File]::ReadAllBytes($document.Path))) -eq (Get-BytesHash $updatedBytes)) {
                    [IO.File]::WriteAllBytes($document.Path, $document.Bytes)
                    if ($null -ne $previous) {
                        [void](New-ItemProperty -LiteralPath $script:SteamKey -Name 'AutoLoginUser' -Value $previous -PropertyType String -Force)
                    } else { Remove-ItemProperty -LiteralPath $script:SteamKey -Name 'AutoLoginUser' -ErrorAction SilentlyContinue }
                }
            } catch { Write-SwitchLog 'Automatic rollback was unavailable. Original backup is preserved.' }
        }
        throw
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
    }
}
