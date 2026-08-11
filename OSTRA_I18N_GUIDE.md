# Ostranauts — система мультиязычной локализации (i18n)
## Полный гайд: что сделано, как устроено, как продолжать (вручную или другой нейронкой)

> Этот файл — единая точка входа. Здесь описано ВСЁ: движок, плагин, мод, шрифты,
> пайплайн перевода, пути, план и подводные камни. Раздел "ПЛАН" — roadmap.

---

## 0. TL;DR (что уже работает)

- **Движок подстановки `[us]`/`[them]`/`[verb]` взломан и пропатчен под любой язык.**
  Это была корневая проблема (местоимения/спряжения). Решено на уровне движка.
- **Шрифты с кириллицей есть в самой игре** (`NotoSansGC-Regular SDF`, 256/256 кириллицы)
  и уже стоят в fallback-цепочках основных шрифтов. Плагин дополнительно страхует.
- **Мод-система перекрывает строки** — перевод живёт в `Mods/lang_<язык>/`, не трогает
  файлы игры, переживает обновления.
- **Пайплайн перевода через локальный Qwen (KWEN)** работает: extract → import_old →
  translate → package.
- **Всё проверено вживую**: настоящие NPC получают русскую грамматику
  ("Josiah Ellison говорит hello", "Shelby Destiny Bass была late", "они машут").

---

## 1. История и суть проблемы

Раньше делали **только перевод на русский** (не i18n) через XUnity.AutoTranslator +
кастомный патч. Была проблема с **местоимениями и спряжениями**: движок игры жёстко
прибит к английской грамматике.

### Почему это сложно
Текст в игре — не статичные строки, а **шаблоны с токенами**: `[us] [says] hello`,
`[them-obj]`, `[us-contractIs]`, `[adds]`. Движок (`GrammarUtils`) подставляет
местоимения (he/she/they), спрягает глаголы по-английски (say/says), добавляет
английские конструкции ('s, the, no longer). Для русского это ломается.

### Решение
Вместо перевода готовых строк мы **переписали поведение движка подстановки** через
Harmony-патчи (BepInEx-плагин `OstraI18n`). Движок теперь умеет:
- спрягать глаголы по лицам/родам/числам целевого языка (таблицы в JSON),
- подставлять местоимения целевого языка по падежам,
- опускать копулу где не нужна (русский),
- не добавлять английские 's / the / no longer,
- обращение к игроку на "ты"/"вы" (настраивается).

Теперь цель — **мультиязычность**: та же система, языки вынесены в удобное место.

---

## 2. Архитектура системы (3 слоя)

```
┌─────────────────────────────────────────────────────────────┐
│ СЛОЙ 1: ДВИЖОК (BepInEx-плагин OstraI18n)                   │
│  Патчит грамматику подстановки + шрифтовой fallback.        │
│  Языконезависим по коду; читает таблицы языка из JSON.      │
│  Файлы игры НЕ трогает (Harmony, runtime).                  │
├─────────────────────────────────────────────────────────────┤
│ СЛОЙ 2: КОНТЕНТ (мод Mods/lang_<язык>/)                     │
│  Переведённые строки GUI + нарративные данные.              │
│  Родная мод-система игры, переживает обновления.            │
├─────────────────────────────────────────────────────────────┤
│ СЛОЙ 3: РАБОЧАЯ ОБЛАСТЬ ПЕРЕВОДА (C:\AIWorkspace\ostra_i18n)│
│  lang_src/  — английский источник (извлечено из игры)       │
│  lang/<язык>/ — переводы по языкам (RU, DE, ...)            │
│  tools/     — extract/import_old/translate/package          │
│  ЭТО "удобное место", где живут языки.                      │
└─────────────────────────────────────────────────────────────┘
```

**Поток:** `игра → extract.py → lang_src/strings.en.json → (import_old.py берёт готовое)
→ translate.py (Qwen добивает) → lang/<язык>/strings.json → package.py →
Mods/lang_<язык>/ + данные плагина → игра на целевом языке.`

---

## 3. Где что лежит (все пути)

### На хосте Windows (Low)
| Что | Путь |
|---|---|
| Игра | `F:\Games\Steam\steamapps\common\Ostranauts` |
| Плагин (собранный) | `...\Ostranauts\BepInEx\plugins\OstraI18n\OstraI18n.dll` |
| Языковые данные плагина | `...\BepInEx\plugins\OstraI18n\grammar_russian.json`, `verbs_russian.json` |
| Конфиг плагина | `...\BepInEx\config\com.coreforge.ostra.i18n.cfg` |
| Лог BepInEx | `...\Ostranauts\BepInEx\LogOutput.log` |
| Мод русского | `...\Ostranauts\Ostranauts_Data\Mods\OstraRU\` (и `lang_ru` после package.py) |
| loading_order | `...\Ostranauts_Data\Mods\loading_order.json` |
| Настройки игры | `C:\Users\Low\AppData\LocalLow\Blue Bottle Games\Ostranauts\settings.json` |
| **РАБОЧАЯ ОБЛАСТЬ** | `C:\AIWorkspace\ostra_i18n\` |
| — исходники плагина | `C:\AIWorkspace\ostra_i18n\plugin\OstraI18n\` |
| — **языки (удобное место)** | `C:\AIWorkspace\ostra_i18n\lang\` и `lang_src\` |
| — инструменты | `C:\AIWorkspace\ostra_i18n\tools\` |
| — декомпилят игры | `C:\AIWorkspace\ostra_i18n\decompiled\` (1329 файлов C#) |
| — живые данные | `C:\AIWorkspace\ostra_i18n\data_live\` |
| **Старый перевод (ресурс)** | `F:\Games\Steam\steamapps\common\Ostranauts\old\` |
| — старый мод RUS | `...\old\Ostranauts_Data\Mods\RUS_CoreForgeLabs\` |
| **Qwen-прокси (KWEN)** | `C:\Users\Low\Desktop\DEV\KWEN\` (llm_client.py) |

### Ключевые хеши (проверка целостности / обновлений игры)
- `Assembly-CSharp.dll` (живой): SHA1 `A6222E810F559CADBDBC00BB6474876EA613F451`
- Плагин хранит хеш в `last_hash.txt` и предупреждает при обновлении игры.

---

## 4. Движок подстановки — что пропатчено

Игра: Mono, Unity 6000.3.10f1, `Assembly-CSharp.dll`. Декомпилят в `decompiled/`.
Класс `GrammarUtils` — сердце.

| Метод | Ваниль | Патч |
|---|---|---|
| `Verb` | спрягает по-английски (say/says, "no longer ") | парадигма из `verbs_<lang>.json` (6 лиц + прош.время по родам), опускает копулу, "больше не" |
| `AttemptSubstitution` | местоимения he/him/his + 's | таблица местоимений из `grammar_<lang>.json` по падежам, без 's |
| `AttemptProperName` | хардкод "you"/"the "/"The " | "ты"/"вы" из конфига, без "the" |
| `DataHandler.UnpackTokens` | TryAdd таблиц (core побеждает, моды не перекрывают) | постфикс **насильно перезаписывает** таблицы языка |
| `Localisation.Get` | всегда "English" | возвращает активный язык из конфига |
| `TMP_Settings.get_instance` | — | постфикс ставит кириллический fallback-шрифт в глобальную цепочку TMP |

**Защита от обновлений:**
- `PatchRunner` — каждый патч в try/catch; метод исчез → патч пропускается, ваниль остаётся.
- `VersionGuard` — хеш Assembly-CSharp.dll; при смене предупреждение в лог.
- Патчи runtime (Harmony), файлы игры не правятся → Steam-обновление ничего не ломает.

**Точки расширения движка (v2+):**
- 1-е/2-е лицо в прошедшем времени с родом (нужен CondOwner-пол).
- Склонение имён собственных для possessive — сейчас пропускается (логируется).
- `characterGenderCond` — гендерные варианты `[us-custom-characterGenderCond|m|f|nb]`.

---

## 5. Шрифты

- Текст через **TextMeshPro (SDF-атласы)**, запечены без кириллицы (CYR=0 почти у всех).
- **НО**: `NotoSansGC-Regular SDF` — **полная кириллица (256/256)**, уже в fallback-цепочках
  `robotocondensedb SDF` (шрифт по умолчанию), `Jura-Bold SDF` и др. Русский рендерится из коробки.
- Плагин дополнительно ставит NotoSansGC (или динамический атлас из Roboto) в **глобальный**
  fallback TMP — страховка для шрифтов без цепочки.
- Офлайн-анализатор шрифтов: `analyze_tmp4.py` (UnityPy + TypeTreeGeneratorAPI читают
  TMP_FontAsset из resources.assets/sharedassets0).

---

## 6. Мод-система игры

- Моды: `Ostranauts_Data\Mods\<ИмяМода>\`, зеркалят `StreamingAssets\`.
  `mod_info.json` в корне мода, `data\...` — перекрываемые данные.
- `loading_order.json` лежит **внутри** `Mods\`:
  `[{"strName":"Mod Loading Order","aLoadOrder":["core","lang_ru"],"aIgnorePatterns":[]}]`
- Строки мода перекрывают core (мод грузится после core).
- Эталон: `SampleMod.zip` в корне игры.

### ⚠️ КРИТИЧЕСКАЯ ЛОВУШКА (уже исправлено)
В `settings.json` был протухший `strPathMods` на старое место игры (`C:\Game\...`).
Из-за этого игра не находила `loading_order.json` и грузила только core — мод молча
не работал. **Исправлено**: `strPathMods` сброшен в `""` → дефолтный `Ostranauts_Data\Mods\`.
Бэкап: `settings.json.bak`. Если мод "не грузится" — проверяй этот путь первым делом.

---

## 7. Пайплайн перевода (KWEN / Qwen)

Qwen-прокси: `127.0.0.1:3089` (Qwen), `3088` (DeepSeek), `3090` (пул).
Модель по умолчанию в `llm_client.py` — `qwen` = `qwen3.8-max` (флагман, безлимитный,
с батчингом). Пул: 232 аккаунта, до 150 параллельно.

### Инструменты (`C:\AIWorkspace\ostra_i18n\tools\`)
1. **`extract.py`** — игра → `lang_src/strings.en.json` (чистый `{KEY: english}`).
2. **`import_old.py [lang]`** — импорт готового перевода из старого мода RUS_CoreForgeLabs
   в `lang/<lang>/strings.json` как базу.
3. **`translate.py [lang]`** — добивает недостающие строки через Qwen. Резюмируемый,
   прогресс в `lang/<lang>/translate_<lang>.log`.
4. **`package.py [lang]`** — собирает `lang/<lang>/` в готовый мод `Mods/lang_<lang>/`
   + копирует грамматику/глаголы в плагин + правит `loading_order.json`.

### Как добавить НОВЫЙ язык (например, немецкий) — БЕЗ моего вмешательства
```
cd C:\AIWorkspace\ostra_i18n\tools
python extract.py            # обновить англ. источник (если игра обновилась)
python translate.py de       # Qwen переведёт ВСЕ строки на немецкий
python package.py de         # соберёт мод Mods/lang_de/
# Затем грамматика движка — см. раздел 8 (grammar_de.json + verbs_de.json)
```

### Качество перевода
Системный промпт в `translate.py` (переменная `SYS`): сохраняет разметку и токены
`[us]`/`[them]`/`[verb]`, краткость, терминология жанра. Правь под себя.

---

## 8. Грамматика языка для движка (важно!)

Слой 1 для нового языка требует два JSON в `lang/<lang>/`:
- **`grammar_<lang>.json`** — местоимения по падежам/формам. Образец `lang/ru/grammar_russian.json`
  (категории subj/obj/pos/reflexive/contractIs/contractHas/contractWill, в каждой 6 форм:
  [я, ты, он, она, они, оно]).
- **`verbs_<lang>.json`** — парадигмы глаголов, ключ = английский токен ("says","is","was").
  Образец `lang/ru/verbs_russian.json` (176 глаголов):
  `{"is": {"kind":"copula","omitPresent":true,"past":[...]}, "says": {"present":[6 форм]}, ...}`

**Важно:** ключи глаголов — английские токены из шаблонов игры, их НЕ переводят по смыслу —
мапят формы целевого языка. Полный список англ. токенов-глаголов: 446 штук (дамп
`data_live`/tokens verbs.json и в архиве).

Для языков без падежей (китайский) таблицы проще; для флективных (немецкий, польский) —
сложнее. Это единственная ручная языковая работа; Qwen сгенерирует черновик по образцу RU.

---

## 9. Старый перевод (`old\`) — что переиспользовать

- **`old\...\Mods\RUS_CoreForgeLabs\data\strings\strings.json`** — 928 GUI-строк на русском,
  927 переиспользуемы (78.6% покрытия). Уже импортировано через import_old.py.
- **`old\...\data\<домены>`** — переведённый нарратив (interactions 3.8МБ, condowners 1.6МБ,
  installables 1.5МБ, items 1МБ). **База для нарративного слоя RU.** ⚠️ сверяй ключи с текущей версией.
- **`old\...\plugins\rus_*.json`** — словари (nouns 65КБ, phrases 48КБ, pronouns, exact,
  ship_labels). Могут пригодиться как глоссарий.
- **`old\...\plugins\OstranautsRusPatch.dll`** — старый кастомный патч. Не смешивай с OstraI18n.
- **XUnity.AutoTranslator** в `old\` — runtime-перехват UI-текста, другой подход.
  Не ставь оба одновременно без нужды.

---

## 10. ПЛАН (roadmap)

### Сделано ✅
1. Декомпилят и полный разбор движка подстановки (IL + C#).
2. Плагин OstraI18n: 6 патчей, защита от обновлений, самотесты.
3. Доказано вживую: русская грамматика на настоящих NPC.
4. Шрифтовой fallback (кириллица).
5. Мод-система перекрытия строк + фикс пути в settings.json.
6. Пайплайн перевода Qwen (extract/import_old/translate/package).
7. Русские GUI-строки собраны (928 старых + добивка новых через Qwen).

### Ближайшее (RU) ▶
8. Довести RU GUI до 100% и проверить в игре визуально.
9. Нарративный слой RU: взять `old\...\data\` как базу, сверить ключи с текущей версией,
   добить Qwen расхождения, собрать в `lang/ru/data/`.
10. Расширить `verbs_russian.json` до всех 446 токенов (сейчас 176 — самые частые).

### Мультиязычность (главная цель) ▶
11. Прогнать `translate.py` для de/fr/es/zh/... — GUI-строки на все языки.
12. Сгенерировать `grammar_<lang>.json` + `verbs_<lang>.json` для каждого языка
    (Qwen-черновик по образцу RU + проверка носителем).
13. Унифицировать нарративный слой: extract для data-доменов (не только strings).
14. Переключатель языка в игре (плагин читает активный `lang_<xx>` из loading_order.json сам).

### Полировка ▶
15. Склонение имён собственных (possessive) для флективных языков.
16. Тесты обновления игры: прогнать после очередного патча BBG, убедиться что
    VersionGuard предупреждает и патчи переприменяются.

---

## 11. Как продолжать без меня

### Руками (человек)
- Перевод: редактируй `lang/<lang>/strings.json` (чистый KEY→перевод), затем `package.py`.
- Грамматика: правь `lang/<lang>/grammar_*.json` и `verbs_*.json` по образцу RU.
- Сборка плагина: `cd plugin\OstraI18n; dotnet build -c Release`, копируй DLL в
  `BepInEx\plugins\OstraI18n\`.

### Другой нейронкой
Дай ей ЭТОТ файл + рабочую область `C:\AIWorkspace\ostra_i18n\`. Точки входа:
- Движок: `plugin\OstraI18n\Plugin.cs` (патчи), `Patches.cs`, `RuData.cs`, `FontFallback.cs`.
- Декомпилят: `decompiled\GrammarUtils.cs`, `DataHandler.cs` (LoadMods/UnpackTokens),
  `Interaction.cs`, `CondOwner.cs`.
- Пайплайн: `tools\*.py`. Перевод: `lang_src\` + `lang\`.
- Проверка: плагин пишет `selftest_static.txt`/`modprobe.txt`/`fontprobe.txt` в
  `BepInEx\plugins\OstraI18n\` — встроенные самотесты.

### Сборка плагина из исходников
```
cd C:\AIWorkspace\ostra_i18n\plugin\OstraI18n
dotnet build -c Release
copy bin\Release\netstandard2.1\OstraI18n.dll "F:\Games\Steam\steamapps\common\Ostranauts\BepInEx\plugins\OstraI18n\"
```
Зависимости (HintPath в .csproj): BepInEx 6 be.785 (`bepinex6_be\extracted`), игровые DLL
(`Ostranauts_Data\Managed`).

---

## 12. Troubleshooting

| Симптом | Причина | Решение |
|---|---|---|
| Мод "не грузится", строки английские | протухший `strPathMods` в settings.json | сбросить в `""`, проверить `Mods\loading_order.json` |
| Игра не стартует с BepInEx | старый BepInEx 5.4 несовместим с Unity 6000 | использовать BepInEx 6.0.0-be.785 (уже стоит) |
| Квадраты вместо кириллицы | шрифт без кириллицы и без fallback | плагин ставит NotoSansGC в fallback; см. лог "Cyrillic fallback font installed" |
| Патч не применился после обновления | метод переименовали в новой версии | смотри LogOutput.log "MISSING ..." — ваниль остаётся, чинить под новую сигнатуру |
| Qwen не переводит | прокси не запущен | `pm2 start C:\Users\Low\Desktop\DEV\KWEN\api\ecosystem.config.cjs` |
| Тесты из MCP не видны | MCP работает в session 0 (фон), GUI игры там заморожен | запускать игру в сессии пользователя через планировщик (см. ниже) |

### Запуск игры из фона в сессию пользователя (для тестов)
MCP агент работает в session 0 (сервисной) — там Unity player loop заморожен.
Чтобы игра реально загрузилась с дисплеем, запускай её в интерактивной сессии через
планировщик задач (InteractiveToken). Самотесты плагина тогда отрабатывают полностью.

---

## 13. Самотесты плагина (встроенные)

Плагин при запуске игры пишет в `BepInEx\plugins\OstraI18n\`:
- `selftest_static.txt` — спряжение глаголов/местоимения (работает в меню, поток).
- `selftest_live.txt` — живой тест на настоящих CondOwner (при загруженной сессии).
- `modprobe.txt` — перекрылись ли строки модом (сверка dictStrings).
- `fontprobe.txt` — какие шрифты в UI и есть ли кириллица (по главному потоку).
- `start_marker.txt` — диагностика, вызывается ли Unity `Start()`.

Смотри эти файлы первыми при любой проверке.

---

*Документ составлен автономно агентом. Дата: 2026-08-08. Версия системы: OstraI18n 0.1.3,*
*BepInEx 6.0.0-be.785, игра Unity 6000.3.10f1 (версия 0.15.x).*
