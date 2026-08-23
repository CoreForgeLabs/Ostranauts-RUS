# -*- coding: utf-8 -*-
"""
Два механических дефекта падежей и лица:

FROZEN_AUX: "[us] [is] собирается забрать [them]." -- [is] это связка, в русском
  настоящем она пустая, поэтому "собирается" осталось намертво в 3-м лице и
  выдавало "Ты собирается забрать огнетушитель.". Лечится токеном [is.aux],
  который спрягается по лицу.

POSSESSIVE_LOST: EN "[them]'s panel" -> RU "панель [them]" даёт "панель
  обогреватель". Токену нужен падеж. Какой именно -- решает предлог перед ним;
  без предлога это родительный.

  python tools/qa_fix_cases.py [--apply]
"""
import json, io, os, re, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EN_DIR = os.path.join(ROOT, "langs", "en", "data")
RU_DIR = os.path.join(ROOT, "langs", "ru", "data")
LF = chr(10)

# Предлоги с однозначным управлением. Многозначные ("с", "за", "под") не трогаем.
PREP_CASE = {
    "в": "prep", "во": "prep", "на": "prep", "о": "prep", "об": "prep",
    "обо": "prep", "при": "prep",
    "у": "gen", "от": "gen", "из": "gen", "для": "gen", "без": "gen",
    "до": "gen", "около": "gen", "возле": "gen", "против": "gen",
    "к": "dat", "ко": "dat", "по": "dat",
}
AUX_RE = re.compile(r"\[is\]\s+собира\w+\s+")

def load(p):
    with io.open(p, encoding="utf-8-sig") as f:
        return json.load(f)

def fix_aux(ru_val):
    if "[is]" not in ru_val or "собира" not in ru_val:
        return None
    new = AUX_RE.sub("[is.aux] ", ru_val)
    return new if new != ru_val else None

def fix_possessive(ru_val, en_val):
    """Возвращает исправленную строку либо None, если случай неоднозначный."""
    owners = set(re.findall(r"\[([^\[\]]+)\]'s", en_val))
    if not owners:
        return None
    new = ru_val
    for owner in owners:
        tok = "[%s]" % owner
        if new.count(tok) != 1:            # несколько вхождений -- не угадать
            return None
        if ("[%s-" % owner) in new:        # падеж уже проставлен где-то рядом
            continue
        idx = new.index(tok)
        before = new[:idx].rstrip()
        prev = re.split(r"[\s,.;:!?()\"]+", before)[-1].lower() if before else ""
        case = PREP_CASE.get(prev, "gen" if prev and not prev.startswith("[") else None)
        if case is None:
            return None
        new = new.replace(tok, "[%s-%s]" % (owner, case))
    return new if new != ru_val else None

def main():
    write = "--apply" in sys.argv
    stats = collections.Counter()
    samples = collections.defaultdict(list)
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
            for field, ru_val in list(rec.items()):
                if not isinstance(ru_val, str):
                    continue
                en_val = en_rec.get(field) if isinstance(en_rec, dict) else None
                new = fix_aux(ru_val)
                kind = "FROZEN_AUX"
                if new is None and isinstance(en_val, str):
                    new = fix_possessive(ru_val, en_val)
                    kind = "POSSESSIVE"
                if new is None:
                    continue
                stats[kind] += 1
                if len(samples[kind]) < 4:
                    samples[kind].append((rec_id, ru_val, new))
                rec[field] = new
                touched += 1
        if touched and write:
            io.open(ru_p, "w", encoding="utf-8", newline=LF).write(
                json.dumps(ru, ensure_ascii=False, indent=2) + LF)
        if touched:
            print("  %-34s строк: %d" % (name, touched))
    print(("ПРИМЕНЕНО" if write else "предпросмотр") + ":", dict(stats))
    for kind, rows in samples.items():
        print("\n%s:" % kind)
        for rec_id, old, new in rows:
            print("  ", rec_id)
            print("    было :", old[:110])
            print("    стало:", new[:110])

if __name__ == "__main__":
    main()
