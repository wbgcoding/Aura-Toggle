@echo off
REM Builds aura.exe into dist\.
REM
REM   build.bat                      framework dependent, needs the .NET 10 runtime, ~460 KB
REM   build.bat standalone           self contained, runs without .NET installed, ~116 MB
REM   build.bat standalone win-arm64
REM   build.bat installer            standalone build packed into a setup (needs Inno Setup)
REM   build.bat installer win-arm64
REM
setlocal

set ROOT=%~dp0
set MODE=%~1
set RID=%~2
if "%RID%"=="" set RID=win-x64

REM Assignments stay on single lines: inside a block they would expand too early.
set SELFCONTAINED=false
set READYTORUN=true
set OUTDIR=%ROOT%dist
if /I "%MODE%"=="standalone" set SELFCONTAINED=true
if /I "%MODE%"=="installer" set SELFCONTAINED=true
if /I "%MODE%"=="standalone" set READYTORUN=false
if /I "%MODE%"=="installer" set READYTORUN=false
if /I "%MODE%"=="standalone" set OUTDIR=%ROOT%dist\standalone\%RID%
if /I "%MODE%"=="installer" set OUTDIR=%ROOT%dist\standalone\%RID%

REM Publish into a staging folder. The publish step also drops its intermediate build
REM output there, and only aura.exe plus its symbols are the actual artifact.
set STAGE=%ROOT%bin\publish\%RID%-%SELFCONTAINED%

echo Building aura.exe [%RID%, self-contained=%SELFCONTAINED%]

if exist "%STAGE%" rd /s /q "%STAGE%"
dotnet publish "%ROOT%AuraToggle.csproj" -c Release -r %RID% --self-contained %SELFCONTAINED% -p:PublishSingleFile=true -p:PublishReadyToRun=%READYTORUN% -o "%STAGE%"

if errorlevel 1 (
    echo.
    echo BUILD FAILED
    call :maybepause
    exit /b 1
)

if exist "%OUTDIR%" rd /s /q "%OUTDIR%"
mkdir "%OUTDIR%"
copy /y "%STAGE%\aura.exe" "%OUTDIR%" >nul
copy /y "%STAGE%\aura.pdb" "%OUTDIR%" >nul

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\make-shortcuts.ps1" -Directory "%OUTDIR%"

if /I "%MODE%"=="installer" call :installer
if errorlevel 1 exit /b 1

echo.
echo Done: %OUTDIR%\aura.exe
call :maybepause
exit /b 0

:installer
REM Version is read from the project file, so it can only be changed in one place.
for /f "tokens=2 delims=<>" %%v in ('findstr /i "<Version>" "%ROOT%AuraToggle.csproj"') do set VERSION=%%v

set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" (
    echo.
    echo Inno Setup 6 not found. Install it with:  winget install JRSoftware.InnoSetup
    call :maybepause
    exit /b 1
)

set ARCH=x64
if /I "%RID%"=="win-arm64" set ARCH=arm64

echo Packing installer [%ARCH%, version %VERSION%]
"%ISCC%" /Q "/DAppVersion=%VERSION%" "/DArch=%ARCH%" "/DSourceExe=%OUTDIR%\aura.exe" "%ROOT%installer\aura.iss"
if errorlevel 1 (
    echo.
    echo INSTALLER FAILED
    call :maybepause
    exit /b 1
)

echo Installer: %ROOT%dist\installer\Setup-AuraToggle-%VERSION%-%ARCH%.exe
exit /b 0

:maybepause
REM Pause only on a double click, never in CI or in the autobuild hook.
if defined NOPAUSE exit /b 0
echo %CMDCMDLINE% | find /I "/c" >nul || exit /b 0
pause
exit /b 0
