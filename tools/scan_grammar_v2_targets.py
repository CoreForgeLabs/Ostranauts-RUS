#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
scan_grammar_v2_targets.py — Task 6.7 scope-measurement scanner.

Scans langs/ru/data/*.json for the SPECIFIC grammar-bug classes Task 6.7's
retranslation pass targets (see task-6.7-brief.md), using tools/fix_pos_case.py
(Phase 5) as style precedent for "scan for a grammar problem pattern, print
counts, don't guess volume."

Class A: [us-pos] immediately followed by a Russian word (adjective or noun)
whose ending indicates it is NOT masculine-singular (neuter/feminine/plural).
[us-pos] resolves to the FIXED nominative-masculine form "твой"/"мой"
(langs/ru/pack.json pronounCategories.pos) -- it never agrees with the
following noun's gender/number, so a non-masc-singular word right after it is
a live grammar bug (this is the "твой текущее действие" bug class verbatim).
NOTE: [them-pos]/[3rd-pos] are deliberately NOT scanned here -- they resolve
to "его"/"её"/"их" which are indeclinable borrowed-genitive possessives in
Russian (correct regardless of the following noun's gender), so there is no
equivalent bug for those aliases -- confirmed against langs/ru/pack.json.

Class B: [has] used in a "quality/trait" sense (should be [has.qual], Task
6.5) rather than "owns a physical object" sense. Heuristic: [has] followed
within a few words by a quality/trait/health noun (skills, vision, memory,
impairment, disorder, allergy, immunity, disposition, personality, health,
reputation, ...). This is a coarse recall-oriented filter -- Class B additionally
requires hand-classification (see report) because [has] is heavily overloaded
in this dataset for perfect-aspect "has done X" (877 raw occurrences of [has]
total; the quality-keyword filter narrows that to ~13-15 before hand-review).

Class C: per the brief, folded into Class A's definition for THIS project's
data -- "possessive from first mention" bugs that are actually live bugs (not
already-fine [them-pos]/[3rd-pos] invariant forms) are exactly the
[us-pos]-before-wrong-gender-noun pattern Class A already finds. No separate
scan needed; see report for reasoning.

Run: python scan_grammar_v2_targets.py
"""
import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RU_DATA = os.path.join(ROOT, "langs", "ru", "data")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from import_old_translation import (  # noqa: E402
    load_category, load_simple_category, is_simple, output_category_for, TRANSLATABLE,
)

CUR_DATA = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data"

CATEGORIES = ["interactions", "careers", "conditions", "pda_apps", "installables", "cooverlays",
              "condowners", "ledgerdefs", "pledges", "slots", "headlines", "plots",
              "market/CoCollections", "ads", "rooms", "jobitems", "racing/tracks", "context", "racing/leagues",
              "conditions_simple", "info", "market/Production", "tips"]

POS_RE = re.compile(r"\[us-pos\]\s*([А-Яа-яЁё]+)")

MASC_ADJ_END = ("ий", "ый", "ой")
NEUT_ADJ_END = ("ое", "ее")
FEM_ADJ_END = ("ая", "яя")
PL_ADJ_END = ("ые", "ие")

QUALITY_KW_RE = re.compile(
    r"\[has\]\s+((?:\w+\s+){0,3}(?:skills?|vision|memory|impairment|disorder|dependency|addiction|"
    r"craving|allerg\w*|intoleran\w*|toleran\w*|resistance|immunity|disposition|temperament|"
    r"personality|trait\w*|quirk\w*|habit\w*|health|hygiene|appearance|physique|build|charisma|"
    r"charm|reputation|standing|immune system))",
    re.I,
)

# Hand-reviewed false-positive exclusions from the raw QUALITY_KW_RE hits (see report):
# perfect-aspect "[has] done X" phrases that happen to contain a quality keyword later
# in the sentence, NOT an actual has.qual construction.
CLASS_B_EXCLUDE = {"RecentlyStudySkill", "ToldMemoryEarth", "HeardMemoryEarth"}


def classify_word(word):
    w = word.lower()
    if w.endswith(NEUT_ADJ_END):
        return "neut"
    if w.endswith(FEM_ADJ_END):
        return "fem"
    if w.endswith(PL_ADJ_END):
        return "pl"
    if w.endswith(MASC_ADJ_END):
        return "masc"
    if w.endswith(("ость", "знь", "пись")):
        return "fem"
    if w.endswith("мя"):
        return "neut"
    if w.endswith("о") or w.endswith("е"):
        return "neut"
    if w.endswith(("а", "я")):
        return "fem"
    if w.endswith(("ы", "и")):
        return "pl"
    return "masc_or_unknown"


def scan_class_a():
    hits = {}
    for fn in sorted(os.listdir(RU_DATA)):
        if not fn.endswith(".json"):
            continue
        data = json.loads(io.open(os.path.join(RU_DATA, fn), encoding="utf-8").read())
        for name, fields in data.items():
            for field, val in fields.items():
                if not isinstance(val, str):
                    continue
                for m in POS_RE.finditer(val):
                    cls = classify_word(m.group(1))
                    if cls in ("neut", "fem", "pl"):
                        hits[(fn, name, field)] = (val, m.group(1), cls)
    return hits


def scan_class_b():
    hits = {}
    for cat in CATEGORIES:
        cur = load_simple_category(CUR_DATA, cat) if is_simple(cat) else load_category(CUR_DATA, cat)
        out_cat = output_category_for(cat)
        fname = out_cat.replace("/", "_") + ".json"
        for name, obj in cur.items():
            if name in CLASS_B_EXCLUDE:
                continue
            for f in TRANSLATABLE:
                v = obj.get(f)
                if not v or not isinstance(v, str):
                    continue
                if QUALITY_KW_RE.search(v):
                    hits[(fname, name, f)] = v
    return hits


def main():
    a = scan_class_a()
    b = scan_class_b()
    union = set(a) | set(b)

    print("=== Class A: [us-pos] + non-masc-singular word ===")
    print("count:", len(a))
    by_file = {}
    for (fn, name, field) in a:
        by_file.setdefault(fn, 0)
        by_file[fn] += 1
    for fn, n in sorted(by_file.items(), key=lambda x: -x[1]):
        print("  %s: %d" % (fn, n))

    print()
    print("=== Class B: [has]-quality candidates (post hand-exclude) ===")
    print("count:", len(b))
    for (fn, name, field), v in sorted(b.items()):
        print("  %s / %s.%s -> %r" % (fn, name, field, v))

    print()
    print("=== Class C ===")
    print("folded into Class A per brief's own scope note (see module docstring)")

    print()
    print("=== TOTAL unique (A union B) ===")
    print(len(union))

    out_path = os.path.join(ROOT, "lang_src", "grammar_v2_candidates.json")
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    dump = {
        "class_a": [{"file": fn, "name": name, "field": field, "matched_word": w, "gender": g, "text": v}
                    for (fn, name, field), (v, w, g) in a.items()],
        "class_b": [{"file": fn, "name": name, "field": field, "text": v}
                    for (fn, name, field), v in b.items()],
    }
    with io.open(out_path, "w", encoding="utf-8") as f:
        json.dump(dump, f, ensure_ascii=False, indent=2)
    print("candidates written to", out_path)


if __name__ == "__main__":
    main()
