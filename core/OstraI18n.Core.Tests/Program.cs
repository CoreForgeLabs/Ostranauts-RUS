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

    static void True(bool cond, string name)
    {
        if (cond) { Console.WriteLine("  PASS " + name); }
        else { failed++; Console.WriteLine("  FAIL " + name); }
    }

    static bool PronounsEqual(Dictionary<string, string[]> a, Dictionary<string, string[]> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var other)) return false;
            if (kv.Value.Length != other.Length) return false;
            for (int i = 0; i < kv.Value.Length; i++)
                if (kv.Value[i] != other[i]) return false;
        }
        return true;
    }

    static bool StringsEqual(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var other)) return false;
            if (kv.Value != other) return false;
        }
        return true;
    }

    static bool VerbsEqual(Dictionary<string, VerbForms> a, Dictionary<string, VerbForms> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var v)) return false;
            var u = kv.Value;
            if (u.Kind != v.Kind || u.OmitPresent != v.OmitPresent || u.NoLongerBefore != v.NoLongerBefore) return false;
            if (!ArrEqual(u.Present, v.Present) || !ArrEqual(u.Past, v.Past)) return false;
        }
        return true;
    }

    static bool ArrEqual(string[] a, string[] b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
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
        Eq(packRu.Get("GUI_TEST_PACKLOADER_SENTINEL"), "тест-пакета", "русская строка загружена");
        var packEn = PackLoader.Load(langsDir, "en");
        Eq(packEn.Get("GUI_TEST_PACKLOADER_SENTINEL"), "test-pack", "английская строка загружена");

        Console.WriteLine("GrammarPackLoader (старая vs новая раскладка ru)");
        {
            var langsDir2 = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "langs"));
            var oldDir = System.IO.Path.Combine(langsDir2, "lang_ru");
            var newDir = System.IO.Path.Combine(langsDir2, "ru");
            Console.WriteLine("  old: " + oldDir);
            Console.WriteLine("  new: " + newDir);

            var oldPack = GrammarPackLoader.Load(oldDir);
            var newPack = GrammarPackLoader.Load(newDir);

            True(oldPack.UsedLegacyLayout, "старая раскладка (grammar.json) помечена UsedLegacyLayout");
            True(!newPack.UsedLegacyLayout, "новая раскладка (pack.json) НЕ помечена UsedLegacyLayout");

            Eq(oldPack.YouWord, newPack.YouWord, "YouWord совпадает между раскладками");
            Eq(oldPack.YouWord, "ты", "YouWord = 'ты'");

            Eq(oldPack.Pronouns.Count.ToString(), newPack.Pronouns.Count.ToString(), "число категорий местоимений совпадает");
            True(PronounsEqual(oldPack.Pronouns, newPack.Pronouns), "словарь Pronouns идентичен между раскладками");

            Eq(oldPack.Verbs.Count.ToString(), newPack.Verbs.Count.ToString(), "число глаголов совпадает");
            True(VerbsEqual(oldPack.Verbs, newPack.Verbs), "словарь Verbs идентичен между раскладками");

            Eq(oldPack.Strings.Count.ToString(), newPack.Strings.Count.ToString(), "число строк совпадает");
            True(StringsEqual(oldPack.Strings, newPack.Strings), "словарь Strings идентичен между раскладками");

            Console.WriteLine("GrammarPackLoader overlay (Task 5.4)");
            True(!oldPack.OverlayValid, "старая раскладка (grammar.json) не содержит overlay -> OverlayValid=false");
            True(newPack.OverlayValid, "новая раскладка (pack.json) содержит overlay -> OverlayValid=true");
            Eq(newPack.OverlayCategoryToField.Count.ToString(), "22", "categoryToField: 22 записи");
            Eq(newPack.OverlayTranslatableFields.Count.ToString(), "14", "translatableFields: 14 записей");
        }

        Console.WriteLine("PathKey");
        Eq(string.Join("/", PathKey.Segments("GUIBountyDetails", new[] { "LeftText", "txtDanger" })),
           "GUIBountyDetails/LeftText/txtDanger", "полный путь из root+path");
        var eq1 = PathKey.Matches(
            new[] { "GUIBountyDetails(Clone)", "LeftText", "txtDanger" },
            new[] { "GUIBountyDetails", "LeftText", "txtDanger" });
        Eq(eq1 ? "yes" : "no", "yes", "суффикс (Clone) игнорируется");
        var eq2 = PathKey.Matches(
            new[] { "GUIBountyDetails(Clone)", "LeftText", "txtOther" },
            new[] { "GUIBountyDetails", "LeftText", "txtDanger" });
        Eq(eq2 ? "yes" : "no", "no", "разный последний сегмент не совпадает");
        var eq3 = PathKey.Matches(
            new[] { "Root", "A" },
            new[] { "Root", "A", "B" });
        Eq(eq3 ? "yes" : "no", "no", "разная длина пути не совпадает");

        Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
        return failed == 0 ? 0 : 1;
    }
}
