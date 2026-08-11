# OstraI18n Фаза 3 — контент-слой (оверлей по strName) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Перевод игровых данных (интеракции, крепёж и т.п.) без копирования файлов — оверлей переводимых полей поверх текущих JSON-данных игры по идентификатору `strName`, применяемый в рантайме после полной загрузки данных, плюс офлайн-инструмент импорта старого перевода с валидацией.

**Architecture:** Один статический делегат `DataHandler.LoadComplete` (уже существует в игре, вызывается на главном потоке после того, как все моды догружены и все словари данных заполнены) — точка привязки. `ContentOverlay.Apply()` читает `langs/<lang>/data/<категория>.json`, находит соответствующий публичный статический словарь `DataHandler.dict*` **по имени поля через рефлексию** и точечно переписывает только переводимые строковые свойства (`strTitle`, `strDesc`, `strTooltip`, ...) у уже десериализованных объектов. Никакого патчинга IL, никакой повторной разборки JSON игры.

**Tech Stack:** C#/.NET (netstandard2.1, тот же плагин), `System.Reflection`, `System.Text.Json`; офлайн-инструменты — Python (тот же стек, что и `tools/extract_prefab_paths.py`).

## Global Constraints

- Игра: Unity 6000.3.10f1, Mono (не IL2CPP) — код декомпилируется, рефлексия работает без ограничений AOT.
- Плагин: `plugin/OstraI18n/OstraI18n.csproj`, netstandard2.1, деплой в `F:\Games\Steam\steamapps\common\Ostranauts\BepInEx\plugins\OstraI18n\`.
- Формат языковых паков — тот же, что в Фазах 1-2: `{"strName": "...", "strLanguage": "...", "dict": {...}}`; для контент-слоя структура вложенного словаря отличается (ключ верхнего уровня — `strName` игрового объекта, значение — объект с переводимыми полями), см. Task 1.
- **Ключ оверлея — категория (папка верхнего уровня, которую игра грузит в один словарь через `DataHandler.LoadModJsons<T>(folder, dict, ...)`), а не буквальное имя JSON-файла.** Это уточнение относительно спеки: в декомпилированном `DataHandler.cs` подтверждено (`LoadModJsons`, ~82 вызова в `DataHandler.cs:915-1000+`), что все `*.json`-файлы под одной папкой (например `interactions/`) сливаются в **один** словарь по `strName` — коллизия при этом уже существует в самой игре (побеждает последний загруженный файл), поэтому различать их по отдельным именам файлов внутри категории избыточно. Различать нужно только **категории** (разные папки/разные типы данных), потому что один и тот же `strName` может случайно встретиться в двух разных категориях (спека: «135 идентификаторов встречаются более чем в одном файле»).
- Точка привязки — `DataHandler.LoadComplete` (`public static Action`, декомпилировано в `decompiled/DataHandler.cs:50`), вызывается из `Ostranauts.Core.LoadManager.AfterLoadThreadsFinish()` (`decompiled/Ostranauts.Core/LoadManager.cs:307-310`) **на главном потоке**, уже после `DataHandler.AllPostLoadAsync()` и `PostModLoadMainThread()` — все словари данных полностью готовы, включая слияние модов. Прямая подписка `DataHandler.LoadComplete += ...` в `Awake()` плагина, без Harmony.
- Переводимые поля (whitelist, из спеки): `strTitle`, `strDesc`, `strTooltip`, `strNameFriendly`, `strNameShort`, `strFriendlyName`. Поле `strName` — идентификатор, в оверлей никогда не пишется и не перезаписывается.
- Старый перевод: `F:\Games\Steam\steamapps\common\Ostranauts\old\Ostranauts_Data\Mods\RUS_CoreForgeLabs\data\` — полные копии файлов (формат подтверждён: `interactions/interactions.json`, список из 736 объектов, поле `strName` присутствует у каждого). Использовать **только** для чтения переводов по `strName`, никогда не копировать файлы целиком.
- Текущие данные игры (эталон для сверки при импорте): `F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data\`.

---

## Task 1: `ContentOverlay` — вертикальный срез на категории `interactions`

**Files:**
- Create: `plugin/OstraI18n/ContentOverlay.cs`
- Create: `langs/en/data/interactions.json` (только тестовая запись, см. ниже)
- Create: `langs/ru/data/interactions.json` (та же запись, переведённая)
- Modify: `plugin/OstraI18n/Plugin.cs` (вызов `ContentOverlay.Init(...)` в `Awake()`)

**Interfaces:**
- Consumes: `DataHandler.LoadComplete` (существующий делегат игры), `DataHandler.dictInteractions` (существующий словарь игры, `Dictionary<string, JsonInteraction>`, оба публичные статические)
- Produces: `ContentOverlay.Init(string pluginDir, string langCode)`, `ContentOverlay.Applied` (int, счётчик применённых полей), `ContentOverlay.Orphans` (int, счётчик записей оверлея без соответствующего `strName` в текущих данных игры) — используются в Task 4 для лог-проверки на живом прогоне

- [ ] **Step 1: Написать `ContentOverlay.cs`**

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace OstraI18n
{
    // Оверлей переводимых полей игровых данных поверх уже загруженных словарей
    // DataHandler.dict* — без патчинга загрузки, без копирования файлов игры.
    // Точка привязки: DataHandler.LoadComplete (см. Global Constraints плана Фазы 3
    // за обоснование, почему это безопасно и достаточно).
    internal static class ContentOverlay
    {
        // категория (папка, которую игра грузит в один словарь через LoadModJsons)
        // -> имя публичного статического поля DataHandler
        private static readonly Dictionary<string, string> CategoryToField = new Dictionary<string, string>
        {
            { "interactions", "dictInteractions" },
        };

        private static readonly HashSet<string> TranslatableFields = new HashSet<string>
        {
            "strTitle", "strDesc", "strTooltip", "strNameFriendly", "strNameShort", "strFriendlyName",
        };

        public static int Applied;
        public static int Orphans;

        public static void Init(string pluginDir, string langCode)
        {
            DataHandler.LoadComplete += () =>
            {
                try { Apply(pluginDir, langCode); }
                catch (Exception ex) { Plugin.Log.LogError("[i18n] контент-оверлей упал: " + ex); }
            };
        }

        private static void Apply(string pluginDir, string langCode)
        {
            var dataDir = Path.Combine(pluginDir, "langs", langCode, "data");
            if (!Directory.Exists(dataDir))
            {
                Plugin.Log.LogInfo("[i18n] контент-оверлей: папка " + dataDir + " не найдена, пропуск");
                return;
            }

            foreach (var kv in CategoryToField)
            {
                var jsonPath = Path.Combine(dataDir, kv.Key + ".json");
                if (!File.Exists(jsonPath)) continue;
                ApplyCategory(kv.Key, kv.Value, jsonPath);
            }

            Plugin.Log.LogInfo("[i18n] контент-оверлей: применено полей " + Applied + ", сирот " + Orphans);
        }

        private static void ApplyCategory(string category, string fieldName, string jsonPath)
        {
            var field = typeof(DataHandler).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            if (field == null)
            {
                Plugin.Log.LogWarning("[i18n] контент-оверлей: DataHandler." + fieldName + " не найдено (категория " + category + ")");
                return;
            }
            var dictObj = field.GetValue(null) as IDictionary;
            if (dictObj == null)
            {
                Plugin.Log.LogWarning("[i18n] контент-оверлей: DataHandler." + fieldName + " не является словарём или null");
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                var strName = entry.Name;
                if (!dictObj.Contains(strName))
                {
                    Orphans++;
                    continue;
                }
                var target = dictObj[strName];
                var targetType = target.GetType();
                foreach (var fieldEntry in entry.Value.EnumerateObject())
                {
                    if (!TranslatableFields.Contains(fieldEntry.Name)) continue;
                    var prop = targetType.GetProperty(fieldEntry.Name);
                    if (prop == null || prop.PropertyType != typeof(string) || !prop.CanWrite) continue;
                    prop.SetValue(target, fieldEntry.Value.GetString());
                    Applied++;
                }
            }
        }
    }
}
```

- [ ] **Step 2: Тестовые данные — одна реальная запись**

Создать `langs/en/data/interactions.json`:

```json
{
  "ACTAddConnection": { "strTitle": "AddConnection" }
}
```

(значение `"AddConnection"` — реальный `strTitle` из `F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data\interactions\interactions.json`, проверено вручную; en-пак здесь служит только справочной парой, рантайм его не читает)

Создать `langs/ru/data/interactions.json`:

```json
{
  "ACTAddConnection": { "strTitle": "ПРОВЕРКА_КОНТЕНТА" }
}
```

- [ ] **Step 3: Подключить в `Plugin.cs`**

Добавить в `Awake()`, после блока `PrefabBinder`:

```csharp
            try { ContentOverlay.Init(DataDir.Value, "ru"); }
            catch (Exception ex) { Log.LogError("[i18n] content overlay init failed: " + ex); }
```

- [ ] **Step 4: Собрать**

```bash
cd /f/DEV2/ostra_i18n/plugin/OstraI18n && dotnet build -c Release 2>&1 | tail -10
```

Ожидается: `Ошибок: 0`.

- [ ] **Step 5: Задеплоить и запустить**

```bash
powershell.exe -Command "Get-Process -Name Ostranauts -ErrorAction SilentlyContinue | Stop-Process -Force"
DST="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/plugins/OstraI18n"
cp -f /f/DEV2/ostra_i18n/plugin/OstraI18n/bin/Release/netstandard2.1/OstraI18n.dll "$DST/OstraI18n.dll"
cp -f /f/DEV2/ostra_i18n/core/OstraI18n.Core/bin/Release/netstandard2.1/OstraI18n.Core.dll "$DST/OstraI18n.Core.dll"
mkdir -p "$DST/langs/ru/data"
cp -f /f/DEV2/ostra_i18n/langs/ru/data/interactions.json "$DST/langs/ru/data/interactions.json"
rm -f "$DST/../../LogOutput.log" 2>/dev/null
rm -f "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
powershell.exe -Command "Start-Process -FilePath 'F:\Games\Steam\steamapps\common\Ostranauts\RUNSAVE.bat' -WorkingDirectory 'F:\Games\Steam\steamapps\common\Ostranauts'"
```

**Напоминание (см. `docs/baseline.md`, урок Фазы 2 Task 5):** копировать ОБЕ сборки (`OstraI18n.dll` и `OstraI18n.Core.dll`) при каждом деплое, даже если кажется, что `Core.dll` не менялся — несоответствие версий даёт ошибку только в узком рантайм-пути, не при старте.

- [ ] **Step 6: Проверить лог**

```bash
LOG="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
until grep -q "контент-оверлей:" "$LOG" 2>/dev/null; do sleep 3; done
grep -E "контент-оверлей:|Exception" "$LOG"
```

Ожидается: `[i18n] контент-оверлей: применено полей 1, сирот 0`, `Exception` — не встречается.

**Гейт:** если `применено полей 0` — проверить, что `ACTAddConnection` действительно есть в `DataHandler.dictInteractions` на момент вызова (маловероятно — этот интерэкшн используется повсеместно), либо что путь `langs/ru/data/interactions.json` в деплое совпадает с тем, что читает `DataDir.Value` (проверить значение конфига `DataDir` в `BepInEx/config/com.coreforge.ostra.i18n.cfg`).

- [ ] **Step 7: Живая проверка значения (без захода в конкретный экран игры)**

Дописать в `ContentOverlay.Apply` после основного цикла (временно, для среза — не убирать, пригодится и в Task 4 как общий self-test):

```csharp
            if (DataHandler.dictInteractions.TryGetValue("ACTAddConnection", out var testEntry))
            {
                Plugin.Log.LogInfo("[i18n] контент-оверлей self-test: ACTAddConnection.strTitle = '" + testEntry.strTitle + "'");
            }
```

Пересобрать, передеплоить, перезапустить (Steps 4-5), проверить лог:

```bash
grep "self-test: ACTAddConnection" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
```

Ожидается: `strTitle = 'ПРОВЕРКА_КОНТЕНТА'`. Это подтверждает, что перезапись происходит именно на объекте, который держит игра (не на копии), без необходимости находить нужный экран UI вручную.

- [ ] **Step 8: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: content overlay by strName, vertical slice on interactions category"
```

---

## Task 2: Импорт старого перевода — офлайн-инструмент с валидацией

**Files:**
- Create: `tools/import_old_translation.py`
- Create: `lang_src/old_import_report.json` (в `.gitignore` — отчёт, не источник истины)

**Interfaces:**
- Consumes: `F:\Games\Steam\steamapps\common\Ostranauts\old\Ostranauts_Data\Mods\RUS_CoreForgeLabs\data\<категория>\*.json` (старый перевод, полные копии), `F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data\<категория>\*.json` (текущие данные игры, эталон для сверки)
- Produces: `langs/ru/data/<категория>.json` (обновляется — принятые записи мержатся в существующий файл, не перезаписывают вручную добавленные вроде тестовой из Task 1), плюс отчёт с корзинами `устарело`/`подозрительно`

- [ ] **Step 1: Написать инструмент**

```python
#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
import_old_translation.py — импортирует старый перевод (Ostranauts/old/.../RUS_CoreForgeLabs/data/)
в оверлей langs/ru/data/, сопоставляя ТОЛЬКО по strName внутри категории, с валидацией
каждой строки. Ничего не применяется молча — три корзины: принято/устарело/подозрительно.

Запуск: python import_old_translation.py <категория> [<категория> ...]
Пример: python import_old_translation.py interactions
"""
import io
import json
import os
import re
import sys

ROOT = r"F:\DEV2\ostra_i18n"
GAME = r"F:\Games\Steam\steamapps\common\Ostranauts"
OLD_DATA = os.path.join(GAME, "old", "Ostranauts_Data", "Mods", "RUS_CoreForgeLabs", "data")
CUR_DATA = os.path.join(GAME, "Ostranauts_Data", "StreamingAssets", "data")

TRANSLATABLE = ("strTitle", "strDesc", "strTooltip", "strNameFriendly", "strNameShort", "strFriendlyName")

TOKEN_RE = re.compile(r"\[(us|them|verb|cap)\]")
PLACEHOLDER_RE = re.compile(r"\{\d+\}")
TAG_RE = re.compile(r"</?[a-zA-Z][a-zA-Z0-9]*>")


def load_category(base_dir, category):
    """Собирает все *.json из папки категории в единый dict strName -> object,
    так же, как это делает игра через DataHandler.LoadModJsons (последний файл
    в алфавитном порядке обхода Directory.GetFiles побеждает при коллизии —
    здесь неважно, т.к. коллизии внутри категории уже есть в самой игре)."""
    folder = os.path.join(base_dir, category)
    result = {}
    if not os.path.isdir(folder):
        return result
    for root, _, files in os.walk(folder):
        for fn in sorted(files):
            if not fn.endswith(".json"):
                continue
            path = os.path.join(root, fn)
            try:
                data = json.loads(io.open(path, encoding="utf-8").read())
            except Exception as e:
                print("  ПРОПУСК (bad JSON) %s: %s" % (path, e))
                continue
            if isinstance(data, list):
                for e in data:
                    if isinstance(e, dict) and e.get("strName"):
                        result[e["strName"]] = e
            elif isinstance(data, dict):
                for k, v in data.items():
                    if isinstance(v, dict):
                        result[k] = v
    return result


def validate(old_val, cur_val, field):
    """Возвращает None если ок, иначе строку с причиной для корзины 'подозрительно'."""
    old_tokens = sorted(TOKEN_RE.findall(old_val))
    cur_tokens = sorted(TOKEN_RE.findall(cur_val))
    if old_tokens != cur_tokens:
        return "токены разошлись: en=%s ru=%s" % (cur_tokens, old_tokens)
    old_ph = sorted(PLACEHOLDER_RE.findall(old_val))
    cur_ph = sorted(PLACEHOLDER_RE.findall(cur_val))
    if old_ph != cur_ph:
        return "плейсхолдеры разошлись: en=%s ru=%s" % (cur_ph, old_ph)
    old_tags = sorted(TAG_RE.findall(old_val))
    cur_tags = sorted(TAG_RE.findall(cur_val))
    if old_tags != cur_tags:
        return "разметка разошлась: en=%s ru=%s" % (cur_tags, old_tags)
    if not old_val.strip():
        return "пустой перевод"
    return None


def import_category(category):
    print("=== %s ===" % category)
    old = load_category(OLD_DATA, category)
    cur = load_category(CUR_DATA, category)
    print("старый перевод: %d записей, текущие данные игры: %d записей" % (len(old), len(cur)))

    accepted = {}
    stale = []
    suspicious = []

    for str_name, old_obj in old.items():
        if str_name not in cur:
            stale.append(str_name)
            continue
        cur_obj = cur[str_name]
        entry = {}
        for field in TRANSLATABLE:
            old_val = old_obj.get(field)
            cur_val = cur_obj.get(field)
            if old_val is None or cur_val is None:
                continue
            if not isinstance(old_val, str) or not isinstance(cur_val, str):
                continue
            reason = validate(old_val, cur_val, field)
            if reason:
                suspicious.append({"strName": str_name, "field": field, "en": cur_val, "ru": old_val, "reason": reason})
                continue
            entry[field] = old_val
        if entry:
            accepted[str_name] = entry

    out_path = os.path.join(ROOT, "langs", "ru", "data", category + ".json")
    existing = {}
    if os.path.exists(out_path):
        existing = json.loads(io.open(out_path, encoding="utf-8").read())
    existing.update(accepted)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    io.open(out_path, "w", encoding="utf-8").write(json.dumps(existing, ensure_ascii=False, indent=2))

    print("принято: %d, устарело (нет в текущей игре): %d, подозрительно (в карантин): %d" %
          (len(accepted), len(stale), len(suspicious)))
    return {"category": category, "accepted": len(accepted), "stale": stale, "suspicious": suspicious}


def main():
    categories = sys.argv[1:] or ["interactions"]
    reports = [import_category(c) for c in categories]
    report_path = os.path.join(ROOT, "lang_src", "old_import_report.json")
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    io.open(report_path, "w", encoding="utf-8").write(json.dumps(reports, ensure_ascii=False, indent=2))
    print("отчёт: %s" % report_path)


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: Прогнать на категории `interactions`**

```bash
cd /f/DEV2/ostra_i18n && /c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe tools/import_old_translation.py interactions
```

Ожидается: `принято: N` в диапазоне сотен (старый перевод содержал 736 интеракций), `устарело`/`подозрительно` — не ноль (это нормально, старый перевод собирался до нескольких обновлений игры).

- [ ] **Step 3: Проверить, что тестовая запись из Task 1 не потерялась**

```bash
grep -A1 "ACTAddConnection" /f/DEV2/ostra_i18n/langs/ru/data/interactions.json
```

Ожидается: `"strTitle": "ПРОВЕРКА_КОНТЕНТА"` — **если импорт перезаписал её реальным переводом, это нормально и ожидаемо** (`existing.update(accepted)` в Step 1 отдаёт приоритет новым данным импорта), но перед этим стоит сверить, что новое значение осмысленно (не мусор), открыв `langs/ru/data/interactions.json` и посмотрев на `ACTAddConnection` глазами.

- [ ] **Step 4: Убрать временный self-test-код из `ContentOverlay.cs`**

Удалить блок, добавленный в Task 1 Step 7 (`if (DataHandler.dictInteractions.TryGetValue("ACTAddConnection", ...`) — он был нужен только для диагностики среза без реального перевода, дальше self-test будет опираться на реальные импортированные записи через Task 4.

- [ ] **Step 5: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: old translation import tool with strName-only matching and validation buckets"
```

---

## Task 3: Валидатор — офлайн-проверка при сборке пака

**Files:**
- Create: `tools/validate_content_overlay.py`

**Interfaces:**
- Consumes: `langs/ru/data/*.json` (оверлей), `Ostranauts_Data/StreamingAssets/data/<категория>/*.json` (текущие данные игры — источник `en`)
- Produces: код возврата процесса (`0` — чисто, `1` — найдены блокирующие ошибки), человекочитаемый отчёт в stdout

- [ ] **Step 1: Написать валидатор**

```python
#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
validate_content_overlay.py — офлайн-проверка langs/ru/data/*.json перед деплоем.
Блокирует (exit 1): расхождение токенов/плейсхолдеров/разметки, сироты (strName
отсутствует в текущих данных игры). Предупреждает без блокировки: перевод совпадает
с оригиналом дословно (вероятно забыт).

Запуск: python validate_content_overlay.py
"""
import io
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__)))
from import_old_translation import load_category, validate, CUR_DATA, TRANSLATABLE  # noqa: E402

ROOT = r"F:\DEV2\ostra_i18n"
RU_DATA = os.path.join(ROOT, "langs", "ru", "data")


def main():
    if not os.path.isdir(RU_DATA):
        print("нет langs/ru/data — нечего проверять")
        return 0

    errors = 0
    warnings = 0
    for fn in sorted(os.listdir(RU_DATA)):
        if not fn.endswith(".json"):
            continue
        category = fn[:-5]
        overlay = json.loads(io.open(os.path.join(RU_DATA, fn), encoding="utf-8").read())
        cur = load_category(CUR_DATA, category)
        print("=== %s: %d записей в оверлее ===" % (category, len(overlay)))

        for str_name, fields in overlay.items():
            if str_name not in cur:
                print("  ОШИБКА: сирота '%s' — отсутствует в текущих данных игры" % str_name)
                errors += 1
                continue
            cur_obj = cur[str_name]
            for field, ru_val in fields.items():
                if field not in TRANSLATABLE:
                    print("  ОШИБКА: '%s'.%s — поле не в списке переводимых" % (str_name, field))
                    errors += 1
                    continue
                en_val = cur_obj.get(field)
                if en_val is None:
                    print("  ОШИБКА: '%s'.%s — поля нет в текущих данных игры" % (str_name, field))
                    errors += 1
                    continue
                reason = validate(ru_val, en_val, field)
                if reason:
                    print("  ОШИБКА: '%s'.%s — %s" % (str_name, field, reason))
                    errors += 1
                    continue
                if ru_val.strip() == en_val.strip() and len(en_val.strip()) > 3:
                    print("  предупреждение: '%s'.%s — перевод совпадает с оригиналом" % (str_name, field))
                    warnings += 1

    print("итого: ошибок %d, предупреждений %d" % (errors, warnings))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 2: Прогнать**

```bash
cd /f/DEV2/ostra_i18n && /c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe tools/validate_content_overlay.py
echo "exit=$?"
```

Ожидается: `exit=0` (если Task 2 отбраковал плохие записи корректно — в оверлее должны остаться только уже провалидированные записи; повторная проверка тем же правилам обязана пройти чисто, иначе это баг в самом валидаторе или в `import_old_translation.py`, а не в данных).

**Гейт:** если `exit != 0` — не переходить к Task 4, разбираться, почему одна и та же логика валидации (`validate()`, общая функция) дала разные результаты на импорте и на проверке.

- [ ] **Step 3: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: offline validator for content overlay (placeholder/token parity, orphans)"
```

---

## Task 4: Расширение на дополнительные категории + живая проверка

По уроку Фазы 1/2 (утверждать не всё сразу) — здесь добавляется небольшое число новых категорий поверх уже проверенной `interactions`, не весь список из 82.

**Files:**
- Modify: `plugin/OstraI18n/ContentOverlay.cs` (таблица `CategoryToField`)
- Modify: `langs/ru/data/careers.json`, `langs/ru/data/conditions.json` (созданы через `import_old_translation.py`)

**Interfaces:**
- Consumes: `DataHandler.dictCareers` (`Dictionary<string, JsonCareer>`), `DataHandler.dictConds` (`Dictionary<string, JsonCond>`) — оба существующие публичные статические поля игры
- Produces: расширенный рабочий оверлей, живое логовое подтверждение без исключений

- [ ] **Step 1: Проверить поля-кандидаты в decompiled-источниках**

```bash
grep -n "public string str" "/f/DEV2/ostra_i18n/decompiled/JsonCareer.cs"
grep -n "public string str" "/f/DEV2/ostra_i18n/decompiled/JsonCond.cs"
```

Убедиться, что среди найденных свойств есть хотя бы одно из `TranslatableFields` (`strTitle`/`strDesc`/`strTooltip`/`strNameFriendly`/`strNameShort`/`strFriendlyName`) — если нет ни одного, эту категорию пропустить (нечего переводить через этот механизм) и выбрать другую из списка вызовов `LoadModJsons` в `decompiled/DataHandler.cs:915-1000`.

- [ ] **Step 2: Добавить категории в `ContentOverlay.CategoryToField`**

```csharp
        private static readonly Dictionary<string, string> CategoryToField = new Dictionary<string, string>
        {
            { "interactions", "dictInteractions" },
            { "careers", "dictCareers" },
            { "conditions", "dictConds" },
        };
```

- [ ] **Step 3: Импортировать старый перевод для новых категорий**

```bash
cd /f/DEV2/ostra_i18n && /c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe tools/import_old_translation.py careers conditions
```

- [ ] **Step 4: Валидатор**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe tools/validate_content_overlay.py
echo "exit=$?"
```

Ожидается: `exit=0`.

- [ ] **Step 5: Собрать, задеплоить, запустить**

```bash
cd /f/DEV2/ostra_i18n/plugin/OstraI18n && dotnet build -c Release 2>&1 | tail -10
powershell.exe -Command "Get-Process -Name Ostranauts -ErrorAction SilentlyContinue | Stop-Process -Force"
DST="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/plugins/OstraI18n"
cp -f /f/DEV2/ostra_i18n/plugin/OstraI18n/bin/Release/netstandard2.1/OstraI18n.dll "$DST/OstraI18n.dll"
cp -f /f/DEV2/ostra_i18n/core/OstraI18n.Core/bin/Release/netstandard2.1/OstraI18n.Core.dll "$DST/OstraI18n.Core.dll"
cp -f /f/DEV2/ostra_i18n/langs/ru/data/interactions.json "$DST/langs/ru/data/interactions.json"
cp -f /f/DEV2/ostra_i18n/langs/ru/data/careers.json "$DST/langs/ru/data/careers.json"
cp -f /f/DEV2/ostra_i18n/langs/ru/data/conditions.json "$DST/langs/ru/data/conditions.json"
rm -f "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
powershell.exe -Command "Start-Process -FilePath 'F:\Games\Steam\steamapps\common\Ostranauts\RUNSAVE.bat' -WorkingDirectory 'F:\Games\Steam\steamapps\common\Ostranauts'"
```

- [ ] **Step 6: Проверить лог**

```bash
LOG="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
until grep -q "контент-оверлей:" "$LOG" 2>/dev/null; do sleep 3; done
sleep 5
grep -E "контент-оверлей:|Exception" "$LOG"
grep -c "Exception" "$LOG"
grep -c "OUTPUTTING STACK TRACE" "/c/Users/Low/AppData/LocalLow/Blue Bottle Games/Ostranauts/Player.log" 2>/dev/null
```

Ожидается: `контент-оверлей: применено полей N, сирот M` (N — сотни, если импорт Task 2/Task 3 прошёл на реальных данных), `0` исключений, `0` крашей.

**Гейт:** ненулевой `Exception` — откатить последнее изменение `CategoryToField` (убрать последнюю добавленную категорию) до диагностики; механизм через рефлексию по конкретному словарю затрагивает только объекты этого типа, не весь UI — откат безопасен и локален.

- [ ] **Step 7: Попросить пользователя визуально подтвердить**

Открыть в игре экран, где отображается описание любой интеракции из переведённого набора (например, навести на пункт контекстного меню объекта — тултип интеракции берётся из `strTooltip`/`strTitle`), и подтвердить, что текст на русском.

- [ ] **Step 8: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: expand content overlay to careers and conditions categories"
```

---

## Определение готовности фазы

1. `dotnet run` в `core/OstraI18n.Core.Tests` даёт `ALL PASS`, код возврата 0
2. `tools/validate_content_overlay.py` — код возврата `0`
3. В логе игры: `контент-оверлей: применено полей N, сирот M` с `N > 0`
4. `grep -c "OUTPUTTING STACK TRACE"` по `Player.log` — `0`
5. `grep -c "Exception"` по `BepInEx/LogOutput.log` — `0`
6. Пользователь визуально подтвердил перевод хотя бы одной строки игровых данных (не UI-текста — это уже покрыто Фазами 1-2)
7. Все изменения закоммичены

## Что НЕ входит в эту фазу

- Полный охват всех ~82 категорий данных игры (`LoadModJsons`-вызовов в `DataHandler.cs`) — только `interactions`, `careers`, `conditions` как проверенный конвейер
- Импорт `rus_phrases.json` (560 пар UI-текста) — это UI-слой (Фазы 1-2 каталоги), не контент-слой; отдельная задача сопоставления с `catalog/literals.json`/`catalog/prefabs.json`
- Формат-строки для конкатенации (270 мест), псевдоязык, детектор переполнения, пакет для разработчиков — Фаза 4
