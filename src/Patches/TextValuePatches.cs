using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace OstranautsRusPatch
{
    /// <summary>
    /// Generic postfix that cleans __result (for methods returning string).
    /// Used by GrammarUtils and DataHandler patches.
    /// PERF: HasLatin fast-exit skips >90% of calls at high game speed.
    /// </summary>
    public static class CleanResultPostfix
    {
        // Diagnostic: log transformations to help debug game logic corruption.
        // Only active during investigation; set to false to disable.
        private const bool DiagLog = false;
        private static int _diagCount = 0;
        private const int MaxDiagLogs = 200;

        public static void Postfix(ref string __result)
        {
            RusPatchPlugin._postfixCalls++;
            if (string.IsNullOrEmpty(__result) || __result.Length < 3) { RusPatchPlugin._postfixSkipped++; return; }

            // GUARD: Skip strings without spaces/newlines — these are internal game IDs/codes.
            // e.g. "ItmDockSys03Open", "MSPortalOpenStartRemoveAuto", "TIsDockSys03Closed"
            // Translating internal IDs corrupts game logic lookups (ModeSwitch, CondOwner, etc.)
            bool hasLs = __result.IndexOf("{ls ") >= 0;
            if (!hasLs && __result.IndexOf(' ') < 0 && __result.IndexOf('\n') < 0)
            {
                RusPatchPlugin._postfixSkipped++;
                return;
            }

            // Strip {ls} tags ALWAYS — these appear in tooltip condition text
            if (hasLs)
            {
                __result = RussianTextCleaner.StripLsBrackets(__result);
            }

            if (!hasLs && !RussianTextCleaner.HasLatin(__result))
            {
                // Pure Cyrillic still needs verb conjugation fix for "Вы + verb"
                if (__result.IndexOf("Вы ") >= 0 || __result.IndexOf("вы ") >= 0)
                    __result = RussianTextCleaner.Clean(__result);
                else
                    { RusPatchPlugin._postfixSkipped++; return; }
            }
            else
            {
                if (DiagLog && _diagCount < MaxDiagLogs)
                {
                    string before = __result;
                    __result = RussianTextCleaner.Clean(__result);
                    if (before != __result && before.Length < 120)
                    {
                        _diagCount++;
                        RusPatchPlugin.Log.LogInfo("[XFORM] '" + before + "' -> '" + __result + "'");
                    }
                }
                else
                    __result = RussianTextCleaner.Clean(__result);
            }
        }
    }

    /// <summary>
    /// Postfix for GUIMFDDisplay.ShowMenu: reduces &lt;size=XX&gt; tag values
    /// in MFD rich text so long Russian ship names fit the display.
    /// MFD uses UnityEngine.UI.Text with rich text tags, not TMPro.
    /// Bypasses the text setter to avoid triggering CleanValuePrefix re-entry.
    /// </summary>
    public static class MFDDisplayFontFix
    {
        private static FieldInfo _txtLeftField;
        private static FieldInfo _txtRightField;
        private static bool _resolved;
        private static int _logCount;
        public static bool _modifying; // re-entry guard for CleanValuePrefix

        public static void Postfix(object __instance)
        {
            try
            {
                if (!_resolved)
                {
                    _resolved = true;
                    Type t = __instance.GetType();
                    _txtLeftField = t.GetField("txtLeft",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    _txtRightField = t.GetField("txtRight",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }

                // Diagnostic logging disabled during lag isolation.

                WrapShipNamesWithSize(_txtLeftField, __instance);
                WrapShipNamesWithSize(_txtRightField, __instance);
            }
            catch { }
        }

        private static string GetFieldText(FieldInfo field, object instance)
        {
            if (object.ReferenceEquals(field, null)) return null;
            object textComp = field.GetValue(instance);
            if (object.ReferenceEquals(textComp, null)) return null;
            PropertyInfo textProp = textComp.GetType().GetProperty("text",
                BindingFlags.Public | BindingFlags.Instance);
            if (object.ReferenceEquals(textProp, null)) return null;
            return textProp.GetValue(textComp, null) as string;
        }

        // Wraps ship-name lines (those WITHOUT <size= tag) in <size=20>
        // so they shrink while distances (<size=30>) and base fontSize (36) stay intact.
        // Only applies to NAV ship-select page (detected by presence of <size=30> distance lines).
        private static void WrapShipNamesWithSize(FieldInfo field, object instance)
        {
            if (object.ReferenceEquals(field, null)) return;
            object textComp = field.GetValue(instance);
            if (object.ReferenceEquals(textComp, null)) return;

            PropertyInfo textProp = textComp.GetType().GetProperty("text",
                BindingFlags.Public | BindingFlags.Instance);
            if (object.ReferenceEquals(textProp, null)) return;

            string txt = textProp.GetValue(textComp, null) as string;
            if (string.IsNullOrEmpty(txt)) return;

            // Only target the ship-select page — it has <size=30> distance lines
            if (txt.IndexOf("<size=30>", StringComparison.Ordinal) < 0) return;

            string[] lines = txt.Split('\n');
            bool changed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                // Skip empty lines and lines that already have <size= (distances)
                if (line.Length < 3) continue;
                if (line.IndexOf("<size=", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                // This line is a ship name — wrap it
                lines[i] = "<size=20>" + line + "</size>";
                changed = true;
            }

            if (changed)
            {
                string newTxt = string.Join("\n", lines);
                _modifying = true;
                try { textProp.SetValue(textComp, newTxt, null); }
                finally { _modifying = false; }

                if (_logCount < 10)
                {
                    _logCount++;
                }
            }
        }
    }

    /// <summary>
    /// Generic prefix that cleans value parameter (for text setters).
    /// Used by UI.Text and TMPro patches.
    /// Uses recursion guard to prevent infinite loop with XUnity.AutoTranslator
    /// which also hooks TMP_Text.text setter.
    /// 
    /// PERFORMANCE: HasLatin check skips all processing for pure-Cyrillic text.
    /// This means already-translated text (90%+ of UI) is handled in ~50ns.
    /// </summary>
    public static class CleanValuePrefix
    {
        [ThreadStatic] private static bool _inClean;
        private static int _diagWhatsNewSetterCount;

        public static void Prefix(ref string value, object __instance)
        {
            RusPatchPlugin._prefixCalls++;
            if (_inClean) { RusPatchPlugin._prefixSkipped++; return; }
            if (MFDDisplayFontFix._modifying) { return; }
            if (string.IsNullOrEmpty(value) || value.Length < 3) { RusPatchPlugin._prefixSkipped++; return; }

            if (_diagWhatsNewSetterCount < 5 && IsWhatsNewHeader(value))
            {
                _diagWhatsNewSetterCount++;
                string compType = "<null>";
                string goName = "<no-go>";
                string fontName = "<no-font>";
                string matName = "<no-mat>";

                if (!object.ReferenceEquals(__instance, null))
                {
                    compType = __instance.GetType().FullName;
                    var comp = __instance as Component;
                    if (!object.ReferenceEquals(comp, null) && !object.ReferenceEquals(comp.gameObject, null))
                        goName = comp.gameObject.name;

                    var tmp = __instance as TMPro.TMP_Text;
                    if (!object.ReferenceEquals(tmp, null))
                    {
                        if (!object.ReferenceEquals(tmp.font, null)) fontName = tmp.font.name;
                        if (!object.ReferenceEquals(tmp.fontSharedMaterial, null)) matName = tmp.fontSharedMaterial.name;
                    }
                }

                RusPatchPlugin.Log.LogInfo(
                    "[DIAG-WHATSNEW-SETTER] type=" + compType +
                    " go=" + goName +
                    " font=" + fontName +
                    " mat=" + matName +
                    " text='" + value + "'" +
                    " unicode=" + BuildUnicodeCodes(value));
            }

            // Fast-path: standalone Yes/No
            if (value == "No") { value = "\u041d\u0435\u0442"; return; }
            if (value == "Yes") { value = "\u0414\u0430"; return; }

            // FAST EXIT: no Latin = no translation needed (except verb conjugation for Вы)
            if (!RussianTextCleaner.HasLatin(value))
            {
                if (value.IndexOf("Вы ") >= 0 || value.IndexOf("вы ") >= 0)
                    value = RussianTextCleaner.Clean(value);
                else
                    { RusPatchPlugin._prefixSkipped++; return; }
            }


            _inClean = true;
            try
            {
                value = RussianTextCleaner.Clean(value);
            }
            finally
            {
                _inClean = false;
            }
            value = InsertNewlinesBeforeLabels(value);
        }

        private static bool IsWhatsNewHeader(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("что нового", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("what's new", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("what’s new", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildUnicodeCodes(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("U+");
                sb.Append(((int)text[i]).ToString("X4"));
            }
            return sb.ToString();
        }

        // Field labels that indicate a new line in the ship log page.
        // Match both English (before translation) and Russian (after cached re-read).
        private static readonly string[] _shipLogLabels = new string[] {
            "REGID:", "\u0420\u0435\u0433. \u043a\u043e\u0434:",  // Рег. код:
            "Date of Construction:", "\u0414\u0430\u0442\u0430 \u043f\u0440\u043e\u0438\u0437\u0432\u043e\u0434\u0441\u0442\u0432\u0430:",  // Дата производства:
            "Date of \u041f\u0440\u043e\u0438\u0437\u0432\u043e\u0434\u0441\u0442\u0432\u043e:",
            "Make:", "\u041c\u0430\u0440\u043a\u0430:",  // Марка:
            "Model:", "\u041c\u043e\u0434\u0435\u043b\u044c:",  // Модель:
            "Homeport:", "\u041f\u043e\u0440\u0442 \u043f\u0440\u0438\u043f\u0438\u0441\u043a\u0438:",  // Порт приписки:
            "Designation:", "\u041e\u0431\u043e\u0437\u043d\u0430\u0447\u0435\u043d\u0438\u0435:",  // Обозначение:
            "Total Mass:", "\u041e\u0431\u0449\u0430\u044f \u043c\u0430\u0441\u0441\u0430:",  // Общая масса:
            "-- -- --",
            "Door ", "\u0414\u0432\u0435\u0440\u044c ",  // Дверь
            "\u041d\u0430\u0437\u0432\u0430\u043d\u0438\u0435 \u0441\u0443\u0434\u043d\u0430:"  // Название судна: (for re-read after translation)
        };

        // System status labels for the ship systems page
        // Includes both English (before translation) and Russian (after cached re-read)
        private static readonly string[] _shipSysLabels = new string[] {
            "RATING CODE", "\u041a\u041e\u0414 \u0420\u0415\u0419\u0422\u0418\u041d\u0413\u0410",  // КОД РЕЙТИНГА
            "VESSEL MASS:", "\u041c\u0410\u0421\u0421\u0410 \u0421\u0423\u0414\u041d\u0410:",  // МАССА СУДНА:
            "TRANSPONDER:", "\u0422\u0420\u0410\u041d\u0421\u041f\u041e\u041d\u0414\u0415\u0420:",  // ТРАНСПОНДЕР:
            "TRANSPONDER", "\u0422\u0420\u0410\u041d\u0421\u041f\u041e\u041d\u0414\u0415\u0420",  // ТРАНСПОНДЕР (section header)
            "ANTENNA:", "\u0410\u041d\u0422\u0415\u041d\u041d\u0410:",  // АНТЕННА:
            "NAV STATION:", "\u041d\u0410\u0412-\u0421\u0422\u0410\u041d\u0426\u0418\u042f:",
            "REACTOR HE3:", "\u0413\u0415\u041b\u0418\u0419-3:",  // ГЕЛИЙ-3:
            "REACTOR D2O:", "\u0422\u042f\u0416\u0401\u041b\u0410\u042f \u0412\u041e\u0414\u0410:",  // ТЯЖЁЛАЯ ВОДА:
            "REACTOR:", "\u0420\u0415\u0410\u041a\u0422\u041e\u0420:",
            "REACTOR PELLETS:", "\u0422\u041e\u041f\u041b\u0418\u0412\u041e \u0420\u0415\u0410\u041a\u0422\u041e\u0420\u0410:",
            "REACTOR PROPELLANT:", "\u0420\u0410\u0411\u041e\u0427\u0415\u0415 \u0422\u0415\u041b\u041e:",
            "RCS THRUSTERS:", "\u0414\u0412\u0418\u0413\u0410\u0422\u0415\u041b\u0418 \u0420\u0421\u0423:",
            "RCS DISTRIBUTOR:", "\u0420\u0410\u0421\u041f\u0420\u0415\u0414\u0415\u041b\u0418\u0422\u0415\u041b\u042c \u0420\u0421\u0423:",
            "RCS REMASS:", "\u0422\u041e\u041f\u041b\u0418\u0412\u041e \u0420\u0421\u0423:",
            "BACKUP POWER:", "\u0420\u0415\u0417\u0415\u0420\u0412\u041d\u041e\u0415 \u041f\u0418\u0422\u0410\u041d\u0418\u0415:",
            "LIFE SUPPORT O2 STORES:", "\u0417\u0410\u041f\u0410\u0421\u042b O2:",  // ЗАПАСЫ O2: (must be before LIFE SUPPORT)
            "LIFE SUPPORT HEAT:", "\u041e\u0411\u041e\u0413\u0420\u0415\u0412:",  // ОБОГРЕВ:
            "LIFE SUPPORT COOL:", "\u041e\u0425\u041b\u0410\u0416\u0414\u0415\u041d\u0418\u0415:",  // ОХЛАЖДЕНИЕ:
            "LIFE SUPPORT", "\u0416\u0418\u0417\u041d\u0415\u041e\u0411\u0415\u0421\u041f\u0415\u0427\u0415\u041d\u0418\u0415",  // ЖИЗНЕОБЕСПЕЧЕНИЕ
            "WORKING O2 PUMPS:", "\u041d\u0410\u0421\u041e\u0421\u042b O2:"  // НАСОСЫ O2:
        };

        /// <summary>
        /// Inserts \n before known field labels in ship log / ship systems pages.
        /// The game builds these texts by appending fields without separators.
        /// </summary>
        internal static string InsertNewlinesBeforeLabels(string text)
        {
            // Ship log page: starts with "Vessel Name:" or translated "Название судна:"
            if (text.IndexOf("Vessel Name:") >= 0 ||
                text.IndexOf("\u041d\u0430\u0437\u0432\u0430\u043d\u0438\u0435 \u0441\u0443\u0434\u043d\u0430:") >= 0)
            {
                return InsertBreaks(text, _shipLogLabels);
            }

            // Ship systems page: starts with "VESSEL" and has system labels
            if (text.Length > 20 &&
                (text.IndexOf("VESSEL") == 0 ||
                 text.IndexOf("TRANSPONDER") >= 0 ||
                 text.IndexOf("\u0422\u0420\u0410\u041d\u0421\u041f\u041e\u041d\u0414\u0415\u0420") >= 0))
            {
                return InsertBreaks(text, _shipSysLabels);
            }

            return text;
        }

        private static string InsertBreaks(string text, string[] labels)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                int pos = 0;
                while (true)
                {
                    pos = text.IndexOf(labels[i], pos);
                    if (pos <= 0) break;
                    if (text[pos - 1] != '\n')
                    {
                        text = text.Insert(pos, "\n");
                        pos += labels[i].Length + 1;
                    }
                    else
                    {
                        pos += labels[i].Length;
                    }
                }
            }
            return text;
        }
    }

    /// <summary>
    /// Prefix for TMPro.TMP_Text.SetText(string) — catches text set via method call.
    /// Uses recursion guard to prevent infinite loop with XUnity.AutoTranslator.
    /// </summary>
    public static class SetTextPrefix
    {
        [ThreadStatic] private static bool _inClean;

        public static void Prefix(ref string sourceText)
        {
            if (_inClean) return;
            if (string.IsNullOrEmpty(sourceText) || sourceText.Length < 3) return;

            if (sourceText == "No") { sourceText = "\u041d\u0435\u0442"; return; }
            if (sourceText == "Yes") { sourceText = "\u0414\u0430"; return; }

            if (!RussianTextCleaner.HasLatin(sourceText))
            {
                if (sourceText.IndexOf("Вы ") >= 0 || sourceText.IndexOf("вы ") >= 0)
                    sourceText = RussianTextCleaner.Clean(sourceText);
                return;
            }

            _inClean = true;
            try
            {
                sourceText = RussianTextCleaner.Clean(sourceText);
            }
            finally
            {
                _inClean = false;
            }
            sourceText = CleanValuePrefix.InsertNewlinesBeforeLabels(sourceText);
        }
    }

    /// <summary>
    /// Catch-all prefix for CondOwner.LogMessage — cleans strMsg before it's stored.
    /// This catches ALL log messages including those from Relationship.cs and CondOwner.cs
    /// that contain hardcoded English phrases like "now considers", "a(n)", "notices".
    /// </summary>
    public static class LogMessagePrefix
    {
        public static bool Prefix(ref string strMsg)
        {
            RusPatchPlugin._logMsgCalls++;
            // Block empty messages (game bug: tiles and some conditions produce empty LogMessage calls)
            if (string.IsNullOrEmpty(strMsg) || strMsg.Trim().Length == 0)
                return false;
            // Always call Clean() — even pure Cyrillic needs verb conjugation fix
            strMsg = RussianTextCleaner.Clean(strMsg);
            // Block if Clean() produced empty result
            if (string.IsNullOrEmpty(strMsg) || strMsg.Trim().Length == 0)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Prefix for TMPro.TMP_Text.SetText(StringBuilder) — catches tooltip text that bypasses
    /// the text property setter. The game may use StringBuilder path which stores text in
    /// internal char array without updating m_text field.
    /// </summary>
    public static class SetTextSBPrefix
    {
        public static void Prefix(System.Text.StringBuilder sourceText)
        {
            if (sourceText == null || sourceText.Length < 3) return;
            string s = sourceText.ToString();

            bool needsClean = s.IndexOf("{ls ") >= 0 || RussianTextCleaner.HasLatin(s);
            if (!needsClean)
            {
                if (s.IndexOf("Вы ") >= 0 || s.IndexOf("вы ") >= 0)
                {
                    string vy = RussianTextCleaner.Clean(s);
                    if (vy != s) { sourceText.Remove(0, sourceText.Length); sourceText.Append(vy); }
                }
                return;
            }

            string cleaned = RussianTextCleaner.StripLsBrackets(s);
            cleaned = RussianTextCleaner.Clean(cleaned);
            if (cleaned != s)
            {
                sourceText.Remove(0, sourceText.Length);
                sourceText.Append(cleaned);
            }
        }
    }

    /// <summary>
    /// Prefix for TMPro.TMP_Text.SetCharArray(char[], int, int) — catches text set via char array.
    /// </summary>
    public static class SetCharArrayPrefix
    {
        public static void Prefix(ref char[] sourceText, ref int start, ref int length)
        {
            if (sourceText == null || length < 3) return;
            string s = new string(sourceText, start, length);

            bool needsClean = RussianTextCleaner.HasLatin(s);
            if (!needsClean)
            {
                if (s.IndexOf("Вы ") >= 0 || s.IndexOf("вы ") >= 0)
                {
                    string vy = RussianTextCleaner.Clean(s);
                    if (vy != s)
                    {
                        sourceText = vy.ToCharArray();
                        start = 0;
                        length = sourceText.Length;
                    }
                }
                return;
            }

            string cleaned = RussianTextCleaner.StripLsBrackets(s);
            cleaned = RussianTextCleaner.Clean(cleaned);
            if (cleaned != s)
            {
                sourceText = cleaned.ToCharArray();
                start = 0;
                length = sourceText.Length;
            }
        }
    }

}
