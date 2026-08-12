#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
migrate_pack_layout.py — Task 5.2: сливает две раскладки языкового пакета
(`langs/lang_ru/{grammar,verbs,strings}.json`, читает `RuData.cs`, и
`langs/ru/{ui,data}/`, читает `ContentOverlay.cs`/`PrefabBinder.cs`) в одну
`langs/ru/`, порождая манифест `langs/ru/pack.json`.

Старая раскладка `langs/lang_ru/` НЕ удаляется и не изменяется — код всё ещё
читает её (RuData.Load). Это чисто миграция данных вперёд; Task 5.3/5.4
переключат C#-загрузчик на новую раскладку и manifест.

Что делает скрипт:
  1. Копирует langs/lang_ru/verbs.json   -> langs/ru/verbs.json   (как есть).
  2. Копирует langs/lang_ru/strings.json -> langs/ru/strings.json (как есть,
     если исходный файл существует — на момент написания существует).
  3. Читает langs/lang_ru/grammar.json и вместе с текущими захардкоженными
     CategoryToField/TranslatableFields из ContentOverlay.cs (см. константы
     ниже — держать в синхроне вручную до Task 5.4, когда чтение станет
     двусторонним) генерирует langs/ru/pack.json:
       {
         "code": "ru",
         "name": "Russian",
         "you": "<grammar.json['you']>",
         "cases": ["nom","gen","dat","acc","ins","prep"],
         "pronounCategories": <grammar.json['pronouns'], как есть>,
         "overlay": {
           "categoryToField": <копия ContentOverlay.CategoryToField>,
           "translatableFields": <копия ContentOverlay.TranslatableFields, список>
         }
       }
  4. Для каждого переноса печатает "источник -> назначение: N ключей" и
     ассертит, что число прочитанных ключей == числу записанных. Ненулевой
     exit при расхождении (это и есть "проверка" из плана).
  5. Идемпотентен: если файл назначения уже существует и побайтово совпадает
     с тем, что скрипт собирается записать — молча перезаписывает (no-op по
     содержимому). Если существует и ОТЛИЧАЕТСЯ — это конфликт имён с чем-то,
     что уже лежит под langs/ru/ и не было создано этим скриптом; скрипт
     останавливается и печатает предупреждение, ничего не перезаписывая,
     вместо того чтобы гадать, какая версия правильная (Global Constraint C4:
     существующие langs/ru/ui/*.json и langs/ru/data/*.json трогать нельзя).

Запуск: python tools/migrate_pack_layout.py
"""
import io
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LANG_RU_OLD = os.path.join(ROOT, "langs", "lang_ru")
LANG_RU_NEW = os.path.join(ROOT, "langs", "ru")

# Текущее содержимое CategoryToField / TranslatableFields из
# plugin/OstraI18n/ContentOverlay.cs (строки ~30-61 на момент написания).
# Task 5.4 переключит ContentOverlay.cs на чтение этих же значений ИЗ
# pack.json, тем самым сделав дублирование временным.
CATEGORY_TO_FIELD = {
    "interactions": "dictInteractions",
    "careers": "dictCareers",
    "conditions": "dictConds",
    "pda_apps": "dictPDAAppIcons",
    "installables": "dictInstallables",
    "cooverlays": "dictCOOverlays",
    "condowners": "dictCOs",
    "ledgerdefs": "dictLedgerDefs",
    "pledges": "dictPledges",
    "slots": "dictSlots",
    "headlines": "dictHeadlines",
    "plots": "dictPlots",
    "market/CoCollections": "dictSupersTemp",
    "ads": "dictAds",
    "rooms": "dictRoomSpecsTemp",
    "jobitems": "dictJobitems",
    "racing/tracks": "dictRaceTracks",
    "context": "dictContext",
    "racing/leagues": "dictRacingLeagues",
    "info": "dictInfoNodes",
    "market/Production": "dictProductionMaps",
    "tips": "dictTips",
}

TRANSLATABLE_FIELDS = [
    "strTitle", "strDesc", "strTooltip", "strNameFriendly", "strNameShort", "strFriendlyName",
    "strArticleBody", "strArticleTitle", "strNodeLabel", "strBody", "strDescription",
    "strRequirementDescription", "strFriendlyDescription", "description",
]

CASES = ["nom", "gen", "dat", "acc", "ins", "prep"]


def load_json(path):
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


def dump_json(obj):
    return json.dumps(obj, ensure_ascii=False, indent=2) + "\n"


def write_json_checked(dst_path, obj, label):
    """Write obj as JSON to dst_path unless a differing file is already
    there, in which case abort loudly (collision with pre-existing content
    under langs/ru/ that this script did not create)."""
    new_text = dump_json(obj)
    if os.path.exists(dst_path):
        with io.open(dst_path, encoding="utf-8") as f:
            old_text = f.read()
        if old_text != new_text:
            print("CONFLICT: %s already exists at %s with DIFFERENT content. "
                  "Refusing to overwrite — resolve manually." % (label, dst_path))
            sys.exit(1)
    with io.open(dst_path, "w", encoding="utf-8") as f:
        f.write(new_text)


def copy_straight(src_name):
    """Copy langs/lang_ru/<src_name> -> langs/ru/<src_name> verbatim (parsed
    and re-dumped with the project's standard formatting, not a byte copy,
    so the "straight copy" is semantically straight: same keys, same
    values). Returns (read_count, written_count) or None if source absent."""
    src_path = os.path.join(LANG_RU_OLD, src_name)
    if not os.path.exists(src_path):
        return None
    data = load_json(src_path)
    read_count = len(data)
    dst_path = os.path.join(LANG_RU_NEW, src_name)
    write_json_checked(dst_path, data, src_name)
    written = load_json(dst_path)
    written_count = len(written)
    print("%s -> %s: %d keys read, %d keys written" % (
        os.path.join("langs", "lang_ru", src_name),
        os.path.join("langs", "ru", src_name),
        read_count, written_count))
    assert read_count == written_count, (
        "%s: read %d keys but wrote %d keys" % (src_name, read_count, written_count))
    return read_count, written_count


def build_pack_json():
    grammar_path = os.path.join(LANG_RU_OLD, "grammar.json")
    grammar = load_json(grammar_path)
    pronouns_src = grammar["pronouns"]
    read_count = len(pronouns_src)

    pack = {
        "code": "ru",
        "name": "Russian",
        "you": grammar.get("you", "ты"),
        "cases": CASES,
        "pronounCategories": pronouns_src,
        "overlay": {
            "categoryToField": CATEGORY_TO_FIELD,
            "translatableFields": TRANSLATABLE_FIELDS,
        },
    }

    dst_path = os.path.join(LANG_RU_NEW, "pack.json")
    write_json_checked(dst_path, pack, "pack.json")

    written = load_json(dst_path)
    written_count = len(written["pronounCategories"])
    print("%s (pronouns) -> %s (pronounCategories): %d keys read, %d keys written" % (
        os.path.join("langs", "lang_ru", "grammar.json"),
        os.path.join("langs", "ru", "pack.json"),
        read_count, written_count))
    assert read_count == written_count, (
        "grammar.json pronouns: read %d keys but wrote %d keys" % (read_count, written_count))

    for key in ("code", "name", "you", "cases", "pronounCategories", "overlay"):
        assert key in written, "pack.json missing required key: %s" % key

    overlay_read = len(CATEGORY_TO_FIELD)
    overlay_written = len(written["overlay"]["categoryToField"])
    print("ContentOverlay.CategoryToField -> pack.json overlay.categoryToField: "
          "%d keys read, %d keys written" % (overlay_read, overlay_written))
    assert overlay_read == overlay_written

    tf_read = len(TRANSLATABLE_FIELDS)
    tf_written = len(written["overlay"]["translatableFields"])
    print("ContentOverlay.TranslatableFields -> pack.json overlay.translatableFields: "
          "%d keys read, %d keys written" % (tf_read, tf_written))
    assert tf_read == tf_written

    return read_count, written_count


def main():
    if not os.path.isdir(LANG_RU_OLD):
        print("ERROR: %s not found" % LANG_RU_OLD)
        sys.exit(1)
    os.makedirs(LANG_RU_NEW, exist_ok=True)

    print("=== migrate_pack_layout: langs/lang_ru/ -> langs/ru/ ===")

    verbs_result = copy_straight("verbs.json")
    if verbs_result is None:
        print("ERROR: langs/lang_ru/verbs.json not found — required by the plan.")
        sys.exit(1)

    strings_result = copy_straight("strings.json")
    if strings_result is None:
        print("langs/lang_ru/strings.json not found — skipping (GUI strings may live elsewhere).")

    build_pack_json()

    print("=== done, all counts matched ===")


if __name__ == "__main__":
    main()
