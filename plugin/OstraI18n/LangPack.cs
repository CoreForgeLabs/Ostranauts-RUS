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
        internal static string YouWord = "ты";

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

        internal static void Load(string dir, string lang, bool formalYou)
        {
            Lang = lang;
            Active = !string.Equals(lang, "English", StringComparison.OrdinalIgnoreCase);
            if (!Active) return;
            if (formalYou) YouWord = "вы";

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

            var newDir = Path.Combine(langsDir, code);
            var oldDir = Path.Combine(langsDir, folder);
            bool preferNew = File.Exists(Path.Combine(newDir, "pack.json"));
            var packDir = preferNew ? newDir : oldDir;

            Plugin.Log.LogInfo("[i18n] language '" + lang + "' -> " + folder + (preferNew ? " (new layout: " + code + ")" : ""));

            var result = GrammarPackLoader.Load(packDir);
            if (result.UsedLegacyLayout)
                Plugin.Log.LogWarning("[i18n] " + packDir + ": no pack.json found - legacy layout fallback (grammar.json)");

            if (result.YouWord != null) YouWord = result.YouWord;
            foreach (var kv in result.Pronouns) Pronouns[kv.Key] = kv.Value;
            foreach (var kv in result.Verbs) Verbs[kv.Key] = kv.Value;
            foreach (var kv in result.Strings) Strings[kv.Key] = kv.Value;

            OverlayValid = result.OverlayValid;
            foreach (var kv in result.OverlayCategoryToField) OverlayCategoryToField[kv.Key] = kv.Value;
            OverlayTranslatableFields.AddRange(result.OverlayTranslatableFields);

            Plugin.Log.LogInfo("[i18n] pack " + lang + " [" + packDir + "]: " + Pronouns.Count + " pronoun cats, "
                + Verbs.Count + " verbs, " + Strings.Count + " strings");
        }
    }
}
