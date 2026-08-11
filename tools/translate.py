#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
translate.py — переводит недостающие строки через локальный Qwen-прокси (KWEN)
в lang/<lang>/strings.json. Резюмируемый: уже переведённое не трогает.

Запуск:  python translate.py [lang]      (по умолчанию ru; de/fr/es/zh/... любой)
Требует запущенный KWEN-прокси на 127.0.0.1:3089 (см. README пользователя).

Идемпотентен: перезапуск продолжит с места, где остановился.
"""
import json, io, os, sys
sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_json, check_api

ROOT = r"F:\DEV2\ostra_i18n"
LANG = sys.argv[1] if len(sys.argv) > 1 else "ru"
LANGNAME = {
    "ru": "русский", "de": "немецкий", "fr": "французский", "es": "испанский",
    "pt": "португальский", "zh": "китайский (упрощённый)", "ja": "японский",
    "ko": "корейский", "pl": "польский", "uk": "украинский", "en": "английский",
}.get(LANG, LANG)

SRC = os.path.join(ROOT, "lang_src", "strings.en.json")
OUTD = os.path.join(ROOT, "langs", "lang_" + LANG)
os.makedirs(OUTD, exist_ok=True)
OUT = os.path.join(OUTD, "strings.json")
LOG = os.path.join(OUTD, "translate_%s.log" % LANG)
BATCH = 20

def log(msg):
    print(msg)
    with io.open(LOG, "a", encoding="utf-8") as f:
        f.write(msg + "\n")

src = json.loads(io.open(SRC, encoding="utf-8").read())
done = {}
if os.path.exists(OUT):
    done = json.loads(io.open(OUT, encoding="utf-8").read())
missing = {k: v for k, v in src.items() if k not in done or not str(done.get(k, "")).strip()}
log("source keys: %d | translated: %d | missing: %d" % (len(src), len(done), len(missing)))
if not missing:
    log("nothing to translate — lang/%s/strings.json полон" % LANG)
    sys.exit(0)

api = check_api(model="qwen")
log("api check: %s" % api)
if not api.get("ok"):
    log("ERROR: Qwen-прокси недоступен. Запусти KWEN (pm2 start .../ecosystem.config.cjs).")
    sys.exit(1)

SYS = (
    "Ты переводчик игры Ostranauts (hard sci-fi космосим от Blue Bottle Games). "
    "Переведи каждый элемент на %s язык. "
    "Правила: кратко и точно, в терминологии жанра; сохраняй разметку (<b>, </b>, <align>, \\n, т.п.) "
    "и токены вида [us], [them], [verb], [is] СТРОГО как есть (это движковые плейсхолдеры); "
    "не переводь KEY. Ответ — СТРОГО JSON-объект {\"KEY\": \"перевод\"} без пояснений и markdown."
    % LANGNAME
)

translated = dict(done)
keys = list(missing.keys())
total_batches = (len(keys) + BATCH - 1) // BATCH
bnum = 0
for i in range(0, len(keys), BATCH):
    bnum += 1
    batch = keys[i:i + BATCH]
    items = [{"key": k, "text": missing[k]} for k in batch]
    try:
        res = chat_json(SYS, items, model="qwen")
    except Exception as e:
        log("batch %d/%d ERROR: %s" % (bnum, total_batches, e))
        continue
    got = 0
    if isinstance(res, dict):
        for k, v in res.items():
            if k in missing and str(v).strip():
                translated[k] = str(v)
                got += 1
    elif isinstance(res, list):
        for el in res:
            if isinstance(el, dict):
                kk = el.get("key")
                vv = el.get(LANG) or el.get("translation") or el.get("text")
                if kk in missing and vv:
                    translated[kk] = str(vv)
                    got += 1
    # сохраняем прогресс после каждого батча (резюмируемость)
    io.open(OUT, "w", encoding="utf-8").write(json.dumps({k: translated[k] for k in sorted(translated)}, ensure_ascii=False, indent=2))
    log("batch %d/%d: +%d (итого %d/%d)" % (bnum, total_batches, got, len(translated), len(src)))

log("DONE. lang/%s/strings.json: %d/%d keys" % (LANG, len(translated), len(src)))
