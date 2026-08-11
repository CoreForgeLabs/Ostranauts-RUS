import inspect
from UnityPy.files import ObjectReader
from UnityPy.helpers import TypeTreeHelper

print("=== _get_typetree_node source ===")
print(inspect.getsource(ObjectReader._get_typetree_node))
print("=== TypeTreeNode init sig ===")
try:
    print(inspect.signature(TypeTreeHelper.TypeTreeNode.__init__))
except Exception as e:
    print("sig fail:", e)
print("=== TypeTreeNode init source ===")
try:
    print(inspect.getsource(TypeTreeHelper.TypeTreeNode.__init__)[:1000])
except Exception as e:
    print("src fail:", e)
print("=== TypeTreeNode attrs ===")
try:
    print([a for a in dir(TypeTreeHelper.TypeTreeNode) if a.startswith("m_") or a in ("children","m_Children")])
except Exception as e:
    print(e)
