$ErrorActionPreference = 'Stop'
# Сборка релиза живёт в build_release.py (BepInEx 5 + BepInEx 6, архивы кладутся в папку "Релиз").
# Этот скрипт оставлен как обёртка, чтобы старые ярлыки продолжали работать.
python "$PSScriptRoot\build_release.py" @args
