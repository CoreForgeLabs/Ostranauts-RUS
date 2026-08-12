using System;
using System.Collections.Generic;

namespace OstraI18n
{
    // Harmony patches into the grammar engine. Every prefix returns bool:
    // true = run vanilla method, false = vanilla skipped (we produced output).
    // Any exception -> log + return true, so a bug in our code can never break the game loop.
    internal static class Patches
    {
        private static readonly HashSet<string> _warned = new HashSet<string>();
        private static void WarnOnce(string key, string msg)
        {
            if (_warned.Add(key)) Plugin.Log.LogWarning(msg);
        }

        // Localisation.Get -> report configured language to anything that asks
        public static void LocalisationGetPostfix(ref string __result)
        {
            if (RuData.Active) __result = RuData.Lang;
        }

        // After tokens are unpacked: force-override pronoun tables.
        // (Game uses TryAdd, so core "subj"/"pos"/etc. can't be overridden by mod JSON alone.)
        public static void UnpackTokensPostfix()
        {
            if (!RuData.Active) return;
            try
            {
                foreach (var kv in RuData.Pronouns)
                    GrammarUtils.partsOfSpeechStr[kv.Key] = kv.Value;
                Plugin.Log.LogInfo("[i18n] pronoun tables overridden: " + RuData.Pronouns.Count + " categories");
            }
            catch (Exception ex) { Plugin.Log.LogError("[i18n] UnpackTokensPostfix: " + ex); }
        }

        // GrammarUtils.Verb -> Russian conjugation.
        // InflectionIndex: 0=I, 1=you, 2=he, 3=she, 4=they, 5=it
        public static bool VerbPrefix(TokenData tokenData)
        {
            if (!RuData.Active) return true;
            try
            {
                if (!GrammarUtils.entityMap.TryGetValue(tokenData.alias, out var ent)) return false;
                var key = (tokenData.verbForms != null && tokenData.verbForms.Length > 0) ? tokenData.verbForms[0] : null;
                if (key == null) return false;

                if (!RuData.Verbs.TryGetValue(key, out var vf))
                {
                    WarnOnce("verb:" + key, "[i18n] no RU paradigm for verb: " + key);
                    var fallback = tokenData.verbForms[tokenData.verbForms.Length > 1 ? 1 : 0];
                    GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(fallback));
                    return false;
                }

                var idx = (int)ent.InflectionIndex;
                var form = "";
                if (vf.Kind == "copula" && vf.OmitPresent)
                {
                    form = ""; // Russian drops present-tense copula
                }
                else if (vf.Present != null)
                {
                    form = vf.Present[Math.Min(idx, vf.Present.Length - 1)];
                }

                if (GrammarUtils.insertNoLonger)
                {
                    GrammarUtils.interactionOutput.Append(vf.NoLongerBefore);
                    GrammarUtils.caret = GrammarUtils.interactionOutput.Length - 1;
                }
                if (form.Length > 0)
                {
                    GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(form));
                }
                else
                {
                    // Dropped copula leaves the surrounding template spaces adjacent to each
                    // other (source text is "[us] [is] голоден" -> "Ты" + " " + "" + " голоден").
                    // Absorb the space that was written just before this token so only the
                    // one that follows survives, producing "Ты голоден" instead of "Ты  голоден".
                    var sb = GrammarUtils.interactionOutput;
                    if (sb.Length > 0 && sb[sb.Length - 1] == ' ')
                        sb.Length -= 1;
                }
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] VerbPrefix: " + ex);
                return true;
            }
        }

        // GrammarUtils.AttemptSubstitution -> Russian pronouns; English 's possessive removed
        public static bool AttemptSubstitutionPrefix(TokenData tokenData)
        {
            if (!RuData.Active) return true;
            try
            {
                if (tokenData.alias.IsNullOrEmpty() || tokenData.category.IsNullOrEmpty()) return false;
                var ent = GrammarUtils.entityMap[tokenData.alias];

                if (!ent.named && ent.CondOwner != null && ent.InflectionIndex != GrammarUtils.PronounInflection.Second)
                {
                    GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(ent.CondOwner.ShortName));
                    if (tokenData.category == "subj") ent.lastSubjectiveWasPronoun = false;
                    GrammarUtils.caret = GrammarUtils.interactionOutput.Length - 1;
                    ent.named = true;
                    return false;
                }

                if (RuData.Pronouns.TryGetValue(tokenData.category, out var forms))
                {
                    GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(forms[(int)ent.InflectionIndex]));
                }
                else if (GrammarUtils.partsOfSpeechStr.TryGetValue(tokenData.category, out var vanilla))
                {
                    GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(vanilla[(int)ent.InflectionIndex]));
                }
                if (tokenData.category == "subj") ent.lastSubjectiveWasPronoun = true;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] AttemptSubstitutionPrefix: " + ex);
                return true;
            }
        }

        // GrammarUtils.AttemptProperName -> no English article, Russian "you"
        public static bool AttemptProperNamePrefix(TokenData tokenData)
        {
            if (!RuData.Active) return true;
            try
            {
                if (tokenData.alias.IsNullOrEmpty() || !GrammarUtils.entityMap.TryGetValue(tokenData.alias, out var ent)) return false;

                if (ent.InflectionIndex == GrammarUtils.PronounInflection.Second)
                {
                    GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(RuData.YouWord));
                    if (tokenData.category == "subj") ent.lastSubjectiveWasPronoun = true;
                    return false;
                }
                if (ent.CondOwner == null) return false;
                GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(ent.CondOwner.ShortName));
                ent.lastSubjectiveWasPronoun = false;
                ent.named = true;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] AttemptProperNamePrefix: " + ex);
                return true;
            }
        }

        // DataHandler.GetString -> override GUI strings from the lang pack strings.json (by KEY).
        // Makes the language folder self-contained for GUI strings (no separate mod needed for them).
        public static void GetStringPostfix(string strName, ref string __result)
        {
            if (!RuData.Active) return;
            try
            {
                string t;
                if (RuData.Strings.TryGetValue(strName, out t)) __result = t;
            }
            catch { }
        }
    }
}
