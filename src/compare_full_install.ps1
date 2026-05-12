param(
    [string]$ReferenceRoot = 'C:\Game\steamapps\steamapps\common\Ostranauts',
    [string]$TargetRoot = 'C:\Game\steamapps\steamapps\common\Ostranauts33',
    [string]$ReportPath = 'C:\Game\steamapps\steamapps\common\Ostranauts\RusPatch_Src\TEMP-compare-Ostranauts-vs-Ostranauts33.txt'
)

$ErrorActionPreference = 'Stop'

function Get-Manifest {
    param([string]$Root)

    Get-ChildItem $Root -Recurse -File | ForEach-Object {
        $relativePath = $_.FullName.Substring($Root.Length).TrimStart('\\')
        [PSCustomObject]@{
            RelativePath = $relativePath
            Size = $_.Length
            LastWriteUtc = $_.LastWriteTimeUtc.ToString('yyyy-MM-ddTHH:mm:ssZ')
            Sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        }
    }
}

if (-not (Test-Path $ReferenceRoot)) {
    throw "Reference root not found: $ReferenceRoot"
}

if (-not (Test-Path $TargetRoot)) {
    throw "Target root not found: $TargetRoot"
}

$referenceManifest = @(Get-Manifest -Root $ReferenceRoot)
$targetManifest = @(Get-Manifest -Root $TargetRoot)

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
        "$path`n  REF : size=$($referenceItem.Size) time=$($referenceItem.LastWriteUtc) sha256=$($referenceItem.Sha256)`n  TGT : size=$($targetItem.Size) time=$($targetItem.LastWriteUtc) sha256=$($targetItem.Sha256)"
    )
}

$reportLines = @()
$reportLines += '=== FULL INSTALL COMPARE ==='
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

$reportDir = Split-Path $ReportPath -Parent
if (-not (Test-Path $reportDir)) {
    New-Item $reportDir -ItemType Directory -Force | Out-Null
}

$reportLines | Set-Content $ReportPath -Encoding UTF8

Write-Host 'DONE'
Write-Host "Report: $ReportPath"
Write-Host "Reference files : $($referenceManifest.Count)"
Write-Host "Target files    : $($targetManifest.Count)"
Write-Host "Identical files : $same"
Write-Host "Only in REF     : $($onlyReference.Count)"
Write-Host "Only in TGT     : $($onlyTarget.Count)"
Write-Host "Different files : $($different.Count)"