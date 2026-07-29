@echo off
REM Builds "Aura Toggle.exe" into dist\.
REM
REM   build.bat                      framework dependent, needs the .NET 10 runtime, ~540 KB
REM   build.bat standalone           self contained, runs without .NET installed, ~120 MB
REM   build.bat standalone win-arm64
REM   build.bat installer            one setup for x64 and ARM64 (needs Inno Setup)
REM   build.bat all                  everything, plus dist\release ready to upload
REM
setlocal

set ROOT=%~dp0
set MODE=%~1
set RID=%~2
set EXE=Aura Toggle.exe
if "%RID%"=="" set RID=win-x64

if /I "%MODE%"=="all" goto :all
if /I "%MODE%"=="installer" goto :installer

if /I "%MODE%"=="standalone" (call :publish standalone %RID%) else (call :publish "" win-x64)
if errorlevel 1 exit /b 1

echo.
echo Done: %OUTDIR%\%EXE%
call :maybepause
exit /b 0

REM Publishes one build: portable (no argument) or self contained (standalone).
REM Leaves the result in %OUTDIR% for the caller.
:publish
REM Assignments stay on single lines: inside a block they would expand too early.
set SELFCONTAINED=false
set READYTORUN=true
set OUTDIR=%ROOT%dist
if /I "%~1"=="standalone" set SELFCONTAINED=true
if /I "%~1"=="standalone" set READYTORUN=false
if /I "%~1"=="standalone" set OUTDIR=%ROOT%dist\standalone\%~2

REM Publish into a staging folder. The publish step also drops its intermediate build
REM output there, and only the executable plus its symbols are the actual artifact.
set STAGE=%ROOT%bin\publish\%~2-%SELFCONTAINED%

echo Building "%EXE%" [%~2, self-contained=%SELFCONTAINED%]

if exist "%STAGE%" rd /s /q "%STAGE%"
dotnet publish "%ROOT%AuraToggle.csproj" -c Release -r %~2 --self-contained %SELFCONTAINED% -p:PublishSingleFile=true -p:PublishReadyToRun=%READYTORUN% -o "%STAGE%"
if errorlevel 1 (
    echo.
    echo BUILD FAILED
    exit /b 1
)

if exist "%OUTDIR%" rd /s /q "%OUTDIR%"
mkdir "%OUTDIR%"
copy /y "%STAGE%\%EXE%" "%OUTDIR%" >nul
copy /y "%STAGE%\Aura Toggle.pdb" "%OUTDIR%" >nul

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\make-shortcuts.ps1" -Directory "%OUTDIR%" -ExeName "%EXE%"
exit /b 0

:all
call :publish "" win-x64
if errorlevel 1 goto :allfailed
call :installer noexit
if errorlevel 1 goto :allfailed
call :release
if errorlevel 1 goto :allfailed
echo.
echo All artifacts are in %ROOT%dist, upload set in %ROOT%dist\release
call :maybepause
exit /b 0

:allfailed
call :maybepause
exit /b 1

:release
REM Collects exactly what gets attached to a release, with checksums beside it.
call :version

set RELEASE=%ROOT%dist\release
if exist "%RELEASE%" rd /s /q "%RELEASE%"
mkdir "%RELEASE%"

copy /y "%ROOT%dist\%EXE%" "%RELEASE%" >nul
copy /y "%ROOT%dist\installer\Setup Aura Toggle v%VERSION%.exe" "%RELEASE%" >nul

powershell -NoProfile -Command "Get-ChildItem -LiteralPath '%RELEASE%' -File | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } | Set-Content -LiteralPath '%RELEASE%\SHA256SUMS.txt' -Encoding ascii"

echo.
echo Release set:
dir /b "%RELEASE%"
exit /b 0

:version
REM Version is read from the project file, so it can only be changed in one place.
REM The line reads "    <Version>1.0.0</Version>": with < and > as the only
REM delimiters the leading indent is token 1, the tag name token 2, the value token 3.
for /f "tokens=3 delims=<>" %%v in ('findstr /i "<Version>" "%ROOT%AuraToggle.csproj"') do set VERSION=%%v
exit /b 0

REM Builds both self contained architectures, then packs one setup covering both.
:installer
call :publish standalone win-x64
if errorlevel 1 goto :installerfailed
call :publish standalone win-arm64
if errorlevel 1 goto :installerfailed
call :version

set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" (
    echo.
    echo Inno Setup 6 not found. Install it with:  winget install JRSoftware.InnoSetup
    goto :installerfailed
)

echo Packing installer [x64 + arm64, version %VERSION%]
"%ISCC%" /Q "/DAppVersion=%VERSION%" "%ROOT%installer\aura.iss"
if errorlevel 1 (
    echo.
    echo INSTALLER FAILED
    goto :installerfailed
)

echo Installer: %ROOT%dist\installer\Setup Aura Toggle v%VERSION%.exe
if /I not "%~1"=="noexit" call :maybepause
exit /b 0

:installerfailed
if /I not "%~1"=="noexit" call :maybepause
exit /b 1

:maybepause
REM Pause only on a double click, never in CI or in the autobuild hook.
if defined NOPAUSE exit /b 0
echo %CMDCMDLINE% | find /I "/c" >nul || exit /b 0
pause
exit /b 0
