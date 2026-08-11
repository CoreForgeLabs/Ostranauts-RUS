# Full offline extraction of every TMP_Text/UI.Text string baked into the game's assets
# (prefabs/scenes), using TypeTreeGenerator (built from the game's own Managed DLLs) to
# properly parse stripped-release MonoBehaviour data — same technique as analyze_tmp4.py
# used for fonts, applied to TextMeshProUGUI / TextMeshPro / UnityEngine.UI.Text instead.
import json, traceback
import UnityPy
from TypeTreeGeneratorAPI import TypeTreeGenerator

GAME = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data"
MANAGED = GAME + r"\Managed"
print("UnityPy", UnityPy.__version__)

gen = TypeTreeGenerator("6000.3.10f1")
for dll in ("netstandard.dll", "mscorlib.dll", "UnityEngine.CoreModule.dll",
            "UnityEngine.TextRenderingModule.dll", "UnityEngine.TextCoreFontEngineModule.dll",
            "UnityEngine.TextCoreTextEngineModule.dll", "Unity.TextMeshPro.dll", "UnityEngine.UI.dll"):
    try:
        gen.load_dll(open(MANAGED + "\\" + dll, "rb").read())
    except Exception as e:
        print("load failed", dll, e)

CANDIDATES = [
    ("TextMeshProUGUI", "Unity.TextMeshPro", "TMPro.TextMeshProUGUI"),
    ("TextMeshPro", "Unity.TextMeshPro", "TMPro.TextMeshPro"),
    ("UI.Text", "UnityEngine.UI", "UnityEngine.UI.Text"),
]
node_sets = []
for label, asm, cls in CANDIDATES:
    try:
        raw_nodes = json.loads(gen.get_nodes_as_json(asm, cls))
        node_sets.append((label, raw_nodes))
        print("nodes for", label, ":", len(raw_nodes))
    except Exception as e:
        print("nodegen failed", label, e)

results = {}  # text -> set of (file, label) provenance (first hit only, kept small)

def try_read(o):
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
        # sanity: mostly printable, not absurd length, not pure binary garbage
        printable = sum(1 for c in txt if c.isprintable() or c in "\n\t")
        if printable / max(1, len(txt)) < 0.9:
            continue
        if len(txt) > 20000:
            continue
        return label, txt
    return None, None

def analyze(fname):
    print("\n== FILE:", fname)
    try:
        env = UnityPy.load(GAME + "\\" + fname)
    except Exception as e:
        print("load failed:", e); return
    found = 0
    checked = 0
    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        checked += 1
        label, txt = try_read(o)
        if txt is None:
            continue
        found += 1
        results.setdefault(txt, []).append(fname + ":" + label)
    print("checked=%d found=%d" % (checked, found))

import os
targets = []
for f in os.listdir(GAME):
    if f.endswith(".assets") or f.startswith("level") or f == "resources.assets":
        targets.append(f)
print("targets:", targets)

for f in targets:
    try:
        analyze(f)
    except Exception:
        traceback.print_exc()

print("\nTOTAL UNIQUE STRINGS:", len(results))
with open(r"F:\DEV2\ostra_i18n\lang_src\baked_text_all.json", "w", encoding="utf-8") as fh:
    json.dump({k: v[:3] for k, v in results.items()}, fh, ensure_ascii=False, indent=2)
print("wrote lang_src/baked_text_all.json")
