using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace OstraI18n
{
    // Оверлей переводимых полей игровых данных поверх уже загруженных словарей
    // DataHandler.dict* — без патчинга загрузки, без копирования файлов игры.
    // Точка привязки: DataHandler.LoadComplete (главный поток, все словари уже
    // заполнены и слиты со всеми модами — см. план Фазы 3, Global Constraints).
    internal static class ContentOverlay
    {
        // категория (папка, которую игра грузит в один словарь через LoadModJsons)
        // -> имя публичного статического поля DataHandler
        private static readonly Dictionary<string, string> CategoryToField = new Dictionary<string, string>
        {
            { "interactions", "dictInteractions" },
        };

        private static readonly HashSet<string> TranslatableFields = new HashSet<string>
        {
            "strTitle", "strDesc", "strTooltip", "strNameFriendly", "strNameShort", "strFriendlyName",
        };

        public static int Applied;
        public static int Orphans;

        public static void Init(string pluginDir, string langCode)
        {
            DataHandler.LoadComplete += () =>
            {
                try { Apply(pluginDir, langCode); }
                catch (Exception ex) { Plugin.Log.LogError("[i18n] контент-оверлей упал: " + ex); }
            };
        }

        private static void Apply(string pluginDir, string langCode)
        {
            var dataDir = Path.Combine(pluginDir, "langs", langCode, "data");
            if (!Directory.Exists(dataDir))
            {
                Plugin.Log.LogInfo("[i18n] контент-оверлей: папка " + dataDir + " не найдена, пропуск");
                return;
            }

            foreach (var kv in CategoryToField)
            {
                var jsonPath = Path.Combine(dataDir, kv.Key + ".json");
                if (!File.Exists(jsonPath)) continue;
                ApplyCategory(kv.Key, kv.Value, jsonPath);
            }

            Plugin.Log.LogInfo("[i18n] контент-оверлей: применено полей " + Applied + ", сирот " + Orphans);

            if (DataHandler.dictInteractions.TryGetValue("ACTAddConnection", out var testEntry))
            {
                Plugin.Log.LogInfo("[i18n] контент-оверлей self-test: ACTAddConnection.strTitle = '" + testEntry.strTitle + "'");
            }
        }

        private static void ApplyCategory(string category, string fieldName, string jsonPath)
        {
            var field = typeof(DataHandler).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            if (field == null)
            {
                Plugin.Log.LogWarning("[i18n] контент-оверлей: DataHandler." + fieldName + " не найдено (категория " + category + ")");
                return;
            }
            var dictObj = field.GetValue(null) as IDictionary;
            if (dictObj == null)
            {
                Plugin.Log.LogWarning("[i18n] контент-оверлей: DataHandler." + fieldName + " не является словарём или null");
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                var strName = entry.Name;
                if (!dictObj.Contains(strName))
                {
                    Orphans++;
                    continue;
                }
                var target = dictObj[strName];
                var targetType = target.GetType();
                foreach (var fieldEntry in entry.Value.EnumerateObject())
                {
                    if (!TranslatableFields.Contains(fieldEntry.Name)) continue;
                    var prop = targetType.GetProperty(fieldEntry.Name);
                    if (prop == null || prop.PropertyType != typeof(string) || !prop.CanWrite) continue;
                    prop.SetValue(target, fieldEntry.Value.GetString());
                    Applied++;
                }
            }
        }
    }
}
