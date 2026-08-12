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
            Active = !string.Equals(lang, "English", StringComparison.OrdinalIgnoreCase);
            if (!Active) return;
            // FormalYou (vy-form) is not yet a field the pack format supports
            // (langs/ru/pack.json has a single "you", no formal variant) --
            // previously this hardcoded a Russian "вы" override here, which
            // both violated C2 AND was silently discarded a few lines down
            // whenever the pack itself provided "you" (which it always does
            // for ru today), i.e. it never actually took effect. Rather than
            // re-add a hardcoded Russian word, surface the gap loudly so it's
            // visible instead of silently doing nothing.
            if (formalYou)
                Plugin.Log.LogWarning("[i18n] FormalYou=true, but the active pack format has no formal-address "
                    + "field yet - config option currently has no effect (tracked for a future grammar-data task)");

            var langsDir = Path.Combine(dir, "langs");
            var manifestPath = Path.Combine(langsDir, "languages.json");
            string folder = "lang_" + lang.ToLowerInvariant();   // default convention
            try
            {
                if (File.Exists(manifestPath))
                {
                    var md = (IDictionary)JsonMapper.ToObject(File.ReadAllText(manifestPath));
                    foreach (var cand in new[] { lang, lang.ToLowerInvariant() })
                        if (md.Contains(cand)) { folder = (string)((JsonData)md[cand]); break; }
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[i18n] languages.json: " + ex.Message); }

            // Short ISO code used by the new pack.json layout (langs/<code>/), derived
            // from the resolved legacy folder name ("lang_ru" -> "ru") since that's the
            // convention Task 5.2 migrated under; falls back to the language name itself
            // if the folder doesn't follow the "lang_" prefix convention.
            string code = folder.StartsWith("lang_", StringComparison.OrdinalIgnoreCase)
                ? folder.Substring("lang_".Length)
                : lang.ToLowerInvariant();
            Code = code;

            var newDir = Path.Combine(langsDir, code);
            var oldDir = Path.Combine(langsDir, folder);
            bool preferNew = File.Exists(Path.Combine(newDir, "pack.json"));
            var packDir = preferNew ? newDir : oldDir;

            Plugin.Log.LogInfo("[i18n] language '" + lang + "' -> " + folder + (preferNew ? " (new layout: " + code + ")" : ""));

            var result = GrammarPackLoader.Load(packDir);
            if (result.UsedLegacyLayout)
                Plugin.Log.LogWarning("[i18n] " + packDir + ": no pack.json found - legacy layout fallback (grammar.json)");

            if (result.YouWord != null) YouWord = result.YouWord;
            else Plugin.Log.LogWarning("[i18n] " + packDir + ": pack has no \"you\" field - keeping placeholder '"
                + YouWord + "' (grammar output for 2nd person will look wrong until pack.json is fixed)");
            foreach (var kv in result.Pronouns) Pronouns[kv.Key] = kv.Value;
            int missingNoLonger = 0;
            foreach (var kv in result.Verbs)
            {
                Verbs[kv.Key] = kv.Value;
                // Task 5.6 (C2 fix round 3): count verbs that fell back to the
                // language-neutral VerbForms.DefaultNoLongerBefore because
                // verbs.json didn't declare an explicit "noLonger" override
                // for them -- same "log loud, don't silently ship a
                // hardcoded-looking-like-pack-data string" pattern as YouWord
                // above. One aggregate warning, not one per verb (417 verbs
                // in the ru pack -- per-verb would be log spam).
                if (kv.Value.NoLongerBefore == VerbForms.DefaultNoLongerBefore) missingNoLonger++;
            }
            if (missingNoLonger > 0)
                Plugin.Log.LogWarning("[i18n] " + packDir + ": " + missingNoLonger + " of " + result.Verbs.Count
                    + " verbs have no \"noLonger\" override in verbs.json - using placeholder '"
                    + VerbForms.DefaultNoLongerBefore + "' (negated grammar output will look wrong until fixed)");
            foreach (var kv in result.Strings) Strings[kv.Key] = kv.Value;

            OverlayValid = result.OverlayValid;
            foreach (var kv in result.OverlayCategoryToField) OverlayCategoryToField[kv.Key] = kv.Value;
            OverlayTranslatableFields.AddRange(result.OverlayTranslatableFields);

            Plugin.Log.LogInfo("[i18n] pack " + lang + " [" + packDir + "]: " + Pronouns.Count + " pronoun cats, "
                + Verbs.Count + " verbs, " + Strings.Count + " strings");

            // Task 6.4: load named_forms.json/morph_rules.json (Task 6.2/6.3 outputs)
            // and build the live TokenResolver. TokenResolver.Load takes the langs/
            // dir + short code convention (same "code" resolved above), always
            // matching the SAME packDir this LangPack load just used -- so if this
            // ever loaded the legacy layout (preferNew=false), the resolver would
            // look for named_forms.json/morph_rules.json in langsDir/<code>/ which
            // may not exist for that language; TokenResolver.Load degrades to an
            // empty table/ruleset rather than throwing in that case, and Resolve()
            // just falls through to nominative (MissCount increments).
            Resolver = TokenResolver.Load(langsDir, code);
            Plugin.Log.LogInfo("[i18n] token resolver [" + code + "]: loaded (named_forms.json + morph_rules.json under "
                + Path.Combine(langsDir, code) + ")");
        }
    }
}
