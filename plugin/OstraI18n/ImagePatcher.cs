using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OstraI18n
{
    // Перехват и замена текстур/кнопок на локализованные версии из langs/<lang>/images/
    internal static class ImagePatcher
    {
        private static string _localizedImagesFolder;
        private static string _langRoot;
        private static string _activeLangCode;

        public static void Init(string pluginDir, string langCode, Harmony harmony)
        {
            _activeLangCode = langCode;
            _langRoot = Path.Combine(Path.Combine(pluginDir, "langs"), langCode);
            var imagesDir = Path.Combine(_langRoot, "images");
            if (Directory.Exists(imagesDir))
            {
                _localizedImagesFolder = imagesDir;
                Plugin.Log.LogInfo("[i18n] image patcher initialized with folder: " + _localizedImagesFolder);
            }
            else
            {
                Plugin.Log.LogWarning("[i18n] image patcher: folder not found: " + imagesDir);
                return;
            }

            // 1. Патч DataHandler.LoadPNG -> подменяет любые PNG из images/
            var targetLoadPNG = AccessTools.Method(typeof(DataHandler), "LoadPNG", new[] { typeof(string), typeof(bool), typeof(bool) });
            if (targetLoadPNG != null)
            {
                harmony.Patch(targetLoadPNG, prefix: new HarmonyMethod(AccessTools.Method(typeof(ImagePatcher), nameof(LoadPNGPrefix))));
                Plugin.Log.LogInfo("[i18n] patched DataHandler.LoadPNG for localized image override");
            }
            else
            {
                Plugin.Log.LogWarning("[i18n] DataHandler.LoadPNG not found for patching");
            }

            // 2. Патч DataHandler.Init -> немедленно добавляет langRoot в aModPaths[0]
            var targetDHInit = AccessTools.Method(typeof(DataHandler), "Init");
            if (targetDHInit != null)
            {
                harmony.Patch(targetDHInit, postfix: new HarmonyMethod(AccessTools.Method(typeof(ImagePatcher), nameof(DataHandlerInitPostfix))));
                Plugin.Log.LogInfo("[i18n] patched DataHandler.Init for early aModPaths registration");
            }

            // 3. Патч MainMenu.Init -> перезагружает текстуры кнопок меню
            var mmInit = AccessTools.Method(typeof(MainMenu), "Init");
            if (mmInit != null)
            {
                harmony.Patch(mmInit, postfix: new HarmonyMethod(AccessTools.Method(typeof(ImagePatcher), nameof(MainMenuInitPostfix))));
                Plugin.Log.LogInfo("[i18n] patched MainMenu.Init for button texture update");
            }
        }

        /// <summary>
        /// Called at runtime when the user switches languages via astronaut click.
        /// Updates the active images folder and aModPaths registration.
        /// </summary>
        public static void SetActiveLanguage(string pluginDir, string langCode)
        {
            _activeLangCode = langCode;
            _langRoot = Path.Combine(Path.Combine(pluginDir, "langs"), langCode);
            var imagesDir = Path.Combine(_langRoot, "images");
            if (Directory.Exists(imagesDir))
            {
                _localizedImagesFolder = imagesDir;
            }
            else
            {
                Plugin.Log.LogInfo("[i18n] SetActiveLanguage: " + imagesDir + " не найдена, перенаправление путей пропущено");
                _localizedImagesFolder = null;
            }

            // Update aModPaths to point to the new language root
            try
            {
                if (DataHandler.aModPaths != null)
                {
                    // Remove any old OstraI18n lang entries
                    var langsBase = Path.Combine(pluginDir, "langs");
                    DataHandler.aModPaths.RemoveAll(p => p.StartsWith(langsBase, StringComparison.OrdinalIgnoreCase));

                    // Insert new language root
                    var modRoot = _langRoot + Path.DirectorySeparatorChar;
                    DataHandler.aModPaths.Insert(0, modRoot);
                    Plugin.Log.LogInfo("[i18n] SetActiveLanguage: aModPaths updated to " + modRoot);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] SetActiveLanguage aModPaths error: " + ex.Message);
            }
        }

        private static void DataHandlerInitPostfix()
        {
            try
            {
                if (DataHandler.aModPaths != null && !string.IsNullOrEmpty(_langRoot))
                {
                    var modRoot = _langRoot + Path.DirectorySeparatorChar;
                    if (!DataHandler.aModPaths.Contains(modRoot))
                    {
                        DataHandler.aModPaths.Insert(0, modRoot);
                        Plugin.Log.LogInfo("[i18n] early registration: " + modRoot + " inserted at DataHandler.aModPaths[0]");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] DataHandlerInitPostfix error: " + ex.Message);
            }
        }

        private static bool LoadPNGPrefix(string strFileName, bool bNorm, bool alwaysLoadFreshInstance, ref Texture2D __result)
        {
            try
            {
                // Only override when we have a localized images folder AND the language is non-English
                if (!LangPack.Active || string.IsNullOrEmpty(_localizedImagesFolder) || string.IsNullOrEmpty(strFileName))
                    return true;

                string localPath = Path.Combine(_localizedImagesFolder, strFileName);
                if (!File.Exists(localPath))
                {
                    // Case-insensitive search (e.g., GUIbtnNew.png -> GUIBtnNew.png)
                    var dir = Path.GetDirectoryName(localPath);
                    var fname = Path.GetFileName(localPath);
                    if (Directory.Exists(dir))
                    {
                        foreach (var f in Directory.GetFiles(dir))
                        {
                            if (string.Equals(Path.GetFileName(f), fname, StringComparison.OrdinalIgnoreCase))
                            {
                                localPath = f;
                                break;
                            }
                        }
                    }
                }

                if (File.Exists(localPath))
                {
                    if (!alwaysLoadFreshInstance && DataHandler.dictImages != null && DataHandler.dictImages.TryGetValue(strFileName, out var cached) && cached != null && cached.name == strFileName)
                    {
                        __result = cached;
                        return false;
                    }

                    byte[] array = File.ReadAllBytes(localPath);
                    var value = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    value.filterMode = FilterMode.Point;
                    value.wrapMode = TextureWrapMode.Clamp;
                    ImageConversion.LoadImage(value, array);
                    if (bNorm)
                    {
                        value = ShaderSetup.NormalPNGtoDXTnm(value);
                    }
                    if (DataHandler.dictImages != null)
                    {
                        DataHandler.dictImages[strFileName] = value;
                    }
                    value.name = strFileName;
                    __result = value;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] LoadPNGPrefix exception for " + strFileName + ": " + ex.Message);
            }
            return true;
        }

        public static void MainMenuInitPostfix(MainMenu __instance)
        {
            if (__instance == null) return;
            try
            {
                if (LangPack.Active && !string.IsNullOrEmpty(_localizedImagesFolder))
                {
                    ReloadButtonsLive(__instance, _activeLangCode ?? LangPack.Code);
                }

                // Attach the language selection astronaut
                MenuLanguageAstronaut.AttachToMainMenu(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] MainMenuInitPostfix failed: " + ex.Message);
            }
        }

        public static void ReloadButtonsLive(MainMenu mm, string langCode)
        {
            if (mm == null) return;
            try
            {
                // langCode is now always the ISO code (e.g. "ru", "en", "de")
                var imagesDir = Path.Combine(Path.Combine(Plugin.DataDir.Value, "langs"), langCode, "images");

                if (!Directory.Exists(imagesDir))
                {
                    Plugin.Log.LogWarning("[i18n] ReloadButtonsLive: images dir not found: " + imagesDir);
                    return;
                }

                var dictTexField = AccessTools.Field(typeof(MainMenu), "dictTextures");
                var btnOutAllMethod = AccessTools.Method(typeof(MainMenu), "BtnOutAll");

                if (dictTexField != null && btnOutAllMethod != null)
                {
                    var dictTextures = dictTexField.GetValue(mm) as Dictionary<string, Texture2D[]>;
                    if (dictTextures != null)
                    {
                        Func<string, Texture2D> loadTex = name => LoadTextureDirect(imagesDir, name);

                        dictTextures["btnContinue"] = new Texture2D[] { loadTex("GUIBtnContinue.png"), loadTex("GUIBtnContinueIn.png") };
                        dictTextures["btnNew"] = new Texture2D[] { loadTex("GUIbtnNew.png"), loadTex("GUIbtnNewIn.png") };
                        dictTextures["btnOptions"] = new Texture2D[] { loadTex("GUIbtnOptions.png"), loadTex("GUIbtnOptionsIn.png") };
                        dictTextures["btnBBG"] = new Texture2D[] { loadTex("GUIBtnBBG.png"), loadTex("GUIBtnBBGIn.png") };
                        dictTextures["btnCredits"] = new Texture2D[] { loadTex("GUIBtnCredits.png"), loadTex("GUIBtnCreditsIn.png") };
                        dictTextures["btnWiki"] = new Texture2D[] { loadTex("GUIBtnWiki.png"), loadTex("GUIBtnWikiIn.png") };
                        dictTextures["btnSteam"] = new Texture2D[] { loadTex("GUIBtnSteam.png"), loadTex("GUIBtnSteamIn.png") };
                        dictTextures["btnDiscord"] = new Texture2D[] { loadTex("GUIBtnDiscord.png"), loadTex("GUIBtnDiscordIn.png") };
                        dictTextures["btnMods"] = new Texture2D[] { loadTex("GUIBtnMods.png"), loadTex("GUIBtnModsIn.png") };

                        btnOutAllMethod.Invoke(mm, null);
                        Plugin.Log.LogInfo("[i18n] MainMenu buttons reloaded from " + imagesDir + " for lang=" + langCode);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] ReloadButtonsLive failed: " + ex.Message);
            }
        }

        private static Texture2D LoadTextureDirect(string folder, string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileName)) return null;
                string localPath = Path.Combine(folder, fileName);
                if (!File.Exists(localPath))
                {
                    var dir = Path.GetDirectoryName(localPath);
                    var fname = Path.GetFileName(localPath);
                    if (Directory.Exists(dir))
                    {
                        foreach (var f in Directory.GetFiles(dir))
                        {
                            if (string.Equals(Path.GetFileName(f), fname, StringComparison.OrdinalIgnoreCase))
                            {
                                localPath = f;
                                break;
                            }
                        }
                    }
                }

                if (File.Exists(localPath))
                {
                    byte[] array = File.ReadAllBytes(localPath);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    ImageConversion.LoadImage(tex, array);
                    tex.name = fileName;
                    if (DataHandler.dictImages != null)
                    {
                        DataHandler.dictImages[fileName] = tex;
                    }
                    return tex;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] LoadTextureDirect failed for " + fileName + ": " + ex.Message);
            }
            return null;
        }
    }
}
