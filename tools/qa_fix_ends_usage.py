# -*- coding: utf-8 -*-
"""
Разбирает уже существующие вхождения [x-ends], написанные переводчиками
вслепую: токена такого не было, он печатался как есть, и ошибки в нём никто
не видел. Теперь токен работает -- значит ошибки станут заметны.

Три разных случая, три разных категории:
  прошедшее время     "повысил[x-ends]"  -- уже верно, не трогаем
  возвратный глагол   "пытался[x-ends]"  -> "пыта[x-endsrefl]": окончание встаёт
                      перед -ся, и сам постфикс меняется (пыталась, пытались)
  краткое причастие   "сломан[x-ends]"   -> "сломан[x-endsadj]": множественное
                      на -ы, а не на -и

И четвёртый: слова, где окончание не приклеивается в принципе. "умер" даёт
"умерла", "привлекателен" -- "привлекательна" (беглая гласная), "неуклюж" --
"неуклюжи" вместо "неуклюжы" (правило жи/ши), "умрет" вообще будущее время.
У них токен снимаем: пусть остаётся мужской род, как и было, чем "умера".

  python tools/qa_fix_ends_usage.py [--apply]
"""
import json, io, os, re, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RU_DIR = os.path.join(ROOT, "langs", "ru", "data")
LF = chr(10)

# основа как она стоит в данных -> чем заменить основу (None = оставить)
REFLEXIVE = {"пытался": "пыта", "насладился": "наслади"}
ADJECTIVE = {"сломан", "вовлечен", "целеустремлен", "привередлив", "подвергнут",
             "заинтересован", "скоординирован", "предан", "отвлечен", "придирчив",
             "уязвим", "спровоцирован"}
# Окончание не приклеить: беглая гласная, чередование основы, правило жи/ши,
# либо это вообще не прошедшее время.
DROP = {"умер", "умрет", "неуклюж", "привлекателен", "склонен", "самоуверен"}

PAT = re.compile(r"([А-Яа-яЁё]+)\[(us|them)-ends\]")


def convert(val, stats):
    def repl(m):
        stem, who = m.group(1), m.group(2)
        low = stem.lower()
        if low in REFLEXIVE:
            stats["возвратные"] += 1
            new_stem = REFLEXIVE[low]
            if stem[0].isupper():
                new_stem = new_stem[0].upper() + new_stem[1:]
            return "%s[%s-endsrefl]" % (new_stem, who)
        if low in ADJECTIVE:
            stats["краткие причастия"] += 1
            return "%s[%s-endsadj]" % (stem, who)
        if low in DROP:
            stats["токен снят"] += 1
            return stem
        stats["прошедшее (без изменений)"] += 1
        return m.group(0)
    return PAT.sub(repl, val)


def main():
    write = "--apply" in sys.argv
    stats = collections.Counter()
    samples = []
    for name in sorted(os.listdir(RU_DIR)):
        if not name.endswith(".json") or name.endswith("_translated.json"):
            continue
        path = os.path.join(RU_DIR, name)
        data = json.load(io.open(path, encoding="utf-8-sig"))
        touched = 0
        for rid, rec in data.items():
            if not isinstance(rec, dict):
                continue
            for field, val in list(rec.items()):
                if not isinstance(val, str) or "-ends]" not in val:
                    continue
                new = convert(val, stats)
                if new != val:
                    rec[field] = new
                    touched += 1
                    if len(samples) < 8:
                        samples.append((rid, val.strip()[:80], new.strip()[:80]))
        if touched and write:
            io.open(path, "w", encoding="utf-8", newline=LF).write(
                json.dumps(data, ensure_ascii=False, indent=2) + LF)
        if touched:
            print("  %-30s строк: %d" % (name, touched))
    print(("ПРИМЕНЕНО" if write else "предпросмотр") + ":", dict(stats))
    for rid, old, new in samples:
        print("  ", rid)
        print("    было :", old)
        print("    стало:", new)


if __name__ == "__main__":
    main()
