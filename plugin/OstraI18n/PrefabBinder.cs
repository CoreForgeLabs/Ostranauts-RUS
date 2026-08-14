using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using OstraI18n.Core;

namespace OstraI18n
{
    internal static class PrefabBinder
    {
        private class Entry { public string[] Path; public string Key; }

        private static readonly List<Entry> SceneEntries = new List<Entry>();
        private static readonly List<Entry> AssetEntries = new List<Entry>();

        public static int LoadCatalog(string pluginDir)
        {
            var path = Path.Combine(pluginDir, "catalog", "prefabs.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("[i18n] каталог префабов не найден: " + path);
                return 0;
            }
            int n = 0;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("approved", out var ap) || !ap.GetBoolean()) continue;
                var kind = e.GetProperty("kind").GetString();
                var root = e.GetProperty("root").GetString();
                var key = e.GetProperty("key").GetString();
                var segs = new List<string> { root };
                foreach (var p in e.GetProperty("path").EnumerateArray()) segs.Add(p.GetString());

                var entry = new Entry { Path = segs.ToArray(), Key = key };
                if (kind == "scene") SceneEntries.Add(entry); else AssetEntries.Add(entry);
                n++;
            }
            Plugin.Log.LogInfo("[i18n] каталог префабов: " + n + " записей ("
                               + SceneEntries.Count + " scene, " + AssetEntries.Count + " asset)");
            return n;
        }

        public static void BindScenes()
        {
            SceneManager.sceneLoaded += (scene, mode) => TryBindAll(scene);
            // При Awake() плагина, помимо активной сцены, могут быть уже загружены
            // (аддитивно) другие сцены, чьё событие sceneLoaded успело сработать ДО
            // подписки выше — например "LoadingScreen", транзитная сцена, показанная
            // очень рано в загрузке (см. docs/baseline.md). GetActiveScene() ловит
            // только одну сцену; перебор всех загруженных ловит и такие случаи.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) TryBindAll(scene);
            }
        }

        private static void TryBindAll(Scene scene)
        {
            int bound = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var entry in SceneEntries)
                {
                    if (entry.Path[0] != root.name) continue;
                    var t = root.transform;
                    bool ok = true;
                    for (int i = 1; i < entry.Path.Length; i++)
                    {
                        t = t.Find(entry.Path[i]);
                        if (t == null) { ok = false; break; }
                    }
                    if (!ok || t.GetComponent<LocalizedText>() != null) continue;
                    var lt = t.gameObject.AddComponent<LocalizedText>();
                    lt.Key = entry.Key;
                    bound++;
                }
            }
            if (bound > 0) Plugin.Log.LogInfo("[i18n] scene-привязка: " + bound + " объектов в сцене " + scene.name);
        }

        public static void ApplyAssetHook(Harmony harmony)
        {
            if (AssetEntries.Count == 0) return;
            var target = AccessTools.Method(typeof(UnityEngine.UI.MaskableGraphic), "OnEnable", Type.EmptyTypes);
            if (target == null) return;
            harmony.Patch(target, postfix: new HarmonyMethod(
                typeof(PrefabBinder).GetMethod(nameof(OnEnablePostfix), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void OnEnablePostfix(UnityEngine.UI.MaskableGraphic __instance)
        {
            try
            {
                if (!(__instance is TMP_Text) && !(__instance is UnityEngine.UI.Text)) return;
                
                if (LangPack.Active && string.Equals(LangPack.Code, "ru", StringComparison.OrdinalIgnoreCase))
                {
                    if (__instance is TMP_Text tmp)
                    {
                        if (tmp.text == "Wear:") tmp.text = "Износ:";
                    }
                    else if (__instance is UnityEngine.UI.Text txt)
                    {
                        if (txt.text == "Wear:") txt.text = "Износ:";
                    }
                }

                if (__instance.GetComponent<LocalizedText>() != null) return;

                foreach (var entry in AssetEntries)
                {
                    var path = BuildPath(__instance.transform, entry.Path.Length);
                    if (path == null) continue;
                    if (!PathKey.Matches(path, entry.Path)) continue;

                    var lt = __instance.gameObject.AddComponent<LocalizedText>();
                    lt.Key = entry.Key;
                    lt.Apply();
                    return;
                }
            }
            catch (Exception ex) { Plugin.Log.LogError("[i18n] asset hook failed: " + ex); }
        }

        private static string[] BuildPath(Transform leaf, int maxLen)
        {
            var stack = new List<string>();
            var t = leaf;
            for (int i = 0; i < maxLen && t != null; i++) { stack.Add(t.name); t = t.parent; }
            if (stack.Count < maxLen) return null;
            stack.Reverse();
            return stack.ToArray();
        }
    }
}
