using System;
using System.Linq;
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

        // ---- prefab label auto-fit ---------------------------------------------
        // Panel labels sit in rects cut for the English word under a physical switch.
        // Cyrillic in the console fonts runs ~1.9x wider, so a faithful translation
        // overruns its module (that is what pushed "К управлению стыковкой" onto the
        // neighbouring tab). Rather than hand-abbreviating every label into things
        // like "ВЫР. СКОР.", fit it: let it wrap if the box has room for a second
        // line, otherwise shrink the type down to a legibility floor.
        private const float FitFloor = 0.62f;

        private static void FitLocalized(TMPro.TMP_Text t, string original, string translated)
        {
            if (t == null) return;
            t.text = translated;
            try { Embolden(t); AutoFit(t, original, translated); }
            catch (Exception ex) { WarnOnce("uifit", "[i18n] label auto-fit failed: " + ex.Message); }
        }

        // Entry point for the prefab/asset path (LocalizedText), which set text directly
        // and so never got wrapping, shrinking or the lamp weight fix.
        public static void FitLabel(TMPro.TMP_Text t, string translated)
            => FitLocalized(t, t != null ? t.text : null, translated);

        // Warning-lamp captions are painted a dark red that sits almost on top of the
        // unlit lamp's own dark red. The heavy pixel face carried the English text
        // through anyway; our thinner Cyrillic fallback does not, so the caption
        // disappears. Weight alone does not fix a contrast problem -- lift the
        // caption's brightness (keeping its hue, so a lit lamp still reads as red)
        // and bold it on top.
        private static void Embolden(TMPro.TMP_Text t)
        {
            if (t.GetComponentInParent<GUILamp>() == null) return;
            if ((t.fontStyle & TMPro.FontStyles.Bold) == 0) t.fontStyle |= TMPro.FontStyles.Bold;

            float hue, sat, val;
            Color.RGBToHSV(t.color, out hue, out sat, out val);
            if (val >= 0.85f && sat <= 0.45f) return;
            var lifted = Color.HSVToRGB(hue, Mathf.Min(sat, 0.35f), Mathf.Max(val, 0.92f));
            lifted.a = Mathf.Max(t.color.a, 1f);
            t.color = lifted;
            ReportFit("(lamp)", t.text, 0f, 0f, 0f, 0f, false, false);
        }

        private static void FitLocalized(Text t, string original, string translated)
        {
            if (t == null) return;
            t.text = translated;
        }

        private static void AutoFit(TMPro.TMP_Text t, string original, string translated)
        {
            var rt = t.rectTransform;
            float w = rt.rect.width, h = rt.rect.height;
            // Zero-sized rects are driven by a layout group; leave those to Unity.
            if (w <= 1f || h <= 1f || t.enableAutoSizing) return;

            t.ForceMeshUpdate();
            if (Fits(t, w, h)) return;

            float baseSize = t.fontSize;
            bool wrapped = false;
            // Only wrap where a second line can actually be shown.
            if (h >= baseSize * 1.9f && translated.IndexOf(' ') >= 0 && !t.enableWordWrapping)
            {
                t.enableWordWrapping = true;
                t.ForceMeshUpdate();
                wrapped = true;
                if (Fits(t, w, h)) { ReportFit(original, translated, w, h, baseSize, baseSize, true, false); return; }
            }

            t.enableAutoSizing = true;
            t.fontSizeMax = baseSize;
            t.fontSizeMin = Mathf.Max(6f, baseSize * FitFloor);
            t.ForceMeshUpdate();
            ReportFit(original, translated, w, h, baseSize, t.fontSize, wrapped, !Fits(t, w, h));
        }

        private static bool Fits(TMPro.TMP_Text t, float w, float h)
            => t.preferredWidth <= w + 0.5f && t.preferredHeight <= h + 0.5f;

        // Debug channel: every label that needed adjusting, so overflowing translations
        // can be found without hunting through screenshots. OVERFLOW marks the ones the
        // floor size still could not save -- those need a shorter wording, not a fit.
        private static string _fitReportPath;
        private static readonly HashSet<string> _fitSeen = new HashSet<string>();

        private static void ReportFit(string original, string translated, float w, float h,
                                      float baseSize, float finalSize, bool wrapped, bool overflows)
        {
            try
            {
                if (_fitReportPath == null)
                    _fitReportPath = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(typeof(Patches).Assembly.Location), "ui_fit_report.txt");
                if (!_fitSeen.Add(original + "" + translated)) return;
                System.IO.File.AppendAllText(_fitReportPath, string.Format(
                    "{0}\t{1}\t{2}\trect={3:0}x{4:0}\tsize {5:0.#}->{6:0.#}\t{7}\n",
                    overflows ? "OVERFLOW" : "fit", original.Replace("\n", "\\n"), translated.Replace("\n", "\\n"),
                    w, h, baseSize, finalSize, wrapped ? "wrapped" : "single"));
            }
            catch { }
        }

        // Nav station modules derive from NavModBase : MonoBehaviour, not GUIData, so
        // GUIDataInitPostfix never walked them -- which is why panel labels stayed
        // English even though strings.json already had translations for them. Only
        // NavModWeaponsControl overrides Start, and it chains to base, so a postfix
        // here fires for every module, including ones instantiated later.
        private static readonly Dictionary<string, string> EmptyMap = new Dictionary<string, string>();

        public static void NavModBaseStartPostfix(Ostranauts.ShipGUIs.NavStation.NavModBase __instance)
        {
            if (!LangPack.Active || __instance == null) return;
            try
            {
                // The module's own title sits on the draggable wrapper above it.
                var root = __instance.transform.parent != null ? __instance.transform.parent : __instance.transform;
                LocalizeHierarchy(root, EmptyMap);
            }
            catch (Exception ex)
            {
                WarnOnce("navmod", "[i18n] NavModBaseStartPostfix failed: " + ex.Message);
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
                            FitLocalized(t, norm, localized);
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
                                FitLocalized(t, norm, localized);
                            }
                        }
                        else
                        {
                            // Screen-level maps like ChargenBodyTextMap only cover the screen they
                            // were written for. Plenty of other UI labels (console button text,
                            // menu chrome) are baked into scenes as static TMP/UI Text that no C#
                            // code ever reads through DataHandler.GetString -- so LangPack.Strings
                            // already carries a translation for them (keyed by their own English
                            // text, same convention as DataHandler.GetString), it just never gets
                            // applied because nothing walks the hierarchy for them. Try that before
                            // giving up, so any such label already translated in strings.json
                            // works everywhere, not only on screens with a bespoke map.
                            if (LangPack.Strings.TryGetValue(norm, out var direct) && direct != norm)
                            {
                                FitLocalized(t, norm, direct);
                            }
                            else
                            {
                                I18n.RecordUntranslated("UI_TEXT", norm, t.transform.name);
                            }
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
                            FitLocalized(t, norm, localized);
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
                                FitLocalized(t, norm, localized);
                            }
                        }
                        else
                        {
                            // Screen-level maps like ChargenBodyTextMap only cover the screen they
                            // were written for. Plenty of other UI labels (console button text,
                            // menu chrome) are baked into scenes as static TMP/UI Text that no C#
                            // code ever reads through DataHandler.GetString -- so LangPack.Strings
                            // already carries a translation for them (keyed by their own English
                            // text, same convention as DataHandler.GetString), it just never gets
                            // applied because nothing walks the hierarchy for them. Try that before
                            // giving up, so any such label already translated in strings.json
                            // works everywhere, not only on screens with a bespoke map.
                            if (LangPack.Strings.TryGetValue(norm, out var direct) && direct != norm)
                            {
                                FitLocalized(t, norm, direct);
                            }
                            else
                            {
                                I18n.RecordUntranslated("UI_TEXT", norm, t.transform.name);
                            }
                        }
                    }
                }
            }
        }

        // Some tutorial beats build ObjectiveDesc at runtime as EnglishText + InputManager.GetGlyphString(key) + MoreText,
        // so the icon shown always matches the player's current input device (keyboard vs gamepad). A plain static
        // TUT_DESC_ translation would either lose that icon or freeze it to one device. Instead, TUT_DESC_ for these
        // beats holds a "{0}"-templated Russian string, and we resolve the same glyph key ourselves and format it in.
        // Beat name -> the input actions its ObjectiveDesc concatenates, in the order they appear.
        // TUT_DESC_ for these holds "{0}" (and "{1}" where the beat splices in two different
        // glyphs, e.g. OpenInventory). Language-neutral by design: the same map serves every
        // language pack, since only the surrounding prose is translated, never the action name.
        private static readonly Dictionary<string, string[]> TutorialGlyphActions = new Dictionary<string, string[]>
        {
            { "ForwardThrust", new[] { "Thrust Up" } },
            { "RightThrust", new[] { "Thrust Right" } },
            { "LeftThrust", new[] { "Thrust Left" } },
            { "RearThrust", new[] { "Thrust Down" } },
            { "CalibrateCW", new[] { "Turn CW" } },
            { "CalibrateCCW", new[] { "Turn CCW" } },
            { "MatchSpeed", new[] { "Toggle station keeping" } },
            { "StopSpin", new[] { "Attitude" } },
            { "SwitchToComms", new[] { "Click" } },
            { "DerelictComms", new[] { "Click" } },
            { "SelectMTT", new[] { "RightClick" } },
            { "SelectCompartment", new[] { "RightClick" } },
            { "NavUseShow", new[] { "RightClick" } },
            { "RestoreNavStation", new[] { "RightClick" } },
            { "DmgVizOff", new[] { "Toggle PDA Power Vizor" } },
            { "DmgVizShow", new[] { "Toggle PDA Power Vizor" } },
            { "VisualisePower", new[] { "Toggle PDA Power Vizor" } },
            { "HallwayConduit2", new[] { "RightClick" } },
            { "HallwayConduit4", new[] { "Click" } },
            { "HallwayConduit8", new[] { "Quick Move Item" } },
            { "HighlightObjects", new[] { "Show Hotkeys & Interactables" } },
            { "MouseoverObjective", new[] { "Click" } },
            { "OpenInventory", new[] { "Click", "Player Inventory" } },
            { "PickUpPermit", new[] { "RightClick" } },
            { "ToggleLightSwitch", new[] { "RightClick" } },
            { "UnpauseWorld", new[] { "Pause" } },
        };

        // Shared by fresh-creation and save-load paths: applies TUT_NAME_/TUT_DESC_/TUT_COMP_
        // for a given TutorialBeat onto an Objective's display fields.
        private static void ApplyTutorialObjectiveTranslation(Ostranauts.Core.Tutorials.TutorialBeat tutorialBeat, Ostranauts.Objectives.Objective objective)
        {
            string beatName = tutorialBeat.GetType().Name;
            string nameKey = "TUT_NAME_" + beatName;
            string descKey = "TUT_DESC_" + beatName;
            string compKey = "TUT_COMP_" + beatName;

            string nameTr = I18n.Get(nameKey);
            if (!string.IsNullOrEmpty(nameTr) && nameTr != nameKey)
                objective.strDisplayName = nameTr;

            string descTr = I18n.Get(descKey);
            if (!string.IsNullOrEmpty(descTr) && descTr != descKey)
            {
                if (TutorialGlyphActions.TryGetValue(beatName, out var actions) && descTr.Contains("{0}"))
                {
                    try
                    {
                        var glyphs = new object[actions.Length];
                        for (int i = 0; i < actions.Length; i++)
                            glyphs[i] = Ostranauts.InputControl.InputManager.GetGlyphString(actions[i]);
                        descTr = string.Format(descTr, glyphs);
                    }
                    catch (Exception ex) { Plugin.Log.LogWarning("[i18n] glyph format failed for " + beatName + ": " + ex.Message); }
                }
                objective.strDisplayDesc = descTr;
            }

            string compTr = I18n.Get(compKey);
            if (!string.IsNullOrEmpty(compTr) && compTr != compKey)
                objective.strDisplayDescComplete = compTr;

            // Once per beat: shows whether the key resolved and what the description ended up as,
            // so a "still English on screen" report can be traced without another guess.
            if (_tutDiag.Add(beatName))
                Plugin.Log.LogInfo("[i18n] tut '" + beatName + "': descKey=" + (descTr != descKey ? "ok" : "MISSING")
                    + " -> " + (objective.strDisplayDesc ?? "<null>"));
        }

        private static readonly HashSet<string> _tutDiag = new HashSet<string>();

        // Objective.MakeTutorialObjective -> localizes tutorial objective name and descriptions
        public static void MakeTutorialObjectivePostfix(Ostranauts.Core.Tutorials.TutorialBeat tutorialBeat, ref Ostranauts.Objectives.Objective __result)
        {
            if (!LangPack.Active || __result == null || tutorialBeat == null) return;
            try
            {
                ApplyTutorialObjectiveTranslation(tutorialBeat, __result);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] MakeTutorialObjectivePostfix failed: " + ex.Message);
            }
        }

        // ObjectiveTracker.LoadObjectives -> save games store strDisplayName/strDisplayDesc/
        // strDisplayDescComplete verbatim (Objective.GetJSON) and LoadObjectives restores them
        // verbatim too, bypassing MakeTutorialObjectivePostfix entirely. Without this, a tutorial
        // objective's text is frozen at whatever it was translated to (or not) the moment it was
        // first saved, even after strings.json is fixed. Re-apply the same TUT_* lookup here for
        // every reloaded objective that got a fresh TutorialBeat instance (unfinished tutorials).
        public static void ObjectiveTrackerLoadObjectivesPostfix(Ostranauts.Objectives.ObjectiveTracker __instance)
        {
            if (!LangPack.Active || __instance == null) return;
            try
            {
                foreach (var objective in __instance.AllObjectives)
                {
                    if (objective == null || !objective.bTutorial || objective.TutorialBeat == null) continue;
                    ApplyTutorialObjectiveTranslation(objective.TutorialBeat, objective);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] ObjectiveTrackerLoadObjectivesPostfix failed: " + ex.Message);
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

        internal static string VerbPresent(string key, int personIdx, string fallback)
        {
            if (TryGetVerb(key, out var vf) && vf.Present != null && vf.Present.Length > 0)
                return vf.Present[Math.Min(personIdx, vf.Present.Length - 1)];
            return fallback;
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
                    if (s.Contains(" gains ") || s.Contains(" loses "))
                    {
                        // VerbForms.Present is [1s, 2s, 3m, 3f, 3pl, 3n]
                        int personIdx = 2; // Default 3rd person singular
                        if (s.StartsWith("You ")) personIdx = 1; // 2nd person singular
                        else if (s.StartsWith("They ")) personIdx = 4; // 3rd person plural

                        if (s.Contains(" gains "))
                            s = s.Replace(" gains ", " " + VerbPresent("gains", personIdx, "получает") + " ");

                        if (s.Contains(" loses "))
                            s = s.Replace(" loses ", " " + VerbPresent("loses", personIdx, "теряет") + " ");

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
                    else if (title.StartsWith("CONNECTED WITH - ")) _mfdTitleProp.SetValue(__instance, "СВЯЗЬ:    " + title.Substring(17));
                    else if (title.StartsWith("СВЯЗЬ: ") && !title.StartsWith("СВЯЗЬ:   ")) _mfdTitleProp.SetValue(__instance, "СВЯЗЬ:    " + title.Substring(7));
                    else if (title.StartsWith("DOCKED WITH: ")) _mfdTitleProp.SetValue(__instance, "ПРИСТЫКОВАН К:    " + title.Substring(13));
                    else if (title == "SELECT TARGET") _mfdTitleProp.SetValue(__instance, "ВЫБОР ЦЕЛИ");
                    else if (title == "NO TARGETS IN RANGE") _mfdTitleProp.SetValue(__instance, "НЕТ ЦЕЛЕЙ В РАДИУСЕ");
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
                        else if (s == "<REQUEST CLEARANCE") s = "<ЗАПРОС ДОПУСКА";
                        else if (s == "CLEARANCE AVAILABLE") s = "ЕСТЬ РАЗРЕШЕНИЕ";
                        else if (s == "<DOCKING") s = "<СТЫКОВКА";
                        else if (s == "Message sent") s = "Отправлено";
                        else if (s == "Waiting for response") s = "Ожидание ответа";
                        else if (s == "Port Open") s = "Порт открыт";
                        else if (s.StartsWith("Docked: ")) s = "Стыковка: " + s.Substring(8);
                        else if (s.IndexOf("расстыковк", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("отстыковк", StringComparison.OrdinalIgnoreCase) >= 0) s = "< Отстыковка";
                        else if (s.IndexOf("стыковк", StringComparison.OrdinalIgnoreCase) >= 0) s = "< Стыковка";
                        else if (s.IndexOf("рынок", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("рыночн", StringComparison.OrdinalIgnoreCase) >= 0) s = "< Рынок";
                        else if (s.IndexOf("экипаж", StringComparison.OrdinalIgnoreCase) >= 0) s = "< Экипаж";
                        else if (s.IndexOf("топлив", StringComparison.OrdinalIgnoreCase) >= 0) s = "< SOS: Топливо";
                        else if (s.IndexOf("угроз", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("нападен", StringComparison.OrdinalIgnoreCase) >= 0) s = "< SOS: Угроза";
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
                        else if (s.IndexOf("расстыковк", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("отстыковк", StringComparison.OrdinalIgnoreCase) >= 0) s = "Отстыковка >";
                        else if (s.IndexOf("стыковк", StringComparison.OrdinalIgnoreCase) >= 0) s = "Стыковка >";
                        else if (s.IndexOf("рынок", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("рыночн", StringComparison.OrdinalIgnoreCase) >= 0) s = "Рынок >";
                        else if (s.IndexOf("экипаж", StringComparison.OrdinalIgnoreCase) >= 0) s = "Экипаж >";
                        else if (s.IndexOf("топлив", StringComparison.OrdinalIgnoreCase) >= 0) s = "SOS: Топливо >";
                        else if (s.IndexOf("угроз", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("нападен", StringComparison.OrdinalIgnoreCase) >= 0) s = "SOS: Угроза >";
                        right[i] = s;
                    }
                }

                // NOTE: no character-count clamping here any more. Cyrillic in
                // 'pixelmid' is 1.7-2.8x wider than Latin, so counting characters
                // both over- and under-shoots; GUIMFDDisplayShowMenuPostfix measures
                // the real pixel width instead and shrinks/truncates there.
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] MFDUpdateDisplayPrefix failed: " + ex.Message);
            }
        }

        // ---- MFD line fitter ----------------------------------------------------
        // The comms MFD is 13 fixed lines glued with '\n' into two legacy Text
        // components that share ONE full-width rect (left-aligned + right-aligned),
        // and each line index maps to a physical bezel button. Two consequences:
        //   * horizontalOverflow is Wrap in the prefab, so one overlong line wraps
        //     and shifts every button below it down by one row;
        //   * the columns are not separated, so a wide left line runs straight into
        //     the right one.
        // Cyrillic in 'pixelmid' is 1.7-2.8x wider than Latin, so the character
        // budgets baked into the game (MFDPage.ClampText: 20/44) are meaningless for
        // us. We re-emit both columns ourselves, measuring every line in pixels and
        // shrinking the pair (left[i] + right[i] must shrink together, they are
        // separate Text components and must keep identical line heights) until it
        // fits, truncating only as a last resort.
        private static readonly System.Reflection.FieldInfo _mfdTxtLeft = AccessTools.Field(typeof(Ostranauts.ShipGUIs.MFD.GUIMFDDisplay), "txtLeft");
        private static readonly System.Reflection.FieldInfo _mfdTxtRight = AccessTools.Field(typeof(Ostranauts.ShipGUIs.MFD.GUIMFDDisplay), "txtRight");
        private static readonly System.Reflection.FieldInfo _mfdTxtTitle = AccessTools.Field(typeof(Ostranauts.ShipGUIs.MFD.GUIMFDDisplay), "txtTitle");

        // Bezel arrows ("<BACK", "HAIL SHIP>") mark which physical button a row belongs
        // to; they are layout, not language, so we strip them before the dictionary
        // lookup and put them back after. That also lets "<MAIN MENU" (left bezel) and
        // "MAIN MENU>" (right bezel) share one key while rendering differently.
        // Lines that end in live data ("ATC CHANNEL: OKLG") are matched by their fixed
        // head; only the head is translated, the payload passes through untouched.
        private static readonly string[] _mfdPrefixes =
        {
            "ATC CHANNEL: ", "CONNECTED WITH - ", "DOCKED WITH: ", "MOORED WITH ", "DOCKED WITH ",
            "Docked: ", "CLEARED TO ", "Listening to ", "Connected with ",
            "OPEN CHANNEL TO ", "ATC Regional Control - ", "PUSHBACK & TAXI "
        };

        // Rows carrying pure telemetry -- ranges, reg IDs, callsigns, bare "?" -- are
        // data, not language. Recording them buries the real misses in the dump.
        private static readonly System.Text.RegularExpressions.Regex _mfdDataLine =
            // Deliberately narrow: single unspaced tokens only, so genuinely untranslated
            // multi-word labels ("NO CLEARANCE") still get reported.
            new System.Text.RegularExpressions.Regex(@"^(\?|-+|[0-9][0-9.,]*\s*(km|m|au|s|kg)?|[A-Z][A-Z0-9_-]{0,9})$");

        private static string LocalizeMFDLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string core = s.Trim();
            if (core.Length == 0) return s;

            // Some rows arrive pre-wrapped in a colour tag ("<color=#A0AFE790><CYCLE
            // PAGE</color>"). Peel the wrapper off for the lookup and re-apply it, so
            // the pack keys stay plain English instead of carrying markup.
            string wrapOpen = "", wrapClose = "";
            var wrap = System.Text.RegularExpressions.Regex.Match(core, "^(<(?:color|size)=[^>]+>)(.*)(</(?:color|size)>)$");
            if (wrap.Success)
            {
                wrapOpen = wrap.Groups[1].Value;
                wrapClose = wrap.Groups[3].Value;
                core = wrap.Groups[2].Value.Trim();
                if (core.Length == 0) return s;
            }

            // Arrow-qualified key wins, so a pack can render the left-bezel "<MAIN MENU"
            // and the right-bezel "MAIN MENU>" differently; the bare key is the fallback.
            string exact;
            if (LangPack.Strings.TryGetValue(core, out exact) && exact != core) return wrapOpen + exact + wrapClose;

            string pre = "", post = "";
            if (core.StartsWith("< ")) { pre = "< "; core = core.Substring(2); }
            else if (core.StartsWith("<") && !core.StartsWith("<color") && !core.StartsWith("<size")) { pre = "<"; core = core.Substring(1); }
            if (core.EndsWith(" >")) { post = " >"; core = core.Substring(0, core.Length - 2); }
            else if (core.EndsWith(">") && !core.EndsWith("</color>") && !core.EndsWith("</size>")) { post = ">"; core = core.Substring(0, core.Length - 1); }

            if (core.Length == 0) return s;
            // Separator rules and anything already Cyrillic need no lookup.
            if (core[0] == '-' || HasCyrillic(core)) return s;

            string tr;
            if (LangPack.Strings.TryGetValue(core, out tr) && tr != core) return wrapOpen + pre + tr + post + wrapClose;

            // MFDShipSelect composes this from a mode index plus two flags
            // ("Mode [1] NAME | Derelicts: ON"), so no single literal can cover it.
            var mode = System.Text.RegularExpressions.Regex.Match(core, @"^Mode \[(\d)\] (CALL|NAME) \| Derelicts: (ON|OFF)$");
            if (mode.Success)
                return wrapOpen + "Режим [" + mode.Groups[1].Value + "] "
                     + (mode.Groups[2].Value == "CALL" ? "ПОЗЫВНОЙ" : "ИМЯ")
                     + " | Дереликты: " + (mode.Groups[3].Value == "ON" ? "ВКЛ" : "ВЫКЛ") + wrapClose;

            foreach (var p in _mfdPrefixes)
            {
                if (!core.StartsWith(p, StringComparison.Ordinal)) continue;
                var head = p.TrimEnd();
                if (LangPack.Strings.TryGetValue(head, out tr) && tr != head)
                    return wrapOpen + pre + tr + p.Substring(head.Length) + core.Substring(p.Length) + post + wrapClose;
                break;
            }

            if (!_mfdDataLine.IsMatch(core)) I18n.RecordUntranslated("MFD_LINE", core, "mfd");
            return s;
        }

        private static bool HasCyrillic(string s)
        {
            for (int i = 0; i < s.Length; i++) if (s[i] >= 'Ѐ' && s[i] <= 'ӿ') return true;
            return false;
        }

        // Size ladder as a fraction of the line's vanilla size. Floor is ~0.62 --
        // below that 'pixelmid' stops being legible on the CRT overlay.
        private static readonly float[] _mfdLadder = { 1f, 0.9f, 0.8f, 0.72f, 0.66f, 0.62f };
        private const string MfdClrEven = "<color=#007FD8FF>";
        private const string MfdClrOdd = "<color=#a0afe7ff>";
        // Transparent glyph at the line's ORIGINAL size, injected only into lines we
        // actually shrank: legacy Text derives line height from the tallest glyph on
        // the line, so without it a shrunk line would pull everything below it up.
        // Tail on the left column / head on the right column keeps it in the gutter,
        // so neither column's outer edge alignment moves.
        private const string MfdSpacerChar = ".";

        public static void GUIMFDDisplayShowMenuPostfix(Ostranauts.ShipGUIs.MFD.GUIMFDDisplay __instance, string id, Ostranauts.Events.DTOs.MFDDTO mfdDto)
        {
            if (!LangPack.Active || mfdDto == null) return;
            if (__instance.PanelId != id) return;
            try
            {
                var tl = _mfdTxtLeft?.GetValue(__instance) as Text;
                var tr = _mfdTxtRight?.GetValue(__instance) as Text;
                if (tl == null || tr == null) return;

                // Wrap is what makes the buttons drift. Kill it on both columns.
                tl.horizontalOverflow = HorizontalWrapMode.Overflow;
                tr.horizontalOverflow = HorizontalWrapMode.Overflow;

                bool isComms = (id == Ostranauts.ShipGUIs.MFD.GUIMFDPageHost.DefaultCommsScreen);
                int rows = isComms ? 13 : 6;
                var left = Pad(mfdDto.Left, rows);
                var right = Pad(mfdDto.Right, rows);

                // Second translation pass. MFDUpdateDisplayPrefix mutates MFDPage.Left/
                // Right in place, which silently does nothing for pages that expose them
                // as expression-bodied getters returning a fresh list (MFDMessageLog) --
                // that is why the log screen stayed English. mfdDto holds the materialized
                // lists, so translating here catches every page uniformly.
                for (int i = 0; i < rows; i++)
                {
                    left[i] = LocalizeMFDLine(left[i]);
                    right[i] = LocalizeMFDLine(right[i]);
                }
                var txtTitle = _mfdTxtTitle?.GetValue(__instance) as Text;
                if (txtTitle != null) txtTitle.text = LocalizeMFDLine(txtTitle.text);

                float width = tl.rectTransform.rect.width;
                var sbL = new System.Text.StringBuilder();
                var sbR = new System.Text.StringBuilder();

                for (int i = 0; i < rows; i++)
                {
                    // Vanilla Format() renders even rows at 30 and odd rows at the
                    // component's own fontSize; FormatShort() renders every row at it.
                    bool small = isComms && (i % 2 == 0);
                    int baseSize = small ? 30 : tl.fontSize;
                    string clr = isComms ? ((i % 2 == 0) ? MfdClrEven : MfdClrOdd) : MfdClrOdd;

                    string l = left[i], r = right[i];
                    int size = FitPair(tl, tr, ref l, ref r, baseSize, width);

                    bool shrunk = size != baseSize;
                    string spacer = shrunk ? "<size=" + baseSize + "><color=#00000000>" + MfdSpacerChar + "</color></size>" : "";
                    string open = (size != baseSize || small || !isComms) ? "<size=" + size + ">" : "";
                    string close = string.IsNullOrEmpty(open) ? "" : "</size>";

                    // Leading space reproduces vanilla Format()'s indent on small rows.
                    sbL.Append(small ? " " : "").Append(open).Append(clr).Append(l).Append("</color>").Append(close).Append(spacer).Append('\n');
                    sbR.Append(spacer).Append(small ? " " : "").Append(open).Append(clr).Append(r).Append("</color>").Append(close).Append('\n');
                }

                tl.text = sbL.ToString();
                tr.text = sbR.ToString();
            }
            catch (Exception ex)
            {
                WarnOnce("mfdfit", "[i18n] MFD fitter failed: " + ex.Message);
            }
        }

        private static List<string> Pad(List<string> src, int rows)
        {
            var list = new List<string>(rows);
            if (src != null) list.AddRange(src);
            while (list.Count < rows) list.Add("");
            if (list.Count > rows) list.RemoveRange(rows, list.Count - rows);
            for (int i = 0; i < rows; i++) if (list[i] == null) list[i] = "";
            return list;
        }

        // Picks the largest size on the ladder at which left+right fit side by side.
        // If even the floor size overflows, truncates the longer side to reclaim the
        // difference. Returns the chosen size; l/r may be rewritten.
        private static int FitPair(Text tl, Text tr, ref string l, ref string r, int baseSize, float width)
        {
            if (string.IsNullOrEmpty(l) && string.IsNullOrEmpty(r)) return baseSize;

            // Two populated halves must not butt up against each other: without a
            // gutter "РЕЖИМЫ" and "ГЛАВНОЕ МЕНЮ>" render as one run of text.
            bool both = !string.IsNullOrEmpty(l) && !string.IsNullOrEmpty(r);
            float gutter = both ? MeasureWidth(tl, "MM", baseSize) : 0f;

            int chosen = baseSize;
            for (int s = 0; s < _mfdLadder.Length; s++)
            {
                int size = Mathf.Max(8, Mathf.RoundToInt(baseSize * _mfdLadder[s]));
                // The spacer only exists on lines we shrank, so it only costs budget there.
                float budget = width - gutter - ((s == 0) ? 0f : 2f * MeasureWidth(tl, MfdSpacerChar, baseSize));
                if (MeasureWidth(tl, l, size) + MeasureWidth(tr, r, size) <= budget) return size;
                chosen = size;
            }

            // Floor reached and still too wide: shave the longer side.
            float over = MeasureWidth(tl, l, chosen) + MeasureWidth(tr, r, chosen)
                         - (width - gutter - 2f * MeasureWidth(tl, MfdSpacerChar, baseSize));
            if (MeasureWidth(tl, l, chosen) >= MeasureWidth(tr, r, chosen))
                l = TrimToWidth(tl, l, MeasureWidth(tl, l, chosen) - over, chosen, true);
            else
                r = TrimToWidth(tr, r, MeasureWidth(tr, r, chosen) - over, chosen, false);
            return chosen;
        }

        // Drops characters from the middle-facing end until the string fits, keeping
        // the "<" / ">" bezel arrow that tells the player which button the row belongs to.
        private static string TrimToWidth(Text t, string s, float target, int size, bool isLeft)
        {
            if (string.IsNullOrEmpty(s) || target <= 0f) return s;
            string clean = System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");
            string prefix = "", suffix = "";
            if (isLeft && clean.StartsWith("<")) { prefix = clean.StartsWith("< ") ? "< " : "<"; clean = clean.Substring(prefix.Length); }
            if (!isLeft && clean.EndsWith(">")) { suffix = clean.EndsWith(" >") ? " >" : ">"; clean = clean.Substring(0, clean.Length - suffix.Length); }

            while (clean.Length > 1 && MeasureWidth(t, prefix + clean + ".." + suffix, size) > target)
                clean = clean.Substring(0, clean.Length - 1);
            return prefix + clean.TrimEnd() + ".." + suffix;
        }

        private static float MeasureWidth(Text t, string s, int size)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            var settings = t.GetGenerationSettings(new Vector2(0f, 0f));
            settings.fontSize = size;
            settings.resizeTextForBestFit = false;
            return t.cachedTextGeneratorForLayout.GetPreferredWidth(s, settings) / t.pixelsPerUnit;
        }

        private static readonly System.Reflection.FieldInfo _msgStatusList = AccessTools.Field(typeof(Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay), "_statusMessages");
        private static readonly System.Reflection.FieldInfo _msgTxtStatus = AccessTools.Field(typeof(Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay), "txtStatus");
        private static readonly System.Reflection.FieldInfo _msgTxtComms = AccessTools.Field(typeof(Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay), "txtComms");

        public static void GUIMessageDisplayPreSetupPrefix(Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay __instance)
        {
            if (!LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var list = _msgStatusList?.GetValue(__instance) as List<string>;
                if (list != null && list.Count > 0)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var s = list[i];
                        if (s.IndexOf("incoming port", StringComparison.OrdinalIgnoreCase) >= 0) list[i] = "Открытие входящего порта";
                        else if (s.IndexOf("routing table", StringComparison.OrdinalIgnoreCase) >= 0) list[i] = "Таблица маршрутизации";
                        else if (s.IndexOf("kernel driver", StringComparison.OrdinalIgnoreCase) >= 0) list[i] = "Загрузка драйвера ядра";
                        else if (s.IndexOf("message Processor", StringComparison.OrdinalIgnoreCase) >= 0) list[i] = "Обработчик сообщений интерфейса";
                    }
                }
            }
            catch { }
        }

        public static void GUIMessageDisplayUpdatePostfix(Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay __instance)
        {
            if (!LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var txtStatus = _msgTxtStatus?.GetValue(__instance) as TMPro.TMP_Text;
                if (txtStatus != null && !string.IsNullOrEmpty(txtStatus.text))
                {
                    if (txtStatus.text.Contains("DONE") || txtStatus.text.Contains("OPERATIONAL") || txtStatus.text.Contains("Connection established") || txtStatus.text.Contains("incoming port"))
                    {
                        txtStatus.text = txtStatus.text
                            .Replace("Open incoming port", "Открытие входящего порта")
                            .Replace("Build routing table", "Таблица маршрутизации")
                            .Replace("Load kernel driver", "Загрузка драйвера ядра")
                            .Replace("Interface message Processor", "Обработчик сообщений интерфейса")
                            .Replace("DONE", "ГОТОВО")
                            .Replace("OPERATIONAL", "РАБОТАЕТ")
                            .Replace("Connection established", "Соединение установлено");
                    }
                }

                var txtComms = _msgTxtComms?.GetValue(__instance) as TMPro.TMP_Text;
                if (txtComms != null && !string.IsNullOrEmpty(txtComms.text))
                {
                    if (txtComms.text.Contains("Connected with ") || txtComms.text.Contains("Automated Response Service") || txtComms.text.Contains("Inventory of "))
                    {
                        var t = txtComms.text
                            .Replace("Connected with OKLG ARS 2000 - Automated Response Service of the K-Leg: Port Azikiwe", "Соединение: ARS 2000 OKLG — Автоответчик станции K-Leg: Порт Азикиве")
                            .Replace("Connected with ", "Соединение: ")
                            .Replace("Automated Response Service of the K-Leg: Port Azikiwe", "Автоответчик станции K-Leg: Порт Азикиве")
                            .Replace("Automated Response Service", "Автоответчик")
                            .Replace("Port Azikiwe", "Порт Азикиве");
                        txtComms.text = LocalizeMarketString(t);
                    }
                }
            }
            catch { }
        }

        public static string LocalizeMarketString(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text
                .Replace("Inventory of AnyConsumerGoods at ", "Запас (Ширпотреб): ")
                .Replace("Inventory of AnyControlSystems at ", "Запас (Системы управления): ")
                .Replace("Inventory of AnyFood at ", "Запас (Продовольствие): ")
                .Replace("Inventory of AnyFurniture at ", "Запас (Мебель): ")
                .Replace("Inventory of AnyHull at ", "Запас (Детали корпуса): ")
                .Replace("Inventory of AnyIndustrialProducts at ", "Запас (Промтовары): ")
                .Replace("Inventory of AnyLifeSupport at ", "Запас (Системы СЖО): ")
                .Replace("Inventory of AnyMedia at ", "Запас (Медиа): ")
                .Replace("Inventory of AnyOres at ", "Запас (Руда): ")
                .Replace("Inventory of AnySensors at ", "Запас (Сенсоры): ")
                .Replace("Inventory of AnyTools at ", "Запас (Инструменты): ")
                .Replace("Inventory of AnyD2O at ", "Запас (D2O): ")
                .Replace("Inventory of AnyElectronics at ", "Запас (Электроника): ")
                .Replace("Inventory of AnyFusionParts at ", "Запас (Детали термояда): ")
                .Replace("Inventory of AnyHe3 at ", "Запас (Гелий-3): ")
                .Replace("Inventory of AnyHVAC at ", "Запас (Климат-контроль): ")
                .Replace("Inventory of AnyIntoxicants at ", "Запас (Дурман): ")
                .Replace("Inventory of AnyLuxuryGoods at ", "Запас (Предметы роскоши): ")
                .Replace("Inventory of AnyMedical at ", "Запас (Медикаменты): ")
                .Replace("Inventory of AnyMetal at ", "Запас (Металл): ")
                .Replace("Inventory of AnyPlastics at ", "Запас (Пластик): ")
                .Replace("Inventory of AnyScience at ", "Запас (Научные материалы): ")
                .Replace("Inventory of AnySpaceSuits at ", "Запас (Скафандры): ")
                .Replace("Inventory of AnyTextiles at ", "Запас (Текстиль): ")
                .Replace("Inventory of AnyTrash at ", "Запас (Мусор): ")
                .Replace("Inventory of AnyVolatiles at ", "Запас (Летучие вещества): ")
                .Replace("Inventory of AnyWater at ", "Запас (Вода): ")
                .Replace("Inventory of AnyWeapons at ", "Запас (Оружие): ")
                .Replace("Inventory of ", "Запас: ")
                .Replace(" at ", " — ")
                .Replace("Not producing at the moment", "В данный момент производство не ведётся");
        }

        public static void ShipMarketGetMarketDescriptionPostfix(ref string __result)
        {
            if (!LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(__result)) return;
            __result = LocalizeMarketString(__result);
        }

        public static void GUIMessageDisplayAddMessagePrefix(ref Ostranauts.Ships.Comms.ShipMessage mfdMessage)
        {
            if (!LangPack.Active || !string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase) || mfdMessage == null) return;
            try
            {
                if (!string.IsNullOrEmpty(mfdMessage.MessageText))
                {
                    var text = mfdMessage.MessageText;
                    if (text.StartsWith("Connected with "))
                    {
                        text = "Соединение: " + text.Substring(15);
                    }
                    if (text.IndexOf("Automated Response Service", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        text = text.Replace("Automated Response Service of the K-Leg: Port Azikiwe", "Автоответчик станции K-Leg: Порт Азикиве")
                                   .Replace("Automated Response Service", "Автоответчик");
                    }
                    mfdMessage.MessageText = LocalizeMarketString(text);
                }
            }
            catch { }
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

        public static class InteractionLogContext
        {
            [ThreadStatic] public static Interaction CurrentIA;
            [ThreadStatic] public static Dictionary<CondOwner, List<CondOwner>> AddedItemsMap = new Dictionary<CondOwner, List<CondOwner>>();
            [ThreadStatic] public static Dictionary<CondOwner, List<CondOwner>> DroppedItemsMap = new Dictionary<CondOwner, List<CondOwner>>();
        }

        public static void InteractionApplyEffectsPrefix(Interaction __instance)
        {
            InteractionLogContext.CurrentIA = __instance;
            if (InteractionLogContext.AddedItemsMap == null) InteractionLogContext.AddedItemsMap = new Dictionary<CondOwner, List<CondOwner>>();
            if (InteractionLogContext.DroppedItemsMap == null) InteractionLogContext.DroppedItemsMap = new Dictionary<CondOwner, List<CondOwner>>();
            InteractionLogContext.AddedItemsMap.Clear();
            InteractionLogContext.DroppedItemsMap.Clear();
        }

        public static void InteractionApplyEffectsCtxPostfix()
        {
            InteractionLogContext.CurrentIA = null;
        }

        public static void CondOwnerAddCOPrefix(CondOwner __instance, CondOwner objCO)
        {
            if (InteractionLogContext.CurrentIA != null && objCO != null)
            {
                if (!InteractionLogContext.AddedItemsMap.ContainsKey(__instance)) InteractionLogContext.AddedItemsMap[__instance] = new List<CondOwner>();
                InteractionLogContext.AddedItemsMap[__instance].Add(objCO);
            }
        }

        public static void CondOwnerDropCOPrefix(CondOwner __instance, CondOwner objCO)
        {
            if (InteractionLogContext.CurrentIA != null && objCO != null)
            {
                if (!InteractionLogContext.DroppedItemsMap.ContainsKey(__instance)) InteractionLogContext.DroppedItemsMap[__instance] = new List<CondOwner>();
                InteractionLogContext.DroppedItemsMap[__instance].Add(objCO);
            }
        }

        // verbs.json is keyed by the English 3rd-person form the game itself emits
        // ("gains", "gives", "takes", "loses"), while the log templates address the
        // verb by its bare stem ("<verb:gain>"). Accept either spelling, otherwise
        // the tag falls through to the raw English word.
        private static bool TryGetVerb(string key, out OstraI18n.Core.VerbForms vf)
        {
            vf = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (LangPack.Verbs.TryGetValue(key, out vf)) return true;
            if (!key.EndsWith("s"))
            {
                if (LangPack.Verbs.TryGetValue(key + "s", out vf)) return true;
                if (LangPack.Verbs.TryGetValue(key + "es", out vf)) return true;
                if (key.EndsWith("y") && LangPack.Verbs.TryGetValue(key.Substring(0, key.Length - 1) + "ies", out vf)) return true;
            }
            vf = null;
            return false;
        }

        private static string ProcessI18nTags(string text, CondOwner subject)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<verb:(.*?)>", m =>
            {
                string verbKey = m.Groups[1].Value;
                if (TryGetVerb(verbKey, out var vf) && vf.Present != null && vf.Present.Length > 0)
                {
                    int idx = string.Equals(subject.ShortName, LangPack.YouWord, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                    return GrammarUtils.SetCase(vf.Present[Math.Min(idx, vf.Present.Length - 1)]);
                }
                return verbKey;
            });

            text = System.Text.RegularExpressions.Regex.Replace(text, @"<(acc|dat|gen|ins|prep|nom)>(.*?)</\1>", m =>
            {
                string caseCode = m.Groups[1].Value;
                string itemsStr = m.Groups[2].Value;
                
                var parts = itemsStr.Split(new[] { ", " }, StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i];
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    
                    string strName = part;
                    string shortName = part;
                    if (part.Contains("|"))
                    {
                        var s = part.Split('|');
                        strName = s[0];
                        shortName = s[1];
                    }
                    parts[i] = LangPack.Resolver.Resolve(strName, shortName, caseCode);
                }
                return string.Join(", ", parts);
            });
            return text;
        }

        public static bool CondOwnerLogMessagePrefix(CondOwner __instance, ref string strMsg, string strColor, string strOwner, string strShort)
        {
            if (string.IsNullOrEmpty(strMsg) || !LangPack.Active) return true;

            try
            {
                if (InteractionLogContext.CurrentIA != null && strMsg.EndsWith("."))
                {
                    var ia = InteractionLogContext.CurrentIA;
                    string templateKey = null;
                    string defaultTemplate = null;
                    CondOwner subject = null;
                    List<CondOwner> items = null;
                    CondOwner target = null;

                    if (strMsg.Contains(" gains "))
                    {
                        templateKey = "InteractionLog_Gains";
                        defaultTemplate = "{0} <verb:gain> <acc>{1}</acc>.";
                        subject = __instance;
                        
                        if (InteractionLogContext.AddedItemsMap.TryGetValue(subject, out var added))
                        {
                            items = new List<CondOwner>(added);
                            added.Clear();
                        }
                    }
                    else if (strMsg.Contains(" gives "))
                    {
                        templateKey = "InteractionLog_Gives";
                        defaultTemplate = "{0} <verb:give> <acc>{1}</acc> to <dat>{2}</dat>.";
                        subject = ia.objUs;
                        target = ia.objThem;
                        items = ia.aLootItemGiveContract;
                    }
                    else if (strMsg.Contains(" takes "))
                    {
                        templateKey = "InteractionLog_Takes";
                        defaultTemplate = "{0} <verb:take> <acc>{1}</acc> from <gen>{2}</gen>.";
                        subject = ia.objUs;
                        target = ia.objThem;
                        items = ia.aLootItemTakeContract;
                    }
                    else if (strMsg.Contains(" loses "))
                    {
                        templateKey = "InteractionLog_Loses";
                        defaultTemplate = "{0} <verb:lose> <acc>{1}</acc>.";
                        subject = __instance;
                        if (InteractionLogContext.DroppedItemsMap.TryGetValue(subject, out var dropped))
                        {
                            items = new List<CondOwner>(dropped);
                            dropped.Clear();
                        }
                    }

                    if (templateKey != null)
                    {
                        string itemsStr = "";
                        if (items != null && items.Count > 0)
                        {
                            itemsStr = string.Join(", ", items.Select(i => (i.strName ?? "") + "|" + (i.ShortName ?? "")));
                        }
                        else
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(strMsg, @"^(.*?) (gains|gives|takes|loses) (.*?)(?: (to|from) (.*?))?\.$");
                            if (match.Success) itemsStr = match.Groups[3].Value;
                        }

                        string targetStr = "";
                        if (target != null) targetStr = (target.strName ?? "") + "|" + (target.ShortName ?? "");

                        string template = LangPack.Strings.TryGetValue(templateKey, out var t) ? t : defaultTemplate;
                        string formatted = string.Format(template, subject.ShortName, itemsStr, targetStr);

                        strMsg = ProcessI18nTags(formatted, subject);
                        return true;
                    }
                }

                if (strMsg.Contains(" no longer "))
                {
                    strMsg = strMsg.Replace(" no longer ", LangPack.Strings.TryGetValue("no_longer", out var noLong) ? noLong : " больше не ");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] CondOwnerLogMessagePrefix error: " + ex);
            }
            return true;
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
                        string key = prop.Name;
                        if (key.StartsWith("\"") && key.EndsWith("\""))
                        {
                            key = key.Substring(1, key.Length - 2);
                            key = key.Replace("\\\"", "\"");
                        }
                        // Beats whose ObjectiveName/Desc is literally "" (CrowbarHallwayStart et al.)
                        // were extracted as the key "\"\"", which unquotes to the empty string. An
                        // empty key makes the substring pass below call Replace("", ...), which
                        // throws -- silently killing every translation after the first miss.
                        if (key.Length == 0) continue;
                        ObjectiveTranslations[key] = prop.Value.GetString();
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
                if (kv.Key.Length == 0) continue;   // Replace("") throws; see loader above
                if (res.Contains(kv.Key))
                    res = res.Replace(kv.Key, kv.Value);
            }
            return res;
        }

        // For a tutorial panel, re-derive both labels from the beat's TUT_* keys and repaint.
        // Used by BOTH SetData and RefreshText: the panel can be painted from an Objective whose
        // strDisplayDesc is still the raw English (a save restores it verbatim, and AddObjective
        // may build the panel before any model-level fix-up runs), and RefreshText repaints from
        // the beat directly. Re-running the lookup here makes the view correct no matter which
        // order those happened in. Returns false when this is not a tutorial panel, so the caller
        // falls back to the literal-catalogue path used by plot objectives.
        private static bool TryRepaintTutorialPanel(Ostranauts.Objectives.ObjectivePanel instance)
        {
            var tr = Traverse.Create(instance);
            var objective = tr.Field("_objective").GetValue<Ostranauts.Objectives.Objective>();
            if (objective == null || objective.TutorialBeat == null) return false;

            ApplyTutorialObjectiveTranslation(objective.TutorialBeat, objective);

            var txtTitle = tr.Field("_txtTitle").GetValue<TMPro.TextMeshProUGUI>();
            if (txtTitle != null && !string.IsNullOrEmpty(objective.strDisplayName))
            {
                FontFallback.EnsureCyrillicFont(txtTitle);
                txtTitle.text = objective.strDisplayName;
            }
            var txtDesc = tr.Field("_txtDescription").GetValue<TMPro.TextMeshProUGUI>();
            if (txtDesc != null && !string.IsNullOrEmpty(objective.strDisplayDesc))
            {
                FontFallback.EnsureCyrillicFont(txtDesc);
                txtDesc.text = objective.strDisplayDesc;
            }
            return true;
        }

        // ObjectivePanel.SetData & RefreshText -> localizes objectives list entries in PDA
        public static void ObjectivePanelSetDataPostfix(Ostranauts.Objectives.ObjectivePanel __instance)
        {
            if (!LangPack.Active || __instance == null) return;
            try
            {
                if (TryRepaintTutorialPanel(__instance)) return;

                if (!string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase)) return;
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
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] ObjectivePanelSetDataPostfix failed: " + ex.Message);
            }
        }

        // ObjectivePanel.RefreshText runs on every input-device change and repaints BOTH labels
        // straight from TutorialBeat.ObjectiveName/ObjectiveDesc -- raw English, bypassing the
        // translated strDisplayName/strDisplayDesc. Falling through to the literal-catalogue
        // lookup below can only rescue the title: ObjectiveDesc arrives with the input glyph
        // already spliced in, so the runtime string never equals any catalogue key (the catalogue
        // stores the C# source expression). That mismatch is exactly the "translated title over
        // English description" state. So for tutorial objectives, re-run the TUT_* lookup for the
        // beat -- which also re-resolves the glyphs for the device that just changed -- and repaint
        // from the objective instead. Non-tutorial panels keep the old catalogue path.
        public static void ObjectivePanelRefreshTextPostfix(Ostranauts.Objectives.ObjectivePanel __instance)
        {
            if (!LangPack.Active || (UnityEngine.Object)(object)__instance == (UnityEngine.Object)null) return;
            try
            {
                if (!TryRepaintTutorialPanel(__instance))
                    ObjectivePanelSetDataPostfix(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] ObjectivePanelRefreshTextPostfix failed: " + ex.Message);
            }
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

        // The "Restore" action's interaction id is not a fixed value: the base game ships an
        // ACTUndamage* family (ACTUndamageTEMP, ACTUndamageNoSparksTEMP, ...) while the live
        // tutorial fires ACTStationNavUndamage, which exists in neither the base data nor the
        // language packs -- it is assembled at runtime. So match the stable "Undamage" stem
        // instead of enumerating ids. Ids are never translated (data files are keyed by them and
        // only strTitle/strDesc are overlaid), which is what makes this language-proof, and the
        // caller additionally requires the target to be the tutorial's own nav station, so the
        // loose stem match cannot fire on an unrelated repair action.
        private const string RestoreInteractionStem = "Undamage";

        private static readonly HashSet<string> _qabDiag = new HashSet<string>();

        // RestoreNavStation.OnQuickActionButton gates completion on `iA.strTitle == "Restore"`.
        // strTitle is a DISPLAY string that the content overlay localizes ("Восстановить"), so in
        // any non-English pack that comparison can never be true and the tutorial step is
        // unfinishable -- the translation itself breaks game progression. Re-run the same check
        // here against the interaction ID instead and complete the beat ourselves.
        public static void RestoreNavStationOnQuickActionButtonPostfix(
            Ostranauts.Core.Tutorials.RestoreNavStation __instance, GUIQuickActionButton qab)
        {
            if (!LangPack.Active || __instance == null || __instance.Finished) return;
            try
            {
                if ((UnityEngine.Object)(object)qab == (UnityEngine.Object)null) return;
                var ia = qab.IA;
                if (ia == null) return;

                var nav = CrewSimTut.playerShipNavStationRef;
                // One line per distinct interaction clicked while this beat is open: shows the ID
                // actually fired and whether the target matched, so a "trigger never fires" report
                // can be read straight off the log instead of guessed at.
                if (_qabDiag.Add(ia.strName ?? "<null>"))
                    Plugin.Log.LogInfo("[i18n] RestoreNavStation ждёт: клик id='" + (ia.strName ?? "<null>")
                        + "' title='" + (ia.strTitle ?? "<null>")
                        + "' them=" + (((UnityEngine.Object)(object)ia.objThem != (UnityEngine.Object)null) ? ia.objThem.strID : "<null>")
                        + " nav=" + (((UnityEngine.Object)(object)nav != (UnityEngine.Object)null) ? nav.strID : "<null>"));

                if (string.IsNullOrEmpty(ia.strName)
                    || ia.strName.IndexOf(RestoreInteractionStem, StringComparison.OrdinalIgnoreCase) < 0) return;
                if ((UnityEngine.Object)(object)ia.objThem == (UnityEngine.Object)null
                    || (UnityEngine.Object)(object)nav == (UnityEngine.Object)null) return;
                if (ia.objThem.strID != nav.strID) return;

                __instance.Finished = true;
                Plugin.Log.LogInfo("[i18n] RestoreNavStation: завершено по ID взаимодействия '"
                    + ia.strName + "' (локализованный strTitle обошёл ванильную проверку)");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] RestoreNavStationOnQuickActionButtonPostfix: " + ex.Message);
            }
        }
        public static void GUIGameCreditsInitPostfix() {}
    }
}
