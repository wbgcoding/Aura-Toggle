; Inno Setup script for Aura Toggle.
;
; Built through build.bat, which publishes both architectures and packs them into ONE installer
; that picks the matching binary for the machine it runs on:
;
;   build.bat installer
;
; The installed build is framework dependent, so it needs the .NET 10 Desktop Runtime. Rather
; than carrying a copy of the runtime in every download - which is what made this setup 63 MB -
; it checks for it and offers to fetch it from Microsoft, once, on the machines that lack it.

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
OutputDir=..\dist
OutputBaseFilename={#SetupName}
SetupIconFile=..\assets\aura.ico
UninstallDisplayIcon={app}\Aura Toggle.exe
UninstallDisplayName={#AppName}

; Per machine into Program Files by default, but the user may choose "just for me" instead,
; which installs into the profile and needs no elevation at all.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
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
; The licence page carries a plain-language preamble in front of the MIT text: what the licence
; allows, what the tool does to the board and who the trademarks belong to. The MIT text alone
; answers none of the questions somebody actually has at that point in a setup.
Name: "en"; MessagesFile: "compiler:Default.isl"; LicenseFile: "license-en.txt"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"; LicenseFile: "license-de.txt"

[CustomMessages]
en.AutoStart=Start Aura Toggle when Windows starts
en.DesktopIcon=Create a desktop shortcut
en.RemoveSettings=Also delete my settings and the remembered effect
en.RuntimeAsk=Aura Toggle needs the Microsoft .NET 10 Desktop Runtime, which this PC does not have yet.%n%nDownload it from Microsoft and install it now? It is about 60 MB and takes a minute.
en.RuntimeDownloading=Downloading the .NET 10 Desktop Runtime... %1%%
en.RuntimeInstalling=Installing the .NET 10 Desktop Runtime...
en.RuntimeDeclined=Aura Toggle cannot run without the .NET 10 Desktop Runtime, so nothing was installed.
en.RuntimeFailed=The .NET 10 Desktop Runtime could not be installed. Install it by hand from https://dotnet.microsoft.com/download/dotnet/10.0 and run this setup again.
de.AutoStart=Aura Toggle mit Windows starten
de.DesktopIcon=Verknüpfung auf dem Desktop anlegen
de.RemoveSettings=Auch meine Einstellungen und den gemerkten Effekt löschen
de.RuntimeAsk=Aura Toggle benötigt die Microsoft .NET 10 Desktop Runtime, die auf diesem PC noch fehlt.%n%nJetzt von Microsoft herunterladen und installieren? Das sind etwa 60 MB und dauert eine Minute.
de.RuntimeDownloading=.NET 10 Desktop Runtime wird heruntergeladen... %1%%
de.RuntimeInstalling=.NET 10 Desktop Runtime wird installiert...
de.RuntimeDeclined=Ohne die .NET 10 Desktop Runtime kann Aura Toggle nicht laufen, es wurde nichts installiert.
de.RuntimeFailed=Die .NET 10 Desktop Runtime konnte nicht installiert werden. Installieren Sie sie von Hand über https://dotnet.microsoft.com/download/dotnet/10.0 und starten Sie dieses Setup erneut.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStart}"; Flags: unchecked

[Files]
Source: "..\dist\Aura Toggle.exe"; DestDir: "{app}"; DestName: "Aura Toggle.exe"; Flags: ignoreversion; Check: not IsArm64
Source: "..\dist\arm64\Aura Toggle.exe"; DestDir: "{app}"; DestName: "Aura Toggle.exe"; Flags: ignoreversion; Check: IsArm64
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
; runasoriginaluser: without it the app inherits the installer's elevation and writes its
; state, settings and autostart entry into the administrator's profile instead of the user's,
; so the next normal start would find none of it.
Filename: "{app}\Aura Toggle.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
{ ---------------------------------------------------------------------------------------------
  The .NET 10 Desktop Runtime. The application is framework dependent, so it has to be there -
  but carrying it in the download would mean shipping 60 MB to every machine that already has
  it. Instead: look for it, ask once, fetch it from Microsoft's own short link and install it
  silently. The aka.ms link always points at the newest 10.0 patch, so nothing here goes stale.
  --------------------------------------------------------------------------------------------- }
const
  RuntimeUrlX64 = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  RuntimeUrlArm64 = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-arm64.exe';
  RuntimeFile = 'windowsdesktop-runtime.exe';
  RestartRequired = 3010;

{ Any 10.x is enough: a framework dependent build rolls forward to the newest one present. }
function DesktopRuntimeInstalled: Boolean;
var
  Found: TFindRec;
begin
  Result := False;
  if not FindFirst(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), Found) then
    Exit;

  try
    repeat
      if (Found.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Result := True;
        Break;
      end;
    until not FindNext(Found);
  finally
    FindClose(Found);
  end;
end;

function OnRuntimeProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax > 0 then
    WizardForm.PreparingLabel.Caption :=
      FmtMessage(CustomMessage('RuntimeDownloading'), [IntToStr((Progress * 100) div ProgressMax)]);

  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Url: String;
  ExitCode: Integer;
begin
  Result := '';
  if DesktopRuntimeInstalled then
    Exit;

  if SuppressibleMsgBox(CustomMessage('RuntimeAsk'), mbConfirmation, MB_YESNO, IDYES) <> IDYES then
  begin
    { Returning a message aborts the install and shows it, which beats installing an
      application that would then refuse to start. }
    Result := CustomMessage('RuntimeDeclined');
    Exit;
  end;

  if IsArm64 then
    Url := RuntimeUrlArm64
  else
    Url := RuntimeUrlX64;

  try
    DownloadTemporaryFile(Url, RuntimeFile, '', @OnRuntimeProgress);
  except
    Result := CustomMessage('RuntimeFailed');
    Exit;
  end;

  WizardForm.PreparingLabel.Caption := CustomMessage('RuntimeInstalling');

  { Its own installer asks for elevation when this setup is running per user, which is the one
    place a "just for me" install still shows a UAC prompt. }
  if not Exec(ExpandConstant('{tmp}\' + RuntimeFile), '/install /quiet /norestart', '',
              SW_SHOW, ewWaitUntilTerminated, ExitCode) then
  begin
    Result := CustomMessage('RuntimeFailed');
    Exit;
  end;

  if ExitCode = RestartRequired then
    NeedsRestart := True
  else if ExitCode <> 0 then
    Result := CustomMessage('RuntimeFailed');
end;

{ User data lives outside the install folder and is kept unless the user opts out. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { The Run entry is only removed by uninsdeletevalue when the install-time task was ticked.
      Autostart switched on later from the gear writes the same value name, so it has to be
      cleared unconditionally - otherwise an entry pointing at a deleted exe is left behind. }
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'AuraToggle');

    DataDir := ExpandConstant('{localappdata}\aura-toggle');
    if DirExists(DataDir) then
      if SuppressibleMsgBox(ExpandConstant('{cm:RemoveSettings}'), mbConfirmation, MB_YESNO, IDNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
