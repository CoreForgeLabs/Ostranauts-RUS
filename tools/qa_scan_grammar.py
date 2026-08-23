# -*- coding: utf-8 -*-
"""
QA-скан качества RU-перевода по шаблонам взаимодействий.

Шаблон -- это не текст, а программа: [us]/[them] подставляют сущность в нужном
падеже, [verb] спрягается по лицу и роду через verbs.json. Поэтому дефект
качества здесь -- это не "коряво звучит", а "переводчик выкинул токен и вписал
русское слово намертво", после чего строка перестаёт согласовываться.

Классы (по убыванию тяжести):
  FROZEN_AUX   -- [is] + вписанное "собирается": лицо застыло в 3-м ("Ты собирается")
  FROZEN_VERB  -- глагольный токен EN потерян, вместо него намертво вписан глагол
  BAD_TOKEN    -- в RU выдуман токен, которого движок не знает -> в игру уйдёт как есть
  POSSESSIVE_LOST -- EN "[X]'s" -> RU оставил [X] в именительном ("панель обогреватель")
  POS_TOKEN    -- [*-pos] даёт "твой/его" без согласования с предметом обладания
  UNTRANSLATED -- в строке есть переводимый текст, но она совпала с оригиналом

Выход: docs/qa_grammar_report.json + сводка в stdout.
"""
import json, io, os, re, sys, collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EN_DIR = os.path.join(ROOT, "langs", "en", "data")
RU_DIR = os.path.join(ROOT, "langs", "ru", "data")
VERBS = os.path.join(ROOT, "langs", "ru", "verbs.json")
OUT = os.path.join(ROOT, "docs", "qa_grammar_report.json")

TOKEN_RE = re.compile(r"\[([^\[\]]+)\]")
CYR_RE = re.compile(r"[А-Яа-яЁё]")
# Спрягаемое русское слово, вписанное намертво вместо токена.
RU_VERB3_RE = re.compile(r"\b\w+(?:ет|ёт|ит|ает|яет|ует|ются|ется|ается|аются)\b")
AUX_FROZEN_RE = re.compile(r"\bсобира(?:ется|ются|ешься|юсь|емся|етесь)\b")

def load(p):
    with io.open(p, encoding="utf-8-sig") as f:
        return json.load(f)

VERB_KEYS = set(k for k in load(VERBS) if not k.startswith("_"))

def _harvest_en_tokens():
    """Словарь валидных токенов берём из английского корпуса, а не из головы:
    что игра печатает по-английски, то движок и умеет резолвить."""
    seen = set()
    for n in os.listdir(EN_DIR):
        if not n.endswith(".json"):
            continue
        try:
            data = load(os.path.join(EN_DIR, n))
        except Exception:
            continue
        for _id, _f, val in str_fields(data):
            seen.update(TOKEN_RE.findall(val))
    return seen

EN_TOKENS = None  # заполняется в main(), после объявления str_fields

# Суффиксы токенов, которые умеет мод. Берём их из pack.json, а не из списка в
# коде: категории объявляются там, и сканер обязан узнавать о новых оттуда же,
# иначе он объявит ошибкой то, что сам же язык-пак и добавил.
def _mod_suffixes():
    base = {"gen", "dat", "acc", "ins", "prep", "subj", "obj", "pos"}
    try:
        pack = load(os.path.join(ROOT, "langs", "ru", "pack.json"))
        base.update(pack.get("pronounCategories", {}).keys())
    except Exception:
        pass
    return tuple(base)

MOD_SUFFIXES = _mod_suffixes()

def known_token(t):
    if t in EN_TOKENS or t in VERB_KEYS or t.split(".")[0] in VERB_KEYS:
        return True
    # [them-gen] и подобные: базу знаем из EN, падеж добавлен модом.
    if "-" in t:
        base, suffix = t.rsplit("-", 1)
        if suffix in MOD_SUFFIXES and (base in EN_TOKENS or any(
                x == base or x.startswith(base + "-") for x in EN_TOKENS)):
            return True
    return False

def str_fields(obj):
    for rec_id, rec in obj.items():
        if isinstance(rec, dict):
            for field, val in rec.items():
                if isinstance(val, str) and val.strip():
                    yield rec_id, field, val

def strip_tokens(s):
    return TOKEN_RE.sub(" ", s)

def scan_file(name):
    en_p, ru_p = os.path.join(EN_DIR, name), os.path.join(RU_DIR, name)
    if not (os.path.exists(en_p) and os.path.exists(ru_p)):
        return []
    en, ru = load(en_p), load(ru_p)
    out = []
    for rec_id, field, ru_val in str_fields(ru):
        en_rec = en.get(rec_id)
        en_val = en_rec.get(field) if isinstance(en_rec, dict) else None
        if not isinstance(en_val, str) or not en_val.strip():
            continue
        en_tokens, ru_tokens = TOKEN_RE.findall(en_val), TOKEN_RE.findall(ru_val)

        def add(kind, note):
            out.append({"file": name, "id": rec_id, "field": field, "kind": kind,
                        "note": note, "en": en_val, "ru": ru_val})

        # 1. [is] + намертво вписанное "собирается" -- лицо застыло в 3-м.
        if "is" in ru_tokens and AUX_FROZEN_RE.search(ru_val):
            add("FROZEN_AUX", "[is] + вписанное 'собирается' -> нужен токен [is.aux]")

        # 2. Глагольный токен EN потерян, а в RU на его месте спрягаемый глагол.
        lost_verbs = [t for t in en_tokens
                      if t in VERB_KEYS and t not in ru_tokens]
        if lost_verbs and RU_VERB3_RE.search(strip_tokens(ru_val)):
            add("FROZEN_VERB", "потерян глагольный токен %s; глагол вписан текстом"
                % ", ".join("[%s]" % t for t in sorted(set(lost_verbs))[:3]))

        # 3. Выдуманные токены: движок их не резолвит, уйдут в игру как есть.
        bad = [t for t in ru_tokens if not known_token(t)]
        if bad:
            add("BAD_TOKEN", "движок не знает: %s"
                % ", ".join("[%s]" % t for t in sorted(set(bad))[:3]))

        # 4. EN "[X]'s" -> RU обязан ставить [X-gen].
        for owner in set(re.findall(r"\[([^\[\]]+)\]'s", en_val)):
            if owner in ru_tokens and (owner + "-gen") not in ru_tokens:
                add("POSSESSIVE_LOST",
                    "EN [%s]'s -> RU [%s] в именительном; нужен [%s-gen]" % (owner, owner, owner))

        # 5. Притяжательное местоимение без согласования с предметом обладания.
        if any(t.endswith("-pos") for t in ru_tokens):
            add("POS_TOKEN", "[*-pos] -> 'твой/его' не согласуется; нужен 'свой' в нужном роде")

        # 6. Строка не переведена -- но только если в ней есть что переводить
        #    помимо токенов и служебных идентификаторов.
        if ru_val.strip() == en_val.strip():
            payload = strip_tokens(en_val)
            if re.search(r"[A-Za-z]{3,}", payload) and " " in payload.strip():
                add("UNTRANSLATED", "совпадает с оригиналом, есть непереведённый текст")
    return out

def main():
    global EN_TOKENS
    EN_TOKENS = _harvest_en_tokens()
    print("валидных токенов в EN-корпусе:", len(EN_TOKENS))
    names = sys.argv[1:] or sorted(os.listdir(RU_DIR))
    all_f = []
    for n in names:
        if n.endswith(".json") and not n.endswith("_translated.json"):
            all_f.extend(scan_file(n))
    by_kind = collections.Counter(f["kind"] for f in all_f)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with io.open(OUT, "w", encoding="utf-8") as f:
        json.dump({"summary": dict(by_kind), "findings": all_f}, f, ensure_ascii=False, indent=2)
    print("находок:", len(all_f))
    order = ["FROZEN_AUX", "FROZEN_VERB", "BAD_TOKEN", "POSSESSIVE_LOST", "POS_TOKEN", "UNTRANSLATED"]
    for k in order:
        if by_kind.get(k):
            print("  %-16s %5d" % (k, by_kind[k]))
    print("\nотчёт:", OUT)

if __name__ == "__main__":
    main()
