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

        // Task 6.4: grammatical CASE codes (mirrors pack.json's "cases" array) --
        // NOT person-role pronoun categories like "subj"/"pos"/"obj"/"reflexive"/
        // "contractIs" etc. A token category in this set means "decline the actual
        // noun text for this case" (TokenResolver.Resolve against named_forms.json/
        // morph_rules.json); a category NOT in this set means "this is a pronoun
        // role, not a case" and the existing role-lookup logic below (LangPack.
        // Pronouns / GrammarUtils.partsOfSpeechStr, indexed by person) applies
        // unchanged. "nom" is deliberately excluded: no data file emits a literal
        // "[x-nom]" token (nominative is the unmarked/default token form, e.g.
        // plain "[them]"), so there is nothing that would ever need to look it up
        // here -- and the first-mention branch's existing ShortName-append
        // behavior is already correct nominative Russian. Only "gen" has live pack
        // data behind it as of this task (Task 6.4 scope); dat/acc/ins/prep are
        // listed for when future tasks add pronoun-category/named-forms data for
        // them, but with no such data yet TokenResolver.Resolve would just fall
        // through to its own nominative/no-op fallback (layer C) for those codes.
        private static readonly HashSet<string> DeclinableCases =
            new HashSet<string> { "gen", "dat", "acc", "ins", "prep" };

        // Localisation.Get -> report configured language to anything that asks
        public static void LocalisationGetPostfix(ref string __result)
        {
            if (LangPack.Active) __result = LangPack.Lang;
        }

        // After tokens are unpacked: force-override pronoun tables, and register
        // every pronoun category the pack declares in DataHandler.categories so the
        // game's own token parser (PrepareToken) recognizes e.g. [them-gen] as a
        // category substitution at all, instead of falling through to the switch
        // below it. Mirrors the game's own "categories" case-loader pattern
        // (Contains-guarded Add into the List<string>, TryAdd-free overwrite into
        // partsOfSpeechStr) since TryAdd there can't be used to override core entries.
        public static void UnpackTokensPostfix()
        {
            if (!LangPack.Active) return;
            try
            {
                int newlyRegistered = 0;
                foreach (var kv in LangPack.Pronouns)
                {
                    if (!DataHandler.categories.Contains(kv.Key))
                    {
                        DataHandler.categories.Add(kv.Key);
                        newlyRegistered++;
                    }
                    GrammarUtils.partsOfSpeechStr[kv.Key] = kv.Value;
                }
                Plugin.Log.LogInfo("[i18n] pronoun tables overridden: " + LangPack.Pronouns.Count + " categories");
                Plugin.Log.LogInfo("[i18n] DataHandler.categories registered: " + newlyRegistered + " new (of " + LangPack.Pronouns.Count + " total)");
            }
            catch (Exception ex) { Plugin.Log.LogError("[i18n] UnpackTokensPostfix: " + ex); }
        }

        // GrammarUtils.Verb -> Russian conjugation.
        // InflectionIndex: 0=I, 1=you, 2=he, 3=she, 4=they, 5=it
        public static bool VerbPrefix(TokenData tokenData)
        {
            if (!LangPack.Active) return true;
            try
            {
                if (!GrammarUtils.entityMap.TryGetValue(tokenData.alias, out var ent)) return false;
                var key = (tokenData.verbForms != null && tokenData.verbForms.Length > 0) ? tokenData.verbForms[0] : null;
                if (key == null) return false;

                if (!LangPack.Verbs.TryGetValue(key, out var vf))
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
            if (!LangPack.Active) return true;
            try
            {
                if (tokenData.alias.IsNullOrEmpty() || tokenData.category.IsNullOrEmpty()) return false;
                var ent = GrammarUtils.entityMap[tokenData.alias];

                if (!ent.named && ent.CondOwner != null && ent.InflectionIndex != GrammarUtils.PronounInflection.Second)
                {
                    // Task 6.4: first mention of a named object used to always append
                    // ShortName unconditionally, i.e. nominative regardless of which
                    // case the token actually requested -- that's the bug this task
                    // fixes. If the requested category is a real grammatical case
                    // (see DeclinableCases above), decline the noun via the resolver
                    // instead of appending it unchanged.
                    string text = ent.CondOwner.ShortName;
                    if (DeclinableCases.Contains(tokenData.category) && LangPack.Resolver != null)
                        text = LangPack.Resolver.Resolve(ent.CondOwner.strName, ent.CondOwner.ShortName, tokenData.category);

                    GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(text));
                    if (tokenData.category == "subj") ent.lastSubjectiveWasPronoun = false;
                    GrammarUtils.caret = GrammarUtils.interactionOutput.Length - 1;
                    ent.named = true;
                    return false;
                }

                if (LangPack.Pronouns.TryGetValue(tokenData.category, out var forms))
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
            if (!LangPack.Active) return true;
            try
            {
                if (tokenData.alias.IsNullOrEmpty() || !GrammarUtils.entityMap.TryGetValue(tokenData.alias, out var ent)) return false;

                if (ent.InflectionIndex == GrammarUtils.PronounInflection.Second)
                {
                    GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(LangPack.YouWord));
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
            if (!LangPack.Active) return;
            try
            {
                string t;
                if (LangPack.Strings.TryGetValue(strName, out t)) __result = t;
            }
            catch { }
        }
    }
}
