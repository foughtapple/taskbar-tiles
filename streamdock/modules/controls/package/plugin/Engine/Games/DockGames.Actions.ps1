
function Try-Lock {
    param([string]$Name)
    if (@($script:HeldLocks | Where-Object { $_.Name -eq $Name }).Count -gt 0) { return $true }
    # Share the old v1 transition lock so old and new shortcuts cannot switch concurrently.
    $mutexName='Local\FoughtApple.StreamDock.Games.v2.' + $Name
    if ($Name -eq 'Transition') { $mutexName='Local\FoughtApple.StreamDock.SteamRL.v1' }
    $handle=[Threading.Mutex]::new($false, $mutexName)
    $owned=$false
    try { $owned=$handle.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned=$true }
    if ($owned) {
        [void]$script:HeldLocks.Add([pscustomobject]@{Name=$Name;Handle=$handle})
        return $true
    }
    $handle.Dispose()
    return $false
}
function Get-ClientProcesses {
    Get-Process -Name steam,steamwebhelper -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $script:SessionId }
}
function Get-GameProcesses {
    param([string]$Key)
    Get-Process -Name $script:Games[$Key].Process -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $script:SessionId }
}
function Get-RunningSteamApps {
    # Ignore stale registry Running bits when the client has fully exited.
    if (@(Get-ClientProcesses).Count -eq 0) { return }
    $path=Join-Path $script:SteamKey 'Apps'
    if (Test-Path -LiteralPath $path) {
        foreach ($key in @(Get-ChildItem -LiteralPath $path -ErrorAction Stop)) {
            if ((Get-RegistryValue $key.PSPath 'Running') -eq 1) { $key.PSChildName }
        }
    }
}
function Test-GameRunningFlag {
    param([string]$Key)
    return (@(Get-RunningSteamApps) -contains $script:Games[$Key].AppId)
}
function Close-GameAndWait {
    param([string]$Key)
    $game=$script:Games[$Key]
    $sent=@{}; $quiet=0
    while ($true) {
        $processes=@(Get-GameProcesses $Key)
        $reported=Test-GameRunningFlag $Key
        if ($processes.Count -eq 0 -and -not $reported) {
            $quiet++
            if ($quiet -ge 3) { Write-SwitchLog ($game.Name + ' has exited and Steam no longer reports it running.'); return }
        } else { $quiet=0 }
        foreach ($process in $processes) {
            if (-not $sent.ContainsKey($process.Id)) {
                try {
                    $process.Refresh()
                    if ($process.HasExited) { continue }
                    if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                        if ($process.CloseMainWindow()) {
                            $sent[$process.Id]=$true
                            Write-SwitchLog ('Requested normal close: ' + $game.Process + '.exe')
                        }
                    }
                } catch {
                    # No forced fallback. Window may have exited between snapshots.
                    Write-SwitchLog ('Normal close could not be sent yet: ' + $_.Exception.Message)
                }
            }
        }
        $text="Closing $($game.Name). Waiting until its process has exited and Steam clears Running.`r`nConfirm any exit prompt in the game. If it will not close, exit it manually."
        if ($processes.Count -eq 0 -and $reported) { $text="Waiting for Steam to finish starting/stopping $($game.Name).`r`nThe account will not change while Steam still reports this game running." }
        Pump-Wait $text
    }
}
function Wait-GamesIdle {
    $quiet=0
    while ($true) {
        $apps=@(Get-RunningSteamApps)
        $rocket=@(Get-GameProcesses 'RocketLeague').Count -gt 0
        if ($apps.Count -eq 0 -and -not $rocket) {
            $quiet++
            if ($quiet -ge 3) { return }
        } else { $quiet=0 }
        $description=($apps -join ', ')
        if ($rocket -and $apps -notcontains '252950') { $description=($description + ' RocketLeague.exe').Trim() }
        Pump-Wait ("Waiting for Steam games/apps to close: $description`r`nClose them normally. This button does not force games shut; the Rocket League toggle can close Rocket League while this waits.")
    }
}
function Stop-SteamAndWait {
    $nextRequest=[datetime]::MinValue; $quiet=0
    while ($true) {
        $clients=@(Get-ClientProcesses)
        if ($clients.Count -eq 0) {
            $quiet++
            if ($quiet -ge 4) { Write-SwitchLog 'Steam client and its web helpers have fully exited.'; return }
        } else {
            $quiet=0
            foreach ($p in @($clients | Where-Object { $_.ProcessName -eq 'steam' })) {
                try { $path=$p.Path } catch { throw 'Cannot inspect steam.exe. Run Steam and Stream Dock as the same normal Windows user, not elevated.' }
                if ([string]::IsNullOrEmpty($path) -or $path -ine $script:SteamExe) { throw 'A different or unreadable Steam executable is running. Close it normally, then retry.' }
            }
            if ([datetime]::Now -ge $nextRequest -and @(Get-RunningSteamApps).Count -eq 0 -and @(Get-GameProcesses 'RocketLeague').Count -eq 0) {
                # Graceful shutdown only; Steam itself retains responsibility for
                # game-exit and Cloud prompts. Reissue infrequently if deferred.
                if (@($clients | Where-Object { $_.ProcessName -eq 'steam' }).Count -gt 0) {
                    Start-Process -FilePath $script:SteamExe -ArgumentList '-shutdown' | Out-Null
                    Write-SwitchLog 'Requested graceful Steam shutdown.'
                    $nextRequest=[datetime]::Now.AddSeconds(20)
                }
            }
        }
        Pump-Wait "Waiting for Steam to finish closing (including its web helpers).`r`nFinish any Steam game-exit, Cloud or confirmation prompt. Account files will not be changed until Steam is completely closed."
    }
}
function Read-SharedLogTail {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    $stream=$null; $reader=$null
    try {
        $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $offset=[Math]::Max(0L,$stream.Length-1048576L)
        [void]$stream.Seek($offset,[IO.SeekOrigin]::Begin)
        $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true)
        if ($offset -gt 0) { [void]$reader.ReadLine() }
        return $reader.ReadToEnd()
    } catch { return '' } finally {
        if ($null -ne $reader) { $reader.Dispose() } elseif ($null -ne $stream) { $stream.Dispose() }
    }
}
function Wait-SteamLogin {
    param([string]$ExpectedSteamId,[string]$Label)
    $stable=0; $stableId=''; $lastPid=0; $manualPid=0
    $waiting=[Diagnostics.Stopwatch]::StartNew()
    $evidence='Unknown'; $nextLogRead=[datetime]::MinValue
    while ($true) {
        $active=Get-ActiveSteamId
        $activePid=Get-RegistryValue (Join-Path $script:SteamKey 'ActiveProcess') 'pid'
        $target=$ExpectedSteamId
        if ([string]::IsNullOrEmpty($target)) { $target=$active }
        $correct=(-not [string]::IsNullOrEmpty($target) -and $active -eq $target)
        if ($correct -and $active -eq $stableId -and $activePid -eq $lastPid) { $stable++ } else {
            $stable=0; $stableId=$active; $lastPid=$activePid; $evidence='Unknown'; $manualPid=0
            $nextLogRead=[datetime]::MinValue
        }
        if ($correct -and [datetime]::Now -ge $nextLogRead) {
            try {
                $steam=Get-Process -Id $activePid -ErrorAction Stop
                $notBefore=$steam.StartTime.AddSeconds(-1)
                $text=Read-SharedLogTail (Join-Path $script:SteamDirectory 'logs\connection_log.txt')
                $evidence=Get-ConnectionEvidence -Text $text -NotBefore $notBefore -ExpectedSteamId $target
            } catch { $evidence='Unknown' }
            $nextLogRead=[datetime]::Now.AddSeconds(2)
        }
        if ($correct -and $stable -ge 5 -and ($evidence -in @('LoggedOn','LogOnResponseOK') -or $manualPid -eq $activePid)) {
            $script:VerifyButton.Visible=$false
            Write-SwitchLog ('Expected live account stable. Sign-in evidence: ' + $evidence + '; user verification: ' + ($manualPid -eq $activePid))
            return $target
        }
        $script:VerifyButton.Visible=($correct -and $stable -ge 5 -and $waiting.Elapsed.TotalSeconds -ge 20)
        if ($script:VerificationClicked) {
            $script:VerificationClicked=$false
            if ($correct -and $stable -ge 5) {
                $reply=[Windows.Forms.MessageBox]::Show(
                    "Steam's live account ID matches $Label, but automatic log confirmation is unavailable.`r`n`r`nOpen Steam and verify its Library is loaded and the account menu is the intended account. Have you actually confirmed that sign-in is complete?`r`n`r`nDo not confirm while a password or Steam Guard prompt is pending.",
                    'Verify sign-in before continuing', 'YesNo', 'Question', 'Button2')
                if ($reply -eq 'Yes' -and (Get-ActiveSteamId) -eq $target) { $manualPid=$activePid }
            }
        }
        $status="Waiting for Steam to sign into $Label.`r`nComplete any login / Steam Guard prompt inside Steam. Rocket League will not launch merely because a timer expires."
        if ($script:VerifyButton.Visible) { $status="Steam reports the intended account active; automatic login-log confirmation is unavailable.`r`nWait, or verify the account and loaded Library in Steam, then use the verification button below." }
        Pump-Wait $status
    }
}
function Start-GameAndWait {
    param([string]$Key,[object]$Config,[string]$ExpectedSteamId='')
    $game=$script:Games[$Key]
    if (@(Get-GameProcesses $Key).Count -gt 0) { Write-SwitchLog ($game.Name + ' is already running; not launching twice.'); return }
    $usesSteam=($Key -eq 'RocketLeague' -or $Config.Overwatch.Launcher -eq 'Steam')
    if ($usesSteam) {
        if (-not [string]::IsNullOrEmpty($ExpectedSteamId) -and (Get-ActiveSteamId) -ne $ExpectedSteamId) {
            throw 'Steam changed account before launch. No game launch was requested.'
        }
        Start-Process -FilePath $script:SteamExe -ArgumentList ('-applaunch ' + $game.AppId) | Out-Null
    } else {
        $launchFile=$Config.Overwatch.Shortcut
        if (-not (Test-Path -LiteralPath $launchFile -PathType Leaf)) { throw 'The saved Overwatch launcher shortcut is missing. Run Setup again.' }
        # Launch through the Windows shell to retain the original shortcut's args.
        $startInfo=New-Object Diagnostics.ProcessStartInfo
        $startInfo.FileName=$launchFile; $startInfo.UseShellExecute=$true
        [void][Diagnostics.Process]::Start($startInfo)
    }
    Write-SwitchLog ('Launch requested: ' + $game.Name)
    $stable=0
    while ($true) {
        if ($usesSteam -and -not [string]::IsNullOrEmpty($ExpectedSteamId) -and (Get-ActiveSteamId) -ne $ExpectedSteamId) { throw 'Steam changed or lost its active account after the launch request. Check Steam; do not press a profile switch again to retry launch.' }
        if (@(Get-GameProcesses $Key).Count -gt 0) { $stable++ } else { $stable=0 }
        if ($stable -ge 3) { Write-SwitchLog ($game.Name + ' process detected. This confirms startup, not loading-screen completion.'); return }
        Pump-Wait ("Waiting for $($game.Name) to start.`r`nCheck its launcher for updates, sign-in or Play prompts. A launcher window alone is not counted as the running game.")
    }
}
function Invoke-GameToggle {
    param([string]$Key,[object]$Config)
    if (-not (Try-Lock ('Game.'+$Key))) {
        Write-SwitchLog 'A request for this game is already running. Duplicate ignored.'
        Show-Message ('Another ' + $script:Games[$Key].Name + ' helper is already running. Finish or cancel its progress window, then retry. No duplicate action was queued.')
        return
    }
    $processRunning=@(Get-GameProcesses $Key).Count -gt 0
    $reported=Test-GameRunningFlag $Key
    if ($processRunning -or $reported) {
        # Closing is deliberately allowed while a profile-only switch waits.
        New-ProgressWindow ('Closing ' + $script:Games[$Key].Name + '...')
        Close-GameAndWait -Key $Key
    } else {
        if (-not (Try-Lock 'Transition')) {
            Show-Message 'An account switch or another game launch is in progress. This launch was not queued. Let it finish or cancel it, then press the game button again.'
            return
        }
        New-ProgressWindow ('Opening ' + $script:Games[$Key].Name + '...')
        Start-GameAndWait -Key $Key -Config $Config
    }
}
function Invoke-AccountSwitch {
    param([object]$Config,[bool]$PlayRocketLeague)
    $document=Read-LoginFile
    foreach ($a in $Config.Accounts) {
        if (@($document.Accounts | Where-Object { $_.SteamId -eq $a.SteamId }).Count -ne 1) { throw 'A configured account is no longer remembered by Steam. Sign into it and run Setup again.' }
    }
    $current=Get-ActiveSteamId
    if (-not $current -and @(Get-ClientProcesses).Count -eq 0) {
        $recent=@($document.Accounts | Where-Object { $_.MostRecent })
        if ($recent.Count -eq 1) { $current=$recent[0].SteamId }
    }
    $target=Get-OppositeSteamId $current $Config.Accounts[0].SteamId $Config.Accounts[1].SteamId
    if (-not $target) {
        $answer=[Windows.Forms.MessageBox]::Show("Current account is unknown or is neither selected account.`r`n`r`nYes: FoughtApple`r`nNo: Banana`r`nCancel: do nothing",'Choose account','YesNoCancel','Question','Button3')
        if ($answer -eq 'Yes') { $target=$Config.Accounts[0].SteamId }
        elseif ($answer -eq 'No') { $target=$Config.Accounts[1].SteamId }
        else { throw [OperationCanceledException]::new('No target selected.') }
    }
    $label=@($Config.Accounts | Where-Object { $_.SteamId -eq $target })[0].Label
    New-ProgressWindow ("Preparing to switch to $label...")
    if ($PlayRocketLeague) {
        while (-not (Try-Lock 'Game.RocketLeague')) { Pump-Wait 'Another Rocket League close/launch operation is finishing. Waiting...' }
        Close-GameAndWait -Key 'RocketLeague'
    }
    Wait-GamesIdle
    Stop-SteamAndWait
    Update-ProgressWindow ("Selecting the remembered $label account...")
    Set-RememberedAccount -TargetSteamId $target
    Update-ProgressWindow ("Starting Steam for $label...")
    Start-Process -FilePath $script:SteamExe | Out-Null
    [void](Wait-SteamLogin -ExpectedSteamId $target -Label $label)
    if ($PlayRocketLeague) { Start-GameAndWait -Key 'RocketLeague' -Config $Config -ExpectedSteamId $target }
    Write-SwitchLog ('Account operation complete: ' + $label)
}
