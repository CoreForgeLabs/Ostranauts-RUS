using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OstranautsRusPatch
{
    public static partial class RussianTextCleaner
    {
        private static bool IsInSubordinateClause(Match m, string fullText)
        {
            int searchStart = System.Math.Max(0, m.Index - 120);
            string before = fullText.Substring(searchStart, m.Index - searchStart);
            int vyPos = before.LastIndexOf("\u0412\u044b");
            if (vyPos < 0) vyPos = before.LastIndexOf("\u0432\u044b");
            if (vyPos >= 0 && rxSubordClause.IsMatch(before.Substring(vyPos)))
                return true;
            return false;
        }

        // Russian prepositions: words after them are nouns, not verbs.
        // Prevents false conjugation like "на стул" → "на стули".
        private static bool IsPrecededByPreposition(Match m, string fullText)
        {
            int pos = m.Index - 1;
            if (pos < 0 || fullText[pos] != ' ') return false;
            pos--; // skip space before match
            if (pos < 0) return false;
            // Read previous word backwards
            int wordEnd = pos + 1;
            while (pos >= 0 && ((fullText[pos] >= '\u0430' && fullText[pos] <= '\u044f') ||
                                (fullText[pos] >= '\u0410' && fullText[pos] <= '\u042f') ||
                                fullText[pos] == '\u0451' || fullText[pos] == '\u0401'))
                pos--;
            if (pos + 1 >= wordEnd) return false;
            string prev = fullText.Substring(pos + 1, wordEnd - pos - 1);
            // Check against known prepositions (case-insensitive via lowercase check)
            string lc = prev.Length <= 6 ? prev.ToLower() : "";
            return lc == "\u043d\u0430" || lc == "\u0432" || lc == "\u0432\u043e" ||
                   lc == "\u0441" || lc == "\u0441\u043e" || lc == "\u043f\u043e" ||
                   lc == "\u043a" || lc == "\u043a\u043e" || lc == "\u0437\u0430" ||
                   lc == "\u0438\u0437" || lc == "\u043e\u0442" || lc == "\u0434\u043e" ||
                   lc == "\u043e\u0431" || lc == "\u043e\u0431\u043e" || lc == "\u043f\u0440\u0438" ||
                   lc == "\u0431\u0435\u0437" || lc == "\u0434\u043b\u044f" || lc == "\u043f\u043e\u0434" ||
                   lc == "\u043d\u0430\u0434" || lc == "\u0443" || lc == "\u043e" ||
                   lc == "\u0447\u0435\u0440\u0435\u0437" || lc == "\u043f\u0435\u0440\u0435\u0434" ||
                   lc == "\u043c\u0435\u0436\u0434\u0443";
        }

        private static string VyReplaceSafe(Regex rx, string text, string suffix)
        {
            return rx.Replace(text, m =>
            {
                if (IsInSubordinateClause(m, text)) return m.Value;
                if (IsPrecededByPreposition(m, text)) return m.Value;
                return m.Groups[1].Value + suffix;
            });
        }

        // --- Coordinate verb conjugation: second-pass for verbs after "и" in "Вы" sentences ---
        // Handles: "Вы любовались ... и не может повторить" → "не можете повторить"
        // First pass (VyReplaceSafe) only reaches verbs within 5 words of "Вы".
        // This second pass conjugates 3rd-person verbs after "и" when "Вы" is the subject.
        // Only applies to present-tense 3rd-person singular (ет/ёт/ит → ете/ёте/ите).
        private static readonly Regex rxCoordVerb = new Regex(
            "(\u0438\\s+(?:\u043d\u0435\\s+|\u043f\u043e\u043a\u0430\\s+\u043d\u0435\\s+|\u0442\u0430\u043a\u0436\u0435\\s+|\u0442\u043e\u0436\u0435\\s+)?)" +  // и (не |пока не |также |тоже )?
            "([\u0430-\u044f\u0451]+?)(\u0435\u0442|\u0451\u0442|\u0438\u0442)\\b",  // verb stem + ет/ёт/ит
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Conjugates 3rd-person present verbs after "и" in sentences starting with "Вы".
        /// "Вы любовались ... и не может повторить" → "и не можете повторить"
        /// Safe: only fires when text starts with "Вы " (player subject).
        /// </summary>
        private static string FixCoordinateVyVerbs(string text)
        {
            if (!text.StartsWith("\u0412\u044b ")) return text;
            return rxCoordVerb.Replace(text, new MatchEvaluator(CoordVerbEvaluator));
        }

        private static string CoordVerbEvaluator(Match m)
        {
            string prefix = m.Groups[1].Value; // "и не " etc.
            string stem = m.Groups[2].Value;
            string ending = m.Groups[3].Value;

            string newEnding;
            if (ending == "\u0435\u0442")       // ет → ете
                newEnding = "\u0435\u0442\u0435";
            else if (ending == "\u0451\u0442")   // ёт → ёте
                newEnding = "\u0451\u0442\u0435";
            else if (ending == "\u0438\u0442")   // ит → ите
                newEnding = "\u0438\u0442\u0435";
            else
                return m.Value;

            return prefix + stem + newEnding;
        }

        /// <summary>
        /// Post-conjugation fix: "Вы имеете X" → "У вас X" (more natural Russian).
        /// After Vy-conjugation converts "Вы имеет" → "Вы имеете", this method
        /// transforms the awkward "Вы имеете" into the natural "У вас" construction.
        /// </summary>
        private static string FixVyImeete(string text)
        {
            // "Вы имеете " → "У вас "
            if (text.IndexOf("\u0412\u044b \u0438\u043c\u0435\u0435\u0442\u0435 ") >= 0)
                text = text.Replace("\u0412\u044b \u0438\u043c\u0435\u0435\u0442\u0435 ", "\u0423 \u0432\u0430\u0441 ");
            // "вы имеете " → "у вас "
            if (text.IndexOf("\u0432\u044b \u0438\u043c\u0435\u0435\u0442\u0435 ") >= 0)
                text = text.Replace("\u0432\u044b \u0438\u043c\u0435\u0435\u0442\u0435 ", "\u0443 \u0432\u0430\u0441 ");
            return text;
        }

        // Replace English "with" between Cyrillic words (combat: "побил X плечо with удар" → "побил X плечо: удар")
        private static readonly Regex rxCyrWithPrep = new Regex(
            "(?<=[\u0430-\u044f\u0451\u0410-\u042f\u0401).\\]0-9])\\s+with\\s+(?=[\u0430-\u044f\u0451\u0410-\u042f\u0401\\[(\u00ab])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // Replace English "now" between Cyrillic words ("Алфаро now обдумывает" → "Алфаро теперь обдумывает")
        private static readonly Regex rxCyrNowAdv = new Regex(
            "(?<=[\u0430-\u044f\u0451\u0410-\u042f\u0401).\\]0-9])\\s+now\\s+(?=[\u0430-\u044f\u0451\u0410-\u042f\u0401\\[(\u00ab])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // Replace English " gains " between Cyrillic words
        private static readonly Regex rxCyrGains = new Regex(
            "(?<=[\u0430-\u044f\u0451\u0410-\u042f\u0401).\\]0-9])\\s+gains\\s+(?=[\u0430-\u044f\u0451\u0410-\u042f\u0401\\[(\u00ab])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // Replace English " loses " between Cyrillic words
        private static readonly Regex rxCyrLoses = new Regex(
            "(?<=[\u0430-\u044f\u0451\u0410-\u042f\u0401).\\]0-9])\\s+loses\\s+(?=[\u0430-\u044f\u0451\u0410-\u042f\u0401\\[(\u00ab])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);


        /// <summary>
        /// Noun declension table: nominative → { case → form }
        /// Used by grammar engine for noun case transformations.
        /// Cases: acc (accusative), gen (genitive), dat (dative),
        ///        inst (instrumental), prep (prepositional)
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> _nounTable =
            new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Sets the noun declension table from external JSON.</summary>
        public static void SetNounTable(Dictionary<string, Dictionary<string, string>> nouns)
        {
            _nounTable = nouns;
            BuildMultiWordNounRegex();
        }

        /// <summary>
        /// Looks up a noun's declined form for the given grammatical case.
        /// Returns null if noun or case not found.
        /// </summary>
        public static string GetNounForm(string nominative, string grammaticalCase)
        {
            Dictionary<string, string> cases;
            if (_nounTable.TryGetValue(nominative, out cases))
            {
                string form;
                if (cases.TryGetValue(grammaticalCase, out form))
                    return form;
            }
            return null;
        }

        /// <summary>
        /// Returns the correct count form for a noun and number n.
        /// Uses Russian grammar rules:
        ///   n%10==1 &amp;&amp; n%100!=11          → nominative singular  (1 корабль)
        ///   n%10 in [2,3,4] &amp;&amp; n%100∉[12-14] → genitive singular    (2 корабля)
        ///   otherwise                          → genitive plural     (5 кораблей)
        /// The nominative singular is the dictionary key itself.
        /// </summary>
        public static string GetCountForm(string noun, int n)
        {
            int last2 = n % 100;
            int last1 = n % 10;
            if (last1 == 1 && last2 != 11)
                return noun; // nominative singular = the key itself
            Dictionary<string, string> cases;
            if (!_nounTable.TryGetValue(noun, out cases))
                return noun;
            if (last1 >= 2 && last1 <= 4 && (last2 < 12 || last2 > 14))
            {
                string sgGen;
                return cases.TryGetValue("gen", out sgGen) ? sgGen : noun; // genitive singular (existing field)
            }
            string plGen;
            return cases.TryGetValue("pl_gen", out plGen) ? plGen : noun; // genitive plural (new field)
        }

        // --- Gender bracket markers: "Умер(ла)" → "Умер", "потерян(а)" → "потерян" ---
        // These come from mod JSON files (strings.json, conditions_simple.json)
        // Default to masculine form by stripping the bracketed feminine suffix
        private static readonly Regex rxGenderBracket = new Regex(
            "([\u0430-\u044f\u0451])\\(([\u0430-\u044f\u0451]{1,4})\\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Strips gender bracket markers like "Умер(ла)" → "Умер", "ответил(а)" → "ответил".
        /// These markers come from mod JSON data and are not valid display text.
        /// </summary>
        private static string StripGenderBrackets(string text)
        {
            if (text.IndexOf("(") < 0) return text;
            return rxGenderBracket.Replace(text, "$1");
        }

        // --- Noun declension via dictionary lookup ---
        // Matches: preposition + space(s) + Cyrillic word (upper or lowercase)
        private static readonly Regex rxPrepNounDecl = new Regex(
            "\\b(\u0438\u0437|\u0434\u043b\u044f|\u0434\u043e|\u043e\u0442|\u0431\u0435\u0437|\u043f\u043e\u0441\u043b\u0435|\u043e\u043a\u043e\u043b\u043e|\u0432\u043e\u0437\u043b\u0435|\u043a\u0440\u043e\u043c\u0435|\u0440\u0430\u0434\u0438|\u0432\u043c\u0435\u0441\u0442\u043e|\u0443" +
            "|\u043a|\u043a\u043e|\u043f\u043e" +
            "|\u0441|\u0441\u043e|\u043d\u0430\u0434|\u043f\u043e\u0434|\u043f\u0435\u0440\u0435\u0434|\u043c\u0435\u0436\u0434\u0443" +
            "|\u0432|\u0432\u043e|\u043d\u0430|\u043e|\u043e\u0431|\u043f\u0440\u0438" +
            "|\u0447\u0435\u0440\u0435\u0437|\u043f\u0440\u043e|\u0437\u0430)\\s+([\u0410-\u042f\u0401\u0430-\u044f\u0451][\u0430-\u044f\u0451]+)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Applies noun declension from the dictionary table.
        /// Matches preposition + known noun and replaces with correct form.
        /// Must run BEFORE regex-based declension (which acts as fallback for unknown nouns).
        /// </summary>
        private static string ApplyNounDeclension(string text)
        {
            if (_nounTable == null || _nounTable.Count == 0) return text;
            return rxPrepNounDecl.Replace(text, new MatchEvaluator(NounDeclEvaluator));
        }

        private static string NounDeclEvaluator(Match m)
        {
            string prep = m.Groups[1].Value;
            string noun = m.Groups[2].Value;

            Dictionary<string, string> cases;
            if (!_nounTable.TryGetValue(noun, out cases))
                return m.Value; // noun not in table, leave for regex fallback

            string prepLower = prep.ToLowerInvariant();
            string targetCase;

            // Determine grammatical case from preposition
            switch (prepLower)
            {
                // Genitive prepositions
                case "\u0438\u0437":     // из
                case "\u0434\u043b\u044f": // для
                case "\u0434\u043e":     // до
                case "\u043e\u0442":     // от
                case "\u0431\u0435\u0437": // без
                case "\u043f\u043e\u0441\u043b\u0435": // после
                case "\u043e\u043a\u043e\u043b\u043e": // около
                case "\u0432\u043e\u0437\u043b\u0435": // возле
                case "\u043a\u0440\u043e\u043c\u0435": // кроме
                case "\u0440\u0430\u0434\u0438": // ради
                case "\u0432\u043c\u0435\u0441\u0442\u043e": // вместо
                case "\u0443":           // у
                    targetCase = "gen";
                    break;

                // Dative prepositions
                case "\u043a":           // к
                case "\u043a\u043e":     // ко
                case "\u043f\u043e":     // по
                    targetCase = "dat";
                    break;

                // Instrumental prepositions
                case "\u0441":           // с
                case "\u0441\u043e":     // со
                case "\u043d\u0430\u0434": // над
                case "\u043f\u043e\u0434": // под
                case "\u043f\u0435\u0440\u0435\u0434": // перед
                case "\u043c\u0435\u0436\u0434\u0443": // между
                    targetCase = "inst";
                    break;

                // Prepositional prepositions
                case "\u0432":           // в
                case "\u0432\u043e":     // во
                case "\u043e":           // о
                case "\u043e\u0431":     // об
                case "\u043f\u0440\u0438": // при
                    targetCase = "prep";
                    break;

                // "на" — dual case: motion → acc, state → prep.
                // Handled separately by ApplyStateVerbPrepNoun(), skip here.

                // Accusative prepositions
                case "\u0447\u0435\u0440\u0435\u0437": // через
                case "\u043f\u0440\u043e": // про
                    targetCase = "acc";
                    break;

                // "за" — in game context usually "behind" (instrumental)
                // "за Дверью" = behind the Door
                case "\u0437\u0430":     // за
                    targetCase = "inst";
                    break;

                default:
                    return m.Value;
            }

            string form;
            if (cases.TryGetValue(targetCase, out form))
                return prep + " " + form;
            return m.Value;
        }

        // --- Multi-word noun declension support ---
        // The single-word rxPrepNounDecl only matches "preposition + one word".
        // Many game item names are multi-word: "Лазерная решётка", "Активная зона" etc.
        // These regexes are built at SetNounTable() time with all multi-word keys.
        private static Regex _rxMultiWordPrepDecl;
        private static Regex _rxMultiWordVerbAcc;

        /// <summary>
        /// Builds regexes for multi-word noun declension at noun-table load time.
        /// Called from SetNounTable(). Sorts alternation longest-first for correct priority.
        /// </summary>
        private static void BuildMultiWordNounRegex()
        {
            _rxMultiWordPrepDecl = null;
            _rxMultiWordVerbAcc = null;

            if (_nounTable == null || _nounTable.Count == 0) return;

            // Collect multi-word noun keys
            List<string> multiWordNouns = new List<string>();
            foreach (string key in _nounTable.Keys)
            {
                if (key.IndexOf(' ') >= 0)
                    multiWordNouns.Add(key);
            }
            if (multiWordNouns.Count == 0) return;

            // Sort longest first for correct alternation priority
            multiWordNouns.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });

            // Build noun alternation with regex escaping
            StringBuilder nounAlts = new StringBuilder();
            for (int i = 0; i < multiWordNouns.Count; i++)
            {
                if (i > 0) nounAlts.Append("|");
                nounAlts.Append(Regex.Escape(multiWordNouns[i]));
            }
            string nounPattern = nounAlts.ToString();

            // 1) Preposition + multi-word noun  (same prep list as rxPrepNounDecl)
            string preps =
                "\u0438\u0437|\u0434\u043b\u044f|\u0434\u043e|\u043e\u0442|\u0431\u0435\u0437" +
                "|\u043f\u043e\u0441\u043b\u0435|\u043e\u043a\u043e\u043b\u043e|\u0432\u043e\u0437\u043b\u0435" +
                "|\u043a\u0440\u043e\u043c\u0435|\u0440\u0430\u0434\u0438|\u0432\u043c\u0435\u0441\u0442\u043e|\u0443" +
                "|\u043a|\u043a\u043e|\u043f\u043e" +
                "|\u0441|\u0441\u043e|\u043d\u0430\u0434|\u043f\u043e\u0434|\u043f\u0435\u0440\u0435\u0434|\u043c\u0435\u0436\u0434\u0443" +
                "|\u0432|\u0432\u043e|\u043d\u0430|\u043e|\u043e\u0431|\u043f\u0440\u0438" +
                "|\u0447\u0435\u0440\u0435\u0437|\u043f\u0440\u043e|\u0437\u0430";

            _rxMultiWordPrepDecl = new Regex(
                "\\b(" + preps + ")\\s+(" + nounPattern + ")(?=[\\s,\\.!?;:\\)\"\\]]|$)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            // 2) Verb ending + multi-word noun (for accusative case)
            string verbEnd =
                "[\u0430-\u044f\u0451](?:\u0435\u0442|\u0451\u0442|\u0438\u0442|\u0430\u0442|\u044f\u0442" +
                "|\u0435\u0442\u0435|\u0438\u0442\u0435|\u0451\u0442\u0435|\u0430\u0442\u0435|\u044f\u0442\u0435" +
                "|\u0430\u043b|\u0438\u043b|\u0435\u043b|\u044f\u043b|\u0443\u043b|\u043e\u043b)";

            _rxMultiWordVerbAcc = new Regex(
                "(" + verbEnd + ")\\s+(" + nounPattern + ")(?=[\\s,\\.!?;:\\)\"\\]]|$)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Applies noun declension to multi-word nouns after prepositions.
        /// "к Лазерная решётка" → "к Лазерной решётке"
        /// Must run BEFORE single-word ApplyNounDeclension().
        /// </summary>
        private static string ApplyMultiWordNounDeclension(string text)
        {
            if (_rxMultiWordPrepDecl == null) return text;
            return _rxMultiWordPrepDecl.Replace(text, new MatchEvaluator(MultiWordPrepDeclEvaluator));
        }

        private static string MultiWordPrepDeclEvaluator(Match m)
        {
            string prep = m.Groups[1].Value;
            string noun = m.Groups[2].Value;

            Dictionary<string, string> cases;
            if (!_nounTable.TryGetValue(noun, out cases))
                return m.Value;

            string prepLower = prep.ToLowerInvariant();
            string targetCase;

            // Same case logic as NounDeclEvaluator
            switch (prepLower)
            {
                // Genitive
                case "\u0438\u0437": case "\u0434\u043b\u044f": case "\u0434\u043e":
                case "\u043e\u0442": case "\u0431\u0435\u0437": case "\u043f\u043e\u0441\u043b\u0435":
                case "\u043e\u043a\u043e\u043b\u043e": case "\u0432\u043e\u0437\u043b\u0435":
                case "\u043a\u0440\u043e\u043c\u0435": case "\u0440\u0430\u0434\u0438":
                case "\u0432\u043c\u0435\u0441\u0442\u043e": case "\u0443":
                    targetCase = "gen"; break;
                // Dative
                case "\u043a": case "\u043a\u043e": case "\u043f\u043e":
                    targetCase = "dat"; break;
                // Instrumental
                case "\u0441": case "\u0441\u043e": case "\u043d\u0430\u0434":
                case "\u043f\u043e\u0434": case "\u043f\u0435\u0440\u0435\u0434":
                case "\u043c\u0435\u0436\u0434\u0443":
                    targetCase = "inst"; break;
                // Prepositional
                case "\u0432": case "\u0432\u043e": case "\u043e":
                case "\u043e\u0431": case "\u043f\u0440\u0438":
                    targetCase = "prep"; break;
                // Accusative
                case "\u0447\u0435\u0440\u0435\u0437": case "\u043f\u0440\u043e":
                    targetCase = "acc"; break;
                // "за" — instrumental in game context
                case "\u0437\u0430":
                    targetCase = "inst"; break;
                default:
                    return m.Value;
            }

            string form;
            if (cases.TryGetValue(targetCase, out form))
                return prep + " " + form;
            return m.Value;
        }

        /// <summary>
        /// Applies accusative case to multi-word nouns after transitive verbs.
        /// "подаёт Лазерная решётка" → "подаёт Лазерную решётку"
        /// Must run BEFORE single-word ApplyVerbAccusative().
        /// </summary>
        private static string ApplyMultiWordVerbAccusative(string text)
        {
            if (_rxMultiWordVerbAcc == null) return text;
            return _rxMultiWordVerbAcc.Replace(text, new MatchEvaluator(MultiWordVerbAccEvaluator));
        }

        private static string MultiWordVerbAccEvaluator(Match m)
        {
            string verbEnd = m.Groups[1].Value;
            string noun = m.Groups[2].Value;

            Dictionary<string, string> cases;
            if (!_nounTable.TryGetValue(noun, out cases))
                return m.Value;

            string form;
            if (cases.TryGetValue("acc", out form))
                return verbEnd + " " + form;
            return m.Value;
        }

        // --- Accusative: verb + direct object noun (dictionary-based) ---
        // Matches: Cyrillic verb ending (ет/ёт/ит/ат/ят/ете/ите/ёте or past -ал/-ил/-ел/-ял/-ал/-ул)
        // + space + Cyrillic noun (upper or lowercase) that IS in the dictionary
        private static readonly Regex rxVerbDirectObj = new Regex(
            "([\u0430-\u044f\u0451](?:\u0435\u0442|\u0451\u0442|\u0438\u0442|\u0430\u0442|\u044f\u0442" +
            "|\u0435\u0442\u0435|\u0438\u0442\u0435|\u0451\u0442\u0435|\u0430\u0442\u0435|\u044f\u0442\u0435" +
            "|\u0430\u043b|\u0438\u043b|\u0435\u043b|\u044f\u043b|\u0443\u043b|\u043e\u043b))\\s+([\u0410-\u042f\u0401\u0430-\u044f\u0451][\u0430-\u044f\u0451]+)(?=[\\s,\\.!?;:\\)]|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Applies accusative case to known nouns after transitive verbs.
        /// "берёте Лампа" → "берёте Лампу", "поднимает Батарея" → "поднимает Батарею"
        /// Safe: only affects nouns in the dictionary (no false positives).
        /// </summary>
        private static string ApplyVerbAccusative(string text)
        {
            if (_nounTable == null || _nounTable.Count == 0) return text;
            return rxVerbDirectObj.Replace(text, new MatchEvaluator(VerbAccEvaluator));
        }

        private static string VerbAccEvaluator(Match m)
        {
            string verbEnd = m.Groups[1].Value;
            string noun = m.Groups[2].Value;

            Dictionary<string, string> cases;
            if (!_nounTable.TryGetValue(noun, out cases))
                return m.Value; // unknown noun, leave as-is

            string accForm;
            if (cases.TryGetValue("acc", out accForm))
                return verbEnd + " " + accForm;
            return m.Value;
        }

        // --- Instrumental: specific verbs requiring instrumental case for their object ---
        // "любуетесь горшок" → "любуетесь горшком", "пользуется лампа" → "пользуется лампой"
        // Verbs: любоваться, восхищаться, пользоваться, наслаждаться, управлять, владеть,
        //        гордиться, дорожить, интересоваться, заниматься
        private static readonly Regex rxVerbInstObj = new Regex(
            "(\u043b\u044e\u0431\u0443\u0435\u0442\u0441\u044f|\u043b\u044e\u0431\u0443\u0435\u0442\u0435\u0441\u044c|\u043b\u044e\u0431\u0443\u044e\u0442\u0441\u044f" +  // любуется/етесь/ются
            "|\u0432\u043e\u0441\u0445\u0438\u0449\u0430\u0435\u0442\u0441\u044f|\u0432\u043e\u0441\u0445\u0438\u0449\u0430\u0435\u0442\u0435\u0441\u044c|\u0432\u043e\u0441\u0445\u0438\u0449\u0430\u044e\u0442\u0441\u044f" +  // восхищается/етесь/ются
            "|\u043f\u043e\u043b\u044c\u0437\u0443\u0435\u0442\u0441\u044f|\u043f\u043e\u043b\u044c\u0437\u0443\u0435\u0442\u0435\u0441\u044c|\u043f\u043e\u043b\u044c\u0437\u0443\u044e\u0442\u0441\u044f" +  // пользуется/етесь/ются
            "|\u043d\u0430\u0441\u043b\u0430\u0436\u0434\u0430\u0435\u0442\u0441\u044f|\u043d\u0430\u0441\u043b\u0430\u0436\u0434\u0430\u0435\u0442\u0435\u0441\u044c|\u043d\u0430\u0441\u043b\u0430\u0436\u0434\u0430\u044e\u0442\u0441\u044f" +  // наслаждается/етесь/ются
            "|\u0443\u043f\u0440\u0430\u0432\u043b\u044f\u0435\u0442|\u0443\u043f\u0440\u0430\u0432\u043b\u044f\u0435\u0442\u0435|\u0443\u043f\u0440\u0430\u0432\u043b\u044f\u044e\u0442" +  // управляет/ете/ют
            "|\u0432\u043b\u0430\u0434\u0435\u0435\u0442|\u0432\u043b\u0430\u0434\u0435\u0435\u0442\u0435|\u0432\u043b\u0430\u0434\u0435\u044e\u0442" +  // владеет/ете/ют
            "|\u0433\u043e\u0440\u0434\u0438\u0442\u0441\u044f|\u0433\u043e\u0440\u0434\u0438\u0442\u0435\u0441\u044c|\u0433\u043e\u0440\u0434\u044f\u0442\u0441\u044f" +  // гордится/итесь/ятся
            "|\u0434\u043e\u0440\u043e\u0436\u0438\u0442|\u0434\u043e\u0440\u043e\u0436\u0438\u0442\u0435|\u0434\u043e\u0440\u043e\u0436\u0430\u0442" +  // дорожит/ите/ат
            "|\u0438\u043d\u0442\u0435\u0440\u0435\u0441\u0443\u0435\u0442\u0441\u044f|\u0438\u043d\u0442\u0435\u0440\u0435\u0441\u0443\u0435\u0442\u0435\u0441\u044c|\u0438\u043d\u0442\u0435\u0440\u0435\u0441\u0443\u044e\u0442\u0441\u044f" +  // интересуется/етесь/ются
            "|\u0437\u0430\u043d\u0438\u043c\u0430\u0435\u0442\u0441\u044f|\u0437\u0430\u043d\u0438\u043c\u0430\u0435\u0442\u0435\u0441\u044c|\u0437\u0430\u043d\u0438\u043c\u0430\u044e\u0442\u0441\u044f" +  // занимается/етесь/ются
            ")\\s+([\u0410-\u042f\u0401\u0430-\u044f\u0451][\u0430-\u044f\u0451]+)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Applies instrumental case to known nouns after verbs that require instrumental.
        /// "любуетесь горшок" → "любуетесь горшком"
        /// </summary>
        private static string ApplyVerbInstrumental(string text)
        {
            if (_nounTable == null || _nounTable.Count == 0) return text;
            return rxVerbInstObj.Replace(text, new MatchEvaluator(VerbInstEvaluator));
        }

        private static string VerbInstEvaluator(Match m)
        {
            string verb = m.Groups[1].Value;
            string noun = m.Groups[2].Value;

            Dictionary<string, string> cases;
            if (!_nounTable.TryGetValue(noun, out cases))
                return m.Value;

            string instForm;
            if (cases.TryGetValue("inst", out instForm))
                return verb + " " + instForm;
            return m.Value;
        }

        // --- Genitive after instrumental nouns: "деталями горшок" → "деталями горшка" ---
        // When an instrumental noun phrase precedes a nominative noun, the second noun should be genitive
        private static readonly Regex rxGenAfterInstNoun = new Regex(
            "(\u0434\u0435\u0442\u0430\u043b\u044f\u043c\u0438|\u043f\u043e\u0434\u0440\u043e\u0431\u043d\u043e\u0441\u0442\u044f\u043c\u0438" +  // деталями|подробностями
            "|\u043a\u0430\u0447\u0435\u0441\u0442\u0432\u0430\u043c\u0438|\u0441\u0432\u043e\u0439\u0441\u0442\u0432\u0430\u043c\u0438" +  // качествами|свойствами
            "|\u043e\u0441\u043e\u0431\u0435\u043d\u043d\u043e\u0441\u0442\u044f\u043c\u0438|\u043a\u043e\u043c\u043f\u043e\u043d\u0435\u043d\u0442\u0430\u043c\u0438" +  // особенностями|компонентами
            "|\u0447\u0430\u0441\u0442\u044f\u043c\u0438|\u043f\u0430\u0440\u0430\u043c\u0435\u0442\u0440\u0430\u043c\u0438" +  // частями|параметрами
            "|\u0445\u0430\u0440\u0430\u043a\u0442\u0435\u0440\u0438\u0441\u0442\u0438\u043a\u0430\u043c\u0438|\u0444\u0443\u043d\u043a\u0446\u0438\u044f\u043c\u0438" +  // характеристиками|функциями
            "|\u0434\u043e\u0441\u0442\u043e\u0438\u043d\u0441\u0442\u0432\u0430\u043c\u0438|\u043d\u0435\u0434\u043e\u0441\u0442\u0430\u0442\u043a\u0430\u043c\u0438" +  // достоинствами|недостатками
            ")\\s+([\u0410-\u042f\u0401\u0430-\u044f\u0451][\u0430-\u044f\u0451]+)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Applies genitive case to known nouns after instrumental-form nouns.
        /// "деталями горшок" → "деталями горшка"
        /// </summary>
        private static string ApplyGenAfterInstNoun(string text)
        {
            if (_nounTable == null || _nounTable.Count == 0) return text;
            return rxGenAfterInstNoun.Replace(text, new MatchEvaluator(GenAfterInstEvaluator));
        }

        private static string GenAfterInstEvaluator(Match m)
        {
            string instNoun = m.Groups[1].Value;
            string noun = m.Groups[2].Value;

            Dictionary<string, string> cases;
            if (!_nounTable.TryGetValue(noun, out cases))
                return m.Value;

            string genForm;
            if (cases.TryGetValue("gen", out genForm))
                return instNoun + " " + genForm;
            return m.Value;
        }

        // --- State-verb + "на" + noun → prepositional case ---
        // "на" is ambiguous: motion verbs → accusative (no change), state verbs → prepositional.
        // State verbs: сидеть, лежать, стоять, находиться, висеть, спать, отдыхать, работать
        // "сидите на стул" → "сидите на стуле", "лежит на кровать" → "лежит на кровати"
        // Motion verbs (садиться, класть, ставить) + на → accusative = nominative for inanimate → no change
        private static readonly Regex rxStateVerbOnNoun = new Regex(
            "(\u0441\u0438\u0434\u0438\u0442\u0435|\u0441\u0438\u0434\u0438\u0442|\u0441\u0438\u0434\u044f\u0442" +          // сидите/сидит/сидят
            "|\u043b\u0435\u0436\u0438\u0442\u0435|\u043b\u0435\u0436\u0438\u0442|\u043b\u0435\u0436\u0430\u0442" +          // лежите/лежит/лежат
            "|\u0441\u0442\u043e\u0438\u0442\u0435|\u0441\u0442\u043e\u0438\u0442|\u0441\u0442\u043e\u044f\u0442" +          // стоите/стоит/стоят
            "|\u043d\u0430\u0445\u043e\u0434\u0438\u0442\u0435\u0441\u044c|\u043d\u0430\u0445\u043e\u0434\u0438\u0442\u0441\u044f" + // находитесь/находится
            "|\u0432\u0438\u0441\u0438\u0442\u0435|\u0432\u0438\u0441\u0438\u0442|\u0432\u0438\u0441\u044f\u0442" +          // висите/висит/висят
            "|\u0441\u043f\u0438\u0442\u0435|\u0441\u043f\u0438\u0442|\u0441\u043f\u044f\u0442" +                            // спите/спит/спят
            "|\u043e\u0442\u0434\u044b\u0445\u0430\u0435\u0442\u0435|\u043e\u0442\u0434\u044b\u0445\u0430\u0435\u0442" +    // отдыхаете/отдыхает
            "|\u0440\u0430\u0431\u043e\u0442\u0430\u0435\u0442\u0435|\u0440\u0430\u0431\u043e\u0442\u0430\u0435\u0442" +    // работаете/работает
            "|\u0441\u043c\u043e\u0442\u0440\u0438\u0442\u0435|\u0441\u043c\u043e\u0442\u0440\u0438\u0442" +                // смотрите/смотрит
            ")\\s+\u043d\u0430\\s+([\u0410-\u042f\u0401\u0430-\u044f\u0451][\u0430-\u044f\u0451]+)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Applies prepositional case to nouns after state-verbs + "на".
        /// "сидите на стул" → "сидите на стуле"
        /// Does NOT affect motion verbs: "садитесь на стул" stays unchanged (accusative = nominative).
        /// </summary>
        private static string ApplyStateVerbPrepNoun(string text)
        {
            if (_nounTable == null || _nounTable.Count == 0) return text;
            return rxStateVerbOnNoun.Replace(text, new MatchEvaluator(StateVerbPrepEvaluator));
        }

        private static string StateVerbPrepEvaluator(Match m)
        {
            string verb = m.Groups[1].Value;
            string noun = m.Groups[2].Value;

            Dictionary<string, string> cases;
            if (!_nounTable.TryGetValue(noun, out cases))
                return m.Value;

            string prepForm;
            if (cases.TryGetValue("prep", out prepForm))
                return verb + " \u043d\u0430 " + prepForm;  // verb + " на " + prepForm
            return m.Value;
        }

        // --- Past tense gender agreement: known noun subject + past verb ---
        // Pattern: (KnownNoun) (verb stem)л(ся|ась|ось|ись|сь)?
        // "Лампа сломался" → "Лампа сломалась" (f), "Стол сломался" (m, no change)
        private static readonly Regex rxPastGender = new Regex(
            "([\u0410-\u042f\u0401][\u0430-\u044f\u0451]+)\\s+([\u0430-\u044f\u0451]+)\u043b(\u0441\u044f|\u0430\u0441\u044c|\u043e\u0441\u044c|\u0438\u0441\u044c|\u0441\u044c)?(?=[\\s,\\.!?;:\\)]|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Fixes past tense verb gender to agree with a known noun subject.
        /// "Лампа сломался" → "Лампа сломалась", "Кресло сломался" → "Кресло сломалось"
        /// </summary>
        private static string ApplyPastTenseGender(string text)
        {
            if (_nounTable == null || _nounTable.Count == 0) return text;
            return rxPastGender.Replace(text, new MatchEvaluator(PastGenderEvaluator));
        }

        private static string PastGenderEvaluator(Match m)
        {
            string noun = m.Groups[1].Value;
            string stem = m.Groups[2].Value;
            string reflexSuffix = m.Groups[3].Success ? m.Groups[3].Value : "";

            Dictionary<string, string> cases;
            if (!_nounTable.TryGetValue(noun, out cases))
                return m.Value;

            string gender;
            if (!cases.TryGetValue("gender", out gender))
                return m.Value;

            // Build correct past tense suffix based on gender
            string pastSuffix;
            string reflexPart;
            switch (gender)
            {
                case "f":
                    pastSuffix = "\u043b\u0430"; // ла
                    reflexPart = (reflexSuffix.Length > 0) ? "\u0441\u044c" : ""; // сь
                    break;
                case "n":
                    pastSuffix = "\u043b\u043e"; // ло
                    reflexPart = (reflexSuffix.Length > 0) ? "\u0441\u044c" : ""; // сь
                    break;
                case "m":
                default:
                    pastSuffix = "\u043b"; // л (already correct)
                    reflexPart = (reflexSuffix.Length > 0) ? "\u0441\u044f" : ""; // ся
                    break;
            }

            return noun + " " + stem + pastSuffix + reflexPart;
        }

        // --- Possessive "ваше" gender agreement with known noun ---
        // AutoTranslator always generates "ваше" (neuter), fix to ваш/ваша/ваше based on noun gender
        private static readonly Regex rxVashGender = new Regex(
            "([\u0412\u0432])\u0430\u0448\u0435\\s+([\u0410-\u042f\u0401][\u0430-\u044f\u0451]+)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Fixes "ваше X" → "ваш/ваша/ваше X" based on known noun gender.
        /// "ваше Лампа" → "ваша Лампа" (f), "ваше Стол" → "ваш Стол" (m)
        /// </summary>
        private static string FixPossessiveGender(string text)
        {
            if (_nounTable == null || _nounTable.Count == 0) return text;
            if (text.IndexOf("\u0430\u0448\u0435") < 0) return text; // quick check for "аше"
            return rxVashGender.Replace(text, new MatchEvaluator(VashGenderEvaluator));
        }

        private static string VashGenderEvaluator(Match m)
        {
            string vCapital = m.Groups[1].Value; // В or в
            string noun = m.Groups[2].Value;

            Dictionary<string, string> cases;
            if (!_nounTable.TryGetValue(noun, out cases))
                return m.Value;

            string gender;
            if (!cases.TryGetValue("gender", out gender))
                return m.Value;

            switch (gender)
            {
                case "f":
                    return vCapital + "\u0430\u0448\u0430 " + noun; // ваша
                case "m":
                    return vCapital + "\u0430\u0448 " + noun; // ваш
                case "n":
                default:
                    return m.Value; // ваше — already correct for neuter
            }
        }
    }
}

