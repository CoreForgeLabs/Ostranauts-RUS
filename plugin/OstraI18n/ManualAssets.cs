using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OstraI18n
{
    // Перенаправление путей движка на локализованные ассеты страниц мануалов
    // (StreamingAssets/images/manuals/<Папка>/NNN.png) — без патчинга кода
    // отрисовки (GUIManual.cs/GUIMultiSheet.cs). Игра уже умеет подменять
    // картинки через штатный мод-механизм: DataHandler.LoadPNG перебирает
    // DataHandler.aModPaths по порядку и берёт первый существующий файл
    // (decompiled/DataHandler.cs:1572-1616), а страница попадает в
    // aModPaths[0] тем же способом, каким туда попадает базовая игра и
    // обычные моды (LoadMod -> aModPaths.Insert(0, ...), decompiled/
    // DataHandler.cs:892-913). Регистрируя нашу языковую папку первой,
    // получаем постраничное переопределение: переведённый файл подставляется,
    // непереведённый (файла нет в нашей папке) молча берётся из оригинала —
    // не нужно переводить весь мануал разом, можно постранично.
    //
    // Список страниц (сколько их и как называются) движок всё равно берёт
    // из БАЗОВОЙ игровой папки (GUIManual.cs:163 сканирует DataHandler.
    // strAssetPath напрямую, не aModPaths) — поэтому набор/порядок страниц
    // менять нельзя, только содержимое конкретных файлов по тем же именам.
    internal static class ManualAssets
    {
        private static string _localizedManualsFolder; // абсолютный путь к langs/<code>/images/manuals, или null

        public static void Init(string pluginDir, string langCode, Harmony harmony)
        {
            var target = AccessTools.Method(typeof(DataHandler), "AllPostLoadAsync");
            if (target == null)
            {
                Plugin.Log.LogWarning("[i18n] manual-assets: DataHandler.AllPostLoadAsync не найден, перенаправление путей пропущено");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(ManualAssets).GetMethod(nameof(RegisterPrefix), BindingFlags.NonPublic | BindingFlags.Static)));

            // Application.OpenURL("<streamingAssetsPath>/images/manuals/") — кнопка
            // "открыть папку с мануалами" (CrewSim.cs, GUIOptions.cs), открывает PDF
            // и PNG во внешнем проводнике. PDF движком не рендерится и не проходит
            // через aModPaths (в отличие от PNG-страниц выше) — единственный способ
            // подсунуть переведённую версию PDF/картинок пользователю "как есть" —
            // перенаправить саму ссылку на нашу языковую папку, если она существует.
            var openUrlTarget = AccessTools.Method(typeof(Application), nameof(Application.OpenURL), new[] { typeof(string) });
            if (openUrlTarget == null)
            {
                Plugin.Log.LogWarning("[i18n] manual-assets: Application.OpenURL не найден, перенаправление ссылки на папку пропущено");
                return;
            }
            harmony.Patch(openUrlTarget, prefix: new HarmonyMethod(
                typeof(ManualAssets).GetMethod(nameof(OpenUrlPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void OpenUrlPrefix(ref string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) return;
                var basePath = Application.streamingAssetsPath + "/images/manuals";
                if (!url.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) return;

                if (_localizedManualsFolder == null) return; // not resolved yet or no localized copy on disk
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
                if (!Directory.Exists(imagesDir))
                {
                    Plugin.Log.LogInfo("[i18n] manual-assets: " + imagesDir + " не найдена, перенаправление путей пропущено");
                    return;
                }

                var manualsDir = Path.Combine(imagesDir, "manuals");
                if (Directory.Exists(manualsDir))
                {
                    _localizedManualsFolder = manualsDir;
                }

                if (DataHandler.aModPaths == null)
                {
                    Plugin.Log.LogWarning("[i18n] manual-assets: DataHandler.aModPaths ещё не инициализирован, перенаправление путей пропущено");
                    return;
                }

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
    }
}
