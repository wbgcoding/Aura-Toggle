; Inno Setup script for Aura Toggle.
;
; Built through build.bat, which publishes both self contained architectures and packs them
; into ONE installer that picks the matching binary for the machine it runs on:
;
;   build.bat installer
;
; The installed build is self contained, so the target machine needs no .NET runtime.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "Aura Toggle"
#define AppPublisher "BG Coding"
#define AppUrl "https://github.com/wbgcoding/aura-toggle"
#define SetupName "Setup Aura Toggle v" + AppVersion

[Setup]
AppId={{8E5C1F42-6A1D-4A0B-9C3F-2B7E4D9A1C55}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename={#SetupName}
SetupIconFile=..\assets\aura.ico
UninstallDisplayIcon={app}\Aura Toggle.exe
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE

; Per machine, into Program Files, so elevation is required.
PrivilegesRequired=admin
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Windows 10 and newer only.
MinVersion=10.0

; Offers to close a running copy instead of failing, and restores it afterwards.
CloseApplications=yes
RestartApplications=yes

; One installer for both architectures - which of the two [Files] entries below actually
; gets copied is decided per machine by the IsArm64 checks.
ArchitecturesAllowed=x64compatible or arm64
ArchitecturesInstallIn64BitMode=x64compatible or arm64

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en.AutoStart=Start Aura Toggle when Windows starts
en.DesktopIcon=Create a desktop shortcut
en.RemoveSettings=Also delete my settings and the remembered effect
de.AutoStart=Aura Toggle mit Windows starten
de.DesktopIcon=Verknüpfung auf dem Desktop anlegen
de.RemoveSettings=Auch meine Einstellungen und den gemerkten Effekt löschen

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStart}"; Flags: unchecked

[Files]
Source: "..\dist\standalone\win-x64\Aura Toggle.exe"; DestDir: "{app}"; DestName: "Aura Toggle.exe"; Flags: ignoreversion; Check: not IsArm64
Source: "..\dist\standalone\win-arm64\Aura Toggle.exe"; DestDir: "{app}"; DestName: "Aura Toggle.exe"; Flags: ignoreversion; Check: IsArm64
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.de.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Aura Toggle.exe"
Name: "{group}\Aura An"; Filename: "{app}\Aura Toggle.exe"; Parameters: "-on"
Name: "{group}\Aura Aus"; Filename: "{app}\Aura Toggle.exe"; Parameters: "-off"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Aura Toggle.exe"; Tasks: desktopicon

[Registry]
; Per user autostart, matching the switch inside the application itself.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "AuraToggle"; ValueData: """{app}\Aura Toggle.exe"" -autostart"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\Aura Toggle.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
{ User data lives outside the install folder and is kept unless the user opts out. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\aura-toggle');
    if DirExists(DataDir) then
      if SuppressibleMsgBox(ExpandConstant('{cm:RemoveSettings}'), mbConfirmation, MB_YESNO, IDNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
