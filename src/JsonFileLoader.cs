using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OstranautsRusPatch
{
    /// <summary>
    /// Minimal JSON file loader for .NET 2.0 / Mono compatibility.
    /// Handles flat dictionaries, arrays of {en,ru} pairs, and string arrays.
    /// Supports \uXXXX, \\, \", \n, \r, \t escape sequences.
    /// 
    /// File formats:
    ///   rus_exact.json    — { "key": "value", ... }
    ///   rus_phrases.json  — [{"en": "...", "ru": "..."}, ...]
    ///   rus_ship_labels.json — ["label1", "label2", ...]
    ///   rus_nouns.json    — { "nom": {"acc": "...", "gen": "...", ...}, ... }
    /// </summary>
    public static class JsonFileLoader
    {
        private static BepInEx.Logging.ManualLogSource Log
        {
            get { return RusPatchPlugin.Log; }
        }

        // =====================================================
        // PUBLIC API
        // =====================================================

        /// <summary>
        /// Loads a flat { "key": "value" } JSON file into a dictionary.
        /// Returns empty dictionary on error.
        /// </summary>
        public static Dictionary<string, string> LoadDictionary(string path)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(path))
                {
                    Log.LogInfo("[JsonLoader] File not found: " + path);
                    return result;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                int pos = 0;
                SkipWhitespace(json, ref pos);

                if (pos >= json.Length || json[pos] != '{')
                {
                    Log.LogWarning("[JsonLoader] Expected '{' at start of " + path);
                    return result;
                }
                pos++; // skip '{'

                while (pos < json.Length)
                {
                    SkipWhitespace(json, ref pos);
                    if (pos >= json.Length || json[pos] == '}') break;

                    string key = ReadString(json, ref pos);
                    if (key == null) break;

                    SkipWhitespace(json, ref pos);
                    if (pos >= json.Length || json[pos] != ':') break;
                    pos++; // skip ':'

                    SkipWhitespace(json, ref pos);
                    // Value can be a string or an object (for nouns dict)
                    if (pos < json.Length && json[pos] == '{')
                    {
                        // Skip nested object (not needed for flat dict)
                        SkipObject(json, ref pos);
                    }
                    else
                    {
                        string val = ReadString(json, ref pos);
                        if (val == null) break;
                        result[key] = val;
                    }

                    SkipWhitespace(json, ref pos);
                    if (pos < json.Length && json[pos] == ',')
                        pos++; // skip ','
                }

                Log.LogInfo("[JsonLoader] Loaded " + result.Count + " entries from " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Log.LogWarning("[JsonLoader] Error loading " + path + ": " + ex.Message);
            }
            return result;
        }

        /// <summary>
        /// Loads noun declension table: { "Nom": {"acc": "...", "gen": "...", ...}, ... }
        /// Returns Dictionary of nominative → Dictionary of case → form.
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> LoadNounTable(string path)
        {
            Dictionary<string, Dictionary<string, string>> result =
                new Dictionary<string, Dictionary<string, string>>();
            try
            {
                if (!File.Exists(path))
                {
                    Log.LogInfo("[JsonLoader] Noun file not found: " + path);
                    return result;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                int pos = 0;
                SkipWhitespace(json, ref pos);

                if (pos >= json.Length || json[pos] != '{') return result;
                pos++; // skip '{'

                while (pos < json.Length)
                {
                    SkipWhitespace(json, ref pos);
                    if (pos >= json.Length || json[pos] == '}') break;

                    string nominative = ReadString(json, ref pos);
                    if (nominative == null) break;

                    SkipWhitespace(json, ref pos);
                    if (pos >= json.Length || json[pos] != ':') break;
                    pos++; // skip ':'

                    SkipWhitespace(json, ref pos);
                    Dictionary<string, string> cases = ReadFlatObject(json, ref pos);
                    result[nominative] = cases;

                    SkipWhitespace(json, ref pos);
                    if (pos < json.Length && json[pos] == ',')
                        pos++;
                }

                Log.LogInfo("[JsonLoader] Loaded " + result.Count + " nouns from " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Log.LogWarning("[JsonLoader] Error loading nouns: " + ex.Message);
            }
            return result;
        }

        /// <summary>
        /// Loads phrase array: [{"en": "...", "ru": "..."}, ...]
        /// Returns string[][] where each element is { en, ru }.
        /// </summary>
        public static string[][] LoadPhraseArray(string path)
        {
            List<string[]> result = new List<string[]>();
            try
            {
                if (!File.Exists(path))
                {
                    Log.LogInfo("[JsonLoader] File not found: " + path);
                    return new string[0][];
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                int pos = 0;
                SkipWhitespace(json, ref pos);

                if (pos >= json.Length || json[pos] != '[') return new string[0][];
                pos++; // skip '['

                while (pos < json.Length)
                {
                    SkipWhitespace(json, ref pos);
                    if (pos >= json.Length || json[pos] == ']') break;

                    if (json[pos] == '{')
                    {
                        pos++; // skip '{'
                        string en = null, ru = null;

                        // Read up to 2 key-value pairs
                        for (int p = 0; p < 2; p++)
                        {
                            SkipWhitespace(json, ref pos);
                            if (pos >= json.Length || json[pos] == '}') break;

                            string key = ReadString(json, ref pos);
                            if (key == null) break;

                            SkipWhitespace(json, ref pos);
                            if (pos >= json.Length || json[pos] != ':') break;
                            pos++;

                            SkipWhitespace(json, ref pos);
                            string val = ReadString(json, ref pos);
                            if (val == null) break;

                            if (key == "en") en = val;
                            else if (key == "ru") ru = val;

                            SkipWhitespace(json, ref pos);
                            if (pos < json.Length && json[pos] == ',')
                                pos++;
                        }

                        // Skip to end of object
                        while (pos < json.Length && json[pos] != '}') pos++;
                        if (pos < json.Length) pos++; // skip '}'

                        if (en != null && ru != null)
                            result.Add(new string[] { en, ru });
                    }

                    SkipWhitespace(json, ref pos);
                    if (pos < json.Length && json[pos] == ',')
                        pos++;
                }

                Log.LogInfo("[JsonLoader] Loaded " + result.Count + " phrases from " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Log.LogWarning("[JsonLoader] Error loading phrases: " + ex.Message);
            }
            return result.ToArray();
        }

        /// <summary>
        /// Loads a simple string array: ["val1", "val2", ...]
        /// </summary>
        public static string[] LoadStringArray(string path)
        {
            List<string> result = new List<string>();
            try
            {
                if (!File.Exists(path))
                {
                    Log.LogInfo("[JsonLoader] File not found: " + path);
                    return new string[0];
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                int pos = 0;
                SkipWhitespace(json, ref pos);

                if (pos >= json.Length || json[pos] != '[') return new string[0];
                pos++; // skip '['

                while (pos < json.Length)
                {
                    SkipWhitespace(json, ref pos);
                    if (pos >= json.Length || json[pos] == ']') break;

                    string val = ReadString(json, ref pos);
                    if (val != null) result.Add(val);

                    SkipWhitespace(json, ref pos);
                    if (pos < json.Length && json[pos] == ',')
                        pos++;
                }

                Log.LogInfo("[JsonLoader] Loaded " + result.Count + " items from " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Log.LogWarning("[JsonLoader] Error loading array: " + ex.Message);
            }
            return result.ToArray();
        }

        // =====================================================
        // JSON PARSING INTERNALS
        // =====================================================

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    pos++;
                else
                    break;
            }
        }

        /// <summary>
        /// Reads a JSON string starting at current position (expects opening quote).
        /// Handles escape sequences: \\, \", \n, \r, \t, \uXXXX
        /// Returns null if not a valid string.
        /// </summary>
        private static string ReadString(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '"')
                return null;
            pos++; // skip opening quote

            StringBuilder sb = new StringBuilder(64);
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == '"')
                {
                    pos++; // skip closing quote
                    return sb.ToString();
                }
                if (c == '\\')
                {
                    pos++;
                    if (pos >= json.Length) break;
                    char esc = json[pos];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            // \uXXXX
                            if (pos + 4 < json.Length)
                            {
                                string hex = json.Substring(pos + 1, 4);
                                int code;
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out code))
                                {
                                    sb.Append((char)code);
                                    pos += 4;
                                }
                                else
                                {
                                    sb.Append('\\');
                                    sb.Append('u');
                                }
                            }
                            break;
                        default:
                            sb.Append('\\');
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
                pos++;
            }
            return sb.ToString(); // unterminated string
        }

        /// <summary>
        /// Reads a flat JSON object { "key": "value", ... } and returns as Dictionary.
        /// </summary>
        private static Dictionary<string, string> ReadFlatObject(string json, ref int pos)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (pos >= json.Length || json[pos] != '{') return result;
            pos++; // skip '{'

            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] == '}') { pos++; break; }

                string key = ReadString(json, ref pos);
                if (key == null) break;

                SkipWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] != ':') break;
                pos++;

                SkipWhitespace(json, ref pos);
                string val = ReadString(json, ref pos);
                if (val != null) result[key] = val;

                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ',')
                    pos++;
            }
            return result;
        }

        /// <summary>
        /// Skips over a JSON object without parsing it (for nested structures in flat dict).
        /// </summary>
        private static void SkipObject(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '{') return;
            int depth = 1;
            pos++;
            bool inString = false;
            while (pos < json.Length && depth > 0)
            {
                char c = json[pos];
                if (inString)
                {
                    if (c == '\\') { pos++; } // skip escaped char
                    else if (c == '"') inString = false;
                }
                else
                {
                    if (c == '"') inString = true;
                    else if (c == '{') depth++;
                    else if (c == '}') depth--;
                }
                pos++;
            }
        }
    }
}
