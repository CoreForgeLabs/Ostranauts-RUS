using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OstraI18n
{
    // Перенаправление путей движка на локализованные ассеты страниц мануалов
    // и надёжное открытие руководств (GUIManual) в игре.
    internal static class ManualAssets
    {
        private static string _localizedManualsFolder;

        public static void Init(string pluginDir, string langCode, Harmony harmony)
        {
            var target = AccessTools.Method(typeof(DataHandler), "AllPostLoadAsync");
            if (target != null)
            {
                harmony.Patch(target, prefix: new HarmonyMethod(
                    typeof(ManualAssets).GetMethod(nameof(RegisterPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
            }

            var openUrlTarget = AccessTools.Method(typeof(Application), nameof(Application.OpenURL), new[] { typeof(string) });
            if (openUrlTarget != null)
            {
                harmony.Patch(openUrlTarget, prefix: new HarmonyMethod(
                    typeof(ManualAssets).GetMethod(nameof(OpenUrlPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
            }

            var manualInitTarget = AccessTools.Method(typeof(GUIManual), "Init");
            if (manualInitTarget != null)
            {
                harmony.Patch(manualInitTarget, postfix: new HarmonyMethod(
                    typeof(ManualAssets).GetMethod(nameof(GUIManualInitPostfix), BindingFlags.NonPublic | BindingFlags.Static)));
            }

            var manualSetPageStrTarget = AccessTools.Method(typeof(GUIManual), "SetPage", new[] { typeof(string) });
            if (manualSetPageStrTarget != null)
            {
                harmony.Patch(manualSetPageStrTarget, prefix: new HarmonyMethod(
                    typeof(ManualAssets).GetMethod(nameof(GUIManualSetPageStrPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
            }

            var manualSetPageIntTarget = AccessTools.Method(typeof(GUIManual), "SetPage", new[] { typeof(int) });
            if (manualSetPageIntTarget != null)
            {
                harmony.Patch(manualSetPageIntTarget, postfix: new HarmonyMethod(
                    typeof(ManualAssets).GetMethod(nameof(GUIManualSetPageIntPostfix), BindingFlags.NonPublic | BindingFlags.Static)));
            }
        }

        private static void OpenUrlPrefix(ref string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) return;
                var basePath = Application.streamingAssetsPath + "/images/manuals";
                if (!url.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) return;

                if (_localizedManualsFolder == null) return;
                url = _localizedManualsFolder + url.Substring(basePath.Length);
                Plugin.Log.LogInfo("[i18n] manual-assets: OpenURL перенаправлен на " + url);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] manual-assets: ошибка перенаправления OpenURL: " + ex);
            }
        }

        private static void RegisterPrefix()
        {
            try
            {
                var langRoot = Path.Combine(Path.Combine(Plugin.DataDir.Value, "langs"), LangPack.Code);
                var imagesDir = Path.Combine(langRoot, "images");
                if (!Directory.Exists(imagesDir)) return;

                var manualsDir = Path.Combine(imagesDir, "manuals");
                if (Directory.Exists(manualsDir))
                {
                    _localizedManualsFolder = manualsDir;
                }

                if (DataHandler.aModPaths == null) return;

                var modRoot = langRoot + Path.DirectorySeparatorChar;
                if (!DataHandler.aModPaths.Contains(modRoot))
                {
                    DataHandler.aModPaths.Insert(0, modRoot);
                    Plugin.Log.LogInfo("[i18n] manual-assets: " + modRoot + " зарегистрирован в aModPaths (приоритет над оригиналом)");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] manual-assets: ошибка регистрации пути: " + ex);
            }
        }

        // Локализация вкладок в интерфейсе книги руководств
        private static void GUIManualInitPostfix(GUIManual __instance)
        {
            if (!LangPack.Active || (UnityEngine.Object)(object)__instance == (UnityEngine.Object)null) return;
            try
            {
                LocalizeTabDictionary(__instance, "dictTabsLeft");
                LocalizeTabDictionary(__instance, "dictTabsRight");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] manual-assets: ошибка локализации вкладок GUIManual: " + ex.Message);
            }
        }

        private static void LocalizeTabDictionary(GUIManual manual, string fieldName)
        {
            var dictObj = AccessTools.Field(typeof(GUIManual), fieldName)?.GetValue(manual);
            if (dictObj is Dictionary<string, GameObject> dict)
            {
                foreach (var kvp in dict)
                {
                    if (kvp.Value == null) continue;
                    var key = "MANUAL_TAB_" + kvp.Key.Replace(" ", "_").Replace("'", "");
                    var tr = I18n.Get(key);
                    if (string.IsNullOrEmpty(tr) || tr == key) continue;

                    var tmp = kvp.Value.GetComponentInChildren<TMP_Text>(true);
                    if (tmp != null)
                    {
                        tmp.text = tr;
                        continue;
                    }
                    var txt = kvp.Value.GetComponentInChildren<Text>(true);
                    if (txt != null)
                    {
                        txt.text = tr;
                    }
                }
            }
        }

        // Преобразование любого русского названия в канонический ключ книги
        private static void GUIManualSetPageStrPrefix(ref string strName)
        {
            if (string.IsNullOrEmpty(strName) || !LangPack.Active) return;
            try
            {
                if (strName.IndexOf("nav", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    strName.IndexOf("навигац", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    strName.IndexOf("поларис", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    strName.IndexOf("полёт", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    strName.IndexOf("полет", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    strName.IndexOf("polaris", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    strName = "Nav Console";
                }
                else if (strName.IndexOf("fusion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         strName.IndexOf("термояд", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         strName.IndexOf("реактор", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    strName = "Fusion";
                }
                else if (strName.IndexOf("env", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         strName.IndexOf("жизнеобеспеч", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         strName.IndexOf("экосистем", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    strName = "Environmental";
                }
                else if (strName.IndexOf("hull", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         strName.IndexOf("холден", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         strName.IndexOf("корпус", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    strName = "Hull Patch";
                }
                else if (strName.IndexOf("safe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         strName.IndexOf("огисо", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         strName.IndexOf("безопасн", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    strName = "Work Safety";
                }
                else if (strName.IndexOf("basic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         strName.IndexOf("основ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         strName.IndexOf("управлен", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    strName = "Basics";
                }
            }
            catch { }
        }

        // Обновление текстур страниц при листании книги
        private static void GUIManualSetPageIntPostfix(GUIManual __instance)
        {
            if (!LangPack.Active || (UnityEngine.Object)(object)__instance == (UnityEngine.Object)null) return;
            try
            {
                var pages = AccessTools.Field(typeof(GUIManual), "aPages")?.GetValue(__instance) as IList;
                var indexObj = AccessTools.Field(typeof(GUIManual), "nIndex")?.GetValue(__instance);
                if (pages == null || !(indexObj is int)) return;

                int index = (int)indexObj;
                if (index >= 0 && index < pages.Count)
                    UpdateRawImageTexture(__instance, "bmpPageL", pages[index] as string);
                if (index + 1 >= 0 && index + 1 < pages.Count)
                    UpdateRawImageTexture(__instance, "bmpPageR", pages[index + 1] as string);
            }
            catch { }
        }

        private static void UpdateRawImageTexture(GUIManual manual, string fieldName, string pageKey)
        {
            if (string.IsNullOrEmpty(pageKey)) return;
            var rawImage = AccessTools.Field(typeof(GUIManual), fieldName)?.GetValue(manual) as RawImage;
            if (rawImage == null) return;

            Texture2D texture = DataHandler.LoadPNG(pageKey, false);
            if (texture != null)
            {
                rawImage.texture = texture;
            }
        }
    }
}
