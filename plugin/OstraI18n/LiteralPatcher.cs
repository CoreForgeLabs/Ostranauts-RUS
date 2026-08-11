using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using HarmonyLib;
using OstraI18n.Core;

namespace OstraI18n
{
    /// Заменяет ldstr-литералы на вызов I18n.Get(ключ) по записям каталога.
    /// Подменяется только литерал с approved-записью; всё остальное проходит нетронутым.
    internal static class LiteralPatcher
    {
        private class Entry
        {
            public string Key;
            public int Ordinal;
        }

        // methodKey -> (литерал -> список записей по порядковому номеру)
        private static readonly Dictionary<string, Dictionary<string, List<Entry>>> ByMethod =
            new Dictionary<string, Dictionary<string, List<Entry>>>(StringComparer.Ordinal);

        private static Dictionary<string, List<Entry>> _current;

        public static int LoadCatalog(string pluginDir)
        {
            var path = Path.Combine(pluginDir, "catalog", "literals.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("[i18n] каталог не найден: " + path);
                return 0;
            }
            int n = 0;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("approved", out var ap) || !ap.GetBoolean()) continue;
                var mk = e.GetProperty("methodKey").GetString();
                var lit = e.GetProperty("literal").GetString();
                var key = e.GetProperty("key").GetString();
                var ord = e.GetProperty("ordinal").GetInt32();
                if (mk == null || lit == null || key == null) continue;

                if (!ByMethod.TryGetValue(mk, out var byLit))
                    ByMethod[mk] = byLit = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
                if (!byLit.TryGetValue(lit, out var list))
                    byLit[lit] = list = new List<Entry>();
                list.Add(new Entry { Key = key, Ordinal = ord });
                n++;
            }
            Plugin.Log.LogInfo("[i18n] каталог: утверждённых записей " + n
                               + " в " + ByMethod.Count + " методах");
            return n;
        }

        public static void ApplyAll(Harmony harmony)
        {
            var asm = typeof(DataHandler).Assembly;
            var transpiler = new HarmonyMethod(
                typeof(LiteralPatcher).GetMethod(nameof(Transpiler),
                    BindingFlags.NonPublic | BindingFlags.Static));

            int methodsPatched = 0, notFound = 0;

            foreach (var kv in ByMethod)
            {
                var m = Resolve(asm, kv.Key);
                if (m == null)
                {
                    notFound++;
                    I18n.Drifted += kv.Value.Count;
                    Plugin.Log.LogWarning("[i18n] дрейф: метод не найден " + kv.Key);
                    continue;
                }
                try
                {
                    _current = kv.Value;
                    harmony.Patch(m, transpiler: transpiler);
                    methodsPatched++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[i18n] патч пропущен " + kv.Key + ": " + ex.Message);
                }
                finally { _current = null; }
            }

            Plugin.Log.LogInfo("[i18n] литералы: методов пропатчено " + methodsPatched
                               + ", подстановок " + I18n.Applied
                               + ", дрейф " + I18n.Drifted
                               + ", методов не найдено " + notFound);
        }

        private static MethodBase Resolve(Assembly asm, string methodKey)
        {
            try
            {
                int sep = methodKey.LastIndexOf("::", StringComparison.Ordinal);
                if (sep < 0) return null;
                var typeName = methodKey.Substring(0, sep);
                var rest = methodKey.Substring(sep + 2);
                int slash = rest.LastIndexOf('/');
                if (slash < 0) return null;
                var name = rest.Substring(0, slash);
                if (!int.TryParse(rest.Substring(slash + 1), out int pc)) return null;

                var type = asm.GetType(typeName, false);
                if (type == null) return null;

                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                  | BindingFlags.Instance | BindingFlags.Static
                                                  | BindingFlags.DeclaredOnly))
                {
                    if (m.Name == name && m.GetParameters().Length == pc
                        && !m.IsAbstract && !m.ContainsGenericParameters)
                        return m;
                }
            }
            catch { }
            return null;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var map = _current;
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var instr in instructions)
            {
                if (map != null && instr.opcode == OpCodes.Ldstr && instr.operand is string lit
                    && map.TryGetValue(lit, out var list))
                {
                    seen.TryGetValue(lit, out int ord);
                    seen[lit] = ord + 1;

                    Entry match = null;
                    foreach (var e in list) if (e.Ordinal == ord) { match = e; break; }

                    if (match != null)
                    {
                        I18n.Applied++;
                        // Метки переходов и границы try/catch, указывавшие на исходный ldstr,
                        // должны остаться на первой инструкции замены — иначе branch/EH-блок
                        // ссылается в никуда и HarmonyX не может собрать метод (IL Compile Error).
                        var replacement = new CodeInstruction(OpCodes.Ldstr, match.Key);
                        replacement.MoveLabelsFrom(instr);
                        replacement.MoveBlocksFrom(instr);
                        yield return replacement;
                        yield return new CodeInstruction(OpCodes.Call,
                            AccessTools.Method(typeof(I18n), nameof(I18n.Get), new[] { typeof(string) }));
                        continue;
                    }
                }
                yield return instr;
            }
        }
    }
}
