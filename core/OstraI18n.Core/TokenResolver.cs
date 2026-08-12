using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OstraI18n.Core
{
    /// Одна запись таблицы named_forms.json -- 6 падежных форм + грамм.
    /// признаки. Форма конкретного strName, а не значение по умолчанию: см.
    /// Task 6.2, langs/ru/named_forms.json.
    public class NamedFormEntry
    {
        public Dictionary<string, string> Forms = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Gender;
        public bool Animate;
        public bool Plural;
    }

    /// Форма именованной сущности по категории (падежу): таблица -> правила
    /// -> фолбэк (план Р2, Task 6.3). Три слоя в порядке приоритета:
    ///   A. named_forms.json -- точное совпадение по strName (стабильный ID
    ///      записи, не переведённый текст).
    ///   B. MorphRules -- суффиксное правило, применённое к тексту, который
    ///      РЕАЛЬНО доступен для склонения в фолбэк-сценарии (см. Resolve).
    ///   C. Именительный падеж без изменений + инкремент MissCount.
    /// Core-only, без зависимости от BepInEx -- вызывающий код (Task 6.4)
    /// подключает это в Patches.cs, здесь только изолированная логика.
    public class TokenResolver
    {
        private readonly Dictionary<string, NamedFormEntry> _table;
        private readonly MorphRules _rules;

        /// Число обращений, дошедших до слоя C (ни таблица, ни правило не
        /// дали формы). Инструмент QA/Task 6.4 -- инспектировать, не сбрасывать
        /// автоматически (счётчик копится за время жизни резолвера).
        public int MissCount { get; private set; }

        public TokenResolver(Dictionary<string, NamedFormEntry> namedForms, MorphRules rules)
        {
            _table = namedForms ?? new Dictionary<string, NamedFormEntry>(StringComparer.Ordinal);
            _rules = rules ?? MorphRules.Empty;
        }

        /// Удобный загрузчик по конвенции этого проекта (langs/<code>/...),
        /// см. PackLoader.Load. Отсутствующие файлы не бросают исключение --
        /// пустая таблица/пустой набор правил, резолвер просто всегда уходит
        /// в фолбэк C и это видно по MissCount.
        public static TokenResolver Load(string langsDir, string languageCode)
        {
            var dir = Path.Combine(langsDir, languageCode);
            var table = LoadNamedForms(Path.Combine(dir, "named_forms.json"));
            var rules = MorphRules.Load(Path.Combine(dir, "morph_rules.json"));
            return new TokenResolver(table, rules);
        }

        private static Dictionary<string, NamedFormEntry> LoadNamedForms(string path)
        {
            var result = new Dictionary<string, NamedFormEntry>(StringComparer.Ordinal);
            if (!File.Exists(path)) return result;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var entry = new NamedFormEntry();
                    if (prop.Value.TryGetProperty("forms", out var forms) && forms.ValueKind == JsonValueKind.Object)
                        foreach (var f in forms.EnumerateObject())
                            if (f.Value.ValueKind == JsonValueKind.String)
                                entry.Forms[f.Name] = f.Value.GetString();

                    if (prop.Value.TryGetProperty("gender", out var g) && g.ValueKind == JsonValueKind.String)
                        entry.Gender = g.GetString();
                    if (prop.Value.TryGetProperty("animate", out var a)
                        && (a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.False))
                        entry.Animate = a.GetBoolean();
                    if (prop.Value.TryGetProperty("plural", out var p)
                        && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False))
                        entry.Plural = p.GetBoolean();

                    result[prop.Name] = entry;
                }
            }
            catch (Exception)
            {
                // Малформед named_forms.json -- частично собранная (или пустая)
                // таблица лучше падения загрузки целиком, тот же принцип, что
                // и в PackLoader.MergeFile.
            }
            return result;
        }

        /// <param name="strName">Стабильный игровой ID записи (напр. поле
        /// strName condowners) -- ключ таблицы A. Может быть null (сущность
        /// вне condowners, у вызывающего может не быть строкового ID).</param>
        /// <param name="shortName">Текст на целевом языке, который РЕАЛЬНО
        /// нужно склонять в фолбэк-сценарии B/C -- то, что вызывающий (Task
        /// 6.4) уже получил как CondOwner.ShortName. strName сам по себе
        /// обычно не русский текст, поэтому применить к нему суффиксное
        /// правило бессмысленно -- нужен реальный текст на выходе.</param>
        /// <param name="caseCode">"nom"/"gen"/"dat"/"acc"/"ins"/"prep".</param>
        public string Resolve(string strName, string shortName, string caseCode)
        {
            // Слой A: точное совпадение по стабильному ID в таблице.
            if (strName != null && _table.TryGetValue(strName, out var entry)
                && entry.Forms.TryGetValue(caseCode ?? "nom", out var tableForm))
                return tableForm;

            var text = shortName ?? strName;
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            // nom всегда равен исходному тексту -- ни правило, ни фолбэк не нужны,
            // и уж тем более не должны увеличивать счётчик промахов.
            if (caseCode == null || caseCode == "nom") return text;

            // Слой B: суффиксное правило по реальному тексту.
            if (_rules.TryDecline(text, caseCode, out var declined)) return declined;

            // Слой C: именительный без изменений + учёт промаха.
            MissCount++;
            return text;
        }
    }
}
