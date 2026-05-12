using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;

namespace OstranautsRusPatch
{
    /// <summary>
    /// BepInEx plugin that fixes Russian translation issues in Ostranauts.
    /// 
    /// WHAT IT DOES:
    /// 1. Replaces English pronoun tables (he/she/they/his/her/their) with Russian
    /// 2. Strips English articles (the/a/an) before Cyrillic text
    /// 3. Removes possessive 's before Cyrillic text
    /// 4. Cleans up double spaces and other artifacts
    /// 5. Translates hardcoded UI labels (ship info panel, chargen career buttons)
    /// 6. Translates ship designations and descriptions
    /// 
    /// WHY IT WORKS:
    /// GrammarUtils.GenerateString() hardcodes English grammar:
    /// - Prepends "the " before non-human entity names
    /// - Inserts English pronouns from partsOfSpeech dictionary
    /// - Adds "'s" for possessive forms
    /// 
    /// PORTABLE ACROSS VERSIONS:
    /// - Uses Harmony runtime patching (no IL/Cecil editing)
    /// - Patches by method name (survives game updates)
    /// - Safe failure if method signatures change
    /// </summary>
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class RusPatchPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.ostranauts.ruspatch";
        public const string PLUGIN_NAME = "Ostranauts Russian Patch";
        public const string PLUGIN_VERSION = "1.0.0";

        internal static BepInEx.Logging.ManualLogSource Log;

        internal static RusPatchPlugin Instance;

        // --- Performance diagnostics ---
        internal static int _postfixCalls = 0;
        internal static int _postfixSkipped = 0;
        internal static int _prefixCalls = 0;
        internal static int _prefixSkipped = 0;
        internal static int _logMsgCalls = 0;
        internal static int _cacheHits = 0;
        internal static int _cleanCalls = 0;
        private const bool PerfLogEnabled = false;
        private float _nextLogTime = 0f;
        private int _lastGC0 = 0;
        private int _lastGC1 = 0;
        private int _lastGC2 = 0;
        private bool _forceUpdateDone;


        void Start()
        {
            // Deferred to Start() (after Awake) so all Harmony hooks are registered
            // before we re-trigger text setters on UI elements that were initialized
            // before the plugin loaded.
            if (!_forceUpdateDone)
            {
                _forceUpdateDone = true;
                ForceUpdateAllUITexts();
            }
        }

        private void ForceUpdateAllUITexts()
        {
            try
            {
                int updated = 0;
                var tmpTexts = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>();
                foreach (var tmp in tmpTexts)
                {
                    if (!object.ReferenceEquals(tmp, null) && !string.IsNullOrEmpty(tmp.text))
                    {
                        string current = tmp.text;
                        // Skip already-translated strings (no Latin = no English to translate).
                        // Pure-Cyrillic/numeric strings would be a dictionary no-op anyway.
                        if (!RussianTextCleaner.HasLatin(current)) continue;
                        tmp.text = current;
                        updated++;
                    }
                }
                var uiTexts = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>();
                foreach (var uiText in uiTexts)
                {
                    if (!object.ReferenceEquals(uiText, null) && !string.IsNullOrEmpty(uiText.text))
                    {
                        string current = uiText.text;
                        if (!RussianTextCleaner.HasLatin(current)) continue;
                        uiText.text = current;
                        updated++;
                    }
                }
                Logger.LogInfo("[RusPatch] Force-updated " + updated + " existing UI text components");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[RusPatch] Force-update UI texts failed: " + ex.Message);
            }
        }


        void Update()
        {
            // Show branding in game console (ConsoleToGUI)
            if (!_brandingShown)
            {
                if (TryShowBranding())
                    _brandingShown = true;
            }

            if (PerfLogEnabled && Time.realtimeSinceStartup >= _nextLogTime)
            {
                _nextLogTime = Time.realtimeSinceStartup + 10f;
                int gc0 = System.GC.CollectionCount(0);
                int gc1 = System.GC.CollectionCount(1);
                int gc2 = System.GC.CollectionCount(2);
                Logger.LogInfo("[PERF] 10s: Postfix=" + _postfixCalls + "(skip=" + _postfixSkipped +
                    ") Prefix=" + _prefixCalls + "(skip=" + _prefixSkipped +
                    ") LogMsg=" + _logMsgCalls +
                    " Clean=" + _cleanCalls + " Hit=" + _cacheHits +
                    " Size=" + RussianTextCleaner.CacheSize +
                    " GC0=+" + (gc0 - _lastGC0) + " GC1=+" + (gc1 - _lastGC1) + " GC2=+" + (gc2 - _lastGC2) +
                    " Mem=" + (System.GC.GetTotalMemory(false) / 1048576) + "MB");
                _lastGC0 = gc0; _lastGC1 = gc1; _lastGC2 = gc2;
                _postfixCalls = 0; _postfixSkipped = 0;
                _prefixCalls = 0; _prefixSkipped = 0;
                _logMsgCalls = 0; _cacheHits = 0; _cleanCalls = 0;
            }
        }

        private bool _brandingShown = false;
        private int _brandingAttempts = 0;

        private bool TryShowBranding()
        {
            _brandingAttempts++;
            if (_brandingAttempts > 600) return true; // give up after ~10 sec
            try
            {
                object consoleType = Type.GetType("ConsoleToGUI, Assembly-CSharp");
                if (consoleType == null) return false;
                object instField = ((Type)consoleType).GetField("instance", BindingFlags.Public | BindingFlags.Static);
                if (instField == null) return false;
                object inst = ((FieldInfo)instField).GetValue(null);
                if (inst == null) return false;
                object logMethod = ((Type)consoleType).GetMethod("LogInfo", new Type[] { typeof(string) });
                if (logMethod == null) return false;
                ((MethodInfo)logMethod).Invoke(inst, new object[] {
                    "<color=#00FF00><b>Ostranauts RUS v" + PLUGIN_VERSION + "</b></color>. Made with love by <color=#00BFFF><b>@CoreForgeLabs</b></color> (telegram/Discord) \u2014 Mod Loaded"
                });
                Logger.LogInfo("[RusPatch] Branding shown in game console (attempt " + _brandingAttempts + ")");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[RusPatch] Branding failed: " + ex.Message);
                return true; // don't retry on exception
            }
        }

        void Awake()
        {
            Log = Logger;
            Instance = this;
            Regex.CacheSize = 50;
            Logger.LogMessage("Ostranauts RUS v" + PLUGIN_VERSION + ". Made with love by @CoreForgeLabs (telegram/Discord) — Mod Loaded");
            Logger.LogInfo("[RusPatch] Initializing " + PLUGIN_NAME + " v" + PLUGIN_VERSION + "...");

            try
            {
                // 0. Load external translation files (override hardcoded data)
                LoadExternalTranslations();

                // 1. Replace English pronouns with Russian equivalents
                ReplacePartsOfSpeech();

                // 2. Apply Harmony patches
                Harmony harmony = new Harmony(PLUGIN_GUID);
                ApplyManualPatches(harmony);

                // 3. Patch hardcoded English in Relationship and CondOwner
                PatchHardcodedEnglish(harmony);

                // 4. Patch TMPro separately (avoid hard dependency)
                PatchTMPro(harmony);

                Logger.LogInfo("[RusPatch] Successfully initialized! All patches applied.");
            }
            catch (Exception ex)
            {
                Logger.LogError("[RusPatch] Failed to initialize: " + ex.ToString());
            }
        }

        /// <summary>
        /// Applies all Harmony patches manually to avoid Type.op_Equality crash on .NET 2.0.
        /// Uses AccessTools.Method (string-based lookup) instead of [HarmonyPatch] attributes.
        /// </summary>
        private void ApplyManualPatches(Harmony harmony)
        {
            int patchCount = 0;
            MethodInfo cleanPostfix = typeof(CleanResultPostfix).GetMethod("Postfix",
                BindingFlags.Static | BindingFlags.Public);

            // --- GrammarUtils patches: hook ALL string-returning methods ---
            Type grammarType = typeof(GrammarUtils);
            foreach (MethodInfo method in grammarType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
            {
                if (method.ReturnType.Equals(typeof(string)))
                {
                    try
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(cleanPostfix));
                        patchCount++;
                        Logger.LogInfo("[RusPatch] Patched " + grammarType.Name + "." + method.Name);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("[RusPatch] Failed to patch " + method.Name + ": " + ex.Message);
                    }
                }
            }

            // --- DataHandler patches: hook ALL string-returning methods ---
            Type dataHandlerType = typeof(DataHandler);
            // Methods that cause Harmony IL compilation errors or should not be patched
            var dhSkipMethods = new System.Collections.Generic.HashSet<string> {
                "CreateJsonFromData", // serialization — must not translate
                "AppendDictWords"     // dictionary builder — internal data
            };
            foreach (MethodInfo method in dataHandlerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic))
            {
                if (method.ReturnType.Equals(typeof(string)) && method.DeclaringType.Equals(dataHandlerType))
                {
                    if (dhSkipMethods.Contains(method.Name))
                    {
                        Logger.LogInfo("[RusPatch] Skipping DataHandler." + method.Name + " (excluded)");
                        continue;
                    }
                    try
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(cleanPostfix));
                        patchCount++;
                        Logger.LogInfo("[RusPatch] Patched DataHandler." + method.Name);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("[RusPatch] Failed to patch DataHandler." + method.Name + ": " + ex.Message);
                    }
                }
            }

            // --- CondOwner patches: hook string-returning methods ---
            // IMPORTANT: Skip methods that return internal identifiers/data.
            // Translating these corrupts game logic (GPM lookups, item IDs, etc.)
            var condOwnerSkip = new HashSet<string> {
                "get_strID",           // internal item ID (ItmHatch01Loose etc.)
                "get_strType",         // internal type string
                "get_Skin",            // skin identifier
                "GetGPMInfo",          // GPM settings data (breaks gear icon in sensor panels!)
                "ParseCondEquation",   // condition equation logic
                "ToString",            // object repr, used programmatically
                "PrintIAH",            // debug info
                "GetDebugQueue",       // debug
                "GetDebugPriorities",  // debug
                "GetDebugTickers",     // debug
                "GetDebugConds",       // debug
            };
            try
            {
                Type condOwnerType = typeof(CondOwner);
                foreach (MethodInfo method in condOwnerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic))
                {
                    if (method.ReturnType.Equals(typeof(string)) && method.DeclaringType.Equals(condOwnerType))
                    {
                        if (condOwnerSkip.Contains(method.Name))
                        {
                            Logger.LogInfo("[RusPatch] SKIP CondOwner." + method.Name + " (internal ID)");
                            continue;
                        }
                        try
                        {
                            harmony.Patch(method, postfix: new HarmonyMethod(cleanPostfix));
                            patchCount++;
                            Logger.LogInfo("[RusPatch] Patched CondOwner." + method.Name);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning("[RusPatch] Skip CondOwner." + method.Name + ": " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[RusPatch] CondOwner blanket patch skipped: " + ex.Message);
            }

            // --- UI.Text setter → CleanValuePrefix ---
            try
            {
                MethodInfo uiCleanPrefix = typeof(CleanValuePrefix).GetMethod("Prefix",
                    BindingFlags.Static | BindingFlags.Public);
                PropertyInfo uiTextProp = typeof(UnityEngine.UI.Text).GetProperty("text",
                    BindingFlags.Public | BindingFlags.Instance);
                if (!object.ReferenceEquals(uiTextProp, null))
                {
                    MethodInfo uiSetter = uiTextProp.GetSetMethod();
                    if (!object.ReferenceEquals(uiSetter, null))
                    {
                        harmony.Patch(uiSetter, prefix: new HarmonyMethod(uiCleanPrefix));
                        patchCount++;
                        Logger.LogInfo("[RusPatch] Patched UI.Text.text setter");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[RusPatch] UI.Text patch skipped: " + ex.Message);
            }

            // --- TextMeshProUGUI.text setter (subclass override of TMP_Text — base patch does NOT cover it) ---
            try
            {
                MethodInfo tmpUguiCleanPrefix = typeof(CleanValuePrefix).GetMethod("Prefix",
                    BindingFlags.Static | BindingFlags.Public);
                PropertyInfo tmpUguiTextProp = typeof(TMPro.TextMeshProUGUI).GetProperty("text",
                    BindingFlags.Public | BindingFlags.Instance);
                if (!object.ReferenceEquals(tmpUguiTextProp, null))
                {
                    MethodInfo tmpUguiSetter = tmpUguiTextProp.GetSetMethod();
                    if (!object.ReferenceEquals(tmpUguiSetter, null))
                    {
                        harmony.Patch(tmpUguiSetter, prefix: new HarmonyMethod(tmpUguiCleanPrefix));
                        patchCount++;
                        Logger.LogInfo("[RusPatch] Patched TextMeshProUGUI.text setter (subclass override)");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[RusPatch] TextMeshProUGUI.text patch skipped: " + ex.Message);
            }

            // --- CondOwner.LogMessage: NO LONGER PATCHED ---
            // Text cleaning for log messages is handled by:
            // 1. InflectedStringDiag postfix on GetInflectedString (fixes empty arrow text)
            // 2. CleanResultPostfix on GetMessageLog (cleans assembled HTML)
            // 3. CleanValuePrefix on TMP_Text.text setter (final cleanup)
            // Both prefix and transpiler approaches failed due to HarmonyX issues.
            // --- Diagnostic+Fix: GetInflectedString(string, Condition, CondOwner) empty result fixer ---
            try
            {
                Type gramTypeDiag = typeof(GrammarUtils);
                // Find the 3-parameter overload: GetInflectedString(string, Condition, CondOwner)
                // Use method enumeration to avoid type resolution issues with Condition class
                MethodInfo gisMethod = null;
                foreach (MethodInfo m in gramTypeDiag.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name == "GetInflectedString")
                    {
                        ParameterInfo[] pars = m.GetParameters();
                        if (pars.Length == 3 &&
                            pars[0].ParameterType.Equals(typeof(string)) &&
                            pars[2].ParameterType.Equals(typeof(CondOwner)))
                        {
                            gisMethod = m;
                            break;
                        }
                    }
                }
                if (!object.ReferenceEquals(gisMethod, null))
                {
                    MethodInfo diagPostfix = typeof(InflectedStringDiag).GetMethod("Postfix",
                        BindingFlags.Static | BindingFlags.Public);
                    harmony.Patch(gisMethod, postfix: new HarmonyMethod(diagPostfix));
                    patchCount++;
                    Logger.LogInfo("[RusPatch] Patched GetInflectedString diagnostic");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[RusPatch] GetInflectedString diag skipped: " + ex.Message);
            }

            Logger.LogInfo("[RusPatch] Applied " + patchCount + " patches total.");

            // --- MegaToolTip patches: CondElement, Interaction, and GrammarUtils.GenerateString ---
            int tooltipPatches = 0;

            // Patch CondElement.UpdateString() — generates the text for each condition line in tooltips
            try
            {
                Type condElementType = AccessTools.TypeByName("Ostranauts.UI.MegaToolTip.DataModules.SubElements.CondElement");
                if (!object.ReferenceEquals(condElementType, null))
                {
                    MethodInfo updateString = condElementType.GetMethod("UpdateString",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (!object.ReferenceEquals(updateString, null))
                    {
                        harmony.Patch(updateString, postfix: new HarmonyMethod(cleanPostfix));
                        tooltipPatches++;
                        Logger.LogInfo("[RusPatch] Patched CondElement.UpdateString (tooltip {ls} fix)");
                    }
                    else
                        Logger.LogWarning("[RusPatch] CondElement.UpdateString not found");
                }
                else
                    Logger.LogWarning("[RusPatch] CondElement type not found");
            }
            catch (Exception ex) { Logger.LogWarning("[RusPatch] CondElement patch: " + ex.Message); }

            // Patch Interaction string methods that might produce {ls} text
            try
            {
                Type interactionType = typeof(Interaction);
                foreach (MethodInfo method in interactionType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic))
                {
                    if (method.ReturnType.Equals(typeof(string)) && method.DeclaringType.Equals(interactionType))
                    {
                        try
                        {
                            harmony.Patch(method, postfix: new HarmonyMethod(cleanPostfix));
                            tooltipPatches++;
                            Logger.LogInfo("[RusPatch] Patched Interaction." + method.Name);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning("[RusPatch] Skip Interaction." + method.Name + ": " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogWarning("[RusPatch] Interaction patches: " + ex.Message); }

            // Patch GrammarUtils.GenerateString() postfix — cleans interactionOutput StringBuilder
            try
            {
                MethodInfo generateString = typeof(GrammarUtils).GetMethod("GenerateString",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (!object.ReferenceEquals(generateString, null))
                {
                    MethodInfo gsPostfix = typeof(GenerateStringPostfix).GetMethod("Postfix",
                        BindingFlags.Static | BindingFlags.Public);
                    harmony.Patch(generateString, postfix: new HarmonyMethod(gsPostfix));
                    tooltipPatches++;
                    Logger.LogInfo("[RusPatch] Patched GrammarUtils.GenerateString (SB cleanup)");
                }
            }
            catch (Exception ex) { Logger.LogWarning("[RusPatch] GenerateString patch: " + ex.Message); }

            Logger.LogInfo("[RusPatch] Tooltip+Interaction patches: " + tooltipPatches);

            // --- GUITooltip.TooltipTextFormat1-4: hardcoded tooltip label translation ---
            int guiTooltipPatches = 0;
            try
            {
                Type guiTooltipType = AccessTools.TypeByName("GUITooltip");
                if (!object.ReferenceEquals(guiTooltipType, null))
                {
                    MethodInfo tooltipPostfix = typeof(TooltipFormatPostfix).GetMethod("Postfix",
                        BindingFlags.Static | BindingFlags.Public);

                    string[] ttMethods = new string[] { "TooltipTextFormat1", "TooltipTextFormat2",
                        "TooltipTextFormat3", "TooltipTextFormat4" };
                    foreach (string methodName in ttMethods)
                    {
                        try
                        {
                            MethodInfo m = guiTooltipType.GetMethod(methodName,
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                            if (!object.ReferenceEquals(m, null))
                            {
                                harmony.Patch(m, postfix: new HarmonyMethod(tooltipPostfix));
                                guiTooltipPatches++;
                                Logger.LogInfo("[RusPatch] Patched GUITooltip." + methodName);
                            }
                            else
                            {
                                Logger.LogWarning("[RusPatch] GUITooltip." + methodName + " not found");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning("[RusPatch] GUITooltip." + methodName + " failed: " + ex.Message);
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("[RusPatch] GUITooltip type not found");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[RusPatch] GUITooltip patches failed: " + ex.Message);
            }
            Logger.LogInfo("[RusPatch] GUITooltip format patches: " + guiTooltipPatches);

            // --- TMPro FillCharacterVertexBuffers: suppress IndexOutOfRangeException ---
            // Game bug: face texture loading fails (concatenated PNG names) causing TMPro to
            // crash in FillCharacterVertexBuffers with IndexOutOfRange during text rendering.
            try
            {
                Type tmpTextType = typeof(TMPro.TMP_Text);
                MethodInfo fillBuffers = tmpTextType.GetMethod("FillCharacterVertexBuffers",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public,
                    null, new Type[] { typeof(int), typeof(int) }, null);
                if (!object.ReferenceEquals(fillBuffers, null))
                {
                    MethodInfo finalizerMethod = typeof(TMProCrashFinalizer).GetMethod("Finalizer",
                        BindingFlags.Static | BindingFlags.Public);
                    harmony.Patch(fillBuffers, postfix: null, finalizer: new HarmonyMethod(finalizerMethod));
                    Logger.LogInfo("[RusPatch] Patched TMPro.FillCharacterVertexBuffers (crash suppressor)");
                }
                else
                {
                    Logger.LogWarning("[RusPatch] TMPro.FillCharacterVertexBuffers not found");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[RusPatch] TMPro crash patch failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Loads external JSON translation files from BepInEx/plugins/ directory.
        /// Files override hardcoded translations, enabling modders to edit without recompiling.
        /// If files don't exist, hardcoded values are used (backward compatible).
        /// </summary>
        private void LoadExternalTranslations()
        {
            string pluginDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(pluginDir))
                pluginDir = System.IO.Path.Combine(System.IO.Path.Combine(Paths.GameRootPath, "BepInEx"), "plugins");

            int loaded = 0;

            // 1. Load phrase replacements (ordered array, order matters for preemption!)
            string phrasesPath = System.IO.Path.Combine(pluginDir, "rus_phrases.json");
            string[][] extPhrases = JsonFileLoader.LoadPhraseArray(phrasesPath);
            if (extPhrases.Length > 0)
            {
                RussianTextCleaner.SetPhraseReplacements(extPhrases);
                loaded++;
                Logger.LogInfo("[RusPatch] Loaded " + extPhrases.Length + " phrase replacements from JSON");
            }

            // 2. Load exact translations (dictionary, overrides hardcoded)
            string exactPath = System.IO.Path.Combine(pluginDir, "rus_exact.json");
            Dictionary<string, string> extExact = JsonFileLoader.LoadDictionary(exactPath);
            if (extExact.Count > 0)
            {
                RussianTextCleaner.MergeExactTranslations(extExact);
                loaded++;
                Logger.LogInfo("[RusPatch] Merged " + extExact.Count + " exact translations from JSON");
            }

            // 3. Load ship info labels
            string labelsPath = System.IO.Path.Combine(pluginDir, "rus_ship_labels.json");
            string[] extLabels = JsonFileLoader.LoadStringArray(labelsPath);
            if (extLabels.Length > 0)
            {
                RussianTextCleaner.SetShipInfoLabels(extLabels);
                loaded++;
                Logger.LogInfo("[RusPatch] Loaded " + extLabels.Length + " ship info labels from JSON");
            }

            // 4. Load pronoun map overrides
            string pronounsPath = System.IO.Path.Combine(pluginDir, "rus_pronouns.json");
            Dictionary<string, string> extPronouns = JsonFileLoader.LoadDictionary(pronounsPath);
            if (extPronouns.Count > 0)
            {
                RussianTextCleaner.MergePronounMap(extPronouns);
                loaded++;
                Logger.LogInfo("[RusPatch] Merged " + extPronouns.Count + " pronoun overrides from JSON");
            }

            // 5. Load noun declension table
            string nounsPath = System.IO.Path.Combine(pluginDir, "rus_nouns.json");
            Dictionary<string, Dictionary<string, string>> extNouns =
                JsonFileLoader.LoadNounTable(nounsPath);
            if (extNouns.Count > 0)
            {
                RussianTextCleaner.SetNounTable(extNouns);
                loaded++;
                Logger.LogInfo("[RusPatch] Loaded " + extNouns.Count + " noun declensions from JSON");
            }

            Logger.LogInfo("[RusPatch] External JSON: " + loaded + "/5 files loaded successfully");
        }

        /// <summary>
        /// Replaces English pronoun tables in GrammarUtils with Russian.
        /// 
        /// PronounInflection indices:
        ///   0=First(I)  1=Second(you/player)  2=Male(he)
        ///   3=Female(she)  4=NonBinary(they)  5=NonHuman(it/objects)
        /// </summary>
        private void ReplacePartsOfSpeech()
        {
            // Lowercase
            GrammarUtils.partsOfSpeech[GrammarUtils.GrammarLUTIndex.Subjective] =
                new string[] { "\u044f", "\u0432\u044b", "\u043e\u043d", "\u043e\u043d\u0430", "\u043e\u043d\u0438", "" };
            GrammarUtils.partsOfSpeech[GrammarUtils.GrammarLUTIndex.Possessive] =
                new string[] { "\u043c\u043e\u0439", "\u0432\u0430\u0448", "\u0435\u0433\u043e", "\u0435\u0451", "\u0438\u0445", "" };
            GrammarUtils.partsOfSpeech[GrammarUtils.GrammarLUTIndex.Objective] =
                new string[] { "\u043c\u0435\u043d\u044f", "\u0432\u0430\u0441", "\u0435\u0433\u043e", "\u0435\u0451", "\u0438\u0445", "" };
            GrammarUtils.partsOfSpeech[GrammarUtils.GrammarLUTIndex.Reflexive] =
                new string[] { "\u0441\u0435\u0431\u044f", "\u0441\u0435\u0431\u044f", "\u0441\u0435\u0431\u044f", "\u0441\u0435\u0431\u044f", "\u0441\u0435\u0431\u044f", "\u0441\u0435\u0431\u044f" };
            // Contractions -> just subjective (no contractions in Russian)
            GrammarUtils.partsOfSpeech[GrammarUtils.GrammarLUTIndex.ContractIs] =
                new string[] { "\u044f", "\u0432\u044b", "\u043e\u043d", "\u043e\u043d\u0430", "\u043e\u043d\u0438", "" };
            GrammarUtils.partsOfSpeech[GrammarUtils.GrammarLUTIndex.ContractHas] =
                new string[] { "\u044f", "\u0432\u044b", "\u043e\u043d", "\u043e\u043d\u0430", "\u043e\u043d\u0438", "" };
            GrammarUtils.partsOfSpeech[GrammarUtils.GrammarLUTIndex.ContractWill] =
                new string[] { "\u044f", "\u0432\u044b", "\u043e\u043d", "\u043e\u043d\u0430", "\u043e\u043d\u0438", "" };

            // Sentence-case
            GrammarUtils.partsOfSpeechSentenceCase[GrammarUtils.GrammarLUTIndex.Subjective] =
                new string[] { "\u042f", "\u0412\u044b", "\u041e\u043d", "\u041e\u043d\u0430", "\u041e\u043d\u0438", "" };
            GrammarUtils.partsOfSpeechSentenceCase[GrammarUtils.GrammarLUTIndex.Possessive] =
                new string[] { "\u041c\u043e\u0439", "\u0412\u0430\u0448", "\u0415\u0433\u043e", "\u0415\u0451", "\u0418\u0445", "" };
            GrammarUtils.partsOfSpeechSentenceCase[GrammarUtils.GrammarLUTIndex.Objective] =
                new string[] { "\u041c\u0435\u043d\u044f", "\u0412\u0430\u0441", "\u0415\u0433\u043e", "\u0415\u0451", "\u0418\u0445", "" };
            GrammarUtils.partsOfSpeechSentenceCase[GrammarUtils.GrammarLUTIndex.Reflexive] =
                new string[] { "\u0421\u0435\u0431\u044f", "\u0421\u0435\u0431\u044f", "\u0421\u0435\u0431\u044f", "\u0421\u0435\u0431\u044f", "\u0421\u0435\u0431\u044f", "\u0421\u0435\u0431\u044f" };
            GrammarUtils.partsOfSpeechSentenceCase[GrammarUtils.GrammarLUTIndex.ContractIs] =
                new string[] { "\u042f", "\u0412\u044b", "\u041e\u043d", "\u041e\u043d\u0430", "\u041e\u043d\u0438", "" };
            GrammarUtils.partsOfSpeechSentenceCase[GrammarUtils.GrammarLUTIndex.ContractHas] =
                new string[] { "\u042f", "\u0412\u044b", "\u041e\u043d", "\u041e\u043d\u0430", "\u041e\u043d\u0438", "" };
            GrammarUtils.partsOfSpeechSentenceCase[GrammarUtils.GrammarLUTIndex.ContractWill] =
                new string[] { "\u042f", "\u0412\u044b", "\u041e\u043d", "\u041e\u043d\u0430", "\u041e\u043d\u0438", "" };

            Logger.LogInfo("[RusPatch] Replaced English pronouns with Russian.");
        }

        /// <summary>
        /// Patches hardcoded English strings in Relationship and CondOwner.
        /// These methods build log messages by string concatenation with English phrases
        /// that cannot be changed via JSON mods.
        /// </summary>
        private void PatchHardcodedEnglish(Harmony harmony)
        {
            int patchCount = 0;

            // --- Relationship.AddRelationship ---
            try
            {
                Type relType = typeof(Relationship);
                MethodInfo addRel = relType.GetMethod("AddRelationship",
                    BindingFlags.Public | BindingFlags.Instance);
                if (!object.ReferenceEquals(addRel, null))
                {
                    MethodInfo postfix = typeof(RelationshipLogPatch).GetMethod("AddRelationshipPostfix",
                        BindingFlags.Static | BindingFlags.Public);
                    // We use a Prefix to replace the entire method
                    // Actually we just rely on LogMessage catch-all + RussianTextCleaner
                    // but let's log for tracking
                    Logger.LogInfo("[RusPatch] Relationship.AddRelationship will be cleaned via LogMessage catch-all");
                    patchCount++;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[RusPatch] Relationship patch skipped: " + ex.Message);
            }

            Logger.LogInfo("[RusPatch] Hardcoded English patches: " + patchCount);
        }

        private void PatchTMPro(Harmony harmony)
        {
            bool enableSetTextStringPatch = true;
            bool enableSetTextStringBuilderPatch = false;
            bool enableSetCharArrayPatch = false;

            Type tmpTextType = AccessTools.TypeByName("TMPro.TMP_Text");
            if (object.ReferenceEquals(tmpTextType, null))
            {
                Logger.LogWarning("[RusPatch] TMPro.TMP_Text type not found, TMPro patches skipped");
                return;
            }

            MethodInfo prefix = typeof(CleanValuePrefix).GetMethod("Prefix",
                BindingFlags.Static | BindingFlags.Public);
            int patchCount = 0;

            // 1. Patch TMP_Text.text property setter
            try
            {
                PropertyInfo textProp = tmpTextType.GetProperty("text",
                    BindingFlags.Public | BindingFlags.Instance);
                if (!object.ReferenceEquals(textProp, null))
                {
                    MethodInfo setter = textProp.GetSetMethod();
                    if (!object.ReferenceEquals(setter, null))
                    {
                        harmony.Patch(setter, prefix: new HarmonyMethod(prefix));
                        patchCount++;
                        Logger.LogInfo("[RusPatch] Patched TMP_Text.text setter");
                    }
                }
            }
            catch (Exception ex) { Logger.LogWarning("[RusPatch] TMP_Text.text setter failed: " + ex.Message); }

            // 2. Patch TMP_Text.SetText(string) method
            if (enableSetTextStringPatch)
            {
                try
                {
                    MethodInfo setTextMethod = tmpTextType.GetMethod("SetText",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(string) },
                        null);
                    if (!object.ReferenceEquals(setTextMethod, null))
                    {
                        MethodInfo setTextPrefix = typeof(SetTextPrefix).GetMethod("Prefix",
                            BindingFlags.Static | BindingFlags.Public);
                        harmony.Patch(setTextMethod, prefix: new HarmonyMethod(setTextPrefix));
                        patchCount++;
                        Logger.LogInfo("[RusPatch] Patched TMP_Text.SetText(string)");
                    }
                }
                catch (Exception ex) { Logger.LogWarning("[RusPatch] SetText(string) failed: " + ex.Message); }
            }
            else
            {
                Logger.LogInfo("[RusPatch] Skip TMP_Text.SetText(string) patch (perf mode)");
            }

            // 2a2. Patch TMP_Text.SetText(string, bool) — some UI code uses this overload
            try
            {
                MethodInfo setTextBoolMethod = tmpTextType.GetMethod("SetText",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(string), typeof(bool) },
                    null);
                if (!object.ReferenceEquals(setTextBoolMethod, null))
                {
                    MethodInfo setTextPrefix = typeof(SetTextPrefix).GetMethod("Prefix",
                        BindingFlags.Static | BindingFlags.Public);
                    harmony.Patch(setTextBoolMethod, prefix: new HarmonyMethod(setTextPrefix));
                    patchCount++;
                    Logger.LogInfo("[RusPatch] Patched TMP_Text.SetText(string, bool)");
                }
            }
            catch (Exception ex) { Logger.LogWarning("[RusPatch] SetText(string,bool) failed: " + ex.Message); }

            // 2b. Patch TMP_Text.SetText(StringBuilder) — tooltip text may use this path
            if (enableSetTextStringBuilderPatch)
            {
                try
                {
                    MethodInfo setTextSBMethod = tmpTextType.GetMethod("SetText",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(System.Text.StringBuilder) },
                        null);
                    if (!object.ReferenceEquals(setTextSBMethod, null))
                    {
                        MethodInfo sbPrefix = typeof(SetTextSBPrefix).GetMethod("Prefix",
                            BindingFlags.Static | BindingFlags.Public);
                        harmony.Patch(setTextSBMethod, prefix: new HarmonyMethod(sbPrefix));
                        patchCount++;
                        Logger.LogInfo("[RusPatch] Patched TMP_Text.SetText(StringBuilder)");
                    }
                    else
                    {
                        Logger.LogInfo("[RusPatch] SetText(StringBuilder) not found on TMP_Text");
                    }
                }
                catch (Exception ex) { Logger.LogWarning("[RusPatch] SetText(SB) failed: " + ex.Message); }
            }
            else
            {
                Logger.LogInfo("[RusPatch] Skip TMP_Text.SetText(StringBuilder) patch (perf mode)");
            }

            // 2c. Patch TMP_Text.SetCharArray(char[], int, int)
            if (enableSetCharArrayPatch)
            {
                try
                {
                    MethodInfo setCharArrayMethod = tmpTextType.GetMethod("SetCharArray",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(char[]), typeof(int), typeof(int) },
                        null);
                    if (!object.ReferenceEquals(setCharArrayMethod, null))
                    {
                        MethodInfo caPrefix = typeof(SetCharArrayPrefix).GetMethod("Prefix",
                            BindingFlags.Static | BindingFlags.Public);
                        harmony.Patch(setCharArrayMethod, prefix: new HarmonyMethod(caPrefix));
                        patchCount++;
                        Logger.LogInfo("[RusPatch] Patched TMP_Text.SetCharArray(char[],int,int)");
                    }
                    else
                    {
                        Logger.LogInfo("[RusPatch] SetCharArray not found on TMP_Text");
                    }
                }
                catch (Exception ex) { Logger.LogWarning("[RusPatch] SetCharArray failed: " + ex.Message); }
            }
            else
            {
                Logger.LogInfo("[RusPatch] Skip TMP_Text.SetCharArray patch (perf mode)");
            }

            // 3. Patch TextMeshProUGUI.text setter if it overrides base
            Type tmpuguiType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
            if (!object.ReferenceEquals(tmpuguiType, null))
            {
                try
                {
                    PropertyInfo uguiTextProp = tmpuguiType.GetProperty("text",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (!object.ReferenceEquals(uguiTextProp, null))
                    {
                        MethodInfo uguiSetter = uguiTextProp.GetSetMethod();
                        if (!object.ReferenceEquals(uguiSetter, null))
                        {
                            harmony.Patch(uguiSetter, prefix: new HarmonyMethod(prefix));
                            patchCount++;
                            Logger.LogInfo("[RusPatch] Patched TextMeshProUGUI.text setter");
                        }
                    }
                }
                catch (Exception ex) { Logger.LogWarning("[RusPatch] TextMeshProUGUI.text setter failed: " + ex.Message); }

                // 4. Patch OnEnable() to catch prefab/Inspector text
                try
                {
                    MethodInfo onEnable = tmpuguiType.GetMethod("OnEnable",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (!object.ReferenceEquals(onEnable, null))
                    {
                        MethodInfo onEnablePostfix = typeof(TextOnEnablePostfix).GetMethod("Postfix",
                            BindingFlags.Static | BindingFlags.Public);
                        harmony.Patch(onEnable, postfix: new HarmonyMethod(onEnablePostfix));
                        patchCount++;
                        Logger.LogInfo("[RusPatch] Patched TextMeshProUGUI.OnEnable (prefab text)");
                    }
                }
                catch (Exception ex) { Logger.LogWarning("[RusPatch] OnEnable failed: " + ex.Message); }
            }

            // --- MFD ship name font fix: TEMPORARILY DISABLED for performance testing ---
            // try
            // {
            //     Type mfdDisplayType = AccessTools.TypeByName("Ostranauts.ShipGUIs.MFD.GUIMFDDisplay");
            //     if (!object.ReferenceEquals(mfdDisplayType, null))
            //     {
            //         MethodInfo showMenu = mfdDisplayType.GetMethod("ShowMenu",
            //             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            //         if (!object.ReferenceEquals(showMenu, null))
            //         {
            //             MethodInfo mfdPostfix = typeof(MFDDisplayFontFix).GetMethod("Postfix",
            //                 BindingFlags.Static | BindingFlags.Public);
            //             harmony.Patch(showMenu, postfix: new HarmonyMethod(mfdPostfix));
            //             patchCount++;
            //             Logger.LogInfo("[RusPatch] Patched GUIMFDDisplay.ShowMenu (MFD font fix)");
            //         }
            //     }
            // }
            // catch (Exception ex) { Logger.LogWarning("[RusPatch] MFD font fix failed: " + ex.Message); }

            Logger.LogInfo("[RusPatch] TMPro patches applied: " + patchCount);
        }

    }
}
