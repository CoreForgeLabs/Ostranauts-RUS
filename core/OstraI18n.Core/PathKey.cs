using System;
using System.Collections.Generic;
using System.Linq;

namespace OstraI18n.Core
{
    /// Сравнение путей в иерархии GameObject. Инстанцированные копии префаба
    /// Unity сама дописывает "(Clone)" к имени корня — сравнение должно это
    /// игнорировать, иначе ни одна копия не совпадёт с каталогом.
    public static class PathKey
    {
        public static string[] Segments(string root, IEnumerable<string> path)
        {
            var list = new List<string> { root };
            list.AddRange(path);
            return list.ToArray();
        }

        public static bool Matches(string[] objectPath, string[] catalogPath)
        {
            if (objectPath.Length != catalogPath.Length) return false;
            for (int i = 0; i < objectPath.Length; i++)
            {
                var a = StripClone(objectPath[i]);
                var b = StripClone(catalogPath[i]);
                if (!string.Equals(a, b, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static string StripClone(string name)
        {
            const string suffix = "(Clone)";
            return name.EndsWith(suffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - suffix.Length)
                : name;
        }
    }
}
