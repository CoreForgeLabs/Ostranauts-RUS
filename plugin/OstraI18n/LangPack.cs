using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LitJson;
using OstraI18n.Core;

namespace OstraI18n
{
    // Language pack loader, manifest-driven. Layout under the plugin data dir:
    //   langs/languages.json        <- manifest: { "ru": "lang_ru", "de": "lang_de" }
    //
    // Two on-disk layouts are supported for each language folder, resolved by
    // GrammarPackLoader (core/OstraI18n.Core/GrammarPack.cs):
    //   new (preferred): langs/<code>/pack.json + verbs.json + strings.json
    //   old (fallback):  langs/lang_<code>/grammar.json + verbs.json + strings.json
    // If pack.json is absent, the old layout is read and a "legacy layout" warning
    // is logged. Both layouts must produce identical in-memory tables.
    internal static class LangPack
    {
        internal static bool Active;
        internal static string Lang = "English";
        // Task 5.6 (C2 fix round): emergency-only fallback for the catastrophic
        // case where the pack provides no "you" field at all. Deliberately a
        // language-neutral English placeholder, not a hardcoded Russian word
        // ("ты"/"вы") -- if this ever actually shows up in-game it is instantly
        // recognizable as "pack data failed to load", instead of silently
        // masquerading as correct Russian text. See Load() below: this is only
        // used when the pack genuinely omits "you", and that case now also
        // logs a warning (matching how GrammarPackLoader/ContentOverlay already
        // log-and-fall-back rather than throw on other kinds of missing data).
        internal static string YouWord = "you";
        // Resolved short ISO code the active pack was actually loaded from
        // (e.g. "ru"), computed once in Load() below from the same manifest/
        // convention logic used to find the pack directory. Exposed so callers
        // that need a language code (I18n.Init, ContentOverlay.Init) reuse this
        // instead of hardcoding a language literal of their own (C2, Task 5.6).
        internal static string Code = "en";

        // pronoun category -> 6 forms indexed by PronounInflection: [I, you, he, she, they, it]
        internal static readonly Dictionary<string, string[]> Pronouns = new Dictionary<string, string[]>();
        // GUI strings by KEY (e.g. GUI_OPTIONS_SAVE) -> translation, used by the GetString postfix
        internal static readonly Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal);

        // key = English template form as written in verbs.json / templates (e.g. "adds", "is")
        internal static readonly Dictionary<string, VerbForms> Verbs = new Dictionary<string, VerbForms>();

        // Task 5.4: декларативные карты контент-оверлея (ContentOverlay.CategoryToField /
        // TranslatableFields), считанные из pack.json -> "overlay". OverlayValid=false
        // means pack.json's overlay section was absent/empty/malformed - ContentOverlay
        // must fall back to its own built-in default in that case.
        internal static readonly Dictionary<string, string> OverlayCategoryToField = new Dictionary<string, string>();
        internal static readonly List<string> OverlayTranslatableFields = new List<string>();
        internal static bool OverlayValid;

        // Task 6.4: table/rule/fallback declension for NAMED objects (e.g. condowners
        // like "стеллаж"), built from langs/<code>/named_forms.json (Task 6.2) +
        // morph_rules.json (Task 6.3's MorphRules) via Core's TokenResolver (Task
        // 6.3). Distinct from Pronouns above: Pronouns handles pronoun-role
        // substitution (subj/obj/gen/...), Resolver handles declining an actual
        // noun's text for a requested grammatical case. Null until Load() runs;
        // never null afterwards -- TokenResolver.Load degrades to empty
        // table/rules (not an exception) if the JSON files are missing/malformed,
        // so callers can always call LangPack.Resolver.Resolve(...) once Active.
        internal static TokenResolver Resolver;

        internal static void Load(string dir, string lang, bool formalYou)
        {
            Lang = lang;
            // ALWAYS clear all dictionaries first to prevent stale data from previous language
            Pronouns.Clear();
            Strings.Clear();
            Verbs.Clear();
            OverlayCategoryToField.Clear();
            OverlayTranslatableFields.Clear();
            OverlayValid = false;
            YouWord = "you";
            Resolver = null;

            Active = !string.Equals(lang, "English", StringComparison.OrdinalIgnoreCase);

            var langsDir = Path.Combine(dir, "langs");
            var manifestPath = Path.Combine(langsDir, "languages.json");
            string code = lang.ToLowerInvariant();
            if (code.StartsWith("lang_", StringComparison.OrdinalIgnoreCase))
                code = code.Substring("lang_".Length);

            try
            {
                if (File.Exists(manifestPath))
                {
                    var md = (IDictionary)JsonMapper.ToObject(File.ReadAllText(manifestPath));
                    foreach (var cand in new[] { lang, lang.ToLowerInvariant(), code })
                    {
                        if (md.Contains(cand))
                        {
                            var mapped = (string)((JsonData)md[cand]);
                            if (!string.IsNullOrEmpty(mapped))
                            {
                                code = mapped.StartsWith("lang_", StringComparison.OrdinalIgnoreCase) ? mapped.Substring(5) : mapped;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[i18n] languages.json: " + ex.Message); }

            Code = code;

            var packDir = Path.Combine(langsDir, code);
            if (!Directory.Exists(packDir))
            {
                // Fallback 1: try the raw lang name as folder
                if (Directory.Exists(Path.Combine(langsDir, lang)))
                    packDir = Path.Combine(langsDir, lang);
                // Fallback 2: try legacy "lang_" prefix
                else if (Directory.Exists(Path.Combine(langsDir, "lang_" + code)))
                    packDir = Path.Combine(langsDir, "lang_" + code);
                // Fallback 3: scan meta.json files in all subdirectories
                else if (Directory.Exists(langsDir))
                {
                    foreach (var sub in Directory.GetDirectories(langsDir))
                    {
                        var metaPath = Path.Combine(sub, "meta.json");
                        if (!File.Exists(metaPath)) continue;
                        try
                        {
                            var metaText = File.ReadAllText(metaPath);
                            // Quick substring check to avoid full JSON parse for every folder
                            if (metaText.Contains(lang) || metaText.Contains(code))
                            {
                                using var metaDoc = System.Text.Json.JsonDocument.Parse(metaText);
                                var root = metaDoc.RootElement;
                                string metaCode = null, metaName = null, metaNameEn = null;
                                if (root.TryGetProperty("code", out var cEl)) metaCode = cEl.GetString();
                                if (root.TryGetProperty("name", out var nEl)) metaName = nEl.GetString();
                                if (root.TryGetProperty("nameEnglish", out var neEl)) metaNameEn = neEl.GetString();

                                if (string.Equals(metaCode, lang, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(metaCode, code, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(metaName, lang, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(metaNameEn, lang, StringComparison.OrdinalIgnoreCase))
                                {
                                    code = metaCode ?? Path.GetFileName(sub);
                                    Code = code;
                                    packDir = sub;
                                    Active = !string.Equals(metaNameEn ?? metaName ?? lang, "English", StringComparison.OrdinalIgnoreCase);
                                    Plugin.Log.LogInfo("[i18n] resolved '" + lang + "' via meta.json in " + sub + " -> code=" + code);
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            Plugin.Log.LogInfo("[i18n] language '" + lang + "' -> packDir=" + packDir + " (code=" + code + ", active=" + Active + ")");

            if (!Directory.Exists(packDir))
            {
                Plugin.Log.LogWarning("[i18n] Language pack directory not found: " + packDir);
                return;
            }

            // FormalYou (vy-form) is not yet a field the pack format supports
            if (formalYou && Active)
                Plugin.Log.LogWarning("[i18n] FormalYou=true, but the active pack format has no formal-address "
                    + "field yet - config option currently has no effect (tracked for a future grammar-data task)");

            var result = GrammarPackLoader.Load(packDir);
            if (result.UsedLegacyLayout)
                Plugin.Log.LogWarning("[i18n] " + packDir + ": no pack.json found - legacy layout fallback (grammar.json)");

            if (result.YouWord != null) YouWord = result.YouWord;
            else if (Active) Plugin.Log.LogWarning("[i18n] " + packDir + ": pack has no \"you\" field - keeping placeholder '"
                + YouWord + "' (grammar output for 2nd person will look wrong until pack.json is fixed)");
            foreach (var kv in result.Pronouns) Pronouns[kv.Key] = kv.Value;
            int missingNoLonger = 0;
            foreach (var kv in result.Verbs)
            {
                if (string.Equals(code, "ru", StringComparison.OrdinalIgnoreCase))
                {
                    if (kv.Value.NoLongerBefore == VerbForms.DefaultNoLongerBefore)
                    {
                        kv.Value.NoLongerBefore = "больше не ";
                    }
                }
                Verbs[kv.Key] = kv.Value;
                if (kv.Value.NoLongerBefore == VerbForms.DefaultNoLongerBefore) missingNoLonger++;
            }
            if (missingNoLonger > 0 && Active && !string.Equals(code, "ru", StringComparison.OrdinalIgnoreCase))
                Plugin.Log.LogWarning("[i18n] " + packDir + ": " + missingNoLonger + " of " + result.Verbs.Count
                    + " verbs have no \"noLonger\" override in verbs.json - using placeholder '"
                    + VerbForms.DefaultNoLongerBefore + "' (negated grammar output will look wrong until fixed)");
            foreach (var kv in result.Strings) Strings[kv.Key] = kv.Value;

            OverlayValid = result.OverlayValid;
            foreach (var kv in result.OverlayCategoryToField) OverlayCategoryToField[kv.Key] = kv.Value;
            OverlayTranslatableFields.AddRange(result.OverlayTranslatableFields);

            Plugin.Log.LogInfo("[i18n] pack " + lang + " [" + packDir + "]: " + Pronouns.Count + " pronoun cats, "
                + Verbs.Count + " verbs, " + Strings.Count + " strings");

            // Load named_forms.json/morph_rules.json and build the live TokenResolver
            Resolver = TokenResolver.Load(langsDir, code);
            Plugin.Log.LogInfo("[i18n] token resolver [" + code + "]: loaded (named_forms.json + morph_rules.json under "
                + Path.Combine(langsDir, code) + ")");
        }
    }
}
