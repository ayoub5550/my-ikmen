using System.Collections.Generic;
using System.Text;

namespace IK.UI {
    /// <summary>
    /// Minimal Arabic shaper + bidi reordering for Unity's legacy uGUI <c>Text</c>, which
    /// renders glyphs left to right with no OpenType shaping. We map each letter to its
    /// Unicode presentation form (U+FE70…U+FEFC), join lam-alef, then emit the string in
    /// visual order (RTL runs reversed, Latin/number runs kept LTR).
    /// The shipped UI font (Amiri, OFL) contains the presentation-forms block.
    /// Pure logic, unit-tested in IK.Tests.
    /// </summary>
    public static class ArabicShaper {
        // base char -> { isolated, final, initial, medial }; a 2-entry row means the letter
        // never joins to the following one (initial/medial fall back to isolated/final).
        static readonly Dictionary<char, char[]> Forms = new Dictionary<char, char[]> {
            { '\u0621', new[] { '\uFE80', '\uFE80', '\uFE80', '\uFE80' } },
            { '\u0622', new[] { '\uFE81', '\uFE82', '\uFE81', '\uFE82' } },
            { '\u0623', new[] { '\uFE83', '\uFE84', '\uFE83', '\uFE84' } },
            { '\u0624', new[] { '\uFE85', '\uFE86', '\uFE85', '\uFE86' } },
            { '\u0625', new[] { '\uFE87', '\uFE88', '\uFE87', '\uFE88' } },
            { '\u0626', new[] { '\uFE89', '\uFE8A', '\uFE8B', '\uFE8C' } },
            { '\u0627', new[] { '\uFE8D', '\uFE8E', '\uFE8D', '\uFE8E' } },
            { '\u0628', new[] { '\uFE8F', '\uFE90', '\uFE91', '\uFE92' } },
            { '\u0629', new[] { '\uFE93', '\uFE94', '\uFE93', '\uFE94' } },
            { '\u062A', new[] { '\uFE95', '\uFE96', '\uFE97', '\uFE98' } },
            { '\u062B', new[] { '\uFE99', '\uFE9A', '\uFE9B', '\uFE9C' } },
            { '\u062C', new[] { '\uFE9D', '\uFE9E', '\uFE9F', '\uFEA0' } },
            { '\u062D', new[] { '\uFEA1', '\uFEA2', '\uFEA3', '\uFEA4' } },
            { '\u062E', new[] { '\uFEA5', '\uFEA6', '\uFEA7', '\uFEA8' } },
            { '\u062F', new[] { '\uFEA9', '\uFEAA', '\uFEA9', '\uFEAA' } },
            { '\u0630', new[] { '\uFEAB', '\uFEAC', '\uFEAB', '\uFEAC' } },
            { '\u0631', new[] { '\uFEAD', '\uFEAE', '\uFEAD', '\uFEAE' } },
            { '\u0632', new[] { '\uFEAF', '\uFEB0', '\uFEAF', '\uFEB0' } },
            { '\u0633', new[] { '\uFEB1', '\uFEB2', '\uFEB3', '\uFEB4' } },
            { '\u0634', new[] { '\uFEB5', '\uFEB6', '\uFEB7', '\uFEB8' } },
            { '\u0635', new[] { '\uFEB9', '\uFEBA', '\uFEBB', '\uFEBC' } },
            { '\u0636', new[] { '\uFEBD', '\uFEBE', '\uFEBF', '\uFEC0' } },
            { '\u0637', new[] { '\uFEC1', '\uFEC2', '\uFEC3', '\uFEC4' } },
            { '\u0638', new[] { '\uFEC5', '\uFEC6', '\uFEC7', '\uFEC8' } },
            { '\u0639', new[] { '\uFEC9', '\uFECA', '\uFECB', '\uFECC' } },
            { '\u063A', new[] { '\uFECD', '\uFECE', '\uFECF', '\uFED0' } },
            { '\u0641', new[] { '\uFED1', '\uFED2', '\uFED3', '\uFED4' } },
            { '\u0642', new[] { '\uFED5', '\uFED6', '\uFED7', '\uFED8' } },
            { '\u0643', new[] { '\uFED9', '\uFEDA', '\uFEDB', '\uFEDC' } },
            { '\u0644', new[] { '\uFEDD', '\uFEDE', '\uFEDF', '\uFEE0' } },
            { '\u0645', new[] { '\uFEE1', '\uFEE2', '\uFEE3', '\uFEE4' } },
            { '\u0646', new[] { '\uFEE5', '\uFEE6', '\uFEE7', '\uFEE8' } },
            { '\u0647', new[] { '\uFEE9', '\uFEEA', '\uFEEB', '\uFEEC' } },
            { '\u0648', new[] { '\uFEED', '\uFEEE', '\uFEED', '\uFEEE' } },
            { '\u0649', new[] { '\uFEEF', '\uFEF0', '\uFEEF', '\uFEF0' } },
            { '\u064A', new[] { '\uFEF1', '\uFEF2', '\uFEF3', '\uFEF4' } },
        };

        // letters that never connect to the following letter
        static readonly HashSet<char> NoForwardJoin = new HashSet<char> {
            '\u0621', '\u0622', '\u0623', '\u0624', '\u0625', '\u0627', '\u0629',
            '\u062F', '\u0630', '\u0631', '\u0632', '\u0648', '\u0649'
        };

        static readonly Dictionary<char, char[]> LamAlef = new Dictionary<char, char[]> {
            { '\u0622', new[] { '\uFEF5', '\uFEF6' } },
            { '\u0623', new[] { '\uFEF7', '\uFEF8' } },
            { '\u0625', new[] { '\uFEF9', '\uFEFA' } },
            { '\u0627', new[] { '\uFEFB', '\uFEFC' } },
        };

        public static bool IsArabicLetter(char c) => Forms.ContainsKey(c);
        public static bool IsArabicMark(char c) => c >= '\u064B' && c <= '\u0652';   // harakat
        public static bool IsRtl(char c) =>
            IsArabicLetter(c) || IsArabicMark(c) || (c >= '\u0600' && c <= '\u06FF') || (c >= '\uFE70' && c <= '\uFEFF');

        public static bool ContainsArabic(string s) {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s) if (IsRtl(c)) return true;
            return false;
        }

        /// <summary>Shape + reorder a string for a left-to-right glyph renderer.</summary>
        public static string Shape(string input) {
            if (!ContainsArabic(input)) return input;
            // dev.8: every line is reordered on its own (reordering the whole string moved
            // line breaks and swapped lines: the About page lines overlapped)
            if (input.IndexOf('\n') >= 0) {
                var lines = input.Split('\n');
                for (int i = 0; i < lines.Length; i++) lines[i] = Shape(lines[i]);
                return string.Join("\n", lines);
            }
            string shaped = Join(input);
            return Reorder(shaped);
        }

        static string Join(string s) {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++) {
                char c = s[i];
                if (!Forms.ContainsKey(c)) { sb.Append(c); continue; }

                // lam + alef ligature
                if (c == '\u0644' && i + 1 < s.Length && LamAlef.ContainsKey(s[i + 1])) {
                    bool joinedBefore = PrevJoins(s, i);
                    sb.Append(LamAlef[s[i + 1]][joinedBefore ? 1 : 0]);
                    i++;
                    continue;
                }

                bool prev = PrevJoins(s, i);
                bool next = NextJoins(s, i);
                var f = Forms[c];
                char glyph;
                if (prev && next) glyph = f[3];
                else if (prev) glyph = f[1];
                else if (next) glyph = f[2];
                else glyph = f[0];
                sb.Append(glyph);
            }
            return sb.ToString();
        }

        static bool PrevJoins(string s, int i) {
            for (int j = i - 1; j >= 0; j--) {
                char p = s[j];
                if (IsArabicMark(p)) continue;
                return Forms.ContainsKey(p) && !NoForwardJoin.Contains(p);
            }
            return false;
        }

        static bool NextJoins(string s, int i) {
            for (int j = i + 1; j < s.Length; j++) {
                char n = s[j];
                if (IsArabicMark(n)) continue;
                return Forms.ContainsKey(n);
            }
            return false;
        }

        static readonly Dictionary<char, char> Mirrored = new Dictionary<char, char> {
            { '(', ')' }, { ')', '(' }, { '[', ']' }, { ']', '[' }, { '{', '}' }, { '}', '{' },
            { '<', '>' }, { '>', '<' }
        };

        /// <summary>
        /// Visual order for a predominantly-RTL line: reverse the sequence of runs, reverse
        /// the characters inside RTL runs, keep Latin words and numbers left to right.
        /// Good enough for UI labels (no nested bidi levels).
        /// </summary>
        public static string Reorder(string s) {
            var runs = new List<KeyValuePair<bool, string>>();   // key: isRtl
            var sb = new StringBuilder();
            bool? currentRtl = null;
            foreach (char c in s) {
                bool rtl = IsRtl(c);
                bool neutral = char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c);
                if (neutral && currentRtl.HasValue) rtl = currentRtl.Value;        // glue to the current run
                else if (neutral) rtl = true;
                if (currentRtl.HasValue && rtl != currentRtl.Value) {
                    runs.Add(new KeyValuePair<bool, string>(currentRtl.Value, sb.ToString()));
                    sb.Length = 0;
                }
                currentRtl = rtl;
                sb.Append(c);
            }
            if (sb.Length > 0 && currentRtl.HasValue)
                runs.Add(new KeyValuePair<bool, string>(currentRtl.Value, sb.ToString()));

            // A space that sits between an RTL and an LTR run must stay *between* them;
            // reversing it with the RTL run is what glues "screenpack" to the next word.
            var outSb = new StringBuilder(s.Length);
            bool pendingSpace = false;
            for (int i = runs.Count - 1; i >= 0; i--) {
                string text = runs[i].Value;
                bool leadSpace = text.Length > 0 && char.IsWhiteSpace(text[0]);
                bool tailSpace = text.Length > 0 && char.IsWhiteSpace(text[text.Length - 1]);
                string trimmed = text.Trim();
                if (trimmed.Length == 0) { pendingSpace = true; continue; }
                if (outSb.Length > 0 && (pendingSpace || tailSpace || leadSpace)) outSb.Append(' ');
                pendingSpace = false;
                if (runs[i].Key) {
                    var chars = trimmed.ToCharArray();
                    System.Array.Reverse(chars);
                    for (int k = 0; k < chars.Length; k++)
                        if (Mirrored.TryGetValue(chars[k], out var m)) chars[k] = m;
                    outSb.Append(chars);
                } else {
                    outSb.Append(trimmed);
                }
            }
            return outSb.ToString();
        }
    }
}
