# Identify TMP_FontAsset MonoBehaviours by parsing m_Name from raw header bytes
# (PPtr m_GameObject [12b] + u8 m_Enabled [pad to 4] + PPtr m_Script [12b] + str m_Name),
# then read full typetree for atlas mode + cyrillic coverage.
import struct, traceback
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
nodes = gen.get_nodes("Unity.TextMeshPro", "TMPro.TMP_FontAsset")
print("nodes:", len(nodes))

def read_name(raw):
    # try the expected offset first (PPtr=12b each, enabled padded)
    for off in (28, 24, 32):
        try:
            L = struct.unpack_from("<i", raw, off)[0]
            if 0 < L < 80 and off + 4 + L <= len(raw):
                s = raw[off+4:off+4+L].decode("utf-8", "strict")
                if all(32 <= ord(c) < 127 for c in s):
                    return s
        except Exception:
            continue
    return ""

def analyze(fname):
    print("\n== FILE:", fname)
    try:
        env = UnityPy.load(GAME + "\\" + fname)
    except Exception as e:
        print("load failed:", e)
        return
    found = 0
    total_mb = 0
    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        total_mb += 1
        raw = None
        try:
            raw = o.read_raw_data()
        except Exception:
            try:
                o.reset()
                raw = o.reader.read_bytes(o.byte_size)
            except Exception:
                continue
        name = read_name(raw)
        if not (name.endswith(" SDF") or "SDF" in name):
            continue
        try:
            t = o.read_typetree(nodes=nodes, check_read=False)
        except Exception as e:
            print("read_typetree failed for", name, ":", str(e)[:120])
            continue
        if not isinstance(t, dict):
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
        print("FONT_ASSET: %s | mode=%s | atlas=%sx%s | chars=%d glyphs=%d | CYR=%d" % (
            name, mode, t.get("m_AtlasWidth", "?"), t.get("m_AtlasHeight", "?"),
            len(chartab), len(glyphtab), cyr))
        found += 1
    print("MonoBehaviours scanned:", total_mb, "| font assets found:", found)

for f in ("resources.assets", "sharedassets0.assets", "sharedassets1.assets"):
    try:
        analyze(f)
    except Exception:
        traceback.print_exc()
