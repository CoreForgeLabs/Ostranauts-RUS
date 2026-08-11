using System;
using System.Collections.Generic;
using OstraI18n.Core;

static class Program
{
    static int failed = 0;

    static void Eq(string actual, string expected, string name)
    {
        if (actual == expected) { Console.WriteLine("  PASS " + name); }
        else { failed++; Console.WriteLine("  FAIL " + name + ": ожидалось '" + expected + "', получено '" + actual + "'"); }
    }

    static int Main()
    {
        Console.WriteLine("MethodKey");
        Eq(MethodKey.Normalize("A.B/Nested"), "A.B+Nested", "Cecil slash -> plus");
        Eq(MethodKey.Normalize("A.B+Nested"), "A.B+Nested", "Reflection plus unchanged");
        Eq(MethodKey.Normalize("A.B"), "A.B", "plain type unchanged");
        Eq(MethodKey.Make("A.B/Nested", "Refresh", 2), "A.B+Nested::Refresh/2", "make key");

        Console.WriteLine("PluralRule (русский)");
        const string ru = "ru";
        Eq(PluralRule.Category(ru, 1),   "one",  "1 предмет");
        Eq(PluralRule.Category(ru, 2),   "few",  "2 предмета");
        Eq(PluralRule.Category(ru, 5),   "many", "5 предметов");
        Eq(PluralRule.Category(ru, 11),  "many", "11 предметов");
        Eq(PluralRule.Category(ru, 21),  "one",  "21 предмет");
        Eq(PluralRule.Category(ru, 22),  "few",  "22 предмета");
        Eq(PluralRule.Category(ru, 0),   "many", "0 предметов");
        Eq(PluralRule.Category(ru, 114), "many", "114 предметов");

        Console.WriteLine("PluralRule (английский)");
        Eq(PluralRule.Category("en", 1), "one",   "1 item");
        Eq(PluralRule.Category("en", 2), "other", "2 items");
        Eq(PluralRule.Category("en", 0), "other", "0 items");

        Console.WriteLine("LanguagePack");
        var en = new LanguagePack(
            new Dictionary<string, object> { ["GUI_OK"] = "OK", ["GUI_ONLY_EN"] = "English only" },
            "en", null);
        var ru2 = new LanguagePack(
            new Dictionary<string, object>
            {
                ["GUI_OK"] = "Хорошо",
                ["GUI_ITEMS"] = new Dictionary<string, string>
                {
                    ["one"] = "{0} предмет", ["few"] = "{0} предмета", ["many"] = "{0} предметов"
                }
            },
            "ru", en);

        Eq(ru2.Get("GUI_OK"), "Хорошо", "прямое попадание");
        Eq(ru2.Get("GUI_ONLY_EN"), "English only", "fallback в английский");
        Eq(ru2.Get("GUI_MISSING") ?? "<null>", "<null>", "отсутствующий ключ даёт null");
        Eq(ru2.Plural("GUI_ITEMS", 1), "{0} предмет", "плюрал one");
        Eq(ru2.Plural("GUI_ITEMS", 3), "{0} предмета", "плюрал few");
        Eq(ru2.Plural("GUI_ITEMS", 7), "{0} предметов", "плюрал many");
        Eq(ru2.Plural("GUI_OK", 5), "Хорошо", "плюрал на обычной строке возвращает строку");

        Console.WriteLine("PackLoader");
        var langsDir = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "langs"));
        Console.WriteLine("  langs: " + langsDir);
        var packRu = PackLoader.Load(langsDir, "ru");
        Eq(packRu.Get("GUI_CHARGEN_AT_LARGE"), "На свободе", "русская строка загружена");
        var packEn = PackLoader.Load(langsDir, "en");
        Eq(packEn.Get("GUI_CHARGEN_AT_LARGE"), "At Large", "английская строка загружена");

        Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
        return failed == 0 ? 0 : 1;
    }
}
