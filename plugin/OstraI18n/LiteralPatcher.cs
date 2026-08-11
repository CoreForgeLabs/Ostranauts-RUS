using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace OstraI18n
{
    // Заменяет ldstr-литералы в методах игры на значения из языкового пакета.
    // Task 1 — вертикальный срез на одном литерале, доказывающий механизм.
    internal static class LiteralPatcher
    {
        // Значение подставляется вместо литерала. В Task 6 заменяется на I18n.Get(ключ).
        public static string SliceReplacement = "На свободе";
        private const string SliceLiteral = "At Large";

        public static int Applied;

        public static void ApplySlice(Harmony harmony)
        {
            var type = AccessTools.TypeByName("GUIChargenCareer");
            if (type == null)
            {
                Plugin.Log.LogError("[i18n] slice: тип GUIChargenCareer не найден");
                return;
            }

            var transpiler = new HarmonyMethod(
                typeof(LiteralPatcher).GetMethod(nameof(SliceTranspiler),
                    BindingFlags.NonPublic | BindingFlags.Static));

            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                              | BindingFlags.Instance | BindingFlags.Static
                                              | BindingFlags.DeclaredOnly))
            {
                if (m.IsAbstract || m.ContainsGenericParameters) continue;
                try
                {
                    if (!MethodHasLiteral(m, SliceLiteral)) continue;
                    harmony.Patch(m, transpiler: transpiler);
                    Plugin.Log.LogInfo("[i18n] slice: пропатчен " + type.Name + "." + m.Name);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[i18n] slice: пропуск " + m.Name + ": " + ex.Message);
                }
            }
            Plugin.Log.LogInfo("[i18n] slice: подстановок применено " + Applied);
        }

        private static bool MethodHasLiteral(MethodBase m, string literal)
        {
            try
            {
                foreach (var instr in PatchProcessor.ReadMethodBody(m))
                    if (instr.Value is string s && s == literal) return true;
            }
            catch { }
            return false;
        }

        private static IEnumerable<CodeInstruction> SliceTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instr in instructions)
            {
                if (instr.opcode == System.Reflection.Emit.OpCodes.Ldstr
                    && (instr.operand as string) == SliceLiteral)
                {
                    Applied++;
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldstr, SliceReplacement);
                }
                else
                {
                    yield return instr;
                }
            }
        }
    }
}
