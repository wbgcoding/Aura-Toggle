; Inno Setup script for Aura Toggle.
;
; Built through build.bat, which publishes the x64 exe and packs it into the installer:
;
;   build.bat installer
;
; The installed build is framework dependent, so it needs the .NET 10 Desktop Runtime. Rather
; than carrying a copy of the runtime in every download - which is what made this setup 63 MB -
; it checks for it and offers to fetch it from Microsoft, once, on the machines that lack it.

#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif

#define AppName "Aura Toggle"
#define AppExe "AuraToggle.exe"
#define AppPublisher "BG Coding"
#define AppUrl "https://github.com/wbgcoding/aura-toggle"
#define SetupName "AuraToggle-Setup-" + AppVersion

[Setup]
AppId={{8E5C1F42-6A1D-4A0B-9C3F-2B7E4D9A1C55}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=© 2026 BG Coding
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
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

; Per machine into Program Files by default, but the user may choose "just for me" instead,
; which installs into the profile and needs no elevation at all.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
; The strongest setting Inno offers. Measured: this is saturated - lzma2/max produces a file
; within a dozen bytes of it. What is left of the download is the Inno engine itself, not the
; program.
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

; Windows 10 and newer only.
MinVersion=10.0

; Offers to close a running copy instead of failing, and restores it afterwards.
CloseApplications=yes
RestartApplications=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
; The licence page carries a plain-language preamble in front of the MIT text: what the licence
; allows, what the tool does to the board and who the trademarks belong to. The MIT text alone
; answers none of the questions somebody actually has at that point in a setup.
Name: "en"; MessagesFile: "compiler:Default.isl"; LicenseFile: "license-en.txt"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"; LicenseFile: "license-de.txt"

[CustomMessages]
en.AutoStart=Start Aura Toggle when Windows starts
en.DesktopIcon=Create a desktop shortcut
en.RemoveSettings=Also delete my settings and the remembered effect (%1)
en.RuntimeAsk=Aura Toggle needs the Microsoft .NET 10 Desktop Runtime, which this PC does not have yet.%n%nDownload it from Microsoft and install it now? It is about 60 MB and takes a minute.
en.RuntimeDownloading=Downloading the .NET 10 Desktop Runtime... %1%%
en.RuntimeInstalling=Installing the .NET 10 Desktop Runtime...
en.RuntimeDeclined=Aura Toggle cannot run without the .NET 10 Desktop Runtime, so nothing was installed.
en.RuntimeFailed=The .NET 10 Desktop Runtime could not be installed. Install it by hand from https://dotnet.microsoft.com/download/dotnet/10.0 and run this setup again.
de.AutoStart=Aura Toggle mit Windows starten
de.DesktopIcon=Verknüpfung auf dem Desktop anlegen
de.RemoveSettings=Auch meine Einstellungen und den gemerkten Effekt löschen (%1)
de.RuntimeAsk=Aura Toggle benötigt die Microsoft .NET 10 Desktop Runtime, die auf diesem PC noch fehlt.%n%nJetzt von Microsoft herunterladen und installieren? Das sind etwa 60 MB und dauert eine Minute.
de.RuntimeDownloading=.NET 10 Desktop Runtime wird heruntergeladen... %1%%
de.RuntimeInstalling=.NET 10 Desktop Runtime wird installiert...
de.RuntimeDeclined=Ohne die .NET 10 Desktop Runtime kann Aura Toggle nicht laufen, es wurde nichts installiert.
de.RuntimeFailed=Die .NET 10 Desktop Runtime konnte nicht installiert werden. Installieren Sie sie von Hand über https://dotnet.microsoft.com/download/dotnet/10.0 und starten Sie dieses Setup erneut.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStart}"; Flags: unchecked

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; DestName: "{#AppExe}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
; Never installed to the target machine - only extracted to {tmp} on demand, to check the
; downloaded .NET runtime installer's signature before PrepareToInstall runs it elevated.
Source: "verify-signature.ps1"; DestDir: "{tmp}"; Flags: dontcopy

[InstallDelete]
; Upgrading from 1.0.0, which installed the executable as "aura.exe" and put three entries in the
; Start menu. Without this the old binary stays behind next to the new one - and a Run entry
; written by that version still points at it, so Windows would go on starting the old build at
; logon. The two extra shortcuts were dropped on purpose (see [Icons] below) and have to go with it.
Type: files; Name: "{app}\aura.exe"
Type: files; Name: "{group}\Aura An.lnk"
Type: files; Name: "{group}\Aura Aus.lnk"

[Icons]
; The application and nothing else. One-click on/off shortcuts used to be created here too, which
; put three entries in the Start menu for a tool whose whole point is one switch - anyone who
; wants them can make one from the documented -on/-off arguments.
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Per user autostart, matching the switch inside the application itself. Correct for the common
; case (elevating as yourself, same HKCU). If the setup was instead elevated with a *different*
; administrator's credentials, HKCU here is that admin's hive, not the one the app actually runs
; under - the [Run] entry below repeats the same write as the original user to cover that case too.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "AuraToggle"; ValueData: """{app}\{#AppExe}"" -autostart"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; runasoriginaluser: without it the app inherits the installer's elevation and writes its
; state, settings and autostart entry into the administrator's profile instead of the user's,
; so the next normal start would find none of it.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent runasoriginaluser
; Same reasoning as above, aimed at the [Registry] entry's blind spot: reg.exe run as the
; original user writes the value into the profile that will actually start the app.
Filename: "reg.exe"; Parameters: "add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v AuraToggle /d ""\""{app}\{#AppExe}\"" -autostart"" /f"; \
    Flags: runasoriginaluser runhidden; Tasks: autostart

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
  RuntimeFile = 'windowsdesktop-runtime.exe';
  RestartRequired = 3010;

{ ASFW_ANY: passed instead of a specific process id to hand the foreground-activation right to
  whichever process asks for it next, rather than one this script would have to track down. }
  AnyProcess = $FFFFFFFF;

{ SetWindowPos: the two z-order positions and the flags that leave everything else alone. }
  TopMost = -1;
  NoTopMost = -2;
  KeepPlace = $0053; { SWP_NOSIZE or SWP_NOMOVE or SWP_NOACTIVATE or SWP_SHOWWINDOW }

function AllowSetForegroundWindow(dwProcessId: LongWord): Boolean;
  external 'AllowSetForegroundWindow@user32.dll stdcall';

function SetForegroundWindow(hWnd: HWND): Boolean;
  external 'SetForegroundWindow@user32.dll stdcall';

function SetWindowPos(hWnd: HWND; hWndInsertAfter: Integer; X, Y, cx, cy: Integer;
  uFlags: LongWord): Boolean; external 'SetWindowPos@user32.dll stdcall';

{ Setup itself can come up behind whatever the user was looking at: the elevated process that
  actually runs the wizard is started by the elevation service, not by the window that had the
  foreground, so Windows does not hand it the foreground automatically. Asking for it is the
  polite half; raising the window to the top of the z-order and straight back off topmost is the
  half that works when the request is refused. Same two steps, and the same reason, as
  ForegroundWindow.Claim in the application - keep both here too. }
var
  WizardRaised: Boolean;

procedure BringWizardToFront;
begin
  SetForegroundWindow(WizardForm.Handle);
  SetWindowPos(WizardForm.Handle, TopMost, 0, 0, 0, 0, KeepPlace);
  SetWindowPos(WizardForm.Handle, NoTopMost, 0, 0, 0, 0, KeepPlace);
end;

procedure InitializeWizard;
begin
  BringWizardToFront;
end;

{ Again on the first page: InitializeWizard runs before the wizard is on screen, and a window that
  is not visible yet cannot be raised above anything. }
procedure CurPageChanged(CurPageID: Integer);
begin
  if not WizardRaised then
  begin
    WizardRaised := True;
    BringWizardToFront;
  end;
end;

{ Checks one candidate ".dotnet root\shared\Microsoft.WindowsDesktop.App" for any 10.x - a
  framework dependent build rolls forward to the newest one present, so the exact patch does
  not matter. A directory name match alone does not prove the runtime is actually usable there
  though - a failed uninstall or an interrupted install can leave an empty or stripped folder
  behind with the right name. System.Windows.Forms.dll is the one assembly this application
  itself needs, so its presence is what "usable" means here, not just a matching folder name. }
function HasDesktopRuntimeAt(const DotnetRoot: String): Boolean;
var
  Found: TFindRec;
begin
  Result := False;
  if (DotnetRoot = '') or
     not FindFirst(DotnetRoot + '\shared\Microsoft.WindowsDesktop.App\10.*', Found) then
    Exit;

  try
    repeat
      if ((Found.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
         FileExists(DotnetRoot + '\shared\Microsoft.WindowsDesktop.App\' + Found.Name +
           '\System.Windows.Forms.dll') then
      begin
        Result := True;
        Break;
      end;
    until not FindNext(Found);
  finally
    FindClose(Found);
  end;
end;

{ The official installer always goes to Program Files, but a machine with the SDK put there by
  `dotnet-install` (common in dev/CI setups) can have it under a custom DOTNET_ROOT, or under
  the user's own profile instead - checked too, so those machines are not asked to download
  60 MB they already effectively have. }
function DesktopRuntimeInstalled: Boolean;
begin
  Result :=
    HasDesktopRuntimeAt(ExpandConstant('{commonpf}\dotnet')) or
    HasDesktopRuntimeAt(ExpandConstant('{localappdata}\Microsoft\dotnet')) or
    HasDesktopRuntimeAt(GetEnv('DOTNET_ROOT'));
end;

function OnRuntimeProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax > 0 then
    WizardForm.PreparingLabel.Caption :=
      FmtMessage(CustomMessage('RuntimeDownloading'), [IntToStr((Progress * 100) div ProgressMax)]);

  Result := True;
end;

{ ---------------------------------------------------------------------------------------------
  DownloadTemporaryFile takes an expected SHA-256, but the aka.ms link always points at whatever
  the newest 10.0 patch is, so there is no fixed hash to pin here. This is the check that stands
  in for it: the same Authenticode verification Windows itself runs when a user double-clicks a
  downloaded .exe and picks "Run" - a tampered, corrupted or unsigned file fails it.

  The natural way to run that check from Inno Setup is WinVerifyTrust, called directly - but
  Pascal Script's "@" operator only yields addresses of procedures (needed for the callback
  DownloadTemporaryFile already takes above), not of plain variables, and WinVerifyTrust needs
  the address of a local WINTRUST_FILE_INFO/WINTRUST_DATA record to point its union member at.
  Confirmed by compiling a minimal test script against ISCC rather than assumed: "@" on a data
  variable is rejected with "Unknown identifier", so there is no way to marshal those structs by
  hand here. PowerShell's own Get-AuthenticodeSignature is the same check without that problem -
  it ships on every supported version of Windows, so this shells out to it instead of adding a
  third-party Inno Setup plugin DLL for one boolean answer.
  --------------------------------------------------------------------------------------------- }
function IsAuthenticodeSigned(const FileName: String): Boolean;
var
  ScriptPath: String;
  Params: String;
  ExitCode: Integer;
begin
  ExtractTemporaryFile('verify-signature.ps1');
  ScriptPath := ExpandConstant('{tmp}\verify-signature.ps1');

  { -ExecutionPolicy Bypass applies only to this one process, not any lasting machine setting -
    without it, a script FILE (unlike an inline -Command) refuses to run at all on a machine
    whose policy is still the Windows default of Restricted. }
  Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '" "' +
    FileName + '"';

  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Params, '',
    SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
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

  try
    DownloadTemporaryFile(RuntimeUrlX64, RuntimeFile, '', @OnRuntimeProgress);
  except
    Result := CustomMessage('RuntimeFailed');
    Exit;
  end;

  if not IsAuthenticodeSigned(ExpandConstant('{tmp}\' + RuntimeFile)) then
  begin
    { Refused rather than run elevated - a broken or missing signature is exactly what a
      tampered or substituted download looks like. }
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

{ ---------------------------------------------------------------------------------------------
  1.0.0 wrote its autostart entry pointing at "aura.exe", and the switch inside the application
  writes the same value name - so an entry left from that version survives an upgrade while the
  file it names has just been deleted above, and autostart silently stops working for someone who
  had deliberately turned it on. Repointed rather than removed, because the user's choice was
  "start with Windows" and that is still what they want; entries that already name the current
  executable are left untouched. Run as the original user for the same reason the [Run] section
  is: elevated as a different administrator, HKCU here is that admin's hive, not the real user's.
  --------------------------------------------------------------------------------------------- }
procedure RepointLegacyAutoStart;
var
  RunKey: String;
  Current: String;
  Wanted: String;
  ResultCode: Integer;
begin
  RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
  if not RegQueryStringValue(HKEY_CURRENT_USER, RunKey, 'AuraToggle', Current) then
    Exit;

  { Only the entry the old version wrote. Anything already naming the current executable - or
    something the user pointed elsewhere themselves - is none of this installer's business. }
  if Pos('aura.exe', Lowercase(Current)) = 0 then
    Exit;

  Wanted := '"' + ExpandConstant('{app}\{#AppExe}') + '" -autostart';
  RegWriteStringValue(HKEY_CURRENT_USER, RunKey, 'AuraToggle', Wanted);

  { Same blind spot the [Run] section works around: elevated as a different administrator, the
    write above lands in that account's hive rather than the one the app actually starts under. }
  ExecAsOriginalUser('reg.exe',
    'add "HKCU\' + RunKey + '" /v AuraToggle /d "' + Wanted + '" /f',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { After the files are in place, so the entry is repointed at an executable that exists. }
  if CurStep = ssPostInstall then
    RepointLegacyAutoStart;

  { The [Run] entry below launches the app de-elevated (runasoriginaluser) once the user clicks
    Finish - a different hand-off than a plain child process, which is why it used to open behind
    whatever window had focus at that point instead of in front. That hand-off does not go through
    this elevated Setup.exe directly, so this grant does not reliably reach the process it actually
    creates - kept anyway as a harmless, no-cost attempt for whichever launch path it does reach.
    The launched application now claims the foreground itself once its window is up, which does
    not depend on this grant at all and is the fix this bug actually needed. }
  if CurStep = ssDone then
    AllowSetForegroundWindow(AnyProcess);
end;

{ A standard, non-elevated user fully controls their own LOCALAPPDATA environment variable - no
  special rights needed, System Properties or a plain "setx" both reach it. Accepted only when it
  has the shape a real profile's local app data folder actually has ("<system drive>\Users\
  <name>\AppData\Local"), so a value pointed somewhere else cannot turn the recursive, forced
  delete in CurUninstallStepChanged below - which can be running with a more privileged token
  than the very user this string is read from - into deleting an arbitrary elevation-only path
  that happens to contain a folder literally named "aura-toggle". }
function LooksLikeLocalAppData(const path: String): Boolean;
var
  prefix: String;
begin
  prefix := Lowercase(ExpandConstant('{sd}\users\'));
  Result :=
    (Length(path) > Length(prefix) + Length('\appdata\local')) and
    (Copy(Lowercase(path), 1, Length(prefix)) = prefix) and
    (Copy(Lowercase(path), Length(path) - 13, 14) = '\appdata\local') and
    { Without this the two checks above only constrain both ends of the string: a value like
      "<sd>\Users\me\..\..\Windows\...\AppData\Local" satisfies them and still walks out of the
      profile. Nothing legitimate needs a relative step in the middle of this path. }
    (Pos('..', path) = 0);
end;

{ The LocalAppData constant resolves for whichever account this process is running as - the real
  interactive user when the uninstaller elevated as themselves, but a different administrator's
  own profile when it did not (the same blind spot the Run-key deletion below already works
  around). Reads the real value out of the original user's own environment instead of assuming it
  matches this process's, the same way ExecAsOriginalUser reaches that user's registry hive: a
  command run as them writes their LocalAppData environment variable to a temp file, which this
  process then reads back. Falls back to the plain constant - this process's own, safely resolved
  by Inno itself rather than read from a spoofable environment variable - whenever that round
  trip fails, or its result does not look like a real profile path; not knowing the right folder
  should not block the rest of the uninstall, and a value that fails the shape check is exactly
  as unusable as one that failed to read at all. }
function OriginalUserLocalAppData: String;
var
  TempFile: String;
  Lines: TArrayOfString;
  ResultCode: Integer;
  candidate: String;
begin
  Result := ExpandConstant('{localappdata}');
  TempFile := ExpandConstant('{tmp}\aura-toggle-orig-localappdata.txt');

  if ExecAsOriginalUser(ExpandConstant('{cmd}'), '/c echo %LOCALAPPDATA%>"' + TempFile + '"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) and
     LoadStringsFromFile(TempFile, Lines) and (GetArrayLength(Lines) > 0) then
  begin
    candidate := Trim(Lines[0]);
    if LooksLikeLocalAppData(candidate) then
      Result := candidate;
  end;

  DeleteFile(TempFile);
end;

{ Plain file names, comma-joined, of every file (not subfolder) directly in DataDir - named
  rather than counted, since Inno's Pascal Script has no JSON parser to count entries with and a
  name list needs none. Used only to tell the user what the Yes below actually deletes. }
function ListDataFiles(DataDir: String): String;
var
  FindRec: TFindRec;
begin
  Result := '';
  if FindFirst(DataDir + '\*', FindRec) then
  begin
    try
      repeat
        if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY = 0 then
        begin
          if Result <> '' then
            Result := Result + ', ';
          Result := Result + FindRec.Name;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

{ User data lives outside the install folder and is kept unless the user opts out. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  FileList: String;
  ResultCode: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { The Run entry is only removed by uninsdeletevalue when the install-time task was ticked.
      Autostart switched on later from the gear writes the same value name, so it has to be
      cleared unconditionally - otherwise an entry pointing at a deleted exe is left behind. }
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'AuraToggle');

    { Same blind spot as the [Run] reg.exe entry at install time: if this uninstaller is
      elevated as a *different* administrator account than the one that actually used the app,
      the call above clears that admin's own Run key, not the real user's. [UninstallRun] has no
      runasoriginaluser flag (Run-only), so ExecAsOriginalUser is the documented way to still
      reach the real interactive user's hive from [Code]. }
    ExecAsOriginalUser('reg.exe',
      'delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v AuraToggle /f',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    DataDir := OriginalUserLocalAppData + '\aura-toggle';
    if DirExists(DataDir) then
    begin
      FileList := ListDataFiles(DataDir);
      if SuppressibleMsgBox(FmtMessage(CustomMessage('RemoveSettings'), [FileList]),
        mbConfirmation, MB_YESNO, IDNO) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
