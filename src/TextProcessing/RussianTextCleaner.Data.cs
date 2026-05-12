using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace OstranautsRusPatch
{
    public static partial class RussianTextCleaner
    {
        private const int MaxCleanCacheSize = 16384;
        private const int TargetCleanCacheSize = 12288;
        private const int MaxCacheableInputLength = 512;
        private const int MaxProcessableInputLength = 2048;
        private static volatile int _cacheTrimScheduled = 0;
        [ThreadStatic] private static string _lastInput;
        [ThreadStatic] private static string _lastOutput;

        private static string LastInput
        {
            get { return _lastInput ?? string.Empty; }
            set { _lastInput = value; }
        }

        private static string LastOutput
        {
            get { return _lastOutput ?? string.Empty; }
            set { _lastOutput = value; }
        }

        // --- Articles and possessive before any word (mixed text) ---
        private static readonly Regex rxThe = new Regex(
            "\\b[Tt]he\\s+(?=\\w)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex rxAAn = new Regex(
            "\\b[Aa]n?\\s+(?=\\w)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex rxPossS = new Regex(
            "'s(?=\\s+\\w)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex rxPossSEnd = new Regex(
            "'s$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex rxMultiSpace = new Regex(
            "[ ]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // --- English pronouns before Cyrillic ---
        // Matches pronouns/contractions that appear before Russian text
        // (?<!\[) prevents matching inside game variables like [them], [he], [her], etc.
        // (?!\]) prevents matching when followed by ] (e.g. [them] → should NOT become [их])
        private static readonly Regex rxPronoun = new Regex(
            "(?<!\\[)\\b(You're|you're|You've|you've|You'll|you'll|They're|they're|They've|they've|They'll|they'll|He's|he's|She's|she's|He'll|he'll|She'll|she'll|I'm|I've|I'll|Your|your|Their|their|You|you|They|they|She|she|His|his|Her|her|Him|him|Them|them|He|he|My|my|Me|me)\\b(?!\\])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly ConcurrentDictionary<string, string> pronounMap =
            new ConcurrentDictionary<string, string>(InitPronounMap(), StringComparer.Ordinal);

        private static Dictionary<string, string> InitPronounMap()
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            // Subjective
            m["You"] = "\u0412\u044b"; m["you"] = "\u0432\u044b";
            m["He"] = "\u041e\u043d"; m["he"] = "\u043e\u043d";
            m["She"] = "\u041e\u043d\u0430"; m["she"] = "\u043e\u043d\u0430";
            m["They"] = "\u041e\u043d\u0438"; m["they"] = "\u043e\u043d\u0438";
            // Possessive
            m["Your"] = "\u0412\u0430\u0448"; m["your"] = "\u0432\u0430\u0448";
            m["His"] = "\u0415\u0433\u043e"; m["his"] = "\u0435\u0433\u043e";
            m["Her"] = "\u0415\u0451"; m["her"] = "\u0435\u0451";
            m["Their"] = "\u0418\u0445"; m["their"] = "\u0438\u0445";
            m["My"] = "\u041c\u043e\u0439"; m["my"] = "\u043c\u043e\u0439";
            // Objective
            m["Him"] = "\u0415\u0433\u043e"; m["him"] = "\u0435\u0433\u043e";
            m["Them"] = "\u0418\u0445"; m["them"] = "\u0438\u0445";
            m["Me"] = "\u041c\u0435\u043d\u044f"; m["me"] = "\u043c\u0435\u043d\u044f";
            // Contractions -> just the Russian pronoun
            m["I'm"] = "\u042f"; m["I've"] = "\u042f"; m["I'll"] = "\u042f";
            m["You're"] = "\u0412\u044b"; m["you're"] = "\u0432\u044b";
            m["You've"] = "\u0412\u044b"; m["you've"] = "\u0432\u044b";
            m["You'll"] = "\u0412\u044b"; m["you'll"] = "\u0432\u044b";
            m["He's"] = "\u041e\u043d"; m["he's"] = "\u043e\u043d";
            m["She's"] = "\u041e\u043d\u0430"; m["she's"] = "\u043e\u043d\u0430";
            m["He'll"] = "\u041e\u043d"; m["he'll"] = "\u043e\u043d";
            m["She'll"] = "\u041e\u043d\u0430"; m["she'll"] = "\u043e\u043d\u0430";
            m["They're"] = "\u041e\u043d\u0438"; m["they're"] = "\u043e\u043d\u0438";
            m["They've"] = "\u041e\u043d\u0438"; m["they've"] = "\u043e\u043d\u0438";
            m["They'll"] = "\u041e\u043d\u0438"; m["they'll"] = "\u043e\u043d\u0438";
            return m;
        }

        // --- Hardcoded English phrases from C# source ---
        // Full pattern: "X now considers Y a(n) Z" or "a[n]" → "X: Y теперь — Z"
        // Game emits both a(n) and a[n] depending on context — char-class covers both.
        private static readonly Regex rxConsidersFull = new Regex(
            @"(.+?) now considers (.+?) a[(\[]n[)\]] (.+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex rxNoLongerConsidersFull = new Regex(
            @"(.+?) no longer considers (.+?) a[(\[]n[)\]] (.+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex rxNowConsiders = new Regex(
            " now considers ", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex rxNoLongerConsiders = new Regex(
            " no longer considers ", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex rxAnParen = new Regex(
            @" a[(\[]n[)\]] ", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex rxNotices = new Regex(
            " notices something is wrong with ", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Full-line gains/loses patterns for early intercept (before length check)
        // Captures: G1=subject, G2=object (condition name)
        private static readonly Regex rxGainsFull = new Regex(
            @"(.+?) gains (.+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex rxLosesFull = new Regex(
            @"(.+?) loses (.+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // --- Fix "Вы" + 3rd person verb → 2nd person plural ---
        // Non-reflexive: "Вы принимает" → "Вы принимаете", "Вы слишком устал" → "Вы слишком устали"
        // Allow 0-2 intervening words (adverbs: слишком, не, уже, очень, etc.)
        private static readonly Regex rxVyVerb = new Regex(
            "(?<=[\u0412\u0432]\u044b\\s(?:[\u0430-\u044f\u0451\u0410-\u042f\u0401]+[,.]?\\s){0,5})(?!\u0438\u043c\u043c\u0443\u043d\u0438\u0442\u0435\u0442\\b|\u043f\u043d\u0435\u0432\u043c\u043e\u043d\u0438\u0442\\b)([\u0430-\u044f\u0451][\u0430-\u044f\u0451\u0410-\u042f\u0401]*[\u0435\u0438\u0451])\u0442\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // Reflexive: "Вы собирается" → "Вы собираетесь"
        private static readonly Regex rxVyVerbRefl = new Regex(
            "(?<=[\u0412\u0432]\u044b\\s(?:[\u0430-\u044f\u0451\u0410-\u042f\u0401]+[,.]?\\s){0,5})([\u0430-\u044f\u0451][\u0430-\u044f\u0451\u0410-\u042f\u0401]*[\u0435\u0438\u0451])\u0442\u0441\u044f\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // Past tense reflexive: "\u0412\u044b \u0437\u0430\u0440\u0430\u0437\u0438\u043b\u0441\u044f" -> "\u0412\u044b \u0437\u0430\u0440\u0430\u0437\u0438\u043b\u0438\u0441\u044c"
        private static readonly Regex rxVyVerbPastRefl = new Regex(
            "(?<=[\u0412\u0432]\u044b\\s(?:[\u0430-\u044f\u0451\u0410-\u042f\u0401]+[,.]?\\s){0,5})([\u0430-\u044f\u0451][\u0430-\u044f\u0451\u0410-\u042f\u0401]*)\u043b\u0441\u044f\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // Past tense: "\u0412\u044b \u0437\u0430\u043a\u043e\u043d\u0447\u0438\u043b" -> "\u0412\u044b \u0437\u0430\u043a\u043e\u043d\u0447\u0438\u043b\u0438"
        // Past tense: "Вы слишком устал" -> "Вы слишком устали"
        private static readonly Regex rxVyVerbPast = new Regex(
            "(?<=[\u0412\u0432]\u044b\\s(?:[\u0430-\u044f\u0451\u0410-\u042f\u0401]+[,.]?\\s){0,5})([\u0430-\u044f\u0451][\u0430-\u044f\u0451\u0410-\u042f\u0401]*)\u043b\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
                // Short participle: "Вы оглушён" -> "Вы оглушены"
        private static readonly Regex rxVyParticiple = new Regex(
            "(?<=[\u0412\u0432]\u044b\\s(?:[\u0430-\u044f\u0451\u0410-\u042f\u0401]+[,.]?\\s){0,3})([\u0430-\u044f\u0451][\u0430-\u044f\u0451\u0410-\u042f\u0401]*)[\u0451\u0435]\u043d\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // Irregular past tense: "Вы вырос" -> "Вы выросли", "Вы умер" -> "Вы умерли"
        private static readonly Regex rxVyIrregPast = new Regex(
            "(?<=[\u0412\u0432]\u044b\\s(?:[\u0430-\u044f\u0451\u0410-\u042f\u0401]+[,.]?\\s){0,5})(\u0432\u044b\u0440\u043e\u0441|\u0443\u043c\u0435\u0440|\u0437\u0430\u043c\u0451\u0440\u0437|\u043f\u043e\u0433\u0438\u0431|\u043f\u0440\u0438\u0432\u044b\u043a|\u0438\u0441\u0447\u0435\u0437|\u043f\u0440\u043e\u043c\u043e\u043a|\u0437\u0430\u0441\u043e\u0445|\u043e\u0441\u043b\u0435\u043f|\u043e\u0433\u043b\u043e\u0445)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // Subordinate clause markers: no false conjugation in relative clauses
        private static readonly Regex rxSubordClause = new Regex(
            "\\b\u043a\u043e\u0442\u043e\u0440(?:\u044b\u0439|\u0430\u044f|\u043e\u0435|\u044b\u0435|\u043e\u043c\u0443|\u043e\u0439|\u043e\u0433\u043e|\u044b\u0445|\u044b\u043c|\u044b\u043c\u0438)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // --- Genitive after prepositions for Capitalized nouns (game item names) ---
        // \u0438\u0437=из \u0434\u043b\u044f=для \u0434\u043e=до \u043e\u0442=от \u0431\u0435\u0437=без \u043f\u043e\u0441\u043b\u0435=после \u043e\u043a\u043e\u043b\u043e=около \u0432\u043e\u0437\u043b\u0435=возле
        // After genitive prepositions: Capitalized word ending in -а → genitive (-ы or -и)
        private static readonly Regex rxGenPrepA = new Regex(
            "(?<=(?:\u0438\u0437|\u0434\u043b\u044f|\u0434\u043e|\u043e\u0442|\u0431\u0435\u0437|\u043f\u043e\u0441\u043b\u0435|\u043e\u043a\u043e\u043b\u043e|\u0432\u043e\u0437\u043b\u0435)\\s)([\u0410-\u042f\u0401][\u0430-\u044f\u0451]+)([\u0430\u0410])(?=\\b)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // After genitive prepositions: Capitalized word ending in -я → genitive -и
        private static readonly Regex rxGenPrepYa = new Regex(
            "(?<=(?:\u0438\u0437|\u0434\u043b\u044f|\u0434\u043e|\u043e\u0442|\u0431\u0435\u0437|\u043f\u043e\u0441\u043b\u0435|\u043e\u043a\u043e\u043b\u043e|\u0432\u043e\u0437\u043b\u0435)\\s)([\u0410-\u042f\u0401][\u0430-\u044f\u0451]+)\u044f(?=\\b)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // After genitive prepositions: Capitalized word ending in -ь → genitive -и
        private static readonly Regex rxGenPrepSoft = new Regex(
            "(?<=(?:\u0438\u0437|\u0434\u043e|\u043e\u0442|\u0431\u0435\u0437)\\s)([\u0410-\u042f\u0401][\u0430-\u044f\u0451]+)\u044c(?=\\b)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Genitive evaluator: -а → -ы unless stem ends in г,к,х,ж,ш,ч,щ → -и
        private static string GenPrepAEvaluator(Match m)
        {
            string stem = m.Groups[1].Value;
            char lastStem = stem[stem.Length - 1];
            // \u0433=г \u043a=к \u0445=х \u0436=ж \u0448=ш \u0447=ч \u0449=щ
            if (lastStem == '\u0433' || lastStem == '\u043a' || lastStem == '\u0445' ||
                lastStem == '\u0436' || lastStem == '\u0448' || lastStem == '\u0447' || lastStem == '\u0449')
                return stem + "\u0438"; // -и
            return stem + "\u044b"; // -ы
        }

        // --- Accusative for Capitalized nouns after transitive verb forms ---
        // Matches: verb (3rd person -ет/-ёт/-ит/-ат/-ят + optional -ся) + space + Capitalized-а
        private static readonly Regex rxAccVerbA = new Regex(
            "(?<=[\u0435\u0438\u0451][\u0442](?:\u0441\u044f|\u0435)?\\s)([\u0410-\u042f\u0401][\u0430-\u044f\u0451]+)\u0430(?=[\\s,\\.!])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // --- Fix subjective pronouns after infinitive verbs → objective case ---
        // "ударить вы" → "ударить вас", "повалить он" → "повалить его"
        // Game uses [them] (subjective) where [them-obj] is needed; English "you"="you" hides it.
        private static readonly Regex rxInfObj = new Regex(
            "(?<=[\u0430-\u044f\u0451\u0410-\u042f\u0401]+(?:\u0442\u044c\u0441\u044f|\u0442\u044c|\u0447\u044c)\\s)([\u0412\u0432]\u044b|\u041e\u043d\u0430|\u043e\u043d\u0430|\u041e\u043d\u0438|\u043e\u043d\u0438|\u041e\u043d|\u043e\u043d)(?=[\\s.,!?:;]|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, string> infObjMap = InitInfObjMap();

        private static Dictionary<string, string> InitInfObjMap()
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            m["\u0432\u044b"] = "\u0432\u0430\u0441"; m["\u0412\u044b"] = "\u0412\u0430\u0441";
            m["\u043e\u043d"] = "\u0435\u0433\u043e"; m["\u041e\u043d"] = "\u0415\u0433\u043e";
            m["\u043e\u043d\u0430"] = "\u0435\u0451"; m["\u041e\u043d\u0430"] = "\u0415\u0451";
            m["\u043e\u043d\u0438"] = "\u0438\u0445"; m["\u041e\u043d\u0438"] = "\u0418\u0445";
            return m;
        }

        private static string InfObjEval(Match m)
        {
            string r;
            if (infObjMap.TryGetValue(m.Value, out r))
                return r;
            return m.Value;
        }

        // --- Fix "вы" after prepositions → correct case form ---
        // Game uses [them]/[us] (subjective) but prepositions require oblique cases.
        // Genitive/Accusative preps → вас: для,от,из,до,без,после,около,возле,против,у,на,в,через,про,о
        private static readonly Regex rxPrepGenVy = new Regex(
            "(?<=(?:^|[\\s.,!\"(])(?:\u0434\u043b\u044f|\u043e\u0442|\u0438\u0437|\u0434\u043e|\u0431\u0435\u0437|\u043f\u043e\u0441\u043b\u0435|\u043e\u043a\u043e\u043b\u043e|\u0432\u043e\u0437\u043b\u0435|\u043f\u0440\u043e\u0442\u0438\u0432|\u0443|\u043d\u0430|\u0432|\u0447\u0435\u0440\u0435\u0437|\u043f\u0440\u043e|\u043e)\\s)[\u0412\u0432]\u044b(?=[\\s.,!?:;]|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // Dative preps → вам: к,по
        private static readonly Regex rxPrepDatVy = new Regex(
            "(?<=(?:^|[\\s.,!\"(])(?:\u043a|\u043f\u043e)\\s)[\u0412\u0432]\u044b(?=[\\s.,!?:;]|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // Instrumental preps → вами: с,перед,между,над,под
        private static readonly Regex rxPrepInstrVy = new Regex(
            "(?<=(?:^|[\\s.,!\"(])(?:\u0441|\u043f\u0435\u0440\u0435\u0434|\u043c\u0435\u0436\u0434\u0443|\u043d\u0430\u0434|\u043f\u043e\u0434)\\s)[\u0412\u0432]\u044b(?=[\\s.,!?:;]|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        // Note: "за" skipped — ambiguous (instrumental "за вами" vs accusative "за вас")

        private static string PrepGenVyEval(Match m)
        {
            return m.Value[0] == '\u0412' ? "\u0412\u0430\u0441" : "\u0432\u0430\u0441";
        }
        private static string PrepDatVyEval(Match m)
        {
            return m.Value[0] == '\u0412' ? "\u0412\u0430\u043c" : "\u0432\u0430\u043c";
        }
        private static string PrepInstrVyEval(Match m)
        {
            return m.Value[0] == '\u0412' ? "\u0412\u0430\u043c\u0438" : "\u0432\u0430\u043c\u0438";
        }

        // Replace English "to" preposition between Cyrillic words
        private static readonly Regex rxCyrToPrep = new Regex(
            "(?<=[\u0430-\u044f\u0451\u0410-\u042f\u0401).\\]\u00bb0-9])\\s+to\\s+(?=[\u0430-\u044f\u0451\u0410-\u042f\u0401\\[(\u00ab])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // --- Subordinate clause protection for Vy-verb conjugation ---
        // --- Stat name translations dictionary (auto-generated, sorted by key length desc) ---
        private static readonly string[][] _statTranslations = new string[][] {
            new string[] { "StatSkillMedicalDiagnosticsProgress", "\u041F\u0440\u043E\u0433\u0440\u0435\u0441\u0441 \u043C\u0435\u0434. \u0434\u0438\u0430\u0433\u043D\u043E\u0441\u0442\u0438\u043A\u0438" },
            new string[] { "StatSkillScienceForensicProgress", "\u041F\u0440\u043E\u0433\u0440\u0435\u0441\u0441 \u043A\u0440\u0438\u043C\u0438\u043D\u0430\u043B\u0438\u0441\u0442\u0438\u043A\u0438" },
            new string[] { "StatComputerCPUArchitecture", "\u0410\u0440\u0445\u0438\u0442\u0435\u043A\u0442\u0443\u0440\u0430 CPU" },
            new string[] { "StatTrainingEngConstruction", "\u041D\u0430\u0432\u044B\u043A: \u0421\u0442\u0440\u043E\u0438\u0442\u0435\u043B\u044C\u0441\u0442\u0432\u043E" },
            new string[] { "StatCrimeVenusArrestWarning", "\u0423\u0433\u0440\u043E\u0437\u0430 \u0430\u0440\u0435\u0441\u0442\u0430 Venus" },
            new string[] { "StatFusionPelletFeederRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u043F\u043E\u0434\u0430\u0447\u0438 \u0442\u043E\u043F\u043B\u0438\u0432\u0430" },
            new string[] { "StatCrimeOKLGArrestWarning", "\u0423\u0433\u0440\u043E\u0437\u0430 \u0430\u0440\u0435\u0441\u0442\u0430 OKLG" },
            new string[] { "StatTrainingEngElectronic", "\u041D\u0430\u0432\u044B\u043A: \u042D\u043B\u0435\u043A\u0442\u0440\u043E\u043D\u0438\u043A\u0430" },
            new string[] { "StatTrainingEngMechanical", "\u041D\u0430\u0432\u044B\u043A: \u041C\u0435\u0445\u0430\u043D\u0438\u043A\u0430" },
            new string[] { "StatTrainingEngSpaceship", "\u041D\u0430\u0432\u044B\u043A: \u041A\u043E\u0441\u043C\u0438\u0447\u0435\u0441\u043A\u0438\u0439 \u043A\u043E\u0440\u0430\u0431\u043B\u044C" },
            new string[] { "StatTrainingMeleeUnarmed", "\u041D\u0430\u0432\u044B\u043A: \u0420\u0443\u043A\u043E\u043F\u0430\u0448\u043D\u044B\u0439 \u0431\u043E\u0439" },
            new string[] { "StatFusionLaserArrayRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u043B\u0430\u0437\u0435\u0440\u043D\u043E\u0439 \u0440\u0435\u0448\u0451\u0442\u043A\u0438" },
            new string[] { "StatComputerDataStorage", "\u0425\u0440\u0430\u043D\u0438\u043B\u0438\u0449\u0435 \u0434\u0430\u043D\u043D\u044B\u0445" },
            new string[] { "StatTrainingEngRobotics", "\u041D\u0430\u0432\u044B\u043A: \u0420\u043E\u0431\u043E\u0442\u043E\u0442\u0435\u0445\u043D\u0438\u043A\u0430" },
            new string[] { "StatTrainingEngSoftware", "\u041D\u0430\u0432\u044B\u043A: \u041F\u0440\u043E\u0433\u0440\u0430\u043C\u043C\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435" },
            new string[] { "StatThrustStrengthTurbo", "\u0421\u0438\u043B\u0430 \u0442\u044F\u0433\u0438 (\u0442\u0443\u0440\u0431\u043E)" },
            new string[] { "StatRecCurrentGameScore", "\u0422\u0435\u043A\u0443\u0449\u0438\u0439 \u0441\u0447\u0451\u0442" },
            new string[] { "StatTrainingMeleeArmed", "\u041D\u0430\u0432\u044B\u043A: \u0411\u043B\u0438\u0436\u043D\u0438\u0439 \u0431\u043E\u0439 (\u043E\u0440\u0443\u0436\u0438\u0435)" },
            new string[] { "StatTrainingOpsEnvSuit", "\u041D\u0430\u0432\u044B\u043A: \u0421\u043A\u0430\u0444\u0430\u043D\u0434\u0440" },
            new string[] { "StatFusionCryoPumpRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u043A\u0440\u0438\u043E\u043F\u043E\u043C\u043F\u044B" },
            new string[] { "StatInfectionHealRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u043B\u0435\u0447\u0435\u043D\u0438\u044F \u0438\u043D\u0444\u0435\u043A\u0446\u0438\u0438" },
            new string[] { "StatThreatIntelligent", "\u0423\u043C\u043D\u0430\u044F \u0443\u0433\u0440\u043E\u0437\u0430" },
            new string[] { "StatTrainingEngCombat", "\u041D\u0430\u0432\u044B\u043A: \u0411\u043E\u0435\u0432\u0430\u044F \u0438\u043D\u0436\u0435\u043D\u0435\u0440\u0438\u044F" },
            new string[] { "StatDismantleProgress", "\u041F\u0440\u043E\u0433\u0440\u0435\u0441\u0441 \u0440\u0430\u0437\u0431\u043E\u0440\u043A\u0438" },
            new string[] { "StatWoundFractureMin", "\u041C\u0438\u043D. \u043F\u0435\u0440\u0435\u043B\u043E\u043C" },
            new string[] { "StatSecurityHealRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u0432\u043E\u0441\u0441\u0442. \u0431\u0435\u0437\u043E\u043F\u0430\u0441\u043D\u043E\u0441\u0442\u0438" },
            new string[] { "StatComputerCPUCores", "\u042F\u0434\u0440\u0430 CPU" },
            new string[] { "StatTrainingEngCivil", "\u041D\u0430\u0432\u044B\u043A: \u0413\u0440\u0430\u0436\u0434. \u0438\u043D\u0436\u0435\u043D\u0435\u0440\u0438\u044F" },
            new string[] { "StatICThrustThrottle", "\u0414\u0440\u043E\u0441\u0441\u0435\u043B\u044C \u0442\u044F\u0433\u0438" },
            new string[] { "StatFatigueHealRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u043E\u0442\u0434\u044B\u0445\u0430" },
            new string[] { "StatTrainingHacking", "\u041D\u0430\u0432\u044B\u043A: \u0412\u0437\u043B\u043E\u043C" },
            new string[] { "StatICPellMaxTheory", "\u0422\u0435\u043E\u0440. \u043C\u0430\u043A\u0441. \u043F\u0435\u043B\u043B\u0435\u0442" },
            new string[] { "StatICReadyLasAlign", "\u041B\u0430\u0437\u0435\u0440 \u0432\u044B\u0440\u043E\u0432\u043D\u0435\u043D" },
            new string[] { "StatICReadyPellFeed", "\u041F\u043E\u0434\u0430\u0447\u0430 \u043F\u0435\u043B\u043B\u0435\u0442" },
            new string[] { "StatInstallRateHULL", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0438 (\u043A\u043E\u0440\u043F\u0443\u0441)" },
            new string[] { "StatInstallRateMISC", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u0443\u0441\u0442\u0430\u043D\u043E\u0432\u043A\u0438 (\u0440\u0430\u0437\u043D\u043E\u0435)" },
            new string[] { "StatTrainingFeeble", "\u0422\u0440\u0435\u043D\u0438\u0440\u043E\u0432\u043A\u0430: \u0421\u043B\u0430\u0431\u0430\u043A" },
            new string[] { "StatTrainingStrong", "\u0422\u0440\u0435\u043D\u0438\u0440\u043E\u0432\u043A\u0430: \u0421\u0438\u043B\u0430\u0447" },
            new string[] { "StatThrustStrength", "\u0421\u0438\u043B\u0430 \u0442\u044F\u0433\u0438" },
            new string[] { "StatBloodHealRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u0437\u0430\u0436\u0438\u0432\u043B\u0435\u043D\u0438\u044F" },
            new string[] { "StatHydrationRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u043E\u0431\u0435\u0437\u0432\u043E\u0436\u0438\u0432\u0430\u043D\u0438\u044F" },
            new string[] { "StatInfectionRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u0438\u043D\u0444\u0435\u043A\u0446\u0438\u0438" },
            new string[] { "StatWoundFraction", "\u041F\u0435\u0440\u0435\u043B\u043E\u043C\u044B" },
            new string[] { "StatWoundHealRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u0437\u0430\u0436\u0438\u0432\u043B\u0435\u043D\u0438\u044F \u0440\u0430\u043D" },
            new string[] { "StatTrainingAdmin", "\u041D\u0430\u0432\u044B\u043A: \u0410\u0434\u043C\u0438\u043D\u0438\u0441\u0442\u0440\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435" },
            new string[] { "StatTrainingUnfit", "\u0422\u0440\u0435\u043D\u0438\u0440\u043E\u0432\u043A\u0430: \u041D\u0435 \u0432 \u0444\u043E\u0440\u043C\u0435" },
            new string[] { "StatPlacesVisited", "\u041C\u0435\u0441\u0442\u0430 \u043F\u043E\u0441\u0435\u0449\u0435\u043D\u044B" },
            new string[] { "StatFatigueCoeff", "\u041A\u043E\u044D\u0444\u0444. \u0443\u0441\u0442\u0430\u043B\u043E\u0441\u0442\u0438" },
            new string[] { "StatSleepComfort", "\u041A\u043E\u043C\u0444\u043E\u0440\u0442 \u0441\u043D\u0430" },
            new string[] { "StatComputerNRAM", "NRAM" },
            new string[] { "StatRecHighScore", "\u0420\u0435\u043A\u043E\u0440\u0434" },
            new string[] { "StatNicotineUsed", "\u041D\u0438\u043A\u043E\u0442\u0438\u043D" },
            new string[] { "StatCannabisUsed", "\u041A\u0430\u043D\u043D\u0430\u0431\u0438\u0441" },
            new string[] { "StatEncumbrance", "\u041D\u0430\u0433\u0440\u0443\u0437\u043A\u0430" },
            new string[] { "StatHygieneRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u0437\u0430\u0433\u0440\u044F\u0437\u043D\u0435\u043D\u0438\u044F" },
            new string[] { "StatAtrophyRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u0430\u0442\u0440\u043E\u0444\u0438\u0438" },
            new string[] { "StatAchievement", "\u0414\u043E\u0441\u0442\u0438\u0436\u0435\u043D\u0438\u044F" },
            new string[] { "StatSelfRespect", "\u0421\u0430\u043C\u043E\u0443\u0432\u0430\u0436\u0435\u043D\u0438\u0435" },
            new string[] { "StatH2SO4Poison", "\u041E\u0442\u0440\u0430\u0432\u043B\u0435\u043D\u0438\u0435 H2SO4" },
            new string[] { "StatGasPressure", "\u0414\u0430\u0432\u043B\u0435\u043D\u0438\u0435 \u0433\u0430\u0437\u0430" },
            new string[] { "StatGasMolH2SO4", "\u041C\u043E\u043B\u0435\u043A\u0443\u043B\u044B H2SO4" },
            new string[] { "StatTrainingFit", "\u0422\u0440\u0435\u043D\u0438\u0440\u043E\u0432\u043A\u0430: \u0412 \u0444\u043E\u0440\u043C\u0435" },
            new string[] { "StatICPressureA", "\u0414\u0430\u0432\u043B\u0435\u043D\u0438\u0435 A" },
            new string[] { "StatICPwrThrust", "\u041C\u043E\u0449\u043D\u043E\u0441\u0442\u044C \u0442\u044F\u0433\u0438" },
            new string[] { "StatBloodDrink", "\u041F\u0438\u0442\u044C\u0451" },
            new string[] { "StatWoundBlunt", "\u0423\u0448\u0438\u0431\u044B" },
            new string[] { "StatArmorBlunt", "\u0411\u0440\u043E\u043D\u044F (\u0443\u0434\u0430\u0440)" },
            new string[] { "StatGasPpH2SO4", "\u041F\u0430\u0440\u0446.\u0434\u0430\u0432\u043B. H2SO4" },
            new string[] { "StatPopulation", "\u041D\u0430\u0441\u0435\u043B\u0435\u043D\u0438\u0435" },
            new string[] { "StatICCoreTemp", "\u0422\u0435\u043C\u043F\u0435\u0440\u0430\u0442\u0443\u0440\u0430 \u044F\u0434\u0440\u0430" },
            new string[] { "StatICCryoMult", "\u041A\u0440\u0438\u043E-\u043C\u043D\u043E\u0436\u0438\u0442\u0435\u043B\u044C" },
            new string[] { "StatICPellRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u043F\u043E\u0434\u0430\u0447\u0438 \u043F\u0435\u043B\u043B\u0435\u0442" },
            new string[] { "StatICPwrTotal", "\u041E\u0431\u0449\u0430\u044F \u043C\u043E\u0449\u043D\u043E\u0441\u0442\u044C" },
            new string[] { "StatBloodRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u043A\u0440\u043E\u0432\u043E\u0442\u0435\u0447\u0435\u043D\u0438\u044F" },
            new string[] { "StatHydration", "\u0413\u0438\u0434\u0440\u0430\u0442\u0430\u0446\u0438\u044F" },
            new string[] { "StatInfection", "\u0418\u043D\u0444\u0435\u043A\u0446\u0438\u044F" },
            new string[] { "StatFightFear", "\u0421\u0442\u0440\u0430\u0445 \u0431\u043E\u044F" },
            new string[] { "StatCO2Poison", "\u041E\u0442\u0440\u0430\u0432\u043B\u0435\u043D\u0438\u0435 CO2" },
            new string[] { "StatNH3Poison", "\u041E\u0442\u0440\u0430\u0432\u043B\u0435\u043D\u0438\u0435 NH3" },
            new string[] { "StatGasMolCH4", "\u041C\u043E\u043B\u0435\u043A\u0443\u043B\u044B CH4" },
            new string[] { "StatGasMolCO2", "\u041C\u043E\u043B\u0435\u043A\u0443\u043B\u044B CO2" },
            new string[] { "StatGasMolNH3", "\u041C\u043E\u043B\u0435\u043A\u0443\u043B\u044B NH3" },
            new string[] { "StatICHe3Rate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C He3" },
            new string[] { "StatICPellMax", "\u041C\u0430\u043A\u0441. \u043F\u0435\u043B\u043B\u0435\u0442" },
            new string[] { "StatICPwrLoad", "\u041D\u0430\u0433\u0440\u0443\u0437\u043A\u0430 \u043C\u043E\u0449\u043D\u043E\u0441\u0442\u0438" },
            new string[] { "StatICABLWall", "\u0421\u0442\u0435\u043D\u043A\u0430 ABL" },
            new string[] { "StatSolidTemp", "\u0422\u0435\u043C\u043F\u0435\u0440\u0430\u0442\u0443\u0440\u0430 \u0442\u0435\u043B\u0430" },
            new string[] { "StatPowerMax", "\u041C\u0430\u043A\u0441. \u043C\u043E\u0449\u043D\u043E\u0441\u0442\u044C" },
            new string[] { "StatFoodRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u043F\u043E\u0442\u0440\u0435\u0431\u043B\u0435\u043D\u0438\u044F \u0435\u0434\u044B" },
            new string[] { "StatDefecate", "\u0414\u0435\u0444\u0435\u043A\u0430\u0446\u0438\u044F" },
            new string[] { "StatMeatFuel", "\u041A\u0430\u043B\u043E\u0440\u0438\u0438" },
            new string[] { "StatWoundCut", "\u041F\u043E\u0440\u0435\u0437\u044B" },
            new string[] { "StatArmorCut", "\u0411\u0440\u043E\u043D\u044F (\u043F\u043E\u0440\u0435\u0437)" },
            new string[] { "StatAltruism", "\u0410\u043B\u044C\u0442\u0440\u0443\u0438\u0437\u043C" },
            new string[] { "StatAutonomy", "\u0410\u0432\u0442\u043E\u043D\u043E\u043C\u043D\u043E\u0441\u0442\u044C" },
            new string[] { "StatIntimacy", "\u0411\u043B\u0438\u0437\u043E\u0441\u0442\u044C" },
            new string[] { "StatSecurity", "\u0411\u0435\u0437\u043E\u043F\u0430\u0441\u043D\u043E\u0441\u0442\u044C" },
            new string[] { "StatGasMolN2", "\u041C\u043E\u043B\u0435\u043A\u0443\u043B\u044B N2" },
            new string[] { "StatGasMolO2", "\u041C\u043E\u043B\u0435\u043A\u0443\u043B\u044B O2" },
            new string[] { "StatGasPpCH4", "\u041F\u0430\u0440\u0446.\u0434\u0430\u0432\u043B. CH4" },
            new string[] { "StatGasPpCO2", "\u041F\u0430\u0440\u0446.\u0434\u0430\u0432\u043B. CO2" },
            new string[] { "StatGasPpNH3", "\u041F\u0430\u0440\u0446.\u0434\u0430\u0432\u043B. NH3" },
            new string[] { "StatPiloting", "\u041F\u0438\u043B\u043E\u0442\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435" },
            new string[] { "StatICPwrFus", "\u041C\u043E\u0449\u043D\u043E\u0441\u0442\u044C \u0442\u0435\u0440\u043C\u043E\u044F\u0434\u0435\u0440\u043D\u043E\u0433\u043E" },
            new string[] { "StatICPwrMHD", "\u041C\u043E\u0449\u043D\u043E\u0441\u0442\u044C MHD" },
            new string[] { "StatSolidHe3", "\u0422\u0432\u0451\u0440\u0434\u044B\u0439 He3" },
            new string[] { "StatFatigue", "\u0423\u0441\u0442\u0430\u043B\u043E\u0441\u0442\u044C" },
            new string[] { "StatHygiene", "\u0413\u0438\u0433\u0438\u0435\u043D\u0430" },
            new string[] { "StatAtrophy", "\u0410\u0442\u0440\u043E\u0444\u0438\u044F" },
            new string[] { "StatSatiety", "\u0421\u044B\u0442\u043E\u0441\u0442\u044C" },
            new string[] { "StatDefense", "\u0417\u0430\u0449\u0438\u0442\u0430" },
            new string[] { "StatContact", "\u041A\u043E\u043D\u0442\u0430\u043A\u0442" },
            new string[] { "StatMeaning", "\u0421\u043C\u044B\u0441\u043B \u0436\u0438\u0437\u043D\u0438" },
            new string[] { "StatPrivacy", "\u041F\u0440\u0438\u0432\u0430\u0442\u043D\u043E\u0441\u0442\u044C" },
            new string[] { "StatGasTemp", "\u0422\u0435\u043C\u043F\u0435\u0440\u0430\u0442\u0443\u0440\u0430 \u0433\u0430\u0437\u0430" },
            new string[] { "StatGasPpN2", "\u041F\u0430\u0440\u0446.\u0434\u0430\u0432\u043B. N2" },
            new string[] { "StatGasPpO2", "\u041F\u0430\u0440\u0446.\u0434\u0430\u0432\u043B. O2" },
            new string[] { "StatICDRate", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C D" },
            new string[] { "StatDamage", "\u041F\u043E\u0432\u0440\u0435\u0436\u0434\u0435\u043D\u0438\u0435" },
            new string[] { "StatOxygen", "\u041A\u0438\u0441\u043B\u043E\u0440\u043E\u0434" },
            new string[] { "StatThreat", "\u0423\u0433\u0440\u043E\u0437\u0430" },
            new string[] { "StatEsteem", "\u0423\u0432\u0430\u0436\u0435\u043D\u0438\u0435" },
            new string[] { "StatFamily", "\u0421\u0435\u043C\u044C\u044F" },
            new string[] { "StatPoison", "\u041E\u0442\u0440\u0430\u0432\u043B\u0435\u043D\u0438\u0435" },
            new string[] { "StatICCapA", "\u0401\u043C\u043A\u043E\u0441\u0442\u044C A" },
            new string[] { "StatLiqD2O", "\u0422\u044F\u0436\u0451\u043B\u0430\u044F \u0432\u043E\u0434\u0430 (D2O)" },
            new string[] { "StatPower", "\u041C\u043E\u0449\u043D\u043E\u0441\u0442\u044C" },
            new string[] { "StatBlood", "\u041A\u0440\u043E\u0432\u044C" },
            new string[] { "StatSleep", "\u0421\u043E\u043D" },
            new string[] { "StatDrunk", "\u041E\u043F\u044C\u044F\u043D\u0435\u043D\u0438\u0435" },
            new string[] { "StatWeird", "\u0421\u0442\u0440\u0430\u043D\u043D\u043E\u0441\u0442\u044C" },
            new string[] { "StatTimer", "\u0422\u0430\u0439\u043C\u0435\u0440" },
            new string[] { "StatLiqHe", "\u0416\u0438\u0434\u043A\u0438\u0439 \u0433\u0435\u043B\u0438\u0439" },
            new string[] { "StatMass", "\u041C\u0430\u0441\u0441\u0430" },
            new string[] { "StatFood", "\u0415\u0434\u0430" },
            new string[] { "StatPain", "\u0411\u043E\u043B\u044C" },
            new string[] { "StatGrav", "\u0413\u0440\u0430\u0432\u0438\u0442\u0430\u0446\u0438\u044F" },
            new string[] { "StatICVe", "\u0421\u043A\u043E\u0440\u043E\u0441\u0442\u044C \u0438\u0441\u0442\u0435\u0447\u0435\u043D\u0438\u044F" },
            new string[] { "StatAge", "\u0412\u043E\u0437\u0440\u0430\u0441\u0442" },
            new string[] { "StatRad", "\u0420\u0430\u0434\u0438\u0430\u0446\u0438\u044F" },
        };

        // --- Hardcoded C# phrases that appear in mixed English/Russian UI text ---
        private static string[][] phraseReplacements = new string[][] {
            // Fix literal Unicode escape sequences in UI text
            new string[] { "\\u2022", "\u2022" },
            // Item stats labels (Condition: 100.00%, Mass: etc.)
            new string[] { "Condition: ", "\u0421\u043e\u0441\u0442\u043e\u044f\u043d\u0438\u0435: " },
            new string[] { "Condition:", "\u0421\u043e\u0441\u0442\u043e\u044f\u043d\u0438\u0435:" },
            // Shift header
            new string[] { ", Active Shift: Work", ", \u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u0420\u0430\u0431\u043e\u0442\u0430" },
            new string[] { ", Active Shift: Rest", ", \u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u041e\u0442\u0434\u044b\u0445" },
            new string[] { ", Active Shift: Free", ", \u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u0421\u0432\u043e\u0431\u043e\u0434\u043d\u0430\u044f" },
            new string[] { ", Active Shift: Custom", ", \u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u041e\u0441\u043e\u0431\u0430\u044f" },
            // Standalone Active Shift (BEFORE generic prefix to prevent preemption)
            new string[] { "Active Shift: Work", "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u0420\u0430\u0431\u043e\u0442\u0430" },
            new string[] { "Active Shift: Rest", "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u041e\u0442\u0434\u044b\u0445" },
            new string[] { "Active Shift: Free", "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u0421\u0432\u043e\u0431\u043e\u0434\u043d\u0430\u044f" },
            new string[] { "Active Shift: Custom", "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u041e\u0441\u043e\u0431\u0430\u044f" },
            new string[] { "Active Shift: ", "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: " },
            new string[] { "Active Shift:", "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430:" },
            // Mixed text: game assembles translated prefix + untranslated English shift type
            new string[] { "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: Work", "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u0420\u0430\u0431\u043e\u0442\u0430" },
            new string[] { "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: Rest", "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u041e\u0442\u0434\u044b\u0445" },
            new string[] { "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: Free", "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u0421\u0432\u043e\u0431\u043e\u0434\u043d\u0430\u044f" },
            new string[] { "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: Custom", "\u0410\u043a\u0442\u0438\u0432\u043d\u0430\u044f \u0441\u043c\u0435\u043d\u0430: \u041e\u0441\u043e\u0431\u0430\u044f" },
            // Career/role names in header context ", Role," or ", Role " 
            new string[] { ", Captain,", ", \u041a\u0430\u043f\u0438\u0442\u0430\u043d," },
            new string[] { ", Captain ", ", \u041a\u0430\u043f\u0438\u0442\u0430\u043d " },
            new string[] { ", Shipbreaker,", ", \u041a\u043e\u0440\u0430\u0431\u043b\u0435\u0440\u0430\u0437\u0431\u043e\u0440\u0449\u0438\u043a," },
            new string[] { ", Shipbreaker ", ", \u041a\u043e\u0440\u0430\u0431\u043b\u0435\u0440\u0430\u0437\u0431\u043e\u0440\u0449\u0438\u043a " },
            new string[] { ", Prisoner,", ", \u0417\u0430\u043a\u043b\u044e\u0447\u0451\u043d\u043d\u044b\u0439," },
            new string[] { ", Prisoner ", ", \u0417\u0430\u043a\u043b\u044e\u0447\u0451\u043d\u043d\u044b\u0439 " },
            new string[] { ", Bartender,", ", \u0411\u0430\u0440\u043c\u0435\u043d," },
            new string[] { ", Bartender ", ", \u0411\u0430\u0440\u043c\u0435\u043d " },
            new string[] { ", Criminal,", ", \u041f\u0440\u0435\u0441\u0442\u0443\u043f\u043d\u0438\u043a," },
            new string[] { ", Criminal ", ", \u041f\u0440\u0435\u0441\u0442\u0443\u043f\u043d\u0438\u043a " },
            new string[] { ", Law Enforcement Officer,", ", \u041e\u0444\u0438\u0446\u0435\u0440 \u043f\u0440\u0430\u0432\u043e\u043f\u043e\u0440\u044f\u0434\u043a\u0430," },
            new string[] { ", Law Enforcement Officer ", ", \u041e\u0444\u0438\u0446\u0435\u0440 \u043f\u0440\u0430\u0432\u043e\u043f\u043e\u0440\u044f\u0434\u043a\u0430 " },
            new string[] { ", Manager,", ", \u041c\u0435\u043d\u0435\u0434\u0436\u0435\u0440," },
            new string[] { ", Manager ", ", \u041c\u0435\u043d\u0435\u0434\u0436\u0435\u0440 " },
            new string[] { ", Pirate,", ", \u041f\u0438\u0440\u0430\u0442," },
            new string[] { ", Pirate ", ", \u041f\u0438\u0440\u0430\u0442 " },
            new string[] { ", Influencer,", ", \u0418\u043d\u0444\u043b\u044e\u0435\u043d\u0441\u0435\u0440," },
            new string[] { ", Influencer ", ", \u0418\u043d\u0444\u043b\u044e\u0435\u043d\u0441\u0435\u0440 " },
            new string[] { ", Scientist,", ", \u0423\u0447\u0451\u043d\u044b\u0439," },
            new string[] { ", Scientist ", ", \u0423\u0447\u0451\u043d\u044b\u0439 " },
            new string[] { ", Technician,", ", \u0422\u0435\u0445\u043d\u0438\u043a," },
            new string[] { ", Technician ", ", \u0422\u0435\u0445\u043d\u0438\u043a " },
            new string[] { ", Engineer,", ", \u0418\u043d\u0436\u0435\u043d\u0435\u0440," },
            new string[] { ", Engineer ", ", \u0418\u043d\u0436\u0435\u043d\u0435\u0440 " },
            new string[] { ", Mechanic,", ", \u041c\u0435\u0445\u0430\u043d\u0438\u043a," },
            new string[] { ", Mechanic ", ", \u041c\u0435\u0445\u0430\u043d\u0438\u043a " },
            new string[] { ", Medic,", ", \u041c\u0435\u0434\u0438\u043a," },
            new string[] { ", Medic ", ", \u041c\u0435\u0434\u0438\u043a " },
            new string[] { ", Pilot,", ", \u041f\u0438\u043b\u043e\u0442," },
            new string[] { ", Pilot ", ", \u041f\u0438\u043b\u043e\u0442 " },
            new string[] { ", Electrician,", ", \u042d\u043b\u0435\u043a\u0442\u0440\u0438\u043a," },
            new string[] { ", Electrician ", ", \u042d\u043b\u0435\u043a\u0442\u0440\u0438\u043a " },
            new string[] { ", Hacker,", ", \u0425\u0430\u043a\u0435\u0440," },
            new string[] { ", Hacker ", ", \u0425\u0430\u043a\u0435\u0440 " },
            new string[] { ", Smuggler,", ", \u041a\u043e\u043d\u0442\u0440\u0430\u0431\u0430\u043d\u0434\u0438\u0441\u0442," },
            new string[] { ", Smuggler ", ", \u041a\u043e\u043d\u0442\u0440\u0430\u0431\u0430\u043d\u0434\u0438\u0441\u0442 " },
            new string[] { ", Fixer,", ", \u0424\u0438\u043a\u0441\u0435\u0440," },
            new string[] { ", Fixer ", ", \u0424\u0438\u043a\u0441\u0435\u0440 " },
            // --- Hardcoded English from C# code ---
            // GUIHire.cs / Hire.cs / JsonCompany.cs
            new string[] { " is now a member of ", " \u0442\u0435\u043f\u0435\u0440\u044c \u0447\u043b\u0435\u043d " },
            // Layer 5 (mixed text): rxAAn strips "a " before "member" — need the article-less form too
            new string[] { " is now member of ", " \u0442\u0435\u043f\u0435\u0440\u044c \u0447\u043b\u0435\u043d " },
            new string[] { " no longer a member of ", " \u0431\u043e\u043b\u044c\u0448\u0435 \u043d\u0435 \u0447\u043b\u0435\u043d " },
            // Layer 5 (mixed text): rxAAn strips "a " before "member" — need the article-less form too
            new string[] { " no longer member of ", " \u0431\u043e\u043b\u044c\u0448\u0435 \u043d\u0435 \u0447\u043b\u0435\u043d " },
            // JsonCompany.NullShift — the "no shift assigned" sentinel value shown in notifications
            new string[] { "Null Shift", "\u041d\u0443\u043b\u0435\u0432\u0430\u044f \u0441\u043c\u0435\u043d\u0430" },
            // Action-log hotfixes (Qwen audit): most frequent mixed EN/RU fragments
            new string[] { "\u043a\u043e\u0440\u0430\u0431\u043b\u044cco", "\u043a\u043e\u0440\u0430\u0431\u043b\u044c \u0441\u043e" },
            new string[] { "is turned on IR sensor", "\u0437\u0430\u0441\u0435\u0447\u0451\u043d \u0418\u041a-\u0434\u0430\u0442\u0447\u0438\u043a\u043e\u043c" },
            new string[] { "is turned on EM sensor", "\u0437\u0430\u0441\u0435\u0447\u0451\u043d \u042d\u041c-\u0434\u0430\u0442\u0447\u0438\u043a\u043e\u043c" },
            new string[] { "is turned on Optical sensor", "\u0437\u0430\u0441\u0435\u0447\u0451\u043d \u043e\u043f\u0442\u0438\u0447\u0435\u0441\u043a\u0438\u043c \u0434\u0430\u0442\u0447\u0438\u043a\u043e\u043c" },
            new string[] { "'s armor with a swing", " \u0431\u0440\u043e\u043d\u044e \u0443\u0434\u0430\u0440\u043e\u043c" },
            new string[] { "'s armor with a held object", " \u0431\u0440\u043e\u043d\u044e \u043f\u0440\u0435\u0434\u043c\u0435\u0442\u043e\u043c \u0432 \u0440\u0443\u043a\u0435" },
            new string[] { "barely affected ", "\u0435\u0434\u0432\u0430 \u043f\u043e\u0446\u0430\u0440\u0430\u043f\u0430\u043b " },
            new string[] { "has never been broken before", "\u043d\u0438\u043a\u043e\u0433\u0434\u0430 \u0440\u0430\u043d\u0435\u0435 \u043d\u0435 \u043b\u043e\u043c\u0430\u043b\u0441\u044f" },
            new string[] { "greater resilience and value", "\u043f\u043e\u0432\u044b\u0448\u0435\u043d\u043d\u0443\u044e \u043f\u0440\u043e\u0447\u043d\u043e\u0441\u0442\u044c \u0438 \u0446\u0435\u043d\u043d\u043e\u0441\u0442\u044c" },
            new string[] { "secondary airlock", "\u0432\u0442\u043e\u0440\u0438\u0447\u043d\u044b\u0439 \u0448\u043b\u044e\u0437" },
            new string[] { "changed their view of ", "\u0438\u0437\u043c\u0435\u043d\u0438\u043b\u0438 \u043c\u043d\u0435\u043d\u0438\u0435 \u043e " },
            new string[] { " is external ship fixture.", " \u2014 \u0432\u043d\u0435\u0448\u043d\u0435\u0435 \u043a\u0440\u0435\u043f\u043b\u0435\u043d\u0438\u0435 \u043a\u043e\u0440\u0430\u0431\u043b\u044f." },
            new string[] { "out of range", "\u0432\u043d\u0435 \u0434\u043e\u0441\u044f\u0433\u0430\u0435\u043c\u043e\u0441\u0442\u0438" },
            // Powered.cs
            new string[] { " no longer has power!", " \u043e\u0431\u0435\u0441\u0442\u043e\u0447\u0435\u043d!" },
            // AIShipManager.cs
            new string[] { "Ferry service has arrived!", "\u041f\u0430\u0440\u043e\u043c \u043f\u0440\u0438\u0431\u044b\u043b!" },
            new string[] { " Local Authority ship scan activity detected.", " \u041e\u0431\u043d\u0430\u0440\u0443\u0436\u0435\u043d\u043e \u0441\u043a\u0430\u043d\u0438\u0440\u043e\u0432\u0430\u043d\u0438\u0435 \u043a\u043e\u0440\u0430\u0431\u043b\u044f \u043c\u0435\u0441\u0442\u043d\u044b\u043c\u0438 \u0432\u043b\u0430\u0441\u0442\u044f\u043c\u0438." },
            // CrewSim.cs
            new string[] { "Welcome back, Captain.", "\u0421 \u0432\u043e\u0437\u0432\u0440\u0430\u0449\u0435\u043d\u0438\u0435\u043c, \u041a\u0430\u043f\u0438\u0442\u0430\u043d." },
            new string[] { "Welcome, Captain.", "\u0414\u043e\u0431\u0440\u043e \u043f\u043e\u0436\u0430\u043b\u043e\u0432\u0430\u0442\u044c, \u041a\u0430\u043f\u0438\u0442\u0430\u043d." },
            // GUIComputer.cs / GUIComputer2.cs
            new string[] { "Welcome, ", "\u0414\u043e\u0431\u0440\u043e \u043f\u043e\u0436\u0430\u043b\u043e\u0432\u0430\u0442\u044c, " },
            // Hire.cs / Quit.cs (Ledger descriptions)
            new string[] { "Sign-on Bonus", "\u0411\u043e\u043d\u0443\u0441 \u0437\u0430 \u0432\u0441\u0442\u0443\u043f\u043b\u0435\u043d\u0438\u0435" },
            // "New ..." forms MUST come BEFORE their base terms to prevent substring preemption
            new string[] { "New Death Pay: $", "\u041d\u043e\u0432\u0430\u044f \u043f\u043e\u0441\u043c\u0435\u0440\u0442\u043d\u0430\u044f \u0432\u044b\u043f\u043b\u0430\u0442\u0430: $" },
            new string[] { "New Salary/day: $", "\u041d\u043e\u0432\u0430\u044f \u0437\u0430\u0440\u043f\u043b\u0430\u0442\u0430/\u0434\u0435\u043d\u044c: $" },
            new string[] { "Death Pay Adjustment", "\u041a\u043e\u0440\u0440\u0435\u043a\u0442\u0438\u0440\u043e\u0432\u043a\u0430 \u043f\u043e\u0441\u043c\u0435\u0440\u0442\u043d\u043e\u0439 \u0432\u044b\u043f\u043b\u0430\u0442\u044b" },
            new string[] { "Death Pay", "\u041f\u043e\u0441\u043c\u0435\u0440\u0442\u043d\u0430\u044f \u0432\u044b\u043f\u043b\u0430\u0442\u0430" },
            new string[] { "Salary/day: $", "\u0417\u0430\u0440\u043f\u043b\u0430\u0442\u0430/\u0434\u0435\u043d\u044c: $" },
            new string[] { "Salary", "\u0417\u0430\u0440\u043f\u043b\u0430\u0442\u0430" },
            // CondOwner.cs condition removal (are → are no longer)
            new string[] { " are no longer ", " \u0431\u043e\u043b\u044c\u0448\u0435 \u043d\u0435 " },
            new string[] { " are ", " " },
            // "Loading ... from ..." MUST come BEFORE generic " from " to prevent preemption
            new string[] { "Loading new ships from JSONs", "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u043d\u043e\u0432\u044b\u0445 \u043a\u043e\u0440\u0430\u0431\u043b\u0435\u0439" },
            // Preposition "from" in social connections (e.g., "Shipbreaker from Location")
            new string[] { " from ", " \u0438\u0437 " },
            // Chargen social connection "New" prefix & ship transponder
            new string[] { "New Ship Transponder: ", "\u041d\u043e\u0432\u044b\u0439 \u0442\u0440\u0430\u043d\u0441\u043f\u043e\u043d\u0434\u0435\u0440: " },
            // Specific "New ..." entries with correct gender BEFORE generic fallback
            new string[] { "New NAV Message Received", "\u041f\u043e\u043b\u0443\u0447\u0435\u043d\u043e \u043d\u043e\u0432\u043e\u0435 \u0441\u043e\u043e\u0431\u0449\u0435\u043d\u0438\u0435 \u041d\u0410\u0412" },
            new string[] { "New Sector Time: ", "\u0412\u0440\u0435\u043c\u044f \u043d\u043e\u0432\u043e\u0433\u043e \u0441\u0435\u043a\u0442\u043e\u0440\u0430: " },
            new string[] { "New Personal Best: ", "\u041d\u043e\u0432\u044b\u0439 \u043b\u0438\u0447\u043d\u044b\u0439 \u0440\u0435\u043a\u043e\u0440\u0434: " },
            new string[] { "New Lap Time: ", "\u041d\u043e\u0432\u043e\u0435 \u0432\u0440\u0435\u043c\u044f \u043a\u0440\u0443\u0433\u0430: " },
            new string[] { "New rate:", "\u041d\u043e\u0432\u044b\u0439 \u043c\u043d\u043e\u0436.:" },
            new string[] { "New Body Type", "\u041d\u043e\u0432\u044b\u0439 \u0442\u0438\u043f \u0442\u0435\u043b\u0430" },
            new string[] { "New Venus Music", "\u041d\u043e\u0432\u0430\u044f \u043c\u0443\u0437\u044b\u043a\u0430 \u0412\u0435\u043d\u0435\u0440\u044b" },
            // Generic fallback (without colon)
            new string[] { "New ", "\u041d\u043e\u0432. " },
            // Save version warnings
            new string[] { "Warning: This Save File uses an old format (v", "\u0412\u043d\u0438\u043c\u0430\u043d\u0438\u0435: \u044d\u0442\u043e \u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u0435 \u0438\u0441\u043f\u043e\u043b\u044c\u0437\u0443\u0435\u0442 \u0441\u0442\u0430\u0440\u044b\u0439 \u0444\u043e\u0440\u043c\u0430\u0442 (v" },
            new string[] { ") which may cause problems.", "), \u0447\u0442\u043e \u043c\u043e\u0436\u0435\u0442 \u0432\u044b\u0437\u0432\u0430\u0442\u044c \u043f\u0440\u043e\u0431\u043b\u0435\u043c\u044b." },
            new string[] { "For best results, you should begin a new game", "\u0414\u043b\u044f \u043b\u0443\u0447\u0448\u0438\u0445 \u0440\u0435\u0437\u0443\u043b\u044c\u0442\u0430\u0442\u043e\u0432 \u043d\u0430\u0447\u043d\u0438\u0442\u0435 \u043d\u043e\u0432\u0443\u044e \u0438\u0433\u0440\u0443" },
            // GUIChargenCareer.cs
            new string[] { "Total cost cannot be negative", "\u041e\u0431\u0449\u0430\u044f \u0441\u0442\u043e\u0438\u043c\u043e\u0441\u0442\u044c \u043d\u0435 \u043c\u043e\u0436\u0435\u0442 \u0431\u044b\u0442\u044c \u043e\u0442\u0440\u0438\u0446\u0430\u0442\u0435\u043b\u044c\u043d\u043e\u0439" },
            // Debug commands (rare but visible)
            new string[] { "**Debug Commands Have Been Disabled**", "**\u041e\u0442\u043b\u0430\u0434\u043e\u0447\u043d\u044b\u0435 \u043a\u043e\u043c\u0430\u043d\u0434\u044b \u043e\u0442\u043a\u043b\u044e\u0447\u0435\u043d\u044b**" },
            new string[] { "**Debug Commands Have Been Activated**", "**\u041e\u0442\u043b\u0430\u0434\u043e\u0447\u043d\u044b\u0435 \u043a\u043e\u043c\u0430\u043d\u0434\u044b \u0430\u043a\u0442\u0438\u0432\u0438\u0440\u043e\u0432\u0430\u043d\u044b**" },
            // --- Ship info panel labels (hardcoded in GUIShip / GUIChargenCareer) ---
            new string[] { "Date of Construction: ", "\u0414\u0430\u0442\u0430 \u043f\u0440\u043e\u0438\u0437\u0432\u043e\u0434\u0441\u0442\u0432\u0430: " },  // Дата производства: (MUST be before "Construction: ")
            new string[] { "Construction: ", "\u041f\u0440\u043e\u0438\u0437\u0432\u043e\u0434\u0441\u0442\u0432\u043e: " },
            new string[] { "Vessel Name: ", "\u041d\u0430\u0437\u0432\u0430\u043d\u0438\u0435 \u0441\u0443\u0434\u043d\u0430: " },  // Название судна:
            new string[] { "REGID: ", "\u0420\u0435\u0433. \u043a\u043e\u0434: " },  // Рег. код:
            new string[] { "Total Mass: ", "\u041e\u0431\u0449\u0430\u044f \u043c\u0430\u0441\u0441\u0430: " },
            new string[] { "Make: ", "\u041c\u0430\u0440\u043a\u0430: " },
            new string[] { "Model: ", "\u041c\u043e\u0434\u0435\u043b\u044c: " },
            new string[] { "Homeport: ", "\u041f\u043e\u0440\u0442 \u043f\u0440\u0438\u043f\u0438\u0441\u043a\u0438: " },
            new string[] { "Designation: ", "\u041e\u0431\u043e\u0437\u043d\u0430\u0447\u0435\u043d\u0438\u0435: " },
            new string[] { "Designation:", "\u041e\u0431\u043e\u0437\u043d\u0430\u0447\u0435\u043d\u0438\u0435:" },  // without space (for <b>Designation:</b> and empty values)
            new string[] { "Year: ", "\u0413\u043e\u0434: " },
            new string[] { "Dimensions: ", "\u0413\u0430\u0431\u0430\u0440\u0438\u0442\u044b: " },
            new string[] { "RCS Count: ", "\u0414\u0432\u0438\u0433. \u0420\u0421\u0423: " },  // Двиг. РСУ:
            new string[] { "Torch Drive: ", "\u041c\u0430\u0440\u0448\u0435\u0432\u044b\u0439 \u0434\u0432\u0438\u0433.: " },
            new string[] { "Payment per shift: ", "\u041e\u043f\u043b\u0430\u0442\u0430 \u0437\u0430 \u0441\u043c\u0435\u043d\u0443: " },
            new string[] { "Mortgage: ", "\u0418\u043f\u043e\u0442\u0435\u043a\u0430: " },
            new string[] { "Location: ", "\u041c\u0435\u0441\u0442\u043e\u043f\u043e\u043b\u043e\u0436\u0435\u043d\u0438\u0435: " },
            new string[] { "Location:", "\u041c\u0435\u0441\u0442\u043e\u043f\u043e\u043b\u043e\u0436\u0435\u043d\u0438\u0435:" },  // without space (for <b>Location:</b>)
            new string[] { "Docking: ", "\u0421\u0442\u044b\u043a\u043e\u0432\u043a\u0430: " },  // Стыковка: (activates shipInfoLabels[4])
            new string[] { "Docked: ", "\u041f\u0440\u0438\u0441\u0442\u044b\u043a\u043e\u0432\u0430\u043d: " },  // Пристыкован:
            new string[] { "Mass: ", "\u041c\u0430\u0441\u0441\u0430: " },
            new string[] { " (kg)", " (\u043a\u0433)" },
            // --- Chargen career skill panel ---
            new string[] { "<b>Total Cost: </b>", "<b>\u041e\u0431\u0449\u0430\u044f \u0441\u0442\u043e\u0438\u043c\u043e\u0441\u0442\u044c: </b>" },
            new string[] { "Skilled in ", "\u041d\u0430\u0432\u044b\u043a: " },
            new string[] { "skilled in ", "\u0443\u043c\u0435\u0435\u0442 " },
            // Interaction intent: "[subject] going to [verb] [object]"
            new string[] { " going to get ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u0432\u0437\u044f\u0442\u044c " },
            new string[] { " going to take ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u0432\u0437\u044f\u0442\u044c " },
            new string[] { " going to uninstall ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u0434\u0435\u043c\u043e\u043d\u0442\u0438\u0440\u043e\u0432\u0430\u0442\u044c " },
            new string[] { " going to install ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u0443\u0441\u0442\u0430\u043d\u043e\u0432\u0438\u0442\u044c " },
            new string[] { " going to drop ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u0431\u0440\u043e\u0441\u0438\u0442\u044c " },
            new string[] { " going to pick up ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u043f\u043e\u0434\u043e\u0431\u0440\u0430\u0442\u044c " },
            new string[] { " going to open ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u043e\u0442\u043a\u0440\u044b\u0442\u044c " },
            new string[] { " going to close ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u0437\u0430\u043a\u0440\u044b\u0442\u044c " },
            new string[] { " going to repair ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u043f\u043e\u0447\u0438\u043d\u0438\u0442\u044c " },
            new string[] { " going to use ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u0438\u0441\u043f\u043e\u043b\u044c\u0437\u043e\u0432\u0430\u0442\u044c " },
            new string[] { " going to scrap ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u0441\u043b\u043e\u043c\u0430\u0442\u044c " },
            new string[] { " going to dismantle ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f \u0440\u0430\u0437\u043e\u0431\u0440\u0430\u0442\u044c " },
            new string[] { " going to ", " \u0441\u043e\u0431\u0438\u0440\u0430\u0435\u0442\u0441\u044f " },
            // UI labels and game messages
            new string[] { "Objective Complete:", "\u0417\u0430\u0434\u0430\u0447\u0430 \u0432\u044b\u043f\u043e\u043b\u043d\u0435\u043d\u0430:" },
            new string[] { "Objective complete:", "\u0417\u0430\u0434\u0430\u0447\u0430 \u0432\u044b\u043f\u043e\u043b\u043d\u0435\u043d\u0430:" },
            new string[] { "World unpaused", "\u041c\u0438\u0440 \u0441\u043d\u044f\u0442 \u0441 \u043f\u0430\u0443\u0437\u044b" },
            new string[] { "World paused", "\u041c\u0438\u0440 \u043d\u0430 \u043f\u0430\u0443\u0437\u0435" },
            new string[] { "Factions: ", "\u0424\u0440\u0430\u043a\u0446\u0438\u0438: " },
            new string[] { "Factions:", "\u0424\u0440\u0430\u043a\u0446\u0438\u0438:" },
            // Pronoun case after prepositions: "na vy" -> "na vas" etc.
            new string[] { " \u043d\u0430 \u0432\u044b.", " \u043d\u0430 \u0432\u0430\u0441." },
            new string[] { " \u043d\u0430 \u0432\u044b,", " \u043d\u0430 \u0432\u0430\u0441," },
            new string[] { " \u043d\u0430 \u0432\u044b ", " \u043d\u0430 \u0432\u0430\u0441 " },
            new string[] { " \u0437\u0430 \u0432\u044b.", " \u0437\u0430 \u0432\u0430\u0441." },
            new string[] { " \u0437\u0430 \u0432\u044b,", " \u0437\u0430 \u0432\u0430\u0441," },
            new string[] { " \u0437\u0430 \u0432\u044b ", " \u0437\u0430 \u0432\u0430\u0441 " },
            new string[] { " \u043e\u0442 \u0432\u044b.", " \u043e\u0442 \u0432\u0430\u0441." },
            new string[] { " \u043e\u0442 \u0432\u044b ", " \u043e\u0442 \u0432\u0430\u0441 " },
            new string[] { " \u043a \u0432\u044b.", " \u043a \u0432\u0430\u043c." },
            new string[] { " \u043a \u0432\u044b ", " \u043a \u0432\u0430\u043c " },
            new string[] { " \u0441 \u0432\u044b.", " \u0441 \u0432\u0430\u043c\u0438." },
            new string[] { " \u0441 \u0432\u044b,", " \u0441 \u0432\u0430\u043c\u0438," },
            new string[] { " \u0441 \u0432\u044b ", " \u0441 \u0432\u0430\u043c\u0438 " },
            new string[] { " \u0434\u043b\u044f \u0432\u044b", " \u0434\u043b\u044f \u0432\u0430\u0441" },
            new string[] { " \u0443 \u0432\u044b", " \u0443 \u0432\u0430\u0441" },
            new string[] { " \u043e \u0432\u044b.", " \u043e \u0432\u0430\u0441." },
            new string[] { " \u043e \u0432\u044b ", " \u043e \u0432\u0430\u0441 " },
            new string[] { " \u043f\u0440\u043e \u0432\u044b", " \u043f\u0440\u043e \u0432\u0430\u0441" },
            new string[] { " \u043d\u0430 \u043e\u043d.", " \u043d\u0430 \u043d\u0435\u0433\u043e." },
            new string[] { " \u043d\u0430 \u043e\u043d,", " \u043d\u0430 \u043d\u0435\u0433\u043e," },
            new string[] { " \u043d\u0430 \u043e\u043d ", " \u043d\u0430 \u043d\u0435\u0433\u043e " },
            new string[] { " \u043d\u0430 \u043e\u043d\u0430.", " \u043d\u0430 \u043d\u0435\u0451." },
            new string[] { " \u043d\u0430 \u043e\u043d\u0430,", " \u043d\u0430 \u043d\u0435\u0451," },
            new string[] { " \u043d\u0430 \u043e\u043d\u0430 ", " \u043d\u0430 \u043d\u0435\u0451 " },
            new string[] { " \u043d\u0430 \u043e\u043d\u0438.", " \u043d\u0430 \u043d\u0438\u0445." },
            new string[] { " \u043d\u0430 \u043e\u043d\u0438 ", " \u043d\u0430 \u043d\u0438\u0445 " },
            // Remove gendered suffix (a) from log messages
            new string[] { "\u043e\u0442\u0432\u0435\u0442\u0438\u043b(\u0430)", "\u043e\u0442\u0432\u0435\u0442\u0438\u043b" },
            new string[] { "\u043e\u0442\u043a\u0440\u044b\u043b(\u0430)", "\u043e\u0442\u043a\u0440\u044b\u043b" },
            new string[] { "\u0437\u0430\u043a\u0440\u044b\u043b(\u0430)", "\u0437\u0430\u043a\u0440\u044b\u043b" },
            new string[] { "\u0441\u043a\u0430\u0437\u0430\u043b(\u0430)", "\u0441\u043a\u0430\u0437\u0430\u043b" },
            new string[] { "\u043e\u0442\u0432\u0435\u0442\u0438\u043b(a)", "\u043e\u0442\u0432\u0435\u0442\u0438\u043b" },
            new string[] { "\u043e\u0442\u043a\u0440\u044b\u043b(a)", "\u043e\u0442\u043a\u0440\u044b\u043b" },
            new string[] { "\u0437\u0430\u043a\u0440\u044b\u043b(a)", "\u0437\u0430\u043a\u0440\u044b\u043b" },
            new string[] { "\u0441\u043a\u0430\u0437\u0430\u043b(a)", "\u0441\u043a\u0430\u0437\u0430\u043b" },
            // --- Loading screen status messages (txtLoadingText TMP component) ---
            // Mixed case variants (if TMP uses fontStyle UpperCase)
            new string[] { "Spawning Station: ", "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0441\u0442\u0430\u043d\u0446\u0438\u0438: " },
            new string[] { "Spawning Ship: ", "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u043a\u043e\u0440\u0430\u0431\u043b\u044f: " },
            new string[] { "Spawning System Body Hierarchy", "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0438\u0435\u0440\u0430\u0440\u0445\u0438\u0438 \u0442\u0435\u043b \u0441\u0438\u0441\u0442\u0435\u043c\u044b" },
            new string[] { "Spawning System Bodies", "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0442\u0435\u043b \u0441\u0438\u0441\u0442\u0435\u043c\u044b" },
            new string[] { "Spawning System Companies", "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u043a\u043e\u043c\u043f\u0430\u043d\u0438\u0439 \u0441\u0438\u0441\u0442\u0435\u043c\u044b" },
            new string[] { "Spawning System Stations", "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0441\u0442\u0430\u043d\u0446\u0438\u0439 \u0441\u0438\u0441\u0442\u0435\u043c\u044b" },
            new string[] { "Spawning System Derelicts", "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0434\u0435\u0440\u0435\u043b\u0438\u043a\u0442\u043e\u0432" },
            new string[] { "Spawning System Ships", "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u043a\u043e\u0440\u0430\u0431\u043b\u0435\u0439 \u0441\u0438\u0441\u0442\u0435\u043c\u044b" },
            new string[] { "Parse System Bodies", "\u0410\u043d\u0430\u043b\u0438\u0437 \u0442\u0435\u043b \u0441\u0438\u0441\u0442\u0435\u043c\u044b" },
            new string[] { "Initializing Star System", "\u0418\u043d\u0438\u0446\u0438\u0430\u043b\u0438\u0437\u0430\u0446\u0438\u044f \u0437\u0432\u0451\u0437\u0434\u043d\u043e\u0439 \u0441\u0438\u0441\u0442\u0435\u043c\u044b" },
            new string[] { "Loading Orbital Bodies!", "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u043e\u0440\u0431\u0438\u0442\u0430\u043b\u044c\u043d\u044b\u0445 \u0442\u0435\u043b!" },
            new string[] { "Creating Orbital Bodies", "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u043e\u0440\u0431\u0438\u0442\u0430\u043b\u044c\u043d\u044b\u0445 \u0442\u0435\u043b" },
            new string[] { "Creating Stations: ", "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0441\u0442\u0430\u043d\u0446\u0438\u0439: " },
            new string[] { "Creating Stations", "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0441\u0442\u0430\u043d\u0446\u0438\u0439" },
            new string[] { "Loading Stations!", "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u0441\u0442\u0430\u043d\u0446\u0438\u0439!" },
            new string[] { "Loading Ships!", "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u043a\u043e\u0440\u0430\u0431\u043b\u0435\u0439!" },
            new string[] { "Loading ship ", "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u043a\u043e\u0440\u0430\u0431\u043b\u044f " },
            new string[] { "Init ship manager", "\u0418\u043d\u0438\u0446\u0438\u0430\u043b\u0438\u0437\u0430\u0446\u0438\u044f \u043c\u0435\u043d\u0435\u0434\u0436\u0435\u0440\u0430 \u043a\u043e\u0440\u0430\u0431\u043b\u0435\u0439" },
            new string[] { "Loading scene", "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u0441\u0446\u0435\u043d\u044b" },
            // UPPERCASE variants (code-level ToUpper before TMP text setter)
            new string[] { "SPAWNING STATION: ", "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0421\u0422\u0410\u041d\u0426\u0418\u0418: " },
            new string[] { "SPAWNING SHIP: ", "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u041a\u041e\u0420\u0410\u0411\u041b\u042f: " },
            new string[] { "SPAWNING SYSTEM BODY HIERARCHY", "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0418\u0415\u0420\u0410\u0420\u0425\u0418\u0418 \u0422\u0415\u041b \u0421\u0418\u0421\u0422\u0415\u041c\u042b" },
            new string[] { "SPAWNING SYSTEM BODIES", "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0422\u0415\u041b \u0421\u0418\u0421\u0422\u0415\u041c\u042b" },
            new string[] { "SPAWNING SYSTEM COMPANIES", "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u041a\u041e\u041c\u041f\u0410\u041d\u0418\u0419 \u0421\u0418\u0421\u0422\u0415\u041c\u042b" },
            new string[] { "SPAWNING SYSTEM STATIONS", "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0421\u0422\u0410\u041d\u0426\u0418\u0419 \u0421\u0418\u0421\u0422\u0415\u041c\u042b" },
            new string[] { "SPAWNING SYSTEM DERELICTS", "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0414\u0415\u0420\u0415\u041b\u0418\u041a\u0422\u041e\u0412" },
            new string[] { "SPAWNING SYSTEM SHIPS", "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u041a\u041e\u0420\u0410\u0411\u041b\u0415\u0419 \u0421\u0418\u0421\u0422\u0415\u041c\u042b" },
            new string[] { "PARSE SYSTEM BODIES", "\u0410\u041d\u0410\u041b\u0418\u0417 \u0422\u0415\u041b \u0421\u0418\u0421\u0422\u0415\u041c\u042b" },
            new string[] { "INITIALIZING STAR SYSTEM", "\u0418\u041d\u0418\u0426\u0418\u0410\u041b\u0418\u0417\u0410\u0426\u0418\u042f \u0417\u0412\u0401\u0417\u0414\u041d\u041e\u0419 \u0421\u0418\u0421\u0422\u0415\u041c\u042b" },
            new string[] { "LOADING ORBITAL BODIES!", "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410 \u041e\u0420\u0411\u0418\u0422\u0410\u041b\u042c\u041d\u042b\u0425 \u0422\u0415\u041b!" },
            new string[] { "CREATING ORBITAL BODIES", "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u041e\u0420\u0411\u0418\u0422\u0410\u041b\u042c\u041d\u042b\u0425 \u0422\u0415\u041b" },
            new string[] { "CREATING STATIONS: ", "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0421\u0422\u0410\u041d\u0426\u0418\u0419: " },
            new string[] { "CREATING STATIONS", "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0421\u0422\u0410\u041d\u0426\u0418\u0419" },
            new string[] { "LOADING STATIONS!", "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410 \u0421\u0422\u0410\u041d\u0426\u0418\u0419!" },
            new string[] { "LOADING SHIPS!", "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410 \u041a\u041e\u0420\u0410\u0411\u041b\u0415\u0419!" },
            new string[] { "LOADING NEW SHIPS FROM JSONS", "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410 \u041d\u041e\u0412\u042b\u0425 \u041a\u041e\u0420\u0410\u0411\u041b\u0415\u0419" },
            new string[] { "LOADING SHIP ", "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410 \u041a\u041e\u0420\u0410\u0411\u041b\u042f " },
            new string[] { "INIT SHIP MANAGER", "\u0418\u041d\u0418\u0426\u0418\u0410\u041b\u0418\u0417\u0410\u0426\u0418\u042f \u041c\u0415\u041d\u0415\u0414\u0416\u0415\u0420\u0410 \u041a\u041e\u0420\u0410\u0411\u041b\u0415\u0419" },
            new string[] { "LOADING SCENE", "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410 \u0421\u0426\u0415\u041d\u042b" },
            // --- Changelog / main menu feature list ---
            new string[] { "PDA Vizor App", "\u041f\u0440\u0438\u043b\u043e\u0436\u0435\u043d\u0438\u0435 \u041a\u041f\u041a \u0412\u0438\u0437\u043e\u0440" },
            new string[] { "Renamable Items", "\u041f\u0435\u0440\u0435\u0438\u043c\u0435\u043d\u043e\u0432\u0430\u043d\u0438\u0435 \u043f\u0440\u0435\u0434\u043c\u0435\u0442\u043e\u0432" },
            new string[] { "Relationship Rebalancing", "\u0420\u0435\u0431\u0430\u043b\u0430\u043d\u0441 \u043e\u0442\u043d\u043e\u0448\u0435\u043d\u0438\u0439" },
            new string[] { "Body Type", "\u0422\u0438\u043f \u0442\u0435\u043b\u0430" },
            new string[] { "Venus Music", "\u041c\u0443\u0437\u044b\u043a\u0430 \u0412\u0435\u043d\u0435\u0440\u044b" },
            new string[] { "Ostraka Tutorial & more...", "\u041e\u0431\u0443\u0447\u0435\u043d\u0438\u0435 Ostraka \u0438 \u0434\u0440\u0443\u0433\u043e\u0435..." },
            // --- Ship info Yes/No values (contextual, after label translation in 2nd pass) ---
            new string[] { "\u041c\u0430\u0440\u0448\u0435\u0432\u044b\u0439 \u0434\u0432\u0438\u0433.: Yes", "\u041c\u0430\u0440\u0448\u0435\u0432\u044b\u0439 \u0434\u0432\u0438\u0433.: \u0414\u0430" },
            new string[] { "\u041c\u0430\u0440\u0448\u0435\u0432\u044b\u0439 \u0434\u0432\u0438\u0433.: No", "\u041c\u0430\u0440\u0448\u0435\u0432\u044b\u0439 \u0434\u0432\u0438\u0433.: \u041d\u0435\u0442" },
            new string[] { "\u0421\u0442\u044b\u043a\u043e\u0432\u043a\u0430: Yes", "\u0421\u0442\u044b\u043a\u043e\u0432\u043a\u0430: \u0414\u0430" },
            new string[] { "\u0421\u0442\u044b\u043a\u043e\u0432\u043a\u0430: No", "\u0421\u0442\u044b\u043a\u043e\u0432\u043a\u0430: \u041d\u0435\u0442" },
            // --- Units (previously handled by XUnity regex) ---
            new string[] { "KPa", "\u043a\u041f\u0430" },
            new string[] { "Km/s", "\u043a\u043c/\u0441" },
            // "kg" only after space/digit/paren — NOT inside words like "Background"
            new string[] { " kg", " \u043a\u0433" },
            new string[] { "(kg)", "(\u043a\u0433)" },
            new string[] { " Kg ", " \u043a\u0433 " },
            // --- Action labels with multipliers (GUIShipManager / interaction actions) ---
            new string[] { "uninstall (x", "\u0414\u0435\u043c\u043e\u043d\u0442\u0438\u0440\u0443\u0435\u0442 (x" },
            new string[] { "install (x", "\u0423\u0441\u0442\u0430\u043d\u0430\u0432\u043b\u0438\u0432\u0430\u0435\u0442 (x" },
            new string[] { "scrap (x", "\u041b\u043e\u043c\u0430\u0435\u0442 (x" },
            new string[] { "dismantle (x", "\u0420\u0430\u0437\u0431\u0438\u0440\u0430\u0435\u0442 (x" },
            new string[] { "repair (x", "\u0427\u0438\u043d\u0438\u0442 (x" },
            // --- Ship panel UI from XUnity ---
            new string[] { "Factions: n/a", "\u0424\u0440\u0430\u043a\u0446\u0438\u0438: \u043d\u0435\u0442" },
            new string[] { "<b>Tools required:", "<b>\u0422\u0440\u0435\u0431\u0443\u044e\u0442\u0441\u044f \u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442\u044b:" },
            new string[] { "<b>We need:", "<b>\u0422\u0440\u0435\u0431\u0443\u0435\u0442\u0441\u044f:" },
            new string[] { "<b>We can't be:", "<b>\u041d\u0435\u0432\u043e\u0437\u043c\u043e\u0436\u043d\u043e \u043f\u0440\u0438:" },
            new string[] { "<b>Effects:", "<b>\u042d\u0444\u0444\u0435\u043a\u0442\u044b:" },
            new string[] { " tiles selected", " \u043f\u043b\u0438\u0442\u043e\u043a \u0432\u044b\u0431\u0440\u0430\u043d\u043e" },
            // --- Missing item / last attempt (CondOwner log messages from XUnity) ---
            new string[] { " Missing item: Is ", " \u041d\u0435\u0442 \u043f\u0440\u0435\u0434\u043c\u0435\u0442\u0430: " },
            new string[] { "Item present but, Not Enough: ", "\u041f\u0440\u0435\u0434\u043c\u0435\u0442 \u0435\u0441\u0442\u044c, \u043d\u043e \u043d\u0435\u0434\u043e\u0441\u0442\u0430\u0442\u043e\u0447\u043d\u043e: " },
            new string[] { "Last attempt by ", "\u041f\u043e\u0441\u043b\u0435\u0434\u043d\u044f\u044f \u043f\u043e\u043f\u044b\u0442\u043a\u0430: " },
            new string[] { " gives ", " \u043f\u043e\u0434\u0430\u0451\u0442 " },
            new string[] { " gains ", " \u043f\u043e\u043b\u0443\u0447\u0430\u0435\u0442 " },
            new string[] { "'s Company", " \u0438 \u043a\u043e\u043c\u0430\u043d\u0434\u0430" },
            new string[] { "Became ", "" },
            new string[] { " during ", " " },
            // --- Crew panel labels ---
            new string[] { "Current:", "\u0422\u0435\u043a\u0443\u0449\u0435\u0435:" },
            new string[] { "Log:", "\u0416\u0443\u0440\u043d\u0430\u043b:" },
            // --- Effect descriptions ---
            new string[] { "Keeps control.", "\u0421\u043e\u0445\u0440\u0430\u043d\u044f\u0435\u0442 \u043a\u043e\u043d\u0442\u0440\u043e\u043b\u044c." },
            // --- List connectors (from {ls} lists) ---
            new string[] { " and ", " \u0438 " },
            // --- Settings descriptions ---
            new string[] { "Hold LeftAlt to see hotkeys", "\u0423\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0439\u0442\u0435 \u041b.Alt, \u0447\u0442\u043e\u0431\u044b \u0443\u0432\u0438\u0434\u0435\u0442\u044c \u0433\u043e\u0440\u044f\u0447\u0438\u0435 \u043a\u043b\u0430\u0432\u0438\u0448\u0438" },
            new string[] { "nearby interactable objects", "\u0431\u043b\u0438\u0436\u0430\u0439\u0448\u0438\u0435 \u0438\u043d\u0442\u0435\u0440\u0430\u043a\u0442\u0438\u0432\u043d\u044b\u0435 \u043e\u0431\u044a\u0435\u043a\u0442\u044b" },
            // --- Ship systems panel labels (multiline text in Clean()) ---
            // Longer matches MUST come before shorter to avoid partial matches
            new string[] { "LIFE SUPPORT O2 STORES:", "\u0417\u0410\u041f\u0410\u0421\u042b O2:" },  // ЗАПАСЫ O2:
            new string[] { "LIFE SUPPORT HEAT:", "\u041e\u0411\u041e\u0413\u0420\u0415\u0412:" },  // ОБОГРЕВ:
            new string[] { "LIFE SUPPORT COOL:", "\u041e\u0425\u041b\u0410\u0416\u0414\u0415\u041d\u0418\u0415:" },  // ОХЛАЖДЕНИЕ:
            new string[] { "LIFE SUPPORT", "\u0416\u0418\u0417\u041d\u0415\u041e\u0411\u0415\u0421\u041f\u0415\u0427\u0415\u041d\u0418\u0415" },  // ЖИЗНЕОБЕСПЕЧЕНИЕ
            new string[] { "WORKING O2 PUMPS:", "\u041d\u0410\u0421\u041e\u0421\u042b O2:" },  // НАСОСЫ O2:
            new string[] { "VESSEL MASS:", "\u041c\u0410\u0421\u0421\u0410 \u0421\u0423\u0414\u041d\u0410:" },  // МАССА СУДНА:
            new string[] { "TRANSPONDER:", "\u0422\u0420\u0410\u041d\u0421\u041f\u041e\u041d\u0414\u0415\u0420:" },  // ТРАНСПОНДЕР:
            new string[] { "ANTENNA:", "\u0410\u041d\u0422\u0415\u041d\u041d\u0410:" },  // АНТЕННА:
            new string[] { "REACTOR HE3:", "\u0413\u0415\u041b\u0418\u0419-3:" },  // ГЕЛИЙ-3:
            new string[] { "REACTOR D2O:", "\u0422\u042f\u0416\u0401\u041b\u0410\u042f \u0412\u041e\u0414\u0410:" },  // ТЯЖЁЛАЯ ВОДА:
            new string[] { "NAV STATION:", "\u041d\u0410\u0412-\u0421\u0422\u0410\u041d\u0426\u0418\u042f:" },           // НАВ-СТАНЦИЯ:
            new string[] { "REACTOR:", "\u0420\u0415\u0410\u041a\u0422\u041e\u0420:" },                          // РЕАКТОР:
            new string[] { "REACTOR PELLETS:", "\u0422\u041e\u041f\u041b\u0418\u0412\u041e \u0420\u0415\u0410\u041a\u0422\u041e\u0420\u0410:" },  // ТОПЛИВО РЕАКТОРА:
            new string[] { "REACTOR PROPELLANT:", "\u0420\u0410\u0411\u041e\u0427\u0415\u0415 \u0422\u0415\u041b\u041e:" },  // РАБОЧЕЕ ТЕЛО:
            new string[] { "RCS THRUSTERS:", "\u0414\u0412\u0418\u0413\u0410\u0422\u0415\u041b\u0418 \u0420\u0421\u0423:" },  // ДВИГАТЕЛИ РСУ:
            new string[] { "RCS DISTRIBUTOR:", "\u0420\u0410\u0421\u041f\u0420\u0415\u0414\u0415\u041b\u0418\u0422\u0415\u041b\u042c \u0420\u0421\u0423:" },  // РАСПРЕДЕЛИТЕЛЬ РСУ:
            new string[] { "RCS REMASS:", "\u0422\u041e\u041f\u041b\u0418\u0412\u041e \u0420\u0421\u0423:" },      // ТОПЛИВО РСУ:
            new string[] { "BACKUP POWER:", "\u0420\u0415\u0417\u0415\u0420\u0412\u041d\u041e\u0415 \u041f\u0418\u0422\u0410\u041d\u0418\u0415:" },  // РЕЗЕРВНОЕ ПИТАНИЕ:
            new string[] { "ZOOM RANGE:", "\u0414\u0410\u041b\u042c\u041d\u041e\u0421\u0422\u042c:" },  // ДАЛЬНОСТЬ:
            // --- Info panel labels ---
            new string[] { "<b>Relationship Status:</b>", "<b>\u0421\u0442\u0430\u0442\u0443\u0441 \u043e\u0442\u043d\u043e\u0448\u0435\u043d\u0438\u0439:</b>" },  // Статус отношений:
            new string[] { "<b>Target:</b>", "<b>\u0426\u0435\u043b\u044c:</b>" },                // Цель:
            new string[] { "<b>Duration:</b>", "<b>\u0414\u043b\u0438\u0442\u0435\u043b\u044c\u043d\u043e\u0441\u0442\u044c:</b>" },  // Длительность:
            new string[] { "They See Us As:", "\u041e\u043d\u0438 \u0432\u0438\u0434\u044f\u0442 \u0432 \u043d\u0430\u0441:" },  // Они видят в нас:
            new string[] { "Career:", "\u041a\u0430\u0440\u044c\u0435\u0440\u0430:" },  // Карьера:
            new string[] { "Homeworld:", "\u0420\u043e\u0434\u043d\u043e\u0439 \u043c\u0438\u0440:" },  // Родной мир:
            new string[] { "Strata:", "\u0421\u0442\u0440\u0430\u0442\u0430:" },
            new string[] { "Stranger", "\u041d\u0435\u0437\u043d\u0430\u043a\u043e\u043c\u0435\u0446" },  // Незнакомец
            new string[] { "Citizen", "\u0413\u0440\u0430\u0436\u0434\u0430\u043d\u0438\u043d" },  // Гражданин
            new string[] { "Friend", "\u0414\u0440\u0443\u0433" },                 // Друг
            // --- Error/warning messages within multiline blocks ---
            new string[] { "None! Cannot be left blank. Will autoassign ", "\u041f\u0443\u0441\u0442\u043e! \u041d\u0435\u043b\u044c\u0437\u044f \u043e\u0441\u0442\u0430\u0432\u0438\u0442\u044c \u043f\u0443\u0441\u0442\u044b\u043c. \u0411\u0443\u0434\u0435\u0442 \u043d\u0430\u0437\u043d\u0430\u0447\u0435\u043d\u043e " },
            // --- Navigation/scanning panel ---
            new string[] { "CURRENT P.O.R.", "\u0422\u0415\u041a\u0423\u0429\u0410\u042f \u0422.\u041e.\u041e." },  // ТЕКУЩАЯ Т.О.О. (точка отсчёта орбиты)
            new string[] { "CURRENT TRG", "\u0422\u0415\u041a\u0423\u0429\u0410\u042f \u0426\u0415\u041b\u042c" },  // ТЕКУЩАЯ ЦЕЛЬ
            new string[] { "VREL", "\u041e\u0422\u041d.\u0421\u041a" },             // ОТН.СК (относительная скорость)
            // --- Game engine hardcoded messages ---
            new string[] { "Items required first. Adding tasks now.", "\u041d\u0435\u043e\u0431\u0445\u043e\u0434\u0438\u043c\u044b \u043f\u0440\u0435\u0434\u043c\u0435\u0442\u044b. \u0414\u043e\u0431\u0430\u0432\u043b\u044f\u044e\u0442\u0441\u044f \u0437\u0430\u0434\u0430\u0447\u0438." },
            // --- Condition descriptions (smoky air stings) ---
            // NOTE: " and " → " и " fires earlier in phraseReplacements, so use "и" here.
            // In Layer 5, rxPronoun replaces "your" → "ваш" BEFORE phraseReplacements,
            // so we need to match "ваш" (not "your") for correct accusative "вашу".
            new string[] { " feel that the smokey air stings \u0432\u0430\u0448 skin, eyes, \u0438 throat", " \u0447\u0443\u0432\u0441\u0442\u0432\u0443\u0435\u0442\u0435, \u0447\u0442\u043e \u0434\u044b\u043c \u0449\u0438\u043f\u043b\u0435\u0442 \u0432\u0430\u0448\u0443 \u043a\u043e\u0436\u0443, \u0433\u043b\u0430\u0437\u0430 \u0438 \u0433\u043e\u0440\u043b\u043e" },
            new string[] { " feel that smokey air stings \u0432\u0430\u0448 skin, eyes, \u0438 throat", " \u0447\u0443\u0432\u0441\u0442\u0432\u0443\u0435\u0442\u0435, \u0447\u0442\u043e \u0434\u044b\u043c \u0449\u0438\u043f\u043b\u0435\u0442 \u0432\u0430\u0448\u0443 \u043a\u043e\u0436\u0443, \u0433\u043b\u0430\u0437\u0430 \u0438 \u0433\u043e\u0440\u043b\u043e" },
            // Partial fallback for other subjects (его/её/их kozhu stays correct in context)
            new string[] { " feel that the smokey air stings ", " \u0447\u0443\u0432\u0441\u0442\u0432\u0443\u0435\u0442\u0435, \u0447\u0442\u043e \u0434\u044b\u043c \u0449\u0438\u043f\u043b\u0435\u0442 " },
            new string[] { " feel that smokey air stings ", " \u0447\u0443\u0432\u0441\u0442\u0432\u0443\u0435\u0442\u0435, \u0447\u0442\u043e \u0434\u044b\u043c \u0449\u0438\u043f\u043b\u0435\u0442 " },
            new string[] { " feels that the smokey air stings ", " \u0447\u0443\u0432\u0441\u0442\u0432\u0443\u0435\u0442, \u0447\u0442\u043e \u0434\u044b\u043c \u0449\u0438\u043f\u043b\u0435\u0442 " },
            new string[] { "skin, eyes, and throat", "\u043a\u043e\u0436\u0443, \u0433\u043b\u0430\u0437\u0430 \u0438 \u0433\u043e\u0440\u043b\u043e" },
            new string[] { "skin, eyes, \u0438 throat", "\u043a\u043e\u0436\u0443, \u0433\u043b\u0430\u0437\u0430 \u0438 \u0433\u043e\u0440\u043b\u043e" },
            // --- Fire condition tooltip phrases ---
            // Layer 5 state: rxPossS removes 's, rxAAn removes "a " before words, rxPronoun "you"→"вы", " and "→" и " fires first
            new string[] { "Fire.\n\n", "\u041e\u0433\u043e\u043d\u044c.\n\n" },
            // Pure-English path: " and " already replaced → "и"; "'s" still present
            new string[] { "One of mankind's oldest, \u0438 most powerful, tools.", "\u041e\u0434\u0438\u043d \u0438\u0437 \u0441\u0442\u0430\u0440\u0435\u0439\u0448\u0438\u0445 \u0438 \u043c\u043e\u0449\u043d\u0435\u0439\u0448\u0438\u0445 \u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442\u043e\u0432 \u0447\u0435\u043b\u043e\u0432\u0435\u0447\u0435\u0441\u0442\u0432\u0430." },
            // Layer 5 state: rxPossS removed "'s" → "mankind oldest"; " and "→" и " already
            new string[] { "One of mankind oldest, \u0438 most powerful, tools.", "\u041e\u0434\u0438\u043d \u0438\u0437 \u0441\u0442\u0430\u0440\u0435\u0439\u0448\u0438\u0445 \u0438 \u043c\u043e\u0449\u043d\u0435\u0439\u0448\u0438\u0445 \u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442\u043e\u0432 \u0447\u0435\u043b\u043e\u0432\u0435\u0447\u0435\u0441\u0442\u0432\u0430." },
            // Original (fallback, won't match in practice but harmless)
            new string[] { "One of mankind's oldest, and most powerful, tools.", "\u041e\u0434\u0438\u043d \u0438\u0437 \u0441\u0442\u0430\u0440\u0435\u0439\u0448\u0438\u0445 \u0438 \u043c\u043e\u0449\u043d\u0435\u0439\u0448\u0438\u0445 \u0438\u043d\u0441\u0442\u0440\u0443\u043c\u0435\u043d\u0442\u043e\u0432 \u0447\u0435\u043b\u043e\u0432\u0435\u0447\u0435\u0441\u0442\u0432\u0430." },
            // Pure-English path: "you" intact, "a spacecraft" intact
            new string[] { "Generally, you don't want to see this sort of thing on a spacecraft.", "\u0412\u043e\u043e\u0431\u0449\u0435-\u0442\u043e, \u0432\u044b \u043d\u0435 \u0445\u043e\u0442\u0438\u0442\u0435 \u0432\u0438\u0434\u0435\u0442\u044c \u0442\u0430\u043a\u043e\u0435 \u043d\u0430 \u0431\u043e\u0440\u0442\u0443." },
            // Layer 5 state: "вы" from rxPronoun, "a " removed by rxAAn → "spacecraft" (no article)
            new string[] { "Generally, \u0432\u044b don't want to see this sort of thing on spacecraft.", "\u0412\u043e\u043e\u0431\u0449\u0435-\u0442\u043e, \u0432\u044b \u043d\u0435 \u0445\u043e\u0442\u0438\u0442\u0435 \u0432\u0438\u0434\u0435\u0442\u044c \u0442\u0430\u043a\u043e\u0435 \u043d\u0430 \u0431\u043e\u0440\u0442\u0443." },
            // With "a " still present (if rxAAn didn't fire)
            new string[] { "Generally, \u0432\u044b don't want to see this sort of thing on a spacecraft.", "\u0412\u043e\u043e\u0431\u0449\u0435-\u0442\u043e, \u0432\u044b \u043d\u0435 \u0445\u043e\u0442\u0438\u0442\u0435 \u0432\u0438\u0434\u0435\u0442\u044c \u0442\u0430\u043a\u043e\u0435 \u043d\u0430 \u0431\u043e\u0440\u0442\u0443." },
            // --- Docking radio messages ---
            new string[] { "please choose a docking facility.", "\u0432\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u0442\u0435\u0440\u043c\u0438\u043d\u0430\u043b \u0441\u0442\u044b\u043a\u043e\u0432\u043a\u0438." },
            new string[] { "please choose a docking port.", "\u0432\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043f\u043e\u0440\u0442 \u0441\u0442\u044b\u043a\u043e\u0432\u043a\u0438." },
            new string[] { "Your ship does not fit at our docking facility", "\u0412\u0430\u0448 \u043a\u043e\u0440\u0430\u0431\u043b\u044c \u043d\u0435 \u043f\u043e\u0434\u0445\u043e\u0434\u0438\u0442 \u043a \u043d\u0430\u0448\u0435\u043c\u0443 \u0442\u0435\u0440\u043c\u0438\u043d\u0430\u043b\u0443" },
            new string[] { "No available docking ports. Come back later or use PASS.", "\u041d\u0435\u0442 \u0441\u0432\u043e\u0431\u043e\u0434\u043d\u044b\u0445 \u043f\u043e\u0440\u0442\u043e\u0432. \u0412\u0435\u0440\u043d\u0438\u0442\u0435\u0441\u044c \u043f\u043e\u0437\u0436\u0435 \u0438\u043b\u0438 \u0438\u0441\u043f\u043e\u043b\u044c\u0437\u0443\u0439\u0442\u0435 P.A.K.S." },
            new string[] { ", ready to proceed", ", \u0433\u043e\u0442\u043e\u0432 \u043a \u0441\u0442\u044b\u043a\u043e\u0432\u043a\u0435" },
        };

        // --- Translated ship info label prefixes for contextual value translation ---
        private static string[] shipInfoLabels = new string[] {
            "\u041c\u0430\u0440\u043a\u0430: ",                                                    // Марка: 
            "\u041c\u043e\u0434\u0435\u043b\u044c: ",                                              // Модель: 
            "\u041e\u0431\u043e\u0437\u043d\u0430\u0447\u0435\u043d\u0438\u0435: ",                    // Обозначение: 
            "\u041c\u0430\u0440\u0448\u0435\u0432\u044b\u0439 \u0434\u0432\u0438\u0433.: ",        // Маршевый двиг.: 
            "\u0421\u0442\u044b\u043a\u043e\u0432\u043a\u0430: ",                                  // Стыковка: 
            "\u041f\u0440\u043e\u0438\u0437\u0432\u043e\u0434\u0441\u0442\u0432\u043e: ",          // Производство: 
        };

        // --- Exact full-string translations for pure-English hardcoded messages ---
        private static Dictionary<string, string> exactTranslations = InitExactTranslations();

        private static Dictionary<string, string> InitExactTranslations()
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            m["Welcome back, Captain."] = "\u0421 \u0432\u043e\u0437\u0432\u0440\u0430\u0449\u0435\u043d\u0438\u0435\u043c, \u041a\u0430\u043f\u0438\u0442\u0430\u043d.";
            m["Welcome, Captain."] = "\u0414\u043e\u0431\u0440\u043e \u043f\u043e\u0436\u0430\u043b\u043e\u0432\u0430\u0442\u044c, \u041a\u0430\u043f\u0438\u0442\u0430\u043d.";
            m["Ferry service has arrived!"] = "\u041f\u0430\u0440\u043e\u043c \u043f\u0440\u0438\u0431\u044b\u043b!";
            m["Total cost cannot be negative"] = "\u041e\u0431\u0449\u0430\u044f \u0441\u0442\u043e\u0438\u043c\u043e\u0441\u0442\u044c \u043d\u0435 \u043c\u043e\u0436\u0435\u0442 \u0431\u044b\u0442\u044c \u043e\u0442\u0440\u0438\u0446\u0430\u0442\u0435\u043b\u044c\u043d\u043e\u0439";
            m["**Debug Commands Have Been Disabled**"] = "**\u041e\u0442\u043b\u0430\u0434\u043e\u0447\u043d\u044b\u0435 \u043a\u043e\u043c\u0430\u043d\u0434\u044b \u043e\u0442\u043a\u043b\u044e\u0447\u0435\u043d\u044b**";
            m["**Debug Commands Have Been Activated**"] = "**\u041e\u0442\u043b\u0430\u0434\u043e\u0447\u043d\u044b\u0435 \u043a\u043e\u043c\u0430\u043d\u0434\u044b \u0430\u043a\u0442\u0438\u0432\u0438\u0440\u043e\u0432\u0430\u043d\u044b**";
            m["Sign-on Bonus"] = "\u0411\u043e\u043d\u0443\u0441 \u0437\u0430 \u0432\u0441\u0442\u0443\u043f\u043b\u0435\u043d\u0438\u0435";
            m["Death Pay"] = "\u041f\u043e\u0441\u043c\u0435\u0440\u0442\u043d\u0430\u044f \u0432\u044b\u043f\u043b\u0430\u0442\u0430";
            m["Death Pay Adjustment"] = "\u041a\u043e\u0440\u0440\u0435\u043a\u0442\u0438\u0440\u043e\u0432\u043a\u0430 \u043f\u043e\u0441\u043c\u0435\u0440\u0442\u043d\u043e\u0439 \u0432\u044b\u043f\u043b\u0430\u0442\u044b";
            m["Salary"] = "\u0417\u0430\u0440\u043f\u043b\u0430\u0442\u0430";
            m["Keeps control."] = "\u0421\u043e\u0445\u0440\u0430\u043d\u044f\u0435\u0442 \u043a\u043e\u043d\u0442\u0440\u043e\u043b\u044c.";
            m["Keeps control"] = "\u0421\u043e\u0445\u0440\u0430\u043d\u044f\u0435\u0442 \u043a\u043e\u043d\u0442\u0440\u043e\u043b\u044c";

            // --- Chargen career UI buttons/labels (hardcoded in GUIChargenCareer.cs) ---
            m["Apply"] = "\u041f\u0440\u0438\u043c\u0435\u043d\u0438\u0442\u044c";
            m["Undo Last"] = "\u041e\u0442\u043c\u0435\u043d\u0438\u0442\u044c";
            m["Clear"] = "\u0421\u0431\u0440\u043e\u0441\u0438\u0442\u044c";
            m["Summary"] = "\u0418\u0442\u043e\u0433\u043e";
            m["Selected Skills"] = "\u0412\u044b\u0431\u0440\u0430\u043d\u043d\u044b\u0435 \u043d\u0430\u0432\u044b\u043a\u0438";
            m["Costs"] = "\u0421\u0442\u043e\u0438\u043c\u043e\u0441\u0442\u044c";
            m["NO DATA"] = "\u041d\u0415\u0422 \u0414\u0410\u041d\u041d\u042b\u0425";
            m["Placeholder"] = "\u0417\u0430\u0433\u043b\u0443\u0448\u043a\u0430";
            m["Take Ship"] = "\u0412\u0437\u044f\u0442\u044c \u043a\u043e\u0440\u0430\u0431\u043b\u044c";
            m["Hobbies:"] = "\u0425\u043e\u0431\u0431\u0438:";
            m["Traits:"] = "\u0427\u0435\u0440\u0442\u044b:";
            // --- Item / stats UI labels ---
            m["Condition:"] = "\u0421\u043e\u0441\u0442\u043e\u044f\u043d\u0438\u0435:";
            m["Condition"] = "\u0421\u043e\u0441\u0442\u043e\u044f\u043d\u0438\u0435";
            // --- Crew & stats UI labels ---
            m["NO CREW"] = "\u041d\u0415\u0422 \u042d\u041a\u0418\u041f\u0410\u0416\u0410";
            m["No Crew"] = "\u041d\u0435\u0442 \u044d\u043a\u0438\u043f\u0430\u0436\u0430";
            m["No crew"] = "\u041d\u0435\u0442 \u044d\u043a\u0438\u043f\u0430\u0436\u0430";
            m["STATS"] = "\u0421\u0422\u0410\u0422.";
            m["Stats"] = "\u0421\u0442\u0430\u0442.";
            m["CREW"] = "\u042d\u041a\u0418\u041f\u0410\u0416";
            m["Crew"] = "\u042d\u043a\u0438\u043f\u0430\u0436";

            // --- Control label translations ---
            m["LeftAlt"] = "\u041b.Alt";
            m["RightAlt"] = "\u041f.Alt";
            m["LeftControl"] = "\u041b.Ctrl";
            m["RightControl"] = "\u041f.Ctrl";
            m["Cycle Crew:"] = "\u0412\u044b\u0431\u043e\u0440 \u044d\u043a\u0438\u043f\u0430\u0436\u0430:";

            // --- Settings panels ---
            m["Accessibility Settings:"] = "\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438 \u0434\u043e\u0441\u0442\u0443\u043f\u043d\u043e\u0441\u0442\u0438:";
            // --- Settings descriptions ---
            m["Hold LeftAlt to see hotkeys and nearby interactable objects."] = "\u0423\u0434\u0435\u0440\u0436\u0438\u0432\u0430\u0439\u0442\u0435 \u041b.Alt, \u0447\u0442\u043e\u0431\u044b \u0443\u0432\u0438\u0434\u0435\u0442\u044c \u0433\u043e\u0440\u044f\u0447\u0438\u0435 \u043a\u043b\u0430\u0432\u0438\u0448\u0438 \u0438 \u0431\u043b\u0438\u0436\u0430\u0439\u0448\u0438\u0435 \u0438\u043d\u0442\u0435\u0440\u0430\u043a\u0442\u0438\u0432\u043d\u044b\u0435 \u043e\u0431\u044a\u0435\u043a\u0442\u044b.";
            m["Background Rotation"] = "\u0412\u0440\u0430\u0449\u0435\u043d\u0438\u0435 \u0444\u043e\u043d\u0430";
            m["BACKGROUND ROTATION"] = "\u0412\u0420\u0410\u0429\u0415\u041d\u0418\u0415 \u0424\u041e\u041d\u0410";
            m["Flicker Amount"] = "\u041c\u0435\u0440\u0446\u0430\u043d\u0438\u0435";
            m["FLICKER AMOUNT"] = "\u041c\u0415\u0420\u0426\u0410\u041d\u0418\u0415";
            m["Soft"] = "\u041c\u044f\u0433\u043a\u043e";
            m["Full"] = "\u041f\u043e\u043b\u043d\u043e\u0435";
            m["Reset Settings"] = "\u0421\u0431\u0440\u043e\u0441 \u043d\u0430\u0441\u0442\u0440\u043e\u0435\u043a";
            m["Ambient Occlusion"] = "\u041e\u043a\u043a\u043b\u044e\u0437\u0438\u044f";
            m["SCREEN SHAKE"] = "\u0422\u0420\u042f\u0421\u041a\u0410 \u042d\u041a\u0420\u0410\u041d\u0410";
            m["Item Background"] = "\u0424\u043e\u043d \u043f\u0440\u0435\u0434\u043c\u0435\u0442\u043e\u0432";
            m["CONTENT WARNINGS"] = "\u041f\u0420\u0415\u0414\u0423\u041f\u0420\u0415\u0416\u0414\u0415\u041d\u0418\u042f";
            m["EFFECTS"] = "\u042d\u0424\u0424\u0415\u041a\u0422\u042b";
            m["FPS Limit"] = "\u041e\u0433\u0440\u0430\u043d\u0438\u0447\u0435\u043d\u0438\u0435 FPS";
            m["WINDOWED"] = "\u041e\u041a\u041e\u041d\u041d\u042b\u0419";
            m["EXPERIMENTAL"] = "\u042d\u041a\u0421\u041f\u0415\u0420\u0418\u041c\u0415\u041d\u0422.";
            m["Line of Sight"] = "\u041b\u0438\u043d\u0438\u044f \u043e\u0431\u0437\u043e\u0440\u0430";
            m["MASTER"] = "\u041e\u0411\u0429\u0410\u042f";
            m["MUSIC"] = "\u041c\u0423\u0417\u042b\u041a\u0410";
            m["Window Style"] = "\u0421\u0442\u0438\u043b\u044c \u043e\u043a\u043d\u0430";
            m["Custom Parameters"] = "\u041f\u043e\u043b\u044c\u0437. \u043f\u0430\u0440\u0430\u043c\u0435\u0442\u0440\u044b";
            m["Disabled"] = "\u041e\u0442\u043a\u043b\u044e\u0447\u0435\u043d\u043e";
            m["None"] = "\u041d\u0435\u0442";
            m["none"] = "\u043d\u0435\u0442";
            m["NONE"] = "\u041d\u0415\u0422";
            m["Normal"] = "\u041e\u0431\u044b\u0447\u043d\u044b\u0439";
            m["Turbo"] = "\u0422\u0443\u0440\u0431\u043e";
            m["Kelvin"] = "\u041a\u0435\u043b\u044c\u0432\u0438\u043d";
            m["Enter Text..."] = "\u0412\u0432\u0435\u0434\u0438\u0442\u0435 \u0442\u0435\u043a\u0441\u0442...";

            // --- Main menu & general UI ---
            m["DONE"] = "\u0413\u041e\u0422\u041e\u0412\u041e";
            m["Done"] = "\u0413\u043e\u0442\u043e\u0432\u043e";
            m["CANCEL"] = "\u041e\u0422\u041c\u0415\u041d\u0410";
            m["Cancel"] = "\u041e\u0442\u043c\u0435\u043d\u0430";
            m["LAUNCH"] = "\u0417\u0410\u041f\u0423\u0421\u041a";
            m["LOADING..."] = "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410...";
            m["MAIN MENU"] = "\u0413\u041b\u0410\u0412\u041d\u041e\u0415 \u041c\u0415\u041d\u042e";
            m["MANUALS"] = "\u0420\u0423\u041a\u041e\u0412\u041e\u0414\u0421\u0422\u0412\u0410";
            m["MODS"] = "\u041c\u041e\u0414\u042b";
            m["MODS STATUS"] = "\u0421\u0422\u0410\u0422\u0423\u0421 \u041c\u041e\u0414\u041e\u0412";
            m["SCREENSHOTS"] = "\u0421\u041a\u0420\u0418\u041d\u0428\u041e\u0422\u042b";
            m["OPEN FOLDERS"] = "\u041e\u0422\u041a\u0420\u042b\u0422\u042c \u041f\u0410\u041f\u041a\u0418";
            m["SAVES"] = "\u0421\u041e\u0425\u0420\u0410\u041d\u0415\u041d\u0418\u042f";
            m["Your Captain"] = "\u0412\u0430\u0448 \u043a\u0430\u043f\u0438\u0442\u0430\u043d";
            m["Your Crew"] = "\u0412\u0430\u0448 \u044d\u043a\u0438\u043f\u0430\u0436";
            m["Your Ship"] = "\u0412\u0430\u0448 \u043a\u043e\u0440\u0430\u0431\u043b\u044c";
            m["Start"] = "\u0421\u0442\u0430\u0440\u0442";
            m["Create"] = "\u0421\u043e\u0437\u0434\u0430\u0442\u044c";
            m["Recruit"] = "\u041d\u0430\u043d\u044f\u0442\u044c";
            m["Pilot"] = "\u041f\u0438\u043b\u043e\u0442";
            m["Credits"] = "\u0422\u0438\u0442\u0440\u044b";

            // --- Crew panel labels ---
            m["Current:"] = "\u0422\u0435\u043a\u0443\u0449\u0435\u0435:";
            m["Log:"] = "\u0416\u0443\u0440\u043d\u0430\u043b:";
            m["Captain"] = "\u041a\u0430\u043f\u0438\u0442\u0430\u043d";
            m["Work"] = "\u0420\u0430\u0431\u043e\u0442\u0430";
            m["Rest"] = "\u041e\u0442\u0434\u044b\u0445";
            m["Free"] = "\u0421\u0432\u043e\u0431\u043e\u0434\u043d\u0430\u044f";
            m["Shipbreaker"] = "\u041a\u043e\u0440\u0430\u0431\u043b\u0435\u0440\u0430\u0437\u0431\u043e\u0440\u0449\u0438\u043a";
            m["Prisoner"] = "\u0417\u0430\u043a\u043b\u044e\u0447\u0451\u043d\u043d\u044b\u0439";
            m["Bartender"] = "\u0411\u0430\u0440\u043c\u0435\u043d";
            m["Criminal"] = "\u041f\u0440\u0435\u0441\u0442\u0443\u043f\u043d\u0438\u043a";
            m["Law Enforcement Officer"] = "\u041e\u0444\u0438\u0446\u0435\u0440 \u043f\u0440\u0430\u0432\u043e\u043f\u043e\u0440\u044f\u0434\u043a\u0430";
            m["Manager"] = "\u041c\u0435\u043d\u0435\u0434\u0436\u0435\u0440";
            m["Pirate"] = "\u041f\u0438\u0440\u0430\u0442";
            m["Influencer"] = "\u0418\u043d\u0444\u043b\u044e\u0435\u043d\u0441\u0435\u0440";
            m["Scientist"] = "\u0423\u0447\u0451\u043d\u044b\u0439";
            m["Technician"] = "\u0422\u0435\u0445\u043d\u0438\u043a";
            m["Engineer"] = "\u0418\u043d\u0436\u0435\u043d\u0435\u0440";
            m["Mechanic"] = "\u041c\u0435\u0445\u0430\u043d\u0438\u043a";
            m["Medic"] = "\u041c\u0435\u0434\u0438\u043a";
            m["Electrician"] = "\u042d\u043b\u0435\u043a\u0442\u0440\u0438\u043a";
            m["Hacker"] = "\u0425\u0430\u043a\u0435\u0440";
            m["Smuggler"] = "\u041a\u043e\u043d\u0442\u0440\u0430\u0431\u0430\u043d\u0434\u0438\u0441\u0442";
            m["Fixer"] = "\u0424\u0438\u043a\u0441\u0435\u0440";
            m["Organization"] = "\u041e\u0440\u0433\u0430\u043d\u0438\u0437\u0430\u0446\u0438\u044f";
            m["APPS"] = "\u041f\u0420\u0418\u0411\u041e\u0420\u042b";
            m["Apps"] = "\u041f\u0440\u0438\u0431\u043e\u0440\u044b";
            m["Objective Complete"] = "\u0417\u0430\u0434\u0430\u0447\u0430 \u0432\u044b\u043f\u043e\u043b\u043d\u0435\u043d\u0430";
            m["Objective complete"] = "\u0417\u0430\u0434\u0430\u0447\u0430 \u0432\u044b\u043f\u043e\u043b\u043d\u0435\u043d\u0430";
            m["World unpaused."] = "\u041c\u0438\u0440 \u0441\u043d\u044f\u0442 \u0441 \u043f\u0430\u0443\u0437\u044b.";
            m["World paused."] = "\u041c\u0438\u0440 \u043d\u0430 \u043f\u0430\u0443\u0437\u0435.";
            m["Early Life: OKLG"] = "\u0420\u0430\u043d\u043d\u044f\u044f \u0436\u0438\u0437\u043d\u044c: OKLG";

            // --- XUnity translations now handled by plugin (XUnity text hooks disabled) ---
            m["ZoneCaptainAndCrew"] = "\u0417\u043e\u043d\u0430 \u043a\u0430\u043f\u0438\u0442\u0430\u043d\u0430 \u0438 \u044d\u043a\u0438\u043f\u0430\u0436\u0430";
            m["ZoneCaptain"] = "\u0417\u043e\u043d\u0430 \u043a\u0430\u043f\u0438\u0442\u0430\u043d\u0430";
            m["ZoneCrew"] = "\u0417\u043e\u043d\u0430 \u044d\u043a\u0438\u043f\u0430\u0436\u0430";
            m["Orders"] = "\u041f\u0440\u0438\u043a\u0430\u0437\u044b";
            m["Build"] = "\u0421\u0442\u0440\u043e\u0438\u0442\u044c";
            m["Install"] = "\u0421\u0442\u0440\u043e\u0438\u0442\u044c";
            m["PowerVis"] = "\u042d\u043d\u0435\u0440\u0433\u0438\u044f";
            m["Gigs"] = "\u041f\u043e\u0434\u0440\u0430\u0431\u043e\u0442\u043a\u0438";
            m["Goals"] = "\u0426\u0435\u043b\u0438";
            m["NAVMAP"] = "\u041d\u0410\u0412\u041a\u0410\u0420\u0422\u0410";
            m["NAVLINK"] = "\u041d\u0410\u0412\u0421\u0412\u042f\u0417\u042c";
            m["NONE"] = "\u041d\u0415\u0422";
            m["TEMP"] = "\u0422\u0415\u041c\u041f.";
            m["P.A.S.S"] = "\u041f.\u0410.\u041a.\u0421.";
            m["CONTROLS"] = "\u0423\u041f\u0420\u0410\u0412\u041b\u0415\u041d\u0418\u0415";
            m["FILES"] = "\u0424\u0410\u0419\u041b\u042b";
            m["VIDEO"] = "\u0412\u0418\u0414\u0415\u041e";
            m["RESOLUTION"] = "\u0420\u0410\u0417\u0420\u0415\u0428\u0415\u041d\u0418\u0415";
            m["SCREEN TYPE"] = "\u0422\u0418\u041f \u042d\u041a\u0420\u0410\u041d\u0410";
            m["FULLSCREEN"] = "\u041f\u041e\u041b\u041d\u042b\u0419 \u042d\u041a\u0420\u0410\u041d";
            m["TEMPERATURE UNITS"] = "\u0421\u0418 \u0422\u0415\u041c\u041f\u0415\u0420\u0410\u0422\u0423\u0420\u042b";
            m["CONTROL SETTINGS"] = "\u041d\u0410\u0421\u0422\u0420\u041e\u0419\u041a\u0418 \u0423\u041f\u0420\u0410\u0412\u041b\u0415\u041d\u0418\u042f";
            m["AUTOSAVE SETTINGS:"] = "\u041d\u0410\u0421\u0422\u0420\u041e\u0419\u041a\u0418 \u0410\u0412\u0422\u041e\u0421\u041e\u0425\u0420\u0410\u041d\u0415\u041d\u0418\u042f:";
            m["INTERFACE SETTINGS:"] = "\u041d\u0410\u0421\u0422\u0420\u041e\u0419\u041a\u0418 \u0418\u041d\u0422\u0415\u0420\u0424\u0415\u0419\u0421\u0410:";
            m["VIDEO SETTINGS"] = "\u041d\u0410\u0421\u0422\u0420\u041e\u0419\u041a\u0418 \u0412\u0418\u0414\u0415\u041e";
            m["AUDIO SETTINGS"] = "\u041d\u0410\u0421\u0422\u0420\u041e\u0419\u041a\u0418 \u0417\u0412\u0423\u041a\u0410";
            m["GENERAL SETTINGS:"] = "\u041e\u0411\u0429\u0418\u0415 \u041d\u0410\u0421\u0422\u0420\u041e\u0419\u041a\u0418:";
            m["Autosave interval"] = "\u0418\u043d\u0442\u0435\u0440\u0432\u0430\u043b \u0430\u0432\u0442\u043e\u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u044f";
            m["Max Autosaves Count"] = "\u041c\u0430\u043a\u0441. \u043a\u043e\u043b\u0438\u0447\u0435\u0441\u0442\u0432\u043e \u0430\u0432\u0442\u043e\u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u0439";
            m["DATE AND TIME FORMAT"] = "\u0424\u041e\u0420\u041c\u0410\u0422 \u0414\u0410\u0422\u042b \u0418 \u0412\u0420\u0415\u041c\u0415\u041d\u0418";
            m["Sample text"] = "\u041e\u0431\u0440\u0430\u0437\u0435\u0446 \u0442\u0435\u043a\u0441\u0442\u0430";
            m["Option A"] = "\u0412\u0430\u0440\u0438\u0430\u043d\u0442 A";
            m["What's New?"] = "\u0427\u0442\u043e \u043d\u043e\u0432\u043e\u0433\u043e?";
            m["Early Access!"] = "\u0420\u0430\u043d\u043d\u0438\u0439 \u0434\u043e\u0441\u0442\u0443\u043f!";
            m["PHOTOSENSITIVITY WARNING"] = "\u041f\u0420\u0415\u0414\u0423\u041f\u0420\u0415\u0416\u0414\u0415\u041d\u0418\u0415 \u041e \u0421\u0412\u0415\u0422\u041e\u0427\u0423\u0412\u0421\u0422\u0412\u0418\u0422\u0415\u041b\u042c\u041d\u041e\u0421\u0422\u0418";

            // --- Visual Overlays panel ---
            m["VISUAL OVERLAYS"] = "\u0412\u0418\u0417. \u041d\u0410\u041b\u041e\u0416\u0415\u041d\u0418\u042f";
            m["Visual Overlays"] = "\u0412\u0438\u0437. \u043d\u0430\u043b\u043e\u0436\u0435\u043d\u0438\u044f";
            m["PRESETS"] = "\u0428\u0410\u0411\u041b\u041e\u041d\u042b";
            m["Presets"] = "\u0428\u0430\u0431\u043b\u043e\u043d\u044b";
            m["GRADIENT TYPE"] = "\u0422\u0418\u041f \u0413\u0420\u0410\u0414\u0418\u0415\u041d\u0422\u0410";
            m["Gradient Type"] = "\u0422\u0438\u043f \u0433\u0440\u0430\u0434\u0438\u0435\u043d\u0442\u0430";
            m["POWER"] = "\u042d\u041d\u0415\u0420\u0413\u0418\u042f";
            m["Power"] = "\u042d\u043d\u0435\u0440\u0433\u0438\u044f";
            m["HEAT"] = "\u041d\u0410\u0413\u0420\u0415\u0412";
            m["Heat"] = "\u041d\u0430\u0433\u0440\u0435\u0432";
            m["DAMAGE"] = "\u041f\u041e\u0412\u0420\u0415\u0416\u0414\u0415\u041d\u0418\u042f";
            m["Damage"] = "\u041f\u043e\u0432\u0440\u0435\u0436\u0434\u0435\u043d\u0438\u044f";
            m["VALUE"] = "\u0421\u0422\u041e\u0418\u041c\u041e\u0421\u0422\u042c";
            m["Value"] = "\u0421\u0442\u043e\u0438\u043c\u043e\u0441\u0442\u044c";
            m["OPACITY"] = "\u041d\u0415\u041f\u0420\u041e\u0417\u0420\u0410\u0427\u041d\u041e\u0421\u0422\u042c";
            m["Opacity"] = "\u041d\u0435\u043f\u0440\u043e\u0437\u0440\u0430\u0447\u043d\u043e\u0441\u0442\u044c";
            m["Golden"] = "\u0417\u043e\u043b\u043e\u0442\u043e\u0439";

            // --- Crew duties table ---
            m["Operate"] = "\u0423\u043f\u0440\u0430\u0432\u043b\u044f\u0442\u044c";
            m["OPERATE"] = "\u0423\u041f\u0420\u0410\u0412\u041b\u042f\u0422\u042c";
            m["Restore"] = "\u0412\u043e\u0441\u0441\u0442\u0430\u043d\u043e\u0432\u0438\u0442\u044c";
            m["RESTORE"] = "\u0412\u041e\u0421\u0421\u0422\u0410\u041d\u041e\u0412\u0418\u0422\u042c";
            m["Demolish"] = "\u0421\u043d\u0435\u0441\u0442\u0438";
            m["DEMOLISH"] = "\u0421\u041d\u0415\u0421\u0422\u0418";
            m["Patch"] = "\u0417\u0430\u043f\u043b\u0430\u0442\u043a\u0430";
            m["PATCH"] = "\u0417\u0410\u041f\u041b\u0410\u0422\u041a\u0410";
            m["Repair"] = "\u0420\u0435\u043c\u043e\u043d\u0442";
            m["REPAIR"] = "\u0420\u0415\u041c\u041e\u041d\u0422";
            m["Haul"] = "\u0422\u0430\u0441\u043a\u0430\u0442\u044c";
            m["HAUL"] = "\u0422\u0410\u0421\u041a\u0410\u0422\u042c";
            m["Name"] = "\u0418\u043c\u044f";
            m["NAME"] = "\u0418\u041c\u042f";

            // --- Loading screen full-string translations ---
            m["Spawning System Bodies"] = "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0442\u0435\u043b \u0441\u0438\u0441\u0442\u0435\u043c\u044b";
            m["Spawning System Body Hierarchy"] = "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0438\u0435\u0440\u0430\u0440\u0445\u0438\u0438 \u0442\u0435\u043b \u0441\u0438\u0441\u0442\u0435\u043c\u044b";
            m["Spawning System Companies"] = "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u043a\u043e\u043c\u043f\u0430\u043d\u0438\u0439 \u0441\u0438\u0441\u0442\u0435\u043c\u044b";
            m["Spawning System Stations"] = "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0441\u0442\u0430\u043d\u0446\u0438\u0439 \u0441\u0438\u0441\u0442\u0435\u043c\u044b";
            m["Spawning System Derelicts"] = "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0434\u0435\u0440\u0435\u043b\u0438\u043a\u0442\u043e\u0432";
            m["Spawning System Ships"] = "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u043a\u043e\u0440\u0430\u0431\u043b\u0435\u0439 \u0441\u0438\u0441\u0442\u0435\u043c\u044b";
            m["Parse System Bodies"] = "\u0410\u043d\u0430\u043b\u0438\u0437 \u0442\u0435\u043b \u0441\u0438\u0441\u0442\u0435\u043c\u044b";
            m["Initializing Star System"] = "\u0418\u043d\u0438\u0446\u0438\u0430\u043b\u0438\u0437\u0430\u0446\u0438\u044f \u0437\u0432\u0451\u0437\u0434\u043d\u043e\u0439 \u0441\u0438\u0441\u0442\u0435\u043c\u044b";
            m["Loading Orbital Bodies!"] = "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u043e\u0440\u0431\u0438\u0442\u0430\u043b\u044c\u043d\u044b\u0445 \u0442\u0435\u043b!";
            m["Creating Orbital Bodies"] = "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u043e\u0440\u0431\u0438\u0442\u0430\u043b\u044c\u043d\u044b\u0445 \u0442\u0435\u043b";
            m["Creating Stations"] = "\u0421\u043e\u0437\u0434\u0430\u043d\u0438\u0435 \u0441\u0442\u0430\u043d\u0446\u0438\u0439";
            m["Loading Stations!"] = "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u0441\u0442\u0430\u043d\u0446\u0438\u0439!";
            m["Loading Ships!"] = "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u043a\u043e\u0440\u0430\u0431\u043b\u0435\u0439!";
            m["Loading new ships from JSONs"] = "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u043d\u043e\u0432\u044b\u0445 \u043a\u043e\u0440\u0430\u0431\u043b\u0435\u0439";
            m["Init ship manager"] = "\u0418\u043d\u0438\u0446\u0438\u0430\u043b\u0438\u0437\u0430\u0446\u0438\u044f \u043c\u0435\u043d\u0435\u0434\u0436\u0435\u0440\u0430 \u043a\u043e\u0440\u0430\u0431\u043b\u0435\u0439";
            m["Loading scene"] = "\u0417\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u0441\u0446\u0435\u043d\u044b";
            // UPPERCASE loading screen translations (code does ToUpper)
            m["SPAWNING SYSTEM BODIES"] = "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0422\u0415\u041b \u0421\u0418\u0421\u0422\u0415\u041c\u042b";
            m["SPAWNING SYSTEM BODY HIERARCHY"] = "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0418\u0415\u0420\u0410\u0420\u0425\u0418\u0418 \u0422\u0415\u041b \u0421\u0418\u0421\u0422\u0415\u041c\u042b";
            m["SPAWNING SYSTEM COMPANIES"] = "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u041a\u041e\u041c\u041f\u0410\u041d\u0418\u0419 \u0421\u0418\u0421\u0422\u0415\u041c\u042b";
            m["SPAWNING SYSTEM STATIONS"] = "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0421\u0422\u0410\u041d\u0426\u0418\u0419 \u0421\u0418\u0421\u0422\u0415\u041c\u042b";
            m["SPAWNING SYSTEM DERELICTS"] = "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0414\u0415\u0420\u0415\u041b\u0418\u041a\u0422\u041e\u0412";
            m["SPAWNING SYSTEM SHIPS"] = "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u041a\u041e\u0420\u0410\u0411\u041b\u0415\u0419 \u0421\u0418\u0421\u0422\u0415\u041c\u042b";
            m["PARSE SYSTEM BODIES"] = "\u0410\u041d\u0410\u041b\u0418\u0417 \u0422\u0415\u041b \u0421\u0418\u0421\u0422\u0415\u041c\u042b";
            m["INITIALIZING STAR SYSTEM"] = "\u0418\u041d\u0418\u0426\u0418\u0410\u041b\u0418\u0417\u0410\u0426\u0418\u042f \u0417\u0412\u0401\u0417\u0414\u041d\u041e\u0419 \u0421\u0418\u0421\u0422\u0415\u041c\u042b";
            m["LOADING ORBITAL BODIES!"] = "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410 \u041e\u0420\u0411\u0418\u0422\u0410\u041b\u042c\u041d\u042b\u0425 \u0422\u0415\u041b!";
            m["CREATING ORBITAL BODIES"] = "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u041e\u0420\u0411\u0418\u0422\u0410\u041b\u042c\u041d\u042b\u0425 \u0422\u0415\u041b";
            m["CREATING STATIONS"] = "\u0421\u041e\u0417\u0414\u0410\u041d\u0418\u0415 \u0421\u0422\u0410\u041d\u0426\u0418\u0419";
            m["LOADING STATIONS!"] = "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410 \u0421\u0422\u0410\u041d\u0426\u0418\u0419!";
            m["LOADING SHIPS!"] = "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410 \u041a\u041e\u0420\u0410\u0411\u041b\u0415\u0419!";
            m["LOADING NEW SHIPS FROM JSONS"] = "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410 \u041d\u041e\u0412\u042b\u0425 \u041a\u041e\u0420\u0410\u0411\u041b\u0415\u0419";
            m["INIT SHIP MANAGER"] = "\u0418\u041d\u0418\u0426\u0418\u0410\u041b\u0418\u0417\u0410\u0426\u0418\u042f \u041c\u0415\u041d\u0415\u0414\u0416\u0415\u0420\u0410 \u041a\u041e\u0420\u0410\u0411\u041b\u0415\u0419";
            m["LOADING SCENE"] = "\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410 \u0421\u0426\u0415\u041d\u042b";

            // --- Ship designations ---
            m["Freighter"] = "\u0413\u0440\u0443\u0437\u043e\u0432\u043e\u0435 \u0441\u0443\u0434\u043d\u043e";
            m["Passenger Shuttle"] = "\u041f\u0430\u0441\u0441\u0430\u0436\u0438\u0440\u0441\u043a\u0438\u0439 \u0448\u0430\u0442\u0442\u043b";
            m["Pleasure Craft"] = "\u041f\u0440\u043e\u0433\u0443\u043b\u043e\u0447\u043d\u043e\u0435 \u0441\u0443\u0434\u043d\u043e";
            m["Pleasure Yacht"] = "\u041f\u0440\u043e\u0433\u0443\u043b\u043e\u0447\u043d\u0430\u044f \u044f\u0445\u0442\u0430";
            m["Pleasure Yacht "] = "\u041f\u0440\u043e\u0433\u0443\u043b\u043e\u0447\u043d\u0430\u044f \u044f\u0445\u0442\u0430";
            m["Courier"] = "\u041a\u0443\u0440\u044c\u0435\u0440\u0441\u043a\u043e\u0435 \u0441\u0443\u0434\u043d\u043e";
            m["Salvage Tug"] = "\u0411\u0443\u043a\u0441\u0438\u0440-\u0443\u0442\u0438\u043b\u0438\u0437\u0430\u0442\u043e\u0440";
            m["Salvage Tug  "] = "\u0411\u0443\u043a\u0441\u0438\u0440-\u0443\u0442\u0438\u043b\u0438\u0437\u0430\u0442\u043e\u0440";
            m["Service Tug"] = "\u0421\u0435\u0440\u0432\u0438\u0441\u043d\u044b\u0439 \u0431\u0443\u043a\u0441\u0438\u0440";
            m["Heavy Tug"] = "\u0422\u044f\u0436\u0451\u043b\u044b\u0439 \u0431\u0443\u043a\u0441\u0438\u0440";
            m["Fuel Tug"] = "\u0422\u043e\u043f\u043b\u0438\u0432\u043d\u044b\u0439 \u0431\u0443\u043a\u0441\u0438\u0440";
            m["Gas Tug"] = "\u0413\u0430\u0437\u043e\u0432\u044b\u0439 \u0431\u0443\u043a\u0441\u0438\u0440";
            m["Inspection Pod"] = "\u0418\u043d\u0441\u043f\u0435\u043a\u0446\u0438\u043e\u043d\u043d\u0430\u044f \u043a\u0430\u043f\u0441\u0443\u043b\u0430";
            m["Interceptor"] = "\u041f\u0435\u0440\u0435\u0445\u0432\u0430\u0442\u0447\u0438\u043a";
            m["Shuttle Pod"] = "\u0427\u0435\u043b\u043d\u043e\u0447\u043d\u0430\u044f \u043a\u0430\u043f\u0441\u0443\u043b\u0430";
            m["Survey"] = "\u0418\u0441\u0441\u043b\u0435\u0434\u043e\u0432\u0430\u0442\u0435\u043b\u044c\u0441\u043a\u043e\u0435 \u0441\u0443\u0434\u043d\u043e";
            m["Rapid Courier"] = "\u0421\u043a\u043e\u0440\u043e\u0441\u0442\u043d\u043e\u0439 \u043a\u0443\u0440\u044c\u0435\u0440";
            m["Container Intermodal"] = "\u041a\u043e\u043d\u0442\u0435\u0439\u043d\u0435\u0440-\u0438\u043d\u0442\u0435\u0440\u043c\u043e\u0434\u0430\u043b";
            m["Labour Barge"] = "\u0420\u0430\u0431\u043e\u0447\u0430\u044f \u0431\u0430\u0440\u0436\u0430";
            m["Docking Platform"] = "\u0421\u0442\u044b\u043a\u043e\u0432\u043e\u0447\u043d\u0430\u044f \u043f\u043b\u0430\u0442\u0444\u043e\u0440\u043c\u0430";
            m["Aero Commuter"] = "\u0410\u0442\u043c\u043e\u0441\u0444\u0435\u0440\u043d\u044b\u0439 \u043a\u043e\u043c\u043c\u0443\u0442\u0435\u0440";
            m["Aero Passenger Shuttle"] = "\u0410\u0442\u043c. \u043f\u0430\u0441\u0441\u0430\u0436\u0438\u0440\u0441\u043a\u0438\u0439 \u0448\u0430\u0442\u0442\u043b";
            m["Aero Passenger Shuttle\r\n"] = "\u0410\u0442\u043c. \u043f\u0430\u0441\u0441\u0430\u0436\u0438\u0440\u0441\u043a\u0438\u0439 \u0448\u0430\u0442\u0442\u043b";
            m["Aero Personal Craft"] = "\u0410\u0442\u043c. \u043b\u0438\u0447\u043d\u044b\u0439 \u0430\u043f\u043f\u0430\u0440\u0430\u0442";
            m["Aero Personal Shuttle"] = "\u0410\u0442\u043c. \u043b\u0438\u0447\u043d\u044b\u0439 \u0448\u0430\u0442\u0442\u043b";
            m["Aero Pleasure Craft"] = "\u0410\u0442\u043c. \u043f\u0440\u043e\u0433\u0443\u043b\u043e\u0447\u043d\u044b\u0439 \u0430\u043f\u043f\u0430\u0440\u0430\u0442";
            m["Aero Racing Craft"] = "\u0410\u0442\u043c. \u0433\u043e\u043d\u043e\u0447\u043d\u044b\u0439 \u0430\u043f\u043f\u0430\u0440\u0430\u0442";

            // --- Additional ship designations ---
            m["Aerostat"] = "\u0410\u044d\u0440\u043e\u0441\u0442\u0430\u0442";
            m["Aerostat Residence"] = "\u0410\u044d\u0440\u043e\u0441\u0442\u0430\u0442-\u0440\u0435\u0437\u0438\u0434\u0435\u043d\u0446\u0438\u044f";
            m["Aerostat Station"] = "\u0410\u044d\u0440\u043e\u0441\u0442\u0430\u0442-\u0441\u0442\u0430\u043d\u0446\u0438\u044f";
            m["Asteroid Residence"] = "\u0410\u0441\u0442\u0435\u0440\u043e\u0438\u0434-\u0440\u0435\u0437\u0438\u0434\u0435\u043d\u0446\u0438\u044f";
            m["Orbital"] = "\u041e\u0440\u0431\u0438\u0442\u0430\u043b\u044c\u043d\u044b\u0439";
            m["Orbital Station"] = "\u041e\u0440\u0431\u0438\u0442\u0430\u043b\u044c\u043d\u0430\u044f \u0441\u0442\u0430\u043d\u0446\u0438\u044f";
            m["Ground Station"] = "\u041d\u0430\u0437\u0435\u043c\u043d\u0430\u044f \u0441\u0442\u0430\u043d\u0446\u0438\u044f";
            m["Satellite"] = "\u0421\u043f\u0443\u0442\u043d\u0438\u043a";
            m["Night Club"] = "\u041d\u043e\u0447\u043d\u043e\u0439 \u043a\u043b\u0443\u0431";
            m["Encantado Porto"] = "\u042d\u043d\u043a\u0430\u043d\u0442\u0430\u0434\u043e \u041f\u043e\u0440\u0442\u043e";

            // --- Ship makes (manufacturers) ---
            m["Testudo"] = "\u0422\u0435\u0441\u0442\u0443\u0434\u043e";
            m["Ryokka"] = "\u0420\u0451\u043a\u043a\u0430";
            m["Van Hummel"] = "\u0412\u0430\u043d \u0425\u0443\u043c\u043c\u0435\u043b\u044c";
            m["ASC"] = "\u0410\u041a\u0421";
            m["Mobile Space Systems"] = "\u041c\u043e\u0431. \u041a\u043e\u0441\u043c. \u0421\u0438\u0441\u0442\u0435\u043c\u044b";
            m["Renske International"] = "\u0420\u0435\u043d\u0441\u043a\u0435 \u0418\u043d\u0442\u0435\u0440\u043d\u0435\u0448\u043d\u043b";
            m["Farrow Independent Shipwrights"] = "\u041d\u0435\u0437\u0430\u0432. \u0432\u0435\u0440\u0444\u0438 \u0424\u044d\u0440\u0440\u043e\u0443";
            m["Custom"] = "\u041a\u0430\u0441\u0442\u043e\u043c";
            m["n/a"] = "\u043d/\u0434";
            m["N/A"] = "\u041d/\u0414";
            m["Yes"] = "\u0414\u0430";
            m["No"] = "\u041d\u0435\u0442";

            // --- Ship models ---
            m["Dream"] = "\u041c\u0435\u0447\u0442\u0430";
            m["Halberd"] = "\u0410\u043b\u0435\u0431\u0430\u0440\u0434\u0430";
            m["Hand of God"] = "\u0420\u0443\u043a\u0430 \u0411\u043e\u0433\u0430";
            m["Cobra"] = "\u041a\u043e\u0431\u0440\u0430";
            m["Ocelot"] = "\u041e\u0446\u0435\u043b\u043e\u0442";
            m["Ibex"] = "\u0418\u0431\u0435\u043a\u0441";
            m["Edelweiss"] = "\u042d\u0434\u0435\u043b\u044c\u0432\u0435\u0439\u0441";
            m["Rouncy"] = "\u0420\u0430\u0443\u043d\u0441\u0438";
            m["Katydid"] = "\u041a\u0430\u0442\u0438\u0434\u0438\u0434";
            m["Melody"] = "\u041c\u0435\u043b\u043e\u0434\u0438\u044f";
            m["Squall"] = "\u0428\u043a\u0432\u0430\u043b";
            m["Whistler"] = "\u0423\u0438\u0441\u0442\u043b\u0435\u0440";
            m["Boomerang"] = "\u0411\u0443\u043c\u0435\u0440\u0430\u043d\u0433";
            m["Coffin"] = "\u0413\u0440\u043e\u0431";
            m["Royal Flush"] = "\u0420\u043e\u044f\u043b \u0424\u043b\u0435\u0448";
            m["Pequod"] = "\u041f\u0435\u043a\u043e\u0434";
            m["Lilliput"] = "\u041b\u0438\u043b\u0438\u043f\u0443\u0442";
            m["Flotilla"] = "\u0424\u043b\u043e\u0442\u0438\u043b\u0438\u044f";
            m["Sled"] = "\u0421\u0430\u043d\u0438";
            m["Box Sled"] = "\u0411\u043e\u043a\u0441-\u0421\u0430\u043d\u0438";
            m["Sundancer"] = "\u0421\u0430\u043d\u0434\u0430\u043d\u0441\u0435\u0440";
            m["Sundancer XR"] = "\u0421\u0430\u043d\u0434\u0430\u043d\u0441\u0435\u0440 XR";
            m["Volatile"] = "\u0412\u043e\u043b\u0430\u0442\u0430\u0439\u043b";
            m["Argute"] = "\u0410\u0440\u0433\u044c\u044e\u0442";
            m["Primigenial"] = "\u041f\u0440\u0438\u043c\u0438\u0433\u0435\u043d\u0438\u0430\u043b";
            m["Retrofit"] = "\u0420\u0435\u0442\u0440\u043e\u0444\u0438\u0442";
            // Models with Mk./Class
            m["Charon Mk. I"] = "\u0425\u0430\u0440\u043e\u043d Mk. I";
            m["Ferry Mk. IX"] = "\u041f\u0430\u0440\u043e\u043c Mk. IX";
            m["Myna Mk. I"] = "\u041c\u0430\u0439\u043d\u0430 Mk. I";
            m["Mesa Mk. I"] = "\u041c\u0435\u0441\u0430 Mk. I";
            m["Tombolo Mk. II"] = "\u0422\u043e\u043c\u0431\u043e\u043b\u043e Mk. II";
            m["Tricorn Mk. II"] = "\u0422\u0440\u0438\u043a\u043e\u0440\u043d Mk. II";
            m["Vector Mk. II"] = "\u0412\u0435\u043a\u0442\u043e\u0440 Mk. II";
            m["Vector Mk. III"] = "\u0412\u0435\u043a\u0442\u043e\u0440 Mk. III";
            m["Bulk Lifter Mk. III"] = "\u0411\u0430\u043b\u043a \u041b\u0438\u0444\u0442\u0435\u0440 Mk. III";
            m["Class 14 Inspection Capsule"] = "\u0418\u043d\u0441\u043f. \u043a\u0430\u043f\u0441\u0443\u043b\u0430 \u043a\u043b.14";
            m["Ostrich Aero 4R"] = "\u0421\u0442\u0440\u0430\u0443\u0441 \u0410\u044d\u0440\u043e 4R";
            m["Ostrich Aero 8R"] = "\u0421\u0442\u0440\u0430\u0443\u0441 \u0410\u044d\u0440\u043e 8R";
            m["Li Bai Gen II"] = "\u041b\u0438 \u0411\u0430\u0439 Gen II";
            m["4C Intermodal"] = "4C \u0418\u043d\u0442\u0435\u0440\u043c\u043e\u0434\u0430\u043b";
            m["CR-43 Indie Retrofit"] = "CR-43 \u0418\u043d\u0434\u0438-\u0440\u0435\u0442\u0440\u043e\u0444\u0438\u0442";
            m["Mooring Buoy 01"] = "\u0428\u0432\u0430\u0440\u0442\u043e\u0432\u043d\u044b\u0439 \u0431\u0443\u0439 01";
            m["Perimeter Buoy 01"] = "\u041f\u0435\u0440\u0438\u043c\u0435\u0442\u0440. \u0431\u0443\u0439 01";
            // --- Misc UI labels seen in runtime cache ---
            m["FINISHED"] = "\u0417\u0410\u0412\u0415\u0420\u0428\u0415\u041d\u041e";
            m["QUIT APP"] = "\u0412\u042b\u0425\u041e\u0414 \u0418\u0417 \u0418\u0413\u0420\u042b";
            m["OPACITY"] = "\u041d\u0415\u041f\u0420\u041e\u0417\u0420\u0410\u0427\u041d\u041e\u0421\u0422\u042c";
            m["CURRENT"] = "\u0422\u0415\u041a\u0423\u0429\u0415\u0415";
            m["Settings"] = "\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438";
            m["AUDIO"] = "\u0410\u0423\u0414\u0418\u041e";
            m["Testing"] = "\u0422\u0435\u0441\u0442\u0438\u0440\u043e\u0432\u0430\u043d\u0438\u0435";
            m["Turbo"] = "\u0422\u0443\u0440\u0431\u043e";
            m["Power"] = "\u042d\u043d\u0435\u0440\u0433\u0438\u044f";
            m["Prune NPCs"] = "\u041e\u0447\u0438\u0441\u0442\u0438\u0442\u044c NPC";
            m["AUTOTASK"] = "\u0410\u0412\u0422\u041e\u0417\u0410\u0414\u0410\u0427\u0410";
            m["AUTOPAUSE"] = "\u041f\u0410\u0423\u0417\u0410";
            m["Respawn Ship+NPCs"] = "\u0412\u043e\u0437\u0440\u043e\u0434\u0438\u0442\u044c \u043a\u043e\u0440\u0430\u0431\u043b\u044c+NPC";
            m["WRONG WAY"] = "\u041d\u0415\u0412\u0415\u0420\u041d\u042b\u0419 \u041f\u0423\u0422\u042c";

            // --- Hardcoded UI tokens (Title Case \u2014 TMP SmallCaps shows them as CAPS visually) ---
            // === Group 1: Toolbar buttons (game wraps multi-word text on \n) ===
            m["Disembark"] = "\u0412\u042b\u0421\u0410\u0414\u041a\u0410";
            m["DISEMBARK"] = "\u0412\u042b\u0421\u0410\u0414\u041a\u0410";
            m["Open Airlocks"] = "\u0428\u041b\u042e\u0417\u042b";
            m["OPEN AIRLOCKS"] = "\u0428\u041b\u042e\u0417\u042b";
            m["Open\nAirlocks"] = "\u0428\u041b\u042e\u0417\u042b";
            m["OPEN\nAIRLOCKS"] = "\u0428\u041b\u042e\u0417\u042b";
            m["Open\r\nAirlocks"] = "\u0428\u041b\u042e\u0417\u042b";
            m["OPEN\r\nAIRLOCKS"] = "\u0428\u041b\u042e\u0417\u042b";
            m["Restore Parts"] = "\u0420\u0415\u041c\u041e\u041d\u0422";
            m["RESTORE PARTS"] = "\u0420\u0415\u041c\u041e\u041d\u0422";
            m["Restore\nParts"] = "\u0420\u0415\u041c\u041e\u041d\u0422";
            m["RESTORE\nPARTS"] = "\u0420\u0415\u041c\u041e\u041d\u0422";
            m["Restore\r\nParts"] = "\u0420\u0415\u041c\u041e\u041d\u0422";
            m["RESTORE\r\nPARTS"] = "\u0420\u0415\u041c\u041e\u041d\u0422";

            // === Group 2: Crew Roster headers ===
            m["Idle Crew Member"] = "\u0411\u0435\u0437\u0434\u0435\u043b\u044c\u043d\u0438\u043a";
            m["Hourly Status"] = "\u0421\u043c\u0435\u043d\u0430";
            m["Free"] = "\u0421\u0432\u043e\u0431\u043e\u0434\u043d\u0430";
            m["Sleep"] = "\u0421\u043e\u043d";
            m["Work"] = "\u0420\u0430\u0431\u043e\u0442\u0430";
            m["Permissions"] = "\u0420\u0430\u0437\u0440\u0435\u0448\u0435\u043d\u0438\u044f";

            // === Group 3: Page headers ===
            m["Crew Roster:"] = "\u042d\u041a\u0418\u041f\u0410\u0416:";
            m["CREW ROSTER:"] = "\u042d\u041a\u0418\u041f\u0410\u0416:";

            // === Group 9: Crew Orders & Building panel ===
            m["CREW ORDERS & BUILDING"] = "\u041f\u0420\u0418\u041a\u0410\u0417\u042b \u0418 \u0421\u0422\u0420\u041e\u0419\u041a\u0410";
            m["Crew Orders & Building"] = "\u041f\u0440\u0438\u043a\u0430\u0437\u044b \u0438 \u0421\u0442\u0440\u043e\u0439\u043a\u0430";
            m["HULL"] = "\u041a\u041e\u0420\u041f\u0423\u0421";
            m["Hull"] = "\u041a\u043e\u0440\u043f\u0443\u0441";
            m["HVAC"] = "\u0412\u0415\u041d\u0422\u0418\u041b\u042f\u0426\u0418\u042f";
            m["Hvac"] = "\u0412\u0435\u043d\u0442\u0438\u043b\u044f\u0446\u0438\u044f";
            m["POWR"] = "\u041f\u0418\u0422\u0410\u041d\u0418\u0415";
            m["Powr"] = "\u041f\u0438\u0442\u0430\u043d\u0438\u0435";
            m["SENS"] = "\u0414\u0410\u0422\u0427\u0418\u041a\u0418";
            m["Sens"] = "\u0414\u0430\u0442\u0447\u0438\u043a\u0438";
            m["CTRL"] = "\u0423\u041f\u0420\u0410\u0412\u041b\u0415\u041d\u0418\u0415";
            m["Ctrl"] = "\u0423\u043f\u0440\u0430\u0432\u043b\u0435\u043d\u0438\u0435";
            m["FURN"] = "\u041c\u0415\u0411\u0415\u041b\u042c";
            m["Furn"] = "\u041c\u0435\u0431\u0435\u043b\u044c";
            m["MISC"] = "\u041f\u0420\u041e\u0427\u0415\u0415";
            m["Misc"] = "\u041f\u0440\u043e\u0447\u0435\u0435";
            m["Crew Duties:"] = "\u041e\u0431\u044f\u0437\u0430\u043d\u043d\u043e\u0441\u0442\u0438:";
            m["Crew Tasks"] = "\u0417\u0430\u0434\u0430\u0447\u0438 \u044d\u043a\u0438\u043f\u0430\u0436\u0430";
            m["Visual Overlays"] = "\u0412\u0438\u0437\u0443\u0430\u043b\u044c\u043d\u044b\u0435 \u0441\u043b\u043e\u0438";
            m["Company"] = "\u041a\u043e\u043c\u043f\u0430\u043d\u0438\u044f";
            m["Renbao Console"] = "Renbao \u041a\u043e\u043d\u0441\u043e\u043b\u044c";
            m["Console Renbao"] = "\u041a\u043e\u043d\u0441\u043e\u043b\u044c Renbao";

            // === Group 4: Crew Tasks columns ===
            m["Task"] = "\u0417\u0430\u0434\u0430\u0447\u0430";
            m["Target"] = "\u0426\u0435\u043b\u044c";
            m["Duty"] = "\u0414\u043e\u043b\u0433";
            m["Ship"] = "\u041a\u043e\u0440\u0430\u0431\u043b\u044c";

            // === Group 5: Viz mode (Power already defined above as \u042d\u043d\u0435\u0440\u0433\u0438\u044f) ===
            m["Default"] = "\u041f\u043e \u0443\u043c\u043e\u043b\u0447\u0430\u043d\u0438\u044e";
            m["DEFAULT"] = "\u041f\u041e \u0423\u041c\u041e\u041b\u0427\u0410\u041d\u0418\u042e";
            m["Mass"] = "\u041c\u0430\u0441\u0441\u0430";
            m["MASS"] = "\u041c\u0410\u0421\u0421\u0410";
            m["Price"] = "\u0426\u0435\u043d\u0430";
            m["PRICE"] = "\u0426\u0415\u041d\u0410";
            m["Pressure"] = "\u0414\u0430\u0432\u043b\u0435\u043d\u0438\u0435";
            m["PRESSURE"] = "\u0414\u0410\u0412\u041b\u0415\u041d\u0418\u0415";
            m["Heat"] = "\u041d\u0430\u0433\u0440\u0435\u0432";
            m["HEAT"] = "\u041d\u0410\u0413\u0420\u0415\u0412";
            m["Damage"] = "\u041f\u043e\u0432\u0440\u0435\u0436\u0434\u0435\u043d\u0438\u044f";
            m["DAMAGE"] = "\u041f\u041e\u0412\u0420\u0415\u0416\u0414\u0415\u041d\u0418\u042f";

            // === Group 6: Viz toggles ===
            m["Power Paths"] = "\u0426\u0435\u043f\u0438 \u043f\u0438\u0442\u0430\u043d\u0438\u044f";
            m["POWER PATHS"] = "\u0426\u0415\u041f\u0418 \u041f\u0418\u0422\u0410\u041d\u0418\u042f";
            m["Exteriors"] = "\u042d\u043a\u0441\u0442\u0435\u0440\u044c\u0435\u0440";
            m["EXTERIORS"] = "\u042d\u041a\u0421\u0422\u0415\u0420\u042c\u0415\u0420";
            m["Placeholders"] = "\u0428\u0430\u0431\u043b\u043e\u043d\u044b";
            m["PLACEHOLDERS"] = "\u0428\u0410\u0411\u041b\u041e\u041d\u042b";
            m["Contours"] = "\u041a\u043e\u043d\u0442\u0443\u0440\u044b";
            m["CONTOURS"] = "\u041a\u041e\u041d\u0422\u0423\u0420\u042b";
            m["Tasks"] = "\u0417\u0430\u0434\u0430\u0447\u0438";
            m["TASKS"] = "\u0417\u0410\u0414\u0410\u0427\u0418";
            m["Log Scale"] = "\u041b\u043e\u0433. \u0448\u043a\u0430\u043b\u0430";
            m["LOG SCALE"] = "\u041b\u041e\u0413. \u0428\u041a\u0410\u041b\u0410";
            m["Ceiling"] = "\u041f\u043e\u0442\u043e\u043b\u043e\u043a";
            m["CEILING"] = "\u041f\u041e\u0422\u041e\u041b\u041e\u041a";
            m["Lights"] = "\u041e\u0441\u0432\u0435\u0449\u0435\u043d\u0438\u0435";
            m["LIGHTS"] = "\u041e\u0421\u0412\u0415\u0429\u0415\u041d\u0418\u0415";
            m["Real Time"] = "\u0420\u0435\u0430\u043b\u044c\u043d\u043e\u0435 \u0432\u0440\u0435\u043c\u044f";
            m["REAL TIME"] = "\u0420\u0415\u0410\u041b\u042c\u041d\u041e\u0415 \u0412\u0420\u0415\u041c\u042f";
            m["FOV"] = "\u041f\u043e\u043b\u0435 \u0437\u0440\u0435\u043d\u0438\u044f";

            // === Group 7: Crew Duties tasks ===
            m["Firefight"] = "\u0422\u0443\u0448\u0438\u0442\u044c";
            m["Construct"] = "\u0421\u0442\u0440\u043e\u0438\u0442\u044c";

            // === Group 10: Orders panel toolbar abbreviations ===
            m["CANC"] = "\u041e\u0422\u041c\u041d";
            m["UNIN"] = "\u0414\u0415\u041c\u041d";
            m["SCRA"] = "\u041b\u041e\u041c";
            m["REPR"] = "\u0420\u0415\u041c\u041d";
            m["DISM"] = "\u0420\u0410\u0417\u0411";
            m["MINE"] = "\u0414\u041e\u0411\u0427";
            m["LOAD"] = "\u0417\u0410\u0413\u0420\u0423\u0417\u0418\u0422\u042c";  // load save game (escape menu)
            m["TOGGLE AFFECTED ITEM TYPE(S)"] = "\u0422\u0418\u041f\u042b \u041f\u0420\u0415\u0414\u041c\u0415\u0422\u041e\u0412";

            // === Group 11: Item type filter buttons ===
            m["WALL"] = "\u0421\u0422\u0415\u041d\u042b";
            m["FLOOR"] = "\u041f\u041e\u041b";
            m["CONDUIT"] = "\u041a\u0410\u0411\u0415\u041b\u0418";
            m["CAN"] = "\u0411\u0410\u041b\u041b\u041e\u041d";
            m["EQUIP"] = "\u041e\u0411\u041e\u0420\u0423\u0414";
            m["LOOSE"] = "\u0412\u0415\u0429\u0418";

            // === Group 12: Visual Overlay gradient selector ===
            m["_None"] = "_\u041d\u0435\u0442";
            m["_Highlight"] = "_\u0412\u044b\u0434\u0435\u043b";

            // === Group 13: Escape menu buttons ===
            m["OPTIONS"] = "\u041d\u0410\u0421\u0422\u0420\u041e\u0419\u041a\u0418";
            m["SAVE"] = "\u0421\u041e\u0425\u0420\u0410\u041d\u0418\u0422\u042c";
            m["SHIP EDITOR"] = "\u0420\u0415\u0414\u0410\u041a\u0422\u041e\u0420 \u041a\u041e\u0420\u0410\u0411\u041b\u042f";

            // === Group 14: Chargen UI (previously handled by XUnity static translations) ===
            // Root cause: SetText(string,bool) Harmony patch intercepts before XUnity's MonoMod hook.
            // Fix: translate directly in our exactTranslations to bypass XUnity dependency.
            m["PRONOUN"] = "\u041c\u0415\u0421\u0422\u041e\u0418\u041c\u0415\u041d\u0418\u042f";      // МЕСТОИМЕНИЯ
            m["DONE!"] = "\u0413\u041e\u0422\u041e\u0412\u041e!";                                      // ГОТОВО!
            m["FLIRTS WITH"] = "\u0424\u041b\u0418\u0420\u0422\u0423\u0415\u0422 \u0421";              // ФЛИРТУЕТ С
            m["RANDOMIZE"] = "\u0421\u041b\u0423\u0427\u0410\u0419\u041d\u041e";                       // СЛУЧАЙНО
            m["HE HIM"] = "\u041e\u041d / \u0415\u0413\u041e";                                         // ОН / ЕГО
            m["SHE HER"] = "\u041e\u041d\u0410 / \u0415\u0401";                                        // ОНА / ЕЁ
            m["THEY THEM"] = "\u041e\u041d\u0418 / \u0418\u0425";                                      // ОНИ / ИХ

            // === Group 15: Nav Mods panel (hardcoded in NavModSensorsMFD prefab) ===
            // "All    modules active." has extra spaces from {ls} tag stripping.
            // After multi-space collapse in Clean() these become single-space keys.
            m["Nav Mods"] = "\u041d\u0430\u0432. \u041c\u043e\u0434\u0443\u043b\u0438";               // Нав. Модули
            m["NAV MODS"] = "\u041d\u0410\u0412. \u041c\u041e\u0414\u0423\u041b\u0418";               // НАВ. МОДУЛИ
            m["All modules active."] = "\u0412\u0441\u0435 \u043c\u043e\u0434\u0443\u043b\u0438 \u0430\u043a\u0442\u0438\u0432\u043d\u044b.";  // Все модули активны.
            m["All modules active"] = "\u0412\u0441\u0435 \u043c\u043e\u0434\u0443\u043b\u0438 \u0430\u043a\u0442\u0438\u0432\u043d\u044b";    // Все модули активны
            // "Drag panels" — catch both pure English AND partially-translated (their→их)
            m["Drag panels here to remove them."] = "\u041f\u0435\u0440\u0435\u0442\u0430\u0449\u0438\u0442\u0435 \u043f\u0430\u043d\u0435\u043b\u0438 \u0441\u044e\u0434\u0430 \u0434\u043b\u044f \u0443\u0434\u0430\u043b\u0435\u043d\u0438\u044f.";
            m["Drag panels here to remove them"] = "\u041f\u0435\u0440\u0435\u0442\u0430\u0449\u0438\u0442\u0435 \u043f\u0430\u043d\u0435\u043b\u0438 \u0441\u044e\u0434\u0430 \u0434\u043b\u044f \u0443\u0434\u0430\u043b\u0435\u043d\u0438\u044f";
            m["Drag panels here to remove their panels."] = "\u041f\u0435\u0440\u0435\u0442\u0430\u0449\u0438\u0442\u0435 \u043f\u0430\u043d\u0435\u043b\u0438 \u0441\u044e\u0434\u0430 \u0434\u043b\u044f \u0443\u0434\u0430\u043b\u0435\u043d\u0438\u044f.";
            m["Drag panels here to remove \u0438\u0445."] = "\u041f\u0435\u0440\u0435\u0442\u0430\u0449\u0438\u0442\u0435 \u043f\u0430\u043d\u0435\u043b\u0438 \u0441\u044e\u0434\u0430 \u0434\u043b\u044f \u0443\u0434\u0430\u043b\u0435\u043d\u0438\u044f.";  // "Drag panels here to remove их." (mixed)

            // === Group 16: Fire, interactions, social, docking ===
            m["Fire"] = "\u041e\u0433\u043e\u043d\u044c";
            m["Fire."] = "\u041e\u0433\u043e\u043d\u044c.";  // title label with period appended by game
            m["Extinguish"] = "\u041f\u043e\u0442\u0443\u0448\u0438\u0442\u044c";
            m["Extinguish Fire"] = "\u041f\u043e\u0442\u0443\u0448\u0438\u0442\u044c \u043e\u0433\u043e\u043d\u044c";
            m["Stamp"] = "\u0417\u0430\u0442\u043e\u043f\u0442\u0430\u0442\u044c";
            m["Start Conversation"] = "\u041d\u0430\u0447\u0430\u0442\u044c \u0440\u0430\u0437\u0433\u043e\u0432\u043e\u0440";
            // --- Gambit interaction titles ---
            m["Cold Start"] = "\u0425\u043e\u043b\u043e\u0434\u043d\u044b\u0439 \u0441\u0442\u0430\u0440\u0442";
            m["Charm (Beautiful)"] = "\u041e\u0431\u0430\u044f\u043d\u0438\u0435 (\u041a\u0440\u0430\u0441\u043e\u0442\u0430)";
            m["Talk Shop (Career)"] = "\u041f\u043e\u0433\u043e\u0432\u043e\u0440\u0438\u0442\u044c \u043e \u0434\u0435\u043b\u0435 (\u041a\u0430\u0440\u044c\u0435\u0440\u0430)";
            // --- Docking radio full-string messages ---
            m["<Configuring Docking Procedure>"] = "<\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0430 \u043f\u0440\u043e\u0446\u0435\u0434\u0443\u0440\u044b \u0441\u0442\u044b\u043a\u043e\u0432\u043a\u0438>";
            m["Ready to proceed"] = "\u0413\u043e\u0442\u043e\u0432 \u043a \u0441\u0442\u044b\u043a\u043e\u0432\u043a\u0435";
            m["Init Docking"] = "\u0418\u043d\u0438\u0446\u0438\u0430\u043b\u0438\u0437\u0430\u0446\u0438\u044f \u0441\u0442\u044b\u043a\u043e\u0432\u043a\u0438";
            m["Docking"] = "\u0421\u0442\u044b\u043a\u043e\u0432\u043a\u0430";
            m["Roger"] = "\u041f\u0440\u0438\u043d\u044f\u0442\u043e";
            m["Negative"] = "\u041e\u0442\u043a\u0430\u0437\u0430\u043d\u043e";

            // --- Ship descriptions (loaded from separate file) ---
            Dictionary<string, string> shipDescs = ShipDescriptionTranslations.GetAll();
            foreach (KeyValuePair<string, string> kvp in shipDescs)
                m[kvp.Key] = kvp.Value;

            return m;
        }

        // =====================================================
        // EXTERNAL JSON OVERRIDE API
        // Used by RusPatchPlugin.LoadExternalTranslations()
        // =====================================================

        /// <summary>
        /// Replaces phraseReplacements array with externally loaded data.
        /// </summary>
        public static void SetPhraseReplacements(string[][] phrases)
        {
            phraseReplacements = phrases;
        }

        /// <summary>
        /// Merges external exact translations into the dictionary.
        /// External values override hardcoded ones.
        /// </summary>
        public static void MergeExactTranslations(Dictionary<string, string> external)
        {
            foreach (KeyValuePair<string, string> kvp in external)
                exactTranslations[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// Replaces ship info labels array with externally loaded data.
        /// </summary>
        public static void SetShipInfoLabels(string[] labels)
        {
            shipInfoLabels = labels;
        }

        /// <summary>
        /// Merges external pronoun map entries.
        /// External values override hardcoded ones.
        /// </summary>
        public static void MergePronounMap(Dictionary<string, string> external)
        {
            foreach (KeyValuePair<string, string> kvp in external)
                pronounMap.AddOrUpdate(kvp.Key, kvp.Value, (_, __) => kvp.Value);
        }
    }
}

