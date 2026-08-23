# -*- coding: utf-8 -*-
"""
Арбитраж расхождений: где Qwen и DeepSeek предложили разное, судьёй выступает
Qwen -- ему показывают оригинал, текущий перевод и оба варианта.

Судья не обязан выбирать из двух. Часто оба варианта плохи по одной и той же
причине, поэтому третий ответ -- "написать свой" -- разрешён явно, как и
четвёртый: "оставить как есть". Иначе арбитраж превращается в лотерею между
двумя ошибками.

Всё, что вернёт судья, проходит ту же машинную валидацию, что и обычные
предложения: выдуманные токены, потерянные участники, латиница, "свой" в
придаточном.

  python tools/qa_arbitrate_llm.py [--limit=N] [--qwen=N] [--apply]
"""
import json, io, os, re, sys, glob, collections, concurrent.futures, threading

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
os.environ.setdefault("LLM_REQUEST_TIMEOUT", "500")

from llm_client import chat_json
import qa_scan_grammar as qa
import qa_review_llm as rv
from qa_scan_grammar import load, TOKEN_RE

CKPT = os.path.join(ROOT, "docs", "qa_arbitration.jsonl")
OUT = os.path.join(ROOT, "docs", "qa_arbitration.json")
RU_DIR = os.path.join(ROOT, "langs", "ru", "data")
LF = chr(10)
BATCH = 6
WORKERS = 150
_lock = threading.Lock()

SYSTEM = rv.SYSTEM + """

СЕЙЧАС ТЫ СУДЬЯ. Два переводчика предложили разное для одной строки. Тебе дают
оригинал (en), текущий перевод (ru_old) и два варианта (a и b).

Выбери лучший ПО СМЫСЛУ И ГРАММАТИКЕ, а не по длине и не по красивости.
Ты НЕ обязан брать один из двух:
 - если оба варианта ошибочны одинаково, напиши свой правильный;
 - если текущий перевод на самом деле лучше обоих, верни его без изменений.

Особенно проверяй: не потерян ли токен, не выдуман ли новый, согласуется ли
"свой" с подлежащим ИМЕННО СВОЕЙ клаузы.

ОТВЕТ: строго JSON-массив, тот же порядок и id:
[{"id": "<id>", "ru": "<итоговый шаблон>", "pick": "a|b|own|keep", "why": "<коротко>"}]
Никакого текста вне JSON."""


def load_disagreements():
    """Расхождения собираем из всех чекпойнтов разбора."""
    rows, seen = [], set()
    for path in sorted(glob.glob(os.path.join(ROOT, "docs", "qa_llm_checkpoint*.jsonl"))):
        for line in io.open(path, encoding="utf-8"):
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except Exception:
                continue
            if r.get("agree") or not r.get("ru_new"):
                continue
            variants = r.get("variants") or {}
            if len(variants) < 1:
                continue
            if len(variants) == 1:
                # Второй ответ отбраковала валидация. Такая строка иначе зависает
                # навсегда: согласия нет и сравнивать не с чем -- поэтому вторым
                # вариантом ставим текущий перевод, и судья решает, лучше ли
                # предложение того, что уже есть.
                only = list(variants.values())[0]
                variants = {"предложение": only, "текущий": r["ru_old"]}
                r = dict(r, variants=variants)
            key = (r["file"], r["id"], r["field"])
            if key in seen:
                continue
            seen.add(key)
            rows.append(r)
    return rows


def load_judged():
    done = set()
    if os.path.exists(CKPT):
        for line in io.open(CKPT, encoding="utf-8"):
            try:
                r = json.loads(line)
            except Exception:
                continue
            done.add((r["file"], r["id"], r["field"]))
    return done


def make_task(r):
    v = list((r.get("variants") or {}).items())
    return {"id": r["id"], "kind": r["kind"], "en": r["en"], "ru_old": r["ru_old"],
            "a": v[0][1], "b": v[1][1]}


def ask(tasks):
    try:
        return chat_json(SYSTEM, tasks, model="qwen-max", temperature=0.1, max_tokens=8000)
    except Exception as e:
        with _lock:
            print("  сбой батча: %s" % str(e)[:90])
        return None


def judge(rows):
    batches = [rows[i:i + BATCH] for i in range(0, len(rows), BATCH)]
    print("батчей: %d (по %d), воркеров: %d" % (len(batches), BATCH, WORKERS))
    fh = io.open(CKPT, "a", encoding="utf-8")
    ex = concurrent.futures.ThreadPoolExecutor(max_workers=WORKERS)
    futures = [(b, ex.submit(ask, [make_task(r) for r in b])) for b in batches]
    stats, verdicts = collections.Counter(), []
    for n, (b, fut) in enumerate(futures, 1):
        resp = rv.index(fut.result())
        for r in b:
            ans = resp.get(r["id"])
            if not ans:
                stats["без ответа судьи"] += 1
                continue
            ru_new = (ans.get("ru") or "").strip()
            err = rv.validate({"en": r["en"]}, ru_new)
            if err:
                stats["отбраковано: " + err.split(":")[0]] += 1
                continue
            if ru_new == r["ru_old"].strip():
                stats["судья: оставить как есть"] += 1
                rec = {"file": r["file"], "id": r["id"], "field": r["field"],
                       "ru_new": None, "pick": "keep"}
                fh.write(json.dumps(rec, ensure_ascii=False) + LF)
                fh.flush()
                continue
            pick = ans.get("pick") or "?"
            stats["выбрано: " + pick] += 1
            rec = {"file": r["file"], "id": r["id"], "field": r["field"],
                   "kind": r["kind"], "en": r["en"], "ru_old": r["ru_old"],
                   "ru_new": ru_new, "pick": pick, "why": (ans.get("why") or "")[:200]}
            verdicts.append(rec)
            fh.write(json.dumps(rec, ensure_ascii=False) + LF)
            fh.flush()
        print("  батч %d/%d" % (n, len(batches)))
    ex.shutdown()
    fh.close()
    return verdicts, stats


VERBS_RU = load(os.path.join(ROOT, "langs", "ru", "verbs.json"))
# Токены, чья русская форма -- служебное слово, а не сказуемое: "[doesn't]"
# раскрывается в голое "не". Строка, где такой токен остался единственным
# "глаголом", читается как "Ты ничего не." -- глагол потерян.
PARTICLES = {"не", "ни", "бы"}
RU_FINITE = re.compile(r"[А-Яа-яЁё]+(?:ю|у|ешь|ёшь|ишь|ет|ёт|ит|ем|ём|им|ете|ёте|ите|ют|ут|ят|ат|"
                       r"л|ла|ло|ли|лся|лась|лись)")


def has_predicate(text):
    """Есть ли в шаблоне хоть какое-то сказуемое."""
    for t in rv.TOK_ALL(text):
        vf = VERBS_RU.get(t) or VERBS_RU.get(t.split(".")[0])
        if not isinstance(vf, dict):
            continue
        if vf.get("kind") == "copula" or vf.get("omitPresent"):
            return True          # именное сказуемое: "[us] [is] поврежден"
        pres = vf.get("present") or []
        form = pres[2] if len(pres) > 2 else (pres[0] if pres else "")
        if form and form not in PARTICLES:
            return True
    return bool(RU_FINITE.search(rv.TOKEN_RE.sub(" ", text)))


def has_verb_token(text):
    for t in rv.TOK_ALL(text):
        vf = VERBS_RU.get(t) or VERBS_RU.get(t.split(".")[0])
        if isinstance(vf, dict):
            pres = vf.get("present") or []
            form = pres[2] if len(pres) > 2 else ""
            if vf.get("kind") == "copula" or (form and form not in PARTICLES):
                return True
    return False


def apply_verdicts():
    rows = []
    for line in io.open(CKPT, encoding="utf-8"):
        try:
            r = json.loads(line)
        except Exception:
            continue
        if r.get("ru_new"):
            rows.append(r)
    by_file = collections.defaultdict(list)
    for r in rows:
        by_file[r["file"]].append(r)
    total, skipped = 0, []
    for name, items in sorted(by_file.items()):
        path = os.path.join(RU_DIR, name)
        if not os.path.exists(path):
            continue
        data = load(path)
        n = 0
        for r in items:
            dead = rv.introduces_dead_token(r)
            if dead:
                skipped.append((r["id"], "токен без парадигмы: " + dead))
                continue
            if has_predicate(r["ru_old"]) and not has_predicate(r["ru_new"]):
                skipped.append((r["id"], "потеряно сказуемое"))
                continue
            # Если в оригинале глагол стоит токеном, он обязан остаться токеном:
            # вписанный словами глагол снова застынет в третьем лице.
            if has_verb_token(r["en"]) and not has_verb_token(r["ru_new"]):
                skipped.append((r["id"], "глагол вписан текстом вместо токена"))
                continue
            rec = data.get(r["id"])
            if isinstance(rec, dict) and rec.get(r["field"]) == r["ru_old"]:
                rec[r["field"]] = r["ru_new"]
                n += 1
        if n:
            io.open(path, "w", encoding="utf-8", newline=LF).write(
                json.dumps(data, ensure_ascii=False, indent=2) + LF)
            print("  %-30s применено: %d" % (name, n))
            total += n
    print("всего применено:", total)
    if skipped:
        print("пропущено: %d" % len(skipped))
        for rid, t in skipped[:10]:
            print("   %s -- %s" % (rid, t))


def main():
    global WORKERS
    qa.EN_TOKENS = qa._harvest_en_tokens()
    argv = sys.argv[1:]
    for a in argv:
        if a.startswith("--qwen="):
            WORKERS = int(a.split("=", 1)[1])
    if "--apply" in argv:
        apply_verdicts()
        return
    rows = load_disagreements()
    done = load_judged()
    rows = [r for r in rows if (r["file"], r["id"], r["field"]) not in done]
    for a in argv:
        if a.startswith("--limit="):
            rows = rows[:int(a.split("=", 1)[1])]
    print("на арбитраж: %d расхождений" % len(rows))
    if not rows:
        return
    verdicts, stats = judge(rows)
    with io.open(OUT, "w", encoding="utf-8") as f:
        json.dump({"stats": dict(stats), "verdicts": verdicts}, f,
                  ensure_ascii=False, indent=2)
    print("")
    for k, v in stats.most_common():
        print("  %-30s %d" % (k, v))
    print("")
    print("вердиктов с правкой: %d" % len(verdicts))


if __name__ == "__main__":
    main()
