#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
translate_gui.py — переводит GUI-строки (короткие лейблы/заголовки, префабы/хардкод)
через Qwen (KWEN), мержит с переиспользованными (gui_auto.json) и пишет итоговый
gui.json и в рабочую область lang/<lang>/, и в плагин langs/lang_<lang>/gui.json.

Запуск:  python translate_gui.py [lang]     (по умолчанию ru)
"""
import json, io, os, sys
sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_json, check_api

ROOT = r"F:\DEV2\ostra_i18n"
LANG = sys.argv[1] if len(sys.argv) > 1 else "ru"
LANGNAME = {"ru":"русский","de":"немецкий","fr":"французский","es":"испанский","pt":"португальский",
            "zh":"китайский (упрощённый)","ja":"японский","ko":"корейский","pl":"польский","uk":"украинский"}.get(LANG, LANG)
PLUGIN_PACK = os.path.join(r"F:\Games\Steam\steamapps\common\Ostranauts\BepInEx\plugins\OstraI18n\langs", "lang_" + LANG)

NEED = os.path.join(ROOT, "lang_src", "gui_need_qwen.en.json")
AUTO = os.path.join(ROOT, "lang", LANG, "gui_auto.json")
OUT_WS = os.path.join(ROOT, "lang", LANG, "gui.json")
OUT_PLUG = os.path.join(PLUGIN_PACK, "gui.json")
LOG = os.path.join(ROOT, "lang", LANG, "translate_gui_%s.log" % LANG)
BATCH = 25

def log(m):
    print(m)
    io.open(LOG, "a", encoding="utf-8").write(m + "\n")

need = json.loads(io.open(NEED, encoding="utf-8").read()) if os.path.exists(NEED) else {}
auto = json.loads(io.open(AUTO, encoding="utf-8").read()) if os.path.exists(AUTO) else {}
log("need Qwen: %d | reused: %d" % (len(need), len(auto)))

result = dict(auto)
keys = list(need.keys())
if keys:
    api = check_api(model="qwen")
    log("api: %s" % api)
    if not api.get("ok"):
        log("ERROR: Qwen-прокси недоступен"); sys.exit(1)
    SYS = ("Ты переводчик UI-строк игры Ostranauts (hard sci-fi космосим). Переведи каждую строку на %s. "
           "Кратко, как в интерфейсах (кнопки/заголовки/подсказки). Сохраняй токены [us]/[them]/[verb] и "
           "разметку как есть. Строка может обрываться (конкатенация в коде) — переводи как самостоятельный "
           "фрагмент, сохраняя смысл. Ответ — СТРОГО JSON-объект {\"английский\": \"перевод\"}." % LANGNAME)
    for i in range(0, len(keys), BATCH):
        batch = keys[i:i+BATCH]
        items = [{"en": k} for k in batch]
        try:
            res = chat_json(SYS, items, model="qwen")
        except Exception as e:
            log("batch err: %s" % e); continue
        got = 0
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
        log("batch %d: +%d (итого %d)" % (i//BATCH+1, got, len(result)))
        io.open(OUT_WS, "w", encoding="utf-8").write(json.dumps(result, ensure_ascii=False, indent=2))

io.open(OUT_WS, "w", encoding="utf-8").write(json.dumps(result, ensure_ascii=False, indent=2))
os.makedirs(os.path.dirname(OUT_PLUG), exist_ok=True)
io.open(OUT_PLUG, "w", encoding="utf-8").write(json.dumps(result, ensure_ascii=False, indent=2))
log("DONE gui.json: %d strings -> workspace + plugin" % len(result))
