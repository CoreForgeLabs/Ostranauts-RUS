@echo off
chcp 65001 > nul
echo ===================================================
echo   Сборка Релиза OstraI18n (CFLabs)
echo ===================================================
python "%~dp0build_release.py" %*
pause
