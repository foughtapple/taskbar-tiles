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
Name: "streamdock"; Description: "Install and manage Taskbar Tiles Stream Dock modules"; GroupDescription: "Optional integrations:"; Flags: checkedonce
Name: "touchreturn"; Description: "Enable Touch Return (mouse/focus back to the previous screen after touchscreen use)"; GroupDescription: "Optional integrations:"; Flags: unchecked
Name: "startup"; Description: "Start Taskbar Tiles when I sign in"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
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
  Result := IsDotNetInstalled(net48, 0);
  if not Result then MsgBox('Taskbar Tiles needs Microsoft .NET Framework 4.8 or later. Install it from Microsoft, then run Setup again.', mbError, MB_OK);
end;
procedure InitializeWizard();
var
  SelectedTasks: String;
begin
  ExistingInstall := FileExists(ExpandConstant('{localappdata}\TaskbarTiles\TaskbarTiles.exe'));
  if ExistingInstall then begin
    { Stream Dock is presented ON by default in Setup, including upgrades. The
      first-install seeding command still refuses to replace an existing state file. }
    SelectedTasks := 'streamdock';
    if FileExists(ExpandConstant('{userstartup}\Taskbar Tiles.lnk')) then
      SelectedTasks := SelectedTasks + ',startup';
    if FileExists(ExpandConstant('{userdesktop}\Taskbar Tiles.lnk')) then
      SelectedTasks := SelectedTasks + ',desktopicon';
    WizardSelectTasks(SelectedTasks);
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

    { First-install choices only. Upgrades preserve the user's existing Stream Dock
      and Touch Return configuration regardless of these new Setup task defaults. }
    if not ExistingInstall then begin
      if WizardIsTaskSelected('streamdock') then begin
        Code := 0;
        if not Exec(ExpandConstant('{app}\TaskbarTiles.exe'), '--seed-streamdock-defaults', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
          Log('Could not seed first-install Stream Dock defaults; Settings can apply them later.');
      end;
      if WizardIsTaskSelected('touchreturn') then begin
        Code := 0;
        if not Exec(ExpandConstant('{app}\TaskbarTiles.exe'), '--enable-touch-return-default', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
          Log('Could not enable the first-install Touch Return master switch.');
      end;
    end;

    { Apply saved/seeded Stream Dock choices. If Stream Dock is running, keep the
      pending state and let Settings or the next app start retry safely. }
    Code := 0;
    if not Exec(ExpandConstant('{app}\TaskbarTiles.exe'), '--sync-streamdock', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
      Log('Taskbar Tiles installed; Stream Dock changes remain pending. See StreamDockData\last-result.txt.');
      if not WizardSilent then MsgBox('Taskbar Tiles was updated. Some Stream Dock updates remain pending. Close Stream Dock, open Taskbar Tiles Settings > Stream Dock and choose Apply. Existing settings were retained.', mbInformation, MB_OK);
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
