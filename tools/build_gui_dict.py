#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_gui_dict.py — собирает словарь GUI-перевода для рантайм-хука.

Источники английского (что реально попадает на экран):
  - gui_unknown.txt          (дамп с живой игры — префабы/запечённое)
  - lang_src/gui_hardcoded.en.json  (захардкоженные C# литералы)

Умно: если английская строка совпадает со значением из strings.json — переиспользуем
готовый перевод из lang/ru/strings.json (консистентно с модом). Остальное — на Qwen.

Результат: lang/ru/gui_auto.json (готовое) + lang_src/gui_need_qwen.en.json (на перевод).
"""
import json, io, os, re

ROOT = r"F:\DEV2\ostra_i18n"
PD = r"F:\Games\Steam\steamapps\common\Ostranauts\BepInEx\plugins\OstraI18n"

strings = set()
du = os.path.join(PD, "gui_unknown.txt")
if os.path.exists(du):
    for line in io.open(du, encoding="utf-8"):
        s = line.rstrip("\n")
        if s:
            strings.add(s)
hc = os.path.join(ROOT, "lang_src", "gui_hardcoded.en.json")
if os.path.exists(hc):
    for k in json.loads(io.open(hc, encoding="utf-8").read()):
        strings.add(k)

NOISE = {"Sample text", "Button", "B", "$TEMPLATE", "Enter Text..."}
def keep(s):
    if not s or len(s) < 2:
        return False
    if ":/" in s:  # dev-файловые пути, запечённые в префабах
        return False
    if s in NOISE:
        return False
    if not re.search(r"[A-Za-z]", s):
        return False
    if s.startswith("GUI_") or s.startswith("ERROR_"):
        return False
    return True

strings = {s for s in strings if keep(s)}

# english value -> russian, из мода (для консистентности с ним)
en = json.loads(io.open(os.path.join(ROOT, "lang_src", "strings.en.json"), encoding="utf-8").read())
ru = json.loads(io.open(os.path.join(ROOT, "lang", "ru", "strings.json"), encoding="utf-8").read())
en2ru = {}
for k, env in en.items():
    rv = ru.get(k)
    if rv and env and env.strip():
        en2ru[env.strip()] = rv

auto, need = {}, {}
for s in sorted(strings):
    key = s.strip()
    if key in en2ru:
        auto[s] = en2ru[key]
    else:
        need[s] = s

io.open(os.path.join(ROOT, "lang", "ru", "gui_auto.json"), "w", encoding="utf-8").write(json.dumps(auto, ensure_ascii=False, indent=2))
io.open(os.path.join(ROOT, "lang_src", "gui_need_qwen.en.json"), "w", encoding="utf-8").write(json.dumps(need, ensure_ascii=False, indent=2))
print("собрано GUI-строк:", len(strings))
print("переиспользовано из мода:", len(auto))
print("на Qwen:", len(need))
print("--- примеры на Qwen ---")
for s in list(need)[:20]:
    print("  ", repr(s))
