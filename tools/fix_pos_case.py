#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fix_pos_case.py — целевая правка мест, где [us-pos]/[them-pos]/[3rd-pos] стоит
сразу после предлога, требующего не именительного падежа (у, для, до, без,
от, из, около, вокруг, против, среди, кроме, мимо). Таблица местоимений в
langs/lang_ru/grammar.json хранит только именительную форму ("твой"/"его"/"её"),
падеж по контексту она не знает — значит "у [us-pos] предела" всегда
рендерится как "у твой предела", а не "у твоего предела".

Полной системы склонений в движке нет (см. docs/architecture-audit.md) —
чинить это здесь, а не общей грамматической подсистемой. Решение: попросить
Qwen переформулировать фразу вокруг проблемного места так, чтобы pos-токен
не стоял сразу после падежного предлога (например заменить genitive-конструкцию
на именительный субъект или на идиому без явного владельца), сохранив ВСЕ
токены (в квадратных скобках) буквально и не потеряв смысл.

Запуск: python fix_pos_case.py
Источник списка: lang_src/pos_prep_issues.json (сгенерирован разовым grep-проходом).
"""
import io
import json
import os
import re
import sys

sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_json  # noqa: E402

ROOT = r"F:\DEV2\ostra_i18n"
ISSUES_PATH = os.path.join(ROOT, "lang_src", "pos_prep_issues.json")

TOKEN_RE = re.compile(r"\[[a-zA-Z][a-zA-Z0-9_-]*\]")
PREP_POS_RE = re.compile(r"\b(у|для|из|от|до|без|около|вокруг|против|среди|кроме|мимо)\s+\[(us|them|3rd)-pos\]")

SYS_PROMPT = (
    "Ты редактор русской локализации игры Ostranauts. Дана фраза с грамматической "
    "ошибкой: токен [us-pos]/[them-pos]/[3rd-pos] всегда подставляется в ИМЕНИТЕЛЬНОМ "
    "падеже ('твой'/'его'/'её'/'их'), но стоит сразу после предлога (у/для/из/от/до/без/"
    "около/вокруг/против/среди/кроме/мимо), которому нужен родительный падеж — движок "
    "не умеет склонять токен, поэтому получается 'у твой предела' вместо 'у твоего предела'.\n\n"
    "Переформулируй русскую фразу так, чтобы эта грамматическая ошибка исчезла — например, "
    "убери предлог перед pos-токеном (сделай его именительным субъектом: '[us-pos] X "
    "на пределе' вместо 'у [us-pos] X'), или замени на безличную идиому без явного "
    "владельца ('на пределе сил' вместо 'у твоего предела'), или переставь слова. "
    "ВАЖНО:\n"
    "- Все токены в квадратных скобках ([us], [them], [is], [us-pos] и т.д.) должны "
    "остаться в тексте буквально, в том же количестве, что и в исходнике — просто, "
    "возможно, в другом месте фразы.\n"
    "- Смысл и тон должны сохраниться.\n"
    "- Если pos-токен стоит после предлога МНОГОКРАТНО в одной фразе, исправь все вхождения.\n"
    "- Плейсхолдеры {0}, теги <b>/<i>, \\n — не трогай.\n\n"
    "Ответ — СТРОГО JSON {\"id\": \"исправленная фраза\", ...} с теми же id, что во входе."
)


def main():
    issues = json.loads(io.open(ISSUES_PATH, encoding="utf-8").read())
    print("issues:", len(issues))

    items = []
    by_id = {}
    for i, (fpath, name, field, val) in enumerate(issues):
        iid = "i%d" % i
        items.append({"id": iid, "text": val})
        by_id[iid] = (fpath, name, field, val)

    BATCH = 10
    results = {}
    for i in range(0, len(items), BATCH):
        batch = items[i:i + BATCH]
        res = chat_json(SYS_PROMPT, batch, model="qwen")
        if isinstance(res, dict):
            results.update(res)
        print("batch %d-%d done" % (i, i + len(batch)))

    changed_files = {}
    kept = 0
    rejected = 0
    for iid, (fpath, name, field, old_val) in by_id.items():
        new_val = results.get(iid)
        if not new_val or not isinstance(new_val, str):
            rejected += 1
            print("НЕТ ОТВЕТА:", fpath, name, field)
            continue
        old_tok = sorted(TOKEN_RE.findall(old_val))
        new_tok = sorted(TOKEN_RE.findall(new_val))
        if old_tok != new_tok:
            rejected += 1
            print("ОТКЛОНЕНО (токены разошлись):", name, field, "en_old=", old_tok, "new=", new_tok)
            continue
        if PREP_POS_RE.search(new_val):
            rejected += 1
            print("ОТКЛОНЕНО (проблема не исчезла):", name, field, "->", new_val)
            continue
        changed_files.setdefault(fpath, {})[(name, field)] = new_val
        kept += 1

    for fpath, edits in changed_files.items():
        d = json.loads(io.open(fpath, encoding="utf-8").read())
        for (name, field), new_val in edits.items():
            d[name][field] = new_val
        io.open(fpath, "w", encoding="utf-8").write(json.dumps(d, ensure_ascii=False, indent=2))

    print("принято: %d, отклонено: %d" % (kept, rejected))


if __name__ == "__main__":
    main()
