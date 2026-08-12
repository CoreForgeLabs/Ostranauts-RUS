using System;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OstraI18n
{
    // Installs a Cyrillic-capable font into TMP's global fallback chain, so any game font
    // missing Cyrillic glyphs falls back instead of rendering blank squares.
    // The game already ships "NotoSansGC-Regular SDF" (full Cyrillic block) — we reuse it.
    // If it is missing (future update), we build a dynamic atlas from a bundled Cyrillic TTF.
    internal static class FontFallback
    {
        private static bool _done;
        private static int _attempts;

        // Harmony postfix on TMP_Settings.get_instance — fires on main thread when TMP initializes.
        public static void AfterSettingsInit(TMPro.TMP_Settings __instance)
        {
            if (_done || !LangPack.Active || _attempts >= 5) return;
            _attempts++;
            try
            {
                TMP_FontAsset cyr = null;
                foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (fa != null && fa.name == "NotoSansGC-Regular SDF") { cyr = fa; break; }
                }
                if (cyr == null)
                {
                    Font src = null;
                    foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                    {
                        if (f != null && (f.name == "NotoSans-Regular" || f.name.StartsWith("Roboto-Regular"))) { src = f; break; }
                    }
                    if (src != null)
                    {
                        cyr = TMP_FontAsset.CreateFontAsset(src, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                        cyr.name = "OstraI18n Cyrillic Dynamic";
                    }
                }
                if (cyr == null)
                {
                    Plugin.Log.LogWarning("[i18n] no Cyrillic-capable font found (attempt " + _attempts + ")");
                    return;
                }
                var list = TMP_Settings.fallbackFontAssets;
                if (list == null)
                {
                    Plugin.Log.LogWarning("[i18n] TMP fallbackFontAssets is null (attempt " + _attempts + ")");
                    return;
                }
                if (!list.Contains(cyr)) list.Add(cyr);
                _done = true;
                Plugin.Log.LogInfo("[i18n] Cyrillic fallback font installed: " + cyr.name);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] FontFallback failed (attempt " + _attempts + "): " + ex.Message);
            }
        }
    }
}
