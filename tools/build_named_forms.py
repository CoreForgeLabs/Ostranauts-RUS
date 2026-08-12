#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_named_forms.py — строит langs/ru/named_forms.json: таблицу падежных форм
для уже переведённых (не английских!) коротких имён именованных игровых
сущностей (`strNameShort` из langs/ru/data/condowners.json).

Это ЗАДАЧА РУССКОГО СКЛОНЕНИЯ, а не перевода — вход уже на русском
("Крионасос", "Отсек батареи"), нужно сгенерировать 6 падежных форм плюс
род/одушевлённость/число. См. .superpowers/sdd/2026-08-13-i18n-architecture-v2/
task-6.2-brief.md для полного обоснования и разбора ключевой схемы.

Ключевая схема (см. отчёт task-6.2-report.md за обоснование): файл кладём по
ТЕКСТУ strNameShort (338 ключей), а не по strName записи (1116 записей,
338 уникальных strNameShort) — потому что рантайм (Patches.cs,
AttemptSubstitutionPrefix/AttemptProperNamePrefix) на месте подстановки имеет
только `ent.CondOwner.ShortName`, который у подавляющего большинства записей
(там, где pspec == null, см. decompiled/CondOwner.cs:957) УЖЕ РАВЕН
strNameShort — то есть тексту, а не ID записи condowners. Ключ по тексту —
единственный, который резолвер Task 6.3 сможет использовать без дополнительной
записи->текст индирекции.

Запуск: python build_named_forms.py
"""
import io
import json
import os
import re
import sys
import time
import concurrent.futures

sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_json, check_api  # noqa: E402

# NOTE: unlike translate_data.py's hardcoded ROOT (which targets the shared
# main checkout), this derives ROOT from the script's own location so it stays
# worktree-safe when run from a git worktree (as this task's tooling was) --
# a hardcoded main-repo path here would silently read/write the WRONG
# checkout's files. See task-6.2-report.md for how this was caught.
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONDOWNERS_PATH = os.path.join(ROOT, "langs", "ru", "data", "condowners.json")
OUT_PATH = os.path.join(ROOT, "langs", "ru", "named_forms.json")
UNRESOLVED_PATH = os.path.join(ROOT, "langs", "ru", "unresolved_forms.tsv")
LOG_PATH = os.path.join(ROOT, "lang_src", "build_named_forms.log")

BATCH = 15
MAX_WORKERS = 8  # снижено относительно translate_data.py (200) -- прокси Qwen
                  # в недавних сессиях перегружался на большой параллельности.

CASES = ("nom", "gen", "dat", "acc", "ins", "prep")

SYS_PROMPT = (
    "Ты — специалист по русской морфологии. Тебе даётся список уже РУССКИХ "
    "существительных и коротких именных словосочетаний — это отображаемые "
    "короткие названия предметов/объектов в игре (например «Крионасос», "
    "«Отсек батареи», «Ядро блест.»). Никакого перевода не требуется и не "
    "нужно — весь текст уже на русском.\n\n"
    "Твоя задача: просклонять каждое словосочетание по шести падежам русского "
    "языка (именительный/родительный/дательный/винительный/творительный/"
    "предложный), определить его грамматический род (мужской/женский/средний) "
    "и одушевлённость, и указать, является ли оно по смыслу множественным "
    "числом (например «Патроны», «Обломки»).\n\n"
    "СТРОГИЕ ПРАВИЛА:\n"
    "1. Поле \"nom\" в твоём ответе ДОЛЖНО дословно совпадать со входной "
    "строкой символ-в-символ (это именительный падеж — то, с чего ты "
    "начинаешь, а не то, что ты сочиняешь заново). Не исправляй, не "
    "перефразируй, не меняй регистр, пробелы или пунктуацию входа.\n"
    "2. Склоняй ТОЛЬКО реальные русские слова. Аббревиатуры, коды, единицы "
    "измерения, латинские вставки, цифры (например «CO2», «O2», «5.56», "
    "«.45», «D2O», «HG», «(слом)», числовые индексы) НЕ склоняются и НЕ "
    "переводятся — переноси их в каждую падежную форму буквально как в "
    "исходнике, склоняя только окружающие русские слова.\n"
    "3. Двоеточия, точки-сокращения (например «Боепр.:», «Актив.»), скобки — "
    "сохраняй как есть на своём месте во всех формах.\n"
    "4. Если словосочетание состоит из нескольких слов (например «Отсек "
    "батареи»), склоняй по правилам согласования/управления русской "
    "грамматики (главное слово меняет падеж, зависимые слова — как требует "
    "грамматика: несогласованное дополнение в родительном обычно остаётся в "
    "родительном во всех формах, согласованное прилагательное согласуется по "
    "падежу с определяемым словом).\n"
    "5. Если строка — не отдельное слово, а обрубленная аббревиатура без "
    "явного русского корня (кириллицей), считай её несклоняемой: во всех 6 "
    "форм верни исходную строку без изменений, gender=\"m\", animate=false, "
    "plural=false.\n\n"
    "Ответ — СТРОГО JSON-объект вида "
    "{\"id\": {\"nom\":\"...\",\"gen\":\"...\",\"dat\":\"...\",\"acc\":\"...\","
    "\"ins\":\"...\",\"prep\":\"...\",\"gender\":\"m|f|n\",\"animate\":true|false,"
    "\"plural\":true|false}, ...} с ТЕМИ ЖЕ id, что во входном массиве. Без "
    "пояснений, без markdown, только JSON."
)

LATIN_RE = re.compile(r"[A-Za-z]")


def log(msg):
    line = "[%s] %s" % (time.strftime("%H:%M:%S"), msg)
    print(line, flush=True)
    try:
        with io.open(LOG_PATH, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except Exception:
        pass


def load_json(path):
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


def save_json(path, data):
    tmp = path + ".tmp"
    with io.open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    os.replace(tmp, path)


def collect_unique_shortnames():
    d = load_json(CONDOWNERS_PATH)
    names = sorted({v.get("strNameShort") for v in d.values() if v.get("strNameShort")})
    return names, d


def validate_item(src, resp):
    """Возвращает (ok, reason)."""
    if not isinstance(resp, dict):
        return False, "not-a-dict"
    for c in CASES:
        v = resp.get(c)
        if not v or not isinstance(v, str) or not v.strip():
            return False, "empty-form:%s" % c
    if resp["nom"] != src:
        return False, "nom-mismatch:%r" % resp["nom"]
    gender = resp.get("gender")
    if gender not in ("m", "f", "n"):
        return False, "bad-gender:%r" % gender
    if "animate" not in resp or not isinstance(resp.get("animate"), bool):
        return False, "bad-animate:%r" % resp.get("animate")
    if "plural" not in resp or not isinstance(resp.get("plural"), bool):
        return False, "bad-plural:%r" % resp.get("plural")
    src_latin = set(LATIN_RE.findall(src))
    for c in CASES:
        form_latin = set(LATIN_RE.findall(resp[c]))
        if form_latin - src_latin:
            return False, "new-latin-in:%s:%r" % (c, resp[c])
    return True, None


def run_batch(names_slice):
    items = [{"id": "n%d" % i, "text": s} for i, s in enumerate(names_slice)]
    try:
        res = chat_json(SYS_PROMPT, items, model="qwen")
    except Exception as e:
        log("batch ERROR: %s" % e)
        return {}
    if not isinstance(res, dict):
        return {}
    out = {}
    for i, s in enumerate(names_slice):
        v = res.get("n%d" % i)
        if v is not None:
            out[s] = v
    return out


def attempt_pass(names, label):
    """Прогоняет весь список names через Qwen батчами, возвращает
    dict short_name -> response (сырой, ещё не провалидированный)."""
    batches = [names[i:i + BATCH] for i in range(0, len(names), BATCH)]
    results = {}
    done = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=MAX_WORKERS) as ex:
        futures = {ex.submit(run_batch, b): b for b in batches}
        for fut in concurrent.futures.as_completed(futures):
            b = futures[fut]
            r = fut.result()
            results.update(r)
            done += len(b)
            log("%s: батч готов (+%d, всего обработано %d/%d)" % (label, len(r), done, len(names)))
    return results


def main():
    api = check_api(model="qwen")
    log("api: %s" % api)
    if not api.get("ok"):
        log("ERROR: Qwen-прокси недоступен")
        sys.exit(1)

    names, condowners = collect_unique_shortnames()
    log("уникальных strNameShort: %d" % len(names))

    raw = attempt_pass(names, "pass1")

    resolved = {}
    failed = {}  # short_name -> reason
    for s in names:
        resp = raw.get(s)
        if resp is None:
            failed[s] = "no-response"
            continue
        ok, reason = validate_item(s, resp)
        if ok:
            resolved[s] = resp
        else:
            failed[s] = reason

    # Один повторный проход только для провалившихся -- не зацикливаемся.
    if failed:
        retry_names = list(failed.keys())
        log("pass2 (retry): %d элементов" % len(retry_names))
        raw2 = attempt_pass(retry_names, "pass2")
        for s in retry_names:
            resp = raw2.get(s)
            if resp is None:
                continue
            ok, reason = validate_item(s, resp)
            if ok:
                resolved[s] = resp
                del failed[s]
            else:
                failed[s] = reason

    # Финальный файл: ключ = ТЕКСТ strNameShort (см. докстринг модуля и отчёт
    # за обоснование этой ключевой схемы вместо strName записи).
    out = {}
    for s, resp in resolved.items():
        out[s] = {
            "forms": {c: resp[c] for c in CASES},
            "gender": resp["gender"],
            "animate": bool(resp["animate"]),
            "plural": bool(resp["plural"]),
        }
    save_json(OUT_PATH, out)

    with io.open(UNRESOLVED_PATH, "w", encoding="utf-8") as f:
        f.write("short_name\treason\n")
        for s in sorted(failed):
            f.write("%s\t%s\n" % (s, failed[s]))

    total = len(names)
    n_resolved = len(resolved)
    n_failed = len(failed)
    rate = 100.0 * n_resolved / total if total else 0.0

    log("=" * 60)
    log("ИТОГО: уникальных strNameShort = %d" % total)
    log("резолвлено (валидный полный набор форм) = %d" % n_resolved)
    log("нерезолвлено (-> unresolved_forms.tsv) = %d" % n_failed)
    log("resolution rate = %.2f%%" % rate)
    log("named_forms.json: %d ключей -> %s" % (len(out), OUT_PATH))
    log("unresolved_forms.tsv -> %s" % UNRESOLVED_PATH)

    if rate < 95.0:
        log("GATE FAILED: resolution rate %.2f%% < 95%%" % rate)
        sys.exit(2)
    else:
        log("GATE PASSED: resolution rate %.2f%% >= 95%%" % rate)


if __name__ == "__main__":
    main()
