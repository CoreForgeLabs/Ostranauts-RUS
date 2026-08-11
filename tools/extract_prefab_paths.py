# Orchestrator: для каждого файла ассетов сначала быстро считает число
# MonoBehaviour-объектов, затем нарезает их на чанки и запускает КАЖДЫЙ ЧАНК
# отдельным процессом с коротким таймаутом. Изоляция на уровне процесса ПО
# ЧАНКУ (не по файлу целиком) обязательна: отдельные объекты в некоторых
# файлах заставляют UnityPy уходить в аномально долгое чтение (мусорный
# счётчик элементов интерпретируется как валидный размер массива), и это
# оказалось не редкостью (~1 на 300-500 объектов), а не единичным случаем —
# при таймауте на весь файл терялся практически весь файл целиком. При
# таймауте на маленький чанк теряется только этот чанк.
#
# Каждый воркер пишет результат построчно (JSONL) со flush после каждой
# записи — при принудительном убийстве по таймауту теряется только хвост
# ЭТОГО ОДНОГО чанка.
import json, os, subprocess, sys, time

ROOT = r"F:\DEV2\ostra_i18n"
PY = sys.executable
WORKER = os.path.join(ROOT, "tools", "extract_prefab_paths_worker.py")
COUNTER = os.path.join(ROOT, "tools", "count_mono.py")
TMP_DIR = os.path.join(ROOT, "lang_src", "prefab_extract_tmp")
OUT = os.path.join(ROOT, "catalog", "prefabs.json")

CHUNK_SIZE = int(os.environ.get("EXTRACT_CHUNK", "200"))
CHUNK_TIMEOUT = int(os.environ.get("EXTRACT_TIMEOUT", "25"))
PER_FILE_BUDGET = int(os.environ.get("EXTRACT_FILE_BUDGET", "300"))  # секунд на файл суммарно

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

MIN_TIMEOUT = 3  # ниже — ложные таймауты просто от инициализации интерпретатора/библиотек
# "Плохие" объекты в некоторых файлах идут не единично, а зонами в десятки-сотни подряд
# (см. docs/baseline.md) — дробить их до размера 1 слишком дорого по количеству попыток.
# Ниже этого размера чанк просто пропускается целиком, теряя и немногие соседние
# "хорошие" объекты вместе с "плохими" — компромисс в пользу скорости, объём потерь мал
# относительно общего числа объектов в файле.
MIN_CHUNK_TO_SPLIT = 8

chunk_files = []  # (fname, out_jsonl) для финальной сборки
timed_out_singles = 0   # объекты, которые пришлось изолировать по одному и пропустить
total_chunks = 0
_chunk_counter = {}  # fname -> следующий свободный индекс имени файла


def next_out_path(fname):
    i = _chunk_counter.get(fname, 0)
    _chunk_counter[fname] = i + 1
    return os.path.join(TMP_DIR, f"{fname}.{i}.jsonl")


def process_range(fname, kind, start, end, deadline):
    # Пытается обработать [start,end) одним чанком; при таймауте делит диапазон
    # пополам и повторяет рекурсивно — так изолируются ровно "плохие" объекты
    # (в худшем случае по одному), а не весь диапазон целиком. Таймаут
    # пропорционален размеру чанка, с нижней границей, покрывающей overhead
    # запуска интерпретатора и инициализации TypeTreeGenerator.
    #
    # deadline — общий бюджет времени на ВЕСЬ файл: некоторые файлы содержат не
    # единичные "плохие" объекты, а целые зоны в сотни подряд (см. docs/baseline.md,
    # level2) — без верхнего предела дробление такой зоны съедает неограниченное
    # время. При превышении остаток диапазона пропускается без попытки чтения.
    global total_chunks, timed_out_singles
    size = end - start
    if time.time() > deadline:
        timed_out_singles += size
        print(f"  [{fname} {start}:{end}] общий бюджет времени файла исчерпан — "
              f"остаток пропущен без попытки ({size} объектов)", flush=True)
        return
    timeout = max(MIN_TIMEOUT, int(CHUNK_TIMEOUT * size / CHUNK_SIZE))
    out_jsonl = next_out_path(fname)
    total_chunks += 1
    t0 = time.time()
    try:
        proc = subprocess.run(
            [PY, "-u", WORKER, fname, kind, out_jsonl, str(start), str(end)],
            timeout=timeout, capture_output=True, text=True, encoding="utf-8",
        )
        if proc.returncode != 0 and proc.stderr.strip():
            print(f"  [{fname} {start}:{end}] STDERR: {proc.stderr.strip()[:200]}", flush=True)
        chunk_files.append((fname, out_jsonl))
        return
    except subprocess.TimeoutExpired:
        pass  # обрабатывается ниже — вне except, чтобы не наращивать глубину traceback при рекурсии

    if size <= MIN_CHUNK_TO_SPLIT:
        timed_out_singles += size
        print(f"  [{fname} {start}:{end}] не удалось за {timeout}с ({time.time()-t0:.1f}с), "
              f"диапазон мал для дальнейшего дробления — пропущен целиком ({size} объектов)",
              flush=True)
        return

    mid = start + size // 2
    print(f"  [{fname} {start}:{end}] таймаут {timeout}с ({time.time()-t0:.1f}с) — "
          f"делю на [{start}:{mid}) и [{mid}:{end})", flush=True)
    process_range(fname, kind, start, mid, deadline)
    process_range(fname, kind, mid, end, deadline)


for fname, kind in jobs:
    path_check = os.path.join(r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data", fname)
    if not os.path.exists(path_check):
        print(f"нет файла, пропуск: {fname}", flush=True)
        continue

    count_proc = subprocess.run([PY, COUNTER, fname], capture_output=True, text=True, encoding="utf-8")
    try:
        n = int(count_proc.stdout.strip())
    except ValueError:
        print(f"{fname}: не удалось посчитать объекты ({count_proc.stderr.strip()}), пропуск", flush=True)
        continue
    print(f"=== {fname}: {n} MonoBehaviour, чанков: {(n + CHUNK_SIZE - 1) // CHUNK_SIZE}, "
          f"бюджет {PER_FILE_BUDGET}с ===", flush=True)

    file_deadline = time.time() + PER_FILE_BUDGET
    start = 0
    while start < n:
        end = min(start + CHUNK_SIZE, n)
        process_range(fname, kind, start, end, file_deadline)
        start = end

    print(f"=== {fname}: обработано ===", flush=True)

# Сборка всех .jsonl в единый каталог, с глобальной дедупликацией ключей.
entries = []
seen_keys = set()
for fname, out_jsonl in chunk_files:
    if not os.path.exists(out_jsonl):
        continue
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

os.makedirs(os.path.dirname(OUT), exist_ok=True)
json.dump(entries, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print("всего записей:", len(entries))
print("уникальных ключей:", len(seen_keys))
print(f"попыток обработки чанков (с рекурсивным дроблением): {total_chunks}, "
      f"объектов пропущено как неустранимо зависающих: {timed_out_singles}")
print("записано:", OUT)
