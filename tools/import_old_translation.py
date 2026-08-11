#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
import_old_translation.py — импортирует старый перевод (Ostranauts/old/.../RUS_CoreForgeLabs/data/)
в оверлей langs/ru/data/, сопоставляя ТОЛЬКО по strName внутри категории, с валидацией
каждой строки. Ничего не применяется молча — три корзины: принято/устарело/подозрительно.

Запуск: python import_old_translation.py <категория> [<категория> ...]
Пример: python import_old_translation.py interactions
"""
import io
import json
import os
import re
import sys

ROOT = r"F:\DEV2\ostra_i18n"
GAME = r"F:\Games\Steam\steamapps\common\Ostranauts"
OLD_DATA = os.path.join(GAME, "old", "Ostranauts_Data", "Mods", "RUS_CoreForgeLabs", "data")
CUR_DATA = os.path.join(GAME, "Ostranauts_Data", "StreamingAssets", "data")

TRANSLATABLE = ("strTitle", "strDesc", "strTooltip", "strNameFriendly", "strNameShort", "strFriendlyName")


# ЛЮБОЕ слово в квадратных скобках — грамматический токен (не только [us]/[them]/
# [verb]/[cap]): игра использует [<английский_глагол>] как ключ поиска в таблице
# спряжений (RuData.Verbs), например "[starts]", "[adds]", "[removes]" — движок
# на лету заменяет его правильной формой под фактическое лицо в рантайме
# (см. Patches.cs VerbPrefix). Перевод НЕ должен заменять токен готовой русской
# формой — тогда согласование ломается для любого лица, кроме того, что
# случайно совпало с формой на момент перевода (найдено вживую: "Ты начинает"
# вместо "Ты начинаешь" — токен [starts] был схлопнут в статичное "начинает").
TOKEN_RE = re.compile(r"\[[a-zA-Z][a-zA-Z0-9_]*\]")
PLACEHOLDER_RE = re.compile(r"\{\d+\}")
TAG_RE = re.compile(r"</?[a-zA-Z][a-zA-Z0-9]*>")


def load_category(base_dir, category):
    """Собирает все *.json из папки категории в единый dict strName -> object,
    так же, как это делает игра через DataHandler.LoadModJsons (последний файл
    в алфавитном порядке обхода Directory.GetFiles побеждает при коллизии —
    здесь неважно, т.к. коллизии внутри категории уже есть в самой игре)."""
    folder = os.path.join(base_dir, category)
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
            except Exception as e:
                print("  ПРОПУСК (bad JSON) %s: %s" % (path, e))
                continue
            if isinstance(data, list):
                for e in data:
                    if isinstance(e, dict) and e.get("strName"):
                        result[e["strName"]] = e
            elif isinstance(data, dict):
                for k, v in data.items():
                    if isinstance(v, dict):
                        result[k] = v
    return result


def validate(old_val, cur_val, field):
    """Возвращает None если ок, иначе строку с причиной для корзины 'подозрительно'."""
    old_tokens = sorted(TOKEN_RE.findall(old_val))
    cur_tokens = sorted(TOKEN_RE.findall(cur_val))
    if old_tokens != cur_tokens:
        return "токены разошлись: en=%s ru=%s" % (cur_tokens, old_tokens)
    old_ph = sorted(PLACEHOLDER_RE.findall(old_val))
    cur_ph = sorted(PLACEHOLDER_RE.findall(cur_val))
    if old_ph != cur_ph:
        return "плейсхолдеры разошлись: en=%s ru=%s" % (cur_ph, old_ph)
    old_tags = sorted(TAG_RE.findall(old_val))
    cur_tags = sorted(TAG_RE.findall(cur_val))
    if old_tags != cur_tags:
        return "разметка разошлась: en=%s ru=%s" % (cur_tags, old_tags)
    if not old_val.strip():
        return "пустой перевод"
    return None


def import_category(category):
    print("=== %s ===" % category)
    old = load_category(OLD_DATA, category)
    cur = load_category(CUR_DATA, category)
    print("старый перевод: %d записей, текущие данные игры: %d записей" % (len(old), len(cur)))

    accepted = {}
    stale = []
    suspicious = []

    for str_name, old_obj in old.items():
        if str_name not in cur:
            stale.append(str_name)
            continue
        cur_obj = cur[str_name]
        entry = {}
        for field in TRANSLATABLE:
            old_val = old_obj.get(field)
            cur_val = cur_obj.get(field)
            if old_val is None or cur_val is None:
                continue
            if not isinstance(old_val, str) or not isinstance(cur_val, str):
                continue
            reason = validate(old_val, cur_val, field)
            if reason:
                suspicious.append({"strName": str_name, "field": field, "en": cur_val, "ru": old_val, "reason": reason})
                continue
            entry[field] = old_val
        if entry:
            accepted[str_name] = entry

    out_path = os.path.join(ROOT, "langs", "ru", "data", category.replace("/", "_") + ".json")
    existing = {}
    if os.path.exists(out_path):
        existing = json.loads(io.open(out_path, encoding="utf-8").read())
    existing.update(accepted)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    io.open(out_path, "w", encoding="utf-8").write(json.dumps(existing, ensure_ascii=False, indent=2))

    print("принято: %d, устарело (нет в текущей игре): %d, подозрительно (в карантин): %d" %
          (len(accepted), len(stale), len(suspicious)))
    return {"category": category, "accepted": len(accepted), "stale": stale, "suspicious": suspicious}


def main():
    categories = sys.argv[1:] or ["interactions"]
    reports = [import_category(c) for c in categories]
    report_path = os.path.join(ROOT, "lang_src", "old_import_report.json")
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    io.open(report_path, "w", encoding="utf-8").write(json.dumps(reports, ensure_ascii=False, indent=2))
    print("отчёт: %s" % report_path)


if __name__ == "__main__":
    main()
