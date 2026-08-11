using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LitJson;

namespace OstraI18n
{
    // Language pack loader, manifest-driven. Layout under the plugin data dir:
    //   langs/languages.json        <- manifest: { "ru": "lang_ru", "de": "lang_de" }
    //   langs/lang_ru/grammar.json  <- pronoun tables
    //   langs/lang_ru/verbs.json    <- verb paradigms
    //   langs/lang_ru/gui.json      <- hardcoded/prefab GUI text (english -> translation)
    //   langs/lang_ru/strings.json  <- GUI strings by KEY (strings.json keys -> translation)
    // Add a language = one line in languages.json + drop a folder. No code changes.
    internal static class RuData
    {
        internal static bool Active;
        internal static string Lang = "English";
        internal static string YouWord = "ты";

        // pronoun category -> 6 forms indexed by PronounInflection: [I, you, he, she, they, it]
        internal static readonly Dictionary<string, string[]> Pronouns = new Dictionary<string, string[]>();
        // GUI strings by KEY (e.g. GUI_OPTIONS_SAVE) -> translation, used by the GetString postfix
        internal static readonly Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal);

        internal class VerbForms
        {
            public string Kind = "verb";          // "verb" | "copula"
            public bool OmitPresent;              // copula: dropped in present tense (Russian)
            public string[] Present;              // [1s, 2s, 3m, 3f, 3pl, 3n]
            public string[] Past;                 // [m, f, n, pl]
            public string NoLongerBefore = "больше не ";
        }
        // key = English template form as written in verbs.json / templates (e.g. "adds", "is")
        internal static readonly Dictionary<string, VerbForms> Verbs = new Dictionary<string, VerbForms>();

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

            var pack = Path.Combine(langsDir, folder);
            Plugin.Log.LogInfo("[i18n] language '" + lang + "' -> " + folder);

            var gramPath = Path.Combine(pack, "grammar.json");
            var verbPath = Path.Combine(pack, "verbs.json");
            if (!File.Exists(gramPath)) throw new FileNotFoundException(gramPath);
            if (!File.Exists(verbPath)) throw new FileNotFoundException(verbPath);

            var g = JsonMapper.ToObject(File.ReadAllText(gramPath));
            if (((IDictionary)g).Contains("you")) YouWord = (string)g["you"];
            foreach (DictionaryEntry kv in (IDictionary)g["pronouns"])
            {
                var arr = (JsonData)kv.Value;
                var forms = new string[arr.Count];
                for (int i = 0; i < arr.Count; i++) forms[i] = (string)arr[i];
                Pronouns[(string)kv.Key] = forms;
            }

            foreach (DictionaryEntry kv in (IDictionary)JsonMapper.ToObject(File.ReadAllText(verbPath)))
            {
                var vname = (string)kv.Key;
                if (vname.StartsWith("_")) continue;
                var jv = (JsonData)kv.Value;
                if (!jv.IsObject) continue;
                var vf = new VerbForms();
                var jvd = (IDictionary)jv;
                if (jvd.Contains("kind")) vf.Kind = (string)jv["kind"];
                if (jvd.Contains("omitPresent")) vf.OmitPresent = (bool)jv["omitPresent"];
                if (jvd.Contains("noLonger")) vf.NoLongerBefore = (string)jv["noLonger"];
                if (jvd.Contains("present")) vf.Present = ToStrArray(jv["present"]);
                if (jvd.Contains("past")) vf.Past = ToStrArray(jv["past"]);
                Verbs[vname] = vf;
            }

            var strPath = Path.Combine(pack, "strings.json");
            if (File.Exists(strPath))
            {
                var sj = JsonMapper.ToObject(File.ReadAllText(strPath));
                foreach (string k in sj.Keys) Strings[k] = (string)sj[k];
            }

            Plugin.Log.LogInfo("[i18n] pack " + lang + " [" + folder + "]: " + Pronouns.Count + " pronoun cats, "
                + Verbs.Count + " verbs, " + Strings.Count + " strings");
        }

        private static string[] ToStrArray(JsonData arr)
        {
            var r = new string[arr.Count];
            for (int i = 0; i < arr.Count; i++) r[i] = (string)arr[i];
            return r;
        }
    }
}
