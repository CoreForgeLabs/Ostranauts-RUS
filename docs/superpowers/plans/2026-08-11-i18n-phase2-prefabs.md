# OstraI18n Фаза 2 — префабы: план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Перевести текст, запечённый в префабах/сценах (не хардкод в коде — это закрыла Фаза 1), через привязку по пути в иерархии вместо подмены по содержимому текста. Закрывает регрессию: после удаления `GuiText.cs` в Фазе 1 такой текст (`"Build-a-Resume Center"`, `"Career Summary"` и т.п.) перестал переводиться.

**Architecture:** Офлайн-извлечение путей через UnityPy + TypeTreeGenerator (уже проверено на вертикальном срезе) строит каталог `путь → ключ`, с явным различением двух видов объектов: `scene` (объект существует в конкретной сцене `level0-4`, путь абсолютный и стабильный) и `asset` (объект — часть префаб-шаблона в `resources.assets`/`sharedassets*`, инстанцируется многократно под разными родителями). В рантайме `PrefabBinder` для `scene`-объектов ищет их один раз при загрузке сцены и вешает `LocalizedText`; для `asset`-объектов использует единственный Harmony-хук на `OnEnable` (тот же безопасный паттерн, что уже пофикшен в Фазе 1 при исправлении краша — приведение типа через `as`), но сопоставление там идёт **по структуре пути**, никогда по содержимому текста — поэтому классы багов Фазы 1 (обрывки анимации, короткие слова) здесь невозможны в принципе.

**Tech Stack:** C# (netstandard2.1), HarmonyX, Python 3.14 + UnityPy 1.24.2 + TypeTreeGeneratorAPI (уже использовались и проверены в этой сессии), BepInEx 6.0.0-be.785.

## Global Constraints

- Рабочая директория проекта: `F:\DEV2\ostra_i18n`
- Директория игры: `F:\Games\Steam\steamapps\common\Ostranauts`
- Python с UnityPy: `C:\Users\Low\AppData\Local\Programs\Python\Python314\python.exe` (НЕ mingw python — у него нет UnityPy; POSIX-пути `/f/...` этому интерпретатору передавать нельзя, только `F:\...`)
- Извлечение путей — из **живых игровых ассетов** (`Ostranauts_Data/resources.assets`, `Ostranauts_Data/level0`-`level4`, `Ostranauts_Data/sharedassets0-4.assets`), не из декомпилята.
- **Критерий «нет краша» включает ОБА источника**: `grep -c "OUTPUTTING STACK TRACE"` по `Player.log` (полный краш процесса) И `grep -c "Exception"` по `BepInEx/LogOutput.log` (managed-исключения, пойманные движком — именно так был найден баг Фазы 1, `Player.log` в тот раз показывал 0). Оба должны быть 0 на каждой проверке.
- Игру перезапускать только через `Start-Process "F:\Games\Steam\steamapps\common\Ostranauts\RUNSAVE.bat" -WorkingDirectory "F:\Games\Steam\steamapps\common\Ostranauts"` (PowerShell). `cmd.exe /c RUNSAVE.bat` из Bash в этой сессии не запускал процесс (проверено дважды) — не использовать.
- После `Start-Process` ждать появления процесса (`Get-Process -Name Ostranauts`, до 10 секунд), затем ждать нужной строки в `BepInEx/LogOutput.log` через `until grep -q ...; do sleep 3; done` — не фиксированный `sleep`.
- Формат JSON: UTF-8 без BOM, отступ 2, `ensure_ascii=false` (Python) / `WriteIndented=true` (C#).
- Плагин деплоится в `F:\Games\Steam\steamapps\common\Ostranauts\BepInEx\plugins\OstraI18n\` вручную (`cp`), не через `dotnet publish`.
- Новый approve — только для записей, где извлечённый путь **однозначно разрешился** в рантайме на этапе вертикального среза. Массовое утверждение без предварительной проверки логики (как это сделала Фаза 1 и получила `NullReferenceException`) не допускается.

---

## Структура файлов

Создаётся:

| Путь | Ответственность |
|---|---|
| `tools/extract_prefab_paths.py` | извлечение путей + текста из ассетов игры (расширение уже написанного `extract_baked_text.py`) |
| `catalog/prefabs.json` | результат извлечения: путь → `{kind, literal, key, approved}` |
| `plugin/OstraI18n/LocalizedText.cs` | рантайм-компонент: держит ключ, подставляет перевод, переподписывается на смену языка |
| `plugin/OstraI18n/PrefabBinder.cs` | находит объекты по каталогу, вешает `LocalizedText`: `scene` — при загрузке сцены, `asset` — через `OnEnable`-хук по структуре пути |
| `core/OstraI18n.Core.Tests/Program.cs` (доп.) | тесты на разбор/сравнение путей |

Модифицируется:

| Путь | Что меняется |
|---|---|
| `core/OstraI18n.Core/*.cs` (новый файл `PathKey.cs`) | разбор пути на `(rootName, relativeSegments[])`, сравнение |
| `plugin/OstraI18n/Plugin.cs` | подключение `PrefabBinder` |
| `langs/en/ui/prefabs.json`, `langs/ru/ui/prefabs.json` | строки, извлечённые из префабов |

---

## Task 0: Полное извлечение путей — инвентаризация

**Files:**
- Create: `tools/extract_prefab_paths.py`
- Create: `catalog/prefabs.json` (результат запуска)

**Interfaces:**
- Consumes: ничего (первый инструмент фазы)
- Produces: `catalog/prefabs.json` — массив `{kind, rootName, path, literal, key, approved}`

- [ ] **Step 1: Написать полный экстрактор**

Файл `tools/extract_prefab_paths.py` — расширение уже проверенного в этой сессии кода (`extract_baked_text.py`), с добавлением построения пути и разделения `kind` по имени файла-источника:

```python
# Полное офлайн-извлечение текста ИЗ ПРЕФАБОВ/СЦЕН вместе с путём в иерархии.
# kind различается по файлу-источнику: level0-4 = "scene" (путь абсолютный,
# стабилен для конкретной сцены), resources.assets/sharedassets* = "asset"
# (объект — часть префаб-шаблона, инстанцируется многократно под разными
# родителями; путь — от корня самого префаба, не от корня сцены).
import json, os, re
import UnityPy
from TypeTreeGeneratorAPI import TypeTreeGenerator

GAME = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data"
MANAGED = GAME + r"\Managed"
OUT = r"F:\DEV2\ostra_i18n\catalog\prefabs.json"

gen = TypeTreeGenerator("6000.3.10f1")
for dll in ("netstandard.dll", "mscorlib.dll", "UnityEngine.CoreModule.dll",
            "UnityEngine.TextRenderingModule.dll", "UnityEngine.TextCoreFontEngineModule.dll",
            "UnityEngine.TextCoreTextEngineModule.dll", "Unity.TextMeshPro.dll", "UnityEngine.UI.dll"):
    gen.load_dll(open(MANAGED + "\\" + dll, "rb").read())

CANDIDATES = [
    ("TextMeshProUGUI", "Unity.TextMeshPro", "TMPro.TextMeshProUGUI"),
    ("TextMeshPro", "Unity.TextMeshPro", "TMPro.TextMeshPro"),
    ("UI.Text", "UnityEngine.UI", "UnityEngine.UI.Text"),
]
node_sets = [(label, json.loads(gen.get_nodes_as_json(asm, cls))) for label, asm, cls in CANDIDATES]


def try_read_text(o):
    for label, nodes in node_sets:
        try:
            t = o.read_typetree(nodes=nodes, check_read=False)
        except Exception:
            continue
        if not isinstance(t, dict):
            continue
        txt = t.get("m_text")
        if not isinstance(txt, str) or not txt.strip():
            continue
        printable = sum(1 for c in txt if c.isprintable() or c in "\n\t")
        if printable / max(1, len(txt)) < 0.9 or len(txt) > 20000:
            continue
        return t
    return None


def go_name(objs, pid):
    o = objs.get(pid)
    if o is None:
        return None
    try:
        return o.read().m_Name
    except Exception:
        return None


def transform_of(objs, go_pid):
    o = objs.get(go_pid)
    if o is None:
        return None
    try:
        d = o.read()
        for c in d.m_Component:
            cp = c.component if hasattr(c, "component") else c
            pid = cp.m_PathID if hasattr(cp, "m_PathID") else cp.path_id
            co = objs.get(pid)
            if co is not None and co.type.name in ("Transform", "RectTransform"):
                return co
    except Exception:
        return None
    return None


def full_path(objs, go_pid, depth_limit=30):
    parts = []
    cur = go_pid
    depth = 0
    while cur and depth < depth_limit:
        n = go_name(objs, cur)
        if n is None:
            break
        parts.append(n)
        tr = transform_of(objs, cur)
        if tr is None:
            break
        try:
            td = tr.read_typetree()
            father = td.get("m_Father", {}).get("m_PathID")
            if not father:
                break
            fo = objs.get(father)
            if fo is None:
                break
            ftd = fo.read_typetree()
            cur = ftd.get("m_GameObject", {}).get("m_PathID")
        except Exception:
            break
        depth += 1
    return list(reversed(parts))


def slug(s):
    s = re.sub(r"[^A-Za-z0-9]+", "_", s).strip("_").upper()
    return s or "TEXT"


def make_key(root, segs, literal):
    tail = slug(segs[-1]) if segs else "TEXT"
    body = slug(literal)[:30]
    return f"GUI_{slug(root)}_{tail}_{body}".rstrip("_")


def analyze(fname, kind, out_entries, seen_keys):
    path = os.path.join(GAME, fname)
    if not os.path.exists(path):
        print("нет файла, пропуск:", fname)
        return
    env = UnityPy.load(path)
    objs = {o.path_id: o for o in env.objects}
    found = 0
    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        t = try_read_text(o)
        if t is None:
            continue
        go_pid = t.get("m_GameObject", {}).get("m_PathID")
        segs = full_path(objs, go_pid)
        if not segs:
            continue
        root, rest = segs[0], segs[1:]
        literal = t["m_text"]
        key = make_key(root, segs, literal)
        n = 1
        base_key = key
        while key in seen_keys:
            n += 1
            key = f"{base_key}_{n}"
        seen_keys.add(key)
        out_entries.append({
            "kind": kind,
            "root": root,
            "path": rest,
            "literal": literal,
            "key": key,
            "sourceFile": fname,
            "approved": False,
        })
        found += 1
    print(f"{fname} ({kind}): найдено {found}")


entries = []
seen_keys = set()
for f in ("level0", "level1", "level2", "level3", "level4"):
    analyze(f, "scene", entries, seen_keys)
for f in ("resources.assets", "sharedassets0.assets", "sharedassets1.assets",
          "sharedassets2.assets", "sharedassets3.assets", "sharedassets4.assets"):
    analyze(f, "asset", entries, seen_keys)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
json.dump(entries, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print("всего записей:", len(entries))
print("уникальных ключей:", len(seen_keys))
print("записано:", OUT)
```

- [ ] **Step 2: Запустить**

```bash
"/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe" "F:\DEV2\ostra_i18n\tools\extract_prefab_paths.py"
```

Ожидается: строки статистики по каждому файлу, `всего записей` **больше 1000**, `уникальных ключей` равно `всего записей` (дедупликация в скрипте гарантирует это самостоятельно — если не равно, в скрипте баг).

- [ ] **Step 3: Проверить контрольную запись**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json
d = json.load(open(r'F:\DEV2\ostra_i18n\catalog\prefabs.json', encoding='utf-8'))
hits = [e for e in d if e['literal'] == 'Build-a-Resume Center']
print('Build-a-Resume Center:', hits)
assert hits, 'контрольная строка не найдена'
print('kind:', hits[0]['kind'], '| root:', hits[0]['root'], '| path:', hits[0]['path'])
"
```

Ожидается: запись найдена (это была та самая строка, регрессию с которой заметил пользователь после Фазы 1).

- [ ] **Step 4: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: extract prefab/scene text paths via UnityPy"
```

---

## Task 1: `LocalizedText` — рантайм-компонент

**Files:**
- Create: `plugin/OstraI18n/LocalizedText.cs`

**Interfaces:**
- Consumes: `I18n.Get(string key)` (готово в Фазе 1)
- Produces: `LocalizedText` — `MonoBehaviour` с полем `Key`, применяет перевод в `OnEnable` и при смене языка

- [ ] **Step 1: Написать компонент**

Файл `plugin/OstraI18n/LocalizedText.cs`:

```csharp
using TMPro;
using UnityEngine;

namespace OstraI18n
{
    /// Вешается в рантайме на объект, найденный PrefabBinder-ом по пути из каталога.
    /// Не хранит и не сравнивает текст — только ключ. Поэтому не может повторить
    /// баги Фазы 1 (обрывок анимации, совпадение коротких слов): единственный
    /// источник истины — путь, зафиксированный один раз при привязке.
    public class LocalizedText : MonoBehaviour
    {
        public string Key;

        private void OnEnable()
        {
            Apply();
        }

        internal void Apply()
        {
            if (string.IsNullOrEmpty(Key)) return;
            var value = I18n.Get(Key);
            if (string.IsNullOrEmpty(value)) return;

            var tmp = GetComponent<TMP_Text>();
            if (tmp != null) { tmp.text = value; return; }

            var legacy = GetComponent<UnityEngine.UI.Text>();
            if (legacy != null) legacy.text = value;
        }
    }
}
```

- [ ] **Step 2: Собрать плагин (без интеграции — только проверка компиляции)**

```bash
cd /f/DEV2/ostra_i18n/plugin/OstraI18n && dotnet build -c Release 2>&1 | tail -6
```

Ожидается: `Ошибок: 0`.

- [ ] **Step 3: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: LocalizedText runtime component"
```

---

## Task 2: Вертикальный срез — один `scene`-объект

Самый рискованный элемент (поиск объекта по абсолютному пути в реальной загруженной сцене) проверяется первым, на одной записи, прежде чем строить массовую привязку.

**Files:**
- Create: `plugin/OstraI18n/PrefabBinder.cs`
- Modify: `plugin/OstraI18n/Plugin.cs`

**Interfaces:**
- Consumes: `catalog/prefabs.json`, `LocalizedText`
- Produces: `PrefabBinder.BindSceneSlice()` — доказательство механизма; переписывается в Task 4

- [ ] **Step 1: Найти в каталоге лёгкую для проверки `scene`-запись**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json
d = json.load(open(r'F:\DEV2\ostra_i18n\catalog\prefabs.json', encoding='utf-8'))
scene = [e for e in d if e['kind']=='scene' and e['sourceFile']=='level2' and len(e['literal'])<30]
for e in scene[:5]:
    print(e['root'], '/', '/'.join(e['path']), '->', repr(e['literal']))
"
```

Записать выбранную запись (`root`, `path`, `literal`) — она понадобится в Step 2. Ожидается непустой список.

- [ ] **Step 2: Написать срез в `PrefabBinder.cs`**

Файл `plugin/OstraI18n/PrefabBinder.cs` — подставить в `SliceRoot`/`SlicePath`/`SliceReplacement` значения, найденные в Step 1 (пример ниже использует один из реально найденных в этой сессии путей — `Canvas GUI/GUIZones/Scrollview/TitleContainer/DescriptionLabel`, замените на актуальный, если он изменился):

```csharp
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OstraI18n
{
    // Находит объекты сцены по абсолютному пути из каталога и вешает LocalizedText.
    // Task 2 — вертикальный срез на одной scene-записи.
    internal static class PrefabBinder
    {
        private const string SliceRoot = "Canvas GUI";
        private static readonly string[] SlicePath = { "GUIZones", "Scrollview", "TitleContainer", "DescriptionLabel" };
        private const string SliceKey = "GUI_SLICE_TEST";
        private const string SliceReplacement = "ПРОВЕРКА_ПРЕФАБА";

        public static void BindSceneSlice()
        {
            SceneManager.sceneLoaded += (scene, mode) => TryBindSlice(scene);
            var active = SceneManager.GetActiveScene();
            if (active.IsValid()) TryBindSlice(active);
        }

        private static void TryBindSlice(Scene scene)
        {
            try
            {
                var roots = scene.GetRootGameObjects();
                var root = roots.FirstOrDefault(r => r.name == SliceRoot);
                if (root == null) return;

                var t = root.transform;
                foreach (var seg in SlicePath)
                {
                    t = t.Find(seg);
                    if (t == null)
                    {
                        Plugin.Log.LogWarning("[i18n] slice: путь не разрешился на '" + seg + "'");
                        return;
                    }
                }

                var lt = t.gameObject.AddComponent<LocalizedText>();
                lt.Key = SliceKey;
                Plugin.Log.LogInfo("[i18n] slice: привязан " + SliceRoot + "/" + string.Join("/", SlicePath));
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("[i18n] slice bind failed: " + ex);
            }
        }
    }
}
```

- [ ] **Step 3: Добавить тестовый перевод ключа**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json
p = r'F:\DEV2\ostra_i18n\langs\ru\ui\common.json'
d = json.load(open(p, encoding='utf-8'))
d[0]['dict']['GUI_SLICE_TEST'] = 'ПРОВЕРКА_ПРЕФАБА'
json.dump(d, open(p,'w',encoding='utf-8'), ensure_ascii=False, indent=2)
print('добавлено')
"
```

- [ ] **Step 4: Подключить в `Plugin.cs`**

В `plugin/OstraI18n/Plugin.cs`, сразу после блока `LiteralPatcher.ApplyAll(...)` (из Фазы 1), добавить:

```csharp
            try { PrefabBinder.BindSceneSlice(); }
            catch (Exception ex) { Log.LogError("[i18n] prefab slice failed: " + ex); }
```

- [ ] **Step 5: Собрать и задеплоить**

```bash
cd /f/DEV2/ostra_i18n/plugin/OstraI18n && dotnet build -c Release 2>&1 | tail -6
DST="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/plugins/OstraI18n"
cp -f bin/Release/netstandard2.1/OstraI18n.dll "$DST/OstraI18n.dll" \
&& cp -f /f/DEV2/ostra_i18n/langs/ru/ui/common.json "$DST/langs/ru/ui/common.json"
```

Ожидается: `Ошибок: 0`.

- [ ] **Step 6: Запустить и проверить лог**

```powershell
Start-Process "F:\Games\Steam\steamapps\common\Ostranauts\RUNSAVE.bat" -WorkingDirectory "F:\Games\Steam\steamapps\common\Ostranauts"
```

Подождать появления процесса, затем:

```bash
until grep -q "\[i18n\] slice:" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log" 2>/dev/null; do sleep 3; done
grep "\[i18n\] slice:" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
grep -c "OUTPUTTING STACK TRACE" "/c/Users/Low/AppData/LocalLow/Blue Bottle Games/Ostranauts/Player.log"
grep -c "Exception" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
```

Ожидается: строка `slice: привязан Canvas GUI/...`, оба счётчика (краш и Exception) — `0`.

**Гейт:** если в логе `путь не разрешился на '<сегмент>'` — сцена `level2`, на которой строился путь при извлечении, не является активной сценой при старте игры (или структура отличается от сохранённой). Выбрать другую контрольную запись из `scene`-подмножества, желательно ту, что относится к экрану, доступному сразу после автозагрузки сейва, и повторить с Step 1.

- [ ] **Step 7: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: prove scene-path binding on single record"
```

---

## Task 3: Расширение `LanguagePack`/тестов на разбор пути

**Files:**
- Create: `core/OstraI18n.Core/PathKey.cs`
- Modify: `core/OstraI18n.Core.Tests/Program.cs`

**Interfaces:**
- Consumes: ничего
- Produces: `PathKey.Segments(root, path) → string[]`, `PathKey.Matches(objectPath, catalogPath) → bool` — используется в Task 5 для `asset`-сопоставления

- [ ] **Step 1: Написать падающий тест**

В `core/OstraI18n.Core.Tests/Program.cs` перед строкой `Console.WriteLine(failed == 0 ...)` добавить:

```csharp
        Console.WriteLine("PathKey");
        Eq(string.Join("/", PathKey.Segments("GUIBountyDetails", new[] { "LeftText", "txtDanger" })),
           "GUIBountyDetails/LeftText/txtDanger", "полный путь из root+path");
        // clone-суффикс "(Clone)" должен игнорироваться при сравнении —
        // инстанцированные копии префаба получают его от Unity автоматически.
        var eq1 = PathKey.Matches(
            new[] { "GUIBountyDetails(Clone)", "LeftText", "txtDanger" },
            new[] { "GUIBountyDetails", "LeftText", "txtDanger" });
        Eq(eq1 ? "yes" : "no", "yes", "суффикс (Clone) игнорируется");
        var eq2 = PathKey.Matches(
            new[] { "GUIBountyDetails(Clone)", "LeftText", "txtOther" },
            new[] { "GUIBountyDetails", "LeftText", "txtDanger" });
        Eq(eq2 ? "yes" : "no", "no", "разный последний сегмент не совпадает");
```

- [ ] **Step 2: Запустить, убедиться в падении**

```bash
cd /f/DEV2/ostra_i18n/core/OstraI18n.Core.Tests && dotnet run 2>&1 | tail -8
```

Ожидается: `CS0246` про `PathKey`.

- [ ] **Step 3: Реализовать `PathKey`**

Файл `core/OstraI18n.Core/PathKey.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace OstraI18n.Core
{
    /// Сравнение путей в иерархии GameObject. Инстанцированные копии префаба
    /// Unity сама дописывает "(Clone)" к имени корня — сравнение должно это
    /// игнорировать, иначе ни одна копия не совпадёт с каталогом.
    public static class PathKey
    {
        public static string[] Segments(string root, IEnumerable<string> path)
        {
            var list = new List<string> { root };
            list.AddRange(path);
            return list.ToArray();
        }

        public static bool Matches(string[] objectPath, string[] catalogPath)
        {
            if (objectPath.Length != catalogPath.Length) return false;
            for (int i = 0; i < objectPath.Length; i++)
            {
                var a = StripClone(objectPath[i]);
                var b = StripClone(catalogPath[i]);
                if (!string.Equals(a, b, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static string StripClone(string name)
        {
            const string suffix = "(Clone)";
            return name.EndsWith(suffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - suffix.Length)
                : name;
        }
    }
}
```

- [ ] **Step 4: Запустить тесты**

```bash
cd /f/DEV2/ostra_i18n/core/OstraI18n.Core.Tests && dotnet run 2>&1 | tail -10; echo "exit=$?"
```

Ожидается: три новых `PASS`, `ALL PASS`, `exit=0`.

- [ ] **Step 5: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: PathKey for clone-suffix-aware path matching"
```

---

## Task 4: Вертикальный срез — один `asset`-объект (клонируемый префаб)

Самая рискованная часть фазы: привязка к объектам, которые ещё не существуют на момент запуска игры и появляются позже через `Instantiate`. Проверяется на одной записи через `OnEnable`-хук — тот же паттерн, что уже безопасно работает в Фазе 1 (приведение типа через `as`), но критерий срабатывания здесь — путь, не текст.

**Files:**
- Modify: `plugin/OstraI18n/PrefabBinder.cs`
- Modify: `plugin/OstraI18n/Plugin.cs`

**Interfaces:**
- Consumes: `PathKey.Matches`
- Produces: `PrefabBinder.ApplyAssetHook(Harmony)` — доказательство механизма для `asset`-записей

- [ ] **Step 1: Найти лёгкую для проверки `asset`-запись**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json
d = json.load(open(r'F:\DEV2\ostra_i18n\catalog\prefabs.json', encoding='utf-8'))
asset = [e for e in d if e['kind']=='asset' and len(e['path'])<=2 and len(e['literal'])<30]
for e in asset[:8]:
    print(e['root'], '/', '/'.join(e['path']), '->', repr(e['literal']))
"
```

Записать выбранную запись — понадобится в Step 2. Короткий путь (≤2 сегмента) снижает риск, что структура успела разойтись с извлечённой.

- [ ] **Step 2: Заменить срез в `PrefabBinder.cs` на `asset`-хук**

Дописать в `plugin/OstraI18n/PrefabBinder.cs` (используйте значения из Step 1 вместо `AssetSliceRoot`/`AssetSlicePath`; пример ниже опирается на реально найденную в этой сессии запись `GUIBountyDetails/LeftText/txtDanger`):

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using OstraI18n.Core;
```

добавить рядом с существующими using в начале файла, затем добавить в класс `PrefabBinder`:

```csharp
        private const string AssetSliceRoot = "GUIBountyDetails";
        private static readonly string[] AssetSlicePath = { "LeftText", "txtDanger" };
        private const string AssetSliceKey = "GUI_ASSET_SLICE_TEST";

        public static void ApplyAssetHook(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(UnityEngine.UI.MaskableGraphic), "OnEnable",
                Type.EmptyTypes);
            if (target == null)
            {
                Plugin.Log.LogWarning("[i18n] asset-hook: MaskableGraphic.OnEnable не найден");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(
                typeof(PrefabBinder).GetMethod(nameof(OnEnablePostfix),
                    BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void OnEnablePostfix(UnityEngine.UI.MaskableGraphic __instance)
        {
            try
            {
                // Приведение через `as`, не прямой каст — этот метод общий для ЛЮБОГО UI-графика
                // (Image, TMP_Text, legacy Text), прямой каст на несовместимый тип уронит процесс
                // (см. Фазу 1, GuiText.OnEnablePostfix).
                if (!(__instance is TMP_Text) && !(__instance is UnityEngine.UI.Text)) return;
                if (__instance.GetComponent<LocalizedText>() != null) return; // уже привязан

                var path = BuildPath(__instance.transform, AssetSlicePath.Length + 1);
                if (path == null) return;
                if (!PathKey.Matches(path, PathKey.Segments(AssetSliceRoot, AssetSlicePath))) return;

                var lt = __instance.gameObject.AddComponent<LocalizedText>();
                lt.Key = AssetSliceKey;
                lt.Apply();
                Plugin.Log.LogInfo("[i18n] asset-slice: привязан " + string.Join("/", path));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] asset-slice hook failed: " + ex);
            }
        }

        // Поднимается от объекта к корню не более maxLen шагов; null, если иерархия короче
        // (не наш случай) или длиннее (объект глубже — тоже не совпадёт по длине в Matches).
        private static string[] BuildPath(Transform leaf, int maxLen)
        {
            var stack = new List<string>();
            var t = leaf;
            for (int i = 0; i < maxLen && t != null; i++)
            {
                stack.Add(t.name);
                t = t.parent;
            }
            if (t != null) return null; // иерархия глубже maxLen — не наш срез
            stack.Reverse();
            return stack.ToArray();
        }
```

- [ ] **Step 3: Добавить тестовый перевод и подключить хук**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe -c "
import json
p = r'F:\DEV2\ostra_i18n\langs\ru\ui\common.json'
d = json.load(open(p, encoding='utf-8'))
d[0]['dict']['GUI_ASSET_SLICE_TEST'] = 'ПРОВЕРКА_АССЕТА'
json.dump(d, open(p,'w',encoding='utf-8'), ensure_ascii=False, indent=2)
print('добавлено')
"
```

В `plugin/OstraI18n/Plugin.cs` рядом с `PrefabBinder.BindSceneSlice();` добавить:

```csharp
            try { PrefabBinder.ApplyAssetHook(new Harmony(GUID + ".prefabs")); }
            catch (Exception ex) { Log.LogError("[i18n] asset hook failed: " + ex); }
```

- [ ] **Step 4: Собрать, задеплоить, запустить**

```bash
cd /f/DEV2/ostra_i18n/plugin/OstraI18n && dotnet build -c Release 2>&1 | tail -6
DST="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/plugins/OstraI18n"
cp -f bin/Release/netstandard2.1/OstraI18n.dll "$DST/OstraI18n.dll" \
&& cp -f /f/DEV2/ostra_i18n/langs/ru/ui/common.json "$DST/langs/ru/ui/common.json"
```

```powershell
Start-Process "F:\Games\Steam\steamapps\common\Ostranauts\RUNSAVE.bat" -WorkingDirectory "F:\Games\Steam\steamapps\common\Ostranauts"
```

- [ ] **Step 5: Проверить лог**

```bash
until grep -q "gui hook\|каталог:" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log" 2>/dev/null; do sleep 3; done
grep -c "OUTPUTTING STACK TRACE" "/c/Users/Low/AppData/LocalLow/Blue Bottle Games/Ostranauts/Player.log"
grep -c "Exception" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
```

Ожидается: оба `0`. Строка `asset-slice: привязан ...` появится в логе только когда экран с этим конкретным объектом (`GUIBountyDetails`) реально откроется в игре — она не обязана быть в первых секундах лога, в отличие от `scene`-среза. Отсутствие строки при пустых счётчиках краша — не гейт, а повод один раз открыть этот экран (детали жертвы контракта) и свериться визуально: должно показаться `ПРОВЕРКА_АССЕТА`.

**Гейт:** ненулевой `Exception` после этого шага — откатить (`git checkout -- plugin/OstraI18n/PrefabBinder.cs`), так как `asset`-хук трогает **все** `OnEnable` в игре, а не одну сцену — цена ошибки здесь выше, чем в Task 2.

- [ ] **Step 6: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: prove asset-clone binding via path-matched OnEnable hook"
```

---

## Task 5: Массовое утверждение (консервативное)

По уроку Фазы 1 (1735 записей утверждены разом → `NullReferenceException` от неучтённого случая) — здесь утверждение ограничивается записями с коротким путём (≤3 сегмента), это резко снижает риск случайного совпадения структуры с чем-то функциональным и держит первый прогон обозримым.

**Files:**
- Modify: `catalog/prefabs.json`
- Modify: `plugin/OstraI18n/PrefabBinder.cs` (переход с single-slice на каталог целиком)
- Create: `langs/en/ui/prefabs.json`, `langs/ru/ui/prefabs.json`

**Interfaces:**
- Consumes: весь каталог `prefabs.json`
- Produces: рабочий перевод префабного текста, без регрессии по логике UI

- [ ] **Step 1: Утвердить консервативное подмножество**

```bash
/c/Users/Low/AppData/Local/Programs/Python/Python314/python.exe - << 'PYEOF'
import json
CAT = r'F:\DEV2\ostra_i18n\catalog\prefabs.json'
d = json.load(open(CAT, encoding='utf-8'))
n = 0
for e in d:
    if len(e['path']) <= 3 and e['literal'].strip():
        e['approved'] = True
        n += 1
json.dump(d, open(CAT, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)

en = {e['key']: e['literal'] for e in d if e['approved']}
json.dump([{"strName": "Game Strings", "strLanguage": "English", "dict": en}],
          open(r'F:\DEV2\ostra_i18n\langs\en\ui\prefabs.json', 'w', encoding='utf-8'),
          ensure_ascii=False, indent=2)
json.dump([{"strName": "Game Strings", "strLanguage": "Russian", "dict": en}],
          open(r'F:\DEV2\ostra_i18n\langs\ru\ui\prefabs.json', 'w', encoding='utf-8'),
          ensure_ascii=False, indent=2)
print('утверждено:', n)
PYEOF
```

Ожидается: `утверждено` в диапазоне сотен (не тысяч — это ожидаемое сужение объёма для безопасности первого прогона).

- [ ] **Step 2: Переписать `PrefabBinder` на работу с полным каталогом**

Заменить содержимое `plugin/OstraI18n/PrefabBinder.cs` целиком:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using OstraI18n.Core;

namespace OstraI18n
{
    internal static class PrefabBinder
    {
        private class Entry { public string[] Path; public string Key; }

        private static readonly List<Entry> SceneEntries = new List<Entry>();
        private static readonly List<Entry> AssetEntries = new List<Entry>();

        public static int LoadCatalog(string pluginDir)
        {
            var path = Path.Combine(pluginDir, "catalog", "prefabs.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("[i18n] каталог префабов не найден: " + path);
                return 0;
            }
            int n = 0;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("approved", out var ap) || !ap.GetBoolean()) continue;
                var kind = e.GetProperty("kind").GetString();
                var root = e.GetProperty("root").GetString();
                var key = e.GetProperty("key").GetString();
                var segs = new List<string> { root };
                foreach (var p in e.GetProperty("path").EnumerateArray()) segs.Add(p.GetString());

                var entry = new Entry { Path = segs.ToArray(), Key = key };
                if (kind == "scene") SceneEntries.Add(entry); else AssetEntries.Add(entry);
                n++;
            }
            Plugin.Log.LogInfo("[i18n] каталог префабов: " + n + " записей ("
                               + SceneEntries.Count + " scene, " + AssetEntries.Count + " asset)");
            return n;
        }

        public static void BindScenes()
        {
            SceneManager.sceneLoaded += (scene, mode) => TryBindAll(scene);
            var active = SceneManager.GetActiveScene();
            if (active.IsValid()) TryBindAll(active);
        }

        private static void TryBindAll(Scene scene)
        {
            int bound = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var entry in SceneEntries)
                {
                    if (entry.Path[0] != root.name) continue;
                    var t = root.transform;
                    bool ok = true;
                    for (int i = 1; i < entry.Path.Length; i++)
                    {
                        t = t.Find(entry.Path[i]);
                        if (t == null) { ok = false; break; }
                    }
                    if (!ok || t.GetComponent<LocalizedText>() != null) continue;
                    var lt = t.gameObject.AddComponent<LocalizedText>();
                    lt.Key = entry.Key;
                    bound++;
                }
            }
            if (bound > 0) Plugin.Log.LogInfo("[i18n] scene-привязка: " + bound + " объектов в сцене " + scene.name);
        }

        public static void ApplyAssetHook(Harmony harmony)
        {
            if (AssetEntries.Count == 0) return;
            var target = AccessTools.Method(typeof(UnityEngine.UI.MaskableGraphic), "OnEnable", Type.EmptyTypes);
            if (target == null) return;
            harmony.Patch(target, postfix: new HarmonyMethod(
                typeof(PrefabBinder).GetMethod(nameof(OnEnablePostfix), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void OnEnablePostfix(UnityEngine.UI.MaskableGraphic __instance)
        {
            try
            {
                if (!(__instance is TMP_Text) && !(__instance is UnityEngine.UI.Text)) return;
                if (__instance.GetComponent<LocalizedText>() != null) return;

                foreach (var entry in AssetEntries)
                {
                    var path = BuildPath(__instance.transform, entry.Path.Length);
                    if (path == null) continue;
                    if (!PathKey.Matches(path, entry.Path)) continue;

                    var lt = __instance.gameObject.AddComponent<LocalizedText>();
                    lt.Key = entry.Key;
                    lt.Apply();
                    return;
                }
            }
            catch (Exception ex) { Plugin.Log.LogError("[i18n] asset hook failed: " + ex); }
        }

        private static string[] BuildPath(Transform leaf, int maxLen)
        {
            var stack = new List<string>();
            var t = leaf;
            for (int i = 0; i < maxLen && t != null; i++) { stack.Add(t.name); t = t.parent; }
            if (t != null) return null;
            stack.Reverse();
            return stack.ToArray();
        }
    }
}
```

- [ ] **Step 3: Обновить `Plugin.cs`**

Заменить блок из Task 2/4 (`PrefabBinder.BindSceneSlice()` / `PrefabBinder.ApplyAssetHook(...)` без загрузки каталога) на:

```csharp
            try
            {
                if (PrefabBinder.LoadCatalog(DataDir.Value) > 0)
                {
                    PrefabBinder.BindScenes();
                    PrefabBinder.ApplyAssetHook(new Harmony(GUID + ".prefabs"));
                }
            }
            catch (Exception ex) { Log.LogError("[i18n] prefab binder failed: " + ex); }
```

- [ ] **Step 4: Собрать, задеплоить**

```bash
cd /f/DEV2/ostra_i18n/plugin/OstraI18n && dotnet build -c Release 2>&1 | tail -6
DST="/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/plugins/OstraI18n"
mkdir -p "$DST/catalog"
cp -f bin/Release/netstandard2.1/OstraI18n.dll "$DST/OstraI18n.dll" \
&& cp -f /f/DEV2/ostra_i18n/catalog/prefabs.json "$DST/catalog/prefabs.json" \
&& cp -f /f/DEV2/ostra_i18n/langs/en/ui/prefabs.json "$DST/langs/en/ui/prefabs.json" \
&& cp -f /f/DEV2/ostra_i18n/langs/ru/ui/prefabs.json "$DST/langs/ru/ui/prefabs.json"
```

Ожидается: `Ошибок: 0`.

- [ ] **Step 5: Запустить и проверить**

```powershell
Start-Process "F:\Games\Steam\steamapps\common\Ostranauts\RUNSAVE.bat" -WorkingDirectory "F:\Games\Steam\steamapps\common\Ostranauts"
```

```bash
until grep -q "каталог префабов:" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log" 2>/dev/null; do sleep 3; done
grep -E "каталог префабов:|scene-привязка:" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
grep -c "OUTPUTTING STACK TRACE" "/c/Users/Low/AppData/LocalLow/Blue Bottle Games/Ostranauts/Player.log"
grep -c "Exception" "/f/Games/Steam/steamapps/common/Ostranauts/BepInEx/LogOutput.log"
```

Ожидается: `каталог префабов: N записей (...)`, оба счётчика краша/исключений — `0`.

**Гейт:** ненулевой `Exception` — немедленно откатить весь `PrefabBinder`/`ApplyAssetHook` до диагностики (`OnEnable`-хук здесь глобальный, ошибка в нём затрагивает весь UI игры, не один экран).

- [ ] **Step 6: Попросить пользователя визуально подтвердить**

Единственная проверка, требующая взгляда на экран в этой фазе: открыть экран, где раньше было замечено `"Build-a-Resume Center"` (создание персонажа → карьера), и подтвердить, что строка теперь на русском (или, если перевод ещё не сделан для этого конкретного ключа — что она хотя бы не пропала и не превратилась в мусор).

- [ ] **Step 7: Коммит**

```bash
cd /f/DEV2/ostra_i18n && git add -A && git commit -m "feat: catalog-driven prefab/scene text binding (conservative subset)"
```

---

## Определение готовности фазы

1. `dotnet run` в `core/OstraI18n.Core.Tests` даёт `ALL PASS`, код возврата 0
2. В логе игры: `каталог префабов: N записей`, `scene-привязка: M объектов`
3. `grep -c "OUTPUTTING STACK TRACE"` по `Player.log` — `0`
4. `grep -c "Exception"` по `BepInEx/LogOutput.log` — `0`
5. Пользователь визуально подтвердил перевод хотя бы одной ранее регрессировавшей строки
6. Все изменения закоммичены

## Что НЕ входит в эту фазу

- Полное утверждение всех записей каталога (только подмножество с путём ≤3 сегмента)
- Контент-данные (`strName`-оверлей) — Фаза 3
- Формат-строки для конкатенации (270 мест) — Фаза 4
- Псевдоязык, детектор переполнения, пакет для разработчиков — Фаза 4
- Перевод самих извлечённых строк на русский (аналогично Фазе 1 — здесь строится только механизм доставки)
