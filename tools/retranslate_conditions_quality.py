#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
retranslate_conditions_quality.py — high-quality A+B+C ensemble pass for
all conditions (and simple conditions) using Qwen 3.8 Max with enhanced
grammar/token agreement and anti-calque instructions.

Usage: python tools/retranslate_conditions_quality.py
"""
import concurrent.futures
import io
import json
import os
import sys
import threading
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LANG_SRC = os.path.join(ROOT, "lang_src")
RU_DATA = os.path.join(ROOT, "langs", "ru", "data")

sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_json, check_api  # noqa: E402
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from import_old_translation import (  # noqa: E402
    load_category, load_simple_category, is_simple, output_category_for,
)

BATCH = 10
MAX_WORKERS = 150
LOG = os.path.join(LANG_SRC, "conditions_quality.log")
DONE_FILE = os.path.join(LANG_SRC, "conditions_quality_done.txt")

CUR_DATA_ROOT = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data"

TRANSLATABLE = ("strTitle", "strDesc", "strTooltip", "strNameFriendly", "strNameShort", "strFriendlyName",
                 "strDescription", "strFriendlyDescription", "description")

CATEGORIES = ["conditions", "conditions_simple"]

_log_lock = threading.Lock()
_done_lock = threading.Lock()
_file_locks = {}


def log(msg):
    line = "[%s] %s" % (time.strftime("%H:%M:%S"), msg)
    print(line, flush=True)
    with _log_lock:
        with io.open(LOG, "a", encoding="utf-8") as f:
            f.write(line + "\n")


def load_json(path):
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


def save_json(path, data):
    tmp = path + ".tmp"
    with io.open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    os.replace(tmp, path)


TRANSLATE_SYS_PROMPT = """Ты — старший редактор официальной русской локализации игры Ostranauts
(суровый hard sci-fi симулятор разборки списанных кораблей и выживания на
орбитальных станциях у пояса астероидов; тон циничный, потрёпанный, без пафоса).

Тебе дают массив строк описаний состояний, черт и статусов (Conditions/Traits).
У каждой строки есть "en" (оригинал), "cur" (текущий русский перевод) и "ctx" (ID записи).

ТВОЯ ЗАДАЧА: написать идеальный русский игровой текст — грамотный, атмосферный,
без корявых английских калек и без грамматических несогласований.

КРИТИЧЕСКИЕ ПРАВИЛА:
1. НЕТ КОНФЛИКТАМ ЛИЦА С [us]:
   [us] для игрока отображается как «Ты», а для других членов экипажа как «Джон / Он / Она».
   Поэтому любой глагол БЕЗ квадратных скобок после [us] ЗАПРЕЩЕНО ставить в личную форму 3-го лица
   (не должно получаться «Ты устаёт», «Ты пытается», «Ты несёшь»).
   Используй безличные/назывные обороты или отглагольные существительные:
   - "[us] [is] out of shape and tires faster than most." -> "[us] [is] не в форме: быстрая утомляемость от любых нагрузок."
   - "[us] [is] unencumbered by what [us-contractIs] carrying." -> "Переносимый груз не стесняет движений [us-gen]."

2. СКАЗУЕМЫЕ СО СВЯЗКОЙ [is] (ТОЛЬКО ИМЕНИТЕЛЬНЫЙ ПАДЕЖ):
   В русском [is] в настоящем времени рендерится как пустая строка.
   Поэтому после [is] ставь существительное строго в ИМЕНИТЕЛЬНОМ падеже:
   - "[us] [is] now a shipbreaker." -> "[us] сейчас [is] разборщик кораблей." (НЕ "разборщиком кораблей").

3. ЗАПРЕТ КАЛЕК:
   - Никаких "У тебя тепло" для "You are warm" -> "Температура тела [us-gen] в норме." или "[us] [is] в тепле и уюте."
   - Никаких "У тебя навык..." -> "Владеет навыком..." или "Навык: ...".

4. СОХРАНЕНИЕ ТОКЕНОВ:
   - Токены [us], [them], [us-gen], [them-gen], [is], [has], [starts] и т.д. сохраняй в квадратных скобках.
   - Плейсхолдеры {0}/%s, теги <b>/<i>/<color=...>, \\n сохраняй буквально.

Ответ — СТРОГО JSON-объект {"id": "финальный текст", ...} с теми же id."""

ARBITER_SYS_PROMPT = """Ты главный редактор русской локализации игры Ostranauts.
Тебе дан массив объектов с "en", "cur", "a" и "b" (двумя независимыми вариантами перевода).
Выбери лучший финальный вариант или составь идеальный синтез:
1. Сохранены все игровые токены в квадратных скобках ([us], [them], [is], [has] и т.д.).
2. Грамматика безупречна: нет конфликтов 2-го/3-го лица ("Ты устаёт"), нет ошибочного творительного падежа ("Ты разборщиком"), нет калек ("У тебя тепло").
3. Текст звучит как живой профессиональный русский игровой перевод.

Ответ — СТРОГО JSON-объект {"id": "финальный текст", ...} с теми же id."""


def collect_condition_items():
    overlays = {}
    for fn in os.listdir(RU_DATA):
        overlays[fn] = load_json(os.path.join(RU_DATA, fn))

    items = []
    seen = set()
    for cat in CATEGORIES:
        cur_en = load_simple_category(CUR_DATA_ROOT, cat) if is_simple(cat) else load_category(CUR_DATA_ROOT, cat)
        out_cat = output_category_for(cat)
        fname = out_cat.replace("/", "_") + ".json"
        target_dict = overlays.get(fname, {})

        for name, data in cur_en.items():
            if not isinstance(data, dict):
                continue
            for fld in TRANSLATABLE:
                en_val = data.get(fld)
                if not en_val or not isinstance(en_val, str) or not en_val.strip():
                    continue
                cur_val = ""
                if name in target_dict and isinstance(target_dict[name], dict):
                    cur_val = target_dict[name].get(fld, "")
                item_id = f"{fname}::{name}::{fld}"
                if item_id in seen:
                    continue
                seen.add(item_id)
                items.append({
                    "id": item_id,
                    "file": fname,
                    "name": name,
                    "field": fld,
                    "en": en_val,
                    "cur": cur_val,
                    "ctx": f"{cat}/{name}.{fld}",
                })
    return items


def process_batch(batch, done_set):
    needed = [x for x in batch if x["id"] not in done_set]
    if not needed:
        return 0, 0

    user_payload = [{"id": x["id"], "en": x["en"], "cur": x["cur"], "ctx": x["ctx"]} for x in needed]

    # Candidate A (temp=0.4)
    cand_a = chat_json(
        TRANSLATE_SYS_PROMPT,
        user_payload,
        model="qwen",
        temperature=0.4
    ) or {}

    # Candidate B (temp=0.8)
    cand_b = chat_json(
        TRANSLATE_SYS_PROMPT,
        user_payload,
        model="qwen",
        temperature=0.8
    ) or {}

    arbiter_payload = []
    for x in needed:
        iid = x["id"]
        arbiter_payload.append({
            "id": iid,
            "en": x["en"],
            "cur": x["cur"],
            "a": cand_a.get(iid, x["cur"]),
            "b": cand_b.get(iid, x["cur"]),
        })

    # Arbiter C (temp=0.2)
    final_map = chat_json(
        ARBITER_SYS_PROMPT,
        arbiter_payload,
        model="qwen",
        temperature=0.2
    ) or {}

    by_file = {}
    for x in needed:
        iid = x["id"]
        val = final_map.get(iid) or cand_a.get(iid) or cand_b.get(iid) or x["cur"]
        if isinstance(val, str) and val.strip():
            by_file.setdefault(x["file"], []).append((x["name"], x["field"], val, iid))

    applied = 0
    for fname, updates in by_file.items():
        lock = _file_locks.setdefault(fname, threading.Lock())
        with lock:
            fpath = os.path.join(RU_DATA, fname)
            cur_data = load_json(fpath)
            for name, fld, val, iid in updates:
                if name not in cur_data:
                    cur_data[name] = {}
                cur_data[name][fld] = val
                applied += 1
            save_json(fpath, cur_data)

    with _done_lock:
        with io.open(DONE_FILE, "a", encoding="utf-8") as f:
            for x in needed:
                f.write(x["id"] + "\n")
                done_set.add(x["id"])

    return len(needed), applied


def main():
    log("=== Starting Conditions & Traits Quality Retranslation (150 workers) ===")
    os.makedirs(LANG_SRC, exist_ok=True)
    done_set = set()
    if os.path.exists(DONE_FILE):
        with io.open(DONE_FILE, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line:
                    done_set.add(line)
        log(f"Resuming: {len(done_set)} items already processed in conditions_quality_done.txt")

    items = collect_condition_items()
    log(f"Total condition items collected: {len(items)}")

    pending = [x for x in items if x["id"] not in done_set]
    log(f"Items pending retranslation: {len(pending)}")

    if not pending:
        log("No items to retranslate!")
        return

    batches = [pending[i:i + BATCH] for i in range(0, len(pending), BATCH)]
    log(f"Batches to process: {len(batches)} (batch size={BATCH}, workers={MAX_WORKERS})")

    t0 = time.time()
    total_processed = 0
    total_applied = 0

    with concurrent.futures.ThreadPoolExecutor(max_workers=MAX_WORKERS) as executor:
        futures = {executor.submit(process_batch, b, done_set): idx for idx, b in enumerate(batches)}
        for future in concurrent.futures.as_completed(futures):
            b_idx = futures[future]
            try:
                proc, app = future.result()
                total_processed += proc
                total_applied += app
                if (b_idx + 1) % 10 == 0 or b_idx == len(batches) - 1:
                    pct = (total_processed / len(pending)) * 100.0
                    elapsed = time.time() - t0
                    log(f"Progress: {total_processed}/{len(pending)} ({pct:.1f}%) | applied={total_applied} | elapsed={elapsed:.1f}s")
            except Exception as ex:
                log(f"Batch {b_idx} failed with error: {ex}")

    log(f"=== Completed Quality Retranslation! Processed: {total_processed}, Applied: {total_applied} ===")


if __name__ == "__main__":
    main()
