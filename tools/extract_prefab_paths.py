# Полное офлайн-извлечение текста ИЗ ПРЕФАБОВ/СЦЕН вместе с путём в иерархии.
# kind различается по файлу-источнику: level0-4 = "scene" (путь абсолютный,
# стабилен для конкретной сцены), resources.assets/sharedassets* = "asset"
# (объект — часть префаб-шаблона, инстанцируется многократно под разными
# родителями; путь — от корня самого префаба, не от корня сцены).
import json, os, re, sys, time
import UnityPy
from TypeTreeGeneratorAPI import TypeTreeGenerator

sys.stdout.reconfigure(line_buffering=True)

GAME = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data"
MANAGED = GAME + r"\Managed"
OUT = r"F:\DEV2\ostra_i18n\catalog\prefabs.json"

gen = TypeTreeGenerator("6000.3.10f1")
for dll in ("netstandard.dll", "mscorlib.dll", "UnityEngine.CoreModule.dll",
            "UnityEngine.TextRenderingModule.dll", "UnityEngine.TextCoreFontEngineModule.dll",
            "UnityEngine.TextCoreTextEngineModule.dll", "Unity.TextMeshPro.dll", "UnityEngine.UI.dll"):
    gen.load_dll(open(MANAGED + "\\" + dll, "rb").read())

CANDIDATES = [
    ("TextMeshProUGUI", "Unity.TextMeshPro", "TMPro.TextMeshProUGUI"),
    ("TextMeshPro", "Unity.TextMeshPro", "TMPro.TextMeshPro"),
    ("UI.Text", "UnityEngine.UI", "UnityEngine.UI.Text"),
]
node_sets = [(label, json.loads(gen.get_nodes_as_json(asm, cls))) for label, asm, cls in CANDIDATES]


def try_read_text(o):
    for label, nodes in node_sets:
        try:
            t = o.read_typetree(nodes=nodes, check_read=False)
        except Exception:
            continue
        if not isinstance(t, dict):
            continue
        txt = t.get("m_text")
        if not isinstance(txt, str) or not txt.strip():
            continue
        printable = sum(1 for c in txt if c.isprintable() or c in "\n\t")
        if printable / max(1, len(txt)) < 0.9 or len(txt) > 20000:
            continue
        return t
    return None


def make_caches():
    return {"name": {}, "transform": {}, "father_go": {}, "path": {}}


def go_name(objs, pid, cache):
    if pid in cache["name"]:
        return cache["name"][pid]
    o = objs.get(pid)
    result = None
    if o is not None:
        try:
            result = o.read().m_Name
        except Exception:
            result = None
    cache["name"][pid] = result
    return result


def transform_of(objs, go_pid, cache):
    if go_pid in cache["transform"]:
        return cache["transform"][go_pid]
    o = objs.get(go_pid)
    result = None
    if o is not None:
        try:
            d = o.read()
            for c in d.m_Component:
                cp = c.component if hasattr(c, "component") else c
                pid = cp.m_PathID if hasattr(cp, "m_PathID") else cp.path_id
                co = objs.get(pid)
                if co is not None and co.type.name in ("Transform", "RectTransform"):
                    result = co
                    break
        except Exception:
            result = None
    cache["transform"][go_pid] = result
    return result


def father_go_pid(objs, go_pid, cache):
    # Родительский GameObject.m_PathID для данного go_pid, с мемоизацией —
    # без неё путь для каждого текстового объекта пересчитывается с нуля,
    # и общие предки (родительские панели) перечитываются тысячи раз.
    if go_pid in cache["father_go"]:
        return cache["father_go"][go_pid]
    result = None
    tr = transform_of(objs, go_pid, cache)
    if tr is not None:
        try:
            td = tr.read_typetree()
            father = td.get("m_Father", {}).get("m_PathID")
            if father:
                fo = objs.get(father)
                if fo is not None:
                    ftd = fo.read_typetree()
                    result = ftd.get("m_GameObject", {}).get("m_PathID")
        except Exception:
            result = None
    cache["father_go"][go_pid] = result
    return result


def full_path(objs, go_pid, cache, depth_limit=30):
    if go_pid in cache["path"]:
        return cache["path"][go_pid]
    n = go_name(objs, go_pid, cache)
    if n is None:
        cache["path"][go_pid] = []
        return []
    father = father_go_pid(objs, go_pid, cache)
    if father is None or depth_limit <= 0:
        result = [n]
    else:
        parent_path = full_path(objs, father, cache, depth_limit - 1)
        result = parent_path + [n]
    cache["path"][go_pid] = result
    return result


def slug(s):
    s = re.sub(r"[^A-Za-z0-9]+", "_", s).strip("_").upper()
    return s or "TEXT"


def make_key(root, segs, literal):
    tail = slug(segs[-1]) if segs else "TEXT"
    body = slug(literal)[:30]
    return f"GUI_{slug(root)}_{tail}_{body}".rstrip("_")


def analyze(fname, kind, out_entries, seen_keys):
    path = os.path.join(GAME, fname)
    if not os.path.exists(path):
        print("нет файла, пропуск:", fname)
        return
    print(f"--- {fname}: начинаю UnityPy.load() ---")
    t0 = time.time()
    env = UnityPy.load(path)
    print(f"--- {fname}: загружен за {time.time()-t0:.1f}с ---")
    t0 = time.time()
    objs = {o.path_id: o for o in env.objects}
    print(f"--- {fname}: индекс объектов построен за {time.time()-t0:.1f}с, объектов: {len(objs)} ---")
    cache = make_caches()
    found = 0
    checked = 0
    t0 = time.time()
    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        checked += 1
        if checked % 2000 == 0:
            print(f"    ...проверено {checked} MonoBehaviour, найдено текста {found}, "
                  f"{time.time()-t0:.1f}с")
        t = try_read_text(o)
        if t is None:
            continue
        go_pid = t.get("m_GameObject", {}).get("m_PathID")
        segs = full_path(objs, go_pid, cache)
        if not segs:
            continue
        root, rest = segs[0], segs[1:]
        literal = t["m_text"]
        key = make_key(root, segs, literal)
        n = 1
        base_key = key
        while key in seen_keys:
            n += 1
            key = f"{base_key}_{n}"
        seen_keys.add(key)
        out_entries.append({
            "kind": kind,
            "root": root,
            "path": rest,
            "literal": literal,
            "key": key,
            "sourceFile": fname,
            "approved": False,
        })
        found += 1
    print(f"{fname} ({kind}): найдено {found}")


entries = []
seen_keys = set()
for f in ("level0", "level1", "level2", "level3", "level4"):
    analyze(f, "scene", entries, seen_keys)
for f in ("resources.assets", "sharedassets0.assets", "sharedassets1.assets",
          "sharedassets2.assets", "sharedassets3.assets", "sharedassets4.assets"):
    analyze(f, "asset", entries, seen_keys)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
json.dump(entries, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print("всего записей:", len(entries))
print("уникальных ключей:", len(seen_keys))
print("записано:", OUT)
