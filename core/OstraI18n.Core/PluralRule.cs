namespace OstraI18n.Core
{
    /// Категории множественного числа по CLDR. Правило выбирается по СЕМЕЙСТВУ
    /// плюрализации (pluralRuleFamily), которое языковой пакет объявляет в
    /// своих данных (meta.json), а НЕ по жёстко зашитому коду языка (C2, Task
    /// 5.6): чтобы добавить украинский или польский (тоже "slavic"), достаточно
    /// пометить их meta.json тем же значением "pluralRuleFamily" — правка этого
    /// файла не требуется. Конечный набор самих АЛГОРИТМОВ (не языков) —
    /// legitimate код: CLDR определяет фиксированное конечное число таких
    /// плюральных семейств, это не языко-специфичная ветка, а компактная
    /// таблица общего назначения.
    public static class PluralRule
    {
        public const string Slavic = "slavic";

        public static string Category(string pluralRuleFamily, long n)
        {
            switch (pluralRuleFamily)
            {
                case Slavic:
                    return SlavicCategory(n);
                default:
                    return n == 1 ? "one" : "other";
            }
        }

        private static string SlavicCategory(long n)
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
