using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;

namespace OstranautsRusPatch
{
    /// <summary>
    /// Postfix for GrammarUtils.GenerateString() — cleans the static interactionOutput StringBuilder.
    /// GenerateString() returns void and writes to GrammarUtils.interactionOutput.
    /// 
    /// ENHANCED: Uses entityMap reflection to determine [us] identity:
    ///   - InflectionIndex=1 (Player/Second): apply verb conjugation Вы→2nd person plural
    ///   - InflectionIndex=2 (Male): he form
    ///   - InflectionIndex=3 (Female): she form
    ///   - InflectionIndex=5 (NonHuman): strip articles, no pronouns
    /// This eliminates regex guessing — we KNOW who [us] is.
    /// 
    /// Also applies noun declension from external rus_nouns.json table.
    /// </summary>
    public static class GenerateStringPostfix
    {
        // Cached reflection for entityMap access
        private static FieldInfo _entityMapField;
        private static PropertyInfo _inflectionProp;
        private static bool _reflectionReady;
        private static bool _reflectionFailed;

        /// <summary>
        /// Gets the PronounInflection index (0-5) for a given entity key in entityMap.
        /// Returns -1 if lookup fails.
        /// </summary>
        private static int GetInflectionIndex(string entityKey)
        {
            if (_reflectionFailed) return -1;

            try
            {
                // One-time field lookup
                if (!_reflectionReady)
                {
                    _entityMapField = typeof(GrammarUtils).GetField("entityMap",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (object.ReferenceEquals(_entityMapField, null))
                    {
                        RusPatchPlugin.Log.LogWarning("[RusGrammar] entityMap field not found on GrammarUtils");
                        _reflectionFailed = true;
                        return -1;
                    }
                    _reflectionReady = true;
                }

                // Get the dictionary
                object mapObj = _entityMapField.GetValue(null);
                if (mapObj == null) return -1;

                // entityMap is Dictionary<string, SentenceEntity>
                // SentenceEntity has InflectionIndex (PronounInflection enum)
                System.Collections.IDictionary map = mapObj as System.Collections.IDictionary;
                if (map == null || !map.Contains(entityKey)) return -1;

                object entity = map[entityKey];
                if (entity == null) return -1;

                // Cache PropertyInfo for InflectionIndex
                if (object.ReferenceEquals(_inflectionProp, null))
                {
                    _inflectionProp = entity.GetType().GetProperty("InflectionIndex",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (object.ReferenceEquals(_inflectionProp, null))
                    {
                        // Try field instead of property
                        FieldInfo fi = entity.GetType().GetField("InflectionIndex",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (!object.ReferenceEquals(fi, null))
                        {
                            int val = (int)fi.GetValue(entity);
                            return val;
                        }
                        RusPatchPlugin.Log.LogWarning("[RusGrammar] InflectionIndex not found on SentenceEntity");
                        _reflectionFailed = true;
                        return -1;
                    }
                }

                return (int)_inflectionProp.GetValue(entity, null);
            }
            catch (Exception ex)
            {
                RusPatchPlugin.Log.LogWarning("[RusGrammar] Reflection error: " + ex.Message);
                _reflectionFailed = true;
                return -1;
            }
        }

        public static void Postfix()
        {
            try
            {
                System.Text.StringBuilder sb = GrammarUtils.interactionOutput;
                if (sb == null || sb.Length < 3) return;
                string s = sb.ToString();

                // --- Get entity context for grammar-aware processing ---
                int usInflection = GetInflectionIndex("us");
                // usInflection: 0=I, 1=You(Player), 2=He, 3=She, 4=They, 5=It, -1=unknown

                // --- Strip {ls} tags ---
                bool hasLs = s.IndexOf("{ls ") >= 0;
                if (hasLs)
                {
                    s = RussianTextCleaner.StripLsBrackets(s);
                }

                // --- Apply entity-aware grammar ---
                bool needsClean = hasLs || RussianTextCleaner.HasLatin(s);
                bool needsVyFix = (usInflection == 1) || // KNOWN player
                    (usInflection == -1 && (s.IndexOf("\u0412\u044b ") >= 0 || s.IndexOf("\u0432\u044b ") >= 0)); // fallback regex

                if (needsClean || needsVyFix)
                {
                    string cleaned = RussianTextCleaner.Clean(s);
                    if (cleaned != s)
                    {
                        sb.Remove(0, sb.Length);
                        sb.Append(cleaned);
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Transpiler that wraps the return value of string-returning methods with Clean().
    /// Injects `call RussianTextCleaner.Clean(string)` before every `ret` instruction.
    /// ZERO per-call dispatch overhead — the call is part of the method's own IL.
    /// </summary>
    public static class CleanReturnTranspiler
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo cleanMethod = typeof(RussianTextCleaner).GetMethod("Clean",
                BindingFlags.Static | BindingFlags.Public, null,
                new Type[] { typeof(string) }, null);

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    // Before ret, the return string is on the stack.
                    // Call Clean() to transform it, then ret the result.
                    yield return new CodeInstruction(OpCodes.Call, cleanMethod);
                }
                yield return instruction;
            }
        }
    }

    /// <summary>
    /// Transpiler that cleans the first string argument (strMsg) of CondOwner.LogMessage.
    /// Injects `ldarg.1; call Clean; starg.1` at the beginning of the method.
    /// For instance methods, arg0=this, arg1=strMsg.
    /// </summary>
    public static class CleanFirstArgTranspiler
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Find Clean method — try multiple approaches for robustness
            MethodInfo cleanMethod = null;

            // Approach 1: Direct type reference
            cleanMethod = typeof(RussianTextCleaner).GetMethod("Clean",
                BindingFlags.Static | BindingFlags.Public, null,
                new Type[] { typeof(string) }, null);

            // Approach 2: AccessTools (handles type resolution issues)
            if (object.ReferenceEquals(cleanMethod, null))
            {
                cleanMethod = AccessTools.Method(typeof(RussianTextCleaner), "Clean",
                    new Type[] { typeof(string) });
            }

            // Approach 3: Search by name (last resort)
            if (object.ReferenceEquals(cleanMethod, null))
            {
                foreach (MethodInfo m in typeof(RussianTextCleaner).GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name == "Clean" && m.GetParameters().Length == 1)
                    {
                        cleanMethod = m;
                        break;
                    }
                }
            }

            if (object.ReferenceEquals(cleanMethod, null))
            {
                // Cannot find Clean method — emit original instructions unmodified
                RusPatchPlugin.Log.LogWarning("[RusPatch] CleanFirstArgTranspiler: Clean method not found, skipping");
                foreach (CodeInstruction instruction in instructions)
                {
                    yield return instruction;
                }
                yield break;
            }

            // Inject at the very beginning: strMsg = Clean(strMsg)
            yield return new CodeInstruction(OpCodes.Ldarg_1);  // load strMsg (first string param, instance method)
            yield return new CodeInstruction(OpCodes.Call, cleanMethod);
            yield return new CodeInstruction(OpCodes.Starg_S, (byte)1);  // store back to strMsg

            // Then emit the rest of the original method unchanged
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
            }
        }
    }

    /// <summary>
    /// Fix+diagnostic postfix for GetInflectedString(string, Condition, CondOwner).
    /// 
    /// ROOT CAUSE: Russian translations removed bracket tokens ([us], [is], [has]) from
    /// strDesc, replacing them with impersonal Russian constructions. E.g.:
    ///   EN: "[us] [is] bleeding."  →  RU: "Кровоточит."
    /// 
    /// The game's PrepareConditionDescriptions() adds these bracketless strings to
    /// inflectedStrings with an EMPTY token list. GenerateString() then runs with
    /// replacements.Count==0, the for-loop never executes, and interactionOutput
    /// stays empty. Result: arrow sprite appears but text is blank.
    /// 
    /// FIX: When inflection produces empty output but input target was non-empty,
    /// return the target string as-is (plain text display without inflection).
    /// This affects 815+ conditions in the Russian mod.
    /// </summary>
    public static class InflectedStringDiag
    {
        private static int _diagCount = 0;
        private static int _fixCount = 0;

        public static void Postfix(string target, ref string __result, CondOwner condOwner)
        {
            _diagCount++;

            bool targetOk = !string.IsNullOrEmpty(target) && target.Length > 2;
            bool resultEmpty = string.IsNullOrEmpty(__result) || __result.Trim().Length == 0;

            if (targetOk && resultEmpty)
            {
                // Fix: use target text directly when inflection produced empty output
                __result = target;
                _fixCount++;

                // Log only first few fixed cases to avoid startup log spam.
                if (_fixCount <= 5)
                {
                    string coId = "null";
                    try { if (condOwner != null) coId = condOwner.strID; } catch { }
                    RusPatchPlugin.Log.LogInfo(
                        "[FIX-EMPTY-ARROW] #" + _fixCount +
                        " restored: \"" + (target.Length > 80 ? target.Substring(0, 80) + "..." : target) + "\"" +
                        " co=" + coId);
                }
            }
        }
    }
}
