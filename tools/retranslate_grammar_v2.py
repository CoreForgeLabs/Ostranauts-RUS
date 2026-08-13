#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
retranslate_grammar_v2.py — Task 6.7 payoff retranslation pass.

Retranslates the entries selected by scan_grammar_v2_targets.py (written to
lang_src/grammar_v2_selected.json) through Qwen with a NEW system prompt that
knows about the vocabulary Tasks 6.1-6.6 added: the [X-gen] case token (the
ONLY case wired into DataHandler.categories as of Task 6.4 -- dat/acc/ins/prep
are NOT registered and would silently fail at runtime, so this prompt does not
offer them), the disambiguated verb keys is.cop/is.aux/has.obj/has.qual (Task
6.5), and the [<alias>-custom-characterGenderCond-<m>-<f>-<nb>] token (decompiled
GrammarUtils.Custom, confirmed already used live in langs/ru/data/interactions.json).

Unlike translate_data.py (which only fills MISSING fields), this tool
deliberately REPLACES existing overlay values for the selected (file, name,
field) triples -- that is the point of a retranslation pass.

Run: python retranslate_grammar_v2.py
"""
import io
import json
import os
import re
import sys
import time

sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_json, check_api  # noqa: E402
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from import_old_translation import (  # noqa: E402
    load_category, load_simple_category, is_simple, output_category_for, SIMPLE_SCHEMAS, CUR_DATA,
)

# Reverse of output_category_for: real merged category -> list of simple-schema source
# categories that get folded into it (see validate_content_overlay.py's MERGE_SOURCES for
# the same pattern -- e.g. "conditions_simple" entries live in the "conditions.json" overlay
# file but are NOT part of load_category(CUR_DATA, "conditions") on their own).
MERGE_SOURCES = {}
for _simple_cat in SIMPLE_SCHEMAS:
    MERGE_SOURCES.setdefault(output_category_for(_simple_cat), []).append(_simple_cat)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RU_DATA = os.path.join(ROOT, "langs", "ru", "data")
SELECTED_PATH = os.path.join(ROOT, "lang_src", "grammar_v2_selected.json")
LOG_PATH = os.path.join(ROOT, "lang_src", "retranslate_grammar_v2.log")

FILENAME_TO_CATEGORY = {
    "market_CoCollections": "market/CoCollections",
    "market_Production": "market/Production",
    "racing_tracks": "racing/tracks",
    "racing_leagues": "racing/leagues",
}

BATCH = 12

SYS_PROMPT = (
    "Ты редактор русской локализации игры Ostranauts (суровый hard sci-fi симулятор "
    "разборки списанных космических кораблей и выживания у пояса астероидов, тон "
    "циничный, потрёпанный, без пафоса). Тебе даны фразы с ДВУМЯ конкретными классами "
    "грамматических ошибок текущего перевода — переформулируй КАЖДУЮ фразу так, чтобы "
    "ошибка исчезла, используя новые инструменты, описанные ниже. Смысл и тон должны "
    "сохраниться, структура фразы может меняться свободно.\n\n"
    "=== Класс A: [us-pos] перед словом не мужского рода/числа ===\n"
    "Токен [us-pos] движок ВСЕГДА подставляет как 'твой' (именительный, мужской род, "
    "единственное число) — он не умеет согласовываться с родом следующего существительного. "
    "Если после [us-pos] по смыслу фразы должно стоять слово среднего/женского рода или "
    "множественного числа, результат ломается: '[us-pos] текущее действие' -> "
    "'твой текущее действие' (правильно — 'твоё текущее действие'; 'действие' — средний род).\n"
    "Способы починки (выбирай тот, что даёт наиболее естественную русскую фразу):\n"
    "  1) Убрать [us-pos] вовсе, если притяжательность и так ясна из контекста (часто "
    "достаточно, когда речь идёт о собственном действии/состоянии субъекта [us]): "
    "'[us] [cancels] [us-pos] текущее действие.' -> '[us] [cancels] текущее действие.'\n"
    "  2) Заменить на родительный оборот с новым падежным токеном [us-gen] (ЕДИНСТВЕННЫЙ "
    "живой падежный токен — движок резолвит [us-gen]/[them-gen]/[3rd-gen] в 'тебя'/'его'/"
    "'её'/'их'; НЕ используй [us-dat]/[us-acc]/[us-ins]/[us-prep] — эти категории НЕ "
    "зарегистрированы в движке и молча сломаются в рантайме): "
    "'[us-pos] способность защищаться в бою.' -> 'Способность [us-gen] защищаться в бою.' "
    "или 'У [us-gen] есть способность защищаться в бою.'\n"
    "  3) Если фраза описывает САМОГО персонажа (не абстрактный объект), можно согласовать "
    "род через custom-токен характер-пола: '[<alias>-custom-characterGenderCond-<форма-м>-"
    "<форма-ж>-<форма-нейтр>]' — движок на лету выберет нужный вариант по полу персонажа "
    "(<alias> — тот же алиас, что в исходном токене, например 'us'). Используй ТОЛЬКО когда "
    "нужно согласовать прилагательное/существительное с ПОЛОМ САМОГО ПЕРСОНАЖА (а не с родом "
    "постороннего существительного) — например 'усталый/усталая/усталый' описание состояния "
    "персонажа.\n"
    "  4) Просто переставить слова / изменить конструкцию так, чтобы [us-pos] оказался перед "
    "существительным мужского рода единственного числа, или заменить его на прилагательное "
    "без родовой формы 'свой' в контексте, где это грамматично.\n"
    "ВАЖНО: [them-pos]/[3rd-pos] НЕ ломаются (движок даёт 'его'/'её'/'их' — эти формы в "
    "русском НЕ склоняются по роду следующего слова, они уже корректны) — не трогай их, если "
    "явно не попросили.\n\n"
    "=== Класс B: [has] в значении 'обладает качеством/чертой', а не 'владеет предметом' ===\n"
    "'[us] [has] bad piloting skills.' с текущим переводом '[us] [has] плохие навыки "
    "пилотирования.' рендерится как 'Ты имеешь плохие навыки пилотирования' — по-русски так "
    "не говорят про качества/черты (это калька с английского 'have'). Правильная русская "
    "идиома для 'обладает качеством' — 'У <родительный> <качество>', БЕЗ спрягаемого глагола. "
    "Используй новый токен [has.qual] (НЕ [has], НЕ [has.obj]) — движок трактует его как "
    "'немой' токен (ничего не выводит, просто маршрутизируется в таблицу глаголов), а перед "
    "ним переводчик САМ пишет 'У [us-gen]' (или '[them-gen]'/'[3rd-gen]' для 3-го лица):\n"
    "  ПРИМЕР: EN '[us] [has.qual] poor piloting skills.' -> "
    "RU 'У [us-gen] [has.qual] плохие навыки пилотирования.' -> в игре: "
    "'У тебя плохие навыки пилотирования.'\n"
    "  То есть: замени '[has]' на '[has.qual]' В ТОМ ЖЕ МЕСТЕ, ГДЕ ОН СТОЯЛ В EN-ОРИГИНАЛЕ, "
    "и ДОБАВЬ 'У [us-gen]' (или '[them-gen]'/'[3rd-gen]', смотря чей это токен субъекта в "
    "исходной фразе) в начало фразы или перед качеством.\n"
    "Если [has] в присланной фразе означает 'владеет физическим предметом' (не качество) — "
    "НЕ трогай его вообще (такие фразы тебе не должны присылаться, но если сомневаешься — "
    "оставь как есть).\n\n"
    "=== Общие правила (не нарушать) ===\n"
    "- [us] и [them] — плейсхолдеры действующих лиц, сохраняй буквально на своём месте (или "
    "переставляй вместе со смыслом фразы, но не удаляй и не переводи).\n"
    "- ЛЮБОЕ ДРУГОЕ слово в квадратных скобках, кроме [us]/[them]/[us-pos]/[them-pos]/"
    "[3rd-pos]/[us-gen]/[them-gen]/[3rd-gen]/[has.qual]/custom-токенов — это токен спряжения "
    "глагола (например [starts], [cancels], [asks]) — КЛЮЧ ПОИСКА в таблице спряжений, "
    "оставляй его буквально в квадратных скобках на грамматически естественном месте (там, "
    "где стоял бы спрягаемый глагол), НЕ заменяй готовой русской формой.\n"
    "- Плейсхолдеры {0}, теги <b>/<i>/<color=...>, \\n — сохраняй без изменений.\n"
    "- Не выдумывай новых имён/персонажей, не меняй смысл фразы.\n"
    "- Каждая входная запись содержит \"en\" (текущий английский оригинал — для понимания "
    "структуры токенов) и \"ru\" (текущий русский перевод с ошибкой, который нужно "
    "исправить).\n\n"
    "Ответ — СТРОГО JSON-объект {\"id\": \"исправленный перевод\", ...} с ТЕМИ ЖЕ id, что "
    "во входном массиве."
)

TOKEN_RE = re.compile(r"\[[^\[\]]+\]")


def log(msg):
    line = "[%s] %s" % (time.strftime("%H:%M:%S"), msg)
    print(line, flush=True)
    with io.open(LOG_PATH, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def load_json(path):
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


def save_json(path, data):
    tmp = path + ".tmp"
    with io.open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    os.replace(tmp, path)


def main():
    api = check_api(model="qwen")
    log("api: %s" % api)
    if not api.get("ok"):
        log("ERROR: Qwen-прокси недоступен")
        sys.exit(1)

    targets = load_json(SELECTED_PATH)
    log("targets: %d" % len(targets))

    # Group by overlay file so we load/save each overlay once.
    by_file = {}
    for t in targets:
        by_file.setdefault(t["file"], []).append(t)

    # Preload current EN game data per real category + RU overlay per file.
    cat_cache = {}

    def get_en_category(real_category):
        if real_category not in cat_cache:
            cur = (
                load_simple_category(CUR_DATA, real_category)
                if is_simple(real_category) else load_category(CUR_DATA, real_category)
            )
            for simple_cat in MERGE_SOURCES.get(real_category, []):
                cur.update(load_simple_category(CUR_DATA, simple_cat))
            cat_cache[real_category] = cur
        return cat_cache[real_category]

    items = []
    by_id = {}
    idx = 0
    overlays = {}
    for fn, ts in by_file.items():
        real_cat = FILENAME_TO_CATEGORY.get(fn[:-5], fn[:-5])
        en_cat = get_en_category(real_cat)
        overlay_path = os.path.join(RU_DATA, fn)
        overlay = load_json(overlay_path)
        overlays[fn] = overlay
        for t in ts:
            name, field = t["name"], t["field"]
            en_val = en_cat.get(name, {}).get(field)
            ru_val = overlay.get(name, {}).get(field)
            if en_val is None or ru_val is None:
                log("SKIP (missing en/ru) %s / %s.%s" % (fn, name, field))
                continue
            iid = "i%d" % idx
            idx += 1
            items.append({"id": iid, "en": en_val, "ru": ru_val})
            by_id[iid] = (fn, name, field, en_val, ru_val)

    log("resolved items: %d" % len(items))

    # Incremental save (Task 6.7 hardening, after a mid-run process death lost an entire
    # 105-item run's results because nothing was written until the very end): write the
    # touched overlay file(s) and append to the changes log after EVERY batch, not once at
    # the end -- same resilience pattern translate_data.py already uses for exactly this
    # reason (Qwen proxy under load / connection resets, see log's repeated "Connection
    # error, retry" lines from the run this replaces).
    changes_out = os.path.join(ROOT, "lang_src", "grammar_v2_changes.json")
    changes_log = []
    changed = 0
    unchanged = 0
    no_answer = 0

    def apply_batch_results(res):
        nonlocal changed, unchanged, no_answer
        if not isinstance(res, dict):
            return
        touched_files = set()
        for iid, new_ru in res.items():
            if iid not in by_id:
                continue
            fn, name, field, en_val, old_ru = by_id[iid]
            if not new_ru or not isinstance(new_ru, str) or not new_ru.strip():
                no_answer += 1
                log("NO ANSWER: %s / %s.%s" % (fn, name, field))
                continue
            new_ru = new_ru.strip()
            if new_ru == old_ru:
                unchanged += 1
                continue
            overlays[fn].setdefault(name, {})[field] = new_ru
            changed += 1
            changes_log.append({"file": fn, "name": name, "field": field, "en": en_val,
                                 "before": old_ru, "after": new_ru})
            touched_files.add(fn)
        for fn in touched_files:
            save_json(os.path.join(RU_DATA, fn), overlays[fn])
        with io.open(changes_out, "w", encoding="utf-8") as f:
            json.dump(changes_log, f, ensure_ascii=False, indent=2)

    answered_ids = set()
    for i in range(0, len(items), BATCH):
        batch = items[i:i + BATCH]
        try:
            res = chat_json(SYS_PROMPT, batch, model="qwen")
        except Exception as e:
            log("batch ERROR at %d: %s" % (i, e))
            continue
        apply_batch_results(res)
        if isinstance(res, dict):
            answered_ids.update(res.keys())
        log("batch %d-%d done (%d/%d answered so far, %d changed, %d unchanged, %d no-answer)" %
            (i, i + len(batch), len(answered_ids), len(items), changed, unchanged, no_answer))

    missing = [iid for iid in by_id if iid not in answered_ids]
    for iid in missing:
        fn, name, field, en_val, old_ru = by_id[iid]
        log("NO ANSWER (never in any batch response): %s / %s.%s" % (fn, name, field))

    log("ГОТОВО: изменено %d, без изменений %d, без ответа %d (changes -> %s)" %
        (changed, unchanged, no_answer + len(missing), changes_out))


if __name__ == "__main__":
    main()
