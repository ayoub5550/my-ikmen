using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IK.Core {
    /// <summary>
    /// Reader for the MUGEN text formats (.def, .cns headers, .snd lists): INI-like
    /// sections, `key = value` lines, `;` comments. Keys and section names are compared
    /// case-insensitively, as the engine does, and a repeated key keeps the first value
    /// (that is what Ikemen GO's iniutils does for DEF files).
    /// </summary>
    public class MugenDef {
        public class Section {
            public string Name;            // lowercase, without the brackets
            public string RawName;
            public readonly List<KeyValuePair<string, string>> Lines = new List<KeyValuePair<string, string>>();
            readonly Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public void Add(string key, string value) {
                Lines.Add(new KeyValuePair<string, string>(key, value));
                if (!map.ContainsKey(key)) map[key] = value;
            }

            public bool Has(string key) => map.ContainsKey(key);

            public string Get(string key, string fallback = "") =>
                map.TryGetValue(key, out var v) ? v : fallback;

            public int GetInt(string key, int fallback = 0) =>
                map.TryGetValue(key, out var v) ? Atoi(v) : fallback;

            public float GetFloat(string key, float fallback = 0f) =>
                map.TryGetValue(key, out var v) ? Atof(v) : fallback;
        }

        readonly List<Section> sections = new List<Section>();

        public IReadOnlyList<Section> Sections => sections;

        /// <summary>First section with this (case-insensitive) name, or null.</summary>
        public Section this[string name] {
            get {
                for (int i = 0; i < sections.Count; i++)
                    if (string.Equals(sections[i].Name, name, StringComparison.OrdinalIgnoreCase))
                        return sections[i];
                return null;
            }
        }

        public static MugenDef Parse(byte[] bytes) => Parse(DecodeText(bytes));

        public static MugenDef Parse(string text) {
            var def = new MugenDef();
            Section current = null;
            foreach (var raw in SplitLines(text)) {
                var line = StripComment(raw).Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[') {
                    int end = line.IndexOf(']');
                    string name = end > 0 ? line.Substring(1, end - 1).Trim() : line.Substring(1).Trim();
                    current = new Section { Name = name.ToLowerInvariant(), RawName = name };
                    def.sections.Add(current);
                    continue;
                }
                if (current == null) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) { current.Add(line.ToLowerInvariant(), ""); continue; }
                current.Add(line.Substring(0, eq).Trim().ToLowerInvariant(), line.Substring(eq + 1).Trim());
            }
            return def;
        }

        /// <summary>Everything before the first unquoted ';'.</summary>
        public static string StripComment(string line) {
            bool quoted = false;
            for (int i = 0; i < line.Length; i++) {
                if (line[i] == '"') quoted = !quoted;
                else if (line[i] == ';' && !quoted) return line.Substring(0, i);
            }
            return line;
        }

        public static string[] SplitLines(string text) =>
            text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        /// <summary>
        /// MUGEN files are mostly ASCII but authors save them in local code pages. UTF-8 is
        /// tried first and we fall back to Latin-1 so a stray byte never kills a load.
        /// </summary>
        public static string DecodeText(byte[] bytes) {
            if (bytes == null || bytes.Length == 0) return "";
            int start = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
            try {
                return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start);
            } catch (ArgumentException) {
                var sb = new StringBuilder(bytes.Length - start);
                for (int i = start; i < bytes.Length; i++) sb.Append((char)bytes[i]);
                return sb.ToString();
            }
        }

        /// <summary>Strips the surrounding quotes of a DEF value, if any.</summary>
        public static string Unquote(string s) {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') return s.Substring(1, s.Length - 2);
            return s;
        }

        /// <summary>
        /// MUGEN's forgiving integer parse (Ikemen GO `Atoi`): leading sign, digits until the
        /// first non-digit, anything else is 0. "12abc" is 12, "abc" is 0, "-x" is 0.
        /// </summary>
        public static int Atoi(string s) {
            if (string.IsNullOrEmpty(s)) return 0;
            int i = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            bool neg = false;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
            long v = 0; bool any = false;
            for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++) {
                any = true;
                v = v * 10 + (s[i] - '0');
                if (v > int.MaxValue) { v = neg ? -(long)int.MinValue : int.MaxValue; break; }
            }
            if (!any) return 0;
            return (int)(neg ? -v : v);
        }

        /// <summary>MUGEN's forgiving float parse: leading number, invariant culture, 0 on junk.</summary>
        public static float Atof(string s) {
            if (string.IsNullOrEmpty(s)) return 0f;
            int i = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            int start = i;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            bool digits = false;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits = true; }
            if (i < s.Length && s[i] == '.') {
                i++;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits = true; }
            }
            if (digits && i < s.Length && (s[i] == 'e' || s[i] == 'E')) {
                int save = i;
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                bool expDigits = false;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; expDigits = true; }
                if (!expDigits) i = save;
            }
            if (!digits) return 0f;
            return float.TryParse(s.Substring(start, i - start), NumberStyles.Float,
                                  CultureInfo.InvariantCulture, out var f) ? f : 0f;
        }

        /// <summary>True when the whole trimmed string is a number (Ikemen `IsNumeric`).</summary>
        public static bool IsNumeric(string s) {
            if (string.IsNullOrEmpty(s)) return false;
            return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        /// <summary>Splits "a, b, c" into trimmed parts (no quote handling needed in DEF values).</summary>
        public static string[] SplitCsv(string value, int max = -1) {
            var parts = max > 0 ? value.Split(new[] { ',' }, max) : value.Split(',');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }
    }
}
