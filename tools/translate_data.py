#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
translate_data.py — переводит записи контент-слоя (langs/ru/data/<категория>.json),
у которых нет старого перевода (не найдено соответствия в RUS_CoreForgeLabs), через
Qwen, с контекстом категории+strName. Параллельные батчи (Qwen многопоточный).

Пишет результат инкрементально после каждого батча.

Запуск: python translate_data.py [lang]     (по умолчанию ru)
"""
import concurrent.futures
import io
import json
import os
import re
import sys
import time

sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_json, check_api  # noqa: E402

ROOT = r"F:\DEV2\ostra_i18n"
LANG = sys.argv[1] if len(sys.argv) > 1 else "ru"
LANGNAME = {"ru": "русский"}.get(LANG, LANG)

BATCH = 15
MAX_WORKERS = 12
LOG = os.path.join(ROOT, "lang_src", "translate_data_%s.log" % LANG)

TRANSLATABLE = ("strTitle", "strDesc", "strTooltip", "strNameFriendly", "strNameShort", "strFriendlyName")

CATEGORIES = ["interactions", "careers", "conditions", "pda_apps", "installables", "cooverlays",
              "condowners", "ledgerdefs", "pledges", "slots", "headlines", "plots",
              "market/CoCollections", "ads", "rooms", "jobitems", "racing/tracks", "context", "racing/leagues"]

SYS_PROMPT = (
    "Ты локализатор игровых данных Ostranauts — суровый hard sci-fi симулятор разборки "
    "списанных космических кораблей и выживания на орбитальных станциях у пояса астероидов "
    "(сеттинг в духе Cowboy Bebop / The Expanse, тон циничный, потрёпанный, без пафоса). "
    "Переведи каждую строку на %s.\n\n"
    "Каждый элемент содержит \"ctx\" — категорию игровых данных и внутренний ID записи "
    "(interactions=действия, conditions=состояния персонажа, installables=устанавливаемые "
    "предметы, careers=карьеры, pledges=ИИ-задачи экипажа, ledgerdefs=финансовые записи, "
    "plots=сюжетные линии). Используй ctx ТОЛЬКО для понимания роли строки, в перевод не включай.\n\n"
    "Правила:\n"
    "- [us] и [them] — плейсхолдеры действующих лиц (заменятся на 'Ты'/имя/местоимение "
    "в рантайме) — сохраняй БЕЗ ИЗМЕНЕНИЙ на том же месте.\n"
    "- ЛЮБОЕ ДРУГОЕ слово в квадратных скобках — токен спряжения глагола, например "
    "[starts], [adds], [removes], [bashes] — это КЛЮЧ ПОИСКА в таблице спряжений, а НЕ "
    "текст для перевода. Игра сама подставит правильную русскую форму под фактическое лицо "
    "(Ты/он/она/они) в момент показа. ЕСЛИ ТЫ ЗАМЕНИШЬ ТОКЕН НА ГОТОВУЮ РУССКУЮ ФОРМУ ГЛАГОЛА "
    "(например 'начинает'), ГРАММАТИКА СЛОМАЕТСЯ для любого лица кроме одного — это реальный "
    "баг, уже найденный в игре ('Ты начинает' вместо 'Ты начинаешь'). Правильно: перевести "
    "фразу вокруг токена так, будто на его месте появится глагол нужного смысла в нужной "
    "форме, а сам токен (английское слово в квадратных скобках) оставить БУКВАЛЬНО как в "
    "оригинале, на грамматически естественном для русского места (обычно там же, где стоял "
    "бы спрягаемый глагол).\n"
    "  Пример: '[us] [starts] toggling power on [them].' -> "
    "'[us] [starts] переключать питание на [them].' (НЕ '[us] начинает переключать...').\n"
    "  Пример: '[us] [adds] connection to [them]' -> '[us] [adds] соединение к [them]' "
    "(НЕ '[us] добавляет соединение к [them]').\n"
    "- Плейсхолдеры {0}/%%s/%%d, теги <b>/<i>/<color=...>, \\n — сохраняй без изменений.\n"
    "- Заголовки (strTitle/strNameFriendly/strNameShort) переводи кратко, как в интерфейсах.\n"
    "- Описания (strDesc/strTooltip) — полными фразами, сохраняя тон.\n"
    "- Термины: 'ship' -> 'корабль', 'crew' -> 'экипаж', 'salvage/scavenge' -> 'разборка/лом', "
    "'captain' -> 'капитан', 'career' -> 'карьера', 'condition' -> 'состояние'.\n\n"
    "Ответ — СТРОГО JSON-объект {\"id\": \"перевод\", ...} с ТЕМИ ЖЕ id, что во входном массиве."
) % LANGNAME


def log(msg):
    line = "[%s] %s" % (time.strftime("%H:%M:%S"), msg)
    print(line, flush=True)
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


def load_current_category(category):
    """Копия logики import_old_translation.load_category без импорта (та завязана на OLD_DATA)."""
    base = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data"
    folder = os.path.join(base, category)
    result = {}
    if not os.path.isdir(folder):
        return result
    for root, _, files in os.walk(folder):
        for fn in sorted(files):
            if not fn.endswith(".json"):
                continue
            path = os.path.join(root, fn)
            try:
                data = json.loads(io.open(path, encoding="utf-8-sig").read(), strict=False)
            except Exception:
                continue
            if isinstance(data, list):
                for e in data:
                    if isinstance(e, dict) and e.get("strName"):
                        result[e["strName"]] = e
    return result


BRACKET_RE = re.compile(r"\[[a-zA-Z][a-zA-Z0-9_]*\]")


def translate_category(category):
    fname = category.replace("/", "_") + ".json"
    out_path = os.path.join(ROOT, "langs", LANG, "data", fname)

    cur = load_current_category(category)
    overlay = load_json(out_path) if os.path.exists(out_path) else {}

    # Переводим (или переводим ЗАНОВО) поле, если: (а) записи нет в оверлее вовсе,
    # либо (б) есть, но набор токенов в скобках ([us]/[them]/[глагол-ключ]) разошёлся
    # с текущим английским — значит перевод сделан раньше исправления промпта и
    # потерял токен спряжения (см. docs/baseline.md, найдено вживую: "Ты начинает"
    # вместо "Ты начинаешь"). Не трогаем поля без токенов и с уже совпадающим набором.
    todo = []
    missing_n = 0
    broken_n = 0
    for str_name, obj in cur.items():
        fields = {}
        existing = overlay.get(str_name, {})
        for f in TRANSLATABLE:
            en_val = obj.get(f)
            if not en_val or not isinstance(en_val, str):
                continue
            ru_val = existing.get(f)
            if ru_val is None:
                fields[f] = en_val
                missing_n += 1
            else:
                en_tok = set(BRACKET_RE.findall(en_val))
                ru_tok = set(BRACKET_RE.findall(ru_val))
                if en_tok != ru_tok:
                    fields[f] = en_val
                    broken_n += 1
        if fields:
            todo.append((str_name, fields))

    log("%s: %d записей (нет перевода: %d полей, сломанные токены: %d полей)" %
        (category, len(todo), missing_n, broken_n))
    if not todo:
        return 0

    # каждая (strName, field) пара — отдельный переводимый элемент, батчуется вместе
    items = []
    for str_name, fields in todo:
        for field, val in fields.items():
            items.append({"id": str_name + "::" + field, "en": val, "ctx": category + "/" + str_name})

    batches = [items[i:i + BATCH] for i in range(0, len(items), BATCH)]

    def run_batch(batch_items):
        try:
            res = chat_json(SYS_PROMPT, batch_items, model="qwen")
        except Exception as e:
            log("%s: batch ERROR: %s" % (category, e))
            return {}
        return res if isinstance(res, dict) else {}

    done = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=MAX_WORKERS) as ex:
        futures = {ex.submit(run_batch, b): b for b in batches}
        for fut in concurrent.futures.as_completed(futures):
            batch_items = futures[fut]
            res = fut.result()
            got = 0
            for it in batch_items:
                v = res.get(it["id"])
                if not v or not str(v).strip():
                    continue
                str_name, field = it["id"].rsplit("::", 1)
                overlay.setdefault(str_name, {})[field] = str(v)
                got += 1
            done += got
            log("%s: батч из %d -> +%d (всего готово %d/%d)" %
                (category, len(batch_items), got, done, len(items)))
            save_json(out_path, overlay)

    return done


def main():
    api = check_api(model="qwen")
    log("api: %s" % api)
    if not api.get("ok"):
        log("ERROR: Qwen-прокси недоступен")
        sys.exit(1)

    total = 0
    for cat in CATEGORIES:
        total += translate_category(cat)

    log("ГОТОВО: переведено %d полей суммарно" % total)


if __name__ == "__main__":
    main()
