namespace OstraI18n.Core
{
    /// Категории множественного числа по CLDR. Правило выбирается по коду языка,
    /// а не зашивается в вызывающий код: добавление языка не требует правки C#.
    public static class PluralRule
    {
        public static string Category(string languageCode, long n)
        {
            switch (languageCode)
            {
                case "ru":
                case "uk":
                case "pl":
                    return Slavic(n);
                default:
                    return n == 1 ? "one" : "other";
            }
        }

        private static string Slavic(long n)
        {
            long abs = n < 0 ? -n : n;
            long mod10 = abs % 10;
            long mod100 = abs % 100;
            if (mod10 == 1 && mod100 != 11) return "one";
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return "few";
            return "many";
        }
    }
}
