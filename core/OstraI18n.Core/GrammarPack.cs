using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OstraI18n.Core
{
    /// Одна словоформа/парадигма глагола: present/past по лицам + флаги для
    /// связки "быть" (в настоящем времени по-русски опускается).
    public class VerbForms
    {
        public string Kind = "verb";          // "verb" | "copula"
        public bool OmitPresent;              // copula: dropped in present tense (Russian)
        public string[] Present;              // [1s, 2s, 3m, 3f, 3pl, 3n]
        public string[] Past;                 // [m, f, n, pl]
        public string NoLongerBefore = "больше не ";
    }

    /// Результат загрузки одного языкового пакета (грамматика+глаголы+строки),
    /// независимо от того, какая раскладка на диске его дала.
    public class GrammarPackResult
    {
        public string YouWord;
        public readonly Dictionary<string, string[]> Pronouns = new Dictionary<string, string[]>(StringComparer.Ordinal);
        public readonly Dictionary<string, VerbForms> Verbs = new Dictionary<string, VerbForms>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal);
        // true, если каталог не содержал pack.json и пришлось читать старую раскладку
        // (grammar.json + verbs.json + strings.json).
        public bool UsedLegacyLayout;
    }

    /// Читает языковой пакет из каталога `dir`, поддерживая обе раскладки:
    ///   новая:  dir/pack.json (поле "pronounCategories") + dir/verbs.json + dir/strings.json
    ///   старая: dir/grammar.json (поле "pronouns")        + dir/verbs.json + dir/strings.json
    /// Раскладка определяется по наличию pack.json — если его нет, читается
    /// старая (UsedLegacyLayout=true), вызывающий код сам решает, логировать ли
    /// предупреждение об этом.
    public static class GrammarPackLoader
    {
        public static GrammarPackResult Load(string dir)
        {
            var result = new GrammarPackResult();

            var packPath = Path.Combine(dir, "pack.json");
            if (File.Exists(packPath))
            {
                result.UsedLegacyLayout = false;
                using var doc = JsonDocument.Parse(File.ReadAllText(packPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("you", out var you) && you.ValueKind == JsonValueKind.String)
                    result.YouWord = you.GetString();
                if (root.TryGetProperty("pronounCategories", out var cats) && cats.ValueKind == JsonValueKind.Object)
                    foreach (var kv in cats.EnumerateObject())
                        result.Pronouns[kv.Name] = ToStrArray(kv.Value);
            }
            else
            {
                result.UsedLegacyLayout = true;
                var gramPath = Path.Combine(dir, "grammar.json");
                if (!File.Exists(gramPath)) throw new FileNotFoundException(gramPath);
                using var doc = JsonDocument.Parse(File.ReadAllText(gramPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("you", out var you) && you.ValueKind == JsonValueKind.String)
                    result.YouWord = you.GetString();
                if (root.TryGetProperty("pronouns", out var prons) && prons.ValueKind == JsonValueKind.Object)
                    foreach (var kv in prons.EnumerateObject())
                        result.Pronouns[kv.Name] = ToStrArray(kv.Value);
            }

            var verbPath = Path.Combine(dir, "verbs.json");
            if (!File.Exists(verbPath)) throw new FileNotFoundException(verbPath);
            using (var doc = JsonDocument.Parse(File.ReadAllText(verbPath)))
            {
                foreach (var kv in doc.RootElement.EnumerateObject())
                {
                    var vname = kv.Name;
                    if (vname.StartsWith("_")) continue;
                    var jv = kv.Value;
                    if (jv.ValueKind != JsonValueKind.Object) continue;
                    var vf = new VerbForms();
                    if (jv.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String) vf.Kind = kind.GetString();
                    if (jv.TryGetProperty("omitPresent", out var op) && (op.ValueKind == JsonValueKind.True || op.ValueKind == JsonValueKind.False)) vf.OmitPresent = op.GetBoolean();
                    if (jv.TryGetProperty("noLonger", out var nl) && nl.ValueKind == JsonValueKind.String) vf.NoLongerBefore = nl.GetString();
                    if (jv.TryGetProperty("present", out var pres)) vf.Present = ToStrArray(pres);
                    if (jv.TryGetProperty("past", out var past)) vf.Past = ToStrArray(past);
                    result.Verbs[vname] = vf;
                }
            }

            var strPath = Path.Combine(dir, "strings.json");
            if (File.Exists(strPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(strPath));
                foreach (var kv in doc.RootElement.EnumerateObject())
                    if (kv.Value.ValueKind == JsonValueKind.String) result.Strings[kv.Name] = kv.Value.GetString();
            }

            return result;
        }

        private static string[] ToStrArray(JsonElement arr)
        {
            if (arr.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var r = new string[arr.GetArrayLength()];
            int i = 0;
            foreach (var e in arr.EnumerateArray()) r[i++] = e.GetString();
            return r;
        }
    }
}
