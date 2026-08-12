using System.Collections.Generic;

namespace OstraI18n.Core
{
    /// Строки одного языка. Значение ключа — либо строка, либо набор форм
    /// множественного числа. Отсутствующий ключ уходит в fallback-пакет,
    /// и только если и там пусто — возвращается null (вызывающий покажет ключ).
    public class LanguagePack
    {
        private readonly Dictionary<string, object> _entries;
        private readonly string _languageCode;
        private readonly string _pluralRuleFamily;
        private readonly LanguagePack _fallback;

        public LanguagePack(Dictionary<string, object> entries, string languageCode, LanguagePack fallback,
            string pluralRuleFamily = null)
        {
            _entries = entries ?? new Dictionary<string, object>();
            _languageCode = languageCode;
            _pluralRuleFamily = pluralRuleFamily;
            _fallback = fallback;
        }

        public string Get(string key)
        {
            if (key != null && _entries.TryGetValue(key, out var v))
            {
                if (v is string s) return s;
                if (v is Dictionary<string, string> forms)
                {
                    if (forms.TryGetValue("other", out var o)) return o;
                    foreach (var kv in forms) return kv.Value;
                }
            }
            return _fallback?.Get(key);
        }

        public string Plural(string key, long count)
        {
            if (key != null && _entries.TryGetValue(key, out var v))
            {
                if (v is Dictionary<string, string> forms)
                {
                    var cat = PluralRule.Category(_pluralRuleFamily, count);
                    if (forms.TryGetValue(cat, out var form)) return form;
                    if (forms.TryGetValue("other", out var other)) return other;
                }
                if (v is string s) return s;
            }
            return _fallback?.Plural(key, count);
        }
    }
}
