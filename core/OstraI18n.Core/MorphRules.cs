using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OstraI18n.Core
{
    /// Суффиксный движок склонения (Task 6.3, план Р2 слой B). Фолбэк для
    /// strName вне таблицы named_forms.json: процедурные имена, будущий
    /// контент модов, любые именованные сущности вне condowners. Не
    /// морфологический анализатор -- упорядоченный список правил
    /// "окончание -> суффиксы по падежам", данные из morph_rules.json.
    /// Языково-нейтрален: сам класс не знает, что правила русские -- он
    /// просто применяет regex+суффикс, которые ему передали данные.
    public class MorphRules
    {
        private class Rule
        {
            public Regex Pattern;
            public int Strip;
            public Dictionary<string, string> Suffix;
        }

        public static readonly MorphRules Empty = new MorphRules(new List<Rule>());

        private readonly List<Rule> _rules;

        private MorphRules(List<Rule> rules)
        {
            _rules = rules;
        }

        /// Битый/отсутствующий файл правил не роняет резолвер -- деградирует
        /// до пустого набора правил (TryDecline всегда возвращает false),
        /// тот же принцип отказоустойчивости, что и в PackLoader.
        public static MorphRules Load(string path)
        {
            var rules = new List<Rule>();
            if (File.Exists(path))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                            AddRule(rules, item);
                    }
                }
                catch (Exception)
                {
                    // Малформед morph_rules.json -- фолбэк на пустой набор правил,
                    // а не исключение наверх (TokenResolver в этом случае просто
                    // всегда уходит в C-фолбэк с инкрементом счётчика промахов).
                }
            }
            return new MorphRules(rules);
        }

        private static void AddRule(List<Rule> rules, JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object) return;
            if (!item.TryGetProperty("match", out var m) || m.ValueKind != JsonValueKind.String) return;

            var rule = new Rule
            {
                Pattern = new Regex(m.GetString()),
                Strip = item.TryGetProperty("strip", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0,
                Suffix = new Dictionary<string, string>(StringComparer.Ordinal),
            };
            if (item.TryGetProperty("suffix", out var suf) && suf.ValueKind == JsonValueKind.Object)
                foreach (var f in suf.EnumerateObject())
                    if (f.Value.ValueKind == JsonValueKind.String)
                        rule.Suffix[f.Name] = f.Value.GetString();
            rules.Add(rule);
        }

        /// Первое сработавшее правило по порядку списка побеждает. Правило
        /// сработало, если его паттерн matches слово И у него есть суффикс
        /// для запрошенного падежа. result = усечённое на Strip символов
        /// слово + суффикс. Возвращает false (result == исходное слово), если
        /// ни одно правило не подошло -- вызывающий (TokenResolver) в этом
        /// случае уходит в конечный фолбэк C.
        public bool TryDecline(string word, string caseCode, out string result)
        {
            result = word;
            if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(caseCode)) return false;

            foreach (var rule in _rules)
            {
                if (rule.Strip > word.Length) continue;
                if (!rule.Pattern.IsMatch(word)) continue;
                if (!rule.Suffix.TryGetValue(caseCode, out var suffix)) continue;

                result = word.Substring(0, word.Length - rule.Strip) + suffix;
                return true;
            }
            return false;
        }
    }
}
