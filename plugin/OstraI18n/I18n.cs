using System;
using System.IO;
using OstraI18n.Core;

namespace OstraI18n
{
    /// Единственная точка входа рантайма. Транспайлер подставляет вызов Get(ключ)
    /// вместо литерала, поэтому Get обязан быть безотказным: любая проблема
    /// возвращает осмысленный текст, а не бросает исключение внутрь кода игры.
    public static class I18n
    {
        private static LanguagePack _pack;
        public static string Language { get; private set; } = "en";
        public static int Applied;
        public static int Drifted;
        public static bool QaMode;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _untranslatedDump =
            new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        private static string _dumpFilePath;

        private static string Wrap(string value) => QaMode ? "⟦" + value + "⟧" : value;

        internal static void Init(string pluginDir, string languageCode)
        {
            Language = languageCode;
            _dumpFilePath = Path.Combine(pluginDir, "untranslated_dump.txt");
            try
            {
                if (!File.Exists(_dumpFilePath))
                {
                    File.WriteAllText(_dumpFilePath, "# OstraI18n Live Untranslated Dump\n# Format: [TYPE] Text (context)\n\n");
                }
            }
            catch { }

            try
            {
                _pack = PackLoader.Load(Path.Combine(pluginDir, "langs"), languageCode);
                foreach (var e in PackLoader.Errors) Plugin.Log.LogWarning("[i18n] pack: " + e);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] загрузка пакета не удалась: " + ex);
                _pack = null;
            }
        }

        public static void RecordUntranslated(string type, string text, string context = "")
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(_dumpFilePath)) return;
            var clean = text.Trim();
            if (clean.Length < 2) return;

            bool hasLatin = false;
            bool hasCyrillic = false;
            foreach (var c in clean)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) hasLatin = true;
                else if (c >= '\u0400' && c <= '\u04FF') { hasCyrillic = true; break; }
            }
            if (!hasLatin || hasCyrillic) return;

            var key = type + "\t" + clean;
            if (_untranslatedDump.TryAdd(key, true))
            {
                try
                {
                    var line = "[" + type + "] " + clean + (string.IsNullOrEmpty(context) ? "" : " (in " + context + ")");
                    File.AppendAllText(_dumpFilePath, line + "\n");
                }
                catch { }
            }
        }

        /// Вызывается из подменённого IL. Никогда не бросает исключений.
        public static string Get(string key)
        {
            try
            {
                var v = _pack?.Get(key);
                if (v == null)
                {
                    RecordUntranslated("MISSING_KEY", key);
                    return Wrap(key);
                }
                return Wrap(v);
            }
            catch { return key; }
        }

        public static string Plural(string key, long count)
        {
            try
            {
                var v = _pack?.Plural(key, count);
                if (v == null)
                {
                    RecordUntranslated("MISSING_KEY_PLURAL", key);
                    return Wrap(key);
                }
                return Wrap(v);
            }
            catch { return key; }
        }
    }
}
