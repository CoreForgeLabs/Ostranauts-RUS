# Analyze TMP_FontAsset objects inside Unity assets: names, atlas population mode, source fonts.
# Static baked atlases without Cyrillic = blank squares for Russian text.
import sys, traceback

GAME = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data"

import UnityPy
print("UnityPy", UnityPy.__version__)

# Try to enable typetree generation from the game's own managed DLLs (for MonoBehaviour fields)
TT_OK = False
try:
    from UnityPy.helpers import TypeTreeHelper
    try:
        from TypeTreeGeneratorAPI import TypeTreeGenerator
        gen = TypeTreeGenerator(GAME + r"\Managed", with_mono=True)
        # different UnityPy versions expose different hooks; try the known ones
        for attr in ("set_generator", "SetGenerator"):
            if hasattr(TypeTreeHelper, attr):
                getattr(TypeTreeHelper, attr)(gen)
                TT_OK = True
                break
        if not TT_OK:
            # UnityPy >= 1.10: UnityPy.helpers.TypeTreeHelper.set_generator may not exist;
            # typetree can be passed per-object instead. We handle that at read time.
            print("TypeTreeHelper has no set_generator; will pass generator per-read if possible")
            TT_OK = True
    except Exception as e:
        print("TypeTreeGeneratorAPI failed:", e)
except Exception as e:
    print("TypeTreeHelper import failed:", e)
print("TT_OK:", TT_OK)

INTEREST = ("SDF", "Font", "font", "Roboto", "Pixel", "Museo", "Bangers", "Anton", "Oswald", "Kode", "Doto", "Ac437", "Electronic", "Comfortaa", "Jura", "Jost", "Noto", "Liberation", "Montserrat")

def analyze(path):
    print("\n== FILE:", path)
    try:
        env = UnityPy.load(path)
    except Exception as e:
        print("load failed:", e)
        return
    counts = {}
    for obj in env.objects:
        counts[obj.type.name] = counts.get(obj.type.name, 0) + 1
    interesting_counts = {k: v for k, v in counts.items() if k in ("MonoBehaviour", "Font", "Texture2D", "Material")}
    print("counts (subset):", interesting_counts)
    n_print = 0
    for obj in env.objects:
        if obj.type.name not in ("MonoBehaviour", "Font"):
            continue
        name = ""
        try:
            if obj.type.name == "Font":
                d = obj.read()
                name = getattr(d, "m_Name", "")
                print("FONT:", name)
                n_print += 1
                continue
        except Exception:
            pass
        # MonoBehaviour: try typetree read
        try:
            try:
                t = obj.read_typetree()
            except Exception:
                try:
                    d = obj.read()
                    t = getattr(d, "__dict__", {})
                except Exception:
                    t = None
            if not isinstance(t, dict):
                continue
            name = t.get("m_Name", "") or ""
            keys = list(t.keys())
            is_tmp = any("tlasPopulation" in k or "ourceFont" in k or "lyph" in k for k in keys)
            if is_tmp or any(x in name for x in INTEREST):
                slim = {k: t[k] for k in keys if any(s in k for s in ("m_Name", "tlasPopulation", "ourceFont", "tlasWidth", "tlasHeight", "lyph"))}
                # compact large fields
                for k in list(slim.keys()):
                    v = slim[k]
                    if isinstance(v, (list, bytes)) and len(str(v)) > 60:
                        slim[k] = "<%d items>" % len(v)
                print("TMP?:", slim)
                n_print += 1
        except Exception:
            continue
    print("printed:", n_print)

for f in (r"\resources.assets", r"\sharedassets0.assets", r"\sharedassets1.assets", r"\globalgamemanagers.assets"):
    try:
        analyze(GAME + f)
    except Exception:
        traceback.print_exc()
