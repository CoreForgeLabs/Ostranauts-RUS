# -*- coding: utf-8 -*-
"""
Отдаёт спорные находки qa_scan_grammar.py на разбор Qwen и DeepSeek сразу.

Модели тут не переводят с нуля -- они чинят шаблон, в котором сломана грамматика.
Поэтому в промпте главное не стиль, а устройство токенов: что [us] склоняется,
что [says] спрягается, и что выдумывать токены нельзя.

Каждое предложение проходит машинную валидацию до того, как попадёт в отчёт.

  python tools/qa_review_llm.py POS_TOKEN --limit 60
  python tools/qa_review_llm.py FROZEN_VERB BAD_TOKEN
  python tools/qa_review_llm.py --apply
"""
import json, io, os, re, sys, glob, collections, concurrent.futures, threading

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
os.environ.setdefault("LLM_REQUEST_TIMEOUT", "500")

from llm_client import chat_json
import qa_scan_grammar as qa
from qa_scan_grammar import TOKEN_RE, load

REPORT = os.path.join(ROOT, "docs", "qa_grammar_report.json")
OUT = os.path.join(ROOT, "docs", "qa_llm_proposals.json")
RU_DIR = os.path.join(ROOT, "langs", "ru", "data")
LF = chr(10)
CKPT_DIR = os.path.join(ROOT, "docs")
CKPT_GLOB = os.path.join(CKPT_DIR, "qa_llm_checkpoint*.jsonl")
CKPT = os.path.join(CKPT_DIR, "qa_llm_checkpoint.jsonl")
BATCH = 6
# Потолок прокси: DeepSeek держит 35 соединений, Qwen до 150. DeepSeek и есть
# узкое место, поэтому при нескольких процессах делить надо именно его.
QWEN_WORKERS, DS_WORKERS = 150, 35
_lock = threading.Lock()

SYSTEM = """Ты — редактор русской локализации космического симулятора Ostranauts.

Тебе дают НЕ обычный текст, а ШАБЛОН строки. В нём есть токены в квадратных
скобках, которые движок подставляет во время игры:

  [us], [them]  — участник сцены; движок склоняет его сам
  [us-gen], [us-dat], [us-acc], [us-ins], [us-prep] — тот же участник в падеже
  [us-subj], [us-obj] — местоимение как подлежащее/дополнение
  [us-pos]  — притяжательное местоимение, даёт «твой/его» БЕЗ согласования
  [says], [asks], [is], [has], [is.aux] — ГЛАГОЛ; движок спрягает его сам
    по лицу, роду и числу

ЖЕЛЕЗНЫЕ ПРАВИЛА:
1. Не выдумывай новые токены. Разрешены только те, что есть в английском
   оригинале, плюс падежные формы [x-gen], [x-dat], [x-acc], [x-ins],
   [x-prep] и вспомогательный [is.aux].
2. Не переводи имя токена. [says] остаётся [says], а не [говорит].
3. Не вписывай глагол словами, если в оригинале там стоит токен — иначе фраза
   застынет в третьем лице и выйдет «Ты собирается».
4. Не выбрасывай участников: если в оригинале есть [them], он должен остаться.
5. В ответе не должно быть латиницы вне токенов.

ЧТО ЧИНИТЬ:
- POS_TOKEN: [us-pos] раскрывается в «твой» и не согласуется с предметом
  обладания.
  «Свой» указывает на подлежащее ТОЙ КЛАУЗЫ, в которой стоит. Проверяй
  подлежащее ближайшего глагола, а не всего предложения.
  ДА:   «[us] чистит [us-pos] зубы» -> «[us] чистит свои зубы»
        (подлежащее клаузы — сам участник)
  НЕТ:  «[us] [feels], что кислотный воздух обжигает [us-pos] глаза»
        -> НЕЛЬЗЯ «обжигает свои глаза»: подлежащее этой клаузы — «воздух»,
        и выйдет, что глаза принадлежат воздуху. Здесь по-русски притяжательное
        вообще не нужно: «обжигает глаза и горло».
  НЕТ:  «[them] чистит [us-pos] зубы» — обладатель не подлежащее, оставь [us-pos].
  Итог: либо «свой» в нужном роде/числе/падеже, либо убери притяжательное
  совсем, если русскому оно не нужно, либо оставь [us-pos] без изменений.
- FROZEN_VERB: глагол вписан словами вместо токена. Верни токен из оригинала
  и перестрой фразу так, чтобы она звучала по-русски.
- BAD_TOKEN: токен, которого движок не знает. Обычно перевод склеил связку
  [is] с дополнением: «[получает повреждения]» из «[is] damaged». Разбери
  обратно — токен отдельно, дополнение словами.
- POSSESSIVE_LOST: в оригинале притяжательная форма, в переводе токен остался
  без падежа.
- UNTRANSLATED: строка не переведена.

ОТВЕТ: строго JSON-массив, по объекту на задачу, тот же порядок и id:
[{"id": "<id>", "ru": "<исправленный шаблон>", "why": "<что было не так>"}]
Если строка на самом деле в порядке — верни её без изменений и why: "ok".
Никакого текста вне JSON."""


def make_task(f):
    return {"id": f["id"], "kind": f["kind"], "problem": f["note"],
            "en": f["en"], "ru": f["ru"]}


def validate(f, ru_new):
    """Машинная проверка предложения модели до попадания в отчёт."""
    if not isinstance(ru_new, str) or not ru_new.strip():
        return "пустой ответ"
    toks = TOKEN_RE.findall(ru_new)
    unknown = [t for t in toks if not qa.known_token(t)]
    if unknown:
        return "выдуманные токены: " + ", ".join("[%s]" % t for t in unknown[:3])
    def ents(s):
        return set(t.split("-")[0] for t in TOKEN_RE.findall(s)
                   if t.split("-")[0] in ("us", "them"))
    lost = ents(f["en"]) - ents(ru_new)
    if lost:
        return "потерян участник: " + ", ".join(sorted(lost))
    if re.search(r"[A-Za-z]{3,}", TOKEN_RE.sub(" ", ru_new)):
        return "латиница вне токенов"
    err = check_svoy(ru_new)
    if err:
        return err
    return None


SVOY_RE = re.compile(r"\bсво(?:й|я|ё|е|и|его|ей|ему|им|их|ими|ю|ём|ем)\b", re.I)
# Подчинительные союзы: за ними начинается новая клауза с собственным
# подлежащим, и "свой" после них уже указывает на него, а не на участника.
CLAUSE_RE = re.compile(r"\b(что|чтобы|который|которая|которое|которые|которых|"
                       r"когда|пока|если|поскольку|потому)\b", re.I)


def check_svoy(ru_new):
    """Модели уверенно ставят «свой» и там, где подлежащее клаузы другое:
    «воздух обжигает свои глаза» -- это глаза воздуха. Машинно определить
    подлежащее нельзя, поэтому режем консервативно: «свой» после
    подчинительного союза не принимаем без человека."""
    m = SVOY_RE.search(ru_new)
    if not m:
        return None
    if CLAUSE_RE.search(ru_new[:m.start()]):
        return "«свой» стоит в придаточном: подлежащее клаузы может быть не участник"
    return None


def ask(model, tasks):
    try:
        return chat_json(SYSTEM, tasks, model=model, temperature=0.1, max_tokens=8000)
    except Exception as e:
        with _lock:
            print("  %s: сбой батча: %s" % (model, str(e)[:90]))
        return None


def index(resp):
    out = {}
    if isinstance(resp, list):
        for r in resp:
            if isinstance(r, dict) and "id" in r:
                out[r["id"]] = r
    return out


def load_done():
    """Уже разобранные строки из ВСЕХ чекпойнтов: процессы читают общую картину,
    но пишет каждый в свой файл, чтобы строки не перемешались на диске."""
    done, rows = set(), []
    for path in sorted(glob.glob(CKPT_GLOB)):
        for line in io.open(path, encoding="utf-8"):
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except Exception:
                continue
            done.add((r["file"], r["id"], r["field"]))
            if r.get("ru_new"):
                rows.append(r)
    return done, rows


def checkpoint(fh, rec):
    fh.write(json.dumps(rec, ensure_ascii=False) + LF)
    fh.flush()
    os.fsync(fh.fileno())


def review(findings):
    batches = [findings[i:i + BATCH] for i in range(0, len(findings), BATCH)]
    print("батчей: %d (по %d задач)" % (len(batches), BATCH))
    proposals, stats = [], collections.Counter()
    print("воркеров: qwen=%d, deepseek=%d" % (QWEN_WORKERS, DS_WORKERS))
    fh = io.open(CKPT, "a", encoding="utf-8")
    qx = concurrent.futures.ThreadPoolExecutor(max_workers=QWEN_WORKERS)
    dx = concurrent.futures.ThreadPoolExecutor(max_workers=DS_WORKERS)
    futures = []
    for b in batches:
        tasks = [make_task(f) for f in b]
        futures.append((b, qx.submit(ask, "qwen-max", tasks),
                        dx.submit(ask, "deepseek", tasks)))
    for n, (b, fq, fd) in enumerate(futures, 1):
        q, d = index(fq.result()), index(fd.result())
        for f in b:
            rid = f["id"]
            cand = []
            for model, r in (("qwen", q.get(rid)), ("deepseek", d.get(rid))):
                if not r:
                    continue
                err = validate(f, r.get("ru"))
                if err:
                    stats["отбраковано:" + model] += 1
                    continue
                cand.append((model, r["ru"].strip(), (r.get("why") or "").strip()))
            if not cand:
                stats["без валидного ответа"] += 1
                checkpoint(fh, {"file": f["file"], "id": rid, "field": f["field"],
                                "ru_new": None, "skip": "нет валидного ответа"})
                continue
            texts = set(c[1] for c in cand)
            if all(t == f["ru"].strip() for t in texts):
                stats["модели: без изменений"] += 1
                checkpoint(fh, {"file": f["file"], "id": rid, "field": f["field"],
                                "ru_new": None, "skip": "изменений не нужно"})
                continue
            agree = len(texts) == 1 and len(cand) == 2
            stats["согласие" if agree else "расхождение"] += 1
            rec = {"file": f["file"], "id": rid, "field": f["field"], "kind": f["kind"],
                   "en": f["en"], "ru_old": f["ru"], "ru_new": cand[0][1],
                   "agree": agree, "variants": {m: t for m, t, _ in cand},
                   "why": cand[0][2]}
            proposals.append(rec)
            checkpoint(fh, rec)
        print("  батч %d/%d готов" % (n, len(batches)))
    qx.shutdown()
    dx.shutdown()
    fh.close()
    return proposals, stats


VERB_KEYS_RU = set(k for k in load(os.path.join(ROOT, "langs", "ru", "verbs.json"))
                   if not k.startswith("_"))
PRONOUN_CATS = ("subj", "pos", "obj", "gen", "reflexive", "contractIs",
                "contractHas", "contractWill", "contractWould",
                "dat", "acc", "ins", "prep")


def introduces_dead_token(p):
    """Токен может быть валидным по имени, но не иметь парадигмы в verbs.json --
    тогда мод напечатает английское слово посреди русской фразы. Проверяем
    только то, что правка ДОБАВИЛА."""
    added = set(TOK_ALL(p["ru_new"])) - set(TOK_ALL(p["ru_old"]))
    for t in added:
        if t in ("us", "them", "is.aux"):
            continue
        if "-" in t and t.rsplit("-", 1)[1] in PRONOUN_CATS:
            continue
        if t in VERB_KEYS_RU or t.split(".")[0] in VERB_KEYS_RU:
            continue
        return t
    return None


def TOK_ALL(s):
    return TOKEN_RE.findall(s)


def apply_proposals(only_agreed=True):
    data = load(OUT)
    props = data["proposals"] if isinstance(data, dict) else data
    by_file = collections.defaultdict(list)
    for p in props:
        if only_agreed and not p.get("agree"):
            continue
        by_file[p["file"]].append(p)
    total, skipped = 0, []
    for name, items in sorted(by_file.items()):
        path = os.path.join(RU_DIR, name)
        ru = load(path)
        n = 0
        for p in items:
            dead = introduces_dead_token(p)
            if dead:
                skipped.append((p["id"], dead))
                continue
            rec = ru.get(p["id"])
            if isinstance(rec, dict) and rec.get(p["field"]) == p["ru_old"]:
                rec[p["field"]] = p["ru_new"]
                n += 1
        if n:
            io.open(path, "w", encoding="utf-8", newline=LF).write(
                json.dumps(ru, ensure_ascii=False, indent=2) + LF)
            print("  %-30s применено: %d" % (name, n))
            total += n
    print("всего применено:", total)
    if skipped:
        print("пропущено (вводят токен без парадигмы): %d" % len(skipped))
        for rid, t in skipped[:10]:
            print("   %s -> [%s]" % (rid, t))


def main():
    qa.EN_TOKENS = qa._harvest_en_tokens()
    argv = sys.argv[1:]
    global QWEN_WORKERS, DS_WORKERS, CKPT, OUT
    kinds = [a for a in argv if not a.startswith("--")]
    limit = None
    for a in argv:
        if a.startswith("--limit="):
            limit = int(a.split("=", 1)[1])
        if a.startswith("--qwen="):
            QWEN_WORKERS = int(a.split("=", 1)[1])
        if a.startswith("--ds="):
            DS_WORKERS = int(a.split("=", 1)[1])
        if a.startswith("--ckpt="):
            CKPT = os.path.join(CKPT_DIR,
                                "qa_llm_checkpoint_%s.jsonl" % a.split("=", 1)[1])
            OUT = os.path.join(CKPT_DIR,
                               "qa_llm_proposals_%s.json" % a.split("=", 1)[1])
    if "--apply" in argv:
        apply_proposals(only_agreed="--all" not in argv)
        return
    report = load(REPORT)
    findings = report["findings"]
    if kinds:
        findings = [f for f in findings if f["kind"] in kinds]
    done, carried = load_done()
    seen, uniq = set(), []
    for f in findings:
        key = (f["file"], f["id"], f["field"])
        if key in seen or key in done:
            continue
        seen.add(key)
        uniq.append(f)
    if done:
        print("уже разобрано ранее: %d (перенесено готовых правок: %d)"
              % (len(done), len(carried)))
    if "--reverse" in argv:
        uniq.reverse()
        print("порядок: с конца")
    if limit:
        uniq = uniq[:limit]
    print("на разбор: %d строк, классы: %s" % (len(uniq), kinds or "все"))
    proposals, stats = review(uniq)
    proposals = carried + proposals
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with io.open(OUT, "w", encoding="utf-8") as f:
        json.dump({"stats": dict(stats), "proposals": proposals}, f,
                  ensure_ascii=False, indent=2)
    print("")
    for k, v in stats.most_common():
        print("  %-26s %d" % (k, v))
    print("")
    print("предложений: %d (согласованных: %d)"
          % (len(proposals), sum(1 for p in proposals if p["agree"])))
    print("отчёт:", OUT)


if __name__ == "__main__":
    main()
