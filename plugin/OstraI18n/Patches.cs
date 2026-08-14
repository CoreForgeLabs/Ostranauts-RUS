using System;
using System.Collections.Generic;
using UnityEngine;

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

                RegisterSyntheticVerbs();
            }
            catch (Exception ex) { Plugin.Log.LogError("[i18n] UnpackTokensPostfix: " + ex); }
        }

        // Task 6.5: the game's own token parser (PrepareToken) only routes a token to
        // GrammarUtils.Verb if DataHandler.dictVerbs.ContainsKey(text) is true, where
        // dictVerbs is the GAME's own verb dictionary (built from its "verbs" data,
        // completely separate from LangPack.Verbs / verbs.json). Our synthetic
        // disambiguated verb keys (is.cop/is.aux/has.obj/has.qual) never appear in the
        // game's own English verb data, so without this step [is.aux] etc. would never
        // reach VerbPrefix at all -- exactly parallel to Task 6.1's DataHandler.categories
        // registration, but for the verb dictionary. Content of the placeholder string[]
        // doesn't matter: VerbPrefix (above) always looks the key up in LangPack.Verbs
        // first and intercepts before vanilla could ever consume dictVerbs' own value;
        // this array only needs to exist so PrepareToken's dictVerbs.ContainsKey(text)
        // check succeeds and t.verbForms[0] ends up equal to our key string.
        // Force-overwrite (not TryAdd) since these are OUR synthetic keys with dots in
        // them (is.cop, is.aux, has.obj, has.qual) -- nothing in the game's own English
        // verb data could ever legitimately already own one, so there's nothing to
        // accidentally clobber, and force-overwrite guarantees our registration wins even
        // if some future game update or another mod happens to add the same literal key.
        private static readonly string[] SyntheticVerbKeys = { "is.cop", "is.aux", "has.obj", "has.qual" };
        private static void RegisterSyntheticVerbs()
        {
            int registered = 0;
            foreach (var key in SyntheticVerbKeys)
            {
                DataHandler.dictVerbs[key] = new[] { key, key };
                registered++;
            }
            Plugin.Log.LogInfo("[i18n] DataHandler.dictVerbs registered: " + registered + " synthetic verb keys");
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

        // GUIDuties.SetCrew -> localizes duty column headers on the ship duties panel.
        public static void DutiesSetCrewPostfix(GUIDuties __instance)
        {
            if (!LangPack.Active || (UnityEngine.Object)(object)__instance == (UnityEngine.Object)null) return;
            try
            {
                var pnl = __instance.transform.Find("pnlHeader/pnlItems");
                if ((UnityEngine.Object)(object)pnl != (UnityEngine.Object)null)
                {
                    var texts = pnl.GetComponentsInChildren<TMPro.TMP_Text>();
                    for (int i = 0; i < texts.Length; i++)
                    {
                        if (i + 1 < JsonCompanyRules.aDutiesNew.Length)
                        {
                            var raw = JsonCompanyRules.aDutiesNew[i + 1];
                            var key = "DUTY_" + raw;
                            var localized = I18n.Get(key);
                            if (!string.IsNullOrEmpty(localized) && localized != key)
                            {
                                texts[i].text = localized;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] DutiesSetCrewPostfix failed: " + ex.Message);
            }
        }

        private static readonly Dictionary<string, string> ChargenBodyTextMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "The First Intra-System Dating App!", "GUI_CHARGEN_DATING_APP_SUBTITLE" },
            { "PRONOUN", "GUI_CHARGEN_PRONOUN" },
            { "FLIRTS WITH", "GUI_CHARGEN_FLIRTS_WITH" },
            { "NAME", "GUI_CHARGEN_NAME" },
            { "RANDOMIZE", "GUI_CHARGEN_RANDOMIZE" },
            { "DONE!", "GUI_CHARGEN_DONE" },
            { "HE\nHIM", "GUI_PRONOUN_HE_HIM" },
            { "HE / HIM", "GUI_PRONOUN_HE_HIM" },
            { "SHE\nHER", "GUI_PRONOUN_SHE_HER" },
            { "SHE / HER", "GUI_PRONOUN_SHE_HER" },
            { "THEY\nTHEM", "GUI_PRONOUN_THEY_THEM" },
            { "THEY / THEM", "GUI_PRONOUN_THEY_THEM" },
            { "SKIN", "GUI_BODY_SKIN" },
            { "HAIR", "GUI_BODY_HAIR" },
            { "SCAR", "GUI_BODY_SCAR" },
            { "GLASSES", "GUI_BODY_GLASSES" },
            { "BEARD", "GUI_BODY_BEARD" },
            { "PUPILS", "GUI_BODY_PUPILS" },
            { "EYES", "GUI_BODY_EYES" },
            { "NOSE", "GUI_BODY_NOSE" },
            { "TEETH", "GUI_BODY_TEETH" },
            { "LIPS", "GUI_BODY_LIPS" },
            { "NECK", "GUI_BODY_NECK" },
            { "HEAD", "GUI_BODY_HEAD" },
            { "BODY", "GUI_BODY_BODY" }
        };

        // GUIChargenBody.Awake -> localizes character creation Dating App UI (RIN-A)
        public static void ChargenBodyAwakePostfix(GUIChargenBody __instance)
        {
            if (!LangPack.Active || (UnityEngine.Object)(object)__instance == (UnityEngine.Object)null) return;
            try
            {
                LocalizeHierarchy(__instance.transform, ChargenBodyTextMap);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] ChargenBodyAwakePostfix failed: " + ex.Message);
            }
        }

        private static void LocalizeHierarchy(Transform root, Dictionary<string, string> map)
        {
            if (root == null) return;
            var tmpTexts = root.GetComponentsInChildren<TMPro.TMP_Text>(true);
            if (tmpTexts != null)
            {
                foreach (var t in tmpTexts)
                {
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    var trimmed = t.text.Trim();
                    if (map.TryGetValue(trimmed, out var key))
                    {
                        var localized = I18n.Get(key);
                        if (!string.IsNullOrEmpty(localized) && localized != key)
                        {
                            t.text = localized;
                        }
                    }
                }
            }

            var uiTexts = root.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            if (uiTexts != null)
            {
                foreach (var t in uiTexts)
                {
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    var trimmed = t.text.Trim();
                    if (map.TryGetValue(trimmed, out var key))
                    {
                        var localized = I18n.Get(key);
                        if (!string.IsNullOrEmpty(localized) && localized != key)
                        {
                            t.text = localized;
                        }
                    }
                }
            }
        }
    }
}
