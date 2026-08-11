# Orchestrator: запускает extract_prefab_paths_worker.py отдельным процессом
# на каждый файл ассетов, с общим таймаутом. Изоляция на уровне процесса — не
# потока — обязательна: отдельные объекты в некоторых файлах заставляют
# UnityPy уходить в аномально долгое чтение (see docs/baseline.md), и поток с
# join(timeout) не спасает, если "зависший" воркер держит GIL внутри
# native-кода C-расширения. Процесс можно убить безусловно.
#
# Каждый воркер пишет результат построчно (JSONL) и делает flush после каждой
# записи — при принудительном убийстве по таймауту теряется только хвост
# ЭТОГО ОДНОГО файла, а не весь накопленный прогресс.
import json, os, subprocess, sys, time

ROOT = r"F:\DEV2\ostra_i18n"
PY = sys.executable
WORKER = os.path.join(ROOT, "tools", "extract_prefab_paths_worker.py")
TMP_DIR = os.path.join(ROOT, "lang_src", "prefab_extract_tmp")
OUT = os.path.join(ROOT, "catalog", "prefabs.json")

TIMEOUT_SECONDS = int(os.environ.get("EXTRACT_TIMEOUT", "180"))

SCENE_FILES = ("level0", "level1", "level2", "level3", "level4")
ASSET_FILES = ("resources.assets", "sharedassets0.assets", "sharedassets1.assets",
               "sharedassets2.assets", "sharedassets3.assets", "sharedassets4.assets")

os.makedirs(TMP_DIR, exist_ok=True)

_only = os.environ.get("EXTRACT_ONLY")
jobs = []
for f in SCENE_FILES:
    if _only and f != _only:
        continue
    jobs.append((f, "scene"))
for f in ASSET_FILES:
    if _only and f != _only:
        continue
    jobs.append((f, "asset"))

timed_out_files = []
for fname, kind in jobs:
    out_jsonl = os.path.join(TMP_DIR, fname + ".jsonl")
    print(f"=== запускаю воркер: {fname} (таймаут {TIMEOUT_SECONDS}с) ===", flush=True)
    t0 = time.time()
    try:
        proc = subprocess.run(
            [PY, "-u", WORKER, fname, kind, out_jsonl],
            timeout=TIMEOUT_SECONDS, capture_output=True, text=True, encoding="utf-8",
        )
        print(proc.stdout, end="")
        if proc.stderr.strip():
            print("STDERR:", proc.stderr, file=sys.stderr)
        print(f"=== {fname}: завершён за {time.time()-t0:.1f}с, код {proc.returncode} ===", flush=True)
    except subprocess.TimeoutExpired as e:
        timed_out_files.append(fname)
        if e.stdout:
            print(e.stdout.decode("utf-8", "replace") if isinstance(e.stdout, bytes) else e.stdout, end="")
        print(f"=== {fname}: ПРЕВЫШЕН ТАЙМАУТ {TIMEOUT_SECONDS}с, использую частичный результат ===",
              flush=True)

# Сборка всех .jsonl в единый каталог, с глобальной дедупликацией ключей
# (воркеры дедуплицируют только внутри своего файла).
entries = []
seen_keys = set()
for fname, kind in jobs:
    out_jsonl = os.path.join(TMP_DIR, fname + ".jsonl")
    if not os.path.exists(out_jsonl):
        continue
    n = 0
    with open(out_jsonl, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            e = json.loads(line)
            key = e["key"]
            i = 1
            base = key
            while key in seen_keys:
                i += 1
                key = f"{base}_{i}"
            e["key"] = key
            seen_keys.add(key)
            entries.append(e)
            n += 1
    print(f"{fname}: собрано {n} записей из частичного файла")

os.makedirs(os.path.dirname(OUT), exist_ok=True)
json.dump(entries, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print("всего записей:", len(entries))
print("уникальных ключей:", len(seen_keys))
if timed_out_files:
    print("файлы с превышенным таймаутом (частичный результат):", timed_out_files)
print("записано:", OUT)
