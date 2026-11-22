; FlowDesk per-user installer. Compile with tools/Build-Installer.ps1 and Inno Setup 6.
; AppVersion comes from the published executable: the csproj remains the version source of truth.
; This AppId MUST stay the same for all future FlowDesk versions (including 1.0.1 upgrades).
#ifndef SourceDir
  #error Supply /DSourceDir with a freshly published win-x64 folder.
#endif
#ifndef OutputDirectory
  #error Supply /DOutputDirectory with a new installer output folder.
#endif
#define AppExe AddBackslash(SourceDir) + "FlowDesk.App.exe"
#if !FileExists(AppExe)
  #error SourceDir does not contain FlowDesk.App.exe. Run tools/Publish.ps1 first.
#endif
#define AppVersion GetStringFileInfo(AppExe, "ProductVersion")
#define FileVersion GetVersionNumbersString(AppExe)

[Setup]
AppId={{75AB15E2-9F93-46B8-9B30-645DAAD7E512}
AppName=FlowDesk
AppVersion={#AppVersion}
AppVerName=FlowDesk {#AppVersion}
AppPublisher=FlowDesk
VersionInfoVersion={#FileVersion}
VersionInfoCompany=FlowDesk
VersionInfoProductName=FlowDesk
DefaultDirName={localappdata}\Programs\FlowDesk
DefaultGroupName=FlowDesk
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\FlowDesk.App.exe
SetupIconFile=..\assets\FlowDesk.ico
LicenseFile=..\LICENSE
OutputDir={#OutputDirectory}
OutputBaseFilename=FlowDesk-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
; Restart Manager detects the running executable/DLLs in [Files] and closes them before replacement.
; No startup task or HKCU Run value is created here; that option belongs to Settings in the app.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\FlowDesk"; Filename: "{app}\FlowDesk.App.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\FlowDesk"; Filename: "{app}\FlowDesk.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\FlowDesk.App.exe"; Description: "Launch FlowDesk"; Flags: nowait postinstall skipifsilent
Filename: "{app}\FlowDesk.App.exe"; Flags: nowait skipifnotsilent; Check: RelaunchRequested

[Code]
function RelaunchRequested: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RELAUNCH') = 0 then Result := True;
end;

{ FlowDesk uses Windows App SDK AppInstance, not a named Win32 mutex. Do not invent AppMutex.
  The in-app handoff supplies its PID; wait for normal persistence/cleanup before replacing files.
  Restart Manager (CloseApplications=yes) still handles ordinary interactive upgrades. }
function OpenProcess(Access: LongWord; InheritHandle: Boolean; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function GetLastError: LongWord;
  external 'GetLastError@kernel32.dll stdcall';

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  I, Pid: Integer;
  Value: String;
  Parent: THandle;
  WaitResult: LongWord;
begin
  Result := '';
  if not RelaunchRequested then Exit;
  for I := 1 to ParamCount do
  begin
    Value := ParamStr(I);
    if CompareText(Copy(Value, 1, 13), '/FLOWDESKPID=') = 0 then
    begin
      Pid := StrToIntDef(Copy(Value, 14, MaxInt), 0);
      if Pid <= 0 then
      begin
        Result := 'Invalid FlowDesk process id.';
        Exit;
      end;
      Parent := OpenProcess($00100000, False, Pid); { SYNCHRONIZE only }
      if Parent <> 0 then
      begin
        WaitResult := WaitForSingleObject(Parent, 60000);
        CloseHandle(Parent);
        if WaitResult <> 0 then
          Result := 'FlowDesk is still exiting. Please close FlowDesk and try again.';
      end
      else if GetLastError <> 87 then { ERROR_INVALID_PARAMETER means the PID already exited }
        Result := 'FlowDesk could not be checked. Please close FlowDesk and try again.';
      Exit;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Remove only an app-created startup entry pointing to THIS installation. Never remove a
      portable/different installation's entry. Workspaces/settings/logs are intentionally retained. }
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'FlowDesk', Command) then
      if CompareText(Trim(Command), '"' + ExpandConstant('{app}\FlowDesk.App.exe') + '"') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'FlowDesk');
  end;
end;
