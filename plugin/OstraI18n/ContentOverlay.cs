using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;

namespace OstraI18n
{
    // Оверлей переводимых полей игровых данных поверх уже загруженных словарей
    // DataHandler.dict* — без патчинга загрузки, без копирования файлов игры.
    //
    // Точка привязки: Harmony-префикс перед DataHandler.AllPostLoadAsync (НЕ
    // DataHandler.LoadComplete, как было раньше). Причина: AllPostLoadAsync
    // вызывает PrepareConditionDescriptions()/PrepareInteractionInflections(),
    // которые строят словарь GrammarUtils.inflectedStrings, ИНДЕКСИРУЯ ЕГО ПО
    // ТЕКУЩЕМУ ЗНАЧЕНИЮ strDesc/strTooltip НА МОМЕНТ ВЫЗОВА. LoadComplete
    // срабатывает ПОЗЖЕ (после AllPostLoadAsync) — если оверлей применяется там,
    // строка уже переведена, а inflectedStrings проиндексирован по ОРИГИНАЛЬНОМУ
    // английскому тексту; GrammarUtils.GetInflectedString ищет по точному
    // совпадению строки, не находит переведённую и возвращает её как есть — токены
    // [us]/[them] остаются сырыми, не подставленными (баг найден вживую в этой
    // сессии, см. docs/baseline.md). Патчинг ПЕРЕД AllPostLoadAsync гарантирует,
    // что подготовка словоформ построится уже по переведённому тексту.
    internal static class ContentOverlay
    {
        // категория (папка, которую игра грузит в один словарь через LoadModJsons)
        // -> имя публичного статического поля DataHandler
        private static readonly Dictionary<string, string> CategoryToField = new Dictionary<string, string>
        {
            { "interactions", "dictInteractions" },
            { "careers", "dictCareers" },
            { "conditions", "dictConds" },
            { "pda_apps", "dictPDAAppIcons" },
            { "installables", "dictInstallables" },
            { "cooverlays", "dictCOOverlays" },
            { "condowners", "dictCOs" },
            { "ledgerdefs", "dictLedgerDefs" },
            { "pledges", "dictPledges" },
            { "slots", "dictSlots" },
            { "headlines", "dictHeadlines" },
            { "plots", "dictPlots" },
            { "market/CoCollections", "dictSupersTemp" },
            { "ads", "dictAds" },
            { "rooms", "dictRoomSpecsTemp" },
            { "jobitems", "dictJobitems" },
            { "racing/tracks", "dictRaceTracks" },
            { "context", "dictContext" },
            { "racing/leagues", "dictRacingLeagues" },
            { "info", "dictInfoNodes" },
            { "market/Production", "dictProductionMaps" },
            { "tips", "dictTips" },
        };

        private static readonly HashSet<string> TranslatableFields = new HashSet<string>
        {
            "strTitle", "strDesc", "strTooltip", "strNameFriendly", "strNameShort", "strFriendlyName",
            "strArticleBody", "strArticleTitle", "strNodeLabel", "strBody", "strDescription",
            "strRequirementDescription", "strFriendlyDescription", "description",
        };

        public static int Applied;
        public static int Orphans;

        private static string _pluginDir;
        private static string _langCode;

        public static void Init(string pluginDir, string langCode, Harmony harmony)
        {
            _pluginDir = pluginDir;
            _langCode = langCode;
            var target = AccessTools.Method(typeof(DataHandler), "AllPostLoadAsync");
            if (target == null)
            {
                Plugin.Log.LogError("[i18n] контент-оверлей: DataHandler.AllPostLoadAsync не найден, оверлей не будет применён");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(ContentOverlay).GetMethod(nameof(ApplyPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void ApplyPrefix()
        {
            try { Apply(_pluginDir, _langCode); }
            catch (Exception ex) { Plugin.Log.LogError("[i18n] контент-оверлей упал: " + ex); }
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
                var fileName = kv.Key.Replace("/", "_") + ".json";
                var jsonPath = Path.Combine(dataDir, fileName);
                if (!File.Exists(jsonPath)) continue;
                ApplyCategory(kv.Key, kv.Value, jsonPath);
            }

            Plugin.Log.LogInfo("[i18n] контент-оверлей: применено полей " + Applied + ", сирот " + Orphans);
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
