@echo off
REM Builds "Aura Toggle.exe" into dist\.
REM
REM   build.bat                      EVERYTHING: portable, installer, dist\release with checksums
REM   build.bat portable             only dist\Aura Toggle.exe, ~740 KB
REM   build.bat installer            only the setup covering x64 and ARM64 (needs Inno Setup)
REM   build.bat all                  same as no argument at all
REM
REM No argument means "build the lot": a double click has to leave nothing out, which is what
REM kept the installer from appearing when only the portable exe was built by default.
REM
REM Nothing here bundles the .NET runtime. Both architectures are framework dependent, and the
REM setup installs the .NET 10 Desktop Runtime itself when the machine has none - which is what
REM took the download from 63 MB down to under 2 MB.
REM
REM A full build empties dist and leaves exactly this behind:
REM
REM   dist\Aura Toggle.exe            the portable x64 build
REM   dist\Aura An.lnk, Aura Aus.lnk  the two shortcuts, with a relative path
REM   dist\Setup Aura Toggle vX.exe   the setup, covering x64 and ARM64
REM   dist\arm64\Aura Toggle.exe      the ARM64 build, only a payload for the setup
REM
REM No .pdb: debug symbols belong to the build, not to what anybody downloads. They stay in
REM bin\publish\<rid>\ for reading a stack trace from a crash.
REM
setlocal

set ROOT=%~dp0
set MODE=%~1
set EXE=Aura Toggle.exe
set PDB=Aura Toggle.pdb

if "%MODE%"=="" goto :dispatch
if /I "%MODE%"=="all" goto :dispatch
if /I "%MODE%"=="installer" goto :dispatch
if /I "%MODE%"=="portable" goto :dispatch

REM A typo must not quietly build something else than what was asked for - and must not reach
REM the taskkill below, which would close the user's running copy for nothing.
echo.
echo Unknown option "%MODE%".
echo Use: portable ^| installer ^| all, or no argument for everything.
call :maybepause
exit /b 2

:dispatch
REM A running copy locks its own exe, which makes the publish step fail after ten retries -
REM that failure aborts "all" before it ever reaches the installer, so the installer silently
REM never appears. Closing it first is safe: it is this project's own build output.
taskkill /IM "%EXE%" /F >nul 2>nul

if "%MODE%"=="" goto :all
if /I "%MODE%"=="all" goto :all
if /I "%MODE%"=="installer" goto :installer

call :publish win-x64 "%ROOT%dist" shortcuts
if errorlevel 1 exit /b 1

echo.
echo Done: %ROOT%dist\%EXE%
call :maybepause
exit /b 0

REM Publishes one architecture into one folder, framework dependent.
REM   %1 runtime identifier   %2 output folder   %3 "shortcuts" to write the two dist shortcuts
REM
REM DEST, not OUTDIR: MSBuild reads the environment as properties and matches names without
REM regard to case, so an OUTDIR of its own made "dotnet publish" treat it as OutDir and drop
REM the whole intermediate build - Aura Toggle.dll, deps.json, runtimeconfig.json - into dist.
:publish
set RID=%~1
set DEST=%~2
set STAGE=%ROOT%bin\publish\%RID%

echo Building "%EXE%" [%RID%]

REM Publish into a staging folder. The publish step also drops its intermediate build
REM output there, and only the executable plus its symbols are the actual artifact.
if exist "%STAGE%" rd /s /q "%STAGE%"
dotnet publish "%ROOT%AuraToggle.csproj" -c Release -r %RID% --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -o "%STAGE%"
if errorlevel 1 (
    echo.
    echo BUILD FAILED
    exit /b 1
)

if not exist "%DEST%" mkdir "%DEST%"

REM Only the exe is taken out of the staging folder. Publishing straight into dist is what used
REM to leave the intermediate build output lying next to it; nothing is deleted here, so a
REM "build.bat portable" no longer removes the setup a full build produced.
REM Every copy is checked: a silent failure used to leave dist empty while "all" marched on and
REM packed a stale installer.
copy /y "%STAGE%\%EXE%" "%DEST%" >nul
if errorlevel 1 goto :copyfailed
if not exist "%DEST%\%EXE%" goto :copyfailed

REM Symbols are not part of the download, and an older build may have left a copy behind.
del /q "%DEST%\%PDB%" >nul 2>nul

REM Only the folder the user actually gets: the ARM64 payload needs no shortcuts of its own.
if /I not "%~3"=="shortcuts" exit /b 0

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\make-shortcuts.ps1" -Directory "%DEST%" -ExeName "%EXE%"
if errorlevel 1 goto :copyfailed
exit /b 0

:copyfailed
echo.
echo BUILD FAILED: could not assemble %DEST%
exit /b 1

:all
call :installer noexit
if errorlevel 1 goto :allfailed
call :checksums
echo.
echo Everything is in %ROOT%dist
dir /b "%ROOT%dist"
call :maybepause
exit /b 0

:allfailed
call :maybepause
exit /b 1

:checksums
REM Printed rather than written to a file: dist holds only what a user downloads, and the
REM checksums are wanted once, when a release is being put together.
echo.
echo Checksums:
powershell -NoProfile -Command "Get-ChildItem -LiteralPath '%ROOT%dist' -File -Filter *.exe | ForEach-Object { '  {0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name }"
exit /b 0

:version
REM Version is read from the project file, so it can only be changed in one place.
REM The line reads "    <Version>1.0.0</Version>": with < and > as the only
REM delimiters the leading indent is token 1, the tag name token 2, the value token 3.
REM APPVER rather than VERSION, for the same reason DEST is not OUTDIR: Version is an MSBuild
REM property, and the environment would override the one in the project file.
set APPVER=
for /f "tokens=3 delims=<>" %%v in ('findstr /r /c:"<Version>[0-9]" "%ROOT%AuraToggle.csproj"') do set APPVER=%%v

REM A missed parse would otherwise produce "Setup Aura Toggle v.exe" and fail much later.
if not defined APPVER (
    echo.
    echo BUILD FAILED: no ^<Version^> found in AuraToggle.csproj
    exit /b 1
)
exit /b 0

REM Empties a folder, with one retry: a file written moments ago can still be held briefly by
REM the virus scanner, and a half-deleted dist would leave yesterday's artifacts in today's.
:wipe
if not exist "%~1" exit /b 0
rd /s /q "%~1" 2>nul
if not exist "%~1" exit /b 0
ping -n 3 127.0.0.1 >nul
rd /s /q "%~1" 2>nul
if exist "%~1" echo WARNING: "%~1" could not be emptied completely.
exit /b 0

REM Builds both architectures, then packs one setup covering both.
:installer
REM Emptied first, so what is left afterwards is exactly this build and nothing from an older
REM one. Safe here because every artifact is rebuilt below - "build.bat portable" deletes
REM nothing, which is what stopped it from removing the setup.
call :wipe "%ROOT%dist"

call :publish win-x64 "%ROOT%dist" shortcuts
if errorlevel 1 goto :installerfailed
call :publish win-arm64 "%ROOT%dist\arm64"
if errorlevel 1 goto :installerfailed
call :version
if errorlevel 1 goto :installerfailed

REM Both payloads have to still be there at this exact moment. A single file bundle is a
REM favourite false positive for antivirus software, and a quarantined one vanishes after its
REM own build step reported success - which would leave the setup to be packed around a missing
REM or, worse, a stale binary.
if not exist "%ROOT%dist\%EXE%" goto :payloadmissing
if not exist "%ROOT%dist\arm64\%EXE%" goto :payloadmissing

set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" (
    echo.
    echo Inno Setup 6 not found. Install it with:  winget install JRSoftware.InnoSetup
    goto :installerfailed
)

echo Packing installer [x64 + arm64, version %APPVER%]
"%ISCC%" /Q "/DAppVersion=%APPVER%" "%ROOT%installer\aura.iss"
if errorlevel 1 (
    echo.
    echo INSTALLER FAILED
    goto :installerfailed
)

echo Installer: %ROOT%dist\Setup Aura Toggle v%APPVER%.exe
if /I not "%~1"=="noexit" call :maybepause
exit /b 0

:payloadmissing
echo.
echo INSTALLER FAILED: a build is missing from dist.
echo Most likely antivirus quarantined the single file bundle - check its log and exclude the
echo dist folder, then build again.
goto :installerfailed

:installerfailed
if /I not "%~1"=="noexit" call :maybepause
exit /b 1

:maybepause
REM Pause only on a double click, never in CI or in the autobuild hook.
REM
REM A double click runs exactly `%COMSPEC% /c ""<full path>" "`, so comparing against that is
REM both precise and free. The previous `echo %CMDCMDLINE% | find /I "/c"` was neither: any
REM `cmd /c build.bat` matched it, and an unqualified `find` resolves to GNU find on a PATH that
REM has Git or MSYS ahead of System32, which turns the test into a filesystem walk and leaves the
REM build sitting at `pause`.
if defined NOPAUSE exit /b 0
if /I not "%CMDCMDLINE%"=="%COMSPEC% /c ""%~f0"" " exit /b 0
pause
exit /b 0
