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

        private static string Wrap(string value) => QaMode ? "⟦" + value + "⟧" : value;

        internal static void Init(string pluginDir, string languageCode)
        {
            Language = languageCode;
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

        /// Вызывается из подменённого IL. Никогда не бросает исключений.
        public static string Get(string key)
        {
            try
            {
                var v = _pack?.Get(key);
                return Wrap(v ?? key);
            }
            catch { return key; }
        }

        public static string Plural(string key, long count)
        {
            try
            {
                var v = _pack?.Plural(key, count);
                return Wrap(v ?? key);
            }
            catch { return key; }
        }
    }
}
