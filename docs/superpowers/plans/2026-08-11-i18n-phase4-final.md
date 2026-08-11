# OstraI18n Фаза 4 — раздувание текста, формат-строки, пакет для разработчиков Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Закрыть оставшиеся пункты границ v1 из спеки: QA-режим (псевдоязык + детектор переполнения + `maxLen`), офлайн-каталог 270 мест композиции строк (только отчёт для ручной проверки — спека прямо запрещает автоматическое применение здесь), и сборка самодостаточного пакета `ostranauts-i18n/` для передачи разработчикам игры.

**Architecture:** QA-режим — новый флаг конфига (`Language = "qa"`), оборачивающий каждое разрешённое значение маркерами прямо в `I18n.Get`/`LanguagePack`, без изменения формата языковых паков. Детектор переполнения — опциональная проверка в `LocalizedText.Apply()` через `TMP_Text.GetPreferredValues()` против размера `RectTransform`, пишет в файл-отчёt (тот же паттерн self-test-файлов, что уже используется в `Plugin.cs`). Извлечение формат-строк — новый Cecil-инструмент `tools_cs/FormatExtract`, переиспользующий `TextSinks`/`MethodKey` из существующего `CatalogExtract`, находит вызовы `String.Concat`/`String.Format`, достигающие sink, и репортит литеральные фрагменты в их методе — не патчит, только пишет `patches/formats.md`. Пакет для разработчиков — копирующий скрипт, а не новый код: собирает уже существующие `plugin/OstraI18n/*.cs`, `catalog/`, `langs/`, `tools/` в `dist/ostranauts-i18n/` по структуре из спеки.

## Global Constraints

- Игра: Unity 6000.3.10f1, Mono, BepInEx 6.0.0-be.785 — тот же плагин `plugin/OstraI18n/OstraI18n.csproj`, netstandard2.1.
- Формат-строки (270 мест) — **только извлечение и отчёт**, спека прямо требует ручной проверки перед применением (`docs/.../design.md:610`: «нужна проверка человеком»); в этой фазе ни один рантайм-патч на конкатенацию не пишется.
- Извлечение опирается на `TextSinks` (`TMPro.TMP_Text::set_text`, `UnityEngine.UI.Text::set_text`, `TMPro.TMP_Text::SetText`) и `OstraI18n.Core.MethodKey.Make(...)` — те же, что в `tools_cs/CatalogExtract/Program.cs:14-19,60`.
- Пакет для разработчиков собирается в `dist/ostranauts-i18n/` (вне `.gitignore`, но сам `dist/` добавляется в `.gitignore` — генерируемый артефакт, не источник истины; пересобирается скриптом).
- Деплой плагина в `F:\Games\Steam\steamapps\common\Ostranauts\BepInEx\plugins\OstraI18n\` — **всегда обе сборки** (`OstraI18n.dll` и `OstraI18n.Core.dll`), урок Фазы 2 Task 5 (см. `docs/baseline.md`).

---

## Task 1: QA-режим — псевдоязык

**Files:**
- Modify: `plugin/OstraI18n/I18n.cs`
- Modify: `plugin/OstraI18n/Plugin.cs` (новый `ConfigEntry<bool> QaMode`)

**Interfaces:**
- Consumes: существующий `I18n.Get(string key)` (сигнатура не меняется)
- Produces: `I18n.QaMode` (`public static bool`) — читается в Task 2 детектором переполнения, чтобы не считать в обычной игре

- [ ] **Step 1: Прочитать текущий `I18n.cs`**

```bash
cat /f/DEV2/ostra_i18n/plugin/OstraI18n/I18n.cs
```

Убедиться, что `Get(string key)` возвращает `pack?.Get(key) ?? key` (или эквивалент) одной строкой — именно эту точку возврата нужно обернуть.

- [ ] **Step 2: Добавить `QaMode` и обёртку**

В `I18n.cs`, рядом с существующими статическими полями, добавить:

```csharp
        public static bool QaMode;

        private static string Wrap(string value) => QaMode ? "⟦" + value + "⟧" : value;
```

Найти место, где `Get` возвращает финальное значение (после fallback-цепочки), и обернуть результат в `Wrap(...)` — **только один return-path**, не в середине цепочки fallback (иначе замусорится сравнение `en == ru` в других инструментах).

- [ ] **Step 3: Добавить конфиг в `Plugin.cs`**

После существующего `FormalYou` `ConfigEntry`:

```csharp
            var qaMode = Config.Bind("General", "QaMode", false, "true = псевдоязык ⟦...⟧ поверх переводов, для поиска непокрытых строк.");
            I18n.QaMode = qaMode.Value;
```

Разместить эту строку **после** `I18n.Init(...)` в `Awake()` (иначе `I18n.QaMode` установится раньше, чем класс проинициализирован — поле статическое, порядок неважен для самого поля, но для ясности кода держать рядом с остальной инициализацией `I18n`).

- [ ] **Step 4: Собрать, задеплоить с `QaMode=true`, проверить**

```bash
cd /f/DEV2/ostra_i18n/plugin/OstraI18n && dotnet build -c Release 2>&1 | tail -10
```

Ожидается: `Ошибок: 0`.

```bash
powershell.exe -Command "Get-Process -Name Ostranauts -ErrorAction SilentlyContinue | Stop-Process -Force"
DST="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/plugins/OstraI18n"
cp -f /f/DEV2/ostra_i18n/plugin/OstraI18n/bin/Release/netstandard2.1/OstraI18n.dll "$DST/OstraI18n.dll"
cp -f /f/DEV2/ostra_i18n/core/OstraI18n.Core/bin/Release/netstandard2.1/OstraI18n.Core.dll "$DST/OstraI18n.Core.dll"
```

Включить `QaMode` в конфиге:

```bash
CFG="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/config/com.coreforge.ostra.i18n.cfg"
grep -n "QaMode" "$CFG" || echo "первый запуск ещё не создал ключ — запустить игру один раз, затем повторить эту команду"
```

Если ключ уже есть (после первого запуска Step 4 создаст его автоматически со значением `false`), заменить на `true`:

```bash
sed -i 's/QaMode = false/QaMode = true/' "$CFG"
rm -f "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
powershell.exe -Command "Start-Process -FilePath 'F:\Games\Steam\steamapps\common\Ostranauts\RUNSAVE.bat' -WorkingDirectory 'F:\Games\Steam\steamapps\common\Ostranauts'"
```

- [ ] **Step 5: Проверить лог и попросить пользователя посмотреть на экран**

```bash
LOG="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
until grep -q "литералы:" "$LOG" 2>/dev/null; do sleep 3; done
grep -c "Exception" "$LOG"
```

Ожидается: `0`. Попросить пользователя подтвердить, что переведённый текст обёрнут в `⟦...⟧`, а непереведённый (ещё английский) — нет. Это единственный способ отличить «не покрыто» от «не переведено» одним взглядом.

- [ ] **Step 6: Выключить `QaMode` обратно и коммит**

```bash
sed -i 's/QaMode = true/QaMode = false/' "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/config/com.coreforge.ostra.i18n.cfg"
cd /f/DEV2/ostra_i18n && git add -A -- plugin/OstraI18n/I18n.cs plugin/OstraI18n/Plugin.cs
git commit -m "feat: QA pseudo-language mode (wraps resolved strings in ⟦...⟧)"
```

---

## Task 2: Детектор переполнения + `maxLen`

**Files:**
- Modify: `plugin/OstraI18n/LocalizedText.cs`
- Modify: `plugin/OstraI18n/I18n.cs` (добавить `MaxLenExceeded` счётчик для лога)

**Interfaces:**
- Consumes: `I18n.QaMode` (из Task 1)
- Produces: `LocalizedText.OverflowReportPath` (`static string`, путь к файлу отчёта — читается только человеком, не другим кодом)

- [ ] **Step 1: Добавить проверку переполнения в `LocalizedText.Apply()`**

Открыть `plugin/OstraI18n/LocalizedText.cs`, найти блок, где `tmp.text = value` — сразу после присвоения, только если `I18n.QaMode`:

```csharp
        internal static string OverflowReportPath;

        internal void Apply()
        {
            if (string.IsNullOrEmpty(Key)) return;
            var value = I18n.Get(Key);
            if (string.IsNullOrEmpty(value)) return;

            var tmp = GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = value;
                if (I18n.QaMode) CheckOverflow(tmp, value);
                return;
            }

            var legacy = GetComponent<UnityEngine.UI.Text>();
            if (legacy != null) legacy.text = value;
        }

        private void CheckOverflow(TMP_Text tmp, string value)
        {
            try
            {
                var rect = tmp.rectTransform.rect;
                var preferred = tmp.GetPreferredValues(value, rect.width > 0 ? rect.width : 10000f, 0f);
                bool overflowsWidth = rect.width > 0 && preferred.x > rect.width * 1.02f;
                bool overflowsHeight = rect.height > 0 && preferred.y > rect.height * 1.02f;
                if (!overflowsWidth && !overflowsHeight) return;

                var line = Key + "\t" + GetPath(transform) + "\tширина " + preferred.x.ToString("F0") +
                           "/" + rect.width.ToString("F0") + "\tвысота " + preferred.y.ToString("F0") +
                           "/" + rect.height.ToString("F0");
                if (!string.IsNullOrEmpty(OverflowReportPath))
                    System.IO.File.AppendAllText(OverflowReportPath, line + "\n");
            }
            catch (System.Exception) { /* детектор не должен ронять игру */ }
        }

        private static string GetPath(Transform t)
        {
            var segs = new System.Collections.Generic.List<string>();
            for (var cur = t; cur != null; cur = cur.parent) segs.Insert(0, cur.name);
            return string.Join("/", segs);
        }
```

- [ ] **Step 2: Подключить путь отчёта в `Plugin.cs`**

После блока `ContentOverlay.Init(...)`:

```csharp
            if (qaMode.Value)
            {
                LocalizedText.OverflowReportPath = Path.Combine(Paths.PluginPath, "OstraI18n", "overflow_report.tsv");
                File.WriteAllText(LocalizedText.OverflowReportPath, "key\tpath\twidth\theight\n");
            }
```

- [ ] **Step 3: Собрать, задеплоить с `QaMode=true`, воспроизвести известный дефект**

```bash
cd /f/DEV2/ostra_i18n/plugin/OstraI18n && dotnet build -c Release 2>&1 | tail -10
```

Ожидается: `Ошибок: 0`.

```bash
powershell.exe -Command "Get-Process -Name Ostranauts -ErrorAction SilentlyContinue | Stop-Process -Force"
DST="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/plugins/OstraI18n"
cp -f /f/DEV2/ostra_i18n/plugin/OstraI18n/bin/Release/netstandard2.1/OstraI18n.dll "$DST/OstraI18n.dll"
cp -f /f/DEV2/ostra_i18n/core/OstraI18n.Core/bin/Release/netstandard2.1/OstraI18n.Core.dll "$DST/OstraI18n.Core.dll"
sed -i 's/QaMode = false/QaMode = true/' "$DST/../../config/com.coreforge.ostra.i18n.cfg" 2>/dev/null || true
rm -f "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
powershell.exe -Command "Start-Process -FilePath 'F:\Games\Steam\steamapps\common\Ostranauts\RUNSAVE.bat' -WorkingDirectory 'F:\Games\Steam\steamapps\common\Ostranauts'"
```

- [ ] **Step 4: Проверить отчёт**

```bash
sleep 60
cat "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/plugins/OstraI18n/overflow_report.tsv"
grep -c "Exception" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
```

Ожидается: `0` исключений. Строки отчёта появятся только для объектов, реально привязанных через `LocalizedText` (Фаза 2/3) — известный дефект `Tutorial: Time Controls` из спеки живёт в отдельной, ещё не привязанной через каталог области (это подтверждено самой спекой как «текущий, не гипотетический» дефект verstka, обнаруженный вручную, не через этот детектор) и может не попасть в отчёт при первом прогоне — это ожидаемо, детектор проверяет **привязанные через LocalizedText объекты**, не весь UI игры разом.

- [ ] **Step 5: Выключить QaMode, коммит**

```bash
sed -i 's/QaMode = true/QaMode = false/' "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/config/com.coreforge.ostra.i18n.cfg"
cd /f/DEV2/ostra_i18n && git add -A -- plugin/OstraI18n/LocalizedText.cs plugin/OstraI18n/Plugin.cs
git commit -m "feat: overflow detector (QA mode) reports TMP preferred-size vs container to overflow_report.tsv"
```

---

## Task 3: Извлечение каталога формат-строк (270 мест) — только отчёт

**Files:**
- Create: `tools_cs/FormatExtract/FormatExtract.csproj`
- Create: `tools_cs/FormatExtract/Program.cs`
- Create: `patches/formats.md` (генерируется, не пишется руками)

**Interfaces:**
- Consumes: `OstraI18n.Core.MethodKey.Make(...)` (та же библиотека, что и `CatalogExtract`)
- Produces: `patches/formats.md` — человекочитаемый отчёт `файл, строка (methodKey), что → на что`; НЕ применяется автоматически ни к чему

- [ ] **Step 1: Скопировать структуру проекта с `CatalogExtract`**

```bash
cat /f/DEV2/ostra_i18n/tools_cs/CatalogExtract/CatalogExtract.csproj
```

Создать `tools_cs/FormatExtract/FormatExtract.csproj` с тем же содержимым, кроме `<AssemblyName>` и `<RootNamespace>`, заменённых на `FormatExtract` (сохранить ссылки на `Mono.Cecil`/`Mono.Cecil.Rocks`/`OstraI18n.Core` в точности как в оригинале).

- [ ] **Step 2: Написать `Program.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OstraI18n.Core;

static class Program
{
    static readonly string[] TextSinks =
    {
        "TMPro.TMP_Text::set_text",
        "UnityEngine.UI.Text::set_text",
        "TMPro.TMP_Text::SetText",
    };

    // Вызовы, чей результат — составная строка (не единичный ldstr).
    static readonly string[] CompositionCalls =
    {
        "System.String::Concat",
        "System.String::Format",
    };

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("использование: FormatExtract <Assembly-CSharp.dll> <patches/formats.md>");
            return 2;
        }
        var asmPath = args[0];
        var outPath = args[1];
        if (!File.Exists(asmPath)) { Console.Error.WriteLine("нет файла: " + asmPath); return 2; }

        var asm = AssemblyDefinition.ReadAssembly(asmPath);
        var report = new List<string>();
        int sites = 0;

        foreach (var type in asm.MainModule.GetTypes())
        {
            foreach (var m in type.Methods)
            {
                if (!m.HasBody) continue;
                bool hasSink = m.Body.Instructions.Any(i => IsCall(i, TextSinks));
                if (!hasSink) continue;

                m.Body.SimplifyMacros();
                var key = MethodKey.Make(type.FullName, m.Name, m.Parameters.Count);

                foreach (var instr in m.Body.Instructions)
                {
                    if (!IsCall(instr, CompositionCalls)) continue;
                    if (!ReachesSinkWithinWindow(instr, TextSinks, 60)) continue;

                    var fragments = CollectPrecedingLiterals(instr, 30);
                    if (fragments.Count == 0) continue;

                    sites++;
                    report.Add("### " + key + " (composition site " + sites + ")\n" +
                               string.Join("\n", fragments.Select(f => "- `" + Escape(f) + "`")) + "\n");
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        var header = "# Места композиции строк (" + sites + ")\n\n" +
                     "Извлечено автоматически, **не применено**. Каждая запись — вызов " +
                     "`String.Concat`/`String.Format`, чей результат достигает text-sink, " +
                     "и литеральные фрагменты, найденные в предшествующих 30 инструкциях того " +
                     "же метода (может включать фрагменты из несвязанной композиции чуть выше по " +
                     "методу — при вычитке проверять реальную принадлежность конкретному вызову).\n\n";
        File.WriteAllText(outPath, header + string.Join("\n", report));

        Console.WriteLine("мест композиции: " + sites);
        Console.WriteLine("записано: " + outPath);
        return 0;
    }

    static bool IsCall(Instruction i, string[] names) =>
        (i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call)
        && i.Operand is MethodReference mr
        && names.Any(s => (mr.DeclaringType.FullName + "::" + mr.Name) == s);

    static bool ReachesSinkWithinWindow(Instruction from, string[] sinks, int window)
    {
        var cur = from.Next;
        for (int i = 0; i < window && cur != null; i++, cur = cur.Next)
        {
            if (IsCall(cur, sinks)) return true;
            if (cur.OpCode == OpCodes.Stloc) return true; // сохранено в переменную — за пределами простого окна, но не false negative важнее, чем ложный positive здесь: помечаем как достигающее, ручная проверка отсеет
        }
        return false;
    }

    static List<string> CollectPrecedingLiterals(Instruction compositionCall, int window)
    {
        var result = new List<string>();
        var cur = compositionCall.Previous;
        for (int i = 0; i < window && cur != null; i++, cur = cur.Previous)
        {
            if (cur.OpCode == OpCodes.Ldstr && cur.Operand is string s && !string.IsNullOrEmpty(s))
                result.Insert(0, s);
        }
        return result;
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("`", "'").Replace("\n", "\\n");
}
```

- [ ] **Step 3: Собрать и прогнать на актуальной сборке игры**

```bash
cd /f/DEV2/ostra_i18n/tools_cs/FormatExtract && dotnet build -c Release 2>&1 | tail -10
```

Ожидается: `Ошибок: 0`.

```bash
find /f/DEV2/ostra_i18n -iname "Assembly-CSharp.dll" 2>/dev/null | head -3
```

Взять путь к той же `Assembly-CSharp.dll`, что использовалась в Фазе 1 для `CatalogExtract` (см. `docs/baseline.md` или предыдущие команды в истории — обычно `dll/Assembly-CSharp.dll` в корне проекта).

```bash
cd /f/DEV2/ostra_i18n && dotnet tools_cs/FormatExtract/bin/Release/net*/FormatExtract.dll dll/Assembly-CSharp.dll patches/formats.md
```

Ожидается: `мест композиции:` в диапазоне сотен (спека называет ориентир 270 — совпадение не обязано быть точным, эвристика окна ловит не 1:1 то же множество, что ручной подсчёт по спеке; важно, что порядок величины сопоставим — от 150 до 400 приемлемо, вне этого диапазона стоит перепроверить логику окна).

**Гейт:** если результат — единицы или тысячи (на порядок вне ожидания), не переходить к Task 4 — проверить, что `TextSinks`/`CompositionCalls` совпадают по написанию с реальными `MethodReference.DeclaringType.FullName` (можно свериться через `--probe-overloads` подход из `CatalogExtract`, если такой режим там есть).

- [ ] **Step 4: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A -- tools_cs/FormatExtract patches/formats.md
git commit -m "feat: offline extraction of string-composition sites for developer package (report only, no runtime patch)"
```

---

## Task 4: Сборка пакета для разработчиков

**Files:**
- Create: `tools/build_dev_package.py`
- Create: `dist/ostranauts-i18n/README.md`
- Create: `dist/ostranauts-i18n/MIGRATION.md`
- Modify: `.gitignore` (добавить `dist/`)

**Interfaces:**
- Consumes: `plugin/OstraI18n/*.cs`, `catalog/*.json`, `langs/`, `tools/`, `patches/formats.md`, `docs/superpowers/specs/2026-08-11-ostra-i18n-keybased-design.md`
- Produces: `dist/ostranauts-i18n/` — самодостаточная папка, без внешних ссылок на пути `F:\DEV2\ostra_i18n` или `F:\Games\...`

- [ ] **Step 1: Написать сборочный скрипт**

```python
#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_dev_package.py — собирает dist/ostranauts-i18n/ из уже существующих
частей проекта (плагин, каталоги, языковые паки, инструменты) по структуре
из спеки. Ничего не генерирует заново — только копирует и пишет README/MIGRATION.

Запуск: python build_dev_package.py
"""
import os
import shutil

ROOT = r"F:\DEV2\ostra_i18n"
OUT = os.path.join(ROOT, "dist", "ostranauts-i18n")

README = """# OstraI18n — референсная реализация key-based i18n для Ostranauts

Что это: рабочий мод (BepInEx) + референсные C#-исходники + каталоги извлечённого
текста + русский перевод, полученные без единого сопоставления по английскому тексту
(см. SPEC.md, раздел "Архитектура").

Проверить за 5 минут:
1. Скопировать содержимое `langs/` в `<Game>/BepInEx/plugins/OstraI18n/langs/`
   (или, при внедрении в исходники игры — в `StreamingAssets`, см. MIGRATION.md шаг 5).
2. Запустить игру с установленным модом (или после внедрения `src/` — без мода).
3. Русский текст должен появиться в UI, в контекстных меню объектов и в описаниях
   состояний персонажа.

Дальше: MIGRATION.md — пошаговое внедрение в исходники игры вместо мода.
"""

MIGRATION = """# Внедрение в исходники игры

| Шаг | Объём | Автоматизация |
|---|---|---|
| 1. Положить `src/` в проект | — | — |
| 2. Прогнать `editor/ApplyBindings.cs` | ~1250 объектов (каталог `catalog/prefabs.json`) | полная |
| 3. Заменить литералы на `GetString` по `catalog/literals.json` | список готов | механическая замена, требует пересборки |
| 4. Применить формат-строки из `patches/formats.md` | см. файл | diff готов, нужна проверка человеком |
| 5. Положить `langs/` в `StreamingAssets` | — | — |
| 6. Удалить BepInEx-плагин | — | плагин больше не нужен |

Шаг 6 — критерий успеха: если после шагов 1-5 плагин всё ещё требуется для перевода,
внедрение выполнено не полностью.
"""

COPY_MAP = [
    # (относительный источник, относительное назначение внутри dist/ostranauts-i18n)
    ("plugin/OstraI18n/I18n.cs", "src/I18n.cs"),
    ("plugin/OstraI18n/LocalizedText.cs", "src/LocalizedText.cs"),
    ("plugin/OstraI18n/PrefabBinder.cs", "src/PrefabBinder.cs"),
    ("plugin/OstraI18n/ContentOverlay.cs", "src/ContentOverlay.cs"),
    ("core/OstraI18n.Core/LanguagePack.cs", "src/LanguagePack.cs"),
    ("core/OstraI18n.Core/PackLoader.cs", "src/PackLoader.cs"),
    ("core/OstraI18n.Core/PluralRule.cs", "src/PluralRule.cs"),
    ("core/OstraI18n.Core/MethodKey.cs", "src/MethodKey.cs"),
    ("core/OstraI18n.Core/PathKey.cs", "src/PathKey.cs"),
    ("docs/superpowers/specs/2026-08-11-ostra-i18n-keybased-design.md", "SPEC.md"),
    ("catalog/literals.json", "catalog/literals.json"),
    ("catalog/prefabs.json", "catalog/prefabs.json"),
    ("patches/formats.md", "patches/formats.md"),
    ("tools/import_old_translation.py", "tools/import_old_translation.py"),
    ("tools/validate_content_overlay.py", "tools/validate_content_overlay.py"),
]


def main():
    if os.path.exists(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)

    missing = []
    for src_rel, dst_rel in COPY_MAP:
        src = os.path.join(ROOT, src_rel)
        dst = os.path.join(OUT, dst_rel)
        if not os.path.exists(src):
            missing.append(src_rel)
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)

    shutil.copytree(os.path.join(ROOT, "langs"), os.path.join(OUT, "langs"))

    with open(os.path.join(OUT, "README.md"), "w", encoding="utf-8") as f:
        f.write(README)
    with open(os.path.join(OUT, "MIGRATION.md"), "w", encoding="utf-8") as f:
        f.write(MIGRATION)

    if missing:
        print("ПРЕДУПРЕЖДЕНИЕ: не найдены и пропущены:")
        for m in missing:
            print("  -", m)
    print("собрано:", OUT)


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: `.gitignore`**

Добавить строку `dist/` в `/f/DEV2/ostra_i18n/.gitignore` (генерируемый артефакт, пересобирается скриптом, не хранится в репозитории).

- [ ] **Step 3: Прогнать сборку**

```bash
cd /f/DEV2/ostra_i18n && /c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe tools/build_dev_package.py
```

Ожидается: `собрано: F:\DEV2\ostra_i18n\dist\ostranauts-i18n`, без строки `ПРЕДУПРЕЖДЕНИЕ` (если она появилась — один из файлов в `COPY_MAP` переименован или удалён в более ранней фазе; сверить с фактическим деревом `plugin/OstraI18n/` и поправить список).

- [ ] **Step 4: Проверить самодостаточность**

```bash
grep -rl "F:\\\\DEV2\|F:\\\\Games" "/f/DEV2/ostra_i18n/dist/ostranauts-i18n" 2>/dev/null
```

Ожидается: пусто (никаких абсолютных путей к машине разработки внутри пакета — иначе пакет не самодостаточен на другой машине).

- [ ] **Step 5: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A -- tools/build_dev_package.py .gitignore
git commit -m "feat: developer package builder (dist/ostranauts-i18n/, self-contained, no absolute paths)"
```

---

## Определение готовности фазы

1. `dotnet run` в `core/OstraI18n.Core.Tests` даёт `ALL PASS`, код возврата 0
2. QA-режим подтверждён визуально (Task 1 Step 5) — `⟦...⟧` виден вокруг переведённого текста
3. `overflow_report.tsv` создаётся без исключений при `QaMode=true` (Task 2 Step 4)
4. `patches/formats.md` содержит от 150 до 400 записей (Task 3 Step 3)
5. `dist/ostranauts-i18n/` собирается без предупреждений о пропущенных файлах и без абсолютных путей внутри
6. `grep -c "OUTPUTTING STACK TRACE"` по `Player.log` — `0`
7. `grep -c "Exception"` по `BepInEx/LogOutput.log` — `0` на последнем живом прогоне
8. Все изменения закоммичены

## Что НЕ входит в эту фазу

- Автоматическое применение 270 формат-строк — по спеке это ручная работа, пакет только готовит diff-материал
- Импорт `rus_phrases.json` (560 пар UI-текста из старого перевода) — отдельная задача сопоставления с `catalog/literals.json`
- Полный охват всех ~82 категорий контент-данных (Фаза 3 покрыла 3 из них: interactions, careers, conditions)
- Автоматическое масштабирование шрифта — спека явно исключает этот подход (ломает единообразие интерфейса)
