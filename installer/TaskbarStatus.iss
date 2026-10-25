; TaskbarStatus installer (Inno Setup 6). Build with build-installer.bat.
; Installs the PawnIO driver first (required for CPU temperature) and registers a scheduled task
; that starts the app elevated without a UAC prompt.

#define AppExe "..\TaskbarStatus.exe"
#define AppVersion RemoveFileExt(GetVersionNumbersString(AddBackslash(SourcePath) + AppExe))
#define Repo "https://github.com/batuhanbecel/TaskbarStatus"

[Setup]
AppId={{8F3C2A51-7B1E-4D6A-9C3E-2B5F0A7D4E91}
AppName=TaskbarStatus
AppVersion={#AppVersion}
AppVerName=TaskbarStatus {#AppVersion}
AppPublisher=Batuhan Becel
AppPublisherURL={#Repo}
AppSupportURL={#Repo}/issues
AppUpdatesURL={#Repo}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\TaskbarStatus
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=TaskbarStatus-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\TaskbarStatus.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
en.Autostart=Start TaskbarStatus when I sign in
tr.Autostart=Oturum açınca TaskbarStatus'u başlat
en.Launch=Start TaskbarStatus
tr.Launch=TaskbarStatus'u başlat
en.InstallingPawnIO=Installing the PawnIO driver (needed for CPU temperature)...
tr.InstallingPawnIO=PawnIO sürücüsü kuruluyor (CPU sıcaklığı için gerekli)...
en.PawnIOFailed=The PawnIO driver could not be installed (code %1). TaskbarStatus needs it to read the CPU temperature.
tr.PawnIOFailed=PawnIO sürücüsü kurulamadı (kod %1). TaskbarStatus CPU sıcaklığını okumak için buna ihtiyaç duyar.

[Tasks]
Name: "autostart"; Description: "{cm:Autostart}"

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"
Source: "..\vendor\modules\COPYING"; DestDir: "{app}"; DestName: "PawnIO-Modules-LICENSE.txt"
Source: "..\vendor\PawnIO_setup.exe"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\TaskbarStatus"; Filename: "{app}\TaskbarStatus.exe"

[Registry]
Root: HKCU; Subkey: "Software\TaskbarStatus"; Flags: uninsdeletekey dontcreatekey
; The app starts in the language picked for the installer (an existing choice is kept).
Root: HKCU; Subkey: "Software\TaskbarStatus"; ValueType: dword; ValueName: "Language"; ValueData: 1; Languages: tr; Flags: createvalueifdoesntexist
Root: HKCU; Subkey: "Software\TaskbarStatus"; ValueType: dword; ValueName: "Language"; ValueData: 2; Languages: en; Flags: createvalueifdoesntexist
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "TaskbarStatus"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\TaskbarStatus.exe"; Parameters: "--register-task on"; Flags: runhidden waituntilterminated; Tasks: autostart
Filename: "{app}\TaskbarStatus.exe"; Parameters: "--register-task off"; Flags: runhidden waituntilterminated; Tasks: not autostart
Filename: "{app}\TaskbarStatus.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Background updates run the setup with /VERYSILENT /RESTARTAPP=1: start the app again afterwards.
Filename: "{app}\TaskbarStatus.exe"; Flags: nowait runasoriginaluser; Check: RestartAfterSilentUpdate

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM TaskbarStatus.exe /F"; Flags: runhidden waituntilterminated; RunOnceId: "StopApp"
Filename: "{app}\TaskbarStatus.exe"; Parameters: "--unregister-task"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveTask"

[Code]
function PawnIOInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

function RestartAfterSilentUpdate: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RESTARTAPP|0}') = '1');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  { Close a running copy so it can be replaced (updates). }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM TaskbarStatus.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);

  if not PawnIOInstalled then
  begin
    WizardForm.PreparingLabel.Caption := CustomMessage('InstallingPawnIO');
    WizardForm.PreparingLabel.Visible := True;
    ExtractTemporaryFile('PawnIO_setup.exe');
    if not Exec(ExpandConstant('{tmp}\PawnIO_setup.exe'), '-install -silent', '', SW_SHOW, ewWaitUntilTerminated, Code) then
      Result := FmtMessage(CustomMessage('PawnIOFailed'), [SysErrorMessage(Code)])
    else if Code = 3010 then
      NeedsRestart := True
    else if Code <> 0 then
      Result := FmtMessage(CustomMessage('PawnIOFailed'), [IntToStr(Code)]);
  end;
end;
