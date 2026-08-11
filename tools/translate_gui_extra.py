#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
translate_gui_extra.py — переводит вторую волну GUI-строк, пойманных периодическим
сканом сцены (chargen category labels, tutorial titles, misc buttons), уже
отфильтрованных от мусора (пути, dev-debug строки, имена NPC, CJK).
Источник: lang_src/gui_extra_need.en.json (ключи с пустыми значениями).
Результат мержится в тот же gui.json, что и основной translate_gui.py.
"""
import json, io, os, sys, threading, concurrent.futures
sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_json, check_api

ROOT = r"F:\DEV2\ostra_i18n"
LANG = sys.argv[1] if len(sys.argv) > 1 else "ru"
LANGNAME = {"ru": "русский"}.get(LANG, LANG)
PLUGIN_PACK = os.path.join(r"F:\Games\Steam\steamapps\common\Ostranauts\BepInEx\plugins\OstraI18n\langs", "lang_" + LANG)

NEED = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "lang_src", "gui_extra_need.en.json")
OUT_WS = os.path.join(ROOT, "langs", "lang_" + LANG, "gui.json")
OUT_PLUG = os.path.join(PLUGIN_PACK, "gui.json")
LOG = os.path.join(ROOT, "lang", LANG, "translate_gui_extra_%s.log" % LANG)
BATCH = 25

def log(m):
    print(m)
    io.open(LOG, "a", encoding="utf-8").write(m + "\n")

need = json.loads(io.open(NEED, encoding="utf-8").read())
existing = json.loads(io.open(OUT_WS, encoding="utf-8").read()) if os.path.exists(OUT_WS) else {}
log("need Qwen (extra wave): %d | already in gui.json: %d" % (len(need), len(existing)))

result = dict(existing)
keys = [k for k in need.keys() if k not in result]
log("actually new: %d" % len(keys))

if keys:
    api = check_api(model="qwen")
    log("api: %s" % api)
    if not api.get("ok"):
        log("ERROR: Qwen-прокси недоступен"); sys.exit(1)
    SYS = ("Ты переводчик UI-строк игры Ostranauts (hard sci-fi космосим). Переведи каждую строку на %s. "
           "Это разношёрстный набор: короткие кнопки/заголовки/лейблы категорий чарджена (SKIN/HAIR/...), "
           "названия туториалов (после 'Tutorial:'), полные фразы интерфейса. Кратко и по-геймдевовски. "
           "Сохраняй '\\n' как есть (перенос строки внутри кнопки), сохраняй токены вида $PlayerCharacterGivenName "
           "и плейсхолдеры в скобках без изменений. Ответ — СТРОГО JSON-объект {\"английский\": \"перевод\"}." % LANGNAME)
    # All batches fire at once into the Qwen pool (up to 150 concurrent, 219 free accounts) instead
    # of waiting on each other sequentially — was the bottleneck (11 batches one-at-a-time).
    batches = [keys[i:i + BATCH] for i in range(0, len(keys), BATCH)]
    lock = threading.Lock()

    def run_batch(idx, batch):
        items = [{"en": k} for k in batch]
        try:
            res = chat_json(SYS, items, model="qwen")
        except Exception as e:
            return idx, batch, None, str(e)
        return idx, batch, res, None

    with concurrent.futures.ThreadPoolExecutor(max_workers=len(batches), thread_name_prefix="gui-extra") as ex:
        futures = [ex.submit(run_batch, i, b) for i, b in enumerate(batches)]
        for fut in concurrent.futures.as_completed(futures):
            idx, batch, res, err = fut.result()
            if err:
                log("batch %d err: %s" % (idx + 1, err)); continue
            got = 0
            with lock:
                if isinstance(res, dict):
                    for k, v in res.items():
                        if not str(v).strip(): continue
                        if k in need: result[k] = str(v); got += 1
                        else:
                            for cand in batch:
                                if cand == k or cand.strip() == k.strip():
                                    result[cand] = str(v); got += 1; break
                elif isinstance(res, list):
                    for j, el in enumerate(res):
                        if isinstance(el, dict):
                            kk = el.get("en") or el.get("key")
                            vv = el.get(LANG) or el.get("translation") or el.get("text")
                            if kk and vv: result[kk] = str(vv); got += 1
                        elif isinstance(el, str) and j < len(batch):
                            result[batch[j]] = el; got += 1
                log("batch %d: +%d (итого %d)" % (idx + 1, got, len(result)))
                io.open(OUT_WS, "w", encoding="utf-8").write(json.dumps(result, ensure_ascii=False, indent=2))

io.open(OUT_WS, "w", encoding="utf-8").write(json.dumps(result, ensure_ascii=False, indent=2))
os.makedirs(os.path.dirname(OUT_PLUG), exist_ok=True)
io.open(OUT_PLUG, "w", encoding="utf-8").write(json.dumps(result, ensure_ascii=False, indent=2))
log("DONE gui.json: %d strings -> workspace + plugin" % len(result))
