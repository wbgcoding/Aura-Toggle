# Regression suite for "Aura Toggle.exe". Switches the mainboard lighting while it runs
# and leaves it turned on at the end.
#
#   powershell -ExecutionPolicy Bypass -File tests\aura-tests.ps1

param(
    [string]$Exe = (Join-Path $PSScriptRoot "..\dist\Aura Toggle.exe")
)

$ErrorActionPreference = "Stop"
$dataDir = Join-Path $env:LOCALAPPDATA "aura-toggle"
$state = Join-Path $dataDir "state.json"
$settings = Join-Path $dataDir "settings.json"
$presets = Join-Path $dataDir "presets.json"
$channelState = Join-Path $dataDir "channel-state.json"
$failed = 0

if (-not (Test-Path $Exe)) {
    Write-Host "$Exe not found - run build.bat"
    exit 1
}

# Every file the tool writes, not just settings.json: the suite switches the lighting, changes
# presets and rewrites the per-channel records, so all of them have to come back exactly as they
# were. Getting this wrong silently destroys the user's presets and channel names.
$dataFiles = @("state.json", "settings.json", "presets.json", "channel-names.json", "channel-state.json")

New-Item -ItemType Directory -Force $dataDir | Out-Null

$backups = @{}
foreach ($name in $dataFiles) {
    $path = Join-Path $dataDir $name
    $backups[$name] = if (Test-Path $path) { Get-Content $path -Raw -Encoding UTF8 } else { $null }
}

function Restore-UserData {
    foreach ($name in $script:dataFiles) {
        $path = Join-Path $script:dataDir $name
        $saved = $script:backups[$name]
        if ($null -eq $saved) {
            Remove-Item $path -ErrorAction SilentlyContinue
        }
        else {
            # -NoNewline: the tool writes no trailing newline, so this restores it byte for byte.
            Set-Content -Path $path -Value $saved -Encoding UTF8 -NoNewline
        }
    }
}

Set-Content -Path $settings -Encoding ascii `
    -Value '{"startMinimised":false,"minimiseOnClose":false,"startAction":""}'

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

try {

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

Write-Host "Brightness"

Test-Case "-brightness sets the stored percentage" {
    Assert-Equal 0 (Invoke-Aura @("-brightness", "50")).ExitCode "exit code"
    Assert-Equal 50 ((Get-Content $state -Raw | ConvertFrom-Json).brightness) "brightness"
}

Test-Case "a brightness below the floor is clamped, not rejected" {
    Assert-Equal 0 (Invoke-Aura @("-brightness", "0")).ExitCode "exit code"
    Assert-Equal 10 ((Get-Content $state -Raw | ConvertFrom-Json).brightness) "brightness"
}

Test-Case "-brightness 100 is accepted" {
    Assert-Equal 0 (Invoke-Aura @("-brightness", "100")).ExitCode "exit code"
    Assert-Equal 100 ((Get-Content $state -Raw | ConvertFrom-Json).brightness) "brightness"
}

foreach ($bad in "101", "255", "abc", "-5") {
    Test-Case "-brightness $bad exits 2" {
        Assert-Equal 2 (Invoke-Aura @("-brightness", $bad)).ExitCode "exit code"
    }
}

Test-Case "-brightness without a value exits 2" {
    Assert-Equal 2 (Invoke-Aura @("-brightness")).ExitCode "exit code"
}

Test-Case "brightness never dims what is stored, only what is sent" {
    [void](Invoke-Aura @("-brightness", "40"))
    [void](Invoke-Aura @("-preset", "static", "#20C0FF"))

    # The record has to hold the colour as chosen. Storing the dimmed value would darken the
    # lighting again on every restore.
    $stored = Get-Content $state -Raw | ConvertFrom-Json
    Assert-Equal 32 $stored.red "stored red"
    Assert-Equal 192 $stored.green "stored green"
    Assert-Equal 255 $stored.blue "stored blue"

    foreach ($entry in (Get-Content $channelState -Raw | ConvertFrom-Json).PSObject.Properties) {
        Assert-Equal 32 $entry.Value.red "channel red"
        Assert-Equal 192 $entry.Value.green "channel green"
        Assert-Equal 255 $entry.Value.blue "channel blue"
    }

    [void](Invoke-Aura @("-brightness", "100"))
}

Test-Case "-brightness while off only stores the percentage" {
    [void](Invoke-Aura @("-off"))
    Assert-Equal 0 (Invoke-Aura @("-brightness", "60")).ExitCode "exit code"
    $stored = Get-Content $state -Raw | ConvertFrom-Json
    Assert-Equal 60 $stored.brightness "brightness"
    Assert-Equal $false $stored.on "stored state"
    [void](Invoke-Aura @("-brightness", "100"))
}

Write-Host "Per channel state"

Test-Case "-off marks every channel off but keeps its effect and colour" {
    [void](Invoke-Aura @("-preset", "static", "#20C0FF"))
    [void](Invoke-Aura @("-off"))

    $entries = @((Get-Content $channelState -Raw | ConvertFrom-Json).PSObject.Properties)
    if ($entries.Count -eq 0) { throw "no channel records were written" }

    foreach ($entry in $entries) {
        Assert-Equal $false $entry.Value.on "channel on"
        Assert-Equal 1 $entry.Value.mode "channel mode kept"
        Assert-Equal 32 $entry.Value.red "channel colour kept"
    }
}

Test-Case "-on brings every channel back on with its own colour" {
    [void](Invoke-Aura @("-on"))
    foreach ($entry in (Get-Content $channelState -Raw | ConvertFrom-Json).PSObject.Properties) {
        Assert-Equal $true $entry.Value.on "channel on"
        Assert-Equal 32 $entry.Value.red "channel colour"
    }
    Assert-Equal $true ((Get-Content $state -Raw | ConvertFrom-Json).on) "board state"
}

Test-Case "a board-wide brightness hands every channel back to the board" {
    [void](Invoke-Aura @("-preset", "static", "#20C0FF"))
    Assert-Equal 0 (Invoke-Aura @("-brightness", "55")).ExitCode "exit code"

    Assert-Equal 55 ((Get-Content $state -Raw | ConvertFrom-Json).brightness) "board brightness"
    foreach ($entry in (Get-Content $channelState -Raw | ConvertFrom-Json).PSObject.Properties) {
        # 0 means "no brightness of its own", which is what the whole board being set implies.
        Assert-Equal 0 $entry.Value.brightness "channel brightness"
    }

    [void](Invoke-Aura @("-brightness", "100"))
}

Test-Case "a channel keeps its own brightness when its colour changes" {
    [void](Invoke-Aura @("-preset", "static", "#20C0FF"))

    # The keys are the real device paths, so they are taken from the file the tool just wrote
    # rather than invented here.
    $records = Get-Content $channelState -Raw | ConvertFrom-Json
    $first = @($records.PSObject.Properties)[0]
    if ($null -eq $first) { throw "no channel records were written" }

    $first.Value.brightness = 40
    Set-Content -Path $channelState -Value ($records | ConvertTo-Json -Depth 4 -Compress) `
        -Encoding ascii -NoNewline

    # An effect and a colour carry no brightness, so the one this channel was given has to stay.
    [void](Invoke-Aura @("-preset", "breathing", "#112233"))
    $after = (Get-Content $channelState -Raw | ConvertFrom-Json).($first.Name)
    Assert-Equal 40 $after.brightness "brightness after a colour change"
    Assert-Equal 2 $after.mode "effect after a colour change"

    # And an off/on round trip must not flatten it either.
    [void](Invoke-Aura @("-off"))
    [void](Invoke-Aura @("-on"))
    Assert-Equal 40 ((Get-Content $channelState -Raw | ConvertFrom-Json).($first.Name).brightness) `
        "brightness after off and on"

    [void](Invoke-Aura @("-brightness", "100"))
}

Test-Case "a custom preset applies its own brightness per channel" {
    [void](Invoke-Aura @("-preset", "static", "#20C0FF"))
    [void](Invoke-Aura @("-brightness", "100"))

    # The real device key, taken from the file the tool just wrote.
    $first = @((Get-Content $channelState -Raw | ConvertFrom-Json).PSObject.Properties)[0]
    if ($null -eq $first) { throw "no channel records were written" }
    $split = $first.Name -split '\|'
    $deviceKey = ($split[0..($split.Count - 2)] -join '|')
    $channel = [int]$split[-1]

    $preset = @{
        name    = "Testhelligkeit"
        entries = @(@{
            deviceKey  = $deviceKey
            channel    = $channel
            label      = "x"
            mode       = 1
            red        = 32
            green      = 192
            blue       = 255
            brightness = 45
        })
    }

    # Wrapped by hand: ConvertTo-Json in Windows PowerShell unwraps a one element array, which
    # would write an object where the tool expects a list - and be ignored, exactly as intended.
    Set-Content -Path $presets -Encoding ascii -NoNewline `
        -Value ("[" + ($preset | ConvertTo-Json -Depth 5 -Compress) + "]")

    $stored = Get-Content $state -Raw | ConvertFrom-Json
    $stored.customPreset = "Testhelligkeit"
    $stored.on = $true
    Set-Content -Path $state -Value ($stored | ConvertTo-Json -Compress) -Encoding ascii -NoNewline

    Assert-Equal 0 (Invoke-Aura @("-on")).ExitCode "exit code"
    Assert-Equal 45 ((Get-Content $channelState -Raw | ConvertFrom-Json).($first.Name).brightness) `
        "brightness taken from the preset"

    Set-Content -Path $presets -Value '[]' -Encoding ascii -NoNewline
    [void](Invoke-Aura @("-brightness", "100"))
}

Test-Case "a channel record without 'on' counts as on" {
    # What a file written before channels remembered their power state looks like.
    Set-Content -Path $channelState -Encoding utf8 `
        -Value '{"legacy|0":{"mode":1,"red":10,"green":20,"blue":30}}'
    Assert-Equal 0 (Invoke-Aura @("-on")).ExitCode "exit code"
    Assert-Equal $true ((Get-Content $state -Raw | ConvertFrom-Json).on) "board state"
}

Write-Host "Damaged files"

Test-Case "an unusable brightness in a channel record is ignored" {
    Set-Content -Path $channelState -Encoding ascii -NoNewline `
        -Value '{"legacy|0":{"mode":1,"red":10,"green":20,"blue":30,"brightness":"loud"}}'
    Assert-Equal 0 (Invoke-Aura @("-on")).ExitCode "exit code"
}

foreach ($junk in '[]', '5', '"x"', 'null', '{"broken":', 'not json') {
    Test-Case "a damaged channel-state.json is ignored ($junk)" {
        Set-Content -Path $channelState -Value $junk -Encoding ascii -NoNewline
        Assert-Equal 0 (Invoke-Aura @("-preset", "rainbow")).ExitCode "exit code"
    }

    Test-Case "a damaged presets.json is ignored ($junk)" {
        Set-Content -Path $presets -Value $junk -Encoding ascii -NoNewline
        Assert-Equal 0 (Invoke-Aura @("-on")).ExitCode "exit code"
    }

    Test-Case "a damaged settings.json still starts ($junk)" {
        Set-Content -Path $settings -Value $junk -Encoding ascii -NoNewline
        Assert-Equal 0 (Invoke-Aura @("-preset", "rainbow")).ExitCode "exit code"
    }
}

Test-Case "no temporary files are left behind" {
    $leftovers = @(Get-ChildItem $dataDir -Filter "*.tmp" -ErrorAction SilentlyContinue)
    Assert-Equal 0 $leftovers.Count "leftover .tmp files"
}

Test-Case "a custom preset naming a missing controller does not claim success" {
    Set-Content -Path $presets -Encoding utf8 -Value `
        '[{"name":"Ghost","entries":[{"deviceKey":"nope","channel":0,"label":"x","mode":1,"red":1,"green":2,"blue":3}]}]'
    Set-Content -Path $state -Encoding utf8 `
        -Value '{"on":true,"mode":1,"red":1,"green":2,"blue":3,"customPreset":"Ghost","brightness":100}'

    # Nothing matched, so the restore has to report a missing controller rather than pretend.
    Assert-Equal 3 (Invoke-Aura @("-on")).ExitCode "exit code"
}

Test-Case "a remembered preset that no longer exists falls back to a normal restore" {
    Set-Content -Path $presets -Value '[]' -Encoding ascii -NoNewline
    Set-Content -Path $state -Encoding utf8 `
        -Value '{"on":true,"mode":5,"red":255,"green":255,"blue":255,"customPreset":"Gone","brightness":100}'
    Assert-Equal 0 (Invoke-Aura @("-on")).ExitCode "exit code"
}

Test-Case "brightness survives an off/on round trip" {
    [void](Invoke-Aura @("-brightness", "40"))
    [void](Invoke-Aura @("-off"))
    [void](Invoke-Aura @("-on"))
    Assert-Equal 40 ((Get-Content $state -Raw | ConvertFrom-Json).brightness) "brightness"
    [void](Invoke-Aura @("-brightness", "100"))
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

}
finally {
    # Runs even when a test throws or the run is interrupted: leave the machine on the default
    # effect, switched on, with every one of the user's own files back exactly as it was.
    [void](Invoke-Aura @("-preset", "rainbow"))
    Restore-UserData
}

Write-Host ""
if ($failed -gt 0) {
    Write-Host "$failed test(s) failed"
    exit 1
}
Write-Host "all tests passed"
exit 0
