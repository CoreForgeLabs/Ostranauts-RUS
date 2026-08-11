import json
from TypeTreeGeneratorAPI import TypeTreeGenerator
GAME = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data"
gen = TypeTreeGenerator("6000.3.10f1")
for dll in ("netstandard.dll","mscorlib.dll","UnityEngine.CoreModule.dll","UnityEngine.TextRenderingModule.dll","UnityEngine.TextCoreFontEngineModule.dll","UnityEngine.TextCoreTextEngineModule.dll","Unity.TextMeshPro.dll"):
    gen.load_dll(open(GAME + "\\Managed\\" + dll, "rb").read())
js = gen.get_nodes_as_json("Unity.TextMeshPro","TMPro.TMP_FontAsset")
nodes = json.loads(js)
print("json nodes:", len(nodes))
print("node0 keys:", list(nodes[0].keys()))
print("node0 sample:", {k: nodes[0][k] for k in list(nodes[0].keys())[:8]})
print("node1 sample:", {k: nodes[1][k] for k in list(nodes[1].keys())[:8]})
import UnityPy
from UnityPy.helpers import TypeTreeHelper
print("TypeTreeHelper methods:", [m for m in dir(TypeTreeHelper) if not m.startswith("_")])
try:
    from UnityPy.classes import TypeTreeNode as UNode
    import inspect
    print("UNode init:", inspect.signature(UNode.__init__))
    print("UNode attrs:", [a for a in dir(UNode) if not a.startswith("__")][:20])
except Exception as e:
    print("UNode fail:", e)
try:
    from UnityPy.helpers.TypeTreeHelper import TypeTreeNode as HNode
    print("HNode init:", inspect.signature(HNode.__init__))
except Exception as e:
    print("HNode fail:", e)
