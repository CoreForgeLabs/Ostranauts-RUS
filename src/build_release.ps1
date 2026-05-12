# =============================================================
# build_release.ps1 — Сборка релизного пакета RUS мода
# Стратегия: копировать проверенный runtime рабочей установки, а не
# угадывать минимальный набор зависимостей.
# Результат: папка RELEASE/ в корне игры. Её можно копировать поверх
# чистой игры.
# =============================================================

$GameRoot  = "C:\Game\steamapps\steamapps\common\Ostranauts"
$OutRoot   = "$GameRoot\RELEASE"

# Версия из mod_info.json
$modInfoPath = "$GameRoot\Ostranauts_Data\Mods\RUS_CoreForgeLabs\mod_info.json"
$modVersion  = (Get-Content $modInfoPath -Raw | ConvertFrom-Json)[0].strModVersion

Write-Host "=== RUS Mod Release Builder ==="
Write-Host "Version : $modVersion"
Write-Host "Output  : $OutRoot"
Write-Host ""

# Пересоздать RELEASE
if (Test-Path $OutRoot) {
    Remove-Item $OutRoot -Recurse -Force
    Write-Host "[clean] Removed old RELEASE"
}
New-Item $OutRoot -ItemType Directory | Out-Null

# ---- Функция копирования ----
function Copy-Item-Rel {
    param($Src, $RelDest)
    $dest = Join-Path $OutRoot $RelDest
    $dir  = Split-Path $dest -Parent
    if (-not (Test-Path $dir)) { New-Item $dir -ItemType Directory -Force | Out-Null }
    Copy-Item $Src $dest -Force
}

function Copy-Dir-Rel {
    param($SrcDir, $RelDest)
    $dest = Join-Path $OutRoot $RelDest
    Copy-Item $SrcDir $dest -Recurse -Force
    Write-Host "[dir]  $RelDest"
}

function Mirror-Dir {
    param(
        [string]$SrcRoot,
        [string]$RelDest,
        [string[]]$ExcludeDirPatterns = @(),
        [string[]]$ExcludeFilePatterns = @()
    )

    $src = Join-Path $GameRoot $SrcRoot
    if (-not (Test-Path $src)) {
        Write-Host "[WARN] NOT FOUND: $SrcRoot"
        return
    }

    Get-ChildItem $src -Recurse -Directory | ForEach-Object {
        $rel = $_.FullName.Substring($src.Length).TrimStart('\\')
        if ($ExcludeDirPatterns | Where-Object { $rel -like $_ }) {
            return
        }
        $destDir = Join-Path (Join-Path $OutRoot $RelDest) $rel
        if (-not (Test-Path $destDir)) {
            New-Item $destDir -ItemType Directory -Force | Out-Null
        }
    }

    Get-ChildItem $src -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($src.Length).TrimStart('\\')
        if ($ExcludeDirPatterns | Where-Object { $rel -like ("$_*") -or $rel -like $_ }) {
            return
        }
        if ($ExcludeFilePatterns | Where-Object { $_.Name -like $_ }) {
            return
        }

        $destRel = if ([string]::IsNullOrEmpty($rel)) { $RelDest } else { Join-Path $RelDest $rel }
        Copy-Item-Rel $_.FullName $destRel
    }

    Write-Host "[mirror] $RelDest"
}

# ============================
# 1. Корень игры — bootstrapper / служебные файлы doorstop
# ============================
$rootFiles = @("winhttp.dll", "doorstop_config.ini", ".doorstop_version")
foreach ($f in $rootFiles) {
    $src = "$GameRoot\$f"
    if (Test-Path $src) {
        Copy-Item-Rel $src $f
        Write-Host "[file] $f"
    } else {
        Write-Host "[WARN] NOT FOUND: $f"
    }
}

# ============================
# 2. BepInEx — копируем почти целиком, исключая только runtime-мусор
# ============================
Mirror-Dir "BepInEx\core" "BepInEx\core"
Mirror-Dir "BepInEx\patchers" "BepInEx\patchers"
Mirror-Dir "BepInEx\plugins" "BepInEx\plugins"
Mirror-Dir "BepInEx\config" "BepInEx\config" @() @("*.bak", "*.tmp")
Mirror-Dir "BepInEx\Translation" "BepInEx\Translation"

# ============================
# 3. Mods\RUS_CoreForgeLabs — данные мода (целиком)
# ============================
$modSrc      = "$GameRoot\Ostranauts_Data\Mods\RUS_CoreForgeLabs"
$modDestAbs  = "$OutRoot\Ostranauts_Data\Mods"
New-Item $modDestAbs -ItemType Directory -Force | Out-Null
Copy-Item $modSrc $modDestAbs -Recurse -Force
Write-Host "[dir]  Ostranauts_Data\Mods\RUS_CoreForgeLabs"

# ============================
# 4. Удалить мусорные файлы (резервные копии, кэш, логи, отладочные хвосты)
# ============================
$junk = Get-ChildItem $OutRoot -Recurse -File | Where-Object {
    $_.Name -match '\.(bak\d*|merged|original|disabled)$' -or
    $_.Name -match '\.log$' -or
    $_.Name -eq 'harmony_interop_cache.dat'
}
foreach ($f in $junk) {
    Remove-Item $f.FullName -Force
    Write-Host "[del]  $($f.Name)"
}

$junkDirs = @(
    "$OutRoot\BepInEx\cache",
    "$OutRoot\BepInEx\logs"
)
foreach ($dir in $junkDirs) {
    if (Test-Path $dir) {
        Remove-Item $dir -Recurse -Force
        Write-Host "[del]  $dir"
    }
}

# ============================
# Итог
# ============================
$fileCount = (Get-ChildItem $OutRoot -Recurse -File).Count
$sizeKB    = [math]::Round((Get-ChildItem $OutRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1KB, 0)

Write-Host ""
Write-Host "=== DONE ==="
Write-Host "Files   : $fileCount"
Write-Host "Size    : $sizeKB KB"
Write-Host "Folder  : $OutRoot"
