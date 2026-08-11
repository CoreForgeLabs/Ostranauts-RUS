#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
import_old.py — импортирует уже готовый перевод из старого мода RUS_CoreForgeLabs
в lang/<lang>/strings.json как базу (чтобы не переводить заново то, что уже переведено).

Запуск:  python import_old.py [lang]     (по умолчанию ru)
"""
import json, io, os, sys

ROOT = r"F:\DEV2\ostra_i18n"
OLD = r"F:\Games\Steam\steamapps\common\Ostranauts\old\Ostranauts_Data\Mods\RUS_CoreForgeLabs\data\strings\strings.json"
LANG = sys.argv[1] if len(sys.argv) > 1 else "ru"
OUTD = os.path.join(ROOT, "langs", "lang_" + LANG)
os.makedirs(OUTD, exist_ok=True)

j = json.loads(io.open(OLD, encoding="utf-8-sig").read())
av = j[0]["aValues"]
pairs = {str(av[i]): av[i+1] for i in range(0, len(av)-1, 2)}
out = os.path.join(OUTD, "strings.json")
io.open(out, "w", encoding="utf-8").write(json.dumps({k: pairs[k] for k in sorted(pairs)}, ensure_ascii=False, indent=2))
print("OK: imported", len(pairs), "reusable", LANG, "strings ->", out)
