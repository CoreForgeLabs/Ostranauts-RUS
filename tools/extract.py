#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
extract.py — извлекает исходный (английский) текст игры в lang_src/.

Это "удобное место" для перевода: чистые {ключ: английский} файлы,
которые может заполнять человек или LLM (Qwen), без доступа к игре/коду.

Запуск:  python extract.py
Результат: lang_src/strings.en.json  (GUI-строки)
"""
import json, io, os

ROOT = r"F:\DEV2\ostra_i18n"
LIVE = os.path.join(ROOT, "data_live", "strings.json")   # живой strings.json из игры
OUT = os.path.join(ROOT, "lang_src")
os.makedirs(OUT, exist_ok=True)

def load_pairs(path):
    j = json.loads(io.open(path, encoding="utf-8-sig").read())
    av = j[0]["aValues"]
    return {str(av[i]): av[i+1] for i in range(0, len(av)-1, 2)}

pairs = load_pairs(LIVE)
src = {k: pairs[k] for k in sorted(pairs)}
out = os.path.join(OUT, "strings.en.json")
io.open(out, "w", encoding="utf-8").write(json.dumps(src, ensure_ascii=False, indent=2))
print("OK: extracted", len(src), "GUI strings ->", out)
print("Формат: {KEY: english text}. Переводчик заполняет значения, ключи НЕ трогает.")
