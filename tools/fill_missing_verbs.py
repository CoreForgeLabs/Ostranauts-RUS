#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fill_missing_verbs.py — собирает все токены-глаголы [word], реально встречающиеся
в текущих данных игры по всем 19 категориям контент-оверлея, сравнивает со
существующей таблицей спряжений langs/lang_ru/verbs.json и генерирует недостающие
через Qwen (структурированный вывод: 6 форм — я/ты/он/она/они/оно, схема
{"present": [...]6]}, та же, что уже использует RuData.Verbs/Patches.VerbPrefix).

Запуск: python fill_missing_verbs.py
"""
import io
import json
import os
import re
import sys

sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_json, check_api  # noqa: E402

ROOT = r"F:\DEV2\ostra_i18n"
CUR_DATA = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data"
VERBS_PATH = os.path.join(ROOT, "langs", "lang_ru", "verbs.json")
LOG = os.path.join(ROOT, "lang_src", "fill_missing_verbs.log")

CATEGORIES = ["interactions", "careers", "conditions", "pda_apps", "installables", "cooverlays",
              "condowners", "ledgerdefs", "pledges", "slots", "headlines", "plots",
              "market/CoCollections", "ads", "rooms", "jobitems", "racing/tracks", "context", "racing/leagues"]

TRANSLATABLE = ("strTitle", "strDesc", "strTooltip", "strNameFriendly", "strNameShort", "strFriendlyName")
BRACKET_RE = re.compile(r"\[([a-zA-Z][a-zA-Z0-9_]*)\]")
NOT_VERBS = {"us", "them", "cap"}
BATCH = 20

SYS_PROMPT = (
    "Ты специалист по русской грамматике, строишь таблицу спряжений глаголов для игры "
    "Ostranauts (hard sci-fi симулятор). Каждый элемент — английское слово-ключ (обычно "
    "форма 3-го лица настоящего времени, 'starts'/'adds'/'bashes' и т.п.), взятое из "
    "шаблона вида '[us] [KEY] verb-phrase [them]'. Твоя задача — дать РУССКИЙ перевод "
    "смысла этого действия в НАСТОЯЩЕМ ВРЕМЕНИ, в 6 формах по лицам, в порядке "
    "[я, ты, он, она, они, оно] — ИМЕННО в этом порядке, как в примере ниже.\n\n"
    "Пример для ключа 'opens' (значение — 'открывать'):\n"
    '{"opens": ["открываю", "открываешь", "открывает", "открывает", "открывают", "открывает"]}\n\n'
    "Правила:\n"
    "- Каждый элемент входного массива содержит \"id\" (английский ключ) и \"ctx\" — "
    "пример полной английской фразы, где этот ключ встретился (чтобы понять точный смысл "
    "глагола в контексте — 'bashes' в контексте боевого действия значит 'бьёт/колотит', "
    "не бытовое 'ругает').\n"
    "- 6 форм строго в порядке я/ты/он/она/они/оно — это порядковые формы для подстановки "
    "по индексу в рантайме, порядок менять нельзя.\n"
    "- Для местоимений 3-го лица (он/она/они/оно), если форма глагола не различается — "
    "повторяй одинаковую форму (как в примере: 'открывает' для он/она/оно).\n\n"
    "Ответ — СТРОГО JSON-объект {\"английский_ключ\": [6 форм], ...} с ТЕМИ ЖЕ ключами, "
    "что во входном массиве (поле id)."
)


def log(msg):
    print(msg, flush=True)
    with io.open(LOG, "a", encoding="utf-8") as f:
        f.write(msg + "\n")


def load_category_current(category):
    folder = os.path.join(CUR_DATA, category)
    result = []
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
                result.extend(data)
    return result


def main():
    api = check_api(model="qwen")
    log("api: %s" % api)
    if not api.get("ok"):
        log("ERROR: Qwen-прокси недоступен")
        sys.exit(1)

    verbs = json.load(open(VERBS_PATH, encoding="utf-8"))

    # ключ -> один пример полной фразы (для контекста)
    found = {}
    for cat in CATEGORIES:
        for obj in load_category_current(cat):
            for field in TRANSLATABLE:
                v = obj.get(field)
                if not isinstance(v, str):
                    continue
                for tok in BRACKET_RE.findall(v):
                    if tok in NOT_VERBS or tok in verbs or tok in found:
                        continue
                    found[tok] = v

    log("уникальных отсутствующих токенов-глаголов: %d" % len(found))
    if not found:
        log("нечего добавлять")
        return

    items = [{"id": k, "ctx": ctx} for k, ctx in found.items()]
    batches = [items[i:i + BATCH] for i in range(0, len(items), BATCH)]

    added = 0
    for i, batch in enumerate(batches):
        try:
            res = chat_json(SYS_PROMPT, batch, model="qwen")
        except Exception as e:
            log("batch %d ERROR: %s" % (i, e))
            continue
        if not isinstance(res, dict):
            log("batch %d: не dict, пропуск" % i)
            continue
        for k, v in res.items():
            if not isinstance(v, list) or len(v) != 6:
                log("  пропуск '%s' — неверный формат: %r" % (k, v))
                continue
            verbs[k] = {"present": [str(x) for x in v]}
            added += 1
        log("batch %d: +%d (всего добавлено %d/%d)" % (i, len(res), added, len(items)))
        tmp = VERBS_PATH + ".tmp"
        with io.open(tmp, "w", encoding="utf-8") as f:
            json.dump(verbs, f, ensure_ascii=False, indent=2)
        os.replace(tmp, VERBS_PATH)

    log("ГОТОВО: глаголов в таблице теперь %d (было %d)" % (len(verbs), len(verbs) - added))


if __name__ == "__main__":
    main()
