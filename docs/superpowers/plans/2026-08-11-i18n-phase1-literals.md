# OstraI18n Фаза 1 — литералы: план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Перевести все захардкоженные в коде игры UI-литералы на ключи из файлов через IL-транспайлер, доказав механизм на реальном запуске игры.

**Architecture:** Офлайн-извлечение литералов из `Assembly-CSharp.dll` инструментом на Mono.Cecil даёт каталог `(метод, значение, порядковый номер) → ключ`. В рантайме Harmony-транспайлер заменяет `ldstr` на вызов `I18n.Get(ключ)`. Чистая логика вынесена в библиотеку без зависимостей от Unity и покрыта тестами; часть, завязанная на Unity, проверяется через лог, а не через скриншоты.

**Tech Stack:** C# (netstandard2.1 / net8.0), Mono.Cecil 0.11 (из BepInEx), HarmonyX, BepInEx 6.0.0-be.785, Unity 6000.3.10f1 Mono, Python 3.14 + UnityPy (для последующих фаз).

## Global Constraints

- Рабочая директория проекта: `F:\DEV2\ostra_i18n`
- Директория игры: `F:\Games\Steam\steamapps\common\Ostranauts`
- Python для инструментов: `C:\Users\Low\AppData\Local\Programs\Python\Python314\python.exe` (в этом плане используется только для проверок JSON)
- **Извлечение только из `F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\Managed\Assembly-CSharp.dll`.** Копия в `F:\DEV2\ostra_i18n\dll\` устарела (6 августа против 11-го) и использоваться не должна.
- Целевой фреймворк плагина: `netstandard2.1`. Инструменты (`CatalogExtract`, тесты): `net8.0`.
- `BaseUnityPlugin` живёт в неймспейсе `BepInEx.Unity.Mono`.
- Ссылаться на фасад `UnityEngine.dll`, не на `UnityEngine.CoreModule.dll` напрямую.
- Формат JSON всех артефактов: UTF-8 без BOM, отступ 2 пробела, `ensure_ascii=false`.
- **Проверка результата только через лог `BepInEx/LogOutput.log` и коды возврата команд.** Утверждение «работает» без вывода команды в этом плане считается невыполненным шагом.
- Мод обязан деградировать к ванильному английскому при любой ошибке, а не ронять игру.

---

## Структура файлов

Создаётся:

| Путь | Ответственность |
|---|---|
| `.gitignore` | исключение декомпилята, ассетов, архивов из репозитория |
| `core/OstraI18n.Core/MethodKey.cs` | построение и нормализация идентификатора метода |
| `core/OstraI18n.Core/LiteralEntry.cs` | запись каталога литералов |
| `core/OstraI18n.Core/CatalogFile.cs` | чтение и запись `catalog/literals.json` |
| `core/OstraI18n.Core/PluralRule.cs` | разбор и применение правила множественного числа |
| `core/OstraI18n.Core/LanguagePack.cs` | строки языка, поиск с fallback, плюрализация |
| `core/OstraI18n.Core/PackLoader.cs` | чтение `langs/` с диска |
| `core/OstraI18n.Core.Tests/Program.cs` | консольный тест-раннер, код возврата 0/1 |
| `tools_cs/CatalogExtract/Program.cs` | извлечение литералов из сборки через Mono.Cecil |
| `catalog/literals.json` | результат извлечения |
| `langs/en/ui/*.json`, `langs/ru/ui/*.json` | строки по экранам |
| `langs/*/meta.json` | метаданные языка, правило плюрализации, шрифты |

Модифицируется:

| Путь | Что меняется |
|---|---|
| `plugin/OstraI18n/OstraI18n.csproj` | ссылка на `OstraI18n.Core` |
| `plugin/OstraI18n/Plugin.cs` | подключение `LiteralPatcher`, удаление свипа сцены |
| `plugin/OstraI18n/I18n.cs` (создаётся) | фасад рантайма |
| `plugin/OstraI18n/LiteralPatcher.cs` (создаётся) | транспайлер |
| `plugin/OstraI18n/GuiText.cs` | **удаляется** в Task 8 |

---

## Task 0: Репозиторий и фиксация исходного состояния

**Files:**
- Create: `F:\DEV2\ostra_i18n\.gitignore`
- Create: `F:\DEV2\ostra_i18n\docs\baseline.md`

**Interfaces:**
- Consumes: ничего
- Produces: git-репозиторий, зафиксированное рабочее состояние для отката

- [ ] **Step 1: Создать `.gitignore`**

Файл `F:\DEV2\ostra_i18n\.gitignore`:

```gitignore
decompiled/
dll/
bepinex6/
bepinex6_be/
bepinex_staging/
data_live/
mod_study/
i18n_release/
i18n_sourse/
plugin_keep/
*.zip
*.log
**/bin/
**/obj/
lang_src/*.txt
весь контекст старой нейронки.txt
```

- [ ] **Step 2: Инициализировать репозиторий**

```bash
cd /f/DEV2/ostra_i18n && git init && git add .gitignore && git commit -m "chore: init repo with gitignore"
```

Ожидается: `Initialized empty Git repository` и один коммит.

- [ ] **Step 3: Проверить, что тяжёлое не попало в индекс**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git status --porcelain | wc -l && git ls-files | grep -c "decompiled/" || echo "decompiled excluded OK"
```

Ожидается: число файлов в первой команде **меньше 500**; вторая печатает `decompiled excluded OK`.
Если `decompiled/` попал в индекс — исправить `.gitignore` и повторить `git rm -r --cached decompiled`.

- [ ] **Step 4: Зафиксировать исходное поведение игры**

Запустить игру и дождаться загрузки:

```bash
cd /f/Games/Steam/steamapps/common/Ostranauts && cmd.exe /c RUNSAVE.bat
```

Через 60 секунд снять состояние:

```bash
grep -E "patches ok|pack Russian|OUTPUTTING STACK TRACE" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log" "/c/Users/Low/AppData/LocalLow/Blue Bottle Games/Ostranauts/Player.log"
```

Ожидается: строка вида `12 patches ok, 0 failed`, строка `pack Russian`, и **отсутствие** `OUTPUTTING STACK TRACE`.

- [ ] **Step 5: Записать baseline**

Файл `docs/baseline.md` — вставить фактический вывод шага 4 и добавить строку с хешем сборки:

```bash
sha1sum "/f/Games/Steam/steamapps/common/Ostranauts/Ostranauts_Data/Managed/Assembly-CSharp.dll" >> docs/baseline.md
```

- [ ] **Step 6: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "chore: baseline state before i18n rewrite"
```

---

## Task 1: Вертикальный срез — доказать транспайлер на одном литерале

Самый рискованный элемент проверяется первым, до постройки инфраструктуры. Если Harmony не сможет найти метод или подменить `ldstr`, вся дальнейшая работа бессмысленна.

**Files:**
- Create: `plugin/OstraI18n/LiteralPatcher.cs`
- Modify: `plugin/OstraI18n/Plugin.cs`

**Interfaces:**
- Consumes: baseline из Task 0
- Produces: `LiteralPatcher.ApplySlice()` — доказательство механизма; класс переписывается в Task 6

- [ ] **Step 1: Подтвердить цель в текущей сборке игры**

```bash
grep -n 'text = "At Large"' /f/DEV2/ostra_i18n/decompiled/GUIChargenCareer.cs
```

Ожидается: строка `936:\t\t\t\t\tcomponent2.text = "At Large";`
Если строки нет — декомпилят устарел относительно обновлённой игры. Остановиться и пересоздать декомпилят перед продолжением.

- [ ] **Step 2: Написать транспайлер для одного литерала**

Файл `plugin/OstraI18n/LiteralPatcher.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace OstraI18n
{
    // Заменяет ldstr-литералы в методах игры на значения из языкового пакета.
    // Task 1 — вертикальный срез на одном литерале, доказывающий механизм.
    internal static class LiteralPatcher
    {
        // Значение подставляется вместо литерала. В Task 6 заменяется на I18n.Get(ключ).
        public static string SliceReplacement = "На свободе";
        private const string SliceLiteral = "At Large";

        public static int Applied;

        public static void ApplySlice(Harmony harmony)
        {
            var type = AccessTools.TypeByName("GUIChargenCareer");
            if (type == null)
            {
                Plugin.Log.LogError("[i18n] slice: тип GUIChargenCareer не найден");
                return;
            }

            var transpiler = new HarmonyMethod(
                typeof(LiteralPatcher).GetMethod(nameof(SliceTranspiler),
                    BindingFlags.NonPublic | BindingFlags.Static));

            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                              | BindingFlags.Instance | BindingFlags.Static
                                              | BindingFlags.DeclaredOnly))
            {
                if (m.IsAbstract || m.ContainsGenericParameters) continue;
                try
                {
                    if (!MethodHasLiteral(m, SliceLiteral)) continue;
                    harmony.Patch(m, transpiler: transpiler);
                    Plugin.Log.LogInfo("[i18n] slice: пропатчен " + type.Name + "." + m.Name);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[i18n] slice: пропуск " + m.Name + ": " + ex.Message);
                }
            }
            Plugin.Log.LogInfo("[i18n] slice: подстановок применено " + Applied);
        }

        private static bool MethodHasLiteral(MethodBase m, string literal)
        {
            try
            {
                foreach (var instr in PatchProcessor.ReadMethodBody(m))
                    if (instr.Value is string s && s == literal) return true;
            }
            catch { }
            return false;
        }

        private static IEnumerable<CodeInstruction> SliceTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instr in instructions)
            {
                if (instr.opcode == System.Reflection.Emit.OpCodes.Ldstr
                    && (instr.operand as string) == SliceLiteral)
                {
                    Applied++;
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldstr, SliceReplacement);
                }
                else
                {
                    yield return instr;
                }
            }
        }
    }
}
```

- [ ] **Step 3: Подключить срез в `Plugin.cs`**

В `plugin/OstraI18n/Plugin.cs`, в методе `Awake`, сразу после строки
`VersionGuard.CheckAndLog(Log);` добавить:

```csharp
            try { LiteralPatcher.ApplySlice(new Harmony(GUID + ".literals")); }
            catch (Exception ex) { Log.LogError("[i18n] slice failed: " + ex); }
```

- [ ] **Step 4: Собрать плагин**

```bash
cd /f/DEV2/ostra_i18n/plugin/OstraI18n && dotnet build -c Release 2>&1 | tail -5
```

Ожидается: `Сборка успешно завершена.` и `Ошибок: 0`.

- [ ] **Step 5: Задеплоить и запустить**

```bash
cp -f /f/DEV2/ostra_i18n/plugin/OstraI18n/bin/Release/netstandard2.1/OstraI18n.dll \
      "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/plugins/OstraI18n/OstraI18n.dll" \
&& cd /f/Games/Steam/steamapps/common/Ostranauts && cmd.exe /c RUNSAVE.bat
```

- [ ] **Step 6: Проверить лог — механизм работает**

Через 60 секунд:

```bash
grep -E "slice:" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
grep -c "OUTPUTTING STACK TRACE" "/c/Users/Low/AppData/LocalLow/Blue Bottle Games/Ostranauts/Player.log"
```

Ожидается: строка `slice: пропатчен GUIChargenCareer.<имя метода>`, строка `slice: подстановок применено 1` (или больше), и `0` во второй команде.

**Гейт:** если `подстановок применено 0` — транспайлер не сработал, дальше идти нельзя. Проверить, что метод не заинлайнен и что `ReadMethodBody` возвращает инструкции.
**Гейт:** если появился `OUTPUTTING STACK TRACE` — откатить `git checkout plugin/` и разбираться до продолжения.

- [ ] **Step 7: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: prove IL literal transpiler on single literal"
```

---

## Task 2: Библиотека Core и ключ метода

**Files:**
- Create: `core/OstraI18n.Core/OstraI18n.Core.csproj`
- Create: `core/OstraI18n.Core/MethodKey.cs`
- Create: `core/OstraI18n.Core.Tests/OstraI18n.Core.Tests.csproj`
- Create: `core/OstraI18n.Core.Tests/Program.cs`

**Interfaces:**
- Consumes: ничего
- Produces: `MethodKey.Normalize(string typeFullName) → string`, `MethodKey.Make(string typeFullName, string methodName, int paramCount) → string`

- [ ] **Step 1: Создать проект библиотеки**

Файл `core/OstraI18n.Core/OstraI18n.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.1;net8.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <AssemblyName>OstraI18n.Core</AssemblyName>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Написать падающий тест на нормализацию имени типа**

Файл `core/OstraI18n.Core.Tests/OstraI18n.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OstraI18n.Core\OstraI18n.Core.csproj" />
  </ItemGroup>
</Project>
```

Файл `core/OstraI18n.Core.Tests/Program.cs`:

```csharp
using System;
using System.Collections.Generic;
using OstraI18n.Core;

static class Program
{
    static int failed = 0;

    static void Eq(string actual, string expected, string name)
    {
        if (actual == expected) { Console.WriteLine("  PASS " + name); }
        else { failed++; Console.WriteLine("  FAIL " + name + ": ожидалось '" + expected + "', получено '" + actual + "'"); }
    }

    static int Main()
    {
        Console.WriteLine("MethodKey");
        // Cecil отдаёт вложенные типы через '/', Reflection — через '+'.
        // Без нормализации ключи каталога и рантайма не совпадут.
        Eq(MethodKey.Normalize("A.B/Nested"), "A.B+Nested", "Cecil slash -> plus");
        Eq(MethodKey.Normalize("A.B+Nested"), "A.B+Nested", "Reflection plus unchanged");
        Eq(MethodKey.Normalize("A.B"), "A.B", "plain type unchanged");
        Eq(MethodKey.Make("A.B/Nested", "Refresh", 2), "A.B+Nested::Refresh/2", "make key");

        Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
        return failed == 0 ? 0 : 1;
    }
}
```

- [ ] **Step 3: Запустить тест и убедиться, что он падает**

```bash
cd /f/DEV2/ostra_i18n/core/OstraI18n.Core.Tests && dotnet run 2>&1 | tail -10
```

Ожидается: ошибка компиляции `тип или имя пространства имен "MethodKey" не найдено` (`CS0246`).

- [ ] **Step 4: Реализовать `MethodKey`**

Файл `core/OstraI18n.Core/MethodKey.cs`:

```csharp
namespace OstraI18n.Core
{
    /// Идентификатор метода, одинаковый при построении из Mono.Cecil (офлайн)
    /// и из System.Reflection (рантайм). Cecil записывает вложенные типы через '/',
    /// Reflection — через '+'; без приведения к одной форме каталог не находит цель.
    public static class MethodKey
    {
        public static string Normalize(string typeFullName)
        {
            if (string.IsNullOrEmpty(typeFullName)) return typeFullName;
            return typeFullName.Replace('/', '+');
        }

        public static string Make(string typeFullName, string methodName, int paramCount)
        {
            return Normalize(typeFullName) + "::" + methodName + "/" + paramCount;
        }
    }
}
```

- [ ] **Step 5: Запустить тест и убедиться, что он проходит**

```bash
cd /f/DEV2/ostra_i18n/core/OstraI18n.Core.Tests && dotnet run 2>&1 | tail -10; echo "exit=$?"
```

Ожидается: четыре строки `PASS`, строка `ALL PASS`, `exit=0`.

- [ ] **Step 6: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: MethodKey with Cecil/Reflection normalization"
```

---

## Task 3: Извлечение литералов через Mono.Cecil

**Files:**
- Create: `tools_cs/CatalogExtract/CatalogExtract.csproj`
- Create: `tools_cs/CatalogExtract/Program.cs`
- Create: `catalog/literals.json` (результат запуска)

**Interfaces:**
- Consumes: `MethodKey.Make` из Task 2
- Produces: `catalog/literals.json` — массив записей `{ methodKey, literal, ordinal, sink, candidate }`

- [ ] **Step 1: Создать проект извлечения**

Файл `tools_cs/CatalogExtract/CatalogExtract.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\core\OstraI18n.Core\OstraI18n.Core.csproj" />
    <Reference Include="Mono.Cecil">
      <HintPath>..\..\bepinex6_be\extracted\BepInEx\core\Mono.Cecil.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Написать извлекатель**

Файл `tools_cs/CatalogExtract/Program.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OstraI18n.Core;

static class Program
{
    // Методы, присваивание в которые означает вывод текста на экран.
    static readonly string[] TextSinks =
    {
        "TMPro.TMP_Text::set_text",
        "UnityEngine.UI.Text::set_text",
        "TMPro.TMP_Text::SetText",
    };

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("использование: CatalogExtract <Assembly-CSharp.dll> <выход.json>");
            return 2;
        }
        var asmPath = args[0];
        var outPath = args[1];

        if (!File.Exists(asmPath)) { Console.Error.WriteLine("нет файла: " + asmPath); return 2; }

        var asm = AssemblyDefinition.ReadAssembly(asmPath);
        var entries = new List<Dictionary<string, object>>();
        int methodsWithSink = 0;

        foreach (var type in asm.MainModule.GetTypes())
        {
            foreach (var m in type.Methods)
            {
                if (!m.HasBody) continue;

                bool hasSink = m.Body.Instructions.Any(i =>
                    (i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call)
                    && i.Operand is MethodReference mr
                    && TextSinks.Any(s => (mr.DeclaringType.FullName + "::" + mr.Name) == s));

                if (!hasSink) continue;
                methodsWithSink++;

                var key = MethodKey.Make(type.FullName, m.Name, m.Parameters.Count);
                var seen = new Dictionary<string, int>();

                foreach (var instr in m.Body.Instructions)
                {
                    if (instr.OpCode != OpCodes.Ldstr) continue;
                    var lit = instr.Operand as string;
                    if (string.IsNullOrEmpty(lit)) continue;

                    seen.TryGetValue(lit, out int ord);
                    seen[lit] = ord + 1;

                    entries.Add(new Dictionary<string, object>
                    {
                        ["methodKey"] = key,
                        ["literal"] = lit,
                        ["ordinal"] = ord,
                        ["candidate"] = LooksLikeUiText(lit),
                    });
                }
            }
        }

        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(entries, opts));

        int cand = entries.Count(e => (bool)e["candidate"]);
        Console.WriteLine($"методов с выводом текста: {methodsWithSink}");
        Console.WriteLine($"литералов всего: {entries.Count}");
        Console.WriteLine($"кандидатов в UI-текст: {cand}");
        Console.WriteLine("записано: " + outPath);
        return 0;
    }

    // Отсекает идентификаторы, имена ассетов и коды — они не показываются игроку.
    static bool LooksLikeUiText(string s)
    {
        if (s.Length < 2 || s.Length > 200) return false;
        if (!s.Any(char.IsLetter)) return false;
        if (s.Any(c => c == '/' || c == '\\')) return false;      // пути к ассетам и файлам
        if (s.StartsWith("<")) return false;                       // куски разметки TMP
        // ИдентификаторыБезПробелов вида ItmFloorGrate01 / TIsToolWelding:
        // смешанный регистр, есть цифры, нет пробелов — игроку такое не показывают.
        if (!s.Contains(' ')
            && s.Length > 3
            && s.Any(char.IsUpper) && s.Any(char.IsLower)
            && s.Any(char.IsDigit)) return false;
        return true;
    }
}
```

- [ ] **Step 3: Запустить извлечение на текущей сборке игры**

```bash
cd /f/DEV2/ostra_i18n/tools_cs/CatalogExtract && dotnet run -- \
  "F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\Managed\Assembly-CSharp.dll" \
  "F:\DEV2\ostra_i18n\catalog\literals.json" 2>&1 | tail -6
```

Ожидается: четыре строки статистики, `методов с выводом текста` **больше 100**, `литералов всего` **больше 500**.

- [ ] **Step 4: Проверить, что контрольный литерал извлечён правильно**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json
d = json.load(open(r'F:\DEV2\ostra_i18n\catalog\literals.json', encoding='utf-8'))
hits = [e for e in d if e['literal'] == 'At Large']
print('записей At Large:', len(hits))
for h in hits: print(h)
assert len(hits) >= 1, 'контрольный литерал не извлечён'
assert all(h['methodKey'].startswith('GUIChargenCareer::') for h in hits), 'неверный methodKey'
print('OK')
"
```

Ожидается: минимум одна запись, `methodKey` начинается с `GUIChargenCareer::`, финальное `OK`.

**Гейт:** если `At Large` не найден — извлечение не работает, останавливаться и чинить. Каталог, не содержащий известного литерала, непригоден.

- [ ] **Step 5: Проверить отсутствие мусора среди кандидатов**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json, random
d = json.load(open(r'F:\DEV2\ostra_i18n\catalog\literals.json', encoding='utf-8'))
c = [e['literal'] for e in d if e['candidate']]
print('кандидатов:', len(c))
random.seed(0)
for s in random.sample(c, min(30, len(c))): print(repr(s))
"
```

Просмотреть вывод глазами. Записать в `docs/baseline.md` количество кандидатов.
Ожидается: среди 30 образцов преобладают человекочитаемые фразы, а не идентификаторы.

- [ ] **Step 6: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: Cecil-based literal extraction into catalog"
```

---

## Task 4: Плюрализация и языковой пакет

**Files:**
- Create: `core/OstraI18n.Core/PluralRule.cs`
- Create: `core/OstraI18n.Core/LanguagePack.cs`
- Modify: `core/OstraI18n.Core.Tests/Program.cs`

**Interfaces:**
- Consumes: ничего
- Produces:
  - `PluralRule.Category(string rule, long n) → string` (`"one" | "few" | "many" | "other"`)
  - `LanguagePack.Get(string key) → string?`
  - `LanguagePack.Plural(string key, long count) → string?`
  - конструктор `LanguagePack(Dictionary<string,object> entries, string pluralRule, LanguagePack? fallback)`

- [ ] **Step 1: Написать падающие тесты**

В `core/OstraI18n.Core.Tests/Program.cs` заменить метод `Main` целиком на:

```csharp
    static int Main()
    {
        Console.WriteLine("MethodKey");
        Eq(MethodKey.Normalize("A.B/Nested"), "A.B+Nested", "Cecil slash -> plus");
        Eq(MethodKey.Normalize("A.B+Nested"), "A.B+Nested", "Reflection plus unchanged");
        Eq(MethodKey.Normalize("A.B"), "A.B", "plain type unchanged");
        Eq(MethodKey.Make("A.B/Nested", "Refresh", 2), "A.B+Nested::Refresh/2", "make key");

        Console.WriteLine("PluralRule (русский)");
        const string ru = "ru";
        Eq(PluralRule.Category(ru, 1),   "one",  "1 предмет");
        Eq(PluralRule.Category(ru, 2),   "few",  "2 предмета");
        Eq(PluralRule.Category(ru, 5),   "many", "5 предметов");
        Eq(PluralRule.Category(ru, 11),  "many", "11 предметов");
        Eq(PluralRule.Category(ru, 21),  "one",  "21 предмет");
        Eq(PluralRule.Category(ru, 22),  "few",  "22 предмета");
        Eq(PluralRule.Category(ru, 0),   "many", "0 предметов");
        Eq(PluralRule.Category(ru, 114), "many", "114 предметов");

        Console.WriteLine("PluralRule (английский)");
        Eq(PluralRule.Category("en", 1), "one",   "1 item");
        Eq(PluralRule.Category("en", 2), "other", "2 items");
        Eq(PluralRule.Category("en", 0), "other", "0 items");

        Console.WriteLine("LanguagePack");
        var en = new LanguagePack(
            new Dictionary<string, object> { ["GUI_OK"] = "OK", ["GUI_ONLY_EN"] = "English only" },
            "en", null);
        var ru2 = new LanguagePack(
            new Dictionary<string, object>
            {
                ["GUI_OK"] = "Хорошо",
                ["GUI_ITEMS"] = new Dictionary<string, string>
                {
                    ["one"] = "{0} предмет", ["few"] = "{0} предмета", ["many"] = "{0} предметов"
                }
            },
            "ru", en);

        Eq(ru2.Get("GUI_OK"), "Хорошо", "прямое попадание");
        Eq(ru2.Get("GUI_ONLY_EN"), "English only", "fallback в английский");
        Eq(ru2.Get("GUI_MISSING") ?? "<null>", "<null>", "отсутствующий ключ даёт null");
        Eq(ru2.Plural("GUI_ITEMS", 1), "{0} предмет", "плюрал one");
        Eq(ru2.Plural("GUI_ITEMS", 3), "{0} предмета", "плюрал few");
        Eq(ru2.Plural("GUI_ITEMS", 7), "{0} предметов", "плюрал many");
        Eq(ru2.Plural("GUI_OK", 5), "Хорошо", "плюрал на обычной строке возвращает строку");

        Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
        return failed == 0 ? 0 : 1;
    }
```

- [ ] **Step 2: Запустить и убедиться, что падает**

```bash
cd /f/DEV2/ostra_i18n/core/OstraI18n.Core.Tests && dotnet run 2>&1 | tail -5
```

Ожидается: ошибка компиляции `CS0246` про `PluralRule` и `LanguagePack`.

- [ ] **Step 3: Реализовать `PluralRule`**

Файл `core/OstraI18n.Core/PluralRule.cs`:

```csharp
namespace OstraI18n.Core
{
    /// Категории множественного числа по CLDR. Правило выбирается по коду языка,
    /// а не зашивается в вызывающий код: добавление языка не требует правки C#.
    public static class PluralRule
    {
        public static string Category(string languageCode, long n)
        {
            switch (languageCode)
            {
                case "ru":
                case "uk":
                case "pl":
                    return Slavic(n);
                default:
                    return n == 1 ? "one" : "other";
            }
        }

        private static string Slavic(long n)
        {
            long abs = n < 0 ? -n : n;
            long mod10 = abs % 10;
            long mod100 = abs % 100;
            if (mod10 == 1 && mod100 != 11) return "one";
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return "few";
            return "many";
        }
    }
}
```

- [ ] **Step 4: Реализовать `LanguagePack`**

Файл `core/OstraI18n.Core/LanguagePack.cs`:

```csharp
using System.Collections.Generic;

namespace OstraI18n.Core
{
    /// Строки одного языка. Значение ключа — либо строка, либо набор форм
    /// множественного числа. Отсутствующий ключ уходит в fallback-пакет,
    /// и только если и там пусто — возвращается null (вызывающий покажет ключ).
    public class LanguagePack
    {
        private readonly Dictionary<string, object> _entries;
        private readonly string _languageCode;
        private readonly LanguagePack _fallback;

        public LanguagePack(Dictionary<string, object> entries, string languageCode, LanguagePack fallback)
        {
            _entries = entries ?? new Dictionary<string, object>();
            _languageCode = languageCode;
            _fallback = fallback;
        }

        public string Get(string key)
        {
            if (key != null && _entries.TryGetValue(key, out var v))
            {
                if (v is string s) return s;
                if (v is Dictionary<string, string> forms)
                {
                    if (forms.TryGetValue("other", out var o)) return o;
                    foreach (var kv in forms) return kv.Value;
                }
            }
            return _fallback?.Get(key);
        }

        public string Plural(string key, long count)
        {
            if (key != null && _entries.TryGetValue(key, out var v))
            {
                if (v is Dictionary<string, string> forms)
                {
                    var cat = PluralRule.Category(_languageCode, count);
                    if (forms.TryGetValue(cat, out var form)) return form;
                    if (forms.TryGetValue("other", out var other)) return other;
                }
                if (v is string s) return s;
            }
            return _fallback?.Plural(key, count);
        }
    }
}
```

- [ ] **Step 5: Запустить тесты**

```bash
cd /f/DEV2/ostra_i18n/core/OstraI18n.Core.Tests && dotnet run 2>&1 | tail -25; echo "exit=$?"
```

Ожидается: все строки `PASS`, `ALL PASS`, `exit=0`.
**Гейт:** русская плюрализация должна дать `one/few/many` ровно как в тесте. `21 → one` и `11 → many` — типичные места ошибки.

- [ ] **Step 6: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: plural rules and language pack with fallback"
```

---

## Task 5: Загрузка языковых пакетов с диска

**Files:**
- Create: `core/OstraI18n.Core/PackLoader.cs`
- Create: `langs/languages.json`
- Create: `langs/en/meta.json`, `langs/en/ui/common.json`
- Create: `langs/ru/meta.json`, `langs/ru/ui/common.json`
- Modify: `core/OstraI18n.Core.Tests/Program.cs`

**Interfaces:**
- Consumes: `LanguagePack` из Task 4
- Produces: `PackLoader.Load(string langsDir, string languageCode) → LanguagePack`

- [ ] **Step 1: Создать манифест и минимальные пакеты**

Файл `langs/languages.json`:

```json
{
  "default": "en",
  "languages": [
    { "code": "en", "folder": "en", "name": "English" },
    { "code": "ru", "folder": "ru", "name": "Русский" }
  ]
}
```

Файл `langs/en/meta.json`:

```json
{
  "code": "en",
  "name": "English",
  "nameEnglish": "English",
  "fallback": [],
  "fontFallbacks": []
}
```

Файл `langs/ru/meta.json`:

```json
{
  "code": "ru",
  "name": "Русский",
  "nameEnglish": "Russian",
  "fallback": ["en"],
  "fontFallbacks": ["NotoSansGC-Regular SDF"]
}
```

Файл `langs/en/ui/common.json`:

```json
[{
  "strName": "Game Strings",
  "strLanguage": "English",
  "dict": {
    "GUI_CHARGEN_AT_LARGE": "At Large"
  }
}]
```

Файл `langs/ru/ui/common.json`:

```json
[{
  "strName": "Game Strings",
  "strLanguage": "Russian",
  "dict": {
    "GUI_CHARGEN_AT_LARGE": "На свободе"
  }
}]
```

- [ ] **Step 2: Написать падающий тест загрузки**

В `core/OstraI18n.Core.Tests/Program.cs` перед строкой
`Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");` вставить:

```csharp
        Console.WriteLine("PackLoader");
        var langsDir = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "langs"));
        Console.WriteLine("  langs: " + langsDir);
        var packRu = PackLoader.Load(langsDir, "ru");
        Eq(packRu.Get("GUI_CHARGEN_AT_LARGE"), "На свободе", "русская строка загружена");
        var packEn = PackLoader.Load(langsDir, "en");
        Eq(packEn.Get("GUI_CHARGEN_AT_LARGE"), "At Large", "английская строка загружена");
```

- [ ] **Step 3: Запустить, убедиться в падении**

```bash
cd /f/DEV2/ostra_i18n/core/OstraI18n.Core.Tests && dotnet run 2>&1 | tail -5
```

Ожидается: ошибка компиляции `CS0246` про `PackLoader`.

- [ ] **Step 4: Реализовать `PackLoader`**

Файл `core/OstraI18n.Core/PackLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OstraI18n.Core
{
    /// Читает langs/<code>/ui/*.json и meta.json. Значение ключа — строка либо
    /// объект с формами множественного числа. Битый файл пропускается с записью
    /// в errors, а не роняет загрузку: частично собранный язык лучше отсутствия языка.
    public static class PackLoader
    {
        public static List<string> Errors { get; } = new List<string>();

        public static LanguagePack Load(string langsDir, string languageCode)
        {
            return Load(langsDir, languageCode, new HashSet<string>());
        }

        private static LanguagePack Load(string langsDir, string code, HashSet<string> visited)
        {
            if (!visited.Add(code)) return null;   // защита от циклической цепочки fallback

            var dir = Path.Combine(langsDir, code);
            if (!Directory.Exists(dir))
            {
                Errors.Add("нет папки языка: " + dir);
                return null;
            }

            LanguagePack fallback = null;
            foreach (var fb in ReadFallback(Path.Combine(dir, "meta.json")))
            {
                fallback = Load(langsDir, fb, visited);
                if (fallback != null) break;
            }

            var entries = new Dictionary<string, object>(StringComparer.Ordinal);
            var uiDir = Path.Combine(dir, "ui");
            if (Directory.Exists(uiDir))
            {
                foreach (var f in Directory.GetFiles(uiDir, "*.json"))
                    MergeFile(f, entries);
            }
            return new LanguagePack(entries, code, fallback);
        }

        private static IEnumerable<string> ReadFallback(string metaPath)
        {
            var result = new List<string>();
            try
            {
                if (!File.Exists(metaPath)) return result;
                using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
                if (doc.RootElement.TryGetProperty("fallback", out var fb)
                    && fb.ValueKind == JsonValueKind.Array)
                    foreach (var e in fb.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String) result.Add(e.GetString());
            }
            catch (Exception ex) { Errors.Add(metaPath + ": " + ex.Message); }
            return result;
        }

        private static void MergeFile(string path, Dictionary<string, object> into)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
                foreach (var block in doc.RootElement.EnumerateArray())
                {
                    if (!block.TryGetProperty("dict", out var dict)) continue;
                    if (dict.ValueKind != JsonValueKind.Object) continue;
                    foreach (var kv in dict.EnumerateObject())
                    {
                        if (kv.Value.ValueKind == JsonValueKind.String)
                        {
                            into[kv.Name] = kv.Value.GetString();
                        }
                        else if (kv.Value.ValueKind == JsonValueKind.Object)
                        {
                            var forms = new Dictionary<string, string>(StringComparer.Ordinal);
                            foreach (var f in kv.Value.EnumerateObject())
                                if (f.Value.ValueKind == JsonValueKind.String)
                                    forms[f.Name] = f.Value.GetString();
                            into[kv.Name] = forms;
                        }
                    }
                }
            }
            catch (Exception ex) { Errors.Add(path + ": " + ex.Message); }
        }
    }
}
```

- [ ] **Step 5: Запустить тесты**

```bash
cd /f/DEV2/ostra_i18n/core/OstraI18n.Core.Tests && dotnet run 2>&1 | tail -30; echo "exit=$?"
```

Ожидается: `ALL PASS`, `exit=0`. Если путь к `langs` напечатан неверно — исправить количество `..` в тесте под фактическое расположение `bin/Debug/net8.0`.

- [ ] **Step 6: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: language pack loader with fallback chain"
```

---

## Task 6: Генерация ключей и утверждение каталога

**Files:**
- Modify: `tools_cs/CatalogExtract/Program.cs`
- Create: `catalog/literals.json` (перезаписывается с ключами)

**Interfaces:**
- Consumes: `catalog/literals.json` из Task 3
- Produces: записи вида `{ methodKey, literal, ordinal, key, approved }`

- [ ] **Step 1: Добавить генерацию ключа**

В `tools_cs/CatalogExtract/Program.cs` добавить метод:

```csharp
    // Ключ строится из имени типа и текста литерала, чтобы быть читаемым
    // и совпадать по стилю с существующими ключами игры (GUI_QUIT_CONFIRM).
    static string MakeKey(string typeName, string literal)
    {
        var screen = typeName;
        int dot = screen.LastIndexOf('.');
        if (dot >= 0) screen = screen.Substring(dot + 1);
        screen = screen.Replace("GUI", "").Replace("Panel", "").Replace("Popup", "");
        screen = Slug(screen);

        var body = Slug(literal);
        if (body.Length > 40) body = body.Substring(0, 40).TrimEnd('_');
        if (body.Length == 0) body = "TEXT";

        return "GUI_" + (screen.Length > 0 ? screen + "_" : "") + body;
    }

    static string Slug(string s)
    {
        var sb = new System.Text.StringBuilder();
        bool lastUnderscore = false;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(char.ToUpperInvariant(ch));
                lastUnderscore = false;
            }
            else if (!lastUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                lastUnderscore = true;
            }
        }
        return sb.ToString().Trim('_');
    }
```

В цикле сбора записей заменить создание словаря на:

```csharp
                    entries.Add(new Dictionary<string, object>
                    {
                        ["methodKey"] = key,
                        ["literal"] = lit,
                        ["ordinal"] = ord,
                        ["key"] = MakeKey(type.FullName, lit),
                        ["approved"] = false,
                    });
```

- [ ] **Step 2: Перегенерировать каталог**

```bash
cd /f/DEV2/ostra_i18n/tools_cs/CatalogExtract && dotnet run -- \
  "F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\Managed\Assembly-CSharp.dll" \
  "F:\DEV2\ostra_i18n\catalog\literals.json" 2>&1 | tail -5
```

- [ ] **Step 3: Проверить уникальность ключей и вид контрольной записи**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json, collections
d = json.load(open(r'F:\DEV2\ostra_i18n\catalog\literals.json', encoding='utf-8'))
cand = [e for e in d if e.get('candidate')]
keys = [e['key'] for e in cand]
dup = [k for k,c in collections.Counter(keys).items() if c > 1]
print('кандидатов:', len(cand), '| уникальных ключей:', len(set(keys)), '| дублей:', len(dup))
al = [e for e in d if e['literal'] == 'At Large']
print('At Large ->', al[0]['key'] if al else 'НЕ НАЙДЕН')
assert al, 'контрольный литерал пропал'
"
```

Ожидается: контрольная запись присутствует, ключ вида `GUI_CHARGENCAREER_AT_LARGE`.
Дубли ключей на этом этапе допустимы (один литерал в нескольких методах) — они разрешаются в Task 7.

- [ ] **Step 4: Утвердить контрольную запись вручную**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json
p = r'F:\DEV2\ostra_i18n\catalog\literals.json'
d = json.load(open(p, encoding='utf-8'))
n = 0
for e in d:
    if e['literal'] == 'At Large':
        e['approved'] = True; e['key'] = 'GUI_CHARGEN_AT_LARGE'; n += 1
json.dump(d, open(p, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
print('утверждено записей:', n)
assert n >= 1
"
```

Ожидается: `утверждено записей: 1` (или больше).

- [ ] **Step 5: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: semantic key generation for catalog literals"
```

---

## Task 7: Фасад I18n и транспайлер по каталогу

**Files:**
- Create: `plugin/OstraI18n/I18n.cs`
- Rewrite: `plugin/OstraI18n/LiteralPatcher.cs`
- Modify: `plugin/OstraI18n/OstraI18n.csproj`
- Modify: `plugin/OstraI18n/Plugin.cs`

**Interfaces:**
- Consumes: `PackLoader.Load`, `LanguagePack`, `catalog/literals.json`
- Produces: `I18n.Get(string key) → string`, `I18n.Applied`, `I18n.Drifted`

- [ ] **Step 1: Подключить Core к проекту плагина**

В `plugin/OstraI18n/OstraI18n.csproj` внутрь существующего `<ItemGroup>` добавить:

```xml
    <ProjectReference Include="..\..\core\OstraI18n.Core\OstraI18n.Core.csproj" />
```

- [ ] **Step 2: Создать фасад**

Файл `plugin/OstraI18n/I18n.cs`:

```csharp
using System;
using System.IO;
using OstraI18n.Core;

namespace OstraI18n
{
    /// Единственная точка входа рантайма. Транспайлер подставляет вызов Get(ключ)
    /// вместо литерала, поэтому Get обязан быть безотказным: любая проблема
    /// возвращает осмысленный текст, а не бросает исключение внутрь кода игры.
    public static class I18n
    {
        private static LanguagePack _pack;
        public static string Language { get; private set; } = "en";
        public static int Applied;
        public static int Drifted;

        internal static void Init(string pluginDir, string languageCode)
        {
            Language = languageCode;
            try
            {
                _pack = PackLoader.Load(Path.Combine(pluginDir, "langs"), languageCode);
                foreach (var e in PackLoader.Errors) Plugin.Log.LogWarning("[i18n] pack: " + e);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] загрузка пакета не удалась: " + ex);
                _pack = null;
            }
        }

        /// Вызывается из подменённого IL. Никогда не бросает исключений.
        public static string Get(string key)
        {
            try
            {
                var v = _pack?.Get(key);
                return v ?? key;
            }
            catch { return key; }
        }

        public static string Plural(string key, long count)
        {
            try
            {
                var v = _pack?.Plural(key, count);
                return v ?? key;
            }
            catch { return key; }
        }
    }
}
```

- [ ] **Step 3: Переписать `LiteralPatcher` под каталог**

Заменить содержимое `plugin/OstraI18n/LiteralPatcher.cs` целиком:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using HarmonyLib;
using OstraI18n.Core;

namespace OstraI18n
{
    /// Заменяет ldstr-литералы на вызов I18n.Get(ключ) по записям каталога.
    /// Подменяется только литерал с approved-записью; всё остальное проходит нетронутым.
    internal static class LiteralPatcher
    {
        private class Entry
        {
            public string Key;
            public int Ordinal;
        }

        // methodKey -> (литерал -> список записей по порядковому номеру)
        private static readonly Dictionary<string, Dictionary<string, List<Entry>>> ByMethod =
            new Dictionary<string, Dictionary<string, List<Entry>>>(StringComparer.Ordinal);

        private static Dictionary<string, List<Entry>> _current;

        public static int LoadCatalog(string pluginDir)
        {
            var path = Path.Combine(pluginDir, "catalog", "literals.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("[i18n] каталог не найден: " + path);
                return 0;
            }
            int n = 0;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("approved", out var ap) || !ap.GetBoolean()) continue;
                var mk = e.GetProperty("methodKey").GetString();
                var lit = e.GetProperty("literal").GetString();
                var key = e.GetProperty("key").GetString();
                var ord = e.GetProperty("ordinal").GetInt32();
                if (mk == null || lit == null || key == null) continue;

                if (!ByMethod.TryGetValue(mk, out var byLit))
                    ByMethod[mk] = byLit = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
                if (!byLit.TryGetValue(lit, out var list))
                    byLit[lit] = list = new List<Entry>();
                list.Add(new Entry { Key = key, Ordinal = ord });
                n++;
            }
            Plugin.Log.LogInfo("[i18n] каталог: утверждённых записей " + n
                               + " в " + ByMethod.Count + " методах");
            return n;
        }

        public static void ApplyAll(Harmony harmony)
        {
            var asm = typeof(DataHandler).Assembly;
            var transpiler = new HarmonyMethod(
                typeof(LiteralPatcher).GetMethod(nameof(Transpiler),
                    BindingFlags.NonPublic | BindingFlags.Static));

            int methodsPatched = 0, notFound = 0;

            foreach (var kv in ByMethod)
            {
                var m = Resolve(asm, kv.Key);
                if (m == null)
                {
                    notFound++;
                    I18n.Drifted += kv.Value.Count;
                    Plugin.Log.LogWarning("[i18n] дрейф: метод не найден " + kv.Key);
                    continue;
                }
                try
                {
                    _current = kv.Value;
                    harmony.Patch(m, transpiler: transpiler);
                    methodsPatched++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[i18n] патч пропущен " + kv.Key + ": " + ex.Message);
                }
                finally { _current = null; }
            }

            Plugin.Log.LogInfo("[i18n] литералы: методов пропатчено " + methodsPatched
                               + ", подстановок " + I18n.Applied
                               + ", дрейф " + I18n.Drifted
                               + ", методов не найдено " + notFound);
        }

        private static MethodBase Resolve(Assembly asm, string methodKey)
        {
            try
            {
                int sep = methodKey.LastIndexOf("::", StringComparison.Ordinal);
                if (sep < 0) return null;
                var typeName = methodKey.Substring(0, sep);
                var rest = methodKey.Substring(sep + 2);
                int slash = rest.LastIndexOf('/');
                if (slash < 0) return null;
                var name = rest.Substring(0, slash);
                if (!int.TryParse(rest.Substring(slash + 1), out int pc)) return null;

                var type = asm.GetType(typeName, false);
                if (type == null) return null;

                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                  | BindingFlags.Instance | BindingFlags.Static
                                                  | BindingFlags.DeclaredOnly))
                {
                    if (m.Name == name && m.GetParameters().Length == pc
                        && !m.IsAbstract && !m.ContainsGenericParameters)
                        return m;
                }
            }
            catch { }
            return null;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var map = _current;
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var instr in instructions)
            {
                if (map != null && instr.opcode == OpCodes.Ldstr && instr.operand is string lit
                    && map.TryGetValue(lit, out var list))
                {
                    seen.TryGetValue(lit, out int ord);
                    seen[lit] = ord + 1;

                    Entry match = null;
                    foreach (var e in list) if (e.Ordinal == ord) { match = e; break; }

                    if (match != null)
                    {
                        I18n.Applied++;
                        yield return new CodeInstruction(OpCodes.Ldstr, match.Key);
                        yield return new CodeInstruction(OpCodes.Call,
                            AccessTools.Method(typeof(I18n), nameof(I18n.Get), new[] { typeof(string) }));
                        continue;
                    }
                }
                yield return instr;
            }
        }
    }
}
```

- [ ] **Step 4: Подключить в `Plugin.cs`**

В `plugin/OstraI18n/Plugin.cs` заменить блок из Task 1
(`try { LiteralPatcher.ApplySlice(...) } ...`) на:

```csharp
            try
            {
                I18n.Init(DataDir.Value, "ru");
                if (LiteralPatcher.LoadCatalog(DataDir.Value) > 0)
                    LiteralPatcher.ApplyAll(new Harmony(GUID + ".literals"));
            }
            catch (Exception ex) { Log.LogError("[i18n] literals failed: " + ex); }
```

- [ ] **Step 5: Собрать**

```bash
cd /f/DEV2/ostra_i18n/plugin/OstraI18n && dotnet build -c Release 2>&1 | tail -5
```

Ожидается: `Ошибок: 0`.

- [ ] **Step 6: Задеплоить каталог, языки и плагин**

```bash
DST="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/plugins/OstraI18n"
mkdir -p "$DST/catalog" "$DST/langs" \
&& cp -f /f/DEV2/ostra_i18n/catalog/literals.json "$DST/catalog/" \
&& cp -rf /f/DEV2/ostra_i18n/langs/. "$DST/langs/" \
&& cp -f /f/DEV2/ostra_i18n/plugin/OstraI18n/bin/Release/netstandard2.1/OstraI18n.dll "$DST/OstraI18n.dll" \
&& cp -f /f/DEV2/ostra_i18n/core/OstraI18n.Core/bin/Release/netstandard2.1/OstraI18n.Core.dll "$DST/OstraI18n.Core.dll" \
&& ls -la "$DST"
```

Ожидается: в листинге присутствуют `OstraI18n.dll`, `OstraI18n.Core.dll`, папки `catalog` и `langs`.

**Гейт:** `OstraI18n.Core.dll` обязателен рядом с плагином — без него плагин упадёт при загрузке типа.

- [ ] **Step 7: Запустить и проверить лог**

```bash
cd /f/Games/Steam/steamapps/common/Ostranauts && cmd.exe /c RUNSAVE.bat
```

Через 60 секунд:

```bash
grep -E "\[i18n\] (каталог|литералы|дрейф)" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
grep -c "OUTPUTTING STACK TRACE" "/c/Users/Low/AppData/LocalLow/Blue Bottle Games/Ostranauts/Player.log"
```

Ожидается: `каталог: утверждённых записей 1`, `литералы: методов пропатчено 1, подстановок 1, дрейф 0`, и `0` крашей.

- [ ] **Step 8: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: catalog-driven literal transpiler with drift detection"
```

---

## Task 8: Массовое утверждение каталога и удаление старого механизма

**Files:**
- Modify: `catalog/literals.json`
- Create: `langs/en/ui/generated.json`, `langs/ru/ui/generated.json`
- Delete: `plugin/OstraI18n/GuiText.cs`
- Modify: `plugin/OstraI18n/Plugin.cs`

**Interfaces:**
- Consumes: всё предыдущее
- Produces: рабочий перевод литералов из файлов, отсутствие подмены по английскому тексту

- [ ] **Step 1: Утвердить кандидатов и сгенерировать английский пакет**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe - << 'PYEOF'
import json, collections, zlib
CAT = r'F:\DEV2\ostra_i18n\catalog\literals.json'
d = json.load(open(CAT, encoding='utf-8'))

# Один литерал может встречаться в разных методах — ключ должен быть уникален.
# Используется crc32, а не hash(): встроенный hash() для строк рандомизируется
# при каждом запуске интерпретатора, и ключи не воспроизводились бы между прогонами.
counts = collections.Counter(e['key'] for e in d if e.get('candidate'))
for e in d:
    if not e.get('candidate'):
        continue
    if counts[e['key']] > 1:
        suffix = zlib.crc32(e['methodKey'].encode('utf-8')) % 100000
        e['key'] = f"{e['key']}__{suffix}_{e['ordinal']}"
    e['approved'] = True

json.dump(d, open(CAT, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)

appr = [e for e in d if e.get('approved')]
keys = [e['key'] for e in appr]
assert len(keys) == len(set(keys)), 'ключи не уникальны после разведения'

en = {e['key']: e['literal'] for e in appr}
block = [{"strName": "Game Strings", "strLanguage": "English", "dict": en}]
json.dump(block, open(r'F:\DEV2\ostra_i18n\langs\en\ui\generated.json', 'w', encoding='utf-8'),
          ensure_ascii=False, indent=2)
print('утверждено:', len(appr), '| уникальных ключей:', len(set(keys)))
PYEOF
```

Ожидается: `утверждено` — сотни записей, количество уникальных ключей равно количеству записей.

- [ ] **Step 2: Создать русский пакет как копию английского (перевод — отдельная задача)**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json, shutil
src = r'F:\DEV2\ostra_i18n\langs\en\ui\generated.json'
d = json.load(open(src, encoding='utf-8'))
d[0]['strLanguage'] = 'Russian'
json.dump(d, open(r'F:\DEV2\ostra_i18n\langs\ru\ui\generated.json','w',encoding='utf-8'),
          ensure_ascii=False, indent=2)
print('ключей в ru:', len(d[0]['dict']))
"
```

Русский пакет пока содержит английский текст — это ожидаемо: задача этого шага в том, чтобы система прогнала **все** строки через ключи, а не в переводе. Перевод выполняется отдельной фазой.

- [ ] **Step 3: Удалить старый механизм подмены по тексту**

```bash
rm /f/DEV2/ostra_i18n/plugin/OstraI18n/GuiText.cs
```

В `plugin/OstraI18n/Plugin.cs` удалить:
- строку `PatchRunner.ApplyGuiHooks(ref ok, ref failed);`
- весь метод `ApplyGuiHooks` и метод `TryPatchOnce` в классе `PatchRunner`
- метод `GuiSweepLoop` и строки запуска потока `sweepTh`

- [ ] **Step 4: Собрать и убедиться в отсутствии ссылок на удалённое**

```bash
cd /f/DEV2/ostra_i18n/plugin/OstraI18n && dotnet build -c Release 2>&1 | tail -8
```

Ожидается: `Ошибок: 0`. Ошибка `CS0103` про `GuiText` означает незачищенную ссылку — удалить её.

- [ ] **Step 5: Задеплоить и запустить**

```bash
DST="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/plugins/OstraI18n"
cp -f /f/DEV2/ostra_i18n/catalog/literals.json "$DST/catalog/" \
&& cp -rf /f/DEV2/ostra_i18n/langs/. "$DST/langs/" \
&& cp -f /f/DEV2/ostra_i18n/plugin/OstraI18n/bin/Release/netstandard2.1/OstraI18n.dll "$DST/OstraI18n.dll" \
&& cp -f /f/DEV2/ostra_i18n/core/OstraI18n.Core/bin/Release/netstandard2.1/OstraI18n.Core.dll "$DST/OstraI18n.Core.dll" \
&& rm -f "$DST/gui_unknown.txt" \
&& cd /f/Games/Steam/steamapps/common/Ostranauts && cmd.exe /c RUNSAVE.bat
```

- [ ] **Step 6: Проверить массовое применение**

Через 90 секунд:

```bash
grep -E "\[i18n\] (каталог|литералы)" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
grep -c "OUTPUTTING STACK TRACE" "/c/Users/Low/AppData/LocalLow/Blue Bottle Games/Ostranauts/Player.log"
```

Ожидается: `подстановок` — сотни, `дрейф 0`, крашей `0`.

**Гейт:** если `подстановок` заметно меньше числа утверждённых записей — часть методов не резолвится. Просмотреть строки `дрейф: метод не найден` и разобраться до перехода к следующей фазе.

- [ ] **Step 7: Проверить, что перевод действительно берётся из файла**

Изменить одну строку в русском пакете и убедиться, что она доехала до игры:

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json
p = r'F:\Games\Steam\steamapps\common\Ostranauts\BepInEx\plugins\OstraI18n\langs\ru\ui\generated.json'
d = json.load(open(p, encoding='utf-8'))
k = 'GUI_CHARGEN_AT_LARGE'
d[0]['dict'][k] = 'ПРОВЕРКА_ФАЙЛА'
json.dump(d, open(p,'w',encoding='utf-8'), ensure_ascii=False, indent=2)
print('изменён ключ', k)
"
cd /f/Games/Steam/steamapps/common/Ostranauts && cmd.exe /c RUNSAVE.bat
```

Открыть в игре экран создания персонажа (вкладка карьеры) и убедиться, что вместо `At Large` показано `ПРОВЕРКА_ФАЙЛА`. Это единственный шаг плана, требующий взгляда на экран: он доказывает, что текст читается из файла, а не из сборки.

Затем вернуть значение:

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json
p = r'F:\Games\Steam\steamapps\common\Ostranauts\BepInEx\plugins\OstraI18n\langs\ru\ui\generated.json'
d = json.load(open(p, encoding='utf-8'))
d[0]['dict']['GUI_CHARGEN_AT_LARGE'] = 'At Large'
json.dump(d, open(p,'w',encoding='utf-8'), ensure_ascii=False, indent=2)
print('возвращено')
"
```

- [ ] **Step 8: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: approve full literal catalog, remove text-matching mechanism"
```

---

## Определение готовности фазы

Фаза считается завершённой, когда одновременно:

1. `dotnet run` в `core/OstraI18n.Core.Tests` даёт `ALL PASS` и код возврата 0
2. В логе игры: `литералы: методов пропатчено N, подстановок M, дрейф 0`, где `M` — сотни
3. `grep -c "OUTPUTTING STACK TRACE"` по `Player.log` даёт `0`
4. Правка строки в `langs/ru/ui/generated.json` меняет текст в игре (проверено в Task 8 Step 7)
5. Файл `plugin/OstraI18n/GuiText.cs` отсутствует, свип сцены удалён
6. Все изменения закоммичены

## Что НЕ входит в эту фазу

- Перевод сгенерированных ключей на русский (русский пакет содержит английский текст)
- Префабы и сцены — фаза 2
- Контент-данные и импорт старого перевода — фаза 3
- Формат-строки для конкатенации (270 мест) — фаза 4
- Псевдоязык, детектор переполнения, пакет для разработчиков — фаза 4
