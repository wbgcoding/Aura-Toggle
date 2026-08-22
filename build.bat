@echo off
REM Builds "AuraToggle.exe" into dist\.
REM
REM   build.bat                      EVERYTHING: portable, installer, dist\release with checksums
REM   build.bat portable             only the portable x64 exe (~630 KB) and its two shortcuts
REM   build.bat installer            only the x64 setup (needs Inno Setup)
REM   build.bat all                  same as no argument at all
REM
REM No argument means "build the lot": a double click has to leave nothing out, which is what
REM kept the installer from appearing when only the portable exe was built by default.
REM
REM Nothing here bundles the .NET runtime. The build is framework dependent, and the setup
REM installs the .NET 10 Desktop Runtime itself when the machine has none - which is what took
REM the download from 63 MB to under 3 MB.
REM
REM A full build empties dist and leaves exactly this behind:
REM
REM   dist\AuraToggle.exe             the portable x64 build
REM   dist\Aura On.lnk, Aura Off.lnk  the two shortcuts, with a relative path
REM   dist\AuraToggle-Setup-X.exe     the setup, x64 only
REM   dist\SHA256SUMS.txt             a checksum for the two exe files above, not the shortcuts
REM
REM No .pdb: debug symbols belong to the build, not to what anybody downloads. They stay in
REM bin\publish\<rid>\ for reading a stack trace from a crash.
REM
setlocal

set "ROOT=%~dp0"
set "MODE=%~1"
set "EXE=AuraToggle.exe"
set "PDB=AuraToggle.pdb"

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
REM never appears. Closing it first is safe - but only this project's own build output: a plain
REM "taskkill /IM" used to close every AuraToggle.exe on the machine, including an installed copy
REM Ben happened to be running at the time. Filtered here to processes whose own path sits under
REM this checkout.
REM %ROOT% keeps its trailing backslash everywhere else in this file, but a quoted argument
REM ending in "\"" is read by the C-runtime argv parser as an escaped quote, not a closing one -
REM it swallowed the rest of the command line into one broken argument and printed a PowerShell
REM parser error above a build that otherwise still finished. Stripped for the command line only;
REM the script below puts it back before comparing, so "Aura-Toggle2\..." still cannot match
REM "Aura-Toggle" as a false-positive prefix.
powershell -NoProfile -Command "& { param($root, $name) $root = $root.TrimEnd('\') + '\'; Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($name)) -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force -ErrorAction SilentlyContinue }" "%ROOT:~0,-1%" "%EXE%"

if "%MODE%"=="" goto :all
if /I "%MODE%"=="all" goto :all
if /I "%MODE%"=="installer" goto :installer

call :publish win-x64 "%ROOT%dist"
if errorlevel 1 exit /b 1

echo.
echo Done: %ROOT%dist\%EXE%
call :maybepause
exit /b 0

REM Publishes into one folder, framework dependent, and writes the two dist shortcuts next to it.
REM   %1 runtime identifier   %2 output folder
REM
REM DEST, not OUTDIR: MSBuild reads the environment as properties and matches names without
REM regard to case, so an OUTDIR of its own made "dotnet publish" treat it as OutDir and drop
REM the whole intermediate build - AuraToggle.dll, deps.json, runtimeconfig.json - into dist.
:publish
set "RID=%~1"
set "DEST=%~2"
set "STAGE=%ROOT%bin\publish\%RID%"

echo Building "%EXE%" [%RID%]

REM Publish into a staging folder. The publish step also drops its intermediate build
REM output there, and only the executable plus its symbols are the actual artifact.
REM
REM No PublishReadyToRun: it precompiles to native code and was carrying ~40% of the exe's
REM weight for a startup saving nobody can feel - the hardware scan on launch already takes
REM longer than any JIT warm-up would.
if exist "%STAGE%" rd /s /q "%STAGE%"
dotnet publish "%ROOT%AuraToggle.csproj" -c Release -r %RID% --self-contained false -p:PublishSingleFile=true -o "%STAGE%"
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
if errorlevel 1 goto :allfailed
echo.
echo Everything is in %ROOT%dist
dir /b "%ROOT%dist"
call :maybepause
exit /b 0

:allfailed
call :maybepause
exit /b 1

:checksums
REM Printed for a quick look, and written into dist as SHA256SUMS.txt too - the standard
REM "<hash>  <filename>" format, so `sha256sum -c` or a manual compare both just work. Written
REM with LF line endings on purpose: Set-Content would end each line with CRLF, and sha256sum
REM then looks for a file whose name ends in a carriage return and reports every line as missing.
echo.
echo Checksums:
REM %ROOT% is passed as a script argument, not spliced into the PowerShell source: a checkout
REM path with an apostrophe (C:\Bob's Projects\...) used to close the single-quoted string early
REM and run the rest of the path as PowerShell code.
powershell -NoProfile -Command "& { param($root) $h = Get-ChildItem -LiteralPath $root -File -Filter *.exe | ForEach-Object { [pscustomobject]@{ H = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(); N = $_.Name } }; $h | ForEach-Object { '  {0}  {1}' -f $_.H, $_.N }; [IO.File]::WriteAllText((Join-Path $root 'SHA256SUMS.txt'), (($h | ForEach-Object { '{0}  {1}' -f $_.H, $_.N }) -join [string][char]10) + [char]10, [Text.Encoding]::ASCII) }" "%ROOT%dist"
if errorlevel 1 (
    echo.
    echo BUILD FAILED: checksum step errored
    exit /b 1
)

REM Two lines expected: the portable exe and the setup exe. "all" marching on over anything
REM else - zero, one, a half-written file from a step that errored past PowerShell's own exit
REM code - used to report success over a checksums file nobody could actually use.
set "SUMLINES=0"
if exist "%ROOT%dist\SHA256SUMS.txt" for /f %%c in ('find /c /v "" ^< "%ROOT%dist\SHA256SUMS.txt"') do set "SUMLINES=%%c"
if not "%SUMLINES%"=="2" (
    echo.
    echo BUILD FAILED: SHA256SUMS.txt has %SUMLINES% line^(s^), expected 2
    exit /b 1
)
exit /b 0

:version
REM Version is read from the project file, so it can only be changed in one place.
REM The line reads "    <Version>1.0.0</Version>": with < and > as the only
REM delimiters the leading indent is token 1, the tag name token 2, the value token 3.
REM APPVER rather than VERSION, for the same reason DEST is not OUTDIR: Version is an MSBuild
REM property, and the environment would override the one in the project file.
set "APPVER="
for /f "tokens=3 delims=<>" %%v in ('findstr /r /c:"<Version>[0-9]" "%ROOT%AuraToggle.csproj"') do set "APPVER=%%v"

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
if exist "%~1" (
    echo.
    echo BUILD FAILED: "%~1" could not be emptied completely.
    exit /b 1
)
exit /b 0

REM Builds the x64 exe, then packs the setup around it.
:installer
REM Emptied first, so what is left afterwards is exactly this build and nothing from an older
REM one. Safe here because every artifact is rebuilt below - "build.bat portable" deletes
REM nothing, which is what stopped it from removing the setup.
call :wipe "%ROOT%dist"
if errorlevel 1 goto :installerfailed

call :publish win-x64 "%ROOT%dist"
if errorlevel 1 goto :installerfailed
call :version
if errorlevel 1 goto :installerfailed

REM The payload has to still be there at this exact moment. A single file bundle is a favourite
REM false positive for antivirus software, and a quarantined one vanishes after its own build
REM step reported success - which would leave the setup to be packed around a missing or, worse,
REM a stale binary.
if not exist "%ROOT%dist\%EXE%" goto :payloadmissing

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
    echo.
    echo Inno Setup 6 not found. Install it with:  winget install JRSoftware.InnoSetup
    goto :installerfailed
)

echo Packing installer [x64, version %APPVER%]
"%ISCC%" /Q "/DAppVersion=%APPVER%" "%ROOT%installer\aura.iss"
if errorlevel 1 (
    echo.
    echo INSTALLER FAILED
    goto :installerfailed
)

echo Installer: %ROOT%dist\AuraToggle-Setup-%APPVER%.exe
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
