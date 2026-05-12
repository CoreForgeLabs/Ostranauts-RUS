using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OstranautsRusPatch
{
    public static partial class RussianTextCleaner
    {

        /// <summary>
        /// Fast check for Cyrillic characters in string.
        /// </summary>
        public static bool HasCyrillic(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '\u0400' && c <= '\u052F')
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if string contains any Latin letters.
        /// </summary>
        public static bool HasLatin(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    return true;
            }
            return false;
        }

        private static bool IsCyrillicChar(char c)
        {
            return c >= '\u0400' && c <= '\u052F';
        }

        private static char? MapLatinHomoglyphToCyrillic(char c)
        {
            switch (c)
            {
                case 'A': return '\u0410';
                case 'a': return '\u0430';
                case 'B': return '\u0412';
                case 'E': return '\u0415';
                case 'e': return '\u0435';
                case 'K': return '\u041A';
                case 'k': return '\u043A';
                case 'M': return '\u041C';
                case 'H': return '\u041D';
                case 'O': return '\u041E';
                case 'o': return '\u043E';
                case 'P': return '\u0420';
                case 'p': return '\u0440';
                case 'C': return '\u0421';
                case 'c': return '\u0441';
                case 'T': return '\u0422';
                case 'X': return '\u0425';
                case 'x': return '\u0445';
                case 'Y': return '\u0423';
                case 'y': return '\u0443';
                default: return null;
            }
        }

        private static string NormalizeMixedScriptHomoglyphs(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 2)
                return text;

            char[] chars = text.ToCharArray();
            bool changed = false;

            for (int i = 0; i < chars.Length; i++)
            {
                char ch = chars[i];
                char? mapped = MapLatinHomoglyphToCyrillic(ch);
                if (!mapped.HasValue) continue;

                bool nearCyrillic =
                    (i > 0 && IsCyrillicChar(chars[i - 1])) ||
                    (i + 1 < chars.Length && IsCyrillicChar(chars[i + 1]));
                if (!nearCyrillic) continue;

                chars[i] = mapped.Value;
                changed = true;
            }

            return changed ? new string(chars) : text;
        }

        /// <summary>
        /// Translates Stat* condition names (e.g. "StatPower" → "Мощность").
        /// Quick IndexOf("Stat") check avoids iteration on irrelevant strings.
        /// Array is sorted by key length DESC to prevent partial matches.
        /// </summary>
        private static string TranslateStatNames(string text)
        {
            if (text.IndexOf("Stat") < 0) return text;
            for (int i = 0; i < _statTranslations.Length; i++)
            {
                if (text.IndexOf(_statTranslations[i][0]) >= 0)
                {
                    text = text.Replace(_statTranslations[i][0], _statTranslations[i][1]);
                }
            }
            return text;
        }

        /// <summary>
        /// Translates English values after known translated ship info labels
        /// (e.g., "Марка: Testudo" → "Марка: Тестудо") by extracting the value
        /// after the label and looking it up in exactTranslations.
        /// Only fires on text that has BOTH Cyrillic (translated labels) and Latin (untranslated values).
        /// </summary>
        private static string TranslateShipInfoValues(string text)
        {
            for (int l = 0; l < shipInfoLabels.Length; l++)
            {
                int idx = text.IndexOf(shipInfoLabels[l]);
                if (idx < 0) continue;

                int valueStart = idx + shipInfoLabels[l].Length;
                if (valueStart >= text.Length) continue;

                // Find end of value (next newline or end of string)
                int valueEnd = text.IndexOf('\n', valueStart);
                if (valueEnd < 0) valueEnd = text.Length;

                // Extract raw value and trim trailing whitespace/CR
                string rawValue = text.Substring(valueStart, valueEnd - valueStart);
                string value = rawValue.TrimEnd('\r', ' ');
                if (value.Length == 0 || HasCyrillic(value)) continue;

                // Look up in exactTranslations
                string translated;
                if (exactTranslations.TryGetValue(value, out translated))
                {
                    text = text.Substring(0, valueStart) + translated +
                           text.Substring(valueStart + value.Length);
                }
            }
            return text;
        }

        private static string PronounReplacer(Match match)
        {
            string val;
            if (pronounMap.TryGetValue(match.Value, out val))
                return val;
            return match.Value;
        }

        // --- Result cache: avoids re-processing identical strings ---
        // NO SIZE LIMIT: game text is bounded (finite entities/conditions).
        // A full session produces ~5000-20000 unique strings (~2MB RAM, trivial).
        // NEVER clear mid-game: bulk Dictionary.Clear() creates thousands of
        // garbage objects → Mono stop-the-world GC → 0.5s freeze.
        private static readonly ConcurrentDictionary<string, string> _cleanCache =
            new ConcurrentDictionary<string, string>(System.Environment.ProcessorCount, 1024);

        /// <summary>Current cache size for diagnostics.</summary>
        public static int CacheSize { get { return _cleanCache.Count; } }

        /// <summary>Dumps cache entries with Latin text (potential untranslated) to file.</summary>
        public static void DumpCacheToFile(string path)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Clean Cache Dump (" + _cleanCache.Count + " entries) ===");
            int latinCount = 0;
            foreach (var kv in _cleanCache)
            {
                bool hasLatin = false;
                string val = kv.Value;
                for (int i = 0; i < val.Length; i++)
                {
                    char c = val[i];
                    if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    {
                        hasLatin = true;
                        break;
                    }
                }
                if (hasLatin && val.Length > 5)
                {
                    latinCount++;
                    string truncIn = kv.Key.Length > 120 ? kv.Key.Substring(0, 120) + "..." : kv.Key;
                    string truncOut = val.Length > 120 ? val.Substring(0, 120) + "..." : val;
                    sb.AppendLine("[EN?] " + truncIn);
                    sb.AppendLine("  => " + truncOut);
                    sb.AppendLine();
                }
            }
            sb.AppendLine("=== Latin entries: " + latinCount + " / " + _cleanCache.Count + " ===");
            System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
        }

        /// <summary>Async debug dump — does not block main thread.</summary>
        public static void DumpCacheToFileAsync(string path)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("=== Clean Cache Dump (" + _cleanCache.Count + " entries) ===");
                    int latinCount = 0;
                    foreach (var kv in _cleanCache)
                    {
                        bool hasLatin = false;
                        string val = kv.Value;
                        for (int i = 0; i < val.Length; i++)
                        {
                            char c = val[i];
                            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) { hasLatin = true; break; }
                        }
                        if (hasLatin && val.Length > 5)
                        {
                            latinCount++;
                            string truncIn  = kv.Key.Length > 120 ? kv.Key.Substring(0, 120) + "..." : kv.Key;
                            string truncOut = val.Length  > 120 ? val.Substring(0, 120) + "..." : val;
                            sb.AppendLine("[EN?] " + truncIn);
                            sb.AppendLine("  => " + truncOut);
                            sb.AppendLine();
                        }
                    }
                    sb.AppendLine("=== Latin entries: " + latinCount + " / " + _cleanCache.Count + " ===");
                    System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
                }
                catch { }
            });
        }

        /// <summary>
        /// Strips {ls ...} wrappers from text using string operations.
        /// </summary>
        public static string StripLsWrappers(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            int idx;
            while ((idx = text.IndexOf("{ls ", StringComparison.Ordinal)) >= 0)
            {
                int start = idx;
                int end = text.IndexOf('}', start);
                if (end < 0)
                    break;
                text = text.Remove(start, end - start + 1);
            }

            return text;
        }

        /// <summary>
        /// Backward-compatible alias for old callers.
        /// </summary>
        public static string StripLsBrackets(string text)
        {
            return StripLsWrappers(text);
        }

        private static bool NeedsPureCyrillicPostProcessing(string text)
        {
            // Fast skip for already-clean Russian text shown frequently in UI.
            if (text.IndexOf("[", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("{ls ", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("\u0412\u044b ", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("\u0432\u044b ", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("\u0442\u0412", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("\u043a\u041f\u041a", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("  ", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("(\u043b\u0430)", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("(\u0435\u0435)", StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // Cached delegate to avoid heap allocation on every rxPronoun.Replace call
        private static readonly MatchEvaluator _pronounReplacer =
            new MatchEvaluator(PronounReplacer);
        private static readonly MatchEvaluator _infObjEvaluator =
            new MatchEvaluator(InfObjEval);
        private static readonly MatchEvaluator _prepGenVyEvaluator =
            new MatchEvaluator(PrepGenVyEval);
        private static readonly MatchEvaluator _prepDatVyEvaluator =
            new MatchEvaluator(PrepDatVyEval);
        private static readonly MatchEvaluator _prepInstrVyEvaluator =
            new MatchEvaluator(PrepInstrVyEval);
        private static readonly MatchEvaluator _genPrepAEvaluator =
            new MatchEvaluator(GenPrepAEvaluator);

        /// <summary>
        /// Cleans English grammar artifacts from text containing Russian.
        /// Also translates known hardcoded pure-English strings.
        /// 
        /// PERFORMANCE ARCHITECTURE:
        ///   Layer 1: Cache lookup → ~50ns for repeated strings
        ///   Layer 2: HasLatin check in prefixes → ~100ns, skips 90% of calls
        ///   Layer 3: Exact dictionary lookup → O(1) for known phrases
        ///   Layer 4: Pure-Cyrillic fast path → only Вы verb fix
        ///   Layer 5: Full processing → only for mixed Cyrillic+Latin text
        /// 
        /// Caches results to avoid repeated processing of the same text.
        /// </summary>
        public static string Clean(string text)
        {
            RusPatchPlugin._cleanCalls++;
            if (string.IsNullOrEmpty(text) || text.Length < 3)
                return text;

            // --- Early intercept: "X gains/loses Y" ---
            // Same reason as considers: GetMessageLog > 2048 chars bypasses Layer 5.
            // Pattern: "[Кирилл имя] gains [Кирилл состояние]" — both parts are Cyrillic
            // (conditions are translated by XUnity). Apply only when subject starts with Cyrillic.
            if (text.IndexOf(" gains ") >= 0 || text.IndexOf(" loses ") >= 0)
            {
                bool found = false;
                if (text.IndexOf(" gains ") >= 0)
                {
                    string rep = rxGainsFull.Replace(text, m => {
                        // Only replace if subject starts with Cyrillic (not a pure-English string)
                        string subj = m.Groups[1].Value;
                        if (subj.Length > 0 && (subj[0] >= '\u0410' && subj[0] <= '\u044f' || subj[0] == '\u0401' || subj[0] == '\u0451'))
                            return subj + " \u043f\u043e\u043b\u0443\u0447\u0430\u0435\u0442: " + m.Groups[2].Value;
                        return m.Value;
                    });
                    if (!string.Equals(rep, text, StringComparison.Ordinal)) { text = rep; found = true; }
                }
                if (text.IndexOf(" loses ") >= 0)
                {
                    string rep = rxLosesFull.Replace(text, m => {
                        string subj = m.Groups[1].Value;
                        if (subj.Length > 0 && (subj[0] >= '\u0410' && subj[0] <= '\u044f' || subj[0] == '\u0401' || subj[0] == '\u0451'))
                            return subj + " \u0442\u0435\u0440\u044f\u0435\u0442: " + m.Groups[2].Value;
                        return m.Value;
                    });
                    if (!string.Equals(rep, text, StringComparison.Ordinal)) { text = rep; found = true; }
                }
                if (found && text.Length > MaxProcessableInputLength)
                    return text;
            }

            // --- Early intercept: "X now considers/no longer considers Y a(n) Z" ---
            // MUST run before length check: GetMessageLog returns the full multi-line log
            // (all accumulated messages), which easily exceeds 2048 chars.
            // Regex.Replace is global — translates all occurrences in one pass.
            // NOTE: rxConsidersFull uses default flags (no Singleline), so '.' does NOT cross '\n'.
            //       This correctly handles multi-line logs — G3 stops at the newline.
            if (text.IndexOf(" now considers ") >= 0 || text.IndexOf(" no longer considers ") >= 0)
            {
                // Normalize Cyrillic '\u0430' (а) → Latin 'a' in "a(n)" / "a[n]"
                string tc = (text.IndexOf('\u0430') >= 0)
                    ? text.Replace("\u0430(n)", "a(n)").Replace("\u0430[n]", "a[n]")
                    : text;
                bool foundConsiders = false;
                if (tc.IndexOf(" now considers ") >= 0 && (tc.IndexOf(" a(n) ") >= 0 || tc.IndexOf(" a[n] ") >= 0))
                {
                    string rep = rxConsidersFull.Replace(tc,
                        m => m.Groups[1].Value + ": " + m.Groups[2].Value
                             + " \u0442\u0435\u043f\u0435\u0440\u044c \u2014 " + m.Groups[3].Value);
                    if (!string.Equals(rep, tc, StringComparison.Ordinal)) { tc = rep; foundConsiders = true; }
                }
                if (tc.IndexOf(" no longer considers ") >= 0 && (tc.IndexOf(" a(n) ") >= 0 || tc.IndexOf(" a[n] ") >= 0))
                {
                    string rep = rxNoLongerConsidersFull.Replace(tc,
                        m => m.Groups[1].Value + ": " + m.Groups[2].Value
                             + " \u0431\u043e\u043b\u044c\u0448\u0435 \u043d\u0435 " + m.Groups[3].Value);
                    if (!string.Equals(rep, tc, StringComparison.Ordinal)) { tc = rep; foundConsiders = true; }
                }
                if (foundConsiders)
                {
                    text = tc;
                    // Full log string may remain > 2048 after fix — return translated log as-is
                    if (text.Length > MaxProcessableInputLength)
                        return text;
                    // Short string continues through normal pipeline below
                }
            }

            // Very long dynamic strings are expensive to process and typically already localized.
            if (text.Length > MaxProcessableInputLength)
                return text;

            string originalText = text;

            // --- Early: strip {ls ...} wrappers (list-item placeholders from game engine) ---
            text = StripLsWrappers(text);

            // Normalize non-breaking and other special whitespace to regular space FIRST.
            // MUST run before multi-space collapse: game sends "All\u00A0\u00A0modules active."
            // (two U+00A0 from {ls NavModule} stripping) — these are NOT ASCII spaces, so
            // the "  " check below won't collapse them. Convert first, then collapse.
            if (text.IndexOf('\u00A0') >= 0 || text.IndexOf('\u202F') >= 0 ||
                text.IndexOf('\u2009') >= 0 || text.IndexOf('\u2002') >= 0)
            {
                text = text.Replace('\u00A0', ' ').Replace('\u202F', ' ')
                           .Replace('\u2009', ' ').Replace('\u2002', ' ');
            }

            // Collapse multiple spaces left by {ls} tag stripping or U+00A0 normalization above.
            // e.g. "All {ls NavModule} modules active." → "All    modules active." → "All modules active."
            if (text.IndexOf("  ", StringComparison.Ordinal) >= 0)
                text = rxMultiSpace.Replace(text, " ");

            // Strip trailing whitespace/newlines — TMPro often appends '\n' to UI labels
            // e.g. "Power Paths\n" must match dictionary key "Power Paths"
            {
                string t = text.TrimEnd();
                if (t.Length >= 3 && t.Length != text.Length)
                    text = t;
            }


            // --- Layer 3: Exact full-string match (pure English messages) ---
            // Moved BEFORE LastInput/cache fast paths: stale cache from previous plugin
            // versions would otherwise return un-translated values for newly added keys.
            string exactResult;
            if (exactTranslations.TryGetValue(text, out exactResult))
            {
                CacheResult(originalText, exactResult);
                return exactResult;
            }
            // Fallback: normalize newlines to spaces for multi-line UI strings
            // (e.g., "Open\nAirlocks" → "Open Airlocks") to match dictionary keys.
            if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
            {
                string normalized = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
                if (exactTranslations.TryGetValue(normalized, out exactResult))
                {
                    CacheResult(originalText, exactResult);
                    return exactResult;
                }
            }

            // Thread-local fast path catches immediate repeated calls on identical input.
            if (originalText == LastInput)
            {
                RusPatchPlugin._cacheHits++;
                return LastOutput;
            }

            // --- Layer 1: Cache lookup (fast path for repeated strings) ---
            string cached;
            if (_cleanCache.TryGetValue(originalText, out cached))
            {
                RusPatchPlugin._cacheHits++;
                return cached;
            }

            bool hasLatin = HasLatin(text);
            bool hasCyrillic = HasCyrillic(text);

            // --- Pure English text (no Cyrillic yet): try phraseReplacements ---
            if (!hasCyrillic && hasLatin)
            {
                bool changed = false;
                for (int i = 0; i < phraseReplacements.Length; i++)
                {
                    if (text.IndexOf(phraseReplacements[i][0]) >= 0)
                    {
                        text = text.Replace(phraseReplacements[i][0], phraseReplacements[i][1]);
                        changed = true;
                    }
                }
                if (!changed)
                {
                    // Even if no phrase matched, try stat name translation
                    text = TranslateStatNames(text);
                    CacheResult(originalText, text);
                    return text;
                }
                // Also translate stat names after phrase replacements
                text = TranslateStatNames(text);
                // Updated flags after modification
                hasCyrillic = true; // We just inserted Cyrillic
                hasLatin = HasLatin(text);
            }

            if (!hasCyrillic)
            {
                CacheResult(originalText, text);
                return text;
            }

            // --- Layer 4: Pure Cyrillic (no Latin) → minimal processing ---
            if (!hasLatin)
            {
                if (!NeedsPureCyrillicPostProcessing(text))
                {
                    CacheResult(originalText, text);
                    return text;
                }

                // Only Вы verb conjugation fix and whitespace cleanup needed
                if (text.IndexOf("\u0412\u044b ") >= 0 || text.IndexOf("\u0432\u044b ") >= 0)
                {
                    text = VyReplaceSafe(rxVyVerbRefl, text, "\u0442\u0435\u0441\u044c");
                    text = VyReplaceSafe(rxVyVerb, text, "\u0442\u0435");
                    text = VyReplaceSafe(rxVyVerbPastRefl, text, "\u043b\u0438\u0441\u044c");
                    text = VyReplaceSafe(rxVyVerbPast, text, "\u043b\u0438");
                    // Short participle: "Вы оглушён" -> "Вы оглушены"
                    text = VyReplaceSafe(rxVyParticiple, text, "\u0435\u043d\u044b");
                    text = VyReplaceSafe(rxVyIrregPast, text, "\u043b\u0438");
                    // NOTE: rxAccFemA/rxAccFemYa/rxAccAdjAya REMOVED — they blindly changed
                    // last word before period (-а→-у, -я→-ю), corrupting correct cases:
                    // "без сознания."→"без сознанию.", "72 часа."→"72 часу.",
                    // "состоявшимся."→"состоявшимсю." — Russian text already has correct endings.
                    // --- Post-conjugation: "Вы имеете X" → "У вас X" (sounds more natural) ---
                    text = FixVyImeete(text);
                    // --- Second pass: conjugate coordinated verbs after "и" ---
                    // "Вы любовались ... и не может повторить" → "и не можете"
                    text = FixCoordinateVyVerbs(text);
                }
                // --- Fix subjective pronouns after infinitive verbs → objective ---
                // "ударить вы" → "ударить вас" (game uses [them] not [them-obj])
                text = rxInfObj.Replace(text, _infObjEvaluator);
                // --- Fix "вы" after prepositions → correct case (311 instances) ---
                // "для вы" → "для вас", "к вы" → "к вам", "с вы" → "с вами"
                text = rxPrepGenVy.Replace(text, _prepGenVyEvaluator);
                text = rxPrepDatVy.Replace(text, _prepDatVyEvaluator);
                text = rxPrepInstrVy.Replace(text, _prepInstrVyEvaluator);
                // --- Strip gender bracket markers: "Умер(ла)" → "Умер" ---
                text = StripGenderBrackets(text);
                // --- Possessive agreement: ваше → ваш/ваша based on noun gender ---
                text = FixPossessiveGender(text);
                // --- Past tense gender agreement for known noun subjects ---
                text = ApplyPastTenseGender(text);
                // --- Noun declension via dictionary (precise, before regex fallback) ---
                text = ApplyNounDeclension(text);
                // --- Accusative for direct objects after verbs (dictionary-based) ---
                text = ApplyVerbAccusative(text);
                // --- Instrumental for objects after verbs requiring instrumental case ---
                text = ApplyVerbInstrumental(text);
                // --- Genitive for nouns after instrumental-form nouns ("деталями горшок" → "деталями горшка") ---
                text = ApplyGenAfterInstNoun(text);
                // --- State-verb + "на" + noun → prepositional ("сидите на стул" → "сидите на стуле") ---
                text = ApplyStateVerbPrepNoun(text);
                // --- Genitive/Accusative regex fallback for nouns NOT in dictionary ---
                text = rxGenPrepA.Replace(text, _genPrepAEvaluator);
                text = rxGenPrepYa.Replace(text, "$1\u0438");
                text = rxGenPrepSoft.Replace(text, "$1\u0438");
                text = rxAccVerbA.Replace(text, "$1\u0443");
                // --- Fix abbreviation casing: game engine lowercases first char of [us]/[them] mid-sentence ---
                // "тВ" → "ТВ", "кПК" → "КПК"
                if (text.IndexOf("\u0442\u0412") >= 0)
                    text = text.Replace("\u0442\u0412", "\u0422\u0412");
                if (text.IndexOf("\u043a\u041f\u041a") >= 0)
                    text = text.Replace("\u043a\u041f\u041a", "\u041a\u041f\u041a");
                if (text.IndexOf("  ") >= 0)
                    text = rxMultiSpace.Replace(text, " ");
                if (text.Length > 0 && text[0] == ' ')
                    text = text.TrimStart();
                CacheResult(originalText, text);
                return text;
            }

            // --- Layer 5: Full processing (mixed Cyrillic + Latin) ---

            // --- Resolve unresolved game bracket variables (early, before grammar) ---
            // If [us]/[them] still present, engine failed to resolve entity refs.
            // Replace [us] with Вы (player is the actor) → triggers Вы conjugation.
            // Remove [them] → noun flows naturally for declension (на Стена → на Стену).
            if (text.IndexOf("[us]") >= 0)
                text = text.Replace("[us] ", "\u0412\u044b ").Replace("[us]", "\u0412\u044b");
            if (text.IndexOf("[them]") >= 0)
                text = text.Replace("[them]: ", "").Replace("[them]", "");

            // Normalize accidental Latin homoglyphs inside Cyrillic words
            // (e.g., Latin 'H' in place of Cyrillic 'Н').
            text = NormalizeMixedScriptHomoglyphs(text);

            // --- Remove articles/possessive before Cyrillic ---
            text = rxThe.Replace(text, "");
            text = rxAAn.Replace(text, "");
            text = rxPossS.Replace(text, "");
            text = rxPossSEnd.Replace(text, "");

            // --- Replace English pronouns ---
            text = rxPronoun.Replace(text, _pronounReplacer);

            // --- Translate hardcoded C# phrases (Active Shift, career names etc.) ---
            for (int i = 0; i < phraseReplacements.Length; i++)
            {
                if (text.IndexOf(phraseReplacements[i][0]) >= 0)
                    text = text.Replace(phraseReplacements[i][0], phraseReplacements[i][1]);
            }

            // Replace English "to" between Cyrillic words
            text = rxCyrToPrep.Replace(text, " \u043a ");

            // Replace English "with" between Cyrillic words (combat logs)
            text = rxCyrWithPrep.Replace(text, ": ");

            // Replace English "now" between Cyrillic words
            text = rxCyrNowAdv.Replace(text, " \u0442\u0435\u043f\u0435\u0440\u044c ");

            // Replace English " gains " between Cyrillic words
            text = rxCyrGains.Replace(text, " \u043f\u043e\u043b\u0443\u0447\u0430\u0435\u0442: ");

            // Replace English " loses " between Cyrillic words
            text = rxCyrLoses.Replace(text, " \u0442\u0435\u0440\u044f\u0435\u0442: ");

            // --- Translate ship info panel values (brand, model, designation, Yes/No) ---
            // --- And handle considers/notices patterns ---
            // Only if Latin characters remain after phrase replacements
            if (HasLatin(text))
            {
                text = TranslateShipInfoValues(text);
                text = TranslateStatNames(text);

                // --- Replace hardcoded English phrases ---
                // Normalize Cyrillic 'а' in "a(n)" / "a[n]" — game outputs U+0430 instead of U+0061
                text = text.Replace(" а(n) ", " a(n) ").Replace(" а[n] ", " a[n] ");

                // First try full "considers ... a(n) ..." pattern as one unit
                bool considersHandled = false;
                if (text.IndexOf(" now considers ") >= 0 && (text.IndexOf(" a(n) ") >= 0 || text.IndexOf(" a[n] ") >= 0))
                {
                    Match m = rxConsidersFull.Match(text);
                    if (m.Success)
                    {
                        text = m.Groups[1].Value + ": " + m.Groups[2].Value + " \u0442\u0435\u043f\u0435\u0440\u044c \u2014 " + m.Groups[3].Value;
                        considersHandled = true;
                    }
                }
                if (!considersHandled && text.IndexOf(" no longer considers ") >= 0 && (text.IndexOf(" a(n) ") >= 0 || text.IndexOf(" a[n] ") >= 0))
                {
                    Match m = rxNoLongerConsidersFull.Match(text);
                    if (m.Success)
                    {
                        text = m.Groups[1].Value + ": " + m.Groups[2].Value + " \u0431\u043e\u043b\u044c\u0448\u0435 \u043d\u0435 " + m.Groups[3].Value;
                        considersHandled = true;
                    }
                }

                // Fallback: individual replacements if full pattern didn't match
                if (!considersHandled)
                {
                    if (text.IndexOf(" now considers ") >= 0)
                        text = rxNowConsiders.Replace(text, " \u0442\u0435\u043f\u0435\u0440\u044c \u0441\u0447\u0438\u0442\u0430\u0435\u0442 ");
                    if (text.IndexOf(" no longer considers ") >= 0)
                        text = rxNoLongerConsiders.Replace(text, " \u0431\u043e\u043b\u044c\u0448\u0435 \u043d\u0435 \u0441\u0447\u0438\u0442\u0430\u0435\u0442 ");
                    if (text.IndexOf(" a(n) ") >= 0 || text.IndexOf(" a[n] ") >= 0)
                        text = rxAnParen.Replace(text, " ");
                }

                if (text.IndexOf(" notices something is wrong with ") >= 0)
                    text = rxNotices.Replace(text, " \u0437\u0430\u043c\u0435\u0447\u0430\u0435\u0442 \u043d\u0435\u043b\u0430\u0434\u043d\u043e\u0435 \u0441 ");
            }

            // --- Fix "Вы" + 3rd person verb → 2nd person plural ---
            // "Вы принимает" → "Вы принимаете", "Вы собирается" → "Вы собираетесь"
            if (text.IndexOf("\u0412\u044b ") >= 0 || text.IndexOf("\u0432\u044b ") >= 0)
            {
                text = VyReplaceSafe(rxVyVerbRefl, text, "\u0442\u0435\u0441\u044c");
                text = VyReplaceSafe(rxVyVerb, text, "\u0442\u0435");
                text = VyReplaceSafe(rxVyVerbPastRefl, text, "\u043b\u0438\u0441\u044c");
                text = VyReplaceSafe(rxVyVerbPast, text, "\u043b\u0438");
                    // Short participle: "Вы оглушён" -> "Вы оглушены"
                    text = VyReplaceSafe(rxVyParticiple, text, "\u0435\u043d\u044b");
                    text = VyReplaceSafe(rxVyIrregPast, text, "\u043b\u0438");
                // NOTE: rxAccFemA/rxAccFemYa/rxAccAdjAya REMOVED — see Layer 4 comment.
                // --- Post-conjugation: "Вы имеете X" → "У вас X" (sounds more natural) ---
                text = FixVyImeete(text);
                // --- Second pass: conjugate coordinated verbs after "и" ---
                text = FixCoordinateVyVerbs(text);
            }
            // --- Fix subjective pronouns after infinitive verbs → objective ---
            text = rxInfObj.Replace(text, _infObjEvaluator);
            // --- Fix "вы" after prepositions → correct case ---
            text = rxPrepGenVy.Replace(text, _prepGenVyEvaluator);
            text = rxPrepDatVy.Replace(text, _prepDatVyEvaluator);
            text = rxPrepInstrVy.Replace(text, _prepInstrVyEvaluator);
            // --- Strip gender bracket markers: "Умер(ла)" → "Умер" ---
            text = StripGenderBrackets(text);
            // --- Possessive agreement: ваше → ваш/ваша based on noun gender ---
            text = FixPossessiveGender(text);
            // --- Past tense gender agreement for known noun subjects ---
            text = ApplyPastTenseGender(text);
            // --- Multi-word noun declension (before single-word pass) ---
            text = ApplyMultiWordNounDeclension(text);
            // --- Noun declension via dictionary (precise, before regex fallback) ---
            text = ApplyNounDeclension(text);
            // --- Multi-word verb accusative (before single-word pass) ---
            text = ApplyMultiWordVerbAccusative(text);
            // --- Accusative for direct objects after verbs (dictionary-based) ---
            text = ApplyVerbAccusative(text);
            // --- Instrumental for objects after verbs requiring instrumental case ---
            text = ApplyVerbInstrumental(text);
            // --- Genitive for nouns after instrumental-form nouns ("деталями горшок" → "деталями горшка") ---
            text = ApplyGenAfterInstNoun(text);
            // --- State-verb + "на" + noun → prepositional ("сидите на стул" → "сидите на стуле") ---
            text = ApplyStateVerbPrepNoun(text);
            // --- Genitive/Accusative regex fallback for nouns NOT in dictionary ---
            text = rxGenPrepA.Replace(text, _genPrepAEvaluator);
            text = rxGenPrepYa.Replace(text, "$1\u0438");
            text = rxGenPrepSoft.Replace(text, "$1\u0438");
            text = rxAccVerbA.Replace(text, "$1\u0443");

            // --- Fix abbreviation casing: game engine lowercases first char of [us]/[them] mid-sentence ---
            if (text.IndexOf("\u0442\u0412") >= 0)
                text = text.Replace("\u0442\u0412", "\u0422\u0412");
            if (text.IndexOf("\u043a\u041f\u041a") >= 0)
                text = text.Replace("\u043a\u041f\u041a", "\u041a\u041f\u041a");

            // --- Cleanup ---

            if (text.IndexOf("  ") >= 0)
                text = rxMultiSpace.Replace(text, " ");

            if (text.Length > 0 && text[0] == ' ')
                text = text.TrimStart();

            CacheResult(originalText, text);
            return text;
        }

        /// <summary>
        /// Stores the Clean() result in cache.
        /// Uses bounded cache with soft background trimming to avoid GC spikes.
        /// </summary>
        private static void CacheResult(string input, string output)
        {
            LastInput = input;
            LastOutput = output;

            // Skip cache growth for long one-off strings (large dialogs/log chunks).
            if (input.Length > MaxCacheableInputLength)
                return;

            _cleanCache[input] = output;
            if (_cleanCache.Count > MaxCleanCacheSize)
                ScheduleCacheTrim();
        }

        private static void ScheduleCacheTrim()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _cacheTrimScheduled, 1, 0) != 0)
                return;

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int toRemove = _cleanCache.Count - TargetCleanCacheSize;
                    if (toRemove <= 0) return;

                    foreach (KeyValuePair<string, string> kv in _cleanCache)
                    {
                        string removed;
                        if (_cleanCache.TryRemove(kv.Key, out removed))
                        {
                            toRemove--;
                            if (toRemove <= 0) break;
                        }
                    }
                }
                catch { }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _cacheTrimScheduled, 0);
                }
            });
        }
    }
}
