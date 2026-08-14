using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
        public static void RegisterSyntheticVerbs()
        {
            if (DataHandler.dictVerbs == null) return;
            int registered = 0;
            if (LangPack.Verbs != null)
            {
                foreach (var key in LangPack.Verbs.Keys)
                {
                    if (!DataHandler.dictVerbs.ContainsKey(key))
                    {
                        DataHandler.dictVerbs[key] = new[] { key, key };
                        registered++;
                    }
                }
            }
            foreach (var key in SyntheticVerbKeys)
            {
                if (!DataHandler.dictVerbs.ContainsKey(key))
                {
                    DataHandler.dictVerbs[key] = new[] { key, key };
                    registered++;
                }
            }
            Plugin.Log.LogInfo("[i18n] DataHandler.dictVerbs registered: " + registered + " new verb keys (total in dictVerbs: " + DataHandler.dictVerbs.Count + ")");
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

                var idx = (ent.CondOwner == null && tokenData.alias == "us")
                    ? (int)GrammarUtils.PronounInflection.Second
                    : (int)ent.InflectionIndex;

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
                else if (vf.Kind == "copula")
                {
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
                if (!GrammarUtils.entityMap.TryGetValue(tokenData.alias, out var ent)) return false;

                if (!ent.named && ent.CondOwner != null && ent.InflectionIndex != GrammarUtils.PronounInflection.Second)
                {
                    string text = ent.CondOwner.ShortName;
                    if (DeclinableCases.Contains(tokenData.category) && LangPack.Resolver != null)
                        text = LangPack.Resolver.Resolve(ent.CondOwner.strName, ent.CondOwner.ShortName, tokenData.category);

                    GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(text));
                    if (tokenData.category == "subj") ent.lastSubjectiveWasPronoun = false;
                    GrammarUtils.caret = GrammarUtils.interactionOutput.Length - 1;
                    ent.named = true;
                    return false;
                }

                var idx = (ent.CondOwner == null && tokenData.alias == "us")
                    ? (int)GrammarUtils.PronounInflection.Second
                    : (int)ent.InflectionIndex;

                if (LangPack.Pronouns.TryGetValue(tokenData.category, out var forms))
                {
                    GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(forms[Math.Min(idx, forms.Length - 1)]));
                }
                else if (GrammarUtils.partsOfSpeechStr.TryGetValue(tokenData.category, out var vanilla))
                {
                    GrammarUtils.interactionOutput.Append(GrammarUtils.SetCase(vanilla[Math.Min(idx, vanilla.Length - 1)]));
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

                if (ent.InflectionIndex == GrammarUtils.PronounInflection.Second || (ent.CondOwner == null && tokenData.alias == "us"))
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
            { "HE", "GUI_PRONOUN_HE" },
            { "HIM", "GUI_PRONOUN_HIM" },
            { "SHE", "GUI_PRONOUN_SHE" },
            { "HER", "GUI_PRONOUN_HER" },
            { "THEY", "GUI_PRONOUN_THEY" },
            { "THEM", "GUI_PRONOUN_THEM" },
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
            { "BODY", "GUI_BODY_BODY" },
            { "Career Details", "GUI_CAREER_DETAILS" },
            { "Career Event", "GUI_CAREER_EVENT" },
            { "Special Events", "GUI_CAREER_SPECIAL_EVENTS_TITLE" },
            { "Summary", "GUI_CAREER_SUMMARY" },
            { "Costs", "GUI_CAREER_COSTS" },
            { "Credentials", "GUI_CAREER_CREDENTIALS" },
            { "Continue Career", "GUI_CAREER_SIDEBAR_CAREER_CONT" },
            { "Return to Career", "GUI_CAREER_RETURN" },
            { "Selected Skills", "GUI_CAREER_SELECTED_SKILLS" },
            { "Skills:", "GUI_CAREER_SKILLS_HEADER" },
            { "Traits:", "GUI_CAREER_TRAITS_HEADER" },
            { "Hobbies:", "GUI_CAREER_HOBBIES_HEADER" },
            { "Take Ship", "GUI_CAREER_TAKE_SHIP" },
            { "Undo Last", "GUI_CAREER_UNDO_LAST" },
            { "Apply", "GUI_CAREER_APPLY" },
            { "Clear", "GUI_CAREER_CLEAR" },
            { "CITIZENSHIP VERIFIED", "GUI_HW_CITIZENSHIP_VERIFIED" },
            { "FUNDS VERIFIED", "GUI_HW_FUNDS_VERIFIED" },
            { "CREDIT", "GUI_HW_CREDIT" },
            { "PREPAY", "GUI_HW_PREPAY" },
            { "POINTS LEFT", "GUI_TRAITS_POINTS_LEFT" },
            { "Subtotal", "GUI_TRAITS_SUBTOTAL" }
        };

        // GUIData.Init -> localizes any opened UI screen hierarchy (Chargen, Duties, PDA, etc.)
        public static void GUIDataInitPostfix(GUIData __instance)
        {
            if (!LangPack.Active || (UnityEngine.Object)(object)__instance == (UnityEngine.Object)null) return;
            try
            {
                LocalizeHierarchy(__instance.transform, ChargenBodyTextMap);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] GUIDataInitPostfix failed: " + ex.Message);
            }
        }

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

        public static void LocalizeHierarchy(Transform root, Dictionary<string, string> map)
        {
            if (root == null) return;
            var tmpTexts = root.GetComponentsInChildren<TMPro.TMP_Text>(true);
            if (tmpTexts != null)
            {
                foreach (var t in tmpTexts)
                {
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    var norm = t.text.Trim().Replace("\r\n", "\n").Replace("\r", "\n");
                    string key;
                    if (map.TryGetValue(norm, out key))
                    {
                        var localized = I18n.Get(key);
                        if (!string.IsNullOrEmpty(localized) && localized != key)
                        {
                            t.text = localized;
                        }
                    }
                    else
                    {
                        var noTags = System.Text.RegularExpressions.Regex.Replace(norm, "<[^>]+>", "").Trim();
                        if (map.TryGetValue(noTags, out key))
                        {
                            var localized = I18n.Get(key);
                            if (!string.IsNullOrEmpty(localized) && localized != key)
                            {
                                t.text = localized;
                            }
                        }
                        else
                        {
                            I18n.RecordUntranslated("UI_TEXT", norm, t.transform.name);
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
                    var norm = t.text.Trim().Replace("\r\n", "\n").Replace("\r", "\n");
                    string key;
                    if (map.TryGetValue(norm, out key))
                    {
                        var localized = I18n.Get(key);
                        if (!string.IsNullOrEmpty(localized) && localized != key)
                        {
                            t.text = localized;
                        }
                    }
                    else
                    {
                        var noTags = System.Text.RegularExpressions.Regex.Replace(norm, "<[^>]+>", "").Trim();
                        if (map.TryGetValue(noTags, out key))
                        {
                            var localized = I18n.Get(key);
                            if (!string.IsNullOrEmpty(localized) && localized != key)
                            {
                                t.text = localized;
                            }
                        }
                        else
                        {
                            I18n.RecordUntranslated("UI_TEXT", norm, t.transform.name);
                        }
                    }
                }
            }
        }

        // Objective.MakeTutorialObjective -> localizes tutorial objective name and descriptions
        public static void MakeTutorialObjectivePostfix(Ostranauts.Core.Tutorials.TutorialBeat tutorialBeat, ref Ostranauts.Objectives.Objective __result)
        {
            if (!LangPack.Active || __result == null || tutorialBeat == null) return;
            try
            {
                string beatName = tutorialBeat.GetType().Name;
                string nameKey = "TUT_NAME_" + beatName;
                string descKey = "TUT_DESC_" + beatName;
                string compKey = "TUT_COMP_" + beatName;

                string nameTr = I18n.Get(nameKey);
                if (!string.IsNullOrEmpty(nameTr) && nameTr != nameKey)
                    __result.strDisplayName = nameTr;

                string descTr = I18n.Get(descKey);
                if (!string.IsNullOrEmpty(descTr) && descTr != descKey)
                    __result.strDisplayDesc = descTr;

                string compTr = I18n.Get(compKey);
                if (!string.IsNullOrEmpty(compTr) && compTr != compKey)
                    __result.strDisplayDescComplete = compTr;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] MakeTutorialObjectivePostfix failed: " + ex.Message);
            }
        }

        // ObjectivePanel.CompleteObjective -> localizes "Objective complete" title banner
        public static void ObjectivePanelCompleteObjectivePostfix(Ostranauts.Objectives.ObjectivePanel __instance)
        {
            if (!LangPack.Active || (UnityEngine.Object)(object)__instance == (UnityEngine.Object)null) return;
            try
            {
                var titleField = HarmonyLib.AccessTools.Field(typeof(Ostranauts.Objectives.ObjectivePanel), "_txtTitle");
                if (titleField != null)
                {
                    var txt = titleField.GetValue(__instance) as TMPro.TMP_Text;
                    if (txt != null)
                    {
                        var tr = I18n.Get("OBJECTIVE_COMPLETE");
                        if (!string.IsNullOrEmpty(tr) && tr != "OBJECTIVE_COMPLETE")
                        {
                            txt.text = tr.TrimEnd(':', ' ');
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] ObjectivePanelCompleteObjectivePostfix failed: " + ex.Message);
            }
        }

        // DerelictShipEntry.SetData -> localizes broker ship entry labels
        public static void DerelictShipEntrySetDataPostfix(Ostranauts.ShipGUIs.ShipBroker.DerelictShipEntry __instance)
        {
            if (!LangPack.Active || (UnityEngine.Object)(object)__instance == (UnityEngine.Object)null) return;
            try
            {
                var txtPublicNameField = HarmonyLib.AccessTools.Field(typeof(Ostranauts.ShipGUIs.ShipBroker.DerelictShipEntry), "txtPublicName");
                if (txtPublicNameField != null)
                {
                    var txt = txtPublicNameField.GetValue(__instance) as TMPro.TMP_Text;
                    if (txt != null && txt.text.StartsWith("Name: "))
                    {
                        var tr = I18n.Get("Name: ");
                        if (!string.IsNullOrEmpty(tr) && tr != "Name: ")
                            txt.text = tr + txt.text.Substring(6);
                    }
                }

                var txtLastVisitedField = HarmonyLib.AccessTools.Field(typeof(Ostranauts.ShipGUIs.ShipBroker.DerelictShipEntry), "txtLastVisited");
                if (txtLastVisitedField != null)
                {
                    var txt = txtLastVisitedField.GetValue(__instance) as TMPro.TMP_Text;
                    if (txt != null)
                    {
                        var tr = I18n.Get(txt.text);
                        if (!string.IsNullOrEmpty(tr) && tr != txt.text)
                            txt.text = tr;
                    }
                }

                var txtModelMakeField = HarmonyLib.AccessTools.Field(typeof(Ostranauts.ShipGUIs.ShipBroker.DerelictShipEntry), "txtModelMake");
                if (txtModelMakeField != null)
                {
                    var txt = txtModelMakeField.GetValue(__instance) as TMPro.TMP_Text;
                    if (txt != null)
                    {
                        var s = txt.text;
                        if (s.StartsWith("Model: "))
                            s = I18n.Get("Model: ") + s.Substring(7);
                        if (s.Contains(" Make: "))
                            s = s.Replace(" Make: ", I18n.Get(" Make: "));
                        txt.text = s;
                    }
                }

                var txtEstField = HarmonyLib.AccessTools.Field(typeof(Ostranauts.ShipGUIs.ShipBroker.DerelictShipEntry), "txtEstimatedValue");
                if (txtEstField != null)
                {
                    var txt = txtEstField.GetValue(__instance) as TMPro.TMP_Text;
                    if (txt != null && txt.text.StartsWith("Estimated value: $"))
                    {
                        txt.text = I18n.Get("Estimated value: $") + txt.text.Substring(18);
                    }
                }

                var txtRoomsField = HarmonyLib.AccessTools.Field(typeof(Ostranauts.ShipGUIs.ShipBroker.DerelictShipEntry), "txtRooms");
                if (txtRoomsField != null)
                {
                    var txt = txtRoomsField.GetValue(__instance) as TMPro.TMP_Text;
                    if (txt != null && txt.text == "No room specializations")
                    {
                        txt.text = I18n.Get("No room specializations");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] DerelictShipEntrySetDataPostfix failed: " + ex.Message);
            }
        }

        // GUISaveIndicator.EstablishSave -> localizes "Last Save: ..."
        public static void SaveIndicatorEstablishSavePostfix(GUISaveIndicator __instance)
        {
            if (!LangPack.Active || (UnityEngine.Object)(object)__instance == (UnityEngine.Object)null) return;
            try
            {
                var tmp = __instance._SaveTime;
                if (tmp != null && tmp.text != null && tmp.text.StartsWith("Last Save:"))
                {
                    var tr = I18n.Get("Last Save:");
                    tmp.text = (!string.IsNullOrEmpty(tr) && tr != "Last Save:")
                        ? tmp.text.Replace("Last Save:", tr)
                        : tmp.text.Replace("Last Save:", "Последнее сохранение:");
                }
            }
            catch { }
        }

        // GUISaveIndicator.Reset -> localizes "No Save" / empty state
        public static void SaveIndicatorResetPostfix(GUISaveIndicator __instance)
        {
            if (!LangPack.Active || (UnityEngine.Object)(object)__instance == (UnityEngine.Object)null) return;
            try
            {
                if (__instance._SaveTime != null)
                {
                    var tr = I18n.Get("No Save");
                    __instance._SaveTime.text = (!string.IsNullOrEmpty(tr) && tr != "No Save") ? tr : "Нет сохранения";
                }
            }
            catch { }
        }

        // Interaction.ApplyEffects -> localizes biography logs in character creation & in-game
        public static void InteractionApplyEffectsPostfix(List<string> aLog)
        {
            if (aLog == null || !LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                for (int i = 0; i < aLog.Count; i++)
                {
                    string s = aLog[i];
                    if (string.IsNullOrEmpty(s)) continue;
                    if (s.StartsWith("New "))
                    {
                        s = "Новый контакт: " + s.Substring(4);
                        s = s.Replace(" from ", " из ");
                        aLog[i] = s;
                    }
                    else if (s.Contains(" becomes a "))
                    {
                        s = s.Replace(" becomes a ", " теперь ").Replace(" to ", " для ");
                        aLog[i] = s;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] InteractionApplyEffectsPostfix failed: " + ex.Message);
            }
        }

        // Ledger.RecordTransaction -> localizes payer name for career events
        public static void LedgerRecordTransactionPrefix(ref string COThemFriendlyName)
        {
            if (!LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                if (COThemFriendlyName == "Career Event")
                {
                    COThemFriendlyName = "события карьеры";
                }
            }
            catch { }
        }

        // GUIPAXIntro.Show -> localizes Welcome / Early Access feedback splash screen
        public static void GUIPAXIntroShowPostfix(GUIPAXIntro __instance)
        {
            if (!LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var texts = __instance.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in texts)
                {
                    if (t == null) continue;
                    var txt = t.text;
                    if (string.IsNullOrEmpty(txt)) continue;

                    FontFallback.EnsureCyrillicFont(t);
                    t.fontStyle = FontStyles.Normal;
                    t.enableAutoSizing = true;
                    t.fontSizeMin = 8f;
                    t.fontSizeMax = Math.Max(t.fontSize, 26f);
                    t.overflowMode = TextOverflowModes.Overflow;
                    t.enableWordWrapping = true;

                    if (txt.IndexOf("WHAT'S NEW", StringComparison.OrdinalIgnoreCase) >= 0 || txt.IndexOf("WHAT", StringComparison.OrdinalIgnoreCase) >= 0 || txt.IndexOf("ЧТО НОВОГО", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        t.text = "ЧТО НОВОГО?";
                    }
                    else if (txt.IndexOf("WELCOME", StringComparison.OrdinalIgnoreCase) >= 0 || txt.IndexOf("ДОБРО ПОЖАЛОВАТЬ", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        t.text = "ДОБРО ПОЖАЛОВАТЬ В OSTRANAUTS";
                    }
                    else if (txt.IndexOf("FEEDBACK", StringComparison.OrdinalIgnoreCase) >= 0 || txt.IndexOf("ОТЗЫВ", StringComparison.OrdinalIgnoreCase) >= 0 || txt.IndexOf("ЦЕНИМ", StringComparison.OrdinalIgnoreCase) >= 0 || txt == "М")
                    {
                        t.text = "МЫ ЦЕНИМ ОТЗЫВЫ!";
                    }
                    else if (txt.IndexOf("active on Discord", StringComparison.OrdinalIgnoreCase) >= 0 || txt.IndexOf("Discord", StringComparison.OrdinalIgnoreCase) >= 0 && txt.IndexOf("сообществ", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        t.text = "Мы активно общаемся в Discord и других сообществах, где игроки помогают нам развивать игру.";
                    }
                    else if (txt.IndexOf("New Plot", StringComparison.OrdinalIgnoreCase) >= 0 || txt.IndexOf("Новый сюжет", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        t.text = "<indent=2.5em>• Новый сюжет и концовки\n• Система контрактов на головы\n• Новые карты станций\n• Поддержка Мастерской Steam\n• Достижения</indent>";
                    }
                    else if (txt.IndexOf("Click Anywhere", StringComparison.OrdinalIgnoreCase) >= 0 || txt.IndexOf("Нажмите в любом месте", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        t.text = "[ Нажмите в любом месте, чтобы продолжить ]";
                    }
                    else if (txt.IndexOf("links to these resources", StringComparison.OrdinalIgnoreCase) >= 0 || txt.IndexOf("Ссылки на эти ресурсы", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        t.text = "Ссылки на эти ресурсы доступны в главном меню и в настройках во время игры.";
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] GUIPAXIntroShowPostfix failed: " + ex.Message);
            }
        }

        // GUIChargenCareer.PageEvent -> fits ship specs text so it doesn't overlap the Take Ship button
        public static void GUIChargenCareerPageEventPostfix(GUIChargenCareer __instance)
        {
            try
            {
                var tfMain = Traverse.Create(__instance).Field("tfMain").GetValue<Transform>();
                if (tfMain == null) return;
                foreach (Transform child in tfMain)
                {
                    if (child.name.Contains("pnlShipInfo"))
                    {
                        var tmp = child.GetComponentInChildren<TMP_Text>();
                        if (tmp != null)
                        {
                            tmp.fontSize = 14f;
                            tmp.lineSpacing = -5f;
                            if (tmp.text.Contains("\n\n"))
                                tmp.text = tmp.text.Replace("\n\n", "\n");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] GUIChargenCareerPageEventPostfix failed: " + ex.Message);
            }
        }

        // GUIRosterRow.SetOwner -> localizes crew roster duties toggles
        public static void GUIRosterRowSetOwnerPostfix(GUIRosterRow __instance)
        {
            if (!LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var tr = Traverse.Create(__instance);
                TranslateToggleText(tr.Field("chkShore").GetValue<Toggle>(), "УВОЛЬНЕНИЕ");
                TranslateToggleText(tr.Field("chkAirlock").GetValue<Toggle>(), "ШЛЮЗЫ");
                TranslateToggleText(tr.Field("chkRestore").GetValue<Toggle>(), "РЕМОНТ");
                TranslateToggleText(tr.Field("chkBatteries").GetValue<Toggle>(), "БАТАРЕИ");
                TranslateToggleText(tr.Field("chkBottles").GetValue<Toggle>(), "КИСЛОРОД");
                TranslateToggleText(tr.Field("chkUnwear").GetValue<Toggle>(), "СНЯТЬ ШЛЕМ");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] GUIRosterRowSetOwnerPostfix failed: " + ex.Message);
            }
        }

        private static void TranslateToggleText(Toggle toggle, string newText)
        {
            if (toggle == null) return;
            var tmp = toggle.GetComponentInChildren<TMP_Text>();
            if (tmp != null) tmp.text = newText;
            var txt = toggle.GetComponentInChildren<Text>();
            if (txt != null) txt.text = newText;
        }

        // Ship.LogGetHeader -> translates vessel metadata header in ship terminal logs
        public static void ShipLogGetHeaderPostfix(List<JsonShipLog> __result)
        {
            if (__result == null || !LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                for (int i = 0; i < __result.Count; i++)
                {
                    var entry = __result[i];
                    if (entry == null || string.IsNullOrEmpty(entry.strEntry)) continue;
                    var s = entry.strEntry;
                    if (s.StartsWith("Vessel Name: ")) s = "Название судна: " + s.Substring(13);
                    else if (s.StartsWith("REGID: ")) s = "Регистрация: " + s.Substring(7);
                    else if (s.StartsWith("Date of Construction: ")) s = "Дата постройки: " + s.Substring(22);
                    else if (s.StartsWith("Make: ")) s = "Производитель: " + s.Substring(6);
                    else if (s.StartsWith("Model: ")) s = "Модель: " + s.Substring(7);
                    else if (s.StartsWith("Homeport: ")) s = "Порт приписки: " + s.Substring(10);
                    else if (s.StartsWith("Designation: ")) s = "Назначение: " + s.Substring(13);
                    else if (s.StartsWith("Total Mass: ")) s = "Общая масса: " + s.Substring(12);
                    entry.strEntry = s;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] ShipLogGetHeaderPostfix failed: " + ex.Message);
            }
        }

        // ShipStatus.PrintStatus -> translates ship diagnostic report
        public static void ShipStatusPrintStatusPostfix(ref string[] aValues)
        {
            if (aValues == null || !LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                ShipStatus.aNames = new string[]
                {
                    "КЛАССИФИКАЦИЯ СУДНА:", "МАССА СУДНА:", "ТРАНСПОНДЕР:", "АНТЕННА ТРАНСПОНДЕРА:", "НАВ. СТАНЦИЯ:", "РЕАКТОР:", "РЕАКТОР HE3:", "РЕАКТОР D2O:", "МАНЕВРОВЫЕ ДВИГАТЕЛИ:", "РАСПРЕДЕЛИТЕЛЬ РСУ:",
                    "РАБОЧЕЕ ТЕЛО РСУ:", "РЕЗЕРВНОЕ ПИТАНИЕ:", "НАСОСЫ О2 ЖИЗНЕОБЕСПЕЧЕНИЯ:", "ЗАПАСЫ О2 ЖИЗНЕОБЕСПЕЧЕНИЯ:", "ОБОГРЕВ ЖИЗНЕОБЕСПЕЧЕНИЯ:", "ОХЛАЖДЕНИЕ ЖИЗНЕОБЕСПЕЧЕНИЯ:"
                };

                for (int i = 0; i < aValues.Length; i++)
                {
                    if (aValues[i] == null) continue;
                    aValues[i] = aValues[i]
                        .Replace("ONLINE", "В СЕТИ")
                        .Replace("OFFLINE", "ОТКЛЮЧЕН")
                        .Replace("NOT FOUND", "НЕ НАЙДЕН")
                        .Replace("ERROR", "ОШИБКА");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] ShipStatusPrintStatusPostfix failed: " + ex.Message);
            }
        }

        private static readonly System.Reflection.PropertyInfo _mfdTitleProp = AccessTools.Property(typeof(Ostranauts.ShipGUIs.MFD.MFDPage), "Title");
        private static readonly System.Reflection.PropertyInfo _mfdLeftProp = AccessTools.Property(typeof(Ostranauts.ShipGUIs.MFD.MFDPage), "Left");
        private static readonly System.Reflection.PropertyInfo _mfdRightProp = AccessTools.Property(typeof(Ostranauts.ShipGUIs.MFD.MFDPage), "Right");

        // MFDPage.UpdateDisplay -> localizes comms, docking, and sensor MFD interface
        public static void MFDUpdateDisplayPrefix(Ostranauts.ShipGUIs.MFD.MFDPage __instance)
        {
            if (!LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var title = _mfdTitleProp?.GetValue(__instance) as string;
                if (!string.IsNullOrEmpty(title))
                {
                    if (title == "MAIN MENU") _mfdTitleProp.SetValue(__instance, "ГЛАВНОЕ МЕНЮ");
                    else if (title.StartsWith("CONNECTED WITH - ")) _mfdTitleProp.SetValue(__instance, "СВЯЗЬ: " + title.Substring(17));
                    else if (title.StartsWith("DOCKED WITH: ")) _mfdTitleProp.SetValue(__instance, "ПРИСТЫКОВАН К: " + title.Substring(13));
                    else if (title == "SELECT TARGET") _mfdTitleProp.SetValue(__instance, "ВЫБЕРИТЕ ЦЕЛЬ");
                    else if (title == "NO TARGETS IN RANGE") _mfdTitleProp.SetValue(__instance, "НЕТ ЦЕЛЕЙ В ЗОНЕ ДЕЙСТВИЯ");
                }

                var left = _mfdLeftProp?.GetValue(__instance) as List<string>;
                if (left != null)
                {
                    for (int i = 0; i < left.Count; i++)
                    {
                        var s = left[i];
                        if (string.IsNullOrEmpty(s)) continue;
                        if (s.StartsWith("ATC CHANNEL: ")) s = "ДИСПЕТЧЕР: " + s.Substring(13);
                        else if (s == "<LOCAL CHANNEL") s = "<ОБЩИЙ КАНАЛ";
                        else if (s == "<MESSAGE LOG") s = "<ЖУРНАЛ СВЯЗИ";
                        else if (s == "<DOCK INFO") s = "<СТЫКОВКА";
                        else if (s == "<UNREAD MESSAGES") s = "<НЕПРОЧИТАННЫЕ";
                        else if (s == "<SHOW ON NAV MAP") s = "<НА КАРТУ";
                        else if (s == "<PREVIOUS PAGE") s = "<ПРЕД. СТР.";
                        else if (s == "<CYCLE PAGE") s = "<СМЕНА СТР.";
                        else if (s == "NO CLEARANCE") s = "НЕТ РАЗРЕШЕНИЯ";
                        else if (s == "<REQUEST CLEARANCE") s = "<ЗАПРОС РАЗРЕШ.";
                        else if (s == "CLEARANCE AVAILABLE") s = "ЕСТЬ РАЗРЕШЕНИЕ";
                        else if (s == "<DOCKING") s = "<СТЫКОВКА";
                        else if (s == "<BACK") s = "<НАЗАД";
                        else if (s == "Message sent") s = "Отправлено";
                        else if (s == "Waiting for response") s = "Ожидание ответа";
                        else if (s == "Port Open") s = "Порт открыт";
                        else if (s.StartsWith("Docked: ")) s = "Стыковка: " + s.Substring(8);
                        left[i] = s;
                    }
                }

                var right = _mfdRightProp?.GetValue(__instance) as List<string>;
                if (right != null)
                {
                    for (int i = 0; i < right.Count; i++)
                    {
                        var s = right[i];
                        if (string.IsNullOrEmpty(s)) continue;
                        if (s == "HAIL SHIP>") s = "ВЫЗОВ>";
                        else if (s == "NEXT PAGE>") s = "СЛЕД. СТР.>";
                        else if (s == "RETURN TO") s = "";
                        else if (s == "MAIN MENU>") s = "В МЕНЮ>";
                        right[i] = s;
                    }
                }

                // Prevent text overlapping across columns on MFD screen
                int maxRows = Math.Max(left != null ? left.Count : 0, right != null ? right.Count : 0);
                for (int i = 0; i < maxRows; i++)
                {
                    bool hasLeft = left != null && i < left.Count && !string.IsNullOrEmpty(left[i]);
                    bool hasRight = right != null && i < right.Count && !string.IsNullOrEmpty(right[i]);
                    if (hasLeft && hasRight)
                    {
                        left[i] = ClampMFDString(left[i], 16, true);
                        right[i] = ClampMFDString(right[i], 16, false);
                    }
                    else if (hasLeft)
                    {
                        left[i] = ClampMFDString(left[i], 30, true);
                    }
                    else if (hasRight)
                    {
                        right[i] = ClampMFDString(right[i], 30, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] MFDUpdateDisplayPrefix failed: " + ex.Message);
            }
        }

        private static string ClampMFDString(string str, int maxLen, bool isLeft)
        {
            if (string.IsNullOrEmpty(str)) return str;
            var clean = System.Text.RegularExpressions.Regex.Replace(str, "<.*?>", "");
            if (clean.Length <= maxLen) return str;
            if (isLeft)
            {
                if (str.StartsWith("< ")) return "< " + clean.Substring(2, Math.Min(maxLen - 4, clean.Length - 2)).TrimEnd() + "..";
                if (str.StartsWith("<")) return "<" + clean.Substring(1, Math.Min(maxLen - 3, clean.Length - 1)).TrimEnd() + "..";
                return clean.Substring(0, Math.Min(maxLen - 2, clean.Length)).TrimEnd() + "..";
            }
            else
            {
                if (str.EndsWith(" >")) return clean.Substring(0, Math.Min(maxLen - 4, clean.Length - 2)).TrimEnd() + ".. >";
                if (str.EndsWith(">")) return clean.Substring(0, Math.Min(maxLen - 3, clean.Length - 1)).TrimEnd() + "..>";
                return clean.Substring(0, Math.Min(maxLen - 2, clean.Length)).TrimEnd() + "..";
            }
        }

        // GUITooltip2.SetToolTip -> translates ValueModule rough/precise value tooltips
        public static void TooltipSetToolTipPrefix(ref string strTitle, ref string strBody)
        {
            if (!LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                if (strTitle == "Rough Value")
                {
                    strTitle = "Приблизительная стоимость";
                    strBody = "Оценка стоимости предмета на глаз неопытным космиком.";
                }
                else if (strTitle == "Precise Value")
                {
                    strTitle = "Точная стоимость";
                    strBody = "Точная оценка стоимости опытным специалистом.";
                }
                else if (strTitle == "Shift and Active Effects")
                {
                    strTitle = "Эффекты смены и активности";
                }
                else if (strTitle == "Fast-Forward Risks")
                {
                    strTitle = "Риски перемотки времени";
                    strBody = "Эти состояния опасны для жизни, перемотка времени может привести к гибели!";
                }
            }
            catch { }
        }

        // GUITooltip.TooltipTextFormat4 -> translates interaction tooltip effects, needs, items
        public static void TooltipTextFormat4Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || !LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                __result = __result.Replace("<b>Effects:</b>", "<b>Эффекты:</b>")
                                   .Replace("<b>We need:</b>", "<b>Нам нужно:</b>")
                                   .Replace("<b>We can't be:</b>", "<b>Мы не можем быть:</b>")
                                   .Replace("<b>Tools required:</b>", "<b>Требуются инструменты:</b>")
                                   .Replace("<b>Input items required:</b>", "<b>Требуются предметы:</b>")
                                   .Replace("<b>Items given:</b>", "<b>Получаемые предметы:</b>")
                                   .Replace("<b>Items consumed:</b>", "<b>Расходуемые предметы:</b>")
                                   .Replace("Us: ", "Ты: ")
                                   .Replace("Them: ", "Собеседник: ")
                                   .Replace("Not always available.", "Доступно не всегда.")
                                   .Replace("Keeps control.", "Сохраняет контроль.");
            }
            catch { }
        }

        private static readonly Dictionary<string, string> PDAJobButtonMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "HULL", "КОРП" },
            { "HVAC", "КЛИМ" },
            { "POWR", "ЭНЕР" },
            { "SENS", "СЕНС" },
            { "CTRL", "УПР" },
            { "FURN", "МЕБ" },
            { "APPS", "ПРИЛ" },
            { "MISC", "РАЗН" },
            { "CANC", "ОТМН" },
            { "UNIN", "ДЕМО" },
            { "SCRA", "ЛОМ" },
            { "REPR", "РЕМ" },
            { "DISM", "РАЗБ" },
            { "HAUL", "ТАСК" },
            { "MINE", "ДОБ" },
            { "LOAD", "ЗАГР" }
        };

        // GUIPDA.ShowJobPaintUI -> localizes PDA construction categories and action button labels
        public static void ShowJobPaintUIPostfix(GUIPDA __instance)
        {
            if (!LangPack.Active || __instance == null || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var texts = __instance.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
                foreach (var t in texts)
                {
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    var trimmed = t.text.Trim();
                    if (PDAJobButtonMap.TryGetValue(trimmed, out var localized))
                    {
                        FontFallback.EnsureCyrillicFont(t);
                        t.text = localized;
                    }
                }
            }
            catch { }
        }

        // Ostranauts.Core.LogHandler.LogMessage -> translates in-game action log messages (gains, loses, etc.)
        public static void LogMessagePrefix(ref string logString)
        {
            if (string.IsNullOrEmpty(logString) || !LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                if (logString.Contains(" gains "))
                    logString = logString.Replace(" gains ", " получает ");
                if (logString.Contains(" loses "))
                    logString = logString.Replace(" loses ", " теряет ");
                if (logString.Contains(" no longer "))
                    logString = logString.Replace(" no longer ", " больше не ");
            }
            catch { }
        }

        private static readonly Dictionary<string, string> ObjectiveTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _objectivesLoaded;

        public static void EnsureObjectiveTranslationsLoaded()
        {
            if (_objectivesLoaded) return;
            _objectivesLoaded = true;
            try
            {
                var path = System.IO.Path.Combine(Plugin.DataDir.Value, "langs", LangPack.Code, "data", "tutorial_objectives.json");
                if (System.IO.File.Exists(path))
                {
                    var json = System.IO.File.ReadAllText(path);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        ObjectiveTranslations[prop.Name] = prop.Value.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] EnsureObjectiveTranslationsLoaded failed: " + ex.Message);
            }
        }

        public static string TranslateObjectiveText(string text)
        {
            if (string.IsNullOrEmpty(text) || !LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase))
                return text;

            EnsureObjectiveTranslationsLoaded();

            if (ObjectiveTranslations.TryGetValue(text, out var direct))
                return direct;

            var res = text;
            foreach (var kv in ObjectiveTranslations)
            {
                if (res.Contains(kv.Key))
                    res = res.Replace(kv.Key, kv.Value);
            }
            return res;
        }

        // ObjectivePanel.SetData & RefreshText -> localizes objectives list entries in PDA
        public static void ObjectivePanelSetDataPostfix(Ostranauts.Objectives.ObjectivePanel __instance)
        {
            if (!LangPack.Active || __instance == null || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var tr = Traverse.Create(__instance);
                var txtTitle = tr.Field("_txtTitle").GetValue<TMPro.TextMeshProUGUI>();
                var txtDesc = tr.Field("_txtDescription").GetValue<TMPro.TextMeshProUGUI>();
                if (txtTitle != null && !string.IsNullOrEmpty(txtTitle.text))
                {
                    FontFallback.EnsureCyrillicFont(txtTitle);
                    txtTitle.text = TranslateObjectiveText(txtTitle.text);
                }
                if (txtDesc != null && !string.IsNullOrEmpty(txtDesc.text))
                {
                    FontFallback.EnsureCyrillicFont(txtDesc);
                    txtDesc.text = TranslateObjectiveText(txtDesc.text);
                }
            }
            catch { }
        }

        public static void ObjectivePanelRefreshTextPostfix(Ostranauts.Objectives.ObjectivePanel __instance)
        {
            ObjectivePanelSetDataPostfix(__instance);
        }

        public static void ObjectivePlotPanelSetDataPostfix(Ostranauts.Objectives.ObjectivePlotPanel __instance)
        {
            if (!LangPack.Active || __instance == null || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var tr = Traverse.Create(__instance);
                var txtTitle = tr.Field("_txtTitle").GetValue<TMPro.TextMeshProUGUI>();
                var txtDesc = tr.Field("_txtDescription").GetValue<TMPro.TextMeshProUGUI>();
                if (txtTitle != null && !string.IsNullOrEmpty(txtTitle.text))
                {
                    FontFallback.EnsureCyrillicFont(txtTitle);
                    txtTitle.text = TranslateObjectiveText(txtTitle.text);
                }
                if (txtDesc != null && !string.IsNullOrEmpty(txtDesc.text))
                {
                    FontFallback.EnsureCyrillicFont(txtDesc);
                    txtDesc.text = TranslateObjectiveText(txtDesc.text);
                }
            }
            catch { }
        }

        // Interaction.FailReasons -> translate hardcoded English failure strings
        public static void FailReasonsPostfix(ref string __result)
        {
            if (!LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrEmpty(__result)) return;
            try
            {
                __result = __result
                    .Replace(" Missing item x", " Не хватает предметов x")
                    .Replace(" Missing item: ", " Не хватает предмета: ")
                    .Replace(" Item present but, ", " Предмет есть, но: ")
                    .Replace(" x Not Enough: ", " x недостаточно: ")
                    .Replace(" Item present but, Not Enough: ", " Предмет есть, но мало: ")
                    .Replace(" We are ", " Мы: ")
                    .Replace(" Target is ", " Цель: ")
                    .Replace(" Room is ", " Помещение: ")
                    .Replace(" 3rd party is ", " Третья сторона: ")
                    .Replace("Невозможно выполнить.", "Невозможно выполнить.")
                    .Replace("Can't do this.", "Невозможно выполнить.");

                // Translate "Is <ConditionName>" -> just condition name (the "Is" is a condition prefix)
                __result = System.Text.RegularExpressions.Regex.Replace(
                    __result, @"\bIs ([A-Z][a-zA-Z]+)", "$1");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] FailReasonsPostfix failed: " + ex.Message);
            }
        }

        // GUIReactor.Awake -> translate fusion reactor Chinese/English panel labels
        public static void GUIReactorAwakePostfix(GUIReactor __instance)
        {
            if (!LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var texts = __instance.GetComponentsInChildren<Text>(true);
                if (texts != null)
                {
                    foreach (var t in texts)
                    {
                        if (t == null || string.IsNullOrEmpty(t.text)) continue;
                        t.text = TranslateReactorText(t.text);
                    }
                }
                var tmpTexts = __instance.GetComponentsInChildren<TMP_Text>(true);
                if (tmpTexts != null)
                {
                    foreach (var t in tmpTexts)
                    {
                        if (t == null || string.IsNullOrEmpty(t.text)) continue;
                        FontFallback.EnsureCyrillicFont(t);
                        t.text = TranslateReactorText(t.text);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] GUIReactorAwakePostfix failed: " + ex.Message);
            }
        }

        private static string TranslateReactorText(string orig)
        {
            if (string.IsNullOrEmpty(orig)) return orig;
            var s = orig.Trim();
            if (_reactorDict.TryGetValue(s, out var tr)) return tr;
            if (_reactorDict.TryGetValue(orig, out var trExact)) return trExact;
            return orig;
        }

        private static readonly Dictionary<string, string> _reactorDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "初始化 / IGNITION SEQUENCE", "ЗАПУСК / ПОСЛЕДОВАТЕЛЬНОСТЬ" },
            { "1. 母线 / PWR BUS", "1. СЕТЬ / PWR BUS" },
            { "2. 炉腔泄压 / CORE PURGE", "2. ПРОДУВКА / CORE PURGE" },
            { "3. 激光电容器 / LAS CAP", "3. КОНДЕНСАТОР / LAS CAP" },
            { "4. 激光校准 / LAS ALIGN", "4. ЮСТИРОВКА / LAS ALIGN" },
            { "5. 靶丸给料 / PELL FEED", "5. ПОДАЧА ТОПЛИВА / PELL FEED" },
            { "6. 低温 / CRYO", "6. КРИОГЕНИКА / CRYO" },
            { "7. 燃油管制 / FUEL REG", "7. РЕГУЛЯТОР / FUEL REG" },
            { "8. 磁场线圈 / FIELD COILS", "8. КАТУШКИ / FIELD COILS" },
            { "9. 发电机 / MHD", "9. МГД-ГЕНЕРАТОР / MHD" },
            { "10. 点火 / IGNITION", "10. ЗАЖИГАНИЕ / IGNITION" },
            { "功率 / POWER (TW)", "МОЩНОСТЬ / POWER (TW)" },
            { "炉腔温度\nCORE TEMP\n(MeV)", "ТЕМПЕРАТУРА В РЕАКТОРЕ / CORE TEMP (MeV)" },
            { "炉腔温度 / CORE TEMP", "ТЕМПЕРАТУРА / CORE TEMP" },
            { "炉腔负压 / CORE PRESSURE", "ДАВЛЕНИЕ В РЕАКТОРЕ / CORE PRESSURE" },
            { "电容器充电 / CAPACITOR CHARGE", "ЗАРЯД КОНДЕНСАТОРОВ / CAPACITOR CHARGE" },
            { "燃料 / FUEL", "ТОПЛИВО / FUEL" },
            { "磁场线圈 / FIELD COILS", "МАГНИТНЫЕ КАТУШКИ / FIELD COILS" },
            { "点火\nIGNITION", "ЗАЖИГАНИЕ\nIGNITION" },
            { "点火 / IGNITION", "ЗАЖИГАНИЕ / IGNITION" },
            { "电池\nBATT.\n(%)", "АКБ\nBATT.\n(%)" },
            { "电池 / BATT.", "АКБ / BATT." },
            { "流量\nFLOW", "РАСХОД\nFLOW" },
            { "流量 / FLOW", "РАСХОД / FLOW" },
            { "总电力\nTOTAL", "ВСЕГО\nTOTAL" },
            { "总电力 / TOTAL", "ВСЕГО / TOTAL" },
            { "聚变炉\nFUS", "РЕАКТОР\nFUS" },
            { "聚变炉 / FUS", "РЕАКТОР / FUS" },
            { "磁流机\nMHD", "МГД\nMHD" },
            { "磁流机 / MHD", "МГД / MHD" },
            { "推进器\nTHR", "ДВИГ.\nTHR" },
            { "推进器 / THR", "ДВИГ. / THR" },
            { "配荷\nLOAD", "НАГРУЗКА\nLOAD" },
            { "配荷 / LOAD", "НАГРУЗКА / LOAD" },
            { "推进器 / THRUST", "ТЯГА / THRUST" },
            { "循环 / CYCLE", "ЦИКЛ / CYCLE" },
            { "开启 / OPEN", "ОТКРЫТО / OPEN" },
            { "关/ CLOSED", "ЗАКРЫТО / CLOSED" },
            { "关 / CLOSED", "ЗАКРЫТО / CLOSED" },
            { "关/ OFF", "ВЫКЛ / OFF" },
            { "关 / OFF", "ВЫКЛ / OFF" },
            { "活性 / ACTIVE", "АКТИВНО / ACTIVE" },
            { "低温\nCRYO", "КРИО\nCRYO" },
            { "低温 / CRYO", "КРИО / CRYO" },
            { "前置\nFWD", "ПЕРЕД\nFWD" },
            { "前置 / FWD", "ПЕРЕД / FWD" },
            { "后\nREAR", "ЗАД\nREAR" },
            { "后 / REAR", "ЗАД / REAR" },
            { "燃料调控\nFUEL REG", "РЕГУЛ.\nFUEL REG" },
            { "燃料调控 / FUEL REG", "РЕГУЛ. / FUEL REG" },
            { "附近车站/STATION\nPROXIMITY", "СТАНЦИЯ РЯДОМ / STATION PROXIMITY" },
            { "附近车站 / STATION PROXIMITY", "СТАНЦИЯ РЯДОМ / STATION PROXIMITY" },
            { "母线 / PWR BUS", "СЕТЬ / PWR BUS" },
            { "充电 / CHRG", "ЗАРЯД / CHRG" },
            { "电池 / BATT", "АКБ / BATT" },
            { "零 / ZERO", "НОЛЬ / ZERO" },
            { "最大 / MAX", "МАКС / MAX" },
            { "真空\nVAC", "ВАКУУМ\nVAC" },
            { "粗\nROUGH", "ФОРВАК.\nROUGH" },
            { "危险\nDANGER", "ОПАСНО\nDANGER" },
            { "亏电\nEMPTY", "РАЗРЯД\nEMPTY" },
            { "就绪\nREADY", "ГОТОВО\nREADY" },
            { "X射线\nX-RAY", "РЕНТГЕН\nX-RAY" },
            { "熔蚀层\nCORE LINER", "ФУТЕРОВКА\nCORE LINER" },
            { "激光电容器\nLAS CAP", "КОНДЕНСАТОР\nLAS CAP" },
            { "激光校准\nLAS ALIGN", "ЮСТИРОВКА\nLAS ALIGN" },
            { "靶丸给料\nPELL FEED", "ПОДАЧА\nPELL FEED" },
            { "警报\nWARN", "ТРЕВОГА\nWARN" },
            { "炉腔泄压\nCORE PURGE", "ПРОДУВКА\nCORE PURGE" },
            { "粗抽 / RGH", "ФОРВАКУУМ / RGH" },
            { "涡度 / TRB", "ТУРБО / TRB" }
        };
    }
}

