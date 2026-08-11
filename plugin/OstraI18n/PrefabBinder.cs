using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OstraI18n
{
    // Находит объекты сцены по абсолютному пути из каталога и вешает LocalizedText.
    // Task 2 — вертикальный срез на одной scene-записи.
    // Task 4 — вертикальный срез на одной asset-записи (динамически инстанцируемый префаб,
    // не являющийся root-объектом сцены — например, всплывающее меню сохранения).
    internal static class PrefabBinder
    {
        private const string SliceRoot = "Canvas Stack";
        private static readonly string[] SlicePath = { "Canvas GUI", "GUIZones", "Scrollview", "TitleContainer", "DescriptionLabel" };
        private const string SliceKey = "GUI_SLICE_TEST";
        private const string SliceReplacement = "ПРОВЕРКА_ПРЕФАБА";

        public static void BindSceneSlice()
        {
            SceneManager.sceneLoaded += (scene, mode) => TryBindSlice(scene);
            var active = SceneManager.GetActiveScene();
            if (active.IsValid()) TryBindSlice(active);
        }

        private static void TryBindSlice(Scene scene)
        {
            try
            {
                var roots = scene.GetRootGameObjects();
                var root = roots.FirstOrDefault(r => r.name == SliceRoot);
                if (root == null)
                {
                    Plugin.Log.LogInfo("[i18n] slice: сцена '" + scene.name + "' не содержит root '" + SliceRoot +
                        "' (roots: " + string.Join(", ", roots.Select(r => r.name)) + ")");
                    return;
                }

                var t = root.transform;
                foreach (var seg in SlicePath)
                {
                    t = t.Find(seg);
                    if (t == null)
                    {
                        Plugin.Log.LogWarning("[i18n] slice: путь не разрешился на '" + seg + "'");
                        return;
                    }
                }

                var lt = t.gameObject.AddComponent<LocalizedText>();
                lt.Key = SliceKey;
                Plugin.Log.LogInfo("[i18n] slice: привязан " + SliceRoot + "/" + string.Join("/", SlicePath));
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("[i18n] slice bind failed: " + ex);
            }
        }

        // Task 4 — asset-объект: экземпляр появляется где-то в иерархии сцены
        // (не как root, с суффиксом "(Clone)") только когда игрок открывает
        // соответствующее меню. Поллинг ограничен по времени — это тестовый
        // срез, не производственный механизм привязки всех asset-записей.
        private const string AssetSliceRoot = "GUISaveMenu";
        private static readonly string[] AssetSlicePath = { "txtTitle" };
        private const string AssetSliceKey = "GUI_ASSET_SLICE_TEST";
        private const float AssetPollTimeoutSeconds = 300f;
        private const float AssetPollIntervalSeconds = 0.5f;

        public static void BindAssetSlice()
        {
            if (Plugin.Instance == null)
            {
                Plugin.Log.LogWarning("[i18n] asset-slice: Plugin.Instance == null, поллинг не запущен");
                return;
            }
            Plugin.Instance.StartCoroutine(PollForAssetRoot());
        }

        private static IEnumerator PollForAssetRoot()
        {
            float elapsed = 0f;
            while (elapsed < AssetPollTimeoutSeconds)
            {
                Transform found = null;
                try { found = FindTransformAnywhere(AssetSliceRoot); }
                catch (System.Exception ex) { Plugin.Log.LogError("[i18n] asset-slice poll failed: " + ex); yield break; }

                if (found != null)
                {
                    TryBindAssetPath(found);
                    yield break;
                }
                yield return new WaitForSeconds(AssetPollIntervalSeconds);
                elapsed += AssetPollIntervalSeconds;
            }
            Plugin.Log.LogWarning("[i18n] asset-slice: '" + AssetSliceRoot + "' не появился за " +
                AssetPollTimeoutSeconds + "с — тест отменён");
        }

        private static Transform FindTransformAnywhere(string name)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == name || t.name == name + "(Clone)") return t;
                    }
                }
            }
            return null;
        }

        private static void TryBindAssetPath(Transform root)
        {
            try
            {
                var t = root;
                foreach (var seg in AssetSlicePath)
                {
                    t = t.Find(seg);
                    if (t == null)
                    {
                        Plugin.Log.LogWarning("[i18n] asset-slice: путь не разрешился на '" + seg + "'");
                        return;
                    }
                }

                var lt = t.gameObject.AddComponent<LocalizedText>();
                lt.Key = AssetSliceKey;
                Plugin.Log.LogInfo("[i18n] asset-slice: привязан " + AssetSliceRoot + "/" + string.Join("/", AssetSlicePath));
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("[i18n] asset-slice bind failed: " + ex);
            }
        }
    }
}
