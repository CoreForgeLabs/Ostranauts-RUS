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
        //
        // Task 5.4: это теперь только встроенный ДЕФОЛТ. Основной источник —
        // langs/ru/pack.json -> "overlay" (см. LangPack.OverlayCategoryToField /
        // OverlayTranslatableFields, читается GrammarPackLoader). Если секция
        // overlay в pack.json отсутствует/пуста/повреждена, Init() ниже оставляет
        // CategoryToField/TranslatableFields указывающими на эти дефолтные
        // таблицы — деплой не ломается даже с повреждённым pack.json.
        private static readonly Dictionary<string, string> DefaultCategoryToField = new Dictionary<string, string>
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

        private static readonly HashSet<string> DefaultTranslatableFields = new HashSet<string>
        {
            "strTitle", "strDesc", "strTooltip", "strNameFriendly", "strNameShort", "strFriendlyName",
            "strArticleBody", "strArticleTitle", "strNodeLabel", "strBody", "strDescription",
            "strRequirementDescription", "strFriendlyDescription", "description", "strTutorialKey",
        };

        // Эффективные таблицы, используемые при применении оверлея. По умолчанию
        // указывают на встроенный дефолт; Init() переключает их на данные из
        // pack.json, если те валидны (см. LangPack.OverlayValid).
        private static Dictionary<string, string> CategoryToField = DefaultCategoryToField;
        private static HashSet<string> TranslatableFields = DefaultTranslatableFields;

        public static int Applied;
        public static int Orphans;

        private static string _pluginDir;
        private static string _langCode;

        public static void Init(string pluginDir, string langCode, Harmony harmony)
        {
            _pluginDir = pluginDir;
            _langCode = langCode;

            if (LangPack.OverlayValid && LangPack.OverlayCategoryToField.Count > 0 && LangPack.OverlayTranslatableFields.Count > 0)
            {
                CategoryToField = LangPack.OverlayCategoryToField;
                TranslatableFields = new HashSet<string>(LangPack.OverlayTranslatableFields);
                Plugin.Log.LogInfo("[i18n] контент-оверлей: карты загружены из pack.json (categoryToField="
                    + CategoryToField.Count + ", translatableFields=" + TranslatableFields.Count + ")");
            }
            else
            {
                CategoryToField = DefaultCategoryToField;
                TranslatableFields = DefaultTranslatableFields;
                Plugin.Log.LogInfo("[i18n] контент-оверлей: секция overlay в pack.json отсутствует/пуста/повреждена, "
                    + "используется встроенный дефолт (categoryToField=" + CategoryToField.Count
                    + ", translatableFields=" + TranslatableFields.Count + ")");
            }

            CheckFieldDiscovery();

            var target = AccessTools.Method(typeof(DataHandler), "AllPostLoadAsync");
            if (target == null)
            {
                Plugin.Log.LogError("[i18n] контент-оверлей: DataHandler.AllPostLoadAsync не найден, оверлей не будет применён");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(
                typeof(ContentOverlay).GetMethod(nameof(ApplyPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        // Task 5.5: рефлексией перечисляет публичные статические поля
        // Dictionary<string,*> у DataHandler и для каждой записи CategoryToField,
        // чьё целевое поле среди них не найдено, логирует предупреждение. Это
        // ловит переименование/удаление поля в DataHandler при обновлении игры
        // (риск из Р6: "Переименование поля в DataHandler -> Категория молча
        // отвалится") ДО того, как категория молча перестанет применяться —
        // не дожидаясь запуска ApplyCategory (который предупреждает только для
        // категорий, у которых есть файл данных в data/).
        private static void CheckFieldDiscovery()
        {
            var discovered = new HashSet<string>();
            foreach (var f in typeof(DataHandler).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!f.FieldType.IsGenericType) continue;
                if (f.FieldType.GetGenericTypeDefinition() != typeof(Dictionary<,>)) continue;
                if (f.FieldType.GetGenericArguments()[0] != typeof(string)) continue;
                discovered.Add(f.Name);
            }

            foreach (var kv in CategoryToField)
            {
                if (!discovered.Contains(kv.Value))
                {
                    Plugin.Log.LogWarning("[i18n] контент-оверлей: WARN: категория " + kv.Key + " -> поле " + kv.Value + " отсутствует");
                }
            }
        }

        // Task 5.5: сравнивает счётчик Applied с эталоном из baseline.json и
        // предупреждает, если он упал ниже 90% (Р6: "Applied ниже 90% от
        // записанного в baseline.json -> предупреждение в лог"). Диагностика
        // read-only: не бросает исключений и не является фатальной ни при
        // отсутствии, ни при повреждении файла — в этом случае просто
        // пропускает проверку и объясняет почему.
        private static void CheckBaseline(string pluginDir, string langCode)
        {
            try
            {
                var baselinePath = Path.Combine(pluginDir, "langs", langCode, "baseline.json");
                if (!File.Exists(baselinePath))
                {
                    Plugin.Log.LogInfo("[i18n] контент-оверлей: baseline.json не найден (" + baselinePath
                        + "), проверка регрессии Applied пропущена");
                    return;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(baselinePath));
                if (!doc.RootElement.TryGetProperty("appliedReference", out var refEl)
                    || refEl.ValueKind != JsonValueKind.Number || !refEl.TryGetInt32(out var reference) || reference <= 0)
                {
                    Plugin.Log.LogWarning("[i18n] контент-оверлей: baseline.json повреждён или не содержит "
                        + "положительного числового поля appliedReference, проверка регрессии Applied пропущена");
                    return;
                }

                if (Applied < 0.9 * reference)
                {
                    var pct = (100.0 * Applied / reference).ToString("F1");
                    Plugin.Log.LogWarning("[i18n] контент-оверлей: WARN: Applied=" + Applied
                        + " ниже 90% от эталона baseline.json (reference=" + reference + ", " + pct + "%)");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] контент-оверлей: не удалось прочитать baseline.json, "
                    + "проверка регрессии Applied пропущена: " + ex.Message);
            }
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

            CheckBaseline(pluginDir, langCode);
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
