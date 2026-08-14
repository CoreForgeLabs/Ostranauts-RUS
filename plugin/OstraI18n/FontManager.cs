using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OstraI18n
{
    /// <summary>
    /// Manages per-language modular fonts from langs/{lang}/fonts/ folder.
    /// Automatically converts TTF/OTF fonts to dynamic SDFAA TextMeshPro Font Assets
    /// and maintains them across language switches.
    /// </summary>
    public static class FontManager
    {
        private static readonly Dictionary<string, TMP_FontAsset> _loadedPackFonts = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);
        public static TMP_FontAsset ActiveFontAsset { get; private set; }
        private static bool _initialized;

        /// <summary>
        /// Initializes the font system when TextMeshPro settings are ready.
        /// </summary>
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                if (LangPack.Active && !string.IsNullOrEmpty(LangPack.Code))
                {
                    var packDir = Path.Combine(Plugin.DataDir.Value, "langs", LangPack.Code);
                    LoadFontsForLanguage(LangPack.Code, packDir);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] FontManager.Init error: " + ex);
            }
        }

        /// <summary>
        /// Loads or switches the active font for the specified language pack directory.
        /// </summary>
        public static void LoadFontsForLanguage(string langCode, string packDir)
        {
            if (string.IsNullOrEmpty(langCode)) return;
            if (string.IsNullOrEmpty(packDir) || !Directory.Exists(packDir))
                packDir = Path.Combine(Plugin.DataDir.Value, "langs", langCode);

            try
            {
                var fontsDir = Path.Combine(packDir, "fonts");
                TMP_FontAsset fontAsset = null;

                if (_loadedPackFonts.TryGetValue(langCode, out var cached) && cached != null)
                {
                    fontAsset = cached;
                }
                else if (Directory.Exists(fontsDir))
                {
                    var fontFiles = Directory.GetFiles(fontsDir, "*.*", SearchOption.TopDirectoryOnly);
                    string bestFile = null;

                    // Prioritize Jura, RobotoCondensed, or any TTF/OTF
                    foreach (var f in fontFiles)
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".ttf" && ext != ".otf") continue;

                        var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        if (name.Contains("jura") || name.Contains("bold"))
                        {
                            bestFile = f;
                            break;
                        }
                        if (bestFile == null) bestFile = f;
                    }

                    if (bestFile != null)
                    {
                        fontAsset = CreateFontAssetFromFile(bestFile, langCode);
                        if (fontAsset != null)
                        {
                            _loadedPackFonts[langCode] = fontAsset;
                            Plugin.Log.LogInfo($"[i18n] Loaded modular font for [{langCode}]: {Path.GetFileName(bestFile)}");
                        }
                    }
                }

                // Fallback to built-in NotoSansGC if language pack has no custom font
                if (fontAsset == null)
                {
                    foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                    {
                        if (fa != null && fa.name == "NotoSansGC-Regular SDF")
                        {
                            fontAsset = fa;
                            break;
                        }
                    }
                }

                if (fontAsset != null)
                {
                    ActiveFontAsset = fontAsset;
                    ApplyFallbackToTMP(fontAsset);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[i18n] LoadFontsForLanguage [{langCode}] error: " + ex);
            }
        }

        private static TMP_FontAsset CreateFontAssetFromFile(string fontFilePath, string langCode)
        {
            try
            {
                var unityFont = new Font(fontFilePath);
                if (unityFont != null)
                {
                    var fontAsset = TMP_FontAsset.CreateFontAsset(
                        unityFont,
                        90,
                        9,
                        GlyphRenderMode.SDFAA,
                        2048,
                        2048,
                        AtlasPopulationMode.Dynamic,
                        true
                    );
                    if (fontAsset != null)
                    {
                        fontAsset.name = $"OstraI18n_{langCode}_{Path.GetFileNameWithoutExtension(fontFilePath)}";
                        return fontAsset;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[i18n] CreateFontAssetFromFile failed for {fontFilePath}: " + ex.Message);
            }

            return null;
        }

        private static void ApplyFallbackToTMP(TMP_FontAsset cyr)
        {
            if (cyr == null) return;

            try
            {
                var list = TMP_Settings.fallbackFontAssets;
                if (list != null)
                {
                    list.Remove(cyr);
                    list.Insert(0, cyr); // Insert as first fallback priority
                }

                // Also inject into all currently loaded game font assets' fallback tables
                foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (fa == null || fa == cyr) continue;
                    if (fa.fallbackFontAssetTable != null)
                    {
                        if (!fa.fallbackFontAssetTable.Contains(cyr))
                        {
                            fa.fallbackFontAssetTable.Insert(0, cyr);
                        }
                    }
                }

                Plugin.Log.LogInfo($"[i18n] Active font fallback set to: {cyr.name}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] ApplyFallbackToTMP warning: " + ex.Message);
            }
        }

        /// <summary>
        /// Ensures the specified text component uses the active modular font.
        /// </summary>
        public static void EnsureFont(TMP_Text text)
        {
            if (text == null) return;
            if (ActiveFontAsset != null && text.font != ActiveFontAsset)
            {
                text.font = ActiveFontAsset;
            }
        }
    }
}
