#Requires -Version 7.0
param(
    [string]$GameRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$ReportPath = (Join-Path $PSScriptRoot 'TEMP-data-json-coverage-report.txt'),
    [string]$TsvPath = (Join-Path $PSScriptRoot 'TEMP-data-json-coverage.tsv')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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

function Test-ContainsCyrillic {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    return $Text -match '[А-Яа-яЁё]'
}

function Test-LooksEnglish {
    param([AllowNull()][string]$Text)

    $visible = Get-VisibleAuditText $Text
    if ([string]::IsNullOrWhiteSpace($visible)) {
        return $false
    }

    return ($visible -match '[A-Za-z]') -and -not (Test-ContainsCyrillic $visible)
}

function Test-HumanTextProperty {
    param([string]$PropertyName)

    return $PropertyName -match '^(str(NameFriendly|FriendlyName|Desc|Description|Interaction|Text|Title|Body|Label|Tooltip|Message|Warning|Subject|Summary|Flavor|Lore|Notes?|Prompt))$'
}

function Add-JsonStringLeaves {
    param(
        $Node,
        [string]$Path,
        [System.Collections.Generic.Dictionary[string,string]]$Map
    )

    if ($null -eq $Node) {
        return
    }

    if ($Node -is [string]) {
        $Map[$Path] = $Node
        return
    }

    if ($Node -is [System.Collections.IDictionary]) {
        foreach ($key in $Node.Keys) {
            $childPath = if ([string]::IsNullOrEmpty($Path)) { [string]$key } else { "$Path.$key" }
            Add-JsonStringLeaves -Node $Node[$key] -Path $childPath -Map $Map
        }
        return
    }

    if ($Node -is [System.Collections.IEnumerable] -and -not ($Node -is [string])) {
        $index = 0
        foreach ($item in $Node) {
            $childPath = "$Path[$index]"
            Add-JsonStringLeaves -Node $item -Path $childPath -Map $Map
            $index++
        }
        return
    }

    $properties = @($Node.PSObject.Properties)
    if ($properties.Count -gt 0) {
        foreach ($property in $properties) {
            $childPath = if ([string]::IsNullOrEmpty($Path)) { $property.Name } else { "$Path.$($property.Name)" }
            Add-JsonStringLeaves -Node $property.Value -Path $childPath -Map $Map
        }
    }
}

function Get-JsonStringLeafMap {
    param([string]$Path)

    $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]'
    Add-JsonStringLeaves -Node $json -Path '$' -Map $map
    return $map
}

$baseDataRoot = Join-Path $GameRoot 'Ostranauts_Data\StreamingAssets\data'
$modDataRoot = Join-Path $GameRoot 'Ostranauts_Data\Mods\RUS_CoreForgeLabs\data'

$rows = New-Object System.Collections.Generic.List[object]

$modFiles = Get-ChildItem -LiteralPath $modDataRoot -Recurse -File -Filter *.json |
    Where-Object {
        $_.Name -ne 'strings.json' -and
        $_.Name -notlike '*.bak*' -and
        $_.Name -notlike '*.original' -and
        $_.Name -notlike '*.merged' -and
        $_.Name -notlike '*.disabled'
    }

foreach ($modFile in $modFiles) {
    $relativePath = $modFile.FullName.Substring($modDataRoot.Length).TrimStart('\')
    $basePath = Join-Path $baseDataRoot $relativePath
    if (-not (Test-Path -LiteralPath $basePath)) {
        continue
    }

    $baseMap = Get-JsonStringLeafMap -Path $basePath
    $modMap = Get-JsonStringLeafMap -Path $modFile.FullName

    foreach ($pathKey in $baseMap.Keys) {
        if (-not $modMap.ContainsKey($pathKey)) {
            continue
        }

        $propertyName = ($pathKey -split '\.')[-1] -replace '\[\d+\]$', ''
        if (-not (Test-HumanTextProperty $propertyName)) {
            continue
        }

        $baseValue = [string]$baseMap[$pathKey]
        $modValue = [string]$modMap[$pathKey]
        $sameAsBase = $baseValue -eq $modValue

        $rows.Add([pscustomobject]@{
            RelativeFile = $relativePath
            JsonPath = $pathKey
            Property = $propertyName
            BaseValue = $baseValue
            ModValue = $modValue
            BaseLooksEnglish = Test-LooksEnglish $baseValue
            ModHasCyrillic = Test-ContainsCyrillic $modValue
            SameAsBase = $sameAsBase
            CandidateUntranslated = $sameAsBase -and (Test-LooksEnglish $baseValue)
        })
    }
}

$candidateRows = @($rows | Where-Object { $_.CandidateUntranslated } | Sort-Object RelativeFile, JsonPath)
$fileGroups = @(
    $candidateRows |
        Group-Object RelativeFile |
        Sort-Object -Property @{ Expression = 'Count'; Descending = $true }, @{ Expression = 'Name'; Descending = $false }
)

$report = New-Object System.Collections.Generic.List[string]
$report.Add('=== DATA JSON COVERAGE AUDIT ===')
$report.Add('')
$report.Add("GameRoot: $GameRoot")
$report.Add("Mod JSON files scanned: $($modFiles.Count)")
$report.Add("Comparable text leaves: $($rows.Count)")
$report.Add("Candidate untranslated leaves: $($candidateRows.Count)")
$report.Add('')
$report.Add('Top files by candidate untranslated leaves:')
foreach ($group in $fileGroups | Select-Object -First 30) {
    $report.Add(('  {0,-60} {1,5}' -f $group.Name, $group.Count))
}

$report.Add('')
$report.Add('Top candidate untranslated leaves:')
foreach ($row in $candidateRows | Select-Object -First 200) {
    $report.Add("  $($row.RelativeFile) :: $($row.JsonPath) = $($row.BaseValue)")
}

$report | Set-Content -LiteralPath $ReportPath -Encoding UTF8
$rows | Export-Csv -LiteralPath $TsvPath -NoTypeInformation -Delimiter "`t" -Encoding UTF8

Write-Host "Saved report: $ReportPath"
Write-Host "Saved TSV:    $TsvPath"
Write-Host "Files:       $($modFiles.Count)"
Write-Host "Text leaves: $($rows.Count)"
Write-Host "Candidates:  $($candidateRows.Count)"