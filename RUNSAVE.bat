@echo off
chcp 65001 >nul
title Ostranauts — Quick Load Save

:: %~dp0 = directory where this .bat file is located (game root)
set "FLAG=%~dp0BepInEx\plugins\autoload.flag"

:: Kill running instance
taskkill /F /IM Ostranauts.exe >nul 2>&1
if %errorlevel%==0 (
    echo Stopping running game...
    timeout /t 2 /nobreak >nul
)

:: Create auto-load flag file
echo. > "%FLAG%"

:: Launch via Steam
start "" "steam://rungameid/1022980"
echo Game launching with auto-load...
timeout /t 3