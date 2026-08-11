# Definitive offline answer: for every TMP_FontAsset in the game's assets,
# report atlas population mode (static/dynamic) and Cyrillic coverage of the baked glyph table.
import traceback
import UnityPy
from TypeTreeGeneratorAPI import TypeTreeGenerator

GAME = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data"
MANAGED = GAME + r"\Managed"

print("UnityPy", UnityPy.__version__)
gen = TypeTreeGenerator("6000.3.10f1")
# load base assemblies so type resolution works (netstandard, mscorlib, UnityEngine modules, TMP)
for dll in ("netstandard.dll", "mscorlib.dll", "UnityEngine.CoreModule.dll",
            "UnityEngine.TextRenderingModule.dll", "UnityEngine.TextCoreFontEngineModule.dll",
            "UnityEngine.TextCoreTextEngineModule.dll", "Unity.TextMeshPro.dll", "UnityEngine.UI.dll"):
    try:
        gen.load_dll(open(MANAGED + "\\" + dll, "rb").read())
        print("loaded", dll)
    except Exception as e:
        print("load failed", dll, e)

nodes = gen.get_nodes("Unity.TextMeshPro", "TMPro.TMP_FontAsset")
print("nodes:", len(nodes))

KNOWN_SDF = {"Ac437_Cordata_PPC-21 SDF","Ac437_ToshibaSat_8x14 SDF","Anton SDF","Bangers SDF","Comfortaa-Bold SDF",
"Doto_Rounded-Regular SDF","Doto_Rounded-SemiBold SDF","Electronic Highway Sign SDF","Jost-Bold SDF",
"Jost-VariableFont_wght SDF","Jura-Bold InfoTitle SDF","Jura-Bold SDF","Jura-Regular SDF",
"KodeMono-VariableFont_wght SDF","LiberationSans SDF","msyh SDF","MuseoModerno-Black SDF","MuseoModerno-ExtraBold SDF",
"MuseoModerno-ExtraLight SDF","MuseoModerno-Light SDF","MuseoModerno-Regular SDF","MuseoModerno-SemiBold SDF",
"MuseoModerno-Thin SDF","MuseoModerno-VariableFont_wght SDF","NotoSans-Regular SDF","NotoSansGC-Regular SDF",
"NotoSansJP-KJ-Regular SDF","NotoSansKR-Regular SDF","NotoSansSC-Regular SDF","NotoSansSC-v2-Regular SDF",
"Oswald Bold SDF","Roboto-Black SDF","Roboto-Bold SDF","robotocondensed SDF","robotocondensedb SDF","Unity SDF"}

def analyze(fname):
    path = GAME + "\\" + fname
    print("\n== FILE:", fname)
    try:
        env = UnityPy.load(path)
    except Exception as e:
        print("load failed:", e)
        return
    # map MonoScript path_id -> class name (same-file refs)
    scriptmap = {}
    for o in env.objects:
        if o.type.name == "MonoScript":
            try:
                d = o.read()
                scriptmap[o.path_id] = getattr(d, "m_ClassName", "")
            except Exception:
                pass
    print("monoscripts:", len(scriptmap))
    found = 0
    for o in env.objects:
        if o.type.name != "MonoBehaviour":
            continue
        # identify by script name if possible
        is_font = False
        name = ""
        try:
            mb = o.read()
            name = getattr(mb, "m_Name", "") or ""
            sp = getattr(mb, "m_Script", None)
            if sp is not None:
                pid = getattr(sp, "m_PathID", getattr(sp, "path_id", 0))
                if scriptmap.get(pid) == "TMP_FontAsset":
                    is_font = True
        except Exception:
            pass
        if not is_font and name in KNOWN_SDF:
            is_font = True
        if not is_font:
            continue
        try:
            t = o.read_typetree(nodes=nodes, check_read=False)
        except Exception as e:
            print("TMP read failed for", name, ":", e)
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
        aw = t.get("m_AtlasWidth", "?"); ah = t.get("m_AtlasHeight", "?")
        srcguid = t.get("m_SourceFontFileGUID", t.get("sourceFontFileGUID", "?"))
        print("FONT_ASSET: %s | mode=%s | atlas=%sx%s | chars=%d glyphs=%d | CYR=%d | srcGuid=%s" % (
            name, mode, aw, ah, len(chartab), len(glyphtab), cyr, srcguid))
        found += 1
    print("found font assets:", found)

for f in ("resources.assets", "sharedassets0.assets"):
    try:
        analyze(f)
    except Exception:
        traceback.print_exc()
