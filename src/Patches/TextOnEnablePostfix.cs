using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OstranautsRusPatch
{
    public static class TextOnEnablePostfix
    {
        [ThreadStatic] private static bool _inClean;
        private static readonly bool EnableOnEnableFontEnhance = false;
        private static bool _fontEnhanceSkipLogged;
        private static readonly object _fontGuardLock = new object();
        private static readonly HashSet<int> _enhancedComponentIds = new HashSet<int>();
        private static int _diagWhatsNewCount;
        private static bool _whatsNewFontFixLogged;

        // Prefab labels from character creation UI that bypass normal text pipelines.
        private static readonly Dictionary<string, string> ChargenBodyPartTranslations =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "SKIN", "КОЖА" },
            { "HAIR", "ВОЛОСЫ" },
            { "SCAR", "ШРАМ" },
            { "GLASSES", "ОЧКИ" },
            { "BEARD", "БОРОДА" },
            { "PUPILS", "ЗРАЧКИ" },
            { "EYES", "ГЛАЗА" },
            { "NOSE", "НОС" },
            { "TEETH", "ЗУБЫ" },
            { "LIPS", "ГУБЫ" },
            { "NECK", "ШЕЯ" },
            { "HEAD", "ГОЛОВА" },
            { "BODY", "ТЕЛО" }
        };

        // Cache PropertyInfo per Type to avoid repeated reflection
        private static readonly Dictionary<Type, PropertyInfo> _propCache =
            new Dictionary<Type, PropertyInfo>();

        // Track already-enhanced shared materials to avoid redundant work
        private static readonly HashSet<int> _enhancedMaterials = new HashSet<int>();
        private static int _dilateID = -1;
        private static PropertyInfo _fontSharedMatProp;
        private static PropertyInfo _fontProp;          // TMP_Text.font (TMP_FontAsset)
        private static FieldInfo _fallbackField;         // TMP_FontAsset.fallbackFontAssets
        private static bool _fontPropsResolved;

        // Cached font assets for replacement
        private static object _robotoMedium;             // TMP_FontAsset reference
        private static object _robotoRegular;            // TMP_FontAsset reference
        internal static object _notoSans;                 // for fallback chain
        internal static object _juraCyrillic;             // Jura-Bold SDF Cyrillic & Greek
        private static bool _fontsScanned;
        private static readonly HashSet<int> _fontReplaced = new HashSet<int>(); // track replaced TMP components

        // Per-font FaceDilate map: lowercase material-name substring -> dilate value
        // Order matters: first match wins
        private static readonly string[] _dilateKeys = new string[] {
            "roboto-light",     // thin → needs more
            "roboto-regular",
            "roboto-medium",
            "roboto-black",     // bold → skip
            "robotocondensedb", // bold → skip
            "robotocondensed",
            "montserrat",       // bold → skip
            "jura-bold",        // bold → skip
            "jura-regular",
            "jura",             // fallback for jura variants
            "museomoderno-semi",// semi → skip
            "museomoderno-regular",
            "museomoderno",     // other bold variants → skip
            "kodemono",
            "liberationsans",
            "notosanssc",
            "notosans",
            "statbar",
            "infotitle",
            "comfortaa",
            "jost-bold",        // bold → skip
            "jost"
        };
        private static readonly float[] _dilateVals = new float[] {
            0.18f,   // roboto-light → extra dilate for thin font
            0.08f,   // roboto-regular
            0.04f,   // roboto-medium (already semi-bold)
            -1f,     // roboto-black → SKIP
            -1f,     // robotocondensedb → SKIP
            0.06f,   // robotocondensed
            -1f,     // montserrat (bold) → SKIP
            -1f,     // jura-bold → SKIP
            0.12f,   // jura-regular
            0.10f,   // jura (other)
            -1f,     // museomoderno-semi → SKIP
            0.10f,   // museomoderno-regular
            -1f,     // museomoderno (other bold variants) → SKIP
            0.06f,   // kodemono
            0.10f,   // liberationsans
            0.08f,   // notosanssc (CJK)
            0.08f,   // notosans
            0.12f,   // statbar txt
            0.10f,   // infotitle
            0.08f,   // comfortaa
            -1f,     // jost-bold → SKIP
            0.08f    // jost
        };

        // Font replacement map: font name substring → "medium", "regular", or null (no replace)
        private static readonly string[] _replaceKeys = new string[] {
            "Roboto-Light"
        };
        private static readonly string[] _replaceTargets = new string[] {
            "medium"   // Light → Medium for better Cyrillic readability
        };

        public static void Postfix(object __instance)
        {
            if (_inClean) return;
            _inClean = true;
            try
            {
                Type t = __instance.GetType();
                string typeName = t.FullName;
                PropertyInfo textProp;
                if (!_propCache.TryGetValue(t, out textProp))
                {
                    textProp = t.GetProperty("text",
                        BindingFlags.Public | BindingFlags.Instance);
                    _propCache[t] = textProp;
                }
                // --- Font replacement & per-font FaceDilate ---
                // Must run BEFORE text check: many TMP components have empty text at OnEnable
                if (!string.IsNullOrEmpty(typeName) && typeName.IndexOf("TMPro", StringComparison.Ordinal) >= 0)
                {
                    if (EnableOnEnableFontEnhance)
                    {
                        UnityEngine.Object uobj = __instance as UnityEngine.Object;
                        if (!object.ReferenceEquals(uobj, null))
                        {
                            int compId = uobj.GetInstanceID();
                            bool shouldEnhance = false;
                            lock (_fontGuardLock)
                            {
                                if (_enhancedComponentIds.Count > 8192)
                                    _enhancedComponentIds.Clear();
                                if (!_enhancedComponentIds.Contains(compId))
                                {
                                    _enhancedComponentIds.Add(compId);
                                    shouldEnhance = true;
                                }
                            }
                            if (shouldEnhance)
                                EnhanceFontWeight(__instance, t);
                        }
                    }
                    else if (!_fontEnhanceSkipLogged && !object.ReferenceEquals(RusPatchPlugin.Log, null))
                    {
                        _fontEnhanceSkipLogged = true;
                        RusPatchPlugin.Log.LogInfo("[RusPatch] OnEnable font enhance skipped (perf mode)");
                    }
                }

                if (object.ReferenceEquals(textProp, null)) return;

                string val = textProp.GetValue(__instance, null) as string;
                if (string.IsNullOrEmpty(val) || val.Length < 3) return;

                TryFixWhatsNewHeaderFont(__instance, val);

                if (_diagWhatsNewCount < 5 && IsWhatsNewHeader(val))
                {
                    _diagWhatsNewCount++;
                    string compType = t.FullName;
                    string goName = "<no-go>";
                    string fontName = "<no-font>";
                    string matName = "<no-mat>";

                    var comp = __instance as Component;
                    if (!object.ReferenceEquals(comp, null))
                    {
                        goName = comp.gameObject != null ? comp.gameObject.name : "<no-go>";
                    }

                    var tmp = __instance as TMPro.TMP_Text;
                    if (!object.ReferenceEquals(tmp, null))
                    {
                        if (!object.ReferenceEquals(tmp.font, null)) fontName = tmp.font.name;
                        if (!object.ReferenceEquals(tmp.fontSharedMaterial, null)) matName = tmp.fontSharedMaterial.name;
                    }

                    RusPatchPlugin.Log.LogInfo(
                        "[DIAG-WHATSNEW] type=" + compType +
                        " go=" + goName +
                        " font=" + fontName +
                        " mat=" + matName +
                        " text='" + val + "'" +
                        " unicode=" + BuildUnicodeCodes(val));
                }

                string chargenTranslation;
                if (ChargenBodyPartTranslations.TryGetValue(val, out chargenTranslation))
                {
                    textProp.SetValue(__instance, chargenTranslation, null);
                    return;
                }

                // Remove CJK characters from Renbao Console header to avoid TMP font fallback warnings
                // (KodeMono lacks CJK glyphs; NotoSansSC may be absent as a fallback).
                val = val.Replace("仁保仪表板", "Renbao Console");

                string cleaned = RussianTextCleaner.Clean(val);
                if (cleaned != val)
                {
                    textProp.SetValue(__instance, cleaned, null);
                }
            }
            catch { }
            finally
            {
                _inClean = false;
            }
        }

        private static void TryFixWhatsNewHeaderFont(object instance, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var tmp = instance as TMPro.TMP_Text;
            if (object.ReferenceEquals(tmp, null)) return;

            var comp = instance as Component;
            string goName = (!object.ReferenceEquals(comp, null) && !object.ReferenceEquals(comp.gameObject, null))
                ? comp.gameObject.name
                : string.Empty;

            bool isTargetObject = !string.IsNullOrEmpty(goName)
                && goName.IndexOf("whatsnew", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isTargetObject && !IsWhatsNewHeader(text)) return;

            var target = ResolveCyrillicCapableFont(tmp.font);
            if (object.ReferenceEquals(target, null))
            {
                ScanFonts();
                target = (_juraCyrillic ?? _notoSans) as TMPro.TMP_FontAsset;
            }
            if (object.ReferenceEquals(target, null)) return;

            if (!object.ReferenceEquals(tmp.font, target))
            {
                tmp.font = target;
                if (!object.ReferenceEquals(target.material, null))
                    tmp.fontSharedMaterial = target.material;

                if (!_whatsNewFontFixLogged && !object.ReferenceEquals(RusPatchPlugin.Log, null))
                {
                    _whatsNewFontFixLogged = true;
                    RusPatchPlugin.Log.LogInfo("[RusPatch] Applied Cyrillic font fix for What's New header: " + target.name + " (go=" + goName + ")");
                }
            }
        }

        private static TMPro.TMP_FontAsset ResolveCyrillicCapableFont(TMPro.TMP_FontAsset baseFont)
        {
            if (object.ReferenceEquals(baseFont, null)) return null;
            if (HasCyrillicGlyphs(baseFont)) return baseFont;

            var fallback = baseFont.fallbackFontAssetTable;
            if (object.ReferenceEquals(fallback, null)) return null;

            for (int i = 0; i < fallback.Count; i++)
            {
                var candidate = fallback[i];
                if (object.ReferenceEquals(candidate, null)) continue;
                if (HasCyrillicGlyphs(candidate)) return candidate;
            }

            return null;
        }

        private static bool HasCyrillicGlyphs(TMPro.TMP_FontAsset font)
        {
            return !object.ReferenceEquals(font, null)
                && font.HasCharacter('\u041d')
                && font.HasCharacter('\u043d');
        }

        private static bool IsWhatsNewHeader(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("что нового", System.StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("what's new", System.StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("what’s new", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildUnicodeCodes(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                int cp = char.ConvertToUtf32(text, i);
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("U+");
                sb.Append(cp.ToString("X4"));
                if (cp > 0xFFFF) i++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Lazy scan for all loaded TMP_FontAsset objects.
        /// Must be called after the scene is loaded (fonts live in shared/resources assets).
        /// </summary>
        private static void ScanFonts()
        {
            if (_fontsScanned) return;
            _fontsScanned = true;
            try
            {
                Type faType = AccessTools.TypeByName("TMPro.TMP_FontAsset");
                if (object.ReferenceEquals(faType, null)) return;

                // Get fallbackFontAssets field 
                _fallbackField = faType.GetField("fallbackFontAssets",
                    BindingFlags.Public | BindingFlags.Instance);

                object[] allFonts = Resources.FindObjectsOfTypeAll(faType);
                if (allFonts == null) return;

                foreach (object fa in allFonts)
                {
                    UnityEngine.Object uobj = fa as UnityEngine.Object;
                    if (object.ReferenceEquals(uobj, null)) continue;
                    string fname = uobj.name;
                    if (string.IsNullOrEmpty(fname)) continue;

                    string fl = fname.ToLowerInvariant();
                    if (fl.Contains("roboto-medium") && _robotoMedium == null)
                        _robotoMedium = fa;
                    else if (fl.Contains("roboto-regular") && fl.Contains("sdf") && _robotoRegular == null)
                        _robotoRegular = fa;
                    // NotoSansGC = Greek/Cyrillic variant — best target for font swap (has Cyrillic + symbols)
                    // Also accept NotoSansKR (Korean+all), which covers Cyrillic glyphs in Unity 6 asset build
                    if (fl.Contains("notosansgc") && fl.Contains("sdf") && _notoSans == null)
                        _notoSans = fa;
                    else if (fl.Contains("notosanskr") && fl.Contains("sdf") && _notoSans == null)
                        _notoSans = fa;
                    else if (fl.Contains("notosans") && fl.Contains("latin") && fl.Contains("sdf") && _notoSans == null)
                        _notoSans = fa;
                    if (fl.Contains("jura") && fl.Contains("cyrillic") && _juraCyrillic == null)
                        _juraCyrillic = fa;

                    RusPatchPlugin.Log.LogInfo("[RusPatch] Font found: " + fname);
                }

                // Add Jura-Bold Cyrillic & Greek as PRIORITY fallback to all font assets
                // This ensures Cyrillic text uses Jura style instead of NotoSans
                if (_juraCyrillic != null && !object.ReferenceEquals(_fallbackField, null))
                {
                    int juraAdded = 0;
                    foreach (object fa in allFonts)
                    {
                        if (object.ReferenceEquals(fa, _juraCyrillic)) continue;
                        try
                        {
                            object fbList = _fallbackField.GetValue(fa);
                            if (fbList == null)
                            {
                                Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(faType);
                                fbList = Activator.CreateInstance(listType);
                                _fallbackField.SetValue(fa, fbList);
                            }
                            System.Collections.IList list = fbList as System.Collections.IList;
                            if (list != null && !list.Contains(_juraCyrillic))
                            {
                                list.Insert(0, _juraCyrillic); // Insert at position 0 = highest priority
                                juraAdded++;
                            }
                        }
                        catch { }
                    }
                    RusPatchPlugin.Log.LogInfo("[RusPatch] Jura Cyrillic fallback added to " + juraAdded + " fonts (priority)");
                }
                else if (_juraCyrillic == null)
                {
                    RusPatchPlugin.Log.LogWarning("[RusPatch] Jura-Bold SDF Cyrillic & Greek NOT FOUND in game assets!");
                }

                // Add NotoSans as fallback to all font assets
                if (_notoSans != null && !object.ReferenceEquals(_fallbackField, null))
                {
                    foreach (object fa in allFonts)
                    {
                        if (object.ReferenceEquals(fa, _notoSans)) continue;
                        try
                        {
                            object fbList = _fallbackField.GetValue(fa);
                            // fallbackFontAssets is List<TMP_FontAsset>
                            if (fbList == null)
                            {
                                // Create new list
                                Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(faType);
                                fbList = Activator.CreateInstance(listType);
                                _fallbackField.SetValue(fa, fbList);
                            }
                            // Check if already contains notoSans
                            System.Collections.IList list = fbList as System.Collections.IList;
                            if (list != null && !list.Contains(_notoSans))
                            {
                                list.Add(_notoSans);
                            }
                        }
                        catch { }
                    }
                    RusPatchPlugin.Log.LogInfo("[RusPatch] NotoSans fallback added to " + allFonts.Length + " fonts");
                }
            }
            catch (Exception ex)
            {
                RusPatchPlugin.Log.LogWarning("[RusPatch] ScanFonts error: " + ex.Message);
            }
        }

        private static void EnhanceFontWeight(object instance, Type t)
        {
            try
            {
                // Resolve font properties once
                if (!_fontPropsResolved)
                {
                    try
                    {
                        _dilateID = Shader.PropertyToID("_FaceDilate");
                    }
                    catch (Exception ex)
                    {
                        RusPatchPlugin.Log.LogWarning("[RusPatch] Shader.PropertyToID failed: " + ex.Message);
                        _dilateID = -1;
                    }

                    Type tmpTextType = null;
                    try
                    {
                        tmpTextType = AccessTools.TypeByName("TMPro.TMP_Text");
                        if (object.ReferenceEquals(tmpTextType, null))
                        {
                            tmpTextType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
                            if (object.ReferenceEquals(tmpTextType, null))
                                tmpTextType = AccessTools.TypeByName("TMPro.TextMeshPro");
                        }
                        // TMP type resolved
                    }
                    catch (Exception ex)
                    {
                        RusPatchPlugin.Log.LogWarning("[RusPatch] TMP type lookup failed: " + ex.Message);
                    }

                    if (!object.ReferenceEquals(tmpTextType, null))
                    {
                        _fontSharedMatProp = tmpTextType.GetProperty("fontSharedMaterial",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (object.ReferenceEquals(_fontSharedMatProp, null))
                            _fontSharedMatProp = tmpTextType.GetProperty("fontSharedMaterial",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                        _fontProp = tmpTextType.GetProperty("font",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (object.ReferenceEquals(_fontProp, null))
                            _fontProp = tmpTextType.GetProperty("font",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                        // font props resolved
                    }

                    ScanFonts();
                    _fontPropsResolved = true;
                    RusPatchPlugin.Log.LogInfo("[RusPatch] Font system initialized");
                }
                if (object.ReferenceEquals(_fontSharedMatProp, null)) return;

                // --- Font replacement (Light → Medium) ---
                if (!object.ReferenceEquals(_fontProp, null))
                {
                    int instID = ((UnityEngine.Object)instance).GetInstanceID();
                    if (!_fontReplaced.Contains(instID))
                    {
                        _fontReplaced.Add(instID);
                        object curFont = _fontProp.GetValue(instance, null);
                        if (curFont != null)
                        {
                            string curName = ((UnityEngine.Object)curFont).name;
                            if (!string.IsNullOrEmpty(curName))
                            {
                                for (int r = 0; r < _replaceKeys.Length; r++)
                                {
                                    if (curName.IndexOf(_replaceKeys[r], StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        object target = null;
                                        if (_replaceTargets[r] == "medium") target = _robotoMedium;
                                        else if (_replaceTargets[r] == "regular") target = _robotoRegular;

                                        if (target != null)
                                        {
                                            _fontProp.SetValue(instance, target, null);
                                            RusPatchPlugin.Log.LogInfo("[RusPatch] Font swap: " + curName + " -> " + ((UnityEngine.Object)target).name);
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                // --- Per-font FaceDilate ---
                Material mat = _fontSharedMatProp.GetValue(instance, null) as Material;
                if (object.ReferenceEquals(mat, null)) return;

                int matID = mat.GetInstanceID();
                if (_enhancedMaterials.Contains(matID)) return;
                _enhancedMaterials.Add(matID);

                string matName = mat.name;
                if (string.IsNullOrEmpty(matName)) return;
                string lower = matName.ToLowerInvariant();

                // Find per-font dilate value
                float dilate = 0.08f; // default for unknown fonts
                for (int i = 0; i < _dilateKeys.Length; i++)
                {
                    if (lower.Contains(_dilateKeys[i]))
                    {
                        dilate = _dilateVals[i];
                        break;
                    }
                }

                // -1 means SKIP (bold/black fonts)
                if (dilate < 0f)
                {
                    RusPatchPlugin.Log.LogInfo("[RusPatch] Font dilate SKIP: " + matName);
                    return;
                }

                if (mat.HasProperty(_dilateID))
                {
                    float current = mat.GetFloat(_dilateID);
                    if (current < dilate - 0.01f) // only increase, don't decrease
                    {
                        mat.SetFloat(_dilateID, dilate);
                        RusPatchPlugin.Log.LogInfo("[RusPatch] Font dilate: " + matName + " " + current + " -> " + dilate);
                    }
                }
            }
            catch (Exception ex)
            {
                RusPatchPlugin.Log.LogWarning("[RusPatch] EnhanceFontWeight error: " + ex.Message);
            }
        }
    }
}
