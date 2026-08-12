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
        // Task 5.6 (C2 fix round 3): language-neutral placeholder, not a
        // hardcoded Russian default -- same reasoning as LangPack.YouWord in
        // the plugin project (see plugin/OstraI18n/LangPack.cs). This text is
        // appended directly into game-facing translated output
        // (Patches.cs:69, GrammarUtils.interactionOutput.Append(vf.NoLongerBefore))
        // whenever a verb's pack data doesn't declare an explicit "noLonger"
        // override in verbs.json -- today NO verb in langs/ru/verbs.json does,
        // so this default is what actually ships. Kept as a public const so
        // the plugin-side loader (LangPack.cs) can detect "still the
        // placeholder" and log a loud, aggregate warning instead of silently
        // shipping a Russian string that LOOKS like it came from the pack.
        public const string DefaultNoLongerBefore = "no longer ";

        public string Kind = "verb";          // "verb" | "copula"
        public bool OmitPresent;              // copula: dropped in present tense (Russian)
        public string[] Present;              // [1s, 2s, 3m, 3f, 3pl, 3n]
        public string[] Past;                 // [m, f, n, pl]
        public string NoLongerBefore = DefaultNoLongerBefore;
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

        // Task 5.4: декларативные карты контент-оверлея (ContentOverlay.cs), читаемые
        // из pack.json -> "overlay" -> {categoryToField, translatableFields}.
        // Заполняются только если раскладка новая (pack.json) И секция "overlay"
        // присутствует и оба подполя непусты — иначе OverlayValid остаётся false и
        // вызывающий код (ContentOverlay) обязан использовать встроенный дефолт.
        public readonly Dictionary<string, string> OverlayCategoryToField = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<string> OverlayTranslatableFields = new List<string>();
        public bool OverlayValid;
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

                if (root.TryGetProperty("overlay", out var overlay) && overlay.ValueKind == JsonValueKind.Object)
                {
                    Dictionary<string, string> c2f = null;
                    List<string> fields = null;
                    if (overlay.TryGetProperty("categoryToField", out var c2fEl) && c2fEl.ValueKind == JsonValueKind.Object)
                    {
                        c2f = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (var kv in c2fEl.EnumerateObject())
                            if (kv.Value.ValueKind == JsonValueKind.String) c2f[kv.Name] = kv.Value.GetString();
                    }
                    if (overlay.TryGetProperty("translatableFields", out var tfEl) && tfEl.ValueKind == JsonValueKind.Array)
                    {
                        fields = new List<string>();
                        foreach (var e in tfEl.EnumerateArray())
                            if (e.ValueKind == JsonValueKind.String) fields.Add(e.GetString());
                    }
                    // Malformed/empty overlay section -> treated as absent; OverlayValid
                    // stays false and the caller falls back to its built-in default.
                    if (c2f != null && c2f.Count > 0 && fields != null && fields.Count > 0)
                    {
                        foreach (var kv in c2f) result.OverlayCategoryToField[kv.Key] = kv.Value;
                        result.OverlayTranslatableFields.AddRange(fields);
                        result.OverlayValid = true;
                    }
                }
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
