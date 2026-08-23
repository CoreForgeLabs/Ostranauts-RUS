# -*- coding: utf-8 -*-
"""
Разбирает склейки, где перевод втянул связку [is]/[was] внутрь выдуманного
токена: "[us] [получает повреждения]." из "[us] [is] damaged."

Раньше это чинить было нечем -- подстановка одного [is] потеряла бы слово
"damaged". Теперь есть категории согласования по роду, поэтому склейка
разбирается на связку и краткое причастие, которое согласуется само:
  [us] [is] поврежден[us-endsadj].  ->  поврежден / повреждена / повреждены

Отдельно чинится "получил[а]" -- переводчик изобрёл токен для окончания
женского рода, потому что настоящего не существовало. Теперь существует.

  python tools/qa_fix_copula_merges.py [--apply]
"""
import json, io, os, re, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RU_DIR = os.path.join(ROOT, "langs", "ru", "data")
LF = chr(10)

# токен-склейка -> (связка, основа причастия) либо (связка, None) если основа
# остаётся из текста строки
MERGES = {
    "получает повреждения": ("[is]", "поврежден"),
    "уничтожается": ("[is]", "уничтожен"),
    "уничтожен": ("[is]", "уничтожен"),
    "поврежден": ("[is]", "поврежден"),
    "невосприимчив": ("[is]", "невосприимчив"),
    "идентифицирован": ("[is]", "идентифицирован"),
}
# прошедшее время, вписанное как токен: нужен сам глагол + согласование
PAST_MERGES = {"закончил": "закончил"}

ENTITY_RE = re.compile(r"\[(us|them)(?:-[a-zA-Z]+)?\]")


def single_entity(val):
    """Окончание относится к участнику строки; если их два, угадывать нельзя."""
    found = set(ENTITY_RE.findall(val))
    return found.pop() if len(found) == 1 else None


def convert(val, stats):
    out = val
    for token, (cop, stem) in MERGES.items():
        pat = re.compile(r"\[(us|them)\](\s*)\[" + re.escape(token) + r"\]")
        def repl(m, cop=cop, stem=stem):
            who = m.group(1)
            stats["связка + причастие"] += 1
            return "[%s]%s%s %s[%s-endsadj]" % (who, m.group(2) or " ", cop, stem, who)
        out = pat.sub(repl, out)

    for token, stem in PAST_MERGES.items():
        pat = re.compile(r"\[(us|them)\](\s*)\[" + re.escape(token) + r"\]")
        def repl2(m, stem=stem):
            who = m.group(1)
            stats["связка + прошедшее"] += 1
            return "[%s]%s[is] %s[%s-ends]" % (who, m.group(2) or " ", stem, who)
        out = pat.sub(repl2, out)

    # [был] -- это глагол "was", у него есть парадигма
    if "[был]" in out:
        stats["[был] -> [was]"] += out.count("[был]")
        out = out.replace("[был]", "[was]")

    # "получил[а]" -- самодельное окончание женского рода
    if "[а]" in out:
        who = single_entity(out)
        if who:
            n = out.count("[а]")
            out = re.sub(r"([А-Яа-яЁё]+)\[а\]", r"\1[%s-ends]" % who, out)
            stats["самодельное [а]"] += n
    return out


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
                if not isinstance(val, str) or "[" not in val:
                    continue
                new = convert(val, stats)
                if new != val:
                    rec[field] = new
                    touched += 1
                    if len(samples) < 8:
                        samples.append((rid, val.strip()[:85], new.strip()[:85]))
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
