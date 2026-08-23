# -*- coding: utf-8 -*-
"""
Проверка покрытия падежной таблицы.

named_forms.json -- отдельный артефакт: конвейер перевода обновляет
condowners.json и о нём не знает. Любое имя, которое он добавил или изменил,
приходит без падежей, и в игре печатается именительный: "панель обогреватель".
Дыру видно только в игре, если её специально не искать -- поэтому ищем.

Возвращает ненулевой код, если покрытие неполное: годится для CI и для вызова
в конце translate_all.py.
"""
import json, io, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CO = os.path.join(ROOT, "langs", "ru", "data", "condowners.json")
NF = os.path.join(ROOT, "langs", "ru", "named_forms.json")
VERBS = os.path.join(ROOT, "langs", "ru", "verbs.json")
PACK = os.path.join(ROOT, "langs", "ru", "pack.json")


def load(p):
    with io.open(p, encoding="utf-8-sig") as f:
        return json.load(f)


def verb_coverage():
    """Глагол без русской парадигмы печатается по-английски посреди фразы.
    Ищем токены английского корпуса, для которых её нет."""
    import re, collections
    TOK = re.compile(r"\[([^\[\]]+)\]")
    ENT = re.compile(r"^(us|them|3rd|it|they)(-.*)?$")
    verbs = set(k for k in load(VERBS) if not k.startswith("_"))
    cats = set(load(PACK).get("pronounCategories", {}))
    cats |= {"firstname", "fullname", "surname", "shipname", "shipfriendly",
             "friendly", "captain", "age", "homeworld", "regID", "loc"}
    missing = collections.Counter()
    en_dir = os.path.join(ROOT, "langs", "en", "data")
    for n in os.listdir(en_dir):
        if not n.endswith(".json"):
            continue
        try:
            data = load(os.path.join(en_dir, n))
        except Exception:
            continue
        for _rid, rec in data.items():
            if not isinstance(rec, dict):
                continue
            for _f, v in rec.items():
                if not isinstance(v, str):
                    continue
                for t in TOK.findall(v):
                    if ENT.match(t):
                        parts = t.split("-")
                        if len(parts) != 2:
                            continue
                        t = parts[1]
                    if t in cats or t in verbs:
                        continue
                    if re.match(r"^[a-z][a-z']+$", t):
                        missing[t] += 1
    return missing


def main():
    co, nf = load(CO), load(NF)
    missing = [k for k in co if k not in nf]
    print("condowners: %d, падежных записей: %d, без падежей: %d"
          % (len(co), len(nf), len(missing)))
    if missing:
        print("\nБез падежных форм (в игре будут в именительном):")
        for k in missing[:40]:
            print("   %-32s %s" % (k, co[k].get("strNameFriendly", "")))
        if len(missing) > 40:
            print("   ... и ещё %d" % (len(missing) - 40))
        return 1
    miss_verbs = verb_coverage()
    if miss_verbs:
        print("")
        print("Глаголы без русской парадигмы (напечатаются по-английски):")
        for t, c in miss_verbs.most_common(20):
            print("   %-18s %d вхождений" % (t, c))
        return 1
    print("покрытие полное: падежи и глагольные парадигмы")
    return 0


if __name__ == "__main__":
    sys.exit(main())
