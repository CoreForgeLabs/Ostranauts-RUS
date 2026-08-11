#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
package.py — собирает lang/<lang>/ (рабочую область) в готовый языковой пакет.

Структура "папка на язык + манифест":
  - плагин: langs/lang_<lang>/ (grammar.json, verbs.json, gui.json, strings.json)
  - манифест: langs/languages.json (язык добавляется одной строкой)
  - мод: Mods/lang_<lang>/data/ (нарративный контент, если есть lang/<lang>/data/)

Запуск:  python package.py [lang]     (по умолчанию ru)
После: выстави язык в конфиге плагина (Language = <lang>). Всё подтянется из папки.
"""
import json, io, os, sys, shutil

ROOT = r"F:\DEV2\ostra_i18n"
GAME = r"F:\Games\Steam\steamapps\common\Ostranauts"
LANG = sys.argv[1] if len(sys.argv) > 1 else "ru"
LANGD = os.path.join(ROOT, "langs", "lang_" + LANG)
PLUGIN_LANGS = os.path.join(GAME, "BepInEx", "plugins", "OstraI18n", "langs")
PACK = os.path.join(PLUGIN_LANGS, "lang_" + LANG)
MODDIR = os.path.join(GAME, "Ostranauts_Data", "Mods", "lang_" + LANG)

if not os.path.isdir(LANGD):
    print("ERROR: нет lang/%s — сначала extract.py + translate.py %s" % (LANG, LANG)); sys.exit(1)

# 1. плагин langs/lang_<lang>/
os.makedirs(PACK, exist_ok=True)
for fn in ("grammar.json", "verbs.json", "gui.json", "strings.json"):
    src = os.path.join(LANGD, fn)
    if os.path.exists(src):
        shutil.copy2(src, os.path.join(PACK, fn))
        print("OK: pack", fn)
    else:
        print("  (нет %s — пропускаю)" % fn)

# 2. манифест languages.json — язык добавляется одной строкой
man_path = os.path.join(PLUGIN_LANGS, "languages.json")
man = {}
if os.path.exists(man_path):
    try:
        man = json.loads(io.open(man_path, encoding="utf-8").read())
    except Exception:
        man = {}
man[LANG] = "lang_" + LANG
man[LANG.lower()] = "lang_" + LANG
io.open(man_path, "w", encoding="utf-8").write(json.dumps(man, ensure_ascii=False, indent=2))
print("OK: languages.json =", man)

# 3. мод для нарративного контента (если есть lang/<lang>/data/)
data_src = os.path.join(LANGD, "data")
if os.path.isdir(data_src):
    shutil.copytree(data_src, os.path.join(MODDIR, "data"), dirs_exist_ok=True)
    mod_info = [{"strName": "lang_%s" % LANG, "strAuthor": "OstraI18n", "strModURL": "",
                 "strGameVersion": "0.15.0.34", "strModVersion": "0.1.0",
                 "strNotes": "Language pack (narrative data). GUI/strings/grammar — через плагин."}]
    io.open(os.path.join(MODDIR, "mod_info.json"), "w", encoding="utf-8").write(json.dumps(mod_info, ensure_ascii=False, indent=2))
    lo_path = os.path.join(GAME, "Ostranauts_Data", "Mods", "loading_order.json")
    lo = {"strName": "Mod Loading Order", "strNotes": "core first, then language pack",
          "aLoadOrder": ["core", "lang_" + LANG], "aIgnorePatterns": []}
    io.open(lo_path, "w", encoding="utf-8").write(json.dumps([lo], ensure_ascii=False, indent=2))
    print("OK: мод данных + loading_order.json")
else:
    print("note: нет lang/%s/data/ — нарративный мод пропущен (GUI/строки/грамматика идут через плагин)" % LANG)

print("\nГОТОВО. Язык '%s' собран в %s. Язык движка: конфиг плагина (Language = %s)." % (LANG, PACK, LANG))
