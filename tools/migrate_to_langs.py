#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
migrate_to_langs.py — сводит рабочую область в каноничную структуру:

  langs/
    languages.json      <- манифест: {"ru": "lang_ru", ...}
    lang_ru/
      strings.json      <- GUI-строки (KEY -> перевод)
      gui.json          <- hardcoded/префаб GUI-текст (english -> перевод)
      grammar.json      <- местоимения движка
      verbs.json        <- парадигмы глаголов движка
      data/             <- (опционально) нарративный контент

Принимает старую раскладку lang/<code>/ с файлами вида grammar_russian.json и
складывает в langs/lang_<code>/ с чистыми именами. Идемпотентен.
"""
import json, io, os, shutil

ROOT = r"F:\DEV2\ostra_i18n"
SRCLANG = os.path.join(ROOT, "lang")      # старая раскладка lang/<code>/
LANGS = os.path.join(ROOT, "langs")       # новая langs/lang_<code>/
os.makedirs(LANGS, exist_ok=True)

NAME_MAP = {
    "strings.json": "strings.json",
    "gui.json": "gui.json",
    "grammar.json": "grammar.json",
    "grammar_russian.json": "grammar.json",
    "verbs.json": "verbs.json",
    "verbs_russian.json": "verbs.json",
}

if os.path.isdir(SRCLANG):
    for code in os.listdir(SRCLANG):
        src = os.path.join(SRCLANG, code)
        if not os.path.isdir(src):
            continue
        dst = os.path.join(LANGS, "lang_" + code)
        os.makedirs(dst, exist_ok=True)
        # data/ копируем целиком
        dsrc = os.path.join(src, "data")
        if os.path.isdir(dsrc):
            shutil.copytree(dsrc, os.path.join(dst, "data"), dirs_exist_ok=True)
        for f in os.listdir(src):
            if f in NAME_MAP and os.path.isfile(os.path.join(src, f)):
                shutil.copy2(os.path.join(src, f), os.path.join(dst, NAME_MAP[f]))
            # grammar_<code>.json / verbs_<code>.json -> clean
            if f == "grammar_%s.json" % code:
                shutil.copy2(os.path.join(src, f), os.path.join(dst, "grammar.json"))
            if f == "verbs_%s.json" % code:
                shutil.copy2(os.path.join(src, f), os.path.join(dst, "verbs.json"))
        print("migrated lang/%s -> langs/lang_%s" % (code, code))

# манифест languages.json из имеющихся папок + алиасы полных названий
# (Plugin.cs Config Language по умолчанию хранит полное имя, напр. "Russian" —
#  без алиаса плагин не находит папку и тихо выключается).
FULLNAME_ALIASES = {
    "ru": "russian", "de": "german", "fr": "french", "es": "spanish",
    "pt": "portuguese", "zh": "chinese", "ja": "japanese", "ko": "korean",
    "pl": "polish", "uk": "ukrainian",
}
man = {}
if os.path.isdir(LANGS):
    for d in sorted(os.listdir(LANGS)):
        if os.path.isdir(os.path.join(LANGS, d)) and d.startswith("lang_"):
            code = d[5:]
            man[code] = d
            if code in FULLNAME_ALIASES:
                man[FULLNAME_ALIASES[code]] = d
io.open(os.path.join(LANGS, "languages.json"), "w", encoding="utf-8").write(
    json.dumps(man, ensure_ascii=False, indent=2))
print("languages.json:", man)
for d in sorted(os.listdir(LANGS)):
    p = os.path.join(LANGS, d)
    if os.path.isdir(p):
        print("  %s/: %s" % (d, sorted(os.listdir(p))))
