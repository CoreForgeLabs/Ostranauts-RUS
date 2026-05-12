# Ostranauts Russian Patch (RusPatch)

BepInEx-плагин для игры **Ostranauts**, исправляющий проблемы русского перевода.

## Возможности

- **Замена английской грамматики:** удаление артиклей (the/a/an), замена местоимений (he/she/they → он/она/они), удаление притяжательного 's
- **Точный перевод компонентов:** 50+ терминов через `rus_exact.json` (Generator → Генератор, Thruster → Двигатель и т.д.)
- **Перевод UI-меток:** панель корабля (FLOW_RATE → ПОТОК, PRESSURE → ДАВЛЕНИЕ), карьерные кнопки (CAPTAIN → КАПИТАН, ENGINEER → ИНЖЕНЕР)
- **Портативность:** Harmony-патчи по именам методов — переживают обновления игры
- **Производительность:** потокобезопасный кэш (ConcurrentDictionary), ThreadStatic StringBuilder, быстрые пути для кириллицы

## Установка

1. Установите [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) для Ostranauts
2. Скопируйте `OstranautsRusPatch.dll` в `BepInEx\plugins\`
3. Скопируйте `rus_exact.json` в `BepInEx\plugins\`
4. Запустите игру

## Структура проекта

```
RusPatch_Src/
├── Plugin.cs              # Точка входа BepInEx, ActionLog
├── JsonFileLoader.cs      # Минимальный JSON-парсер (совместим с .NET 2.0/Mono)
├── ShipDescriptions.cs    # Перевод описаний кораблей
├── rus_exact.json         # Словарь точных переводов
├── Patches/
│   ├── GrammarPatches.cs  # Транспайлеры + префиксы для GrammarUtils.GenerateString()
│   ├── TextOnEnablePostfix.cs  # Постфикс Text.OnEnable + транспайлер set_text
│   ├── TextValuePatches.cs     # Замена строковых литералов FLOW_RATE, PRESSURE и др.
│   └── UIPatches.cs       # Постфиксы для UI-панелей, FLabel, crew_nameplate_btn
├── TextProcessing/
│   ├── RussianTextCleaner.Clean.cs  # Ядро очистки (кэш, быстрые пути)
│   ├── RussianTextCleaner.Data.cs   # Regex-паттерны
│   └── RussianTextCleaner.Logic.cs  # ApplyAllPatterns, RemoveArticles, ReplacePronouns
├── OstranautsRusPatch.csproj
├── README.md
└── CHANGELOG.md
```

## Сборка

Требуется .NET Framework 3.5 (совместимость с Mono в Unity).

```powershell
# Из директории RusPatch_Src:
MSBuild.exe OstranautsRusPatch.csproj /p:Configuration=Release
```

Ссылки на сборки BepInEx и UnityEngine ожидаются в `Lib\` (настраивается в `.csproj`).

## Полная сборка (`BUILD_ALL.bat`)

`BUILD_ALL.bat` — это **полный конвейер сборки**, который последовательно выполняет **все 9 шагов**:

| # | Команда | Назначение |
|---|---------|------------|
| 0 | Проверка окружения | Python ✓, dotnet ✓ |
| 1 | `Ostranauts.exe -doorstop-enable true` | Запуск игры с exact.dll → `en_texts.json` |
| 2 | `python -m collect_game_text collect en_texts` | Агрегация `en_texts.json` → `prod\rus_src.json` |
| 3 | Резервное копирование | `prod\` → `backup\backup_ДАТА\` |
| 4 | `python -m enrich_names process` | Обогащение имён → `prod\rus_name_enriched.json` |
| 5 | `python -m exact_ru exact` | Точный перевод → `prod\rus_exact.json` |
| 6 | `python -m deepseek_ru translate` | DeepSeek-перевод → `prod\rus_deepseek.json` |
| 7 | `python -m rus_pack json2bin` | Упаковка в бинарный `.rus` → `BepInEx\plugins\lang.rus` |
| 8 | `dotnet build` | Компиляция `OstranautsRusPatch.dll` |

```batch
BUILD_ALL.bat
```

**Результат**: полностью готовый плагин в `BepInEx\plugins\` (`OstranautsRusPatch.dll` + `lang.rus`).

Если конвейер прерывается на каком-то шаге, достаточно **повторно запустить `BUILD_ALL.bat`** — он продолжит с того места, где остановился (промежуточные файлы сохраняются в `prod\`).

## Устранение неполадок

Лог-файл: `<игра>\actionlog.log` (автоматическая ротация при превышении 10 МБ).

## Лицензия

MIT