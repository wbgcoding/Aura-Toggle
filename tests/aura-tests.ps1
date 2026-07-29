# Regression suite for "Aura Toggle.exe". Switches the mainboard lighting while it runs
# and leaves it turned on at the end.
#
#   powershell -ExecutionPolicy Bypass -File tests\aura-tests.ps1

param(
    [string]$Exe = (Join-Path $PSScriptRoot "..\dist\Aura Toggle.exe")
)

$ErrorActionPreference = "Stop"
$state = Join-Path $env:LOCALAPPDATA "aura-toggle\state.json"
$settings = Join-Path $env:LOCALAPPDATA "aura-toggle\settings.json"
$failed = 0

# The suite must not depend on, or destroy, the settings in use on this machine.
$settingsBackup = "$settings.testbak"
if (Test-Path $settings) { Copy-Item $settings $settingsBackup -Force }
New-Item -ItemType Directory -Force (Split-Path $settings) | Out-Null
Set-Content -Path $settings -Encoding ascii `
    -Value '{"startMinimised":false,"minimiseOnClose":false,"startAction":""}'

function Restore-Settings {
    if (Test-Path $settingsBackup) {
        Move-Item $settingsBackup $settings -Force
    }
    else {
        Remove-Item $settings -ErrorAction SilentlyContinue
    }
}

function Invoke-Aura {
    param([string[]]$Arguments)
    $out = New-TemporaryFile
    $err = New-TemporaryFile
    # Start-Process joins the arguments unquoted, so anything with a space needs its own quotes.
    $quoted = $Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    try {
        $process = Start-Process $Exe -ArgumentList $quoted -Wait -NoNewWindow -PassThru `
            -RedirectStandardOutput $out -RedirectStandardError $err
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdErr   = (Get-Content $err -Raw -Encoding UTF8)
        }
    }
    finally {
        Remove-Item $out, $err -ErrorAction SilentlyContinue
    }
}

function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        Write-Host "  PASS  $Name"
    }
    catch {
        Write-Host "  FAIL  $Name -- $($_.Exception.Message)"
        $script:failed++
    }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$What)
    if ($Expected -ne $Actual) {
        throw "$What expected '$Expected' but was '$Actual'"
    }
}

if (-not (Test-Path $Exe)) {
    Write-Host "$Exe not found - run build.bat"
    exit 1
}

Write-Host "Arguments"

Test-Case "unknown argument reports usage and exits 2" {
    $r = Invoke-Aura @("-bla")
    Assert-Equal 2 $r.ExitCode "exit code"
    # Matched language independently: the tool answers in German or English.
    if ($r.StdErr -notmatch "aura \[-on") { throw "no usage line on stderr" }
}

Test-Case "more than one argument exits 2" {
    Assert-Equal 2 (Invoke-Aura @("on", "off")).ExitCode "exit code"
}

foreach ($form in "-off", "--off", "/off", "off", "-OFF") {
    Test-Case "'$form' switches off" {
        Assert-Equal 0 (Invoke-Aura @($form)).ExitCode "exit code"
        Assert-Equal $false ((Get-Content $state -Raw | ConvertFrom-Json).on) "stored state"
    }
}

foreach ($form in "-on", "--on", "/on", "on", "-ON") {
    Test-Case "'$form' switches on" {
        Assert-Equal 0 (Invoke-Aura @($form)).ExitCode "exit code"
        Assert-Equal $true ((Get-Content $state -Raw | ConvertFrom-Json).on) "stored state"
    }
}

Write-Host "Presets"

Test-Case "-preset rainbow selects effect 5" {
    Assert-Equal 0 (Invoke-Aura @("-preset", "rainbow")).ExitCode "exit code"
    Assert-Equal 5 ((Get-Content $state -Raw | ConvertFrom-Json).mode) "effect mode"
}

Test-Case "a preset name with spaces is accepted" {
    Assert-Equal 0 (Invoke-Aura @("-preset", "Spectrum Cycle")).ExitCode "exit code"
    Assert-Equal 4 ((Get-Content $state -Raw | ConvertFrom-Json).mode) "effect mode"
}

Test-Case "punctuation in a preset name is ignored" {
    Assert-Equal 0 (Invoke-Aura @("--preset", "chase_fade")).ExitCode "exit code"
    Assert-Equal 7 ((Get-Content $state -Raw | ConvertFrom-Json).mode) "effect mode"
}

Test-Case "a preset switches the lighting on" {
    [void](Invoke-Aura @("-off"))
    [void](Invoke-Aura @("-preset", "wave"))
    Assert-Equal $true ((Get-Content $state -Raw | ConvertFrom-Json).on) "stored state"
}

Test-Case "an unknown preset lists the available ones and exits 2" {
    $r = Invoke-Aura @("-preset", "does-not-exist")
    Assert-Equal 2 $r.ExitCode "exit code"
    if ($r.StdErr -notmatch "rainbow") { throw "preset list missing on stderr" }
}

Test-Case "-preset without a name exits 2" {
    Assert-Equal 2 (Invoke-Aura @("-preset")).ExitCode "exit code"
}

Test-Case "a preset takes a hex colour" {
    Assert-Equal 0 (Invoke-Aura @("-preset", "static", "#20C0FF")).ExitCode "exit code"
    $stored = Get-Content $state -Raw | ConvertFrom-Json
    Assert-Equal 32 $stored.red "red"
    Assert-Equal 192 $stored.green "green"
    Assert-Equal 255 $stored.blue "blue"
}

Test-Case "a preset takes a colour name" {
    Assert-Equal 0 (Invoke-Aura @("-preset", "static", "Lime")).ExitCode "exit code"
    Assert-Equal 0 ((Get-Content $state -Raw | ConvertFrom-Json).red) "red"
}

Test-Case "an unusable colour exits 2" {
    Assert-Equal 2 (Invoke-Aura @("-preset", "static", "not-a-colour")).ExitCode "exit code"
}

Write-Host "State"

Test-Case "switching off keeps the stored effect" {
    $before = (Get-Content $state -Raw | ConvertFrom-Json).mode
    [void](Invoke-Aura @("-off"))
    Assert-Equal $before ((Get-Content $state -Raw | ConvertFrom-Json).mode) "effect mode"
}

Test-Case "switching off twice stays off without error" {
    Assert-Equal 0 (Invoke-Aura @("-off")).ExitCode "exit code"
    Assert-Equal 0 (Invoke-Aura @("-off")).ExitCode "exit code"
    Assert-Equal $false ((Get-Content $state -Raw | ConvertFrom-Json).on) "stored state"
}

Test-Case "a damaged state file falls back to the default effect" {
    Set-Content -Path $state -Value "not json" -Encoding ascii
    Assert-Equal 0 (Invoke-Aura @("-on")).ExitCode "exit code"
    Assert-Equal 5 ((Get-Content $state -Raw | ConvertFrom-Json).mode) "effect mode"
}

Test-Case "a preset survives an off/on round trip" {
    [void](Invoke-Aura @("-preset", "chase"))
    [void](Invoke-Aura @("-off"))
    [void](Invoke-Aura @("-on"))
    Assert-Equal 9 ((Get-Content $state -Raw | ConvertFrom-Json).mode) "effect mode"
}

Write-Host "Shortcuts"

# Do not call the loop variable $name: inside Test-Case that would resolve to its own
# $Name parameter, which is how this test first went looking for the wrong file.
foreach ($linkName in "Aura An.lnk", "Aura Aus.lnk") {
    Test-Case "'$linkName' exists and carries a relative path" {
        $link = Join-Path (Split-Path $Exe) $linkName
        if (-not (Test-Path $link)) { throw "shortcut missing at $link" }

        $bytes = [IO.File]::ReadAllBytes($link)
        $flags = [BitConverter]::ToUInt32($bytes, 20)
        if (($flags -band 0x08) -eq 0) { throw "no relative path stored, the folder cannot be moved" }

        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($link)
        $expected = if ($linkName -eq "Aura An.lnk") { "-on" } else { "-off" }
        Assert-Equal $expected $shortcut.Arguments "arguments"
        Assert-Equal (Resolve-Path $Exe).Path $shortcut.TargetPath "target"
    }
}

Write-Host "Window"

Test-Case "-autostart shows the window when start minimised is off" {
    $process = Start-Process $Exe -ArgumentList "-autostart" -PassThru
    Start-Sleep -Seconds 3
    $process.Refresh()
    try {
        if ($process.MainWindowHandle -eq 0) { throw "no window" }
    }
    finally {
        [void]$process.CloseMainWindow()
        [void]$process.WaitForExit(5000)
    }
}

Test-Case "window opens, closes and leaves no process behind" {
    $process = Start-Process $Exe -PassThru
    Start-Sleep -Seconds 3
    $process.Refresh()
    if ($process.HasExited) { throw "process exited immediately" }
    # The title gains the controller name once the device has answered.
    if ($process.MainWindowTitle -notlike "Aura*") { throw "unexpected title '$($process.MainWindowTitle)'" }
    if ($process.MainWindowHandle -eq 0) { throw "no window handle" }

    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(5000)) { throw "window did not close" }
    Assert-Equal 0 $process.ExitCode "exit code"

    # Scoped to this build: another copy of the tool may legitimately be running elsewhere.
    $target = (Resolve-Path $Exe).Path
    $leftover = @(Get-Process -Name "Aura Toggle" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $target })
    Assert-Equal 0 $leftover.Count "leftover processes"
}

# Leave the machine on the default effect, switched on, with the real settings back.
[void](Invoke-Aura @("-preset", "rainbow"))
Restore-Settings

Write-Host ""
if ($failed -gt 0) {
    Write-Host "$failed test(s) failed"
    exit 1
}
Write-Host "all tests passed"
exit 0
