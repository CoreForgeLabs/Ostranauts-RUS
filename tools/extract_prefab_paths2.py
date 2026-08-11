# Оркестратор v2 — доизвлечение пропущенных ранее объектов (в первую очередь
# resources.assets, где ~17155 объектов были пропущены как "зависающие" из-за
# катастрофического замедления UnityPy на отдельных типах MonoBehaviour, см.
# docs/baseline.md). Отличия от v1 (extract_prefab_paths.py):
#
# 1. ПАРАЛЛЕЛЬНАЯ обработка чанков верхнего уровня (ThreadPoolExecutor) —
#    v1 запускала subprocess.run строго последовательно, что и было главной
#    причиной долгого прогона; параллелизм по чанкам безопасен, т.к. каждый
#    воркер — отдельный процесс, читающий один и тот же файл только на чтение.
# 2. Пишет в ОТДЕЛЬНЫЙ выходной файл (не catalog/prefabs.json напрямую) —
#    чтобы не затронуть уже утверждённые вручную записи текущего каталога.
#    Слияние — отдельный ручной шаг после ревью найденного.
# 3. Постоянное сохранение: после КАЖДОГО завершившегося чанка пересобирает
#    сводный файл из всех .jsonl в TMP_DIR — при прерывании скрипта уже
#    найденное не теряется (не только внутричанково, но и на уровне сводки).
import concurrent.futures
import json
import os
import subprocess
import sys
import threading
import time

ROOT = r"F:\DEV2\ostra_i18n"
PY = sys.executable
WORKER = os.path.join(ROOT, "tools", "extract_prefab_paths_worker.py")
COUNTER = os.path.join(ROOT, "tools", "count_mono.py")
TMP_DIR = os.path.join(ROOT, "lang_src", "prefab_extract_tmp2")
OUT = os.path.join(ROOT, "catalog", "prefabs_new_chargen.json")

CHUNK_SIZE = int(os.environ.get("EXTRACT_CHUNK", "100"))
CHUNK_TIMEOUT = int(os.environ.get("EXTRACT_TIMEOUT", "15"))
PER_FILE_BUDGET = int(os.environ.get("EXTRACT_FILE_BUDGET", "1800"))  # секунд на файл суммарно
MAX_WORKERS = int(os.environ.get("EXTRACT_WORKERS", "8"))

ASSET_FILES = ("resources.assets", "sharedassets1.assets", "sharedassets2.assets", "sharedassets4.assets")

os.makedirs(TMP_DIR, exist_ok=True)

MIN_TIMEOUT = 3
MIN_CHUNK_TO_SPLIT = 6

chunk_files = []
chunk_files_lock = threading.Lock()
timed_out_singles = 0
total_chunks = 0
counters_lock = threading.Lock()
_chunk_counter = {}
_chunk_counter_lock = threading.Lock()


def next_out_path(fname):
    with _chunk_counter_lock:
        i = _chunk_counter.get(fname, 0)
        _chunk_counter[fname] = i + 1
        return os.path.join(TMP_DIR, f"{fname}.{i}.jsonl")


def resave_summary():
    """Пересобирает сводный OUT из всех .jsonl в TMP_DIR — вызывается после
    каждого завершённого чанка, чтобы прогресс не терялся при прерывании."""
    entries = []
    seen_keys = set()
    with chunk_files_lock:
        snapshot = list(chunk_files)
    for fname, out_jsonl in snapshot:
        if not os.path.exists(out_jsonl):
            continue
        with open(out_jsonl, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    e = json.loads(line)
                except json.JSONDecodeError:
                    continue
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
    tmp = OUT + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(entries, f, ensure_ascii=False, indent=2)
    os.replace(tmp, OUT)
    return len(entries)


def process_range(fname, kind, start, end, deadline):
    global total_chunks, timed_out_singles
    size = end - start
    if time.time() > deadline:
        with counters_lock:
            timed_out_singles += size
        print(f"  [{fname} {start}:{end}] бюджет файла исчерпан — пропущен без попытки ({size} объектов)", flush=True)
        return
    timeout = max(MIN_TIMEOUT, int(CHUNK_TIMEOUT * size / CHUNK_SIZE))
    out_jsonl = next_out_path(fname)
    with counters_lock:
        total_chunks += 1
    t0 = time.time()
    try:
        proc = subprocess.run(
            [PY, "-u", WORKER, fname, kind, out_jsonl, str(start), str(end)],
            timeout=timeout, capture_output=True, text=True, encoding="utf-8",
        )
        if proc.returncode != 0 and proc.stderr.strip():
            print(f"  [{fname} {start}:{end}] STDERR: {proc.stderr.strip()[:200]}", flush=True)
        with chunk_files_lock:
            chunk_files.append((fname, out_jsonl))
        n = resave_summary()
        print(f"  [{fname} {start}:{end}] готово за {time.time()-t0:.1f}с (сводно записей: {n})", flush=True)
        return
    except subprocess.TimeoutExpired:
        pass

    if size <= MIN_CHUNK_TO_SPLIT:
        with counters_lock:
            timed_out_singles += size
        print(f"  [{fname} {start}:{end}] не удалось за {timeout}с — диапазон мал для дробления, пропущен ({size} объектов)", flush=True)
        return

    mid = start + size // 2
    print(f"  [{fname} {start}:{end}] таймаут {timeout}с — делю на [{start}:{mid}) и [{mid}:{end})", flush=True)
    # Разбитые под-диапазоны — тоже отдельные задачи в пуле, чтобы они тоже
    # обрабатывались параллельно с остальными, а не блокировали текущий поток.
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as inner:
        f1 = inner.submit(process_range, fname, kind, start, mid, deadline)
        f2 = inner.submit(process_range, fname, kind, mid, end, deadline)
        f1.result()
        f2.result()


def main():
    only = os.environ.get("EXTRACT_ONLY")
    files = [only] if only else list(ASSET_FILES)

    for fname in files:
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
        print(f"=== {fname}: {n} MonoBehaviour, воркеров {MAX_WORKERS}, бюджет {PER_FILE_BUDGET}с ===", flush=True)

        file_deadline = time.time() + PER_FILE_BUDGET
        ranges = []
        start = 0
        while start < n:
            end = min(start + CHUNK_SIZE, n)
            ranges.append((start, end))
            start = end

        with concurrent.futures.ThreadPoolExecutor(max_workers=MAX_WORKERS) as ex:
            futures = [ex.submit(process_range, fname, "asset", s, e, file_deadline) for s, e in ranges]
            for fut in concurrent.futures.as_completed(futures):
                fut.result()

        print(f"=== {fname}: обработано ===", flush=True)

    n = resave_summary()
    print(f"ИТОГО записей в {OUT}: {n}", flush=True)
    print(f"попыток обработки чанков: {total_chunks}, объектов пропущено как зависающих: {timed_out_singles}", flush=True)


if __name__ == "__main__":
    main()
