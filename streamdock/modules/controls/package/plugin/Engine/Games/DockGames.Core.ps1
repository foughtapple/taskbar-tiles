# Pure parsing/selection functions. No registry, process, or network access.
# Compatible with Windows PowerShell 5.1. Steam VDF text is patched, not rebuilt.

function ConvertFrom-VdfQuotedString {
    param([Parameter(Mandatory=$true)][string]$Raw)
    if (-not $Raw.StartsWith('"')) { return $Raw }
    $builder = New-Object System.Text.StringBuilder
    for ($i = 1; $i -lt ($Raw.Length - 1); $i++) {
        $ch = $Raw[$i]
        if ($ch -eq '\' -and ($i + 1) -lt ($Raw.Length - 1)) {
            $i++
            switch ($Raw[$i]) {
                '"' { [void]$builder.Append('"') }
                '\' { [void]$builder.Append('\') }
                'n' { [void]$builder.Append("`n") }
                'r' { [void]$builder.Append("`r") }
                't' { [void]$builder.Append("`t") }
                default { [void]$builder.Append('\'); [void]$builder.Append($Raw[$i]) }
            }
        } else { [void]$builder.Append($ch) }
    }
    return $builder.ToString()
}

function Get-VdfTokens {
    param([Parameter(Mandatory=$true)][string]$Text)
    $pattern = '(?<space>\s+)|(?<comment>//[^\r\n]*)|(?<quoted>"(?:\\.|[^"\\])*")|(?<open>\{)|(?<close>\})|(?<bare>[^\s"{}]+)'
    $regex = [System.Text.RegularExpressions.Regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $position = 0
    while ($position -lt $Text.Length) {
        $match = $regex.Match($Text, $position)
        if (-not $match.Success -or $match.Index -ne $position) {
            throw "Invalid VDF text at character $position. No Steam files were changed."
        }
        $position += $match.Length
        if ($match.Groups['space'].Success -or $match.Groups['comment'].Success) { continue }
        $kind = 'String'
        if ($match.Groups['open'].Success) { $kind = 'Open' }
        if ($match.Groups['close'].Success) { $kind = 'Close' }
        [pscustomobject]@{
            Kind = $kind; Start = $match.Index; Length = $match.Length
            Value = (ConvertFrom-VdfQuotedString -Raw $match.Value)
        }
    }
}

function Read-VdfNodeList {
    param([object[]]$Tokens, [ref]$Position, [bool]$ExpectClose, [int]$Depth = 0)
    if ($Depth -gt 32) { throw 'Unexpected VDF nesting depth.' }
    $nodes = New-Object 'System.Collections.Generic.List[object]'
    while ($Position.Value -lt $Tokens.Count) {
        $key = $Tokens[$Position.Value]
        if ($key.Kind -eq 'Close') {
            if (-not $ExpectClose) { throw 'Unexpected closing brace in VDF.' }
            $Position.Value++
            return [pscustomobject]@{ Nodes = @($nodes.ToArray()); Close = $key }
        }
        if ($key.Kind -ne 'String') { throw 'Expected a VDF key.' }
        $Position.Value++
        if ($Position.Value -ge $Tokens.Count) { throw 'Missing VDF value.' }
        $value = $Tokens[$Position.Value]
        $Position.Value++
        if ($value.Kind -eq 'Open') {
            $child = Read-VdfNodeList -Tokens $Tokens -Position $Position -ExpectClose $true -Depth ($Depth + 1)
            [void]$nodes.Add([pscustomobject]@{
                Key = $key.Value; Kind = 'Object'; Children = @($child.Nodes)
                Close = $child.Close; Value = $null; ValueToken = $null
            })
        } elseif ($value.Kind -eq 'String') {
            [void]$nodes.Add([pscustomobject]@{
                Key = $key.Value; Kind = 'Value'; Children = @()
                Close = $null; Value = $value.Value; ValueToken = $value
            })
        } else { throw 'Missing VDF value before closing brace.' }
    }
    if ($ExpectClose) { throw 'Unclosed VDF object.' }
    return [pscustomobject]@{ Nodes = @($nodes.ToArray()); Close = $null }
}

function Read-SteamLoginText {
    param([Parameter(Mandatory=$true)][string]$Text)
    $tokens = @(Get-VdfTokens -Text $Text)
    $position = 0
    $root = Read-VdfNodeList -Tokens $tokens -Position ([ref]$position) -ExpectClose $false
    $userRoots = @($root.Nodes | Where-Object { $_.Key -ieq 'users' -and $_.Kind -eq 'Object' })
    if ($userRoots.Count -ne 1) { throw 'Expected one users object in loginusers.vdf.' }
    $records = New-Object 'System.Collections.Generic.List[object]'
    $seenIds = @{}
    foreach ($node in $userRoots[0].Children) {
        if ($node.Kind -ne 'Object' -or $node.Key -notmatch '^\d{17}$') {
            throw 'Unrecognised account structure in loginusers.vdf. Nothing was changed.'
        }
        if ($seenIds.ContainsKey($node.Key)) { throw 'Duplicate SteamID in loginusers.vdf.' }
        $seenIds[$node.Key] = $true
        $fields = @{}
        foreach ($field in $node.Children) {
            if ($fields.ContainsKey($field.Key)) { throw 'Duplicate account field in loginusers.vdf.' }
            $fields[$field.Key] = $field
        }
        if (-not $fields.ContainsKey('AccountName') -or $fields['AccountName'].Kind -ne 'Value') {
            throw 'A saved Steam account has no AccountName.'
        }
        $persona = $fields['AccountName'].Value
        if ($fields.ContainsKey('PersonaName') -and $fields['PersonaName'].Kind -eq 'Value') {
            $persona = $fields['PersonaName'].Value
        }
        $recent = $false
        if ($fields.ContainsKey('MostRecent')) { $recent = $fields['MostRecent'].Value -eq '1' }
        [void]$records.Add([pscustomobject]@{
            SteamId = $node.Key; AccountName = $fields['AccountName'].Value
            PersonaName = $persona; MostRecent = $recent; Fields = $fields; Node = $node
        })
    }
    return [pscustomobject]@{ Text = $Text; Accounts = @($records.ToArray()) }
}

function New-SteamLoginText {
    param([Parameter(Mandatory=$true)][string]$Text, [Parameter(Mandatory=$true)][string]$TargetSteamId)
    $document = Read-SteamLoginText -Text $Text
    if (@($document.Accounts | Where-Object { $_.SteamId -eq $TargetSteamId }).Count -ne 1) {
        throw 'The selected account is no longer in Steam. Run account setup again.'
    }
    $newline = "`n"
    if ($Text.Contains("`r`n")) { $newline = "`r`n" }
    $patches = New-Object 'System.Collections.Generic.List[object]'
    foreach ($account in $document.Accounts) {
        $desired = [ordered]@{ MostRecent = '0' }
        if ($account.SteamId -eq $TargetSteamId) {
            $desired['MostRecent'] = '1'
            $desired['RememberPassword'] = '1'
            $desired['AllowAutoLogin'] = '1'
        }
        $missing = New-Object 'System.Collections.Generic.List[string]'
        foreach ($key in $desired.Keys) {
            if ($account.Fields.ContainsKey($key)) {
                $field = $account.Fields[$key]
                if ($field.Kind -ne 'Value') { throw "Unexpected object in $key field." }
                if ($field.Value -ne $desired[$key]) {
                    [void]$patches.Add([pscustomobject]@{
                        Start = $field.ValueToken.Start; Length = $field.ValueToken.Length
                        NewText = ('"' + $desired[$key] + '"')
                    })
                }
            } else {
                [void]$missing.Add(('"' + $key + '"' + "`t`t" + '"' + $desired[$key] + '"'))
            }
        }
        if ($missing.Count -gt 0) {
            $closeStart = $account.Node.Close.Start
            $lineStart = $Text.LastIndexOf("`n", $closeStart) + 1
            $prefix = $Text.Substring($lineStart, $closeStart - $lineStart)
            if ($prefix -match '^\s*$') {
                $insertAt = $lineStart
                $newLines = @($missing | ForEach-Object { $prefix + "`t" + $_ })
                $insertText = ($newLines -join $newline) + $newline
            } else {
                $insertAt = $closeStart
                $insertText = $newline + (($missing | ForEach-Object { "`t`t" + $_ }) -join $newline) + $newline
            }
            [void]$patches.Add([pscustomobject]@{ Start = $insertAt; Length = 0; NewText = $insertText })
        }
    }
    $result = $Text
    foreach ($patch in @($patches.ToArray() | Sort-Object -Property Start -Descending)) {
        $result = $result.Remove($patch.Start, $patch.Length).Insert($patch.Start, $patch.NewText)
    }
    $check = Read-SteamLoginText -Text $result
    $recent = @($check.Accounts | Where-Object { $_.MostRecent })
    if ($recent.Count -ne 1 -or $recent[0].SteamId -ne $TargetSteamId) { throw 'VDF verification failed.' }
    if ($check.Accounts.Count -ne $document.Accounts.Count) { throw 'VDF account count changed unexpectedly.' }
    foreach ($before in $document.Accounts) {
        $after = @($check.Accounts | Where-Object { $_.SteamId -eq $before.SteamId })[0]
        if ($after.AccountName -cne $before.AccountName -or $after.PersonaName -cne $before.PersonaName) {
            throw 'VDF account details changed unexpectedly.'
        }
    }
    return $result
}

function Get-OppositeSteamId {
    param([string]$CurrentSteamId, [string]$FirstSteamId, [string]$SecondSteamId)
    if ($FirstSteamId -eq $SecondSteamId) { throw 'Choose two different Steam accounts.' }
    if ($CurrentSteamId -eq $FirstSteamId) { return $SecondSteamId }
    if ($CurrentSteamId -eq $SecondSteamId) { return $FirstSteamId }
    return $null
}

function ConvertTo-SteamId64 {
    param([long]$AccountId)
    # ActiveUser is a DWORD and can be returned as a signed Int32 by the registry.
    if ($AccountId -lt 0) { $AccountId += 4294967296L }
    if ($AccountId -le 0 -or $AccountId -gt 4294967295L) { return $null }
    return ([long]76561197960265728L + $AccountId).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}


# State helpers are pure and covered by synthetic tests. No Steam access.
function Get-ToggleIntent {
    param([bool]$HasGameProcess, [bool]$SteamReportsRunning, [bool]$TransitionBusy)
    if ($HasGameProcess -or $SteamReportsRunning) { return 'Close' }
    if ($TransitionBusy) { return 'Blocked' }
    return 'Launch'
}

function Get-ConnectionEvidence {
    param([AllowEmptyString()][string]$Text, [datetime]$NotBefore, [string]$ExpectedSteamId)
    # Only timestamped entries from this Steam process lifetime are eligible.
    # Connectivity-test success is NOT login success. Unknown formats fail closed.
    $state = 'Unknown'
    foreach ($line in ($Text -split "`n")) {
        $dateMatch = [regex]::Match($line, '^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]')
        if (-not $dateMatch.Success) { continue }
        $time = [datetime]::MinValue
        if (-not [datetime]::TryParseExact($dateMatch.Groups[1].Value, 'yyyy-MM-dd HH:mm:ss',
            [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$time)) { continue }
        if ($time -lt $NotBefore) { continue }
        if ($line -match '(?i)\[Logged\s+Off\s*,' -or $line -match '(?i)Log(?:ged)?\s*OffResponse|LogOnResponse.*\[(?:Fail|InvalidPassword|AccountLogonDenied|ServiceUnavailable)\]') {
            $state = 'NotLoggedOn'
        }
        $logged = [regex]::Match($line, '(?i)\[Logged\s+On\s*,[^\]]*\]\s*\[U:1:(\d+)\]')
        if ($logged.Success) {
            $id = ConvertTo-SteamId64 -AccountId ([long]$logged.Groups[1].Value)
            if ($id -eq $ExpectedSteamId) { $state = 'LoggedOn' } else { $state = 'OtherAccount' }
        } elseif ($line -match '(?i)RecvMsgClientLogOnResponse[^\r\n]*\[OK\]') {
            # This event is accepted only together with the separately verified,
            # stable ActiveUser belonging to the configured live steam.exe PID.
            $state = 'LogOnResponseOK'
        }
        if ($line -match '(?i)\bDisconnected\b' -and $line -notmatch '(?i)Connectivity\s+test') {
            $state = 'Disconnected'
        }
    }
    return $state
}
