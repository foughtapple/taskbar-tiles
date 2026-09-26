; Per-user installer. Fixed path preserves previous versions' X-Mouse commands.
#ifndef AppVersion
  #define AppVersion "0.7.0"
#endif
[Setup]
AppId=TaskbarTiles
AppName=Taskbar Tiles
AppVersion={#AppVersion}
AppVerName=Taskbar Tiles {#AppVersion}
AppPublisher=Taskbar Tiles contributors
AppPublisherURL=https://github.com/foughtapple/taskbar-tiles
AppSupportURL=https://github.com/foughtapple/taskbar-tiles/issues
AppUpdatesURL=https://github.com/foughtapple/taskbar-tiles/releases/latest
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=Taskbar Tiles Setup
DefaultDirName={localappdata}\TaskbarTiles
DefaultGroupName=Taskbar Tiles
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
PrivilegesRequired=lowest
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\assets\TaskbarTiles.ico
UninstallDisplayIcon={app}\TaskbarTiles.exe
UninstallDisplayName=Taskbar Tiles
LicenseFile=..\LICENSE
InfoBeforeFile=INSTALL-NOTES.txt
OutputDir=..\dist
OutputBaseFilename=TaskbarTiles-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
DisableWelcomePage=no
ChangesAssociations=no
[Tasks]
Name: "startup"; Description: "Start Taskbar Tiles when I sign in"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "streamdock"; Description: "Install and enable the bundled Stream Dock integration"; GroupDescription: "Optional integrations:"; Check: IsFreshInstall
Name: "touchreturn"; Description: "Enable Touch Return (mouse/focus back to the previous screen after touchscreen use)"; GroupDescription: "Optional integrations:"; Flags: unchecked; Check: IsFreshInstall
[Files]
Source: "..\build\app\TaskbarTiles.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\app\TaskbarTiles.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\config\settings.ini"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\XMOUSE-SETUP.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\app\streamdock\*"; DestDir: "{app}\streamdock"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Taskbar Tiles"; Filename: "{app}\TaskbarTiles.exe"; Parameters: "--show"; WorkingDir: "{app}"
Name: "{group}\Uninstall Taskbar Tiles"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Taskbar Tiles"; Filename: "{app}\TaskbarTiles.exe"; Parameters: "--show"; Tasks: desktopicon
Name: "{userstartup}\Taskbar Tiles"; Filename: "{app}\TaskbarTiles.exe"; WorkingDir: "{app}"; Tasks: startup
[Run]
Filename: "{app}\TaskbarTiles.exe"; Description: "Start Taskbar Tiles"; Flags: nowait postinstall skipifsilent
[Code]
var
  BackupDone: Boolean;
  ExistingInstall: Boolean;
  ExistingPreferences: Boolean;

function IsFreshInstall(): Boolean;
begin
  Result := (not ExistingInstall) and (not ExistingPreferences);
end;
function StopResident(const Folder: String): Boolean;
var
  Code, I: Integer;
begin
  Result := True;
  if not CheckForMutexes('Local\TaskbarTiles.Instance.v01') then exit;
  if FileExists(Folder + '\TaskbarTiles.exe') then
    Exec(Folder + '\TaskbarTiles.exe', '--exit', Folder, SW_HIDE, ewWaitUntilTerminated, Code);
  for I := 1 to 60 do begin
    if not CheckForMutexes('Local\TaskbarTiles.Instance.v01') then exit;
    Sleep(100);
  end;
  Result := False;
end;
function InitializeSetup(): Boolean;
begin
  ExistingInstall := FileExists(ExpandConstant('{localappdata}\TaskbarTiles\TaskbarTiles.exe'));
  ExistingPreferences := FileExists(ExpandConstant('{localappdata}\TaskbarTiles\settings.ini')) or
    FileExists(ExpandConstant('{localappdata}\TaskbarTiles\StreamDockData\state.json'));
  Result := IsDotNetInstalled(net48, 0);
  if not Result then MsgBox('Taskbar Tiles needs Microsoft .NET Framework 4.8 or later. Install it from Microsoft, then run Setup again.', mbError, MB_OK);
end;
procedure InitializeWizard();
begin
  if ExistingInstall then begin
    WizardSelectTasks('');
    if FileExists(ExpandConstant('{userstartup}\Taskbar Tiles.lnk')) then WizardSelectTasks('startup');
    if FileExists(ExpandConstant('{userdesktop}\Taskbar Tiles.lnk')) then WizardSelectTasks('desktopicon');
  end;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Folder, Backup, Name: String;
  I, Code: Integer;
  Names: TArrayOfString;
begin
  Result := '';
  Folder := ExpandConstant('{app}');
  if not StopResident(Folder) then begin
    Result := 'Taskbar Tiles is still running. Exit it from its tray icon, then choose Next. No other apps have been closed.';
    exit;
  end;
  if FileExists(Folder + '\StreamDockData\state.json') and FileExists(Folder + '\TaskbarTiles.exe') then begin
    Code := 0;
    if not Exec(Folder + '\TaskbarTiles.exe', '--streamdock-ready', Folder, SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
      Result := 'Stream Dock plugin updates are enabled. Fully exit Stream Dock from its tray icon, then choose Next. Plugins and settings have not been replaced.';
      exit;
    end;
  end;
  if BackupDone or not FileExists(Folder + '\TaskbarTiles.exe') then exit;
  Backup := Folder + '\Backups\' + GetDateTimeString('yyyymmdd-hhnnss', '-', ':');
  if not ForceDirectories(Backup) then begin Result := 'Could not create a backup of the old installation.'; exit; end;
  SetArrayLength(Names, 4);
  Names[0] := 'TaskbarTiles.exe'; Names[1] := 'TaskbarTiles.exe.config';
  Names[2] := 'settings.ini'; Names[3] := 'favourites.json';
  for I := 0 to GetArrayLength(Names) - 1 do begin
    Name := Names[I];
    if FileExists(Folder + '\' + Name) and not FileCopy(Folder + '\' + Name, Backup + '\' + Name, False) then begin
      Result := 'Could not back up ' + Name + '. Nothing has been installed yet.';
      exit;
    end;
  end;
  BackupDone := True;
end;
procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then begin
    if not WizardIsTaskSelected('startup') then DeleteFile(ExpandConstant('{userstartup}\Taskbar Tiles.lnk'));
    if not WizardIsTaskSelected('desktopicon') then DeleteFile(ExpandConstant('{userdesktop}\Taskbar Tiles.lnk'));
    { Fresh installs expose explicit integration choices. Upgrades preserve the
      existing state/configuration and never re-enable an option the user disabled. }
    if not ExistingInstall then begin
      if WizardIsTaskSelected('streamdock') then begin
        Code := 0;
        if not Exec(ExpandConstant('{app}\TaskbarTiles.exe'), '--init-streamdock-defaults', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
          Log('Fresh-install Stream Dock integration is selected but remains pending. See StreamDockData\last-result.txt.');
          if not WizardSilent then MsgBox('Taskbar Tiles saved the Stream Dock integration choice, but it could not finish installing the plugin. Fully exit Stream Dock, then open Taskbar Tiles Settings > Stream Dock and choose Apply.', mbInformation, MB_OK);
        end;
      end;
      if WizardIsTaskSelected('touchreturn') then begin
        Code := 0;
        if not Exec(ExpandConstant('{app}\TaskbarTiles.exe'), '--installer-enable-touch', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
          Log('Could not enable the Touch Return master switch during Setup.');
          if not WizardSilent then MsgBox('Touch Return remains off. You can enable it later in Taskbar Tiles Settings > Touch screen monitor support.', mbInformation, MB_OK);
        end;
      end;
    end;
    { Existing installs/reinstalls reconcile only their saved Stream Dock
      choices. A truly fresh selected install was already handled above. }
    if ExistingInstall or ExistingPreferences then begin
      Code := 0;
      if not Exec(ExpandConstant('{app}\TaskbarTiles.exe'), '--sync-streamdock', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
        Log('Taskbar Tiles installed; Stream Dock changes remain pending. See StreamDockData\last-result.txt.');
        if not WizardSilent then MsgBox('Taskbar Tiles was installed. Some Stream Dock updates remain pending. Fully exit Stream Dock, open Taskbar Tiles Settings > Stream Dock and choose Apply. Existing settings were retained.', mbInformation, MB_OK);
      end;
    end;
    { Supersede only our own optional local-build registration, never other apps. }
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\TaskbarTiles-LocalBuild');
  end;
end;
function InitializeUninstall(): Boolean;
begin
  Result := StopResident(ExpandConstant('{app}'));
  if not Result then MsgBox('Exit Taskbar Tiles from its tray icon, then try uninstalling again.', mbError, MB_OK);
end;
