using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OstraI18n.Core
{
    /// Читает langs/<code>/ui/*.json и meta.json. Значение ключа — строка либо
    /// объект с формами множественного числа. Битый файл пропускается с записью
    /// в errors, а не роняет загрузку: частично собранный язык лучше отсутствия языка.
    public static class PackLoader
    {
        public static List<string> Errors { get; } = new List<string>();

        public static LanguagePack Load(string langsDir, string languageCode)
        {
            return Load(langsDir, languageCode, new HashSet<string>());
        }

        private static LanguagePack Load(string langsDir, string code, HashSet<string> visited)
        {
            if (!visited.Add(code)) return null;   // защита от циклической цепочки fallback

            var dir = Path.Combine(langsDir, code);
            if (!Directory.Exists(dir))
            {
                Errors.Add("нет папки языка: " + dir);
                return null;
            }

            LanguagePack fallback = null;
            foreach (var fb in ReadFallback(Path.Combine(dir, "meta.json")))
            {
                fallback = Load(langsDir, fb, visited);
                if (fallback != null) break;
            }

            var entries = new Dictionary<string, object>(StringComparer.Ordinal);
            var stringsPath = Path.Combine(dir, "strings.json");
            if (File.Exists(stringsPath))
            {
                MergeFile(stringsPath, entries);
            }

            // ui/*.json is merged AFTER strings.json and overwrites it key-for-key. That is
            // intentional (ui/ holds screen-fitted variants), but a stale duplicate there
            // silently defeats any edit made in strings.json -- a trap that costs hours to
            // find. Report every shadowed key so it surfaces in the log for each language.
            var uiDir = Path.Combine(dir, "ui");
            if (Directory.Exists(uiDir))
            {
                var fromStrings = new Dictionary<string, object>(entries, StringComparer.Ordinal);
                foreach (var f in Directory.GetFiles(uiDir, "*.json"))
                    MergeFile(f, entries);

                var shadowed = new List<string>();
                foreach (var kv in fromStrings)
                {
                    if (entries.TryGetValue(kv.Key, out var now)
                        && now is string ns && kv.Value is string os && ns != os)
                        shadowed.Add(kv.Key);
                }
                if (shadowed.Count > 0)
                {
                    shadowed.Sort(StringComparer.Ordinal);
                    Errors.Add(code + ": ui/*.json перекрывает strings.json для " + shadowed.Count
                        + " ключей (правки в strings.json для них не применятся): "
                        + string.Join(", ", shadowed.GetRange(0, Math.Min(12, shadowed.Count)))
                        + (shadowed.Count > 12 ? ", ..." : ""));
                }
            }
            var pluralRuleFamily = ReadPluralRuleFamily(Path.Combine(dir, "meta.json"));
            return new LanguagePack(entries, code, fallback, pluralRuleFamily);
        }

        private static IEnumerable<string> ReadFallback(string metaPath)
        {
            var result = new List<string>();
            try
            {
                if (!File.Exists(metaPath)) return result;
                using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
                if (doc.RootElement.TryGetProperty("fallback", out var fb)
                    && fb.ValueKind == JsonValueKind.Array)
                    foreach (var e in fb.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String) result.Add(e.GetString());
            }
            catch (Exception ex) { Errors.Add(metaPath + ": " + ex.Message); }
            return result;
        }

        // Task 5.6 (C2 fix round): plural-rule family is data the pack declares
        // (e.g. "slavic"), not something PluralRule.cs infers from the language
        // code itself -- see PluralRule.cs for why. Absent/malformed meta.json
        // simply yields null, which PluralRule.Category treats as the default
        // (western two-form) family.
        private static string ReadPluralRuleFamily(string metaPath)
        {
            try
            {
                if (!File.Exists(metaPath)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
                if (doc.RootElement.TryGetProperty("pluralRuleFamily", out var v)
                    && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
            }
            catch (Exception ex) { Errors.Add(metaPath + ": " + ex.Message); }
            return null;
        }

        private static void MergeFile(string path, Dictionary<string, object> into)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var kv in doc.RootElement.EnumerateObject())
                    {
                        if (kv.Value.ValueKind == JsonValueKind.String)
                        {
                            into[kv.Name] = kv.Value.GetString();
                        }
                        else if (kv.Value.ValueKind == JsonValueKind.Object)
                        {
                            var forms = new Dictionary<string, string>(StringComparer.Ordinal);
                            foreach (var f in kv.Value.EnumerateObject())
                                if (f.Value.ValueKind == JsonValueKind.String)
                                    forms[f.Name] = f.Value.GetString();
                            into[kv.Name] = forms;
                        }
                    }
                    return;
                }

                if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
                foreach (var block in doc.RootElement.EnumerateArray())
                {
                    if (!block.TryGetProperty("dict", out var dict)) continue;
                    if (dict.ValueKind != JsonValueKind.Object) continue;
                    foreach (var kv in dict.EnumerateObject())
                    {
                        if (kv.Value.ValueKind == JsonValueKind.String)
                        {
                            into[kv.Name] = kv.Value.GetString();
                        }
                        else if (kv.Value.ValueKind == JsonValueKind.Object)
                        {
                            var forms = new Dictionary<string, string>(StringComparer.Ordinal);
                            foreach (var f in kv.Value.EnumerateObject())
                                if (f.Value.ValueKind == JsonValueKind.String)
                                    forms[f.Name] = f.Value.GetString();
                            into[kv.Name] = forms;
                        }
                    }
                }
            }
            catch (Exception ex) { Errors.Add(path + ": " + ex.Message); }
        }
    }
}
