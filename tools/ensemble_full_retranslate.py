#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ensemble_full_retranslate.py — a+b+c ensemble quality pass over the ENTIRE
translated corpus in langs/ru/data/*.json (~27000 fields), per user
instruction 2026-08-13: "весь текст, но с параллелизмом" (whole corpus, but
parallel — the narrow 62-candidate pass in ensemble_retranslate.py only
covered pattern-matched grammar bugs, not general awkward/calque phrasing
like "сканирует стеллаж").

Pipeline, BATCHED (unlike ensemble_retranslate.py's 1-item-3-calls):
  1. Collect every (file, name, field) with a non-empty EN source string
     across ALL categories (same category list as tools/translate_data.py).
  2. Group into batches of BATCH items. For each batch, in one worker thread:
       a) call Qwen (temperature 0.4) -> candidate A per id
       b) call Qwen (temperature 0.8) -> candidate B per id (independent
          sampling, not a rerun of the same prompt-response)
       c) call Qwen arbiter (temperature 0.2) with en + current-RU + A + B
          per id -> final text per id
  3. Apply results directly into langs/ru/data/<file>.json after EVERY
     batch (thread-safe per-file lock), so a crash loses at most one
     in-flight batch, not the whole run (lesson from Task 6.7).
  4. Resume support: completed ids are appended to ensemble_full_done.txt
     (one id per line, flushed each write) — on restart, already-done ids
     are skipped without re-reading the (potentially huge) full results.

Run: python tools/ensemble_full_retranslate.py
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
LOG = os.path.join(LANG_SRC, "ensemble_full.log")
DONE_FILE = os.path.join(LANG_SRC, "ensemble_full_done.txt")

CUR_DATA_ROOT = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data"

TRANSLATABLE = ("strTitle", "strDesc", "strTooltip", "strNameFriendly", "strNameShort", "strFriendlyName",
                 "strArticleBody", "strArticleTitle", "strNodeLabel", "strBody", "strDescription",
                 "strRequirementDescription", "strFriendlyDescription", "description", "strTutorialKey")

CATEGORIES = ["interactions", "careers", "conditions", "pda_apps", "installables", "cooverlays",
              "condowners", "ledgerdefs", "pledges", "slots", "headlines", "plots",
              "market/CoCollections", "ads", "rooms", "jobitems", "racing/tracks", "context",
              "racing/leagues", "conditions_simple", "info", "market/Production", "tips"]

_log_lock = threading.Lock()


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
орбитальных станциях у пояса астероидов, в духе Cowboy Bebop / The Expanse;
тон циничный, потрёпанный, без пафоса).

Тебе дают массив строк ИГРОВОГО текста для ПЕРЕПРОВЕРКИ И УЛУЧШЕНИЯ. У каждой
есть "en" (английский оригинал), "cur" (текущий русский перевод — может быть
корявым, калькированным или содержать грамматические ошибки) и "ctx"
(категория/ID записи, только для понимания контекста, в перевод не включать).

ТВОЯ ЗАДАЧА: написать финальный русский текст так, как написал бы
профессиональный русский игровой локализатор — естественно, без калек,
без дословного построения по английскому порядку слов. Пример проблемы,
которую нужно избегать: буквальное "сканирует стеллаж" вместо естественного
"сканирует содержимое стеллажа" / "проверяет, что лежит на стеллаже" —
подбирай формулировку, которая звучит как обычная русская фраза, а не
перевод слово-в-слово. Если "cur" уже хорош — можешь оставить почти как
есть; если корявый/калька/ошибка — переписывай смело.

СТРОГИЕ ПРАВИЛА (нарушение ломает игру, не только стиль):
- [us] и [them] — плейсхолдеры действующих лиц (заменяются на "Ты"/имя/
  местоимение в рантайме). Сохраняй БЕЗ ИЗМЕНЕНИЙ на грамматически
  естественном месте.
- ЛЮБОЕ ДРУГОЕ односложное слово в квадратных скобках без суффикса —
  например [starts], [adds], [removes], [bashes], [feels], [wants] — это
  КЛЮЧ ПОИСКА в таблице спряжений глагола, а НЕ текст для перевода. Игра
  сама подставит правильную русскую форму под фактическое лицо (Ты/он/она/
  они) в момент показа. НИКОГДА не заменяй такой токен готовой русской
  формой глагола (это ломает согласование лица для всех случаев кроме
  одного) — переведи фразу вокруг токена так, будто на его месте появится
  глагол в нужном смысле и форме, а сам токен оставь буквально в квадратных
  скобках на естественном для русского месте (обычно там, где стоял бы
  спрягаемый глагол).
    Пример: "[us] [starts] toggling power on [them]." ->
    "[us] [starts] переключать питание на [them]." (НЕ "[us] начинает...").
- Псевдоглаголы-связки: [is.cop] — невидимая связка (рендерится пусто,
  как русское настоящее время "быть"), используй когда по-русски связка
  вообще не нужна ("Ты [is.cop] голоден" -> "Ты голоден"). [is.aux] —
  вспомогательный глагол "быть/находиться" там, где он звучит. [has.obj] —
  обладание ПРЕДМЕТОМ ("У [X-gen] [has.obj] предмет" -> "У тебя есть
  предмет"). [has.qual] — обладание КАЧЕСТВОМ/НАВЫКОМ/НЕДУГОМ, не
  предметом ("У [X-gen] [has.qual] навык" -> "У тебя навык", без лишнего
  "имеет"). НЕ используй [has.qual] для физических предметов и наоборот.
- Токены вида [us-pos]/[my-pos]/[them-pos] дают ТОЛЬКО именительный падеж
  муж. рода ед. числа ("твой"/"мой") — они НИКОГДА не согласуются с родом/
  числом следующего существительного и не склоняются по падежам напрямую.
  Если после токена стоит существительное не муж. рода/не ед. числа —
  либо подставь мысленно ближайший синоним муж. рода ед. числа, либо
  перепиши через возвратное "свой/своя/своё/свои" (обычное русское слово,
  БЕЗ квадратных скобок, само согласуется с любым родом/числом) —
  это обычно самый естественный вариант. [them-pos] на "его/её/их" —
  падежно неизменяемое местоимение, с ним таких проблем нет.
- Из падежных суффиксов доступен ТОЛЬКО "-gen" (например [us-gen],
  [them-gen], [3rd-gen]) — родительный падеж, обычно после предлогов
  (у, для, из, от, до, без, около) или для родительного посессива
  ("график [them-gen]" = "график его/её/их", "У [us-gen]" = "У тебя").
  НЕ придумывай -dat/-acc/-ins/-prep суффиксы — их не существует в
  движке, такой токен молча не сработает.
- Плейсхолдеры {0}/%s/%d, HTML-теги <b>/<i>/<color=...>, \\n — сохраняй
  буквально, не трогай и не переставляй местами относительно текста.
- Заголовки (strTitle/strNameFriendly/strNameShort/strTutorialKey)
  переводи кратко, как в игровых интерфейсах, не полными предложениями.
- Описания (strDesc/strTooltip/strBody/description и т.п.) — полными
  естественными фразами, сохраняя циничный потрёпанный тон.
- Термины: ship -> корабль, crew -> экипаж, salvage/scavenge ->
  разборка/лом, captain -> капитан, career -> карьера, condition ->
  состояние, station -> станция, hull -> корпус.

Ответ — СТРОГО JSON-объект {"id": "финальный текст", ...} с ТЕМИ ЖЕ id, что
во входном массиве. Ничего кроме JSON."""

ARBITER_SYS_PROMPT = """Ты главный редактор русской локализации игры Ostranauts, принимающий
финальное решение по правкам. Тебе дают массив объектов, у каждого: "en"
(английский оригинал), "cur" (прежний русский перевод), "a" и "b" (два
НЕЗАВИСИМЫХ варианта улучшенного перевода от другого редактора).

Для каждого id выбери ЛУЧШИЙ финальный вариант — из a, из b, из cur (если
оба кандидата хуже прежнего перевода), или синтезируй свой, взяв сильные
стороны каждого. Критерии, в порядке важности:
1. Все игровые токены (что угодно в квадратных скобках, псевдоглаголы
   is.cop/is.aux/has.obj/has.qual, -gen суффиксы) сохранены и не заменены
   на голый русский текст, набор токенов не потерян и не искажён.
2. Грамматически корректно (согласование рода/числа/падежа/лица там, где
   оно не зависит от токена).
3. Звучит как естественная русская фраза профессионального игрового
   локализатора — БЕЗ калек и дословного построения по английскому
   порядку слов (например "сканирует стеллаж" вместо "сканирует
   содержимое стеллажа" — это калька, плохо).
4. Сохранён циничный потрёпанный тон игры, плейсхолдеры {0}/\\n/теги не
   тронуты.

Если и a, и b нарушают правило 1 (токены) — верни cur как есть (безопаснее
не трогать, чем сломать движок).

Ответ — СТРОГО JSON-объект {"id": "финальный текст", ...} с ТЕМИ ЖЕ id, что
во входном массиве. Ничего кроме JSON."""


def collect_items():
    """(file_name, str_name, field) -> {en, cur} for every non-empty EN
    translatable field across the whole corpus, mapped onto the current
    langs/ru/data overlay (cur may be "" if never translated)."""
    overlays = {}
    for fn in os.listdir(RU_DATA):
        overlays[fn] = load_json(os.path.join(RU_DATA, fn))

    items = []
    seen = set()
    for cat in CATEGORIES:
        cur_en = load_simple_category(CUR_DATA_ROOT, cat) if is_simple(cat) else load_category(CUR_DATA_ROOT, cat)
        out_cat = output_category_for(cat)
        fname = out_cat.replace("/", "_") + ".json"
        overlay = overlays.get(fname, {})
        for str_name, obj in cur_en.items():
            for f in TRANSLATABLE:
                en_val = obj.get(f)
                if not en_val or not isinstance(en_val, str) or not en_val.strip():
                    continue
                key = "%s::%s::%s" % (fname, str_name, f)
                if key in seen:
                    continue
                seen.add(key)
                ru_val = overlay.get(str_name, {}).get(f, "")
                items.append({"id": key, "file": fname, "name": str_name, "field": f,
                              "en": en_val, "cur": ru_val if isinstance(ru_val, str) else ""})
    return items


def load_done():
    if not os.path.exists(DONE_FILE):
        return set()
    with io.open(DONE_FILE, encoding="utf-8") as f:
        return set(line.strip() for line in f if line.strip())


_done_lock = threading.Lock()


def mark_done(ids):
    with _done_lock:
        with io.open(DONE_FILE, "a", encoding="utf-8") as f:
            for i in ids:
                f.write(i + "\n")


def process_batch(batch, state):
    for_translate = [{"id": it["id"], "en": it["en"], "cur": it["cur"], "ctx": it["file"] + "/" + it["name"]}
                      for it in batch]

    try:
        cand_a = chat_json(TRANSLATE_SYS_PROMPT, for_translate, model="qwen", temperature=0.4)
    except Exception as e:
        log("batch A ERROR: %s" % e)
        cand_a = None
    try:
        cand_b = chat_json(TRANSLATE_SYS_PROMPT, for_translate, model="qwen", temperature=0.8)
    except Exception as e:
        log("batch B ERROR: %s" % e)
        cand_b = None

    cand_a = cand_a if isinstance(cand_a, dict) else {}
    cand_b = cand_b if isinstance(cand_b, dict) else {}
    if not cand_a and not cand_b:
        log("SKIP batch (both A and B failed), %d items" % len(batch))
        return 0

    arb_items = []
    for it in batch:
        a = cand_a.get(it["id"]) or it["cur"] or it["en"]
        b = cand_b.get(it["id"]) or it["cur"] or it["en"]
        arb_items.append({"id": it["id"], "en": it["en"], "cur": it["cur"], "a": a, "b": b})

    try:
        final = chat_json(ARBITER_SYS_PROMPT, arb_items, model="qwen", temperature=0.2)
    except Exception as e:
        log("batch ARBITER ERROR: %s" % e)
        final = None
    final = final if isinstance(final, dict) else {}
    if not final:
        # fall back to candidate A (or cur) so the batch still makes progress
        final = {it["id"]: (cand_a.get(it["id"]) or it["cur"] or it["en"]) for it in batch}

    got = 0
    touched_files = set()
    done_ids = []
    for it in batch:
        v = final.get(it["id"])
        if not v or not str(v).strip():
            continue
        v = str(v).strip()
        with state["locks"][it["file"]]:
            state["overlays"][it["file"]].setdefault(it["name"], {})[it["field"]] = v
        touched_files.add(it["file"])
        done_ids.append(it["id"])
        got += 1

    for fn in touched_files:
        with state["locks"][fn]:
            save_json(os.path.join(RU_DATA, fn), state["overlays"][fn])
    mark_done(done_ids)
    return got


def main():
    api = check_api(model="qwen")
    log("api: %s" % api)
    if not api.get("ok"):
        log("ERROR: Qwen-прокси недоступен")
        sys.exit(1)

    all_items = collect_items()
    log("всего переводимых полей в корпусе: %d" % len(all_items))

    done_ids = load_done()
    log("уже готово (resume): %d" % len(done_ids))
    remaining = [it for it in all_items if it["id"] not in done_ids]
    log("осталось: %d" % len(remaining))
    if not remaining:
        log("ГОТОВО: нечего переводить")
        return

    overlays = {}
    locks = {}
    for fn in os.listdir(RU_DATA):
        overlays[fn] = load_json(os.path.join(RU_DATA, fn))
        locks[fn] = threading.Lock()
    state = {"overlays": overlays, "locks": locks}

    batches = [remaining[i:i + BATCH] for i in range(0, len(remaining), BATCH)]
    log("батчей: %d (по %d полей, %d воркеров)" % (len(batches), BATCH, MAX_WORKERS))

    total_target = len(remaining)
    done_count = 0
    start = time.time()
    with concurrent.futures.ThreadPoolExecutor(max_workers=MAX_WORKERS) as ex:
        futures = {ex.submit(process_batch, b, state): b for b in batches}
        for fut in concurrent.futures.as_completed(futures):
            try:
                got = fut.result()
            except Exception as e:
                log("batch task ERROR: %s" % e)
                got = 0
            done_count += got
            elapsed = time.time() - start
            rate = done_count / elapsed if elapsed > 0 else 0
            eta_min = (total_target - done_count) / rate / 60 if rate > 0 else -1
            log("прогресс: %d/%d (%.1f/сек, ETA %.0f мин)" % (done_count, total_target, rate, eta_min))

    log("ВСЁ ГОТОВО: %d полей обработано" % done_count)


if __name__ == "__main__":
    main()
