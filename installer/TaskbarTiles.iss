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
[Files]
Source: "..\build\app\TaskbarTiles.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\app\TaskbarTiles.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\config\settings.ini"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\XMOUSE-SETUP.txt"; DestDir: "{app}"; Flags: ignoreversion
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
  Existing: Boolean;
begin
  Existing := FileExists(ExpandConstant('{localappdata}\TaskbarTiles\TaskbarTiles.exe'));
  if Existing then begin
    WizardSelectTasks('');
    if FileExists(ExpandConstant('{userstartup}\Taskbar Tiles.lnk')) then WizardSelectTasks('startup');
    if FileExists(ExpandConstant('{userdesktop}\Taskbar Tiles.lnk')) then WizardSelectTasks('desktopicon');
  end;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Folder, Backup, Name: String;
  I: Integer;
  Names: TArrayOfString;
begin
  Result := '';
  Folder := ExpandConstant('{app}');
  if not StopResident(Folder) then begin
    Result := 'Taskbar Tiles is still running. Exit it from its tray icon, then choose Next. No other apps have been closed.';
    exit;
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
begin
  if CurStep = ssPostInstall then begin
    if not WizardIsTaskSelected('startup') then DeleteFile(ExpandConstant('{userstartup}\Taskbar Tiles.lnk'));
    if not WizardIsTaskSelected('desktopicon') then DeleteFile(ExpandConstant('{userdesktop}\Taskbar Tiles.lnk'));
    { Supersede only our own optional local-build registration, never other apps. }
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\TaskbarTiles-LocalBuild');
  end;
end;
function InitializeUninstall(): Boolean;
begin
  Result := StopResident(ExpandConstant('{app}'));
  if not Result then MsgBox('Exit Taskbar Tiles from its tray icon, then try uninstalling again.', mbError, MB_OK);
end;
