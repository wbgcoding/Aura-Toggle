# Regression suite for "AuraToggle.exe". Switches the mainboard lighting while it runs
# and leaves it turned on at the end.
#
#   powershell -ExecutionPolicy Bypass -File tests\aura-tests.ps1

param(
    [string]$Exe = (Join-Path $PSScriptRoot "..\dist\AuraToggle.exe")
)

$ErrorActionPreference = "Stop"
$dataDir = Join-Path $env:LOCALAPPDATA "aura-toggle"
$state = Join-Path $dataDir "state.json"
$settings = Join-Path $dataDir "settings.json"
$presets = Join-Path $dataDir "presets.json"
$channelState = Join-Path $dataDir "channel-state.json"
$channelNames = Join-Path $dataDir "channel-names.json"
$log = Join-Path $dataDir "log.txt"
$oldLog = Join-Path $dataDir "log.old.txt"
$failed = 0

if (-not (Test-Path $Exe)) {
    Write-Host "$Exe not found - run build.bat"
    exit 1
}

# Every file the tool writes, not just settings.json: the suite switches the lighting, changes
# presets and rewrites the per-channel records, so all of them have to come back exactly as they
# were. Getting this wrong silently destroys the user's presets and channel names.
$dataFiles = @("state.json", "settings.json", "presets.json", "channel-names.json", "channel-state.json")

# Kept apart from $dataFiles (that list is the user's own preferences); the log tests below
# delete, overwrite and rotate log.txt on purpose, so it needs the same backup/restore net or a
# suite run destroys whatever diagnosis someone had it open for.
$logFiles = @("log.txt", "log.old.txt")

New-Item -ItemType Directory -Force $dataDir | Out-Null

$backups = @{}
foreach ($name in $dataFiles + $logFiles) {
    $path = Join-Path $dataDir $name
    $backups[$name] = if (Test-Path $path) { Get-Content $path -Raw -Encoding UTF8 } else { $null }
}

function Restore-UserData {
    foreach ($name in $script:dataFiles + $script:logFiles) {
        $path = Join-Path $script:dataDir $name
        $saved = $script:backups[$name]
        if ($null -eq $saved) {
            Remove-Item $path -ErrorAction SilentlyContinue
        }
        else {
            # Windows PowerShell 5.1's "-Encoding UTF8" always adds a BOM, which AuraFiles.Write
            # never does - restoring through it left a byte the original file never had, despite
            # what this comment used to claim. WriteAllText with an explicit BOM-less UTF8Encoding
            # is the one way to actually match the app's own files byte for byte.
            $utf8NoBom = New-Object System.Text.UTF8Encoding $false
            [IO.File]::WriteAllText($path, $saved, $utf8NoBom)
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
            StdOut   = (Get-Content $out -Raw -Encoding UTF8)
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

function Get-RealChannelEntries {
    # channel-state.json can carry old records from other tools or manual testing. Only a real
    # HID path is a channel the running build actually wrote this call - anything else is stale
    # and would fail an assertion for a reason that has nothing to do with what it is checking.
    param($Json)
    $real = @($Json.PSObject.Properties | Where-Object { $_.Name.StartsWith('\\?\hid#') })
    if ($real.Count -eq 0) { throw "no real device channel records were found" }
    return $real
}

function Get-RealChannelKey {
    param($Json)
    return (Get-RealChannelEntries $Json)[0]
}

try {

Write-Host "Arguments"

Test-Case "unknown argument reports usage and exits 2" {
    $r = Invoke-Aura @("-bla")
    Assert-Equal 2 $r.ExitCode "exit code"
    # Matched language independently: the tool answers in German or English.
    if ($r.StdErr -notmatch "AuraToggle \[-on") { throw "no usage line on stderr" }
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

Test-Case "a transparent colour name exits 2" {
    Assert-Equal 2 (Invoke-Aura @("-preset", "static", "transparent")).ExitCode "exit code"
}

Test-Case "a system colour name exits 2" {
    Assert-Equal 2 (Invoke-Aura @("-preset", "static", "control")).ExitCode "exit code"
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

    foreach ($entry in Get-RealChannelEntries (Get-Content $channelState -Raw | ConvertFrom-Json)) {
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

    $entries = Get-RealChannelEntries (Get-Content $channelState -Raw | ConvertFrom-Json)

    foreach ($entry in $entries) {
        Assert-Equal $false $entry.Value.on "channel on"
        Assert-Equal 1 $entry.Value.mode "channel mode kept"
        Assert-Equal 32 $entry.Value.red "channel colour kept"
    }
}

Test-Case "-on brings every channel back on with its own colour" {
    [void](Invoke-Aura @("-on"))
    foreach ($entry in Get-RealChannelEntries (Get-Content $channelState -Raw | ConvertFrom-Json)) {
        Assert-Equal $true $entry.Value.on "channel on"
        Assert-Equal 32 $entry.Value.red "channel colour"
    }
    Assert-Equal $true ((Get-Content $state -Raw | ConvertFrom-Json).on) "board state"
}

Test-Case "a board-wide brightness hands every channel back to the board" {
    [void](Invoke-Aura @("-preset", "static", "#20C0FF"))
    Assert-Equal 0 (Invoke-Aura @("-brightness", "55")).ExitCode "exit code"

    Assert-Equal 55 ((Get-Content $state -Raw | ConvertFrom-Json).brightness) "board brightness"
    foreach ($entry in Get-RealChannelEntries (Get-Content $channelState -Raw | ConvertFrom-Json)) {
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
    $first = Get-RealChannelKey $records

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
    $first = Get-RealChannelKey (Get-Content $channelState -Raw | ConvertFrom-Json)
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

    try {
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
    }
    finally {
        # A failed assertion above must not leave this bogus preset active for every test after it.
        Set-Content -Path $presets -Value '[]' -Encoding ascii -NoNewline
        [void](Invoke-Aura @("-brightness", "100"))
    }
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

Test-Case "a forced write failure leaves no .tmp file behind" {
    $locked = [System.IO.File]::Open($state, [System.IO.FileMode]::Open, `
        [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        [void](Invoke-Aura @("-on"))
    }
    finally {
        $locked.Dispose()
    }

    Assert-Equal $false (Test-Path "$state.tmp") "leftover state.json.tmp"
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

    # The same forced error is what proves the log actually records one.
    $lastLine = Get-Content $log -Tail 1
    if ($lastLine -notmatch "ERROR") { throw "no error line logged: $lastLine" }
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

Test-Case "-toggle switches off when on, then on again" {
    [void](Invoke-Aura @("-on"))
    Assert-Equal 0 (Invoke-Aura @("-toggle")).ExitCode "exit code"
    Assert-Equal $false ((Get-Content $state -Raw | ConvertFrom-Json).on) "stored state after first toggle"
    Assert-Equal 0 (Invoke-Aura @("-toggle")).ExitCode "exit code"
    Assert-Equal $true ((Get-Content $state -Raw | ConvertFrom-Json).on) "stored state after second toggle"
}

Test-Case "-toggle on one channel follows only that channel's own state" {
    [void](Invoke-Aura @("-on"))
    [void](Invoke-Aura @("-off", "-channel", "1"))
    Assert-Equal 0 (Invoke-Aura @("-toggle", "-channel", "1")).ExitCode "exit code"

    $first = Get-RealChannelKey (Get-Content $channelState -Raw | ConvertFrom-Json)
    Assert-Equal $true $first.Value.on "channel 1 back on"
}

Write-Host "Targeting"

Test-Case "-channel accepts the flat number from -list" {
    Assert-Equal 0 (Invoke-Aura @("-preset", "static", "red", "-channel", "1")).ExitCode "exit code"
}

Test-Case "-channel accepts the controller.channel form from -list" {
    Assert-Equal 0 (Invoke-Aura @("-preset", "static", "lime", "-channel", "1.2")).ExitCode "exit code"
}

Test-Case "-channel accepts a default channel name" {
    Assert-Equal 0 (Invoke-Aura @("-preset", "static", "blue", "-channel", "ARGB 1")).ExitCode "exit code"
}

Test-Case "-channel accepts a name of the user's own" {
    # The real device key is whatever the app itself just wrote to channel-state.json - there is
    # no CLI way to learn it beforehand, so read it back rather than guessing at one. It is a HID
    # path with backslashes, which need escaping to land in valid JSON.
    [void](Invoke-Aura @("-on"))
    $first = Get-RealChannelKey (Get-Content $channelState -Raw | ConvertFrom-Json)
    $deviceKey = ($first.Name -split '\|')[0]
    $escapedKey = $deviceKey -replace '\\', '\\'
    Set-Content -Path $channelNames -Encoding utf8 -Value "{`"$escapedKey|0`":`"MyHeader`"}"
    Assert-Equal 0 (Invoke-Aura @("-preset", "static", "yellow", "-channel", "MyHeader")).ExitCode "exit code"
}

Test-Case "-channel with an unknown target exits 2 and lists the possible ones" {
    $r = Invoke-Aura @("-preset", "static", "red", "-channel", "does-not-exist")
    Assert-Equal 2 $r.ExitCode "exit code"
    if ($r.StdErr -notmatch "1\.1") { throw "no candidate list on stderr" }
}

Test-Case "-device targets a whole controller" {
    Assert-Equal 0 (Invoke-Aura @("-on", "-device", "1")).ExitCode "exit code"
}

Test-Case "an unknown -device number exits 2" {
    Assert-Equal 2 (Invoke-Aura @("-on", "-device", "99")).ExitCode "exit code"
}

Test-Case "-status prints the board and one line per channel" {
    $r = Invoke-Aura @("-status")
    Assert-Equal 0 $r.ExitCode "exit code"
    if ($r.StdOut -notmatch "(?m)^Board\t") { throw "no board line: $($r.StdOut)" }
    if ($r.StdOut -notmatch "1\.1") { throw "no channel line: $($r.StdOut)" }
}

Test-Case "-status --json prints one parseable line" {
    [void](Invoke-Aura @("-preset", "static", "#20C0FF"))
    $r = Invoke-Aura @("-status", "--json")
    Assert-Equal 0 $r.ExitCode "exit code"

    $lines = $r.StdOut.Trim() -split "`r?`n"
    Assert-Equal 1 $lines.Count "line count"

    $parsed = $lines[0] | ConvertFrom-Json
    Assert-Equal $true $parsed.on "on"
    Assert-Equal "static" $parsed.effect "effect"
    Assert-Equal "#20C0FF" $parsed.colour "colour"
    if ($parsed.channels.Count -lt 1) { throw "no channels in JSON status" }
    if ($null -eq $parsed.channels[0].device) { throw "channel entry missing device" }
}

Test-Case "--version prints only the version number" {
    $r = Invoke-Aura @("--version")
    Assert-Equal 0 $r.ExitCode "exit code"
    if ($r.StdOut.Trim() -notmatch '^\d+\.\d+\.\d+') { throw "unexpected version output: '$($r.StdOut)'" }
}

Test-Case "-help lists every documented command" {
    $r = Invoke-Aura @("-help")
    Assert-Equal 0 $r.ExitCode "exit code"

    # Every command the help claims to document has to actually appear in it - the list is easy
    # to extend in code and forget here, which is how a flag ends up undocumented.
    foreach ($flag in @("-on", "-off", "-toggle", "-preset", "-brightness", "-custom", "-list", "-status",
                        "--json", "--version", "-help", "-device", "-channel")) {
        if ($r.StdOut -notmatch [regex]::Escape($flag)) { throw "-help does not mention $flag" }
    }

    # The review harness is not an end-user flag and must stay out of the published help.
    if ($r.StdOut -match "-review") { throw "-help must not advertise -review" }

    # The brightness range comes from the code's own constants, so a change there cannot leave a
    # stale number in the help text.
    if ($r.StdOut -notmatch "10-100") { throw "-help does not show the real brightness range" }
}

Test-Case "-help is reachable as -h, --help and /?" {
    foreach ($form in @("-h", "--help", "/?")) {
        $r = Invoke-Aura @($form)
        Assert-Equal 0 $r.ExitCode "exit code for $form"
        if ($r.StdOut -notmatch "Usage: AuraToggle.exe") { throw "$form did not print the help" }
    }
}

Test-Case "-help rejects a targeting option that does not apply to it" {
    $r = Invoke-Aura @("-help", "-channel", "1")
    Assert-Equal 2 $r.ExitCode "exit code"
}

Test-Case "-custom applies a saved preset by name" {
    $first = Get-RealChannelKey (Get-Content $channelState -Raw | ConvertFrom-Json)
    $deviceKey = ($first.Name -split '\|')[0]
    $escapedKey = $deviceKey -replace '\\', '\\'
    Set-Content -Path $presets -Encoding utf8 -Value `
        "[{`"name`":`"CliPreset`",`"entries`":[{`"deviceKey`":`"$escapedKey`",`"channel`":0,`"label`":`"x`",`"mode`":1,`"red`":10,`"green`":20,`"blue`":30}]}]"
    Assert-Equal 0 (Invoke-Aura @("-custom", "CliPreset")).ExitCode "exit code"
    Assert-Equal "CliPreset" ((Get-Content $state -Raw | ConvertFrom-Json).customPreset) "active preset"
}

Test-Case "-custom applies a saved preset by its number from -list" {
    $first = Get-RealChannelKey (Get-Content $channelState -Raw | ConvertFrom-Json)
    $deviceKey = ($first.Name -split '\|')[0]
    $escapedKey = $deviceKey -replace '\\', '\\'
    Set-Content -Path $presets -Encoding utf8 -Value `
        "[{`"name`":`"CliPreset`",`"entries`":[{`"deviceKey`":`"$escapedKey`",`"channel`":0,`"label`":`"x`",`"mode`":1,`"red`":10,`"green`":20,`"blue`":30}]}]"
    Assert-Equal 0 (Invoke-Aura @("-custom", "1")).ExitCode "exit code"
    Assert-Equal "CliPreset" ((Get-Content $state -Raw | ConvertFrom-Json).customPreset) "active preset"
}

Test-Case "-custom with an unknown name exits 2" {
    Assert-Equal 2 (Invoke-Aura @("-custom", "does-not-exist")).ExitCode "exit code"
}

Test-Case "-custom with a number past the end of the list exits 2" {
    Assert-Equal 2 (Invoke-Aura @("-custom", "99")).ExitCode "exit code"
}

Test-Case "-custom rejects -device, since a preset names its own channels" {
    # Regression: -device/-channel used to be extracted before the command was even looked at, so
    # this silently applied the preset board-wide and exited 0 as if -device had done something.
    Assert-Equal 2 (Invoke-Aura @("-custom", "CliPreset", "-device", "1")).ExitCode "exit code"
}

Test-Case "-list rejects -channel, since it has nothing to target" {
    Assert-Equal 2 (Invoke-Aura @("-list", "-channel", "1")).ExitCode "exit code"
}

Test-Case "-list names a saved custom preset" {
    $first = Get-RealChannelKey (Get-Content $channelState -Raw | ConvertFrom-Json)
    $deviceKey = ($first.Name -split '\|')[0]
    $escapedKey = $deviceKey -replace '\\', '\\'
    Set-Content -Path $presets -Encoding utf8 -Value `
        "[{`"name`":`"ListedPreset`",`"entries`":[{`"deviceKey`":`"$escapedKey`",`"channel`":0,`"label`":`"x`",`"mode`":1,`"red`":10,`"green`":20,`"blue`":30}]}]"
    $r = Invoke-Aura @("-list")
    Assert-Equal 0 $r.ExitCode "exit code"
    if ($r.StdOut -notmatch "Presets:") { throw "no Presets: block on stdout" }
    if ($r.StdOut -notmatch "ListedPreset") { throw "preset name missing from -list" }
}

Test-Case "-list omits the Presets block when none are saved" {
    Set-Content -Path $presets -Value "[]" -Encoding ascii -NoNewline
    $r = Invoke-Aura @("-list")
    Assert-Equal 0 $r.ExitCode "exit code"
    if ($r.StdOut -match "Presets:") { throw "Presets: block shown with no saved presets" }
}

Write-Host "Log"

Test-Case "a start line is written to the log" {
    Remove-Item $log -ErrorAction SilentlyContinue
    [void](Invoke-Aura @("-on"))
    if (-not (Test-Path $log)) { throw "log.txt was not created" }
    # Not the last line: -on always logs its device discovery right after the start line.
    if (-not (Select-String -Path $log -Pattern "Start" -Quiet)) { throw "no start line in the log" }
}

Test-Case "the log rotates past 200 KB" {
    Remove-Item $oldLog -ErrorAction SilentlyContinue
    Set-Content -Path $log -Encoding ascii -NoNewline -Value ("x" * 210000)
    [void](Invoke-Aura @("-on"))
    if (-not (Test-Path $oldLog)) { throw "log.txt was not rotated to log.old.txt" }
    if ((Get-Item $log).Length -gt 1000) { throw "log.txt was not restarted after rotation" }
}

Write-Host "Shortcuts"

# Do not call the loop variable $name: inside Test-Case that would resolve to its own
# $Name parameter, which is how this test first went looking for the wrong file.
foreach ($linkName in "Aura On.lnk", "Aura Off.lnk") {
    Test-Case "'$linkName' exists and carries a relative path" {
        $link = Join-Path (Split-Path $Exe) $linkName
        if (-not (Test-Path $link)) { throw "shortcut missing at $link" }

        $bytes = [IO.File]::ReadAllBytes($link)
        $flags = [BitConverter]::ToUInt32($bytes, 20)
        if (($flags -band 0x08) -eq 0) { throw "no relative path stored, the folder cannot be moved" }

        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($link)
        $expected = if ($linkName -eq "Aura On.lnk") { "-on" } else { "-off" }
        Assert-Equal $expected $shortcut.Arguments "arguments"
        Assert-Equal (Resolve-Path $Exe).Path $shortcut.TargetPath "target"
    }
}

Write-Host "Window"

Test-Case "-autostart shows no window, but the process stays running and can be stopped" {
    $process = Start-Process $Exe -ArgumentList "-autostart" -PassThru
    Start-Sleep -Seconds 3
    $process.Refresh()
    try {
        if ($process.HasExited) { throw "process exited immediately" }
        if ($process.MainWindowHandle -ne 0) { throw "a window was shown" }
    }
    finally {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
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
    $leftover = @(Get-Process -Name "AuraToggle" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $target })
    Assert-Equal 0 $leftover.Count "leftover processes"
}

# A custom preset is a bundle of different effects, so the button used to animate whatever
# board-wide effect happened to be set before the preset took over - a rainbow wash under a preset
# that runs no rainbow anywhere. It now animates the effect most of the preset's channels run, in
# the colour of the first channel running it. Two channels breathing red and green, one static
# blue, one chase yellow: breathing wins on count, and the colour is the first breathing channel's.
Test-Case "the button animates a custom preset's most used effect" {
    $report = Join-Path $env:TEMP "aura-layout.txt"
    Remove-Item $report -Force -ErrorAction SilentlyContinue

    $bundle = @'
[{"name":"Suite Mix","entries":[
 {"device":"review-1","channel":0,"label":"Onboard","mode":2,"red":255,"green":0,"blue":0,"brightness":100},
 {"device":"review-1","channel":1,"label":"ARGB 1","mode":1,"red":0,"green":0,"blue":255,"brightness":100},
 {"device":"review-1","channel":2,"label":"ARGB 2","mode":2,"red":0,"green":255,"blue":0,"brightness":100},
 {"device":"review-2","channel":0,"label":"Onboard","mode":9,"red":255,"green":255,"blue":0,"brightness":100}]}]
'@

    # Both files are on the suite's backup list, so whatever the machine had comes back at the end.
    Set-Content -Path $presets -Value $bundle -Encoding utf8
    Set-Content -Path $state -Encoding utf8 `
        -Value '{"on":true,"mode":5,"red":255,"green":255,"blue":255,"brightness":100,"customPreset":"Suite Mix"}'

    $process = Start-Process $Exe -ArgumentList "-review", "layout" -PassThru
    try {
        Start-Sleep -Seconds 6
        if (-not (Test-Path $report)) { throw "no layout report was written" }

        $line = @(Get-Content $report | Where-Object { $_ -match "^button" }) | Select-Object -Last 1
        if (-not $line) { throw "the report says nothing about the button" }

        # Rainbow is the board-wide effect in state.json above - the one the button must NOT show.
        if ($line -notmatch "effect=breathing") { throw "expected breathing:`n$line" }
        if ($line -notmatch "colour=#FF0000") { throw "expected the first breathing channel's red:`n$line" }
    }
    finally {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        [void]$process.WaitForExit(5000)
        Remove-Item $report -Force -ErrorAction SilentlyContinue
    }
}

# The complaint that came back three times: after the window is moved to a monitor at another
# display scale, the effect list is handed less room than its own longest entry needs and cuts the
# text off. "-review layout <scale>" puts the window through exactly that move, so it is a check
# here rather than something only a second physical monitor could show.
# The popups measure their own spacing when they open. One left open across a display-scale change
# has to put it back at the new scale, which is what these three paddings prove: 14/12, 16/14 and
# 16/16 at 96 dpi, so exactly double at 200 %.
foreach ($surface in @(@{ Name = "settings"; Padding = "28,24" }, @{ Name = "editor"; Padding = "32,28" }, @{ Name = "update"; Padding = "32,32" })) {
    Test-Case "the $($surface.Name) popup rescales when the display scale changes" {
        $report = Join-Path $env:TEMP "aura-layout.txt"
        Remove-Item $report -Force -ErrorAction SilentlyContinue

        $process = Start-Process $Exe -ArgumentList "-review", $surface.Name, "200" -PassThru
        try {
            Start-Sleep -Seconds 5
            if (-not (Test-Path $report)) { throw "no report was written" }

            $last = (Get-Content $report -Raw) -split "--- " | Select-Object -Last 1
            if ($last -notmatch "dpi\s+192") { throw "the popup never reached 192 dpi:`n$last" }
            if ($last -notmatch "padding\s+$($surface.Padding)") {
                throw "padding did not follow the scale (expected $($surface.Padding)):`n$last"
            }
        }
        finally {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            [void]$process.WaitForExit(5000)
            Remove-Item $report -Force -ErrorAction SilentlyContinue
        }
    }
}

# The window is transparent until it has measured itself, but a controller that takes longer to
# answer than the reveal backstop waits shows it anyway - and then the channel selector arrives and
# widens it while the user is looking at it. The remembered width is what covers that gap, so it has
# to survive the constructor's own Render(), which measures a row that has no selector in it yet.
# First run writes the width down on close, second run has to open at it.
Test-Case "the window opens at the width it will keep, before anything is discovered" {
    $report = Join-Path $env:TEMP "aura-layout.txt"

    function Invoke-LayoutRun {
        Remove-Item $report -Force -ErrorAction SilentlyContinue
        $process = Start-Process $Exe -ArgumentList "-review", "layout" -PassThru
        try {
            Start-Sleep -Seconds 4
            if (-not (Test-Path $report)) { throw "no layout report was written" }
            $text = Get-Content $report -Raw
            # Closed rather than killed: the width is written down on the way out.
            [void]$process.CloseMainWindow()
            [void]$process.WaitForExit(3000)
            return $text
        }
        finally {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            [void]$process.WaitForExit(5000)
        }
    }

    try {
        [void](Invoke-LayoutRun)
        $text = Invoke-LayoutRun

        $blocks = @($text -split "--- ")
        $before = @($blocks | Where-Object { $_ -match "^before shown" }) | Select-Object -Last 1
        $opened = @($blocks | Where-Object { $_ -match "^as opened" }) | Select-Object -Last 1
        if (-not $before -or -not $opened) { throw "the report is missing one of the two passes" }
        if ($before -notmatch "clientsize\s+(\d+)x") { throw "no client size before the window was shown" }
        $start = [int]$Matches[1]
        if ($opened -notmatch "clientsize\s+(\d+)x") { throw "no client size once open" }
        $final = [int]$Matches[1]

        if ($start -ne $final) {
            throw "shown at $start px, then grew to $final px - visible if discovery outlasts the reveal backstop"
        }
    }
    finally {
        Remove-Item $report -Force -ErrorAction SilentlyContinue
    }
}

# Out to another display and back has to land on the size it started at - the window is meant to
# follow the scale, not to grow a little on every trip. Two stops at the same scale with a
# different one in between, so the check holds whatever scale this machine itself runs at.
Test-Case "the window comes back to the same size after a round trip" {
    $report = Join-Path $env:TEMP "aura-layout.txt"
    Remove-Item $report -Force -ErrorAction SilentlyContinue

    $process = Start-Process $Exe -ArgumentList "-review", "layout", "150,200,150" -PassThru
    try {
        Start-Sleep -Seconds 20
        if (-not (Test-Path $report)) { throw "no layout report was written" }

        $blocks = @((Get-Content $report -Raw) -split "--- " | Where-Object { $_ -match "move to 150%" })
        if ($blocks.Count -lt 2) { throw "the report has no two stops at 150 %" }

        $widths = @($blocks[0], $blocks[-1] | ForEach-Object {
            if ($_ -notmatch "clientsize\s+(\d+)x") { throw "no client size in:`n$_" }
            [int]$Matches[1]
        })

        if ($widths[0] -ne $widths[1]) {
            throw "width $($widths[0]) px on the way out, $($widths[1]) px on the way back"
        }
    }
    finally {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        [void]$process.WaitForExit(5000)
        Remove-Item $report -Force -ErrorAction SilentlyContinue
    }
}

# The complaint before this fix: the layout only caught up once the mouse button came back up,
# so a window dragged onto a second monitor sat clipped for as long as the drag lasted.
# "-review layout 200" simulates WM_DPICHANGED arriving while the window is still between
# WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE - the 250 ms pass ("dragging") already has to show the
# new dpi and no overflow, not just the later ones taken after the simulated release.
Test-Case "the layout catches up mid-drag, not only after release" {
    $report = Join-Path $env:TEMP "aura-layout.txt"
    Remove-Item $report -Force -ErrorAction SilentlyContinue

    $process = Start-Process $Exe -ArgumentList "-review", "layout", "200" -PassThru
    try {
        Start-Sleep -Seconds 5
        if (-not (Test-Path $report)) { throw "no layout report was written" }

        $dragging = (Get-Content $report -Raw) -split "--- " |
            Where-Object { $_ -match "^250 ms after the move to 200% \(dragging\)" }
        if (-not $dragging) { throw "no 250 ms (dragging) pass in the report" }

        if ($dragging -notmatch "dpi\s+192") { throw "still the old dpi while dragging:`n$dragging" }
        if ($dragging -match "CLIPPED|OVERFLOW") { throw "clipped while dragging:`n$dragging" }
    }
    finally {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        [void]$process.WaitForExit(5000)
        Remove-Item $report -Force -ErrorAction SilentlyContinue
    }
}

# The same window, only scaled: twice the display scale is twice the width, give or take rounding.
# Measuring text against the display scale as well as through its own font made it half again as
# wide as it should be on a second monitor, with everything in the row stretched to fill the room.
Test-Case "the window is only as wide as the display scale asks for" {
    $report = Join-Path $env:TEMP "aura-layout.txt"
    Remove-Item $report -Force -ErrorAction SilentlyContinue

    $process = Start-Process $Exe -ArgumentList "-review", "layout", "100,200" -PassThru
    try {
        Start-Sleep -Seconds 14
        if (-not (Test-Path $report)) { throw "no layout report was written" }

        $blocks = @((Get-Content $report -Raw) -split "--- ")
        $at100 = @($blocks | Where-Object { $_ -match "move to 100%" }) | Select-Object -Last 1
        $at200 = @($blocks | Where-Object { $_ -match "move to 200%" }) | Select-Object -Last 1
        if (-not $at100 -or -not $at200) { throw "the report is missing one of the two stops" }
        if ($at100 -notmatch "clientsize\s+(\d+)x") { throw "no client size at 100 %" }
        $narrow = [int]$Matches[1]
        if ($at200 -notmatch "clientsize\s+(\d+)x") { throw "no client size at 200 %" }
        $wide = [int]$Matches[1]

        $ratio = $wide / $narrow
        if ([Math]::Abs($ratio - 2) -gt 0.03) {
            throw "$narrow px at 100 % but $wide px at 200 % - ratio $([Math]::Round($ratio, 3)), expected 2"
        }
    }
    finally {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        [void]$process.WaitForExit(5000)
        Remove-Item $report -Force -ErrorAction SilentlyContinue
    }
}

foreach ($scale in 100, 125, 200) {
    Test-Case "the layout survives a move to a display at $scale %" {
        $report = Join-Path $env:TEMP "aura-layout.txt"
        Remove-Item $report -Force -ErrorAction SilentlyContinue

        $process = Start-Process $Exe -ArgumentList "-review", "layout", $scale -PassThru
        try {
            Start-Sleep -Seconds 5
            if (-not (Test-Path $report)) { throw "no layout report was written" }

            # Only the last pass matters: the earlier ones are the window on its way there.
            $last = (Get-Content $report -Raw) -split "--- " | Select-Object -Last 1
            if ($last -match "CLIPPED") {
                throw "clipped after the move:`n$last"
            }
            if ($last -notmatch "clientsize\s+(\d+)x" ) { throw "no client size in:`n$last" }
            $width = [int]$Matches[1]
            if ($last -notmatch "width bounds\s+min=\d+ max=(\d+)") { throw "no width bounds in:`n$last" }
            $max = [int]$Matches[1]
            if ($last -notmatch "effects\s+w=(\d+) preferred=(\d+)") {
                throw "the report says nothing about the effect list:`n$last"
            }

            # At the maximum width a shortened entry is the intended result, not a defect: the
            # window is capped on purpose rather than growing with a forty-character preset name.
            if ($width -lt $max -and [int]$Matches[1] -lt [int]$Matches[2]) {
                throw "effect list got $($Matches[1]) px for text needing $($Matches[2]) px"
            }
        }
        finally {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            [void]$process.WaitForExit(5000)
            Remove-Item $report -Force -ErrorAction SilentlyContinue
        }
    }
}

# The complaint: with the window snapped flush against the screen's right edge, the settings panel
# used to land noticeably short of the toggle button, leaving a strip of it visible past the panel's
# own right edge - PopupForm.OnScreen's screen-edge safety margin was eating into the anchor, and
# the anchor itself stopped at the gear rather than the window edge. Later complaint, on a
# multi-monitor layout: the panel landed roughly centred under the gear with most of it hanging off
# the window, because SettingsPopup.Open resolved the screen from a point already shifted left by
# the panel's own width, which can land on the wrong monitor. Last one, again on a second monitor:
# the panel is fitted at the scale of the display it was created on and re-fitted at the owner's,
# and only its left edge was ever placed - so the wider panel grew rightwards and sat centred under
# the gear. "-review gear" snaps the window to the edge itself, reopens the panel at each scale and
# then re-fits it half again as wide, so all three are a check here rather than something only a
# real monitor edge or a specific multi-monitor layout could show; DescribeSettingsAnchor itself
# flags either failure as CLIPPED or OVERFLOW.
Test-Case "the settings panel sits a few px inside the window edge and covers the button whole when snapped to the edge" {
    $report = Join-Path $env:TEMP "aura-gear.txt"
    Remove-Item $report -Force -ErrorAction SilentlyContinue

    $process = Start-Process $Exe -ArgumentList "-review", "gear", "125,150,200" -PassThru
    try {
        Start-Sleep -Seconds 8
        if (-not (Test-Path $report)) { throw "no gear report was written" }

        $blocks = @((Get-Content $report -Raw) -split "--- " | Where-Object { $_.Trim() })
        if ($blocks.Count -lt 8) {
            throw "expected 8 blocks (as opened, 125%, 150%, 200%, each also refitted), got $($blocks.Count):`n$($blocks -join "`n")"
        }

        foreach ($block in $blocks) {
            if ($block -match "CLIPPED|OVERFLOW") { throw "panel drifted from the window edge:`n$block" }
            if ($block -notmatch "panel\.Right - window\.Right\s+(-?\d+)\s+\(expected (-?\d+)\)") {
                throw "no panel.Right - window.Right line in:`n$block"
            }
            if ([int]$Matches[1] -gt 0) {
                throw "panel.Right - window.Right was $($Matches[1]), past the window's right edge:`n$block"
            }
            if ($block -notmatch "panel\.Right - toggle\.Right\s+(-?\d+)") {
                throw "no panel.Right - toggle.Right line in:`n$block"
            }
            if ([int]$Matches[1] -le 0) {
                throw "panel.Right - toggle.Right was $($Matches[1]), a strip of the button stays visible:`n$block"
            }
        }
    }
    finally {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        [void]$process.WaitForExit(5000)
        Remove-Item $report -Force -ErrorAction SilentlyContinue
    }
}

# The complaint: the F2/Delete and effect-hint tooltip still showed as the plain white system box
# in dark mode, even though its BackColor/ForeColor were already set to match - Windows ignores
# both while visual styles are active unless the tooltip draws itself. "-review tip" renders the
# same Draw handler onto a bitmap instead of a live system tooltip, which only ever paints for the
# foreground window and cannot be relied on headless; the corner pixel is the panel's own fill
# colour with nothing else drawn over it yet, so it stands in for "did this draw itself at all".
Test-Case "the effect-list tooltip actually draws in the current theme" {
    $png = Join-Path $env:TEMP "aura-tip.png"
    Remove-Item $png -Force -ErrorAction SilentlyContinue

    $process = Start-Process $Exe -ArgumentList "-review", "tip" -PassThru
    try {
        Start-Sleep -Seconds 3
        if (-not (Test-Path $png)) { throw "no aura-tip.png was written" }

        Add-Type -AssemblyName System.Drawing
        $bmp = [System.Drawing.Bitmap]::new($png)
        try {
            $corner = $bmp.GetPixel(2, 2)
        }
        finally {
            $bmp.Dispose()
        }

        # Same registry value and same "missing means light" default as Theme.AppsUseDarkTheme.
        $appsUseLight = (Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" `
            -Name AppsUseLightTheme -ErrorAction SilentlyContinue).AppsUseLightTheme
        $dark = $appsUseLight -eq 0
        $expected = if ($dark) { [System.Drawing.Color]::FromArgb(45, 47, 51) } else { [System.Drawing.Color]::White }

        if ($corner.R -ne $expected.R -or $corner.G -ne $expected.G -or $corner.B -ne $expected.B) {
            $theme = if ($dark) { "dark" } else { "light" }
            throw "tooltip background was $($corner.R),$($corner.G),$($corner.B), expected $($expected.R),$($expected.G),$($expected.B) ($theme theme)"
        }
    }
    finally {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        [void]$process.WaitForExit(5000)
        Remove-Item $png -Force -ErrorAction SilentlyContinue
    }
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
