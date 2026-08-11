using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;

namespace OstraI18n
{
    // Runtime UI text translation: catches ALL text reaching the screen regardless of source —
    // strings.json via GetString, hardcoded C# literals (.text="..."), and prefab-baked m_text
    // (set in the editor, bypasses the property setter at deserialization).
    // Translates English -> active language via gui_<lang>.json.
    // Unknown English UI strings are dumped to gui_unknown.txt for the translation pipeline.
    internal static class GuiText
    {
        // english -> translation (exact match on the displayed string, never regex/substring —
        // a mismatch here can only ever mean "not in the dictionary yet", not a partial rewrite)
        internal static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        internal static bool DumpUnknown = true;

        // Per-component last-seen raw text, to detect typewriter/letter-reveal animations (some
        // screens type text out one character per frame). Translating mid-reveal would exact-match
        // a *partial* frame (e.g. "Tha" out of "Thank you") against the dictionary and splice in a
        // full replacement the game's own reveal coroutine never expected, breaking/freezing the
        // animation on that fragment. While a call's value is a strict extension of the previous
        // call's value for the same component, we treat it as "still typing" and pass it through
        // untouched; once it stops growing (same value again, unrelated value, or shrinks) it's
        // treated as settled and translated normally.
        private static readonly ConditionalWeakTable<object, string[]> _lastRaw = new ConditionalWeakTable<object, string[]>();

        internal static string Translate(string s)
        {
            if (!RuData.Active || string.IsNullOrEmpty(s)) return s;
            string t;
            if (Map.TryGetValue(s, out t)) return t;
            if (DumpUnknown) NoteUnknown(s);
            return s;
        }

        internal static string TranslateTracked(object instance, string s)
        {
            if (!RuData.Active || string.IsNullOrEmpty(s) || instance == null) return Translate(s);
            string[] box;
            if (_lastRaw.TryGetValue(instance, out box))
            {
                var prev = box[0];
                bool stillGrowing = prev != null && s.Length > prev.Length && s.StartsWith(prev, StringComparison.Ordinal);
                box[0] = s;
                if (stillGrowing) return s;
            }
            else
            {
                _lastRaw.Add(instance, new[] { s });
            }
            return Translate(s);
        }

        private static void NoteUnknown(string s)
        {
            try
            {
                int letters = 0, digits = 0;
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c >= 0x0400 && c <= 0x04FF) return;   // already Cyrillic — skip
                    if (c >= 0x4E00 && c <= 0x9FFF) return;   // CJK — stray dev/locale-test string, out of scope
                    if (char.IsLetter(c)) letters++;
                    else if (char.IsDigit(c)) digits++;
                }
                if (letters == 0) return;          // clocks/pure numbers
                if (digits * 2 > letters) return;  // mostly dynamic numbers (speed, stats)
                if (s.Length > 120) return;        // huge blobs — not UI labels
                if (s.IndexOf('<') >= 0 || s.IndexOf('[') >= 0) return; // markup/engine tokens — engine handles those
                if (s.IndexOf('/') >= 0 || s.IndexOf('\\') >= 0) return; // file paths
                if (s.StartsWith("Spawning ", StringComparison.Ordinal)) return;      // world-gen debug spam
                if (s.StartsWith("txtDebug", StringComparison.Ordinal)) return;       // leftover placeholder objects
                if (s.IndexOf("Debug", StringComparison.Ordinal) >= 0) return;        // dev/debug readouts
                lock (_seen)
                {
                    if (!_seen.Add(s)) return;
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(Plugin.DataDir.Value, "gui_unknown.txt"),
                        s.Replace("\r", " ").Replace("\n", "\\n") + "\n");
                }
            }
            catch { }
        }

        // Harmony prefix on TMP_Text.set_text(string value) — runtime assignments.
        // Modifies the value in place before the setter stores it. __instance is Harmony-injected;
        // needed for the typewriter-animation guard (see TranslateTracked).
        public static void SetTextPrefix(object __instance, ref string value)
        {
            if (!RuData.Active) return;
            try { value = TranslateTracked(__instance, value); } catch { }
        }

        // Harmony postfix on MaskableGraphic.OnEnable() — prefab-baked text becomes visible.
        // This base method is shared by EVERY UI graphic (TMP_Text, legacy UI.Text, Image, ...),
        // so __instance must be typed as the base and safely filtered with `as` — casting it
        // directly to TMP_Text crashes the process (invalid type coercion) for non-TMP callers.
        // Also handles legacy UnityEngine.UI.Text: several game screens (e.g. chargen) still use
        // the old Unity UI Text component instead of TextMeshPro, and were invisible to this hook
        // entirely (not even reaching gui_unknown.txt) before this branch was added.
        // Reads current text, translates if English, writes back (goes through set_text -> idempotent).
        public static void OnEnablePostfix(UnityEngine.UI.MaskableGraphic __instance)
        {
            if (!RuData.Active) return;
            try
            {
                var tmp = __instance as TMP_Text;
                if (tmp != null)
                {
                    var cur = tmp.text;
                    if (string.IsNullOrEmpty(cur)) return;
                    var tr = TranslateTracked(tmp, cur);
                    if (!ReferenceEquals(cur, tr) && cur != tr) tmp.text = tr;
                    return;
                }
                var legacy = __instance as UnityEngine.UI.Text;
                if (legacy != null)
                {
                    var cur = legacy.text;
                    if (string.IsNullOrEmpty(cur)) return;
                    var tr = TranslateTracked(legacy, cur);
                    if (!ReferenceEquals(cur, tr) && cur != tr) legacy.text = tr;
                }
            }
            catch { }
        }

        // Periodic fallback sweep (main thread, called every ~2s from Plugin's background loop).
        // OnEnable only fires the first time a GameObject becomes active in the hierarchy; several
        // screens (e.g. chargen category labels: SKIN/HAIR/PRONOUN/...) keep their whole panel alive
        // and toggle visibility via CanvasGroup alpha/interactable instead of SetActive, or their
        // OnEnable happened once before this plugin's patches were applied. Those texts never route
        // through set_text or OnEnable again, so they were invisible to both hooks. This sweep scans
        // every TMP_Text/UI.Text in the scene (including inactive, so it also catches labels before
        // their panel is first shown) and translates on sight — a safety net, not the primary path.
        internal static void SweepScene()
        {
            if (!RuData.Active) return;
            try
            {
                var tmps = UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
                for (int i = 0; i < tmps.Length; i++)
                {
                    try
                    {
                        var cur = tmps[i].text;
                        if (string.IsNullOrEmpty(cur)) continue;
                        var tr = TranslateTracked(tmps[i], cur);
                        if (!ReferenceEquals(cur, tr) && cur != tr) tmps[i].text = tr;
                    }
                    catch { }
                }
                var texts = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    try
                    {
                        var cur = texts[i].text;
                        if (string.IsNullOrEmpty(cur)) continue;
                        var tr = TranslateTracked(texts[i], cur);
                        if (!ReferenceEquals(cur, tr) && cur != tr) texts[i].text = tr;
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
