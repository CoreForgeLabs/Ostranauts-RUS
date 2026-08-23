# -*- coding: utf-8 -*-
"""
Чинит токены, которые конвейер перевёл как обычный текст: [says] -> [говорит].
Движок такие токены не резолвит и печатает как есть.

Карта не пишется руками: она выводится выравниванием EN- и RU-последовательностей
токенов. Если в позиции i у EN стоит [says], а у RU -- неизвестный [говорит], и
эта пара стабильно повторяется по всему корпусу, значит это одно и то же.

  python tools/qa_fix_tokens.py           -- показать карту и что будет заменено
  python tools/qa_fix_tokens.py --apply   -- записать изменения в langs/ru/data
"""
import json, io, os, re, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EN_DIR = os.path.join(ROOT, "langs", "en", "data")
RU_DIR = os.path.join(ROOT, "langs", "ru", "data")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from qa_scan_grammar import TOKEN_RE, load, str_fields, VERB_KEYS, _harvest_en_tokens
import qa_scan_grammar as qa

LF = chr(10)
MIN_SUPPORT = 3          # реже -- это совпадение, а не правило
MIN_CONFIDENCE = 0.90    # доля доминирующего варианта среди всех наблюдений
# Связки и модальные: в английском они тянут за собой дополнение ("[is] damaged"),
# которое русский перевод вобрал внутрь токена. Механически такие не чиним.
COMPLEMENT_TAKERS = {"is", "has", "doesn't", "does", "fails", "can", "will", "was"}

def build_mapping():
    votes = collections.defaultdict(collections.Counter)
    for name in sorted(os.listdir(RU_DIR)):
        if not name.endswith(".json") or name.endswith("_translated.json"):
            continue
        en_p, ru_p = os.path.join(EN_DIR, name), os.path.join(RU_DIR, name)
        if not (os.path.exists(en_p) and os.path.exists(ru_p)):
            continue
        en, ru = load(en_p), load(ru_p)
        for rec_id, field, ru_val in str_fields(ru):
            en_rec = en.get(rec_id)
            en_val = en_rec.get(field) if isinstance(en_rec, dict) else None
            if not isinstance(en_val, str):
                continue
            en_toks, ru_toks = TOKEN_RE.findall(en_val), TOKEN_RE.findall(ru_val)
            unknown = [t for t in ru_toks if not qa.known_token(t)]
            if not unknown:
                continue
            # кандидаты: токены EN, которых в RU не осталось
            missing = [t for t in en_toks if t not in ru_toks]
            if len(unknown) == len(missing):
                # позиционное выравнивание один-к-одному
                for u, m in zip(unknown, missing):
                    votes[u][m] += 1
    mapping, rejected = {}, {}
    for ru_tok, counter in votes.items():
        total = sum(counter.values())
        best, n = counter.most_common(1)[0]
        if total < MIN_SUPPORT or n / total < MIN_CONFIDENCE:
            rejected[ru_tok] = (counter.most_common(3), total)
            continue
        # Замена безопасна, только если RU-токен -- один глагол, вставший на место
        # одного глагола EN. Многословные ("[получает повреждения]" из "[is] damaged")
        # вобрали в себя дополнение, и подстановка [is] его потеряет.
        if " " in ru_tok or best in COMPLEMENT_TAKERS or best not in VERB_KEYS:
            rejected[ru_tok] = (counter.most_common(3), total)
            continue
        mapping[ru_tok] = (best, n, total)
    return mapping, rejected

def safe_pair(ru_tok, en_tok):
    """Замена безопасна, только если один русский глагол встал на место одного
    английского. Многословные ("[получает повреждения]" из "[is] damaged") вобрали
    дополнение -- подстановка [is] его потеряет."""
    return (" " not in ru_tok
            and en_tok not in COMPLEMENT_TAKERS
            and en_tok in VERB_KEYS)

def apply_per_string(write):
    """Чиним каждую строку по её собственному оригиналу: [говорит] -> [says] там,
    где в EN стоял [says], и -> [tells] там, где стоял [tells]."""
    total_fixed, total_strings, per_pair = 0, 0, collections.Counter()
    for name in sorted(os.listdir(RU_DIR)):
        if not name.endswith(".json") or name.endswith("_translated.json"):
            continue
        en_p, ru_p = os.path.join(EN_DIR, name), os.path.join(RU_DIR, name)
        if not (os.path.exists(en_p) and os.path.exists(ru_p)):
            continue
        en, ru = load(en_p), load(ru_p)
        touched = 0
        for rec_id, rec in ru.items():
            if not isinstance(rec, dict):
                continue
            en_rec = en.get(rec_id)
            if not isinstance(en_rec, dict):
                continue
            for field, ru_val in list(rec.items()):
                en_val = en_rec.get(field)
                if not isinstance(ru_val, str) or not isinstance(en_val, str):
                    continue
                ru_toks, en_toks = TOKEN_RE.findall(ru_val), TOKEN_RE.findall(en_val)
                unknown = [t for t in ru_toks if not qa.known_token(t)]
                missing = [t for t in en_toks if t not in ru_toks]
                if not unknown or len(unknown) != len(missing):
                    continue
                pairs = list(zip(unknown, missing))
                if not all(safe_pair(u, m) for u, m in pairs):
                    continue
                new_val = ru_val
                for u, m in pairs:
                    new_val = new_val.replace("[" + u + "]", "[" + m + "]")
                    per_pair[(u, m)] += 1
                    total_fixed += 1
                rec[field] = new_val
                touched += 1
        if touched:
            total_strings += touched
            print("  %-34s строк: %d" % (name, touched))
            if write:
                io.open(ru_p, "w", encoding="utf-8", newline=LF).write(
                    json.dumps(ru, ensure_ascii=False, indent=2) + LF)
    return total_strings, total_fixed, per_pair

def main():
    qa.EN_TOKENS = _harvest_en_tokens()
    mapping, rejected = build_mapping()
    print("принятая карта токенов (%d):" % len(mapping))
    for ru_tok, (en_tok, n, total) in sorted(mapping.items(), key=lambda x: -x[1][2]):
        print("  [%s] -> [%s]   %d/%d" % (ru_tok, en_tok, n, total))
    if rejected:
        print("\nотклонено (мало данных или разнобой): %d" % len(rejected))
        for ru_tok, (top, total) in sorted(rejected.items(), key=lambda x: -x[1][1])[:12]:
            print("  [%s] всего %d -> %s" % (ru_tok, total, top))
    write = "--apply" in sys.argv
    print("")
    print("%s (по каждой строке, против её оригинала):"
          % ("ПРИМЕНЯЮ" if write else "предпросмотр"))
    strings, fixed, per_pair = apply_per_string(write)
    print("строк: %d, замен токенов: %d, различных пар: %d"
          % (strings, fixed, len(per_pair)))
    for (u, m), n in per_pair.most_common(12):
        print("   [%s] -> [%s]  x%d" % (u, m, n))

if __name__ == "__main__":
    main()
