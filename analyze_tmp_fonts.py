# Read TMP_FontAsset MonoBehaviours with typetrees generated from the game's Managed DLLs.
# Goal: for every SDF font asset report name, atlas population mode (0=static,1=dynamic),
# source font, atlas size, and Cyrillic glyph coverage in the baked character table.
import traceback
import UnityPy

GAME = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data"

print("UnityPy", UnityPy.__version__)

gen = None
try:
    from TypeTreeGeneratorAPI import TypeTreeGenerator
    try:
        gen = TypeTreeGenerator("6000.3.10f1", asm_path=GAME + r"\Managed")
    except Exception:
        gen = TypeTreeGenerator("6000.3.10f1")
        gen.load_dll(GAME + r"\Managed\Unity.TextMeshPro.dll")
    print("TypeTreeGenerator OK")
except Exception as e:
    print("TypeTreeGenerator FAILED:", e)

def read_tree(obj):
    if gen is not None:
        for call in (lambda: obj.read_typetree(gen),
                     lambda: obj.read_typetree(generator=gen),
                     lambda: obj.read_typetree(generator=gen, with_refs=False)):
            try:
                t = call()
                if isinstance(t, dict):
                    return t
            except Exception:
                continue
    try:
        t = obj.read_typetree()
        if isinstance(t, dict):
            return t
    except Exception:
        pass
    return None

def cyr_count(chartab):
    n = 0
    for ch in chartab or []:
        try:
            uni = ch.get("m_Unicode", 0) if isinstance(ch, dict) else 0
            if 0x400 <= uni <= 0x4FF or uni == 0x401 or uni == 0x451:
                n += 1
        except Exception:
            pass
    return n

for fname in ("resources.assets", "sharedassets0.assets", "sharedassets1.assets"):
    path = GAME + "\\" + fname
    print("\n== FILE:", fname)
    try:
        env = UnityPy.load(path)
    except Exception as e:
        print("load failed:", e)
        continue
    found = 0
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        t = read_tree(obj)
        if not t:
            continue
        keys = list(t.keys())
        if not any("tlasPopulation" in k for k in keys):
            continue
        name = t.get("m_Name", "?")
        mode = None
        for k in keys:
            if "tlasPopulation" in k:
                mode = t[k]
        chartab = t.get("m_CharacterTable") or t.get("m_Characters") or []
        cyr = cyr_count(chartab)
        atlas_w = t.get("m_AtlasWidth", "?")
        atlas_h = t.get("m_AtlasHeight", "?")
        print("TMP_FontAsset: %s | mode=%s | atlas=%sx%s | chars=%d | CYRILLIC=%d" % (name, mode, atlas_w, atlas_h, len(chartab), cyr))
        found += 1
    print("found TMP assets:", found)
