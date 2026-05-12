#Requires -Version 7.0
param(
    [string]$GameRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$KeyPattern = '^(GUI_|TOOLTIP_|TUTORIAL_|PLOT_|TERMINAL_)',
    [string]$ReportPath = (Join-Path $PSScriptRoot 'TEMP-translation-coverage-report.txt'),
    [string]$TsvPath = (Join-Path $PSScriptRoot 'TEMP-translation-coverage-ui.tsv')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Test-ContainsCyrillic {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    return $Text -match '[А-Яа-яЁё]'
}

function Get-VisibleAuditText {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ''
    }

    $visible = $Text -replace '<[^>]+>', ' '
    $visible = $visible -replace '\\n', ' '
    $visible = $visible -replace '\s+', ' '
    return $visible.Trim()
}

function Test-LooksEnglish {
    param([AllowNull()][string]$Text)

    $visible = Get-VisibleAuditText $Text
    if ([string]::IsNullOrWhiteSpace($visible)) {
        return $false
    }

    return ($visible -match '[A-Za-z]') -and -not (Test-ContainsCyrillic $visible)
}

function Unescape-CSharpString {
    param([string]$Value)

    return [regex]::Unescape($Value)
}

function Load-StringsJsonMap {
    param([string]$Path)

    $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $pairs = $json[0].aValues
    $map = [ordered]@{}

    for ($i = 0; $i -lt $pairs.Count; $i += 2) {
        $key = [string]$pairs[$i]
        $value = if ($i + 1 -lt $pairs.Count) { [string]$pairs[$i + 1] } else { '' }
        $map[$key] = $value
    }

    return $map
}

function Load-JsonObjectMap {
    param([string]$Path)

    $raw = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $map = [ordered]@{}

    foreach ($property in $raw.PSObject.Properties) {
        $map[[string]$property.Name] = [string]$property.Value
    }

    return $map
}

function Load-PhrasePairs {
    param([string]$Path)

    $raw = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $exact = [ordered]@{}
    $pairs = New-Object System.Collections.Generic.List[object]

    foreach ($item in $raw) {
        $en = [string]$item.en
        $ru = [string]$item.ru
        if (-not [string]::IsNullOrWhiteSpace($en)) {
            if (-not $exact.Contains($en)) {
                $exact[$en] = $ru
            }
            $pairs.Add([pscustomobject]@{ en = $en; ru = $ru })
        }
    }

    return [pscustomobject]@{
        Exact = $exact
        All = $pairs
    }
}

function Load-XunityMap {
    param([string]$Path)

    $map = [ordered]@{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line.StartsWith('#') -or $line.StartsWith(';') -or $line.StartsWith('sr:')) {
            continue
        }

        $idx = $line.IndexOf('=')
        if ($idx -lt 1) {
            continue
        }

        $key = $line.Substring(0, $idx)
        $value = $line.Substring($idx + 1)
        if (-not $map.Contains($key)) {
            $map[$key] = $value
        }
    }

    return $map
}

function Load-CSharpExactMap {
    param([string]$Path)

    $map = [ordered]@{}
    $content = Get-Content -LiteralPath $Path -Raw
    $regex = [regex]'m\["((?:\\.|[^"\\])*)"\]\s*=\s*"((?:\\.|[^"\\])*)"'

    foreach ($match in $regex.Matches($content)) {
        $key = Unescape-CSharpString $match.Groups[1].Value
        $value = Unescape-CSharpString $match.Groups[2].Value
        if (-not $map.Contains($key)) {
            $map[$key] = $value
        }
    }

    return $map
}

function Load-CSharpPhrasePairs {
    param([string]$Path)

    $pairs = New-Object System.Collections.Generic.List[object]
    $exact = [ordered]@{}
    $inBlock = $false
    $regex = [regex]'\{\s*"((?:\\.|[^"\\])*)"\s*,\s*"((?:\\.|[^"\\])*)"\s*\}'

    foreach ($line in Get-Content -LiteralPath $Path) {
        if (-not $inBlock) {
            if ($line -match 'private\s+static\s+string\[\]\[\]\s+phraseReplacements') {
                $inBlock = $true
            }
            continue
        }

        if ($line -match '^\s*};\s*$') {
            break
        }

        foreach ($match in $regex.Matches($line)) {
            $en = Unescape-CSharpString $match.Groups[1].Value
            $ru = Unescape-CSharpString $match.Groups[2].Value
            if (-not [string]::IsNullOrWhiteSpace($en)) {
                if (-not $exact.Contains($en)) {
                    $exact[$en] = $ru
                }
                $pairs.Add([pscustomobject]@{ en = $en; ru = $ru })
            }
        }
    }

    return [pscustomobject]@{
        Exact = $exact
        All = $pairs
    }
}

function Find-PhraseFragment {
    param(
        [AllowNull()][string]$Text,
        [System.Collections.Generic.List[object]]$Pairs
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    foreach ($pair in $Pairs) {
        if ([string]::IsNullOrWhiteSpace($pair.en)) {
            continue
        }
        if ($pair.en.Length -lt 4) {
            continue
        }
        if ($Text.Contains($pair.en)) {
            return $pair.en
        }
    }

    return $null
}

$baseStringsPath = Join-Path $GameRoot 'Ostranauts_Data\StreamingAssets\data\strings\strings.json'
$modStringsPath = Join-Path $GameRoot 'Ostranauts_Data\Mods\RUS_CoreForgeLabs\data\strings\strings.json'
$xunityPath = Join-Path $GameRoot 'BepInEx\Translation\ru\Text\_AutoGeneratedTranslations.txt'
$exactJsonPath = Join-Path $GameRoot 'BepInEx\plugins\rus_exact.json'
$phrasesJsonPath = Join-Path $GameRoot 'BepInEx\plugins\rus_phrases.json'
$cleanerDataPath = Join-Path $PSScriptRoot 'TextProcessing\RussianTextCleaner.Data.cs'

$baseStrings = Load-StringsJsonMap -Path $baseStringsPath
$modStrings = Load-StringsJsonMap -Path $modStringsPath
$xunityMap = Load-XunityMap -Path $xunityPath
$exactJsonMap = Load-JsonObjectMap -Path $exactJsonPath
$phraseJson = Load-PhrasePairs -Path $phrasesJsonPath
$hardcodedExact = Load-CSharpExactMap -Path $cleanerDataPath
$hardcodedPhrase = Load-CSharpPhrasePairs -Path $cleanerDataPath

$rows = New-Object System.Collections.Generic.List[object]

foreach ($key in $baseStrings.Keys) {
    if ($key -notmatch $KeyPattern) {
        continue
    }

    $baseValue = [string]$baseStrings[$key]
    $modHasKey = $modStrings.Contains($key)
    $modValue = if ($modHasKey) { [string]$modStrings[$key] } else { '' }
    $modTranslated = $modHasKey -and ($modValue -ne $baseValue)
    $xunityHit = $xunityMap.Contains($baseValue)
    $exactJsonKeyHit = $exactJsonMap.Contains($key)
    $exactJsonValueHit = $exactJsonMap.Contains($baseValue)
    $hardcodedExactHit = $hardcodedExact.Contains($baseValue)
    $phraseJsonExactHit = $phraseJson.Exact.Contains($baseValue)
    $hardcodedPhraseExactHit = $hardcodedPhrase.Exact.Contains($baseValue)
    $phraseFragment = Find-PhraseFragment -Text $baseValue -Pairs $phraseJson.All
    $hardcodedPhraseFragment = Find-PhraseFragment -Text $baseValue -Pairs $hardcodedPhrase.All

    $owner = if ($modTranslated) {
        'mod-strings'
    }
    elseif ($exactJsonKeyHit) {
        'plugin-exact-key'
    }
    elseif ($xunityHit) {
        'xunity-raw'
    }
    elseif ($exactJsonValueHit -or $hardcodedExactHit) {
        'exact-raw'
    }
    elseif ($phraseJsonExactHit -or $hardcodedPhraseExactHit -or $phraseFragment -or $hardcodedPhraseFragment) {
        'phrase-raw'
    }
    else {
        'missing'
    }

    $rows.Add([pscustomobject]@{
        Key = $key
        BaseValue = $baseValue
        ModValue = $modValue
        BaseLooksEnglish = Test-LooksEnglish $baseValue
        ModTranslated = $modTranslated
        ModHasCyrillic = Test-ContainsCyrillic $modValue
        XunityRaw = $xunityHit
        PluginExactKey = $exactJsonKeyHit
        PluginExactValue = $exactJsonValueHit
        HardcodedExactValue = $hardcodedExactHit
        PluginPhraseExact = $phraseJsonExactHit
        HardcodedPhraseExact = $hardcodedPhraseExactHit
        PluginPhraseFragment = if ($null -ne $phraseFragment) { $phraseFragment } else { '' }
        HardcodedPhraseFragment = if ($null -ne $hardcodedPhraseFragment) { $hardcodedPhraseFragment } else { '' }
        OwnerGuess = $owner
    })
}

$candidateMissing = @($rows | Where-Object {
    $_.BaseLooksEnglish -and
    -not $_.ModTranslated -and
    -not $_.XunityRaw -and
    -not $_.PluginExactKey -and
    -not $_.PluginExactValue -and
    -not $_.HardcodedExactValue -and
    -not $_.PluginPhraseExact -and
    -not $_.HardcodedPhraseExact -and
    [string]::IsNullOrWhiteSpace($_.PluginPhraseFragment) -and
    [string]::IsNullOrWhiteSpace($_.HardcodedPhraseFragment)
} | Sort-Object Key)

$redundantCoverage = @($rows | Where-Object {
    $_.ModTranslated -and ($_.XunityRaw -or $_.PluginExactKey -or $_.PluginExactValue -or $_.HardcodedExactValue)
} | Sort-Object Key)

$ownerGroups = @($rows | Group-Object OwnerGuess | Sort-Object Name)

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add('=== TRANSLATION COVERAGE AUDIT ===')
$reportLines.Add('')
$reportLines.Add("GameRoot: $GameRoot")
$reportLines.Add("KeyPattern: $KeyPattern")
$reportLines.Add('')
$reportLines.Add("Scoped base keys: $($rows.Count)")
$reportLines.Add("Candidate missing keys: $($candidateMissing.Count)")
$reportLines.Add("Redundant multi-source keys: $($redundantCoverage.Count)")
$reportLines.Add('')
$reportLines.Add('Coverage by guessed owner:')
foreach ($group in $ownerGroups) {
    $reportLines.Add(('  {0,-16} {1,5}' -f $group.Name, $group.Count))
}

$reportLines.Add('')
$reportLines.Add('Top candidate missing keys:')
foreach ($row in $candidateMissing | Select-Object -First 80) {
    $reportLines.Add("  $($row.Key) = $($row.BaseValue)")
}

$reportLines.Add('')
$reportLines.Add('Examples with redundant coverage (mod + other layer):')
foreach ($row in $redundantCoverage | Select-Object -First 60) {
    $flags = @()
    if ($row.XunityRaw) { $flags += 'xunity' }
    if ($row.PluginExactKey) { $flags += 'plugin-key' }
    if ($row.PluginExactValue) { $flags += 'plugin-value' }
    if ($row.HardcodedExactValue) { $flags += 'hardcoded-exact' }
    $reportLines.Add("  $($row.Key) [$([string]::Join(', ', $flags))]")
}

$reportLines | Set-Content -LiteralPath $ReportPath -Encoding UTF8
$rows | Export-Csv -LiteralPath $TsvPath -NoTypeInformation -Delimiter "`t" -Encoding UTF8

Write-Host "Saved report: $ReportPath"
Write-Host "Saved TSV:    $TsvPath"
Write-Host "Scoped keys:  $($rows.Count)"
Write-Host "Missing:      $($candidateMissing.Count)"
Write-Host "Redundant:    $($redundantCoverage.Count)"