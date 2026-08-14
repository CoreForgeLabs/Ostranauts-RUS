using System;
using TMPro;
using UnityEngine;

namespace OstraI18n
{
    // Installs a Cyrillic-capable font into TMP's global fallback chain, so any game font
    // missing Cyrillic glyphs falls back instead of rendering blank squares.
    // Delegates to FontManager for modular per-language font loading.
    internal static class FontFallback
    {
        public static TMP_FontAsset CyrillicFontAsset => FontManager.ActiveFontAsset;

        public static void EnsureCyrillicFont(TMP_Text text)
        {
            FontManager.EnsureFont(text);
        }

        // Harmony postfix on TMP_Settings.get_instance — fires on main thread when TMP initializes.
        public static void AfterSettingsInit(TMPro.TMP_Settings __instance)
        {
            FontManager.Init();
        }
    }
}
