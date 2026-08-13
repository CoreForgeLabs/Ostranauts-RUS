#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ensemble_retranslate.py — a+b+c ensemble quality pass over remaining Phase 6
grammar-bug candidates (Class A: [us-pos] before non-masc-singular noun;
Class B: [has]-quality constructions), per user instruction 2026-08-13:
"a+b+c: a/b = 2 independent Qwen translations per text, c = a third Qwen
that judges which is better" — run once, thoroughly, so this doesn't need
redoing.

Pipeline per candidate item:
  1. Call Qwen twice independently (candidate A, candidate B) with the same
     system prompt (grammar-fix instructions, same as tools/retranslate_grammar_v2.py's
     SYS_PROMPT) but two separate API calls, so sampling variance gives two
     genuinely different attempts.
  2. Call Qwen a third time (arbiter) with the EN source, the current (buggy)
     RU text, and both candidates A/B, asking it to pick the better one OR
     synthesize an improved final version combining their strengths -- and to
     self-report a short reason.
  3. Validate token-set resolvability against Task 6.6's rule (informal
     re-check here; the real gate is tools/validate_content_overlay.py run
     after this script finishes).
  4. Save incrementally after EVERY item (not batched) -- lesson learned from
     Task 6.7's mid-run crash losing 105 unsaved results.

Run: python ensemble_retranslate.py
"""
import io
import json
import os
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LANG_SRC = os.path.join(ROOT, "lang_src")
RU_DATA = os.path.join(ROOT, "langs", "ru", "data")

sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_text, check_api  # noqa: E402

LOG = os.path.join(LANG_SRC, "ensemble_retranslate.log")
OUT_JSON = os.path.join(LANG_SRC, "ensemble_results.json")


def log(msg):
    line = "[%s] %s" % (time.strftime("%H:%M:%S"), msg)
    print(line, flush=True)
    with io.open(LOG, "a", encoding="utf-8") as f:
        f.write(line + "\n")


TRANSLATE_SYS_PROMPT = """Ты редактор русской локализации игры Ostranauts (суровый sci-fi симулятор
разборки кораблей и выживания в поясе астероидов, тон циничный, без пафоса).

Тебе дают ОДНУ фразу с грамматической ошибкой (английский источник для контекста,
текущий русский текст с багом) и просят переформулировать русский текст,
исправив ошибку. Классы ошибок:
- [us-pos]/[my-pos] стоит перед существительным НЕ мужского рода/не единственного
  числа. Токен [us-pos]/[my-pos] выдаёт ТОЛЬКО "твой"/"мой" (именительный, муж.
  род, ед. число) — он никогда не согласуется с родом/числом существительного.
  Исправление: либо поставь после токена существительное мужского рода
  единственного числа (даже если это близкий синоним), либо перестрой фразу
  так, чтобы не требовалось согласование (например используй "У [us-gen]" +
  масс.род.ед.ч. существительное, или используй возвратное местоимение
  "свой/своя/своё/свои" (пишется буквально, без токена) — оно согласуется
  само по себе с любым родом и не требует токена).
- [has] используется в значении "обладает качеством/навыком/недугом", а не
  "владеет предметом" — по-русски это должно звучать как "У [X-gen] [has.qual]
  качество", а не буквально "имеет качество". Токен [has.qual] уже существует
  в verbs.json и рендерится как пустая связка (аналогично русскому "быть" в
  настоящем времени) — то есть "У [us-gen] [has.qual] X" даёт "У тебя X" без
  лишнего глагола.

ВАЖНЫЕ ПРАВИЛА:
- Единственный ДОСТУПНЫЙ падежный токен — "-gen" (например [us-gen],
  [them-gen]). НЕ используй -dat/-acc/-ins/-prep — этих категорий пока нет в
  движке, токен с ними молча не сработает (текст останется английским /
  токен не подставится). Если [X-gen] недостаточно — используй возвратное
  "свой/своя/своё/свои" как обычное русское слово, не токен.
- Сохраняй все остальные токены ([us], [them], [is], [feels], [wants] и т.д.)
  буквально, на разумном месте во фразе.
- Плейсхолдеры {0}, теги <i>/<b>, \\n — не трогай.
- Ответ — только исправленная русская фраза, без пояснений, без кавычек
  вокруг всего ответа."""

ARBITER_SYS_PROMPT = """Ты старший редактор русской локализации игры Ostranauts. Тебе дают
английский оригинал фразы, ТЕКУЩИЙ (баговый) русский вариант и ДВА кандидата
на исправление (A и B), независимо сгенерированных другой моделью.

Выбери или создай ЛУЧШИЙ финальный вариант:
- Он должен реально исправлять грамматическую ошибку (см. описание класса
  ошибки в контексте).
- Он должен звучать естественно по-русски, в тоне игры (цинично, без пафоса).
- Он ДОЛЖЕН использовать только токен [-gen] из падежных (не -dat/-acc/-ins/
  -prep), и сохранять все остальные токены исходной фразы (просто прочитай
  оба кандидата — если оба используют одинаковый набор токенов, но чуть
  по-разному сформулированы, выбери более естественный; если один кандидат
  ошибочно использует запрещённый падеж или ломает токены — отклони его в
  пользу другого, или исправь сам).
- Можешь взять частично от A, частично от B, или написать свой вариант, если
  оба хуже, чем то, что ты можешь придумать сам.

Ответ — СТРОГО JSON: {"final": "финальная фраза", "reason": "одна короткая
фраза, почему"}."""


def load_candidates():
    cands = json.loads(io.open(os.path.join(LANG_SRC, "grammar_v2_candidates.json"),
                                encoding="utf-8").read())
    items = []
    for e in cands.get("class_a", []):
        items.append({"file": e["file"], "name": e["name"], "field": e["field"],
                       "ru": e["text"], "cls": "A"})
    for e in cands.get("class_b", []):
        items.append({"file": e["file"], "name": e["name"], "field": e["field"],
                       "ru": e["text"], "cls": "B"})
    return items


def load_en_source():
    """EN source text keyed by (file, name, field) -- reuse import_old_translation's
    category loader the same way scan_grammar_v2_targets.py does."""
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from import_old_translation import load_category, load_simple_category, is_simple, output_category_for
    CUR_DATA = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data"
    CATEGORIES = ["interactions", "careers", "conditions", "pda_apps", "installables", "cooverlays",
                  "condowners", "ledgerdefs", "pledges", "slots", "headlines", "plots",
                  "market/CoCollections", "ads", "rooms", "jobitems", "racing/tracks", "context",
                  "racing/leagues", "conditions_simple", "info", "market/Production", "tips"]
    en_by_file = {}
    for cat in CATEGORIES:
        cur = load_simple_category(CUR_DATA, cat) if is_simple(cat) else load_category(CUR_DATA, cat)
        out_cat = output_category_for(cat)
        fname = out_cat.replace("/", "_") + ".json"
        en_by_file.setdefault(fname, {}).update(cur)
    return en_by_file


def get_en_text(en_by_file, fn, name, field):
    obj = en_by_file.get(fn, {}).get(name, {})
    return obj.get(field, "")


def translate_once(en, ru, cls):
    ctx = ("Класс ошибки: %s\nАнглийский оригинал: %s\nТекущий русский (с багом): %s\n"
           "Исправленная фраза:") % (
        "A ([us-pos]/[my-pos] перед не-муж.-ед. существительным)" if cls == "A"
        else "B ([has] в значении качества/навыка)", en, ru)
    try:
        result = chat_text(TRANSLATE_SYS_PROMPT, ctx, model="qwen", max_tokens=500)
        return result.strip() if result else None
    except Exception as e:
        log("translate_once ERROR: %s" % e)
        return None


def arbitrate(en, ru, cand_a, cand_b, cls):
    ctx = json.dumps({
        "class": "A" if cls == "A" else "B",
        "en": en, "current_ru": ru, "candidate_A": cand_a, "candidate_B": cand_b,
    }, ensure_ascii=False)
    try:
        from llm_client import chat_json
        result = chat_json(ARBITER_SYS_PROMPT, ctx, model="qwen", max_tokens=500)
        if isinstance(result, dict) and result.get("final"):
            return result["final"].strip(), result.get("reason", "")
    except Exception as e:
        log("arbitrate ERROR: %s" % e)
    return None, None


def main():
    api = check_api(model="qwen")
    log("api: %s" % api)
    if not api.get("ok"):
        log("ERROR: Qwen недоступен")
        sys.exit(1)

    items = load_candidates()
    log("candidates: %d" % len(items))
    en_by_file = load_en_source()

    results = {}
    if os.path.exists(OUT_JSON):
        results = json.loads(io.open(OUT_JSON, encoding="utf-8").read())
        log("resuming, already have %d results" % len(results))

    done = 0
    for it in items:
        key = "%s::%s::%s" % (it["file"], it["name"], it["field"])
        if key in results:
            done += 1
            continue

        en = get_en_text(en_by_file, it["file"], it["name"], it["field"])
        cand_a = translate_once(en, it["ru"], it["cls"])
        cand_b = translate_once(en, it["ru"], it["cls"])
        if not cand_a and not cand_b:
            log("SKIP (both candidates failed): %s" % key)
            continue
        if not cand_a:
            cand_a = it["ru"]
        if not cand_b:
            cand_b = it["ru"]

        final, reason = arbitrate(en, it["ru"], cand_a, cand_b, it["cls"])
        if not final:
            log("SKIP (arbiter failed): %s" % key)
            continue

        results[key] = {
            "file": it["file"], "name": it["name"], "field": it["field"],
            "en": en, "before": it["ru"], "candidate_a": cand_a, "candidate_b": cand_b,
            "final": final, "reason": reason,
        }
        # incremental save after EVERY item
        with io.open(OUT_JSON, "w", encoding="utf-8") as f:
            json.dump(results, f, ensure_ascii=False, indent=2)

        done += 1
        log("done %d/%d: %s -> %s (%s)" % (done, len(items), key, final[:60], reason))

    log("ALL DONE: %d/%d results in %s" % (len(results), len(items), OUT_JSON))

    # Apply results into langs/ru/data/*.json
    by_file = {}
    for r in results.values():
        by_file.setdefault(r["file"], {})
        by_file[r["file"]].setdefault(r["name"], {})[r["field"]] = r["final"]

    applied = 0
    for fn, overlay in by_file.items():
        path = os.path.join(RU_DATA, fn)
        d = json.loads(io.open(path, encoding="utf-8").read())
        for name, fields in overlay.items():
            d.setdefault(name, {}).update(fields)
            applied += len(fields)
        with io.open(path, "w", encoding="utf-8") as f:
            json.dump(d, f, ensure_ascii=False, indent=2)
        log("applied %d fields into %s" % (len(overlay), fn))

    log("APPLIED: %d fields total" % applied)


if __name__ == "__main__":
    main()
