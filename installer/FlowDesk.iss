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
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "VirtualDesktopAccessor.dll,*\VirtualDesktopAccessor.dll,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\FlowDesk"; Filename: "{app}\FlowDesk.App.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\FlowDesk"; Filename: "{app}\FlowDesk.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\FlowDesk.App.exe"; Description: "Launch FlowDesk"; Flags: nowait postinstall skipifsilent

[Code]
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
