#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
translate_new_categories.py — извлекает и переводит новые категории игрового контента:
- attackmodes/coAttacks (dictAModes) -> langs/ru/data/attackmodes_coAttacks.json
- homeworlds (dictHomeworlds) -> langs/ru/data/homeworlds.json
- ships (dictShips) -> langs/ru/data/ships.json

Использует LLM (qwen3.8-max) через llm_client.chat_text, батчами, с валидацией токенов.
"""
import concurrent.futures
import io
import json
import os
import re
import sys
import time

sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_text, check_api

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA_DIR = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data"
RU_DATA = os.path.join(ROOT, "langs", "ru", "data")
LOG_PATH = os.path.join(ROOT, "lang_src", "translate_new_categories.log")

def log(msg):
    line = "[%s] %s" % (time.strftime("%H:%M:%S"), msg)
    print(line, flush=True)
    with io.open(LOG_PATH, "a", encoding="utf-8") as f:
        f.write(line + "\n")

SYS_PROMPT = """Ты профессиональный локализатор игры Ostranauts — сурового hard sci-fi симулятора выживания
и разборки списанных космических кораблей на орбитах и в поясе астероидов (атмосфера Cowboy Bebop / The Expanse,
тон потрёпанный, индустриальный, без пафоса).

Переведи предоставленные игровые данные на русский язык.
Правила перевода:
- Описания кораблей (description): передавай технический, практичный и слегка потрёпанный тон sci-fi симулятора.
- Обозначения кораблей (designation): краткие морские/авиационные термины (Freighter -> Грузовоз, Tug -> Буксир, Heavy Tug -> Тяжёлый буксир, Scrapper -> Утилизатор, Gunboat -> Канонерка, Courier -> Курьер, Patrol -> Патрульный катер, Yacht -> Яхта, Shuttle -> Шаттл, Heavy Freighter -> Тяжёлый грузовоз, Light Freighter -> Лёгкий грузовоз, Interceptor -> Перехватчик, Hauler -> Тягач/Транспортник).
- Модели и модификации (model, make): технические названия, аббревиатуры оставляй (CR-43, K-Type), модификаторы переводи (Indie Retrofit -> Кустарная модификация, Long Haul -> Дальней дистанции, Fleet Variant -> Флотская модификация).
- Режимы атак (strNameFriendly): названия боевых приёмов и атак оружия (punch -> удар кулаком, slash -> рубящий удар, finishing blow -> добивающий удар, shrapnel -> шрапнель, angle grinder -> угловая шлифмашина, collision -> столкновение, stab -> колющий удар, blunt strike -> дробящий удар).
- Колонии и миры (strColonyName, strMetonym): Port Mojave, Ceres -> Порт-Мохаве, Церера; Ceres -> Церера; Detroit, Earth -> Детройт, Земля; Earth -> Земля; Jade Rabbit, Luna -> Нефритовый Заяц, Луна; Luna -> Луна; Ganymede -> Ганимед; Mars -> Марс; Mercury -> Меркурий; Titan -> Титан; Callisto -> Каллисто; Europa -> Европа; Io -> Ио.
- Если значение равно "$TEMPLATE" или содержит системный плейсхолдер вроде "{0}" — сохраняй без изменений.
- Токены в квадратных скобках ([us], [them], глаголы) — сохраняй в оригинальном виде.

Ответ СТРОГО в формате JSON без разметки Markdown:
{
  "id1": "перевод1",
  "id2": "перевод2"
}
"""

def extract_category_items(cat_name, sub_folder, fields_to_extract):
    src_dir = os.path.join(DATA_DIR, sub_folder)
    items = {}
    for root, _, files in os.walk(src_dir):
        for fn in sorted(files):
            if not fn.endswith(".json"):
                continue
            path = os.path.join(root, fn)
            try:
                with open(path, "r", encoding="utf-8-sig") as fp:
                    data = json.load(fp)
            except Exception as e:
                log("Error reading %s: %s" % (path, e))
                continue
            arr = data if isinstance(data, list) else [data]
            for obj in arr:
                if not isinstance(obj, dict) or not obj.get("strName"):
                    continue
                name = obj["strName"]
                extracted = {}
                for fld in fields_to_extract:
                    val = obj.get(fld)
                    if val is not None and isinstance(val, str) and val.strip():
                        extracted[fld] = val.strip()
                if extracted:
                    items[name] = extracted
    return items

def translate_batch(batch_items, max_retries=3):
    req_dict = {uid: text for uid, text, _ in batch_items}
    user_prompt = "Переведи на русский следующие элементы игрового текста:\n" + json.dumps(req_dict, ensure_ascii=False, indent=2)
    
    for attempt in range(max_retries):
        try:
            raw = chat_text(SYS_PROMPT, user_prompt, model="qwen", temperature=0.1)
            if not raw:
                time.sleep(1)
                continue
            # Strip markdown code blocks if any
            clean = raw.strip()
            if clean.startswith("```json"):
                clean = clean[7:]
            elif clean.startswith("```"):
                clean = clean[3:]
            if clean.endswith("```"):
                clean = clean[:-3]
            clean = clean.strip()
            
            res = json.loads(clean)
            if isinstance(res, dict):
                matched = {}
                for uid, text, _ in batch_items:
                    if uid in res and isinstance(res[uid], str) and res[uid].strip():
                        matched[uid] = res[uid].strip()
                if len(matched) == len(batch_items):
                    return matched
                elif len(matched) > 0:
                    return matched
        except Exception as e:
            log("API attempt %d error: %s" % (attempt + 1, e))
            time.sleep(1)
    return {}

def run_translation_for_category(out_filename, items_dict):
    out_path = os.path.join(RU_DATA, out_filename)
    existing = {}
    if os.path.exists(out_path):
        try:
            with open(out_path, "r", encoding="utf-8") as f:
                existing = json.load(f)
        except:
            existing = {}
    
    to_translate = []
    for str_name, fields in items_dict.items():
        ex_entry = existing.get(str_name, {})
        for fld, orig_val in fields.items():
            if fld in ex_entry and ex_entry[fld].strip():
                continue
            if orig_val == "$TEMPLATE":
                ex_entry[fld] = "$TEMPLATE"
                existing[str_name] = ex_entry
                continue
            unique_id = f"{str_name}___{fld}"
            to_translate.append((unique_id, orig_val, (str_name, fld)))
            
    log("Категория %s: всего полей %d, требуется перевести: %d" % (
        out_filename,
        sum(len(v) for v in items_dict.values()),
        len(to_translate)
    ))
    
    if not to_translate:
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(existing, f, ensure_ascii=False, indent=2)
        return existing
        
    BATCH_SIZE = 10
    batches = [to_translate[i:i + BATCH_SIZE] for i in range(0, len(to_translate), BATCH_SIZE)]
    log("Разбито на %d батчей. Запуск..." % len(batches))
    
    with concurrent.futures.ThreadPoolExecutor(max_workers=10) as executor:
        futures = {executor.submit(translate_batch, b): b for b in batches}
        for future in concurrent.futures.as_completed(futures):
            try:
                res = future.result()
                for uid, ru_text in res.items():
                    parts = uid.split("___")
                    s_name, s_fld = parts[0], parts[1]
                    if s_name not in existing:
                        existing[s_name] = {}
                    existing[s_name][s_fld] = ru_text
                # save incrementally
                with open(out_path + ".tmp", "w", encoding="utf-8") as f:
                    json.dump(existing, f, ensure_ascii=False, indent=2)
                os.replace(out_path + ".tmp", out_path)
            except Exception as e:
                log("Batch future error: %s" % e)
                
    log("Категория %s завершена. Сохранено %d записей в %s." % (out_filename, len(existing), out_path))
    return existing

def main():
    log("=== Старт перевода новых категорий ===")
    api_status = check_api()
    log("LLM API status: %s" % api_status.get("ok"))
    
    # 1. attackmodes/coAttacks
    co_attacks = extract_category_items("attackmodes_coAttacks", os.path.join("attackmodes", "coAttacks"), ["strNameFriendly"])
    run_translation_for_category("attackmodes_coAttacks.json", co_attacks)
    
    # 2. homeworlds
    homeworlds = extract_category_items("homeworlds", "homeworlds", ["strColonyName", "strMetonym"])
    run_translation_for_category("homeworlds.json", homeworlds)
    
    # 3. ships
    ships = extract_category_items("ships", "ships", ["description", "designation", "model", "origin"])
    run_translation_for_category("ships.json", ships)
    
    log("=== Все категории успешно обработаны ===")

if __name__ == "__main__":
    main()
