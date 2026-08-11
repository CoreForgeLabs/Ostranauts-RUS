# Быстрый подсчёт MonoBehaviour-объектов в файле — не требует TypeTreeGenerator,
# используется orchestrator-ом для нарезки файла на чанки перед запуском воркеров.
import sys
import UnityPy

GAME = r"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data"
fname = sys.argv[1]
env = UnityPy.load(GAME + "\\" + fname)
n = sum(1 for o in env.objects if o.type.name == "MonoBehaviour")
print(n)
