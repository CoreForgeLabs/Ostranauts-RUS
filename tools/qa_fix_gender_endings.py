# -*- coding: utf-8 -*-
"""
Переводит краткие причастия на согласование по роду через [x-endsadj].

"[us] [is] уничтожен." печаталось мужским родом для любого подлежащего:
"Панель уничтожен". Токен [us-endsadj] раскрывается в окончание по роду и
числу участника: уничтожен / уничтожена / уничтожены / уничтожено.

Белый список намеренно узкий. Не всякое короткое слово после [is] -- краткое
причастие: "радиоактивен" теряет беглую "е" в женском ("радиоактивна"), а
"фиолетовый" вообще полное прилагательное с другим набором окончаний. Такие
не трогаем -- лучше оставить как есть, чем сделать "радиоактивена".

  python tools/qa_fix_gender_endings.py [--apply]
"""
import json, io, os, re, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RU_DIR = os.path.join(ROOT, "langs", "ru", "data")
LF = chr(10)

# слово в данных -> основа, к которой безопасно клеится окончание
SAFE = {
    "уничтожен": "уничтожен",
    "повреждён": "поврежден",       # ё -> е: женский род "повреждена", не "повреждёна"
    "поврежден": "поврежден",
    "разобран": "разобран",
    "законсервирован": "законсервирован",
    "читаем": "читаем",
    "сыт": "сыт",
    "пьян": "пьян",
    "ядовит": "ядовит",
}
PAT = re.compile(r"\[(us|them)\](\s+\[is\]\s+)([А-Яа-яЁё]+)(\s*\.)")


def convert(val):
    def repl(m):
        who, mid, word, tail = m.groups()
        stem = SAFE.get(word.lower())
        if not stem:
            return m.group(0)
        if word[0].isupper():
            stem = stem[0].upper() + stem[1:]
        return "[%s]%s%s[%s-endsadj]%s" % (who, mid, stem, who, tail)
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
                if not isinstance(val, str) or "[is]" not in val:
                    continue
                new = convert(val)
                if new != val:
                    rec[field] = new
                    touched += 1
                    stats[name] += 1
                    if len(samples) < 6:
                        samples.append((rid, val.strip(), new.strip()))
        if touched and write:
            io.open(path, "w", encoding="utf-8", newline=LF).write(
                json.dumps(data, ensure_ascii=False, indent=2) + LF)
        if touched:
            print("  %-30s строк: %d" % (name, touched))
    print(("ПРИМЕНЕНО" if write else "предпросмотр") + ": всего", sum(stats.values()))
    for rid, old, new in samples:
        print("  ", rid)
        print("    было :", old[:90])
        print("    стало:", new[:90])


if __name__ == "__main__":
    main()
