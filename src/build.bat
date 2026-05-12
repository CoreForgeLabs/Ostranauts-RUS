@echo off
chcp 65001 >nul
echo ============================================
echo  Компиляция OstranautsRusPatch BepInEx Plugin
echo ============================================
echo.

set GAME=C:\Game\steamapps\steamapps\common\Ostranauts
set CSPROJ=OstranautsRusPatch.csproj
set PLUGINS=%GAME%\BepInEx\plugins
set OUTPUT=%PLUGINS%\OstranautsRusPatch.dll

echo [1/3] Компиляция...
set GAME=%GAME%
dotnet build "%CSPROJ%" -c Release --nologo -v minimal -p:GAME="%GAME%" -p:OutDir="%PLUGINS%\\" -p:AppendTargetFrameworkToOutputPath=false

if %ERRORLEVEL% neq 0 (
    echo.
    echo [ОШИБКА] Компиляция не удалась!
    pause
    exit /b 1
)

echo [2/3] Проверка артефакта в BepInEx/plugins...
if not exist "%OUTPUT%" (
    echo.
    echo [ОШИБКА] DLL не найдена после сборки: %OUTPUT%
    pause
    exit /b 1
)

echo [3/3] Удаление старого ArticleCleaner (заменён RusPatch)...
if exist "%PLUGINS%\ArticleCleaner.dll" (
    rename "%PLUGINS%\ArticleCleaner.dll" "ArticleCleaner.dll.disabled"
    echo        ArticleCleaner.dll отключён (переименован в .disabled)
)

echo.
echo ============================================
echo  ГОТОВО! Плагин установлен.
echo  Запустите игру для проверки.
echo ============================================
