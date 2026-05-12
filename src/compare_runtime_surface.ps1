param(
    [string]$ReferenceRoot = 'C:\Game\steamapps\steamapps\common\Ostranauts',
    [string]$TargetRoot = 'C:\Game\steamapps\steamapps\common\Ostranauts33',
    [string]$ReportPath = 'C:\Game\steamapps\steamapps\common\Ostranauts\RusPatch_Src\TEMP-compare-runtime-surface.txt'
)

$ErrorActionPreference = 'Stop'

$includePaths = @(
    'winhttp.dll',
    'doorstop_config.ini',
    '.doorstop_version',
    'BepInEx\core',
    'BepInEx\patchers',
    'BepInEx\plugins',
    'BepInEx\config',
    'BepInEx\Translation',
    'Ostranauts_Data\Mods\RUS_CoreForgeLabs'
)

function Get-ScopedManifest {
    param([string]$Root, [string[]]$RelativePaths)

    $items = New-Object System.Collections.Generic.List[object]

    foreach ($relativePath in $RelativePaths) {
        $absolutePath = Join-Path $Root $relativePath
        if (-not (Test-Path $absolutePath)) {
            continue
        }

        $item = Get-Item $absolutePath
        if ($item.PSIsContainer) {
            Get-ChildItem $absolutePath -Recurse -File | ForEach-Object {
                $items.Add([PSCustomObject]@{
                    RelativePath = $_.FullName.Substring($Root.Length).TrimStart('\\')
                    Size = $_.Length
                    Sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
                })
            }
        }
        else {
            $items.Add([PSCustomObject]@{
                RelativePath = $relativePath
                Size = $item.Length
                Sha256 = (Get-FileHash $absolutePath -Algorithm SHA256).Hash
            })
        }
    }

    $items
}

$referenceManifest = @(Get-ScopedManifest -Root $ReferenceRoot -RelativePaths $includePaths)
$targetManifest = @(Get-ScopedManifest -Root $TargetRoot -RelativePaths $includePaths)

$referenceMap = @{}
foreach ($item in $referenceManifest) {
    $referenceMap[$item.RelativePath] = $item
}

$targetMap = @{}
foreach ($item in $targetManifest) {
    $targetMap[$item.RelativePath] = $item
}

$allPaths = ($referenceMap.Keys + $targetMap.Keys | Sort-Object -Unique)
$onlyReference = New-Object System.Collections.Generic.List[string]
$onlyTarget = New-Object System.Collections.Generic.List[string]
$different = New-Object System.Collections.Generic.List[string]
$same = 0

foreach ($path in $allPaths) {
    $inReference = $referenceMap.ContainsKey($path)
    $inTarget = $targetMap.ContainsKey($path)

    if ($inReference -and -not $inTarget) {
        $onlyReference.Add($path)
        continue
    }

    if ($inTarget -and -not $inReference) {
        $onlyTarget.Add($path)
        continue
    }

    $referenceItem = $referenceMap[$path]
    $targetItem = $targetMap[$path]

    if ($referenceItem.Sha256 -eq $targetItem.Sha256) {
        $same++
        continue
    }

    $different.Add(
        "$path`n  REF : size=$($referenceItem.Size) sha256=$($referenceItem.Sha256)`n  TGT : size=$($targetItem.Size) sha256=$($targetItem.Sha256)"
    )
}

$reportLines = @()
$reportLines += '=== RUNTIME SURFACE COMPARE ==='
$reportLines += "Reference: $ReferenceRoot"
$reportLines += "Target   : $TargetRoot"
$reportLines += "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$reportLines += ''
$reportLines += '=== SUMMARY ==='
$reportLines += "Reference files : $($referenceManifest.Count)"
$reportLines += "Target files    : $($targetManifest.Count)"
$reportLines += "Identical files : $same"
$reportLines += "Only in REF     : $($onlyReference.Count)"
$reportLines += "Only in TGT     : $($onlyTarget.Count)"
$reportLines += "Different files : $($different.Count)"
$reportLines += ''
$reportLines += '=== ONLY IN REF ==='
$reportLines += ($onlyReference | Sort-Object)
$reportLines += ''
$reportLines += '=== ONLY IN TGT ==='
$reportLines += ($onlyTarget | Sort-Object)
$reportLines += ''
$reportLines += '=== DIFFERENT FILES ==='
$reportLines += ($different | Sort-Object)

$reportLines | Set-Content $ReportPath -Encoding UTF8

Write-Host 'DONE'
Write-Host "Report: $ReportPath"
Write-Host "Reference files : $($referenceManifest.Count)"
Write-Host "Target files    : $($targetManifest.Count)"
Write-Host "Identical files : $same"
Write-Host "Only in REF     : $($onlyReference.Count)"
Write-Host "Only in TGT     : $($onlyTarget.Count)"
Write-Host "Different files : $($different.Count)"