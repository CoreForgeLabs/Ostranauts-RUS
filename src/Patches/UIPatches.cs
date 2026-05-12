using System;
using HarmonyLib;
using UnityEngine;

namespace OstranautsRusPatch
{
    /// <summary>
    /// Harmony Finalizer for TMPro.TMP_Text.FillCharacterVertexBuffers.
    /// Suppresses IndexOutOfRangeException and logs diagnostic info about the problematic text.
    /// </summary>
    public static class TMProCrashFinalizer
    {
        private static int _suppressCount;
        private static bool _fixing; // re-entrancy guard

        public static Exception Finalizer(Exception __exception, TMPro.TMP_Text __instance)
        {
            if (__exception is System.IndexOutOfRangeException)
            {
                if (_fixing) return null; // prevent infinite loop

                _suppressCount++;
                string txt = "null";
                try { txt = __instance.text; } catch { }
                int charCount = -1;
                try
                {
                    if (__instance.textInfo != null)
                        charCount = __instance.textInfo.characterCount;
                }
                catch { }
                string fontName = "unknown";
                try
                {
                    if (__instance.font != null)
                        fontName = __instance.font.name;
                }
                catch { }

                // --- ROOT CAUSE FIX: swap font if it can't render Cyrillic ---
                // NotoSansSC (Simplified Chinese) has no Cyrillic glyphs → chars=0 → IndexOutOfRange.
                // Prefer NotoSans-Regular SDF (broad Unicode + Jura Cyrillic fallback).
                // Fall back to Jura-Bold Cyrillic if NotoSans not available.
                bool fontSwapped = false;
                if (charCount == 0)
                {
                    try
                    {
                        // Check if text has any Cyrillic character (quick scan)
                        bool hasCyrillic = false;
                        if (txt != null)
                        {
                            for (int i = 0; i < txt.Length; i++)
                            {
                                char c = txt[i];
                                if (c >= '\u0400' && c <= '\u04FF') { hasCyrillic = true; break; }
                            }
                        }
                        if (hasCyrillic)
                        {
                            object targetFont = TextOnEnablePostfix._notoSans ?? TextOnEnablePostfix._juraCyrillic;
                            if (targetFont != null)
                            {
                                _fixing = true;
                                try
                                {
                                    __instance.font = (TMPro.TMP_FontAsset)targetFont;
                                    // Prevent Ellipsis character warning — many SDF fonts lack u+2026.
                                    // Switch overflow mode from Ellipsis to Truncate.
                                    try
                                    {
                                        if (__instance.overflowMode == TMPro.TextOverflowModes.Ellipsis)
                                            __instance.overflowMode = TMPro.TextOverflowModes.Truncate;
                                    }
                                    catch { }
                                    fontSwapped = true;
                                }
                                finally
                                {
                                    _fixing = false;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (_suppressCount <= 10) // log first 10 occurrences
                {
                    string goName = "unknown";
                    try { goName = __instance.gameObject.name; } catch { }
                    string goParent = "";
                    try
                    {
                        if (__instance.transform.parent != null)
                            goParent = __instance.transform.parent.gameObject.name;
                    }
                    catch { }
                    RusPatchPlugin.Log.LogWarning(
                        "[TMPRO-FIX] #" + _suppressCount +
                        " chars=" + charCount +
                        " font=\"" + fontName + "\"" +
                        (fontSwapped ? " -> SWAPPED to " + ((UnityEngine.Object)__instance.font).name : "") +
                        " go=\"" + goName + "\"" +
                        " parent=\"" + goParent + "\"" +
                        " text=\"" + (txt != null && txt.Length > 120 ? txt.Substring(0, 120) + "..." : txt) + "\"");
                }
                return null; // suppress
            }
            return __exception;
        }
    }

    /// <summary>
    /// Postfix for GUITooltip.TooltipTextFormat1-4 — translates hardcoded English labels
    /// in item/interaction tooltips. These methods build tooltip text via String.Concat
    /// and the result bypasses TMP_Text.set_text Harmony hooks, so we patch the source.
    /// </summary>
    public static class TooltipFormatPostfix
    {
        public static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || __result.Length < 3) return;

            // --- Tooltip-specific label replacements (TooltipTextFormat1) ---
            __result = __result
                .Replace("\nCondition: ", "\n\u0421\u043e\u0441\u0442\u043e\u044f\u043d\u0438\u0435: ")   // Состояние
                .Replace("\nMass: ", "\n\u041c\u0430\u0441\u0441\u0430: ")                                 // Масса
                .Replace("\nMass of stack: ", "\n\u041c\u0430\u0441\u0441\u0430 \u0441\u0442\u043e\u043f\u043a\u0438: ") // Масса стопки
                .Replace("\nCharge: ", "\n\u0417\u0430\u0440\u044f\u0434: ")                               // Заряд
                .Replace("\nPressure: ", "\n\u0414\u0430\u0432\u043b\u0435\u043d\u0438\u0435: ")           // Давление
                .Replace("kg", "\u043a\u0433");                                                            // кг

            // --- TooltipTextFormat2 labels ---
            __result = __result
                .Replace("Install job requires ", "\u0414\u043b\u044f \u0443\u0441\u0442\u0430\u043d\u043e\u0432\u043a\u0438 \u0442\u0440\u0435\u0431\u0443\u0435\u0442\u0441\u044f ") // Для установки требуется
                .Replace("\n\nParts in lot: ", "\n\n\u0414\u0435\u0442\u0430\u043b\u0438 \u0432 \u043b\u043e\u0442\u0435: ") // Детали в лоте
                .Replace("\n\nInstall Progress: ", "\n\n\u041f\u0440\u043e\u0433\u0440\u0435\u0441\u0441 \u0443\u0441\u0442\u0430\u043d\u043e\u0432\u043a\u0438: ") // Прогресс установки
                .Replace("\n\nUninstall Progress: ", "\n\n\u041f\u0440\u043e\u0433\u0440\u0435\u0441\u0441 \u0434\u0435\u043c\u043e\u043d\u0442\u0430\u0436\u0430: "); // Прогресс демонтажа

            // --- TooltipTextFormat3 labels ---
            __result = __result
                .Replace("\nTools required:\n", "\n\u041d\u0435\u043e\u0431\u0445\u043e\u0434\u0438\u043c\u044b\u0435 \u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442\u044b:\n") // Необходимые инструменты
                .Replace("\nInput items required:\n", "\n\u041d\u0435\u043e\u0431\u0445\u043e\u0434\u0438\u043c\u044b\u0435 \u043c\u0430\u0442\u0435\u0440\u0438\u0430\u043b\u044b:\n") // Необходимые материалы
                .Replace("\nParts in lot:", "\n\u0414\u0435\u0442\u0430\u043b\u0438 \u0432 \u043b\u043e\u0442\u0435:"); // Детали в лоте

            // --- TooltipTextFormat4 labels ---
            __result = __result
                .Replace("\n<b>We need:</b>\n", "\n<b>\u041d\u0430\u043c \u043d\u0443\u0436\u043d\u043e:</b>\n")         // Нам нужно
                .Replace("\n<b>We can't be:</b>\n", "\n<b>\u041d\u0435\u0434\u043e\u043f\u0443\u0441\u0442\u0438\u043c\u044b\u0435 \u0443\u0441\u043b\u043e\u0432\u0438\u044f:</b>\n") // Недопустимые условия
                .Replace("\n<b>Effects:</b>\n", "\n<b>\u042d\u0444\u0444\u0435\u043a\u0442\u044b:</b>\n")                 // Эффекты
                .Replace("\n<b>Tools required:</b>\n", "\n<b>\u041d\u0435\u043e\u0431\u0445\u043e\u0434\u0438\u043c\u044b\u0435 \u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442\u044b:</b>\n") // Необходимые инструменты
                .Replace("\n<b>Items given:</b>\n", "\n<b>\u0412\u044b\u0434\u0430\u043d\u043d\u044b\u0435 \u043f\u0440\u0435\u0434\u043c\u0435\u0442\u044b:</b>\n") // Выданные предметы
                .Replace("\n<b>Items consumed:</b>\n", "\n<b>\u041f\u043e\u0442\u0440\u0435\u0431\u043b\u044f\u0435\u043c\u044b\u0435 \u043f\u0440\u0435\u0434\u043c\u0435\u0442\u044b:</b>\n"); // Потребляемые предметы

            // Also run general Clean for any remaining English artifacts
            if (RussianTextCleaner.HasLatin(__result))
                __result = RussianTextCleaner.Clean(__result);
        }
    }

    /// <summary>
    /// Placeholder for future Relationship-specific patches.
    /// </summary>
    public static class RelationshipLogPatch
    {
        public static void AddRelationshipPostfix() { }
    }
}
