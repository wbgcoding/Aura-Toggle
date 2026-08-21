# Static checks for Aura Toggle - the half of the suite that needs no controller and no VM.
# "aura-tests.ps1" drives the real hardware; this one only reads the tree.
#
#   powershell -ExecutionPolicy Bypass -File tests\static-checks.ps1
#
# Prints one line per finding as "file:line - what is wrong". Exit code 0 when everything is
# clean, 1 as soon as one check fails, so it can gate a build.

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$findings = 0

function Add-Finding {
    param([string]$Where, [string]$What)
    Write-Host "  $Where - $What"
    $script:findings++
}

function Write-Section {
    param([string]$Name)
    Write-Host ""
    Write-Host "== $Name =="
}

function Get-SourceLines {
    param([string[]]$Paths)
    foreach ($path in $Paths) {
        $relative = $path.Substring($root.Length + 1)
        $number = 0
        foreach ($line in [IO.File]::ReadAllLines($path)) {
            $number++
            [pscustomobject]@{ File = $relative; Number = $number; Text = $line }
        }
    }
}

$csharp = @(Get-ChildItem (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName })
$sources = @(Get-SourceLines $csharp)

# --------------------------------------------------------------------------------------------
Write-Section "Resource keys"

function Get-ResxKeys {
    param([string]$Path)
    $document = New-Object System.Xml.XmlDocument
    $document.Load($Path)
    $keys = @{}
    foreach ($node in $document.SelectNodes("/root/data")) {
        $keys[$node.name] = $node.value
    }
    $keys
}

# Every language file the application embeds, read out of the one table in Strings.cs that
# declares them - a language added there and nowhere else is caught here rather than at run time,
# where it would only show up as English text in a Japanese window.
$bundles = @()
$stringsCs = Get-Content (Join-Path $root "src\Strings.cs") -Raw
foreach ($match in [regex]::Matches($stringsCs, '\("([a-z]{2})",\s*"(Strings[A-Za-z]*)"\)')) {
    $bundles += [pscustomobject]@{
        Code = $match.Groups[1].Value
        Name = "src\" + $match.Groups[2].Value + ".resx"
        Path = Join-Path $root ("src\" + $match.Groups[2].Value + ".resx")
    }
}

if ($bundles.Count -lt 2) {
    Add-Finding "src\Strings.cs" "the language table could not be read - resource checks skipped"
}

$missingFile = @($bundles | Where-Object { -not (Test-Path $_.Path) })
foreach ($bundle in $missingFile) { Add-Finding $bundle.Name "declared in Strings.cs but not on disk" }
$bundles = @($bundles | Where-Object { Test-Path $_.Path })

foreach ($bundle in $bundles) {
    $bundle | Add-Member -NotePropertyName Keys -NotePropertyValue (Get-ResxKeys $bundle.Path)
}

$english = $bundles[0].Keys
$englishPath = $bundles[0].Path
Write-Host "  $($bundles.Count) languages, $($english.Count) keys in $($bundles[0].Name)"

# Placeholders are what actually breaks: a translation that loses "{0}" prints a sentence with a
# hole in it, and one that invents "{2}" throws a FormatException at the user.
function Get-Placeholders {
    param([string]$Text)
    $found = @([regex]::Matches($Text, '\{\d+\}') | ForEach-Object { $_.Value } | Sort-Object -Unique)
    ($found -join ",")
}

$names = @{}
foreach ($bundle in $bundles) {
    foreach ($key in $english.Keys) {
        if (-not $bundle.Keys.ContainsKey($key)) {
            Add-Finding $bundle.Name "key '$key' is missing"
            continue
        }
        if ([string]::IsNullOrWhiteSpace($bundle.Keys[$key])) {
            Add-Finding $bundle.Name "key '$key' has no text"
        }
        $wanted = Get-Placeholders $english[$key]
        $have = Get-Placeholders $bundle.Keys[$key]
        if ($wanted -ne $have) {
            Add-Finding $bundle.Name "key '$key' uses placeholders '$have' where English uses '$wanted'"
        }
    }
    foreach ($key in $bundle.Keys.Keys) {
        if (-not $english.ContainsKey($key)) { Add-Finding $bundle.Name "key '$key' exists in no other language" }
    }

    # The settings list names every language in itself, so two files carrying the same name means
    # one of them was copied and never translated.
    $own = $bundle.Keys["LanguageName"]
    if ([string]::IsNullOrWhiteSpace($own)) {
        Add-Finding $bundle.Name "no LanguageName - the settings list would show an empty entry"
    }
    elseif ($names.ContainsKey($own)) {
        Add-Finding $bundle.Name "calls itself '$own', the same as $($names[$own])"
    }
    else { $names[$own] = $bundle.Name }
}

# Keys reached through Strings.Get(...) plus the two families built at run time: every preset's
# ResourceKey out of AuraPresets.All, and the "EffectHint<suffix>" that AuraPreset.HintText
# derives from it. A key missing here is a caption that falls back to its own key name on screen.
$used = New-Object System.Collections.Generic.HashSet[string]
foreach ($match in [regex]::Matches((Get-Content (Join-Path $root "src\Strings.cs") -Raw), 'Get\("([A-Za-z0-9_]+)"\)')) {
    [void]$used.Add($match.Groups[1].Value)
}
foreach ($file in Get-ChildItem (Join-Path $root "src") -Filter *.cs) {
    foreach ($match in [regex]::Matches((Get-Content $file.FullName -Raw), 'InLanguage\("([A-Za-z0-9_]+)"')) {
        [void]$used.Add($match.Groups[1].Value)
    }
}
foreach ($match in [regex]::Matches((Get-Content (Join-Path $root "src\AuraPresets.cs") -Raw), 'new AuraPreset\("[^"]+",\s*\d+,\s*"(Preset[A-Za-z0-9]+)"')) {
    $key = $match.Groups[1].Value
    [void]$used.Add($key)
    [void]$used.Add("EffectHint" + $key.Substring("Preset".Length))
}

foreach ($key in $used) {
    foreach ($bundle in $bundles) {
        if (-not $bundle.Keys.ContainsKey($key)) { Add-Finding $bundle.Name "key '$key' is used in code but missing" }
    }
}
foreach ($key in $english.Keys) {
    if (-not $used.Contains($key)) { Add-Finding $bundles[0].Name "key '$key' is never used" }
}

# --------------------------------------------------------------------------------------------
Write-Section "Hardcoded interface text"

# Captions belong in the two .resx files. The window title is the one deliberate exception: it is
# a product name, identical in both languages, and the "-review" surfaces below are development
# aids that never ship a caption to a user.
$textAllowList = @("Aura Toggle", "Aura Toggle - No controller found", "")
$textPatterns = @(
    '\.Text\s*=\s*[$]?"',
    'AccessibleName\s*=\s*[$]?"',
    'SetToolTip\([^,]+,\s*[$]?"',
    'MessageBox\.Show\(\s*[$]?"'
)

foreach ($line in $sources) {
    if ($line.File -eq "src\Strings.cs") { continue }
    foreach ($pattern in $textPatterns) {
        if ($line.Text -notmatch $pattern) { continue }
        $literal = [regex]::Match($line.Text, '"([^"]*)"').Groups[1].Value
        if ($textAllowList -contains $literal) { continue }
        # A caption assembled from Strings.* pieces is translated already; so is a literal that
        # carries no letters at all (a glyph, a separator, a format placeholder).
        if ($literal -match "\{" -or $literal -notmatch "\p{L}") { continue }
        Add-Finding "$($line.File):$($line.Number)" "hardcoded text `"$literal`""
    }
}

# --------------------------------------------------------------------------------------------
Write-Section "Fixed pixel sizes"

# WinForms auto scaling is switched off in this project (AutoScaleDimensions is never set), so a
# pixel distance that does not go through Scaled() stays 100 % wide on a 200 % display.
# "DesignSize" is the exception by definition: it is the unscaled base a control scales from, and
# a line marked "unscaled:" says in its own words why it stays raw.
foreach ($line in $sources) {
    if ($line.Text -match "Scaled\(" -or $line.Text -match "DesignSize" -or $line.Text -match "unscaled:") { continue }
    if ($line.Text -match "new (Padding|Size)\(\s*[2-9]\d*" -or $line.Text -match "\.(Width|Height)\s*=\s*[2-9]\d*") {
        Add-Finding "$($line.File):$($line.Number)" "fixed pixel size without Scaled(): $($line.Text.Trim())"
    }
}

# --------------------------------------------------------------------------------------------
Write-Section "Versions"

$projectVersion = [regex]::Match((Get-Content (Join-Path $root "AuraToggle.csproj") -Raw), "<Version>([^<]+)</Version>").Groups[1].Value
$setupVersion = [regex]::Match((Get-Content (Join-Path $root "installer\aura.iss") -Raw), '#define AppVersion "([^"]+)"').Groups[1].Value
$changelog = Get-Content (Join-Path $root "CHANGELOG.md")
$changelogVersion = ""
foreach ($line in $changelog) {
    $match = [regex]::Match($line, "^## \[(\d+\.\d+\.\d+)\]")
    if ($match.Success) { $changelogVersion = $match.Groups[1].Value; break }
}

Write-Host "  csproj $projectVersion, installer $setupVersion, changelog $changelogVersion"
if ($projectVersion -ne $setupVersion) { Add-Finding "installer\aura.iss" "AppVersion $setupVersion does not match the project's $projectVersion" }
if ($projectVersion -ne $changelogVersion) { Add-Finding "CHANGELOG.md" "newest entry is $changelogVersion, the project builds $projectVersion" }

# --------------------------------------------------------------------------------------------
Write-Section "Leftovers and private traces"

$textFiles = @(
    $csharp
    Get-ChildItem (Join-Path $root "tools") -Filter *.ps1 | ForEach-Object { $_.FullName }
    Get-ChildItem (Join-Path $root "tools") -Filter *.py | ForEach-Object { $_.FullName }
    Get-ChildItem (Join-Path $root "tests") -Filter *.ps1 | ForEach-Object { $_.FullName }
    Join-Path $root "installer\aura.iss"
    Join-Path $root "build.bat"
    Join-Path $root "README.md"
    Join-Path $root "CHANGELOG.md"
)

foreach ($line in Get-SourceLines $textFiles) {
    # This file is the one place the patterns themselves are written down.
    if ($line.File -eq "tests\static-checks.ps1") { continue }

    if ($line.Text -match "\b(TODO|FIXME|HACK|XXX)\b") {
        Add-Finding "$($line.File):$($line.Number)" "leftover marker: $($line.Text.Trim())"
    }
    # A user profile path or a mail address in a shipped file is a privacy leak, not a bug.
    # "C:\Users\<name>" spelled with a placeholder is documentation, not a leak.
    if ($line.Text -match "[A-Za-z]:\\Users\\(?!(<|&lt;|%|\$))") {
        Add-Finding "$($line.File):$($line.Number)" "user profile path in a shipped file"
    }
    # Pascal's "external 'Name@user32.dll'" reads like a mail address to any simple pattern.
    if ($line.Text -match "[A-Za-z0-9._%-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}" -and
        $line.Text -notmatch "users\.noreply\.github\.com" -and
        $line.Text -notmatch "external '" -and
        $line.Text -notmatch "@[A-Za-z0-9.-]+\.dll") {
        Add-Finding "$($line.File):$($line.Number)" "mail address in a shipped file"
    }
}

# Console output is the command line's business. A window that writes to a console nobody sees is
# a leftover from debugging.
foreach ($line in $sources) {
    if ($line.File -eq "src\Program.cs") { continue }
    if ($line.Text -match "Console\.(Write|WriteLine|Error)") {
        Add-Finding "$($line.File):$($line.Number)" "console output outside the command line"
    }
}

$dist = Join-Path $root "dist"
if (Test-Path $dist) {
    foreach ($file in Get-ChildItem $dist -Filter *.pdb) {
        Add-Finding "dist\$($file.Name)" "debug symbols must not ship"
    }
}

# --------------------------------------------------------------------------------------------
Write-Host ""
if ($findings -eq 0) {
    Write-Host "All static checks passed."
    exit 0
}

Write-Host "$findings finding(s)."
exit 1
