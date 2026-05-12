param(
    [string]$LogPath = "c:\Game\steamapps\steamapps\common\Ostranauts\BepInEx\logs\ruspatch_actions_clean.tsv",
    [int]$SkipLines = 0,
    [string]$OutputPath = "c:\Game\steamapps\steamapps\common\Ostranauts\RusPatch_Src\TEMP-actionlog-summary.txt"
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $LogPath)) {
    throw "Log file not found: $LogPath"
}

$defaultWhitelist = @(
    '^iscrowbarhallwaydoor2$',
    '^isstartingclothes$',
    '^isstartingleftsneaker$',
    '^isstartingrightsneaker$',
    '^isstartingtoolbox$',
    '^istutorialhallwaydoor$',
    '^oklg_[a-z0-9_]+$',
    '^o-[a-z0-9]+$'
)

$rows = Get-Content $LogPath
$data = $rows | Select-Object -Skip ([Math]::Max(1, $SkipLines + 1) - 1)

$cleanValues = New-Object System.Collections.Generic.List[string]
foreach ($ln in $data) {
    $parts = $ln -split "`t", 6
    if ($parts.Count -ge 6) {
        $cleanValues.Add($parts[5])
    }
}

$latinRaw = $cleanValues | Where-Object { $_ -match '[A-Za-z]' }

$latinFiltered = @()
foreach ($line in $latinRaw) {
    $isWhitelisted = $false
    foreach ($rx in $defaultWhitelist) {
        if ($line -match $rx) {
            $isWhitelisted = $true
            break
        }
    }
    if (-not $isWhitelisted) {
        $latinFiltered += $line
    }
}

$topRaw = $latinRaw | Group-Object | Sort-Object Count -Descending | Select-Object -First 40
$topFiltered = $latinFiltered | Group-Object | Sort-Object Count -Descending | Select-Object -First 40

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("TOTAL_LINES=" + $data.Count)
[void]$sb.AppendLine("CLEAN_LATIN_RAW=" + $latinRaw.Count)
[void]$sb.AppendLine("CLEAN_LATIN_FILTERED=" + $latinFiltered.Count)
[void]$sb.AppendLine("")
[void]$sb.AppendLine("WHITELIST:")
foreach ($rx in $defaultWhitelist) {
    [void]$sb.AppendLine("  " + $rx)
}
[void]$sb.AppendLine("")
[void]$sb.AppendLine("TOP_RAW:")
foreach ($item in $topRaw) {
    [void]$sb.AppendLine(($item.Count.ToString().PadLeft(6) + "  " + $item.Name))
}
[void]$sb.AppendLine("")
[void]$sb.AppendLine("TOP_FILTERED:")
foreach ($item in $topFiltered) {
    [void]$sb.AppendLine(($item.Count.ToString().PadLeft(6) + "  " + $item.Name))
}

[System.IO.File]::WriteAllText($OutputPath, $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Output ("SAVED=" + $OutputPath)
Write-Output ("TOTAL_LINES=" + $data.Count)
Write-Output ("CLEAN_LATIN_RAW=" + $latinRaw.Count)
Write-Output ("CLEAN_LATIN_FILTERED=" + $latinFiltered.Count)
