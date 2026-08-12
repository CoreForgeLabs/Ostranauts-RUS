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

        Console.WriteLine("PluralRule (slavic family)");
        // Task 5.6 (C2 fix round): PluralRule.Category now takes a plural-rule
        // FAMILY id (declared by pack data, e.g. langs/ru/meta.json's
        // "pluralRuleFamily") instead of a raw language code -- see
        // PluralRule.cs. Exercise it via the family constant, not a literal
        // "ru"/"uk"/"pl".
        const string slavic = PluralRule.Slavic;
        Eq(PluralRule.Category(slavic, 1),   "one",  "1 предмет");
        Eq(PluralRule.Category(slavic, 2),   "few",  "2 предмета");
        Eq(PluralRule.Category(slavic, 5),   "many", "5 предметов");
        Eq(PluralRule.Category(slavic, 11),  "many", "11 предметов");
        Eq(PluralRule.Category(slavic, 21),  "one",  "21 предмет");
        Eq(PluralRule.Category(slavic, 22),  "few",  "22 предмета");
        Eq(PluralRule.Category(slavic, 0),   "many", "0 предметов");
        Eq(PluralRule.Category(slavic, 114), "many", "114 предметов");

        Console.WriteLine("PluralRule (default family)");
        Eq(PluralRule.Category("default", 1), "one",   "1 item");
        Eq(PluralRule.Category("default", 2), "other", "2 items");
        Eq(PluralRule.Category("default", 0), "other", "0 items");
        Eq(PluralRule.Category(null, 1), "one",   "null family falls back to default (1)");
        Eq(PluralRule.Category(null, 2), "other", "null family falls back to default (2)");

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
            "ru", en, PluralRule.Slavic);

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
            // Task 5.7 follow-up: langs/lang_ru/ (the production legacy-layout
            // directory) was deleted once the pack.json migration was confirmed
            // safe (Task 5.7 gate). The legacy-layout CODE PATH in
            // GrammarPackLoader/LangPack stays -- it's the generic fallback for
            // any future language pack that hasn't been migrated to pack.json
            // yet -- so it still needs a real old-shape fixture to load and
            // compare against the new layout. Rather than depending on deleted
            // production data, the old side now reads a self-contained
            // test-only fixture (testdata/legacy_ru/) reconstructed verbatim
            // from git history (commit a278168~1, the last commit before the
            // deletion) -- same content as the production ru pack had before
            // Task 5.2 migrated it, just still in the old three-file shape.
            var langsDir2 = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "langs"));
            var testDataDir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "testdata"));
            var oldDir = System.IO.Path.Combine(testDataDir, "legacy_ru");
            var newDir = System.IO.Path.Combine(langsDir2, "ru");
            Console.WriteLine("  old (fixture): " + oldDir);
            Console.WriteLine("  new (production): " + newDir);

            var oldPack = GrammarPackLoader.Load(oldDir);
            var newPack = GrammarPackLoader.Load(newDir);

            True(oldPack.UsedLegacyLayout, "старая раскладка (grammar.json) помечена UsedLegacyLayout");
            True(!newPack.UsedLegacyLayout, "новая раскладка (pack.json) НЕ помечена UsedLegacyLayout");

            Eq(oldPack.YouWord, newPack.YouWord, "YouWord совпадает между раскладками");
            Eq(oldPack.YouWord, "ты", "YouWord = 'ты'");

            Eq(oldPack.Pronouns.Count.ToString(), newPack.Pronouns.Count.ToString(), "число категорий местоимений совпадает");
            True(PronounsEqual(oldPack.Pronouns, newPack.Pronouns), "словарь Pronouns идентичен между раскладками");

            // Task 6.5 added 4 new synthetic disambiguation keys (is.cop/is.aux/has.obj/
            // has.qual) directly to the production pack only -- they're intentionally
            // NOT in the frozen pre-migration legacy_ru fixture (that fixture exists to
            // prove the Task 5.2 migration was lossless, not to track every subsequent
            // feature addition). Exclude them here so this test keeps verifying migration
            // fidelity for every verb that predates Task 6.5, while still asserting the
            // new keys exist as exactly +4 on top of that unchanged set.
            var task65NewVerbKeys = new[] { "is.cop", "is.aux", "has.obj", "has.qual" };
            var newPackVerbsMinusTask65 = new Dictionary<string, VerbForms>(newPack.Verbs);
            foreach (var k in task65NewVerbKeys) newPackVerbsMinusTask65.Remove(k);

            Eq(oldPack.Verbs.Count.ToString(), newPackVerbsMinusTask65.Count.ToString(), "число глаголов совпадает (за вычетом новых ключей Task 6.5)");
            True(VerbsEqual(oldPack.Verbs, newPackVerbsMinusTask65), "словарь Verbs идентичен между раскладками (за вычетом новых ключей Task 6.5)");
            foreach (var k in task65NewVerbKeys)
                True(newPack.Verbs.ContainsKey(k), "Task 6.5: новый ключ '" + k + "' присутствует в production verbs.json");

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

        Console.WriteLine("TokenResolver / MorphRules (Task 6.3)");
        {
            // Table + rules loaded from the real production data (langs/ru/named_forms.json,
            // langs/ru/morph_rules.json) via the same langsDir used by the PackLoader section above.
            var resolver = TokenResolver.Load(langsDir, "ru");

            // (a) лемма из таблицы отдаёт запрошенный падеж.
            Eq(resolver.Resolve("AABarTechnoLowPass", null, "gen"), "Раковины", "таблица: strName в named_forms.json отдаёт запрошенный падеж (gen)");
            Eq(resolver.Resolve("AABarTechnoLowPass", null, "dat"), "Раковине", "таблица: тот же strName, другой падеж (dat)");
            Eq(resolver.Resolve("AABarTechnoLowPass", null, "nom"), "Раковина", "таблица: nom отдаёт исходный текст без изменений");

            var missBefore = resolver.MissCount;

            // (b) лемма вне таблицы, но текст подходит под правило MorphRules -- склонённая форма, не именительный.
            var declinedMasc = resolver.Resolve("QA_NOT_IN_TABLE_SAILOR", "Матрос", "gen");
            Eq(declinedMasc, "Матроса", "правило: муж.р. согласная основа, gen через суффикс");
            True(declinedMasc != "Матрос", "правило: результат отличается от исходного nom (не просто фолбэк)");

            var declinedFem = resolver.Resolve("QA_NOT_IN_TABLE_ROOM", "Комната", "gen");
            Eq(declinedFem, "Комнаты", "правило: жен.р. основа на -а, gen через суффикс");

            Eq(resolver.MissCount.ToString(), missBefore.ToString(), "правило сработало -- счётчик промахов не увеличился");

            // (c) неизвестное окончание -- ни таблицы, ни правила; nom без изменений + счётчик промахов +1.
            var missBeforeUnknown = resolver.MissCount;
            var unresolved = resolver.Resolve("QA_NOT_IN_TABLE_UNKNOWN", "Xyz", "gen");
            Eq(unresolved, "Xyz", "неизвестное окончание: возвращается nom (исходный текст) без изменений");
            Eq(resolver.MissCount.ToString(), (missBeforeUnknown + 1).ToString(), "неизвестное окончание: счётчик промахов увеличился ровно на 1");

            var missBeforeSecond = resolver.MissCount;
            resolver.Resolve("QA_NOT_IN_TABLE_UNKNOWN2", "Qwerty", "ins");
            Eq(resolver.MissCount.ToString(), (missBeforeSecond + 1).ToString(), "повторный промах увеличивает счётчик ещё на 1 (не защёлкивается)");

            // Доп. проверки устойчивости: пустой/null вход не должен падать с исключением.
            Eq(resolver.Resolve(null, null, "gen"), "", "null strName и null shortName не роняют резолвер, отдают пустую строку");
            Eq(resolver.Resolve("QA_EMPTY", "", "gen"), "", "пустая строка ShortName возвращается как есть, без исключения");
        }

        Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
        return failed == 0 ? 0 : 1;
    }
}
