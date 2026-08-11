# Final TMP font analysis: raw-header identify + read_typetree with TTG JSON nodes.
import json, struct, traceback
import UnityPy
from TypeTreeGeneratorAPI import TypeTreeGenerator

GAME = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data"
MANAGED = GAME + r"\Managed"
print("UnityPy", UnityPy.__version__)

gen = TypeTreeGenerator("6000.3.10f1")
for dll in ("netstandard.dll","mscorlib.dll","UnityEngine.CoreModule.dll","UnityEngine.TextRenderingModule.dll",
            "UnityEngine.TextCoreFontEngineModule.dll","UnityEngine.TextCoreTextEngineModule.dll","Unity.TextMeshPro.dll","UnityEngine.UI.dll"):
    try:
        gen.load_dll(open(MANAGED + "\\" + dll, "rb").read())
    except Exception as e:
        print("load failed", dll, e)

raw_nodes = json.loads(gen.get_nodes_as_json("Unity.TextMeshPro","TMPro.TMP_FontAsset"))
print("ttg json nodes:", len(raw_nodes))

nodes_plain = [dict(n) for n in raw_nodes]
nodes_full = []
for i, n in enumerate(raw_nodes):
    d = dict(n)
    d.setdefault("m_ByteSize", 0); d.setdefault("m_Version", 1); d.setdefault("m_Index", i)
    d.setdefault("m_TypeFlags", 0); d.setdefault("m_VariableCount", 0); d.setdefault("m_RefTypeHash", 0)
    nodes_full.append(d)

def read_name(raw):
    for off in (28, 24, 32):
        try:
            L = struct.unpack_from("<i", raw, off)[0]
            if 0 < L < 90 and off + 4 + L <= len(raw):
                s = raw[off+4:off+4+L].decode("utf-8", "strict")
                if all(32 <= ord(c) < 127 for c in s):
                    return s
        except Exception:
            continue
    return ""

mode_ok = {"nodes": None}

def analyze(fname):
    print("\n== FILE:", fname)
    try:
        env = UnityPy.load(GAME + "\\" + fname)
    except Exception as e:
        print("load failed:", e); return
    found = 0
    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        raw = None
        try:
            raw = o.read_raw_data()
        except Exception:
            try:
                o.reset(); raw = o.reader.read_bytes(o.byte_size)
            except Exception:
                continue
        name = read_name(raw)
        if "SDF" not in name:
            continue
        t = None
        if mode_ok["nodes"] is not None:
            try:
                t = o.read_typetree(nodes=mode_ok["nodes"], check_read=False)
            except Exception:
                t = None
        else:
            for label, cand in (("plain", nodes_plain), ("full", nodes_full)):
                try:
                    t = o.read_typetree(nodes=cand, check_read=False)
                    if isinstance(t, dict) and ("m_AtlasPopulationMode" in t or "m_CharacterTable" in t):
                        mode_ok["nodes"] = cand
                        print("[nodes mode:", label, "worked]")
                        break
                except Exception:
                    continue
        if not isinstance(t, dict):
            print("READ FAIL:", name)
            continue
        mode = t.get("m_AtlasPopulationMode", "?")
        chartab = t.get("m_CharacterTable") or []
        glyphtab = t.get("m_GlyphTable") or []
        cyr = 0
        for ch in chartab:
            try:
                uni = ch.get("m_Unicode", 0) if isinstance(ch, dict) else 0
                if 0x400 <= uni <= 0x4FF:
                    cyr += 1
            except Exception:
                pass
        print("FONT: %-46s | mode=%s | %sx%s | chars=%d glyphs=%d | CYR=%d" % (
            name, mode, t.get("m_AtlasWidth","?"), t.get("m_AtlasHeight","?"), len(chartab), len(glyphtab), cyr))
        found += 1
    print("font assets:", found)

for f in ("resources.assets", "sharedassets0.assets"):
    try:
        analyze(f)
    except Exception:
        traceback.print_exc()
