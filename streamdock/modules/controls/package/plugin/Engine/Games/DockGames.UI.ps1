
function Write-SwitchLog {
    param([string]$Text)
    try { [IO.File]::AppendAllText($script:LogPath, ((Get-Date).ToString('o') + ' ' + $Text + [Environment]::NewLine), [Text.UTF8Encoding]::new($false)) } catch { }
}
function Show-Message {
    param([string]$Text, [string]$Title='Stream Dock Games', [string]$Icon='Information')
    [void][Windows.Forms.MessageBox]::Show($Text, $Title, 'OK', $Icon)
}
function New-ProgressWindow {
    param([string]$Text)
    if ($null -ne $script:Form) { return }
    $script:Form = New-Object Windows.Forms.Form
    $script:Form.Text = 'Stream Dock Games'
    $script:Form.ClientSize = New-Object Drawing.Size(620, 228)
    $script:Form.StartPosition = 'CenterScreen'
    $script:Form.FormBorderStyle = 'FixedDialog'
    $script:Form.MaximizeBox = $false
    $script:Form.Font = New-Object Drawing.Font('Segoe UI',10)
    $script:Form.AutoScaleMode = 'Dpi'
    $script:ProgressLabel = New-Object Windows.Forms.Label
    $script:ProgressLabel.Location = New-Object Drawing.Point(18,18)
    $script:ProgressLabel.Size = New-Object Drawing.Size(584,94)
    $script:Form.Controls.Add($script:ProgressLabel)
    $script:ElapsedLabel = New-Object Windows.Forms.Label
    $script:ElapsedLabel.Location = New-Object Drawing.Point(18,119)
    $script:ElapsedLabel.Size = New-Object Drawing.Size(580,43)
    $script:Form.Controls.Add($script:ElapsedLabel)
    $script:VerifyButton = New-Object Windows.Forms.Button
    $script:VerifyButton.Text = "I've checked Steam sign-in..."
    $script:VerifyButton.Location = New-Object Drawing.Point(18,179)
    $script:VerifyButton.Size = New-Object Drawing.Size(270,32)
    $script:VerifyButton.Visible = $false
    $script:VerifyButton.Add_Click({ $script:VerificationClicked = $true })
    $script:Form.Controls.Add($script:VerifyButton)
    $cancel = New-Object Windows.Forms.Button
    $cancel.Text = 'Cancel'
    $cancel.Location = New-Object Drawing.Point(501,179)
    $cancel.Size = New-Object Drawing.Size(100,32)
    $cancel.Add_Click({ $script:Cancelled = $true })
    $script:Form.Controls.Add($cancel)
    $script:Form.Add_FormClosing({ param($sender,$e)
        if (-not $script:ClosingForm) { $script:Cancelled = $true; $e.Cancel = $true }
    })
    $script:Form.Show()
    Update-ProgressWindow $Text
}
function Update-ProgressWindow {
    param([string]$Text)
    if ($script:Cancelled) { throw [OperationCanceledException]::new('Cancelled by user.') }
    if ($Text -ne $script:LastStatus) { Write-SwitchLog $Text.Replace("`r`n",' | '); $script:LastStatus = $Text }
    if ($null -ne $script:Form) {
        $script:ProgressLabel.Text = $Text
        $script:ElapsedLabel.Text = ('Elapsed: {0:mm\:ss}. Waiting is cancellable; no processes are force-killed.' -f $script:Stopwatch.Elapsed)
        [Windows.Forms.Application]::DoEvents()
    }
    if ($script:Cancelled) { throw [OperationCanceledException]::new('Cancelled by user.') }
}
function Pump-Wait {
    param([string]$Text)
    Update-ProgressWindow $Text
    # Polling interval, never used as a substitute for completion detection.
    Start-Sleep -Milliseconds 500
}
function Close-ProgressWindow {
    if ($null -ne $script:Form) {
        $script:ClosingForm = $true
        $script:Form.Close()
        $script:Form.Dispose()
        $script:Form = $null
    }
}
function New-SetupLabel {
    param([object]$Form,[string]$Text,[int]$X,[int]$Y,[int]$Width=715,[int]$Height=28)
    $label = New-Object Windows.Forms.Label
    $label.Text=$Text; $label.Location=New-Object Drawing.Point($X,$Y); $label.Size=New-Object Drawing.Size($Width,$Height)
    $Form.Controls.Add($label)
}
function Show-Setup {
    Set-SteamLocation -Path (Find-SteamExe)
    $document = Read-LoginFile
    if ($document.Accounts.Count -lt 2) { throw 'Sign into both Steam accounts once and let Steam remember each sign-in. Then run Setup again.' }
    $prior=$null
    if (Test-Path -LiteralPath $script:ConfigPath) {
        try { $prior = Get-Content -LiteralPath $script:ConfigPath -Encoding UTF8 -Raw | ConvertFrom-Json } catch { }
    } else {
        $old = Join-Path $env:LOCALAPPDATA 'FoughtApple\StreamDock-SteamRL\accounts.json'
        if (Test-Path -LiteralPath $old) { try { $prior = Get-Content -LiteralPath $old -Encoding UTF8 -Raw | ConvertFrom-Json } catch { } }
    }
    $form=New-Object Windows.Forms.Form
    $form.Text='Stream Dock Games - Setup'; $form.ClientSize=New-Object Drawing.Size(760,430)
    $form.StartPosition='CenterScreen'; $form.FormBorderStyle='FixedDialog'; $form.MaximizeBox=$false
    $form.Font=New-Object Drawing.Font('Segoe UI',10); $form.AutoScaleMode='Dpi'
    New-SetupLabel $form 'Select the two accounts already remembered by Steam. No passwords are requested.' 18 15 720 45
    New-SetupLabel $form 'FoughtApple' 18 69 128
    New-SetupLabel $form 'Banana' 18 116 128
    $first=New-Object Windows.Forms.ComboBox; $second=New-Object Windows.Forms.ComboBox
    $first.Location=New-Object Drawing.Point(155,65); $second.Location=New-Object Drawing.Point(155,112)
    foreach ($combo in @($first,$second)) {
        $combo.Size=New-Object Drawing.Size(582,32); $combo.DropDownStyle='DropDownList'; $combo.DropDownWidth=760
        foreach ($account in $document.Accounts) { [void]$combo.Items.Add(($account.PersonaName + '  |  ' + $account.AccountName + '  |  ' + $account.SteamId)) }
        $form.Controls.Add($combo)
    }
    if ($null -ne $prior -and $prior.PSObject.Properties['Accounts'] -and @($prior.Accounts).Count -eq 2) {
        for ($i=0; $i -lt $document.Accounts.Count; $i++) {
            if ($document.Accounts[$i].SteamId -eq $prior.Accounts[0].SteamId) { $first.SelectedIndex=$i }
            if ($document.Accounts[$i].SteamId -eq $prior.Accounts[1].SteamId) { $second.SelectedIndex=$i }
        }
    }
    New-SetupLabel $form 'Overwatch opens through:' 18 171 240
    $launcher=New-Object Windows.Forms.ComboBox
    $launcher.Location=New-Object Drawing.Point(260,167); $launcher.Size=New-Object Drawing.Size(477,32)
    $launcher.DropDownStyle='DropDownList'
    [void]$launcher.Items.Add('Steam')
    [void]$launcher.Items.Add('Existing working game shortcut (e.g. Battle.net)')
    # Require an explicit selection, never silently pick the wrong launcher.
    $form.Controls.Add($launcher)
    New-SetupLabel $form 'Existing Overwatch shortcut (.lnk or .url), only for the second option:' 18 213 720
    $shortcut=New-Object Windows.Forms.TextBox
    $shortcut.Location=New-Object Drawing.Point(18,244); $shortcut.Size=New-Object Drawing.Size(610,28); $shortcut.ReadOnly=$true
    $form.Controls.Add($shortcut)
    $browse=New-Object Windows.Forms.Button; $browse.Text='Browse...'; $browse.Location=New-Object Drawing.Point(638,242); $browse.Size=New-Object Drawing.Size(99,31)
    $browse.Add_Click({
        $picker=New-Object Windows.Forms.OpenFileDialog
        $picker.Title='Choose the existing shortcut that actually launches Overwatch'
        $picker.Filter='Game shortcuts (*.lnk;*.url)|*.lnk;*.url'
        $picker.DereferenceLinks=$false
        try { if ($picker.ShowDialog() -eq 'OK') { $shortcut.Text=$picker.FileName } } finally { $picker.Dispose() }
    })
    $form.Controls.Add($browse)
    $launcher.Add_SelectedIndexChanged({
        $shortcut.Enabled=($launcher.SelectedIndex -eq 1); $browse.Enabled=($launcher.SelectedIndex -eq 1)
    })
    $shortcut.Enabled=$false; $browse.Enabled=$false
    if ($null -ne $prior -and $prior.PSObject.Properties['Overwatch']) {
        if ($prior.Overwatch.Launcher -eq 'Steam') { $launcher.SelectedIndex=0 }
        elseif ($prior.Overwatch.Launcher -eq 'Shortcut') { $launcher.SelectedIndex=1; $shortcut.Text=$prior.Overwatch.Shortcut }
    }
    New-SetupLabel $form 'Rocket League uses Steam. Both selected accounts must already be able to launch it.' 18 289 720 46
    New-SetupLabel $form 'Setup only saves these choices. It does not close games or switch Steam.' 18 335 720 27
    $save=New-Object Windows.Forms.Button; $save.Text='Save setup'; $save.Location=New-Object Drawing.Point(613,380); $save.Size=New-Object Drawing.Size(124,32)
    $save.Add_Click({
        if ($first.SelectedIndex -lt 0 -or $second.SelectedIndex -lt 0 -or $first.SelectedIndex -eq $second.SelectedIndex) {
            Show-Message 'Select two different Steam accounts.'; return
        }
        if ($launcher.SelectedIndex -lt 0) { Show-Message 'Choose how your installed copy of Overwatch opens.'; return }
        if ($launcher.SelectedIndex -eq 1) {
            if (-not (Test-Path -LiteralPath $shortcut.Text -PathType Leaf) -or [IO.Path]::GetExtension($shortcut.Text) -notin @('.lnk','.url')) {
                Show-Message 'Browse to an existing .lnk or .url shortcut that launches Overwatch.'; return
            }
        }
        $form.DialogResult='OK'; $form.Close()
    })
    $form.Controls.Add($save)
    try {
        if ($form.ShowDialog() -ne 'OK') { throw [OperationCanceledException]::new('Setup cancelled.') }
        $owLauncher='Steam'; $owShortcut=''
        if ($launcher.SelectedIndex -eq 1) {
            $owLauncher='Shortcut'
            $owShortcut=Join-Path $script:DataPath ('Overwatch-launch' + [IO.Path]::GetExtension($shortcut.Text))
            if ([IO.Path]::GetFullPath($shortcut.Text) -ine [IO.Path]::GetFullPath($owShortcut)) { Copy-Item -LiteralPath $shortcut.Text -Destination $owShortcut -Force }
        }
        $settings=[pscustomobject]@{
            Version=2; SteamExe=$script:SteamExe
            Accounts=@(
                [pscustomobject]@{ Label='FoughtApple'; SteamId=$document.Accounts[$first.SelectedIndex].SteamId },
                [pscustomobject]@{ Label='Banana'; SteamId=$document.Accounts[$second.SelectedIndex].SteamId }
            )
            Overwatch=[pscustomobject]@{ Launcher=$owLauncher; Shortcut=$owShortcut }
        }
        Save-Json -Value $settings -Path $script:ConfigPath
    } finally { $form.Dispose() }
}
function Get-Configuration {
    param([string]$RequestedMode)
    $config=$null
    if (Test-Path -LiteralPath $script:ConfigPath -PathType Leaf) {
        try { $config=Get-Content -LiteralPath $script:ConfigPath -Encoding UTF8 -Raw | ConvertFrom-Json }
        catch { throw ('Cannot read copied game settings. Open Helpers > Game setup. File: ' + $script:ConfigPath) }
    }
    if ($null -eq $config) {
        $config=[pscustomobject]@{Version=2;SteamExe='';Accounts=@();Overwatch=[pscustomobject]@{Launcher='';Shortcut=''}}
    }
    if (-not $config.PSObject.Properties['SteamExe']) { $config | Add-Member NoteProperty SteamExe '' }
    if (-not $config.PSObject.Properties['Accounts']) { $config | Add-Member -MemberType NoteProperty -Name Accounts -Value @() }
    if (-not $config.PSObject.Properties['Overwatch']) { $config | Add-Member NoteProperty Overwatch ([pscustomobject]@{Launcher='';Shortcut=''}) }
    if ($null -eq $config.Overwatch) { $config.Overwatch=[pscustomobject]@{Launcher='';Shortcut=''} }
    if (-not $config.Overwatch.PSObject.Properties['Launcher']) { $config.Overwatch | Add-Member NoteProperty Launcher '' }
    if (-not $config.Overwatch.PSObject.Properties['Shortcut']) { $config.Overwatch | Add-Member NoteProperty Shortcut '' }
    if (-not $config.SteamExe -or -not (Test-Path -LiteralPath ([string]$config.SteamExe) -PathType Leaf)) { $config.SteamExe=Find-SteamExe }
    Set-SteamLocation -Path $config.SteamExe
    if ($RequestedMode -in @('Switch','SwitchPlay')) {
        $accounts=@($config.Accounts)
        $valid=($accounts.Count -eq 2)
        if ($valid) {
            foreach ($account in $accounts) {
                if (-not $account.PSObject.Properties['SteamId'] -or [string]$account.SteamId -notmatch '^\d{17}$') { $valid=$false }
                if (-not $account.PSObject.Properties['Label']) { $valid=$false }
            }
            if ($valid -and $accounts[0].SteamId -eq $accounts[1].SteamId) { $valid=$false }
        }
        if (-not $valid) {
            Show-Message 'Choose the two remembered accounts in setup. This requested switch continues only after you save setup.'
            Show-Setup
            $config=Get-Content -LiteralPath $script:ConfigPath -Encoding UTF8 -Raw | ConvertFrom-Json
            Set-SteamLocation -Path $config.SteamExe
        }
    }
    # A running game can be closed without configuring its launcher or the other game.
    if ($RequestedMode -eq 'OverwatchToggle' -and @(Get-GameProcesses 'Overwatch').Count -eq 0 -and -not (Test-GameRunningFlag 'Overwatch')) {
        if ($config.Overwatch.Launcher -notin @('Steam','Shortcut')) {
            $answer=[Windows.Forms.MessageBox]::Show(
                "How do you normally open Overwatch?`r`n`r`nYes = Steam`r`nNo = select an existing Overwatch game shortcut (for example Battle.net)`r`nCancel = do nothing",
                'Choose Overwatch launcher', 'YesNoCancel', 'Question', 'Button3')
            if ($answer -eq 'Cancel') { throw [OperationCanceledException]::new('Overwatch launch setup cancelled.') }
            $ow=[pscustomobject]@{Launcher='Steam';Shortcut=''}
            if ($answer -eq 'No') {
                $picker=New-Object Windows.Forms.OpenFileDialog
                $picker.Title='Select the working shortcut that launches Overwatch'
                $picker.Filter='Game shortcuts (*.lnk;*.url)|*.lnk;*.url'
                $picker.DereferenceLinks=$false
                try {
                    if ($picker.ShowDialog() -ne 'OK') { throw [OperationCanceledException]::new('No Overwatch shortcut selected.') }
                    $to=Join-Path $script:DataPath ('Overwatch-launch' + [IO.Path]::GetExtension($picker.FileName))
                    if ([IO.Path]::GetFullPath($picker.FileName) -ine [IO.Path]::GetFullPath($to)) { Copy-Item -LiteralPath $picker.FileName -Destination $to -Force }
                    $ow.Launcher='Shortcut'; $ow.Shortcut=$to
                } finally { $picker.Dispose() }
            }
            $config.Overwatch=$ow
            Save-Json -Value $config -Path $script:ConfigPath
        }
    }
    return $config
}

function Show-Status {
    param([object]$Config)
    $id=Get-ActiveSteamId
    $label='No live Steam account identified'
    if ($id) {
        $matches=@($Config.Accounts | Where-Object { $_.SteamId -eq $id })
        if ($matches.Count -eq 1) { $label=$matches[0].Label } else { $label='Another Steam account' }
    }
    $rl=@(Get-GameProcesses 'RocketLeague').Count -gt 0
    $ow=@(Get-GameProcesses 'Overwatch').Count -gt 0
    $apps=@(Get-RunningSteamApps)
    Show-Message ("Steam active account: $label`r`nRocket League process running: $rl`r`nOverwatch process running: $ow`r`nSteam reports running: $($apps -join ', ')`r`nOverwatch launcher: $($Config.Overwatch.Launcher)`r`n`r`nSettings and logs:`r`n$script:DataPath`r`n`r`nThis is local process/client state, not a server-side account or ownership check.") 'Stream Dock Games - Status'
}
