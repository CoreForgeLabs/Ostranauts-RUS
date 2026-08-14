using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;

namespace OstraI18n
{
    [BepInPlugin(GUID, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.coreforge.ostra.i18n";
        public const string Name = "OstraI18n";
        public const string Version = "2.0.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Language;
        internal static ConfigEntry<string> DataDir;
        internal static ConfigEntry<bool> FormalYou;
        internal static ConfigEntry<bool> QaMode;
        private static SynchronizationContext unitySync;

        private void Awake()
        {
            Instance = this;
            Application.runInBackground = true; // session-0/headless: keep player loop pumping without window focus
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "Master switch. false = vanilla game.");
            Language = Config.Bind("General", "Language", "ru", "Target language code (ru, en).");
            DataDir = Config.Bind("General", "DataDir", Path.Combine(Paths.PluginPath, "OstraI18n"), "Folder with language packs.");
            FormalYou = Config.Bind("General", "FormalYou", false, "true = vy-form for player address.");
            QaMode = Config.Bind("General", "QaMode", false, "true = псевдоязык ⟦...⟧ поверх переводов, для поиска непокрытых строк.");

            if (!Enabled.Value) { Log.LogInfo("[i18n] disabled by config"); return; }

            try { LangPack.Load(DataDir.Value, Language.Value, FormalYou.Value); }
            catch (Exception ex) { Log.LogError("[i18n] data load failed, plugin off: " + ex); return; }

            int ok = 0, failed = 0;
            PatchRunner.ApplyAll(ref ok, ref failed);
            VersionGuard.CheckAndLog(Log);
            try
            {
                I18n.Init(DataDir.Value, LangPack.Code);
                I18n.QaMode = QaMode.Value;
                if (LiteralPatcher.LoadCatalog(DataDir.Value) > 0)
                    LiteralPatcher.ApplyAll(new Harmony(GUID + ".literals"));
            }
            catch (Exception ex) { Log.LogError("[i18n] literals failed: " + ex); }
            try
            {
                if (PrefabBinder.LoadCatalog(DataDir.Value) > 0)
                {
                    PrefabBinder.BindScenes();
                    PrefabBinder.ApplyAssetHook(new Harmony(GUID + ".prefabs"));
                }
            }
            catch (Exception ex) { Log.LogError("[i18n] prefab binder failed: " + ex); }
            try { ContentOverlay.Init(DataDir.Value, LangPack.Code, new Harmony(GUID + ".content")); }
            catch (Exception ex) { Log.LogError("[i18n] content overlay init failed: " + ex); }
            try { ManualAssets.Init(DataDir.Value, LangPack.Code, new Harmony(GUID + ".manuals")); }
            catch (Exception ex) { Log.LogError("[i18n] manual assets init failed: " + ex); }
            try { ImagePatcher.Init(DataDir.Value, LangPack.Code, new Harmony(GUID + ".images")); }
            catch (Exception ex) { Log.LogError("[i18n] image patcher init failed: " + ex); }
            try { MenuLanguageAstronaut.Init(DataDir.Value); }
            catch (Exception ex) { Log.LogError("[i18n] menu language astronaut init failed: " + ex); }
            if (QaMode.Value)
            {
                LocalizedText.OverflowReportPath = Path.Combine(Paths.PluginPath, "OstraI18n", "overflow_report.tsv");
                File.WriteAllText(LocalizedText.OverflowReportPath, "key\tpath\twidth\theight\n");
            }
            Log.LogInfo("[i18n] OstraI18n " + Version + ": " + ok + " patches ok, " + failed + " failed/skipped, lang=" + Language.Value);

            unitySync = SynchronizationContext.Current;
            Log.LogInfo("[i18n] unity sync context: " + (unitySync != null ? unitySync.GetType().FullName : "NULL"));

            var th = new Thread(Orchestrator) { IsBackground = true, Name = "OstraI18nOrchestrator" };
            th.Start();
        }

        private void Start()
        {
            try { File.WriteAllText(Path.Combine(DataDir.Value, "start_marker.txt"), "Start() ok " + DateTime.Now.ToString("HH:mm:ss")); }
            catch { }
            Log.LogInfo("[i18n] Start() fired");
        }

        private void Orchestrator()
        {
            try { StaticHarness(); } catch (Exception ex) { try { Log.LogError("[i18n] static harness outer: " + ex); } catch { } }

            // Mod probe: verify the OstraRU mod actually overrode game strings
            try { ModProbe(); } catch (Exception ex) { try { Log.LogError("[i18n] mod probe outer: " + ex); } catch { } }

            // Font probe on main thread, retry-post until the file appears (main thread may be busy loading data)
            var probeFile = Path.Combine(DataDir.Value, "fontprobe.txt");
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Thread.Sleep(20000);
                try { if (File.Exists(probeFile) && File.ReadAllText(probeFile).Contains("PROBE DONE")) break; } catch { }
                PostToMain("font-probe", FontProbeBody);
            }

            // Live test: poll up to 15 min for a real game session
            int waited = 0;
            while (waited < 900000)
            {
                try { if (DataHandler.mapCOs != null && DataHandler.mapCOs.Count > 10) break; } catch { }
                Thread.Sleep(5000); waited += 5000;
            }
            PostToMain("live-self-test", LiveTestBody);
        }

        private static void PostToMain(string label, Action body, bool quiet = false)
        {
            try
            {
                if (unitySync != null)
                {
                    unitySync.Post(_ =>
                    {
                        try { body(); }
                        catch (Exception ex) { try { Log.LogError("[i18n] " + label + " body failed: " + ex); } catch { } }
                    }, null);
                    if (!quiet) Log.LogInfo("[i18n] posted " + label + " to main thread");
                }
                else
                {
                    Log.LogWarning("[i18n] no sync context; running " + label + " on background thread (risky)");
                    body();
                }
            }
            catch (Exception ex) { try { Log.LogError("[i18n] PostToMain " + label + ": " + ex); } catch { } }
        }

        // ---------------- static harness (background thread, no Unity API) ----------------
        private static void StaticHarness()
        {
            var outStatic = Path.Combine(DataDir.Value, "selftest_static.txt");
            var sw = new System.Text.StringBuilder();
            sw.AppendLine("=== OstraI18n STATIC HARNESS (thread) ===");
            try
            {
                sw.AppendLine("t0 " + DateTime.Now.ToString("HH:mm:ss"));
                File.WriteAllText(outStatic, sw.ToString());

                int waitedMs = 0;
                while (waitedMs < 45000)
                {
                    if (GrammarUtils.partsOfSpeechStr != null && GrammarUtils.partsOfSpeechStr.Count > 0) break;
                    Thread.Sleep(1000); waitedMs += 1000;
                }
                sw.AppendLine("partsOfSpeechStr tables: " + (GrammarUtils.partsOfSpeechStr != null ? GrammarUtils.partsOfSpeechStr.Count : -1) + " after " + waitedMs + "ms");
                foreach (var c in new[] { "subj", "obj", "pos" })
                {
                    if (GrammarUtils.partsOfSpeechStr != null && GrammarUtils.partsOfSpeechStr.TryGetValue(c, out var row))
                        sw.AppendLine("table[" + c + "] = " + string.Join(",", row));
                }

                var ent = GrammarUtils.entityMap["us"];
                var tdSays = new TokenData { alias = "us", verbForms = new[] { "says", "say" } };
                var persons = new[] {
                    GrammarUtils.PronounInflection.First, GrammarUtils.PronounInflection.Second,
                    GrammarUtils.PronounInflection.ThirdMasculine, GrammarUtils.PronounInflection.ThirdFeminine,
                    GrammarUtils.PronounInflection.ThirdNeuter, GrammarUtils.PronounInflection.ThirdNeuterNonHuman };
                foreach (var pi in persons)
                {
                    ent.InflectionIndex = pi; ent.named = false; ent.lastSubjectiveWasPronoun = false;
                    GrammarUtils.insertNoLonger = false;
                    GrammarUtils.interactionOutput.Clear(); GrammarUtils.caret = -1;
                    GrammarUtils.Verb(tdSays);
                    sw.AppendLine("Verb[says] " + pi + " -> '" + GrammarUtils.interactionOutput + "'");
                }
                ent.InflectionIndex = GrammarUtils.PronounInflection.ThirdMasculine;
                GrammarUtils.insertNoLonger = true;
                GrammarUtils.interactionOutput.Clear(); GrammarUtils.caret = -1;
                GrammarUtils.Verb(tdSays);
                sw.AppendLine("Verb[says]+noLonger male -> '" + GrammarUtils.interactionOutput + "'");
                GrammarUtils.insertNoLonger = false;

                var tdIs = new TokenData { alias = "us", verbForms = new[] { "is", "are" } };
                GrammarUtils.interactionOutput.Clear(); GrammarUtils.caret = -1;
                GrammarUtils.Verb(tdIs);
                sw.AppendLine("Verb[is] male -> '" + GrammarUtils.interactionOutput + "' (empty = copula dropped, correct for RU)");

                foreach (var pi in new[] { GrammarUtils.PronounInflection.ThirdMasculine, GrammarUtils.PronounInflection.ThirdFeminine, GrammarUtils.PronounInflection.Second })
                {
                    ent.InflectionIndex = pi; ent.named = true;
                    GrammarUtils.interactionOutput.Clear(); GrammarUtils.caret = -1;
                    GrammarUtils.AttemptSubstitution(new TokenData { alias = "us", category = "subj" });
                    sw.AppendLine("Subj[" + pi + "] -> '" + GrammarUtils.interactionOutput + "'");
                    GrammarUtils.interactionOutput.Clear(); GrammarUtils.caret = -1;
                    GrammarUtils.AttemptSubstitution(new TokenData { alias = "us", category = "obj" });
                    sw.AppendLine("Obj[" + pi + "] -> '" + GrammarUtils.interactionOutput + "'");
                }
                ent.InflectionIndex = GrammarUtils.PronounInflection.Second;
                GrammarUtils.interactionOutput.Clear(); GrammarUtils.caret = -1;
                GrammarUtils.AttemptProperName(new TokenData { alias = "us" });
                sw.AppendLine("ProperName[you] -> '" + GrammarUtils.interactionOutput + "'");
                ent.Reset();
                sw.AppendLine("STATIC DONE");
            }
            catch (Exception ex) { sw.AppendLine("STATIC ERROR: " + ex); }
            try { File.WriteAllText(outStatic, sw.ToString()); } catch { }
            try { Log.LogInfo("[i18n] static self-test done -> " + outStatic); } catch { }
        }

        // ---------------- mod probe (background thread): did the OstraRU mod override game strings? ----------------
        private static void ModProbe()
        {
            var outFile = Path.Combine(DataDir.Value, "modprobe.txt");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== OstraRU MOD PROBE ===");
            try
            {
                int waitedMs = 0;
                while (waitedMs < 180000)
                {
                    if (DataHandler.dictStrings != null && DataHandler.dictStrings.Count > 100) break;
                    Thread.Sleep(2000); waitedMs += 2000;
                }
                sb.AppendLine("dictStrings count: " + (DataHandler.dictStrings != null ? DataHandler.dictStrings.Count : -1) + " after " + waitedMs + "ms");
                sb.AppendLine("partsOfSpeechStr tables: " + (GrammarUtils.partsOfSpeechStr != null ? GrammarUtils.partsOfSpeechStr.Count : -1));
                string[] probeKeys = { "GUI_QUIT_DESCRIPTION", "GUI_QUIT_CONFIRM", "GUI_OPTIONS_SETTINGS", "GUI_OPTIONS_MODS", "GUI_TIME_PAUSE_TITLE", "GUI_HUD_LOG_TITLE" };
                foreach (var k in probeKeys)
                {
                    string val = "<missing>";
                    try { if (DataHandler.dictStrings != null && DataHandler.dictStrings.TryGetValue(k, out var v)) val = v; } catch { }
                    sb.AppendLine(k + " => " + val);
                }
                sb.AppendLine("MOD PROBE DONE");
            }
            catch (Exception ex) { sb.AppendLine("MOD PROBE ERROR: " + ex); }
            try { File.WriteAllText(outFile, sb.ToString()); } catch { }
            try { Log.LogInfo("[i18n] mod probe done -> modprobe.txt"); } catch { }
        }

        // ---------------- font probe (main thread via Post, read-only) ----------------
        private static void FontProbeBody()
        {
            var probeFile = Path.Combine(DataDir.Value, "fontprobe.txt");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== FONT PROBE (entry marker) ===");
            try { File.WriteAllText(probeFile, sb.ToString()); } catch { }
            try
            {
                var texts = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>(true);
                sb.AppendLine("TMP_Text objects: " + texts.Length);
                var fonts = new System.Collections.Generic.List<TMPro.TMP_FontAsset>();
                foreach (var t in texts)
                {
                    try { if (t.font != null && !fonts.Contains(t.font)) fonts.Add(t.font); } catch { }
                }
                sb.AppendLine("distinct fonts in use: " + fonts.Count);
                foreach (var f in fonts)
                {
                    string src = "?";
                    try { src = f.sourceFontFile != null ? f.sourceFontFile.name : "?"; } catch { }
                    string mode = "?";
                    try { mode = f.atlasPopulationMode.ToString(); } catch { }
                    string hasYa = "?", hasA = "?", hasZhz = "?";
                    try { hasYa = f.HasCharacter('\u042F').ToString(); } catch { }
                    try { hasA = f.HasCharacter('\u0410').ToString(); } catch { }
                    try { hasZhz = f.HasCharacter('\u0436').ToString(); } catch { }
                    string fbNames = "";
                    try
                    {
                        if (f.fallbackFontAssetTable != null)
                            foreach (var fb in f.fallbackFontAssetTable)
                                if (fb != null) fbNames += fb.name + ";";
                    }
                    catch { }
                    sb.AppendLine("FONT: " + f.name + " | src=" + src + " | mode=" + mode + " | hasYa=" + hasYa + " hasA=" + hasA + " haszh=" + hasZhz + (fbNames.Length > 0 ? " | fallbacks: " + fbNames : ""));
                }
                try
                {
                    var def = TMPro.TMP_Settings.defaultFontAsset;
                    if (def != null)
                    {
                        string dmode = "?";
                        try { dmode = def.atlasPopulationMode.ToString(); } catch { }
                        sb.AppendLine("TMP default font: " + def.name + " | mode=" + dmode + " | hasYa=" + def.HasCharacter('\u042F'));
                    }
                }
                catch { }
                sb.AppendLine("PROBE DONE");
            }
            catch (Exception ex) { sb.AppendLine("PROBE ERROR: " + ex); }
            try { File.WriteAllText(probeFile, sb.ToString()); } catch { }
            try { Log.LogInfo("[i18n] font probe done -> fontprobe.txt"); } catch { }
        }

        // ---------------- live harness (main thread via Post, real CondOwners) ----------------
        private static void LiveTestBody()
        {
            var outLive = Path.Combine(DataDir.Value, "selftest_live.txt");
            var lw = new System.Text.StringBuilder();
            lw.AppendLine("=== OstraI18n LIVE TEST ===");
            try
            {
                lw.AppendLine("mapCOs count: " + (DataHandler.mapCOs != null ? DataHandler.mapCOs.Count : -1));
                CondOwner male = null, female = null, nb = null, nonhuman = null;
                foreach (var co in DataHandler.mapCOs.Values)
                {
                    try
                    {
                        if (male == null && co.HasCond("IsMale")) male = co;
                        else if (female == null && co.HasCond("IsFemale")) female = co;
                        else if (nb == null && co.HasCond("IsNB")) nb = co;
                        else if (nonhuman == null && !co.HasCond("IsHuman")) nonhuman = co;
                    }
                    catch { }
                    if (male != null && female != null && nb != null && nonhuman != null) break;
                }
                lw.AppendLine("male: " + (male != null ? male.ShortName : "NULL"));
                lw.AppendLine("female: " + (female != null ? female.ShortName : "NULL"));
                lw.AppendLine("nb: " + (nb != null ? nb.ShortName : "NULL"));
                lw.AppendLine("nonhuman: " + (nonhuman != null ? nonhuman.ShortName : "NULL"));

                var prep = typeof(DataHandler).GetMethod("PrepareInflectedString",
                    BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(object), typeof(string) }, null);
                if (prep == null) { lw.AppendLine("ABORT: PrepareInflectedString not found"); File.WriteAllText(outLive, lw.ToString()); return; }

                string[] templates = {
                    "[us-subj] [says] hello.",
                    "[us-subj] [says] hello. Then [us-subj] [waves] and [us-subj] [is] ready.",
                    "[us-subj] [was] late.",
                    "[us] [waves] back.",
                    "I see [us-obj]. [us-subj] [smiles].",
                    "[us-subj] [wants] [us-obj] back.",
                    // Task 6.5: live-verify the 4 new synthetic pseudo-verb keys through the
                    // real production GrammarUtils.GetInflectedString path (no UI clicking --
                    // this harness already runs automatically at startup, see class comment
                    // above). "[us] [is] ready." above must stay unchanged (plain [is] regression
                    // check); these new lines confirm each new key routes through dictVerbs/
                    // VerbPrefix and renders its own distinct paradigm. All templates in this
                    // array are deliberately EN-only scaffolding (like every template above) --
                    // has.qual's real "У [x-gen] [has.qual] ..." convention (see verbs.json's
                    // has.qual _comment and the Task 6.5 report) belongs in actual RU translation
                    // data under langs/ru/data/, not hardcoded here as a Cyrillic literal; this
                    // template instead uses an EN placeholder ("AT") in the same slot purely to
                    // confirm has.qual drops silently with correct space handling, same as [is].
                    "[us-subj] [is.aux] dismantle [us-obj].",
                    "[us-subj] [is.cop] ready.",
                    "[us-subj] [has.obj] a crowbar.",
                    "AT [us-gen] [has.qual] mediocre piloting skills."
                };
                foreach (var t in templates)
                {
                    try { prep.Invoke(null, new object[] { null, t }); }
                    catch (Exception ex) { lw.AppendLine("prep fail [" + t + "]: " + ex.Message); }
                }
                foreach (var t in templates)
                {
                    lw.AppendLine("TEMPLATE: " + t);
                    if (male != null) lw.AppendLine("  male: " + SafeInflect(t, male));
                    if (female != null) lw.AppendLine("  fem:  " + SafeInflect(t, female));
                    if (nb != null) lw.AppendLine("  nb:   " + SafeInflect(t, nb));
                    if (nonhuman != null) lw.AppendLine("  it:   " + SafeInflect(t, nonhuman));
                    try
                    {
                        var player = CrewSim.coPlayer;
                        if (player != null) lw.AppendLine("  you:  " + SafeInflect(t, player));
                    }
                    catch { }
                }
            }
            catch (Exception ex) { lw.AppendLine("LIVE ERROR: " + ex); }
            try { File.WriteAllText(outLive, lw.ToString()); } catch { }
            try { Log.LogInfo("[i18n] live self-test written: " + outLive); } catch { }
        }

        private static string SafeInflect(string t, CondOwner co)
        {
            try { return GrammarUtils.GetInflectedString(t, co); }
            catch (Exception ex) { return "ERROR: " + ex.Message; }
        }
    }

    // Applies every patch defensively: if a target method vanished in a game update,
    // that single patch is skipped (vanilla behavior kept) instead of crashing the game.
    internal static class PatchRunner
    {
        public static void ApplyAll(ref int ok, ref int failed)
        {
            var h = new Harmony(Plugin.GUID);
            var flagsPub = BindingFlags.Public | BindingFlags.Static;
            var flagsPriv = BindingFlags.NonPublic | BindingFlags.Static;
            var flagsInstPriv = BindingFlags.NonPublic | BindingFlags.Instance;
            var t = typeof(Patches);

            TryPatch(h, typeof(Localisation), "Get", flagsPub,
                null, t.GetMethod(nameof(Patches.LocalisationGetPostfix)), ref ok, ref failed);
            TryPatch(h, typeof(DataHandler), "UnpackTokens", flagsPriv,
                null, t.GetMethod(nameof(Patches.UnpackTokensPostfix)), ref ok, ref failed);
            TryPatch(h, typeof(GrammarUtils), "Verb", flagsPub,
                t.GetMethod(nameof(Patches.VerbPrefix)), null, ref ok, ref failed);
            TryPatch(h, typeof(GrammarUtils), "AttemptSubstitution", flagsPub,
                t.GetMethod(nameof(Patches.AttemptSubstitutionPrefix)), null, ref ok, ref failed);
            TryPatch(h, typeof(GrammarUtils), "AttemptProperName", flagsPub,
                t.GetMethod(nameof(Patches.AttemptProperNamePrefix)), null, ref ok, ref failed);
            TryPatch(h, typeof(TMPro.TMP_Settings), "get_instance", flagsPub,
                null, typeof(FontFallback).GetMethod(nameof(FontFallback.AfterSettingsInit)), ref ok, ref failed);
            TryPatch(h, typeof(DataHandler), "GetString", flagsPub,
                null, t.GetMethod(nameof(Patches.GetStringPostfix)), ref ok, ref failed);
            TryPatch(h, typeof(GUIDuties), "SetCrew", flagsInstPriv,
                null, t.GetMethod(nameof(Patches.DutiesSetCrewPostfix)), ref ok, ref failed);
            TryPatch(h, typeof(GUIChargenBody), "Awake", flagsInstPriv,
                null, t.GetMethod(nameof(Patches.ChargenBodyAwakePostfix)), ref ok, ref failed);
            var flagsInstPub = BindingFlags.Public | BindingFlags.Instance;
            TryPatch(h, typeof(GUIData), "Init", flagsInstPub,
                null, t.GetMethod(nameof(Patches.GUIDataInitPostfix)), ref ok, ref failed);
            TryPatch(h, typeof(Ostranauts.Objectives.Objective), "MakeTutorialObjective", flagsPub,
                null, t.GetMethod(nameof(Patches.MakeTutorialObjectivePostfix)), ref ok, ref failed);
            TryPatch(h, typeof(Ostranauts.Objectives.ObjectivePanel), "CompleteObjective", flagsInstPub,
                null, t.GetMethod(nameof(Patches.ObjectivePanelCompleteObjectivePostfix)), ref ok, ref failed);
            TryPatch(h, typeof(Ostranauts.ShipGUIs.ShipBroker.DerelictShipEntry), "SetData", flagsInstPub,
                null, t.GetMethod(nameof(Patches.DerelictShipEntrySetDataPostfix)), ref ok, ref failed,
                new Type[] { typeof(Ship), typeof(float) });
            TryPatch(h, typeof(Interaction), "ApplyEffects", flagsInstPub,
                null, t.GetMethod(nameof(Patches.InteractionApplyEffectsPostfix)), ref ok, ref failed,
                new Type[] { typeof(List<string>), typeof(bool) });
            TryPatch(h, typeof(Ledger), "RecordTransaction", flagsPub,
                t.GetMethod(nameof(Patches.LedgerRecordTransactionPrefix)), null, ref ok, ref failed,
                new Type[] { typeof(CondOwner), typeof(string), typeof(double), typeof(string), typeof(string), typeof(LedgerLI) });
            TryPatch(h, typeof(GUIPAXIntro), "Show", flagsInstPub,
                null, t.GetMethod(nameof(Patches.GUIPAXIntroShowPostfix)), ref ok, ref failed);
            TryPatch(h, typeof(GUIChargenCareer), "PageEvent", flagsInstPriv,
                null, t.GetMethod(nameof(Patches.GUIChargenCareerPageEventPostfix)), ref ok, ref failed,
                new Type[] { typeof(JsonLifeEvent) });
            TryPatch(h, typeof(GUIRosterRow), "SetOwner", flagsInstPub,
                null, t.GetMethod(nameof(Patches.GUIRosterRowSetOwnerPostfix)), ref ok, ref failed,
                new Type[] { typeof(string), typeof(JsonCompanyRules) });
            TryPatch(h, typeof(Ship), "LogGetHeader", flagsInstPub,
                null, t.GetMethod(nameof(Patches.ShipLogGetHeaderPostfix)), ref ok, ref failed);
            TryPatch(h, typeof(ShipStatus), "PrintStatus", flagsPub,
                null, t.GetMethod(nameof(Patches.ShipStatusPrintStatusPostfix)), ref ok, ref failed,
                new Type[] { typeof(CondOwner), typeof(string[]).MakeByRefType() });
            TryPatch(h, typeof(Ostranauts.ShipGUIs.MFD.MFDPage), "UpdateDisplay", flagsInstPriv,
                t.GetMethod(nameof(Patches.MFDUpdateDisplayPrefix)), null, ref ok, ref failed);
            TryPatch(h, typeof(GUITooltip2), "SetToolTip", flagsPub,
                t.GetMethod(nameof(Patches.TooltipSetToolTipPrefix)), null, ref ok, ref failed,
                new Type[] { typeof(string), typeof(string), typeof(bool), typeof(bool) });
            TryPatch(h, typeof(GUITooltip), "TooltipTextFormat4", flagsPriv | BindingFlags.Static,
                null, t.GetMethod(nameof(Patches.TooltipTextFormat4Postfix)), ref ok, ref failed,
                new Type[] { typeof(Interaction) });
            TryPatch(h, typeof(GUIPDA), "ShowJobPaintUI", flagsInstPriv,
                null, t.GetMethod(nameof(Patches.ShowJobPaintUIPostfix)), ref ok, ref failed);
            TryPatch(h, typeof(Ostranauts.Core.LogHandler), "LogMessage", flagsInstPub,
                t.GetMethod(nameof(Patches.LogMessagePrefix)), null, ref ok, ref failed,
                new Type[] { typeof(string) });
            TryPatch(h, typeof(Ostranauts.Objectives.ObjectivePanel), "SetData", flagsInstPub,
                null, t.GetMethod(nameof(Patches.ObjectivePanelSetDataPostfix)), ref ok, ref failed,
                new Type[] { typeof(Ostranauts.Objectives.Objective), typeof(bool) });
            TryPatch(h, typeof(Ostranauts.Objectives.ObjectivePanel), "RefreshText", flagsInstPriv,
                null, t.GetMethod(nameof(Patches.ObjectivePanelRefreshTextPostfix)), ref ok, ref failed);
            TryPatch(h, typeof(Ostranauts.Objectives.ObjectivePlotPanel), "SetData", flagsInstPub,
                null, t.GetMethod(nameof(Patches.ObjectivePlotPanelSetDataPostfix)), ref ok, ref failed,
                new Type[] { typeof(Ostranauts.Objectives.Objective), typeof(bool), typeof(bool) });
            TryPatch(h, typeof(Interaction), "FailReasons", flagsInstPub,
                null, t.GetMethod(nameof(Patches.FailReasonsPostfix)), ref ok, ref failed,
                new Type[] { typeof(bool), typeof(bool), typeof(bool) });
            TryPatch(h, typeof(GUIReactor), "Awake", flagsInstPriv,
                null, t.GetMethod(nameof(Patches.GUIReactorAwakePostfix)), ref ok, ref failed);
            TryPatch(h, typeof(GUISaveIndicator), "EstablishSave", flagsInstPub,
                null, t.GetMethod(nameof(Patches.SaveIndicatorEstablishSavePostfix)), ref ok, ref failed,
                new Type[] { typeof(bool) });
            TryPatch(h, typeof(GUISaveIndicator), "Reset", flagsInstPub,
                null, t.GetMethod(nameof(Patches.SaveIndicatorResetPostfix)), ref ok, ref failed);
            TryPatch(h, typeof(Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay), "PreSetup", flagsInstPub,
                t.GetMethod(nameof(Patches.GUIMessageDisplayPreSetupPrefix)), null, ref ok, ref failed);
            TryPatch(h, typeof(Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay), "AddMessage", flagsInstPub,
                t.GetMethod(nameof(Patches.GUIMessageDisplayAddMessagePrefix)), null, ref ok, ref failed,
                new Type[] { typeof(Ostranauts.Ships.Comms.ShipMessage) });
        }

        private static void TryPatch(Harmony h, Type type, string method, BindingFlags flags,
            MethodInfo prefix, MethodInfo postfix, ref int ok, ref int failed, Type[] paramTypes = null)
        {
            try
            {
                var target = paramTypes != null
                    ? type.GetMethod(method, flags, null, paramTypes, null)
                    : type.GetMethod(method, flags);
                if (target == null)
                {
                    failed++;
                    Plugin.Log.LogWarning("[i18n] MISSING " + type.Name + "." + method + " (game update?) - patch skipped, vanilla kept");
                    return;
                }
                h.Patch(target,
                    prefix == null ? null : new HarmonyMethod(prefix),
                    postfix == null ? null : new HarmonyMethod(postfix));
                ok++;
                Plugin.Log.LogInfo("[i18n] patched " + type.Name + "." + method);
            }
            catch (Exception ex)
            {
                failed++;
                Plugin.Log.LogError("[i18n] FAILED " + type.Name + "." + method + ": " + ex);
            }
        }

    }

    // Detects game updates by hashing Assembly-CSharp.dll; on change logs a warning
    // so the user knows patches were re-validated against a new build.
    internal static class VersionGuard
    {
        public static void CheckAndLog(ManualLogSource log)
        {
            try
            {
                var dll = typeof(DataHandler).Assembly.Location;
                string hash;
                using (var sha = System.Security.Cryptography.SHA1.Create())
                    hash = Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(dll)));
                var stamp = Path.Combine(Paths.PluginPath, "OstraI18n", "last_hash.txt");
                var prev = File.Exists(stamp) ? File.ReadAllText(stamp).Trim() : "";
                if (prev != hash)
                {
                    log.LogWarning("[i18n] Assembly-CSharp.dll changed (update or first run). Hash=" + hash.Substring(0, 12) + " - patches re-validated this launch");
                    Directory.CreateDirectory(Path.GetDirectoryName(stamp));
                    File.WriteAllText(stamp, hash);
                }
                else log.LogInfo("[i18n] game binary unchanged since last run");
            }
            catch (Exception ex) { log.LogWarning("[i18n] version check failed: " + ex.Message); }
        }
    }
}
