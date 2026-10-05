using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace IK.Core {
    /// <summary>
    /// Compiler for ZSS, Ikemen GO's state language (`.zss`), into the same
    /// <see cref="StateDef"/> / <see cref="StateController"/> form the CNS reader produces, so
    /// the engine runs both the same way.
    ///
    /// Supported (what the shipped characters use, see engine/ikemen-go/src/compiler.go
    /// `stateCompileZ`): `[StateDef n; key: value; ...]`, `[Function name(params) rets]`,
    /// `if … { } else if … { } else { }`, `persistent(n)` and `ignoreHitPause` prefixes,
    /// bare `{ }` blocks, `let name = expr;` locals (`$name`), `call Name(args);` and
    /// `let x = call Name(args);`, and every sctrl written `name{ key: value; ... }`.
    ///
    /// Nested conditions are flattened: every controller gets the AND of the conditions of
    /// the blocks around it (an `else` adds the negation of the earlier branches). Functions
    /// are inlined at the call site with their locals renamed, so recursion is not supported
    /// (Ikemen forbids it too). Locals become per-state variables read with `zss__name`.
    /// </summary>
    public static class ZssFile {
        public const string LetType = "zss_let";
        public const string ExprType = "zss_expr";

        class Function {
            public string Name;
            public List<string> Params = new List<string>();
            public List<string> Rets = new List<string>();
            public string Body;
        }

        public static readonly List<string> Warnings = new List<string>();

        public static CnsFile Parse(byte[] bytes) => Parse(MugenDef.DecodeText(bytes));

        public static CnsFile Parse(string text) {
            var cns = new CnsFile();
            var src = StripComments(text ?? "");
            // split into sections at '[' that starts a line
            var sections = new List<KeyValuePair<string, string>>();
            int i = 0;
            string header = null;
            var body = new StringBuilder();
            while (i < src.Length) {
                int lineEnd = src.IndexOf('\n', i);
                if (lineEnd < 0) lineEnd = src.Length;
                string line = src.Substring(i, lineEnd - i);
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("[") && (trimmed.StartsWith("[statedef", StringComparison.OrdinalIgnoreCase) ||
                                                trimmed.StartsWith("[function", StringComparison.OrdinalIgnoreCase))) {
                    if (header != null) sections.Add(new KeyValuePair<string, string>(header, body.ToString()));
                    body.Clear();
                    // the header may span lines until ']'
                    int start = src.IndexOf('[', i);
                    int close = src.IndexOf(']', start);
                    if (close < 0) close = src.Length - 1;
                    header = src.Substring(start + 1, close - start - 1);
                    i = close + 1;
                    continue;
                }
                if (header != null) body.Append(line).Append('\n');
                i = lineEnd + 1;
            }
            if (header != null) sections.Add(new KeyValuePair<string, string>(header, body.ToString()));

            var functions = new Dictionary<string, Function>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sections) {
                if (!s.Key.TrimStart().StartsWith("function", StringComparison.OrdinalIgnoreCase)) continue;
                var f = ParseFunctionHeader(s.Key);
                if (f == null) continue;
                f.Body = s.Value;
                functions[f.Name] = f;
            }
            foreach (var s in sections) {
                if (!s.Key.TrimStart().StartsWith("statedef", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = SplitTop(s.Key, ';');
                var def = new StateDef { No = MugenDef.Atoi(parts[0].Trim().Substring(8).Trim()) };
                for (int p = 1; p < parts.Count; p++) {
                    int colon = parts[p].IndexOf(':');
                    if (colon < 0) continue;
                    string k = parts[p].Substring(0, colon).Trim().ToLowerInvariant();
                    if (k.Length > 0 && !def.Params.ContainsKey(k)) def.Params[k] = Locals(parts[p].Substring(colon + 1).Trim());
                }
                var existing = cns.Get(def.No);
                if (existing != null && def.No < 0) {
                    // the special states (-1..-4) of every file run one after the other
                    var ctx2 = new Compiler { Functions = functions, Def = existing };
                    ctx2.Block(s.Value, "", -1, 0, 0);
                    continue;
                }
                if (existing != null) continue;          // the first definition wins
                var ctx = new Compiler { Functions = functions, Def = def };
                ctx.Block(s.Value, "", -1, 0, 0);
                cns.States.Add(def);
                cns.ByNumber[def.No] = def;
            }
            return cns;
        }

        static Function ParseFunctionHeader(string h) {
            var m = Regex.Match(h.Trim(), @"^function\s+([A-Za-z_][\w\.]*)\s*\(([^)]*)\)\s*(.*)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var f = new Function { Name = m.Groups[1].Value };
            foreach (var p in m.Groups[2].Value.Split(',')) if (p.Trim().Length > 0) f.Params.Add(p.Trim());
            foreach (var r in m.Groups[3].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)) f.Rets.Add(r.Trim());
            return f;
        }

        /// <summary>`$name` → `zss__name` (a trigger the fighter answers from its locals).</summary>
        public static string Locals(string expr) => Regex.Replace(expr ?? "", @"\$([A-Za-z_]\w*)", "zss__$1");

        class Compiler {
            public Dictionary<string, Function> Functions;
            public StateDef Def;
            int inlineCount;
            int depth;

            /// <summary>Compiles a block body. <paramref name="cond"/> is the condition of the
            /// enclosing blocks ("" = always), persistent -1 = default.</summary>
            public void Block(string text, string cond, int persistent, int ignoreHitPause, int level) {
                if (level > 32) return;
                int i = 0;
                string elseCond = null;     // negation of the previous if/else-if chain
                while (true) {
                    Skip(text, ref i);
                    if (i >= text.Length) break;
                    int persist = persistent, ihp = ignoreHitPause;
                    // prefixes
                    while (true) {
                        Skip(text, ref i);
                        if (Word(text, i, "persistent")) {
                            i += "persistent".Length; Skip(text, ref i);
                            if (i < text.Length && text[i] == '(') {
                                int close = Match(text, i, '(', ')');
                                persist = MugenDef.Atoi(text.Substring(i + 1, close - i - 1).Trim());
                                i = close + 1;
                            } else persist = 0;
                            continue;
                        }
                        if (Word(text, i, "ignorehitpause")) { i += "ignorehitpause".Length; ihp = 1; continue; }
                        break;
                    }
                    Skip(text, ref i);
                    if (i >= text.Length) break;
                    bool isElse = false;
                    if (Word(text, i, "else")) {
                        i += 4; Skip(text, ref i);
                        isElse = true;
                    }
                    if (Word(text, i, "if")) {
                        i += 2;
                        int open = FindTop(text, i, '{');
                        if (open < 0) break;
                        string c = Locals(text.Substring(i, open - i).Trim());
                        int close = Match(text, open, '{', '}');
                        string inner = text.Substring(open + 1, Math.Max(0, close - open - 1));
                        string full = c;
                        if (isElse && elseCond != null) full = elseCond + " && (" + c + ")";
                        Block(inner, And(cond, full), persist, ihp, level + 1);
                        elseCond = isElse && elseCond != null ? elseCond + " && !(" + c + ")" : "!(" + c + ")";
                        i = close + 1;
                        continue;
                    }
                    if (isElse) {
                        // plain else { }
                        Skip(text, ref i);
                        if (i < text.Length && text[i] == '{') {
                            int close = Match(text, i, '{', '}');
                            Block(text.Substring(i + 1, Math.Max(0, close - i - 1)), And(cond, elseCond ?? "1"), persist, ihp, level + 1);
                            i = close + 1;
                        }
                        elseCond = null;
                        continue;
                    }
                    elseCond = null;
                    if (text[i] == '{') {
                        int close = Match(text, i, '{', '}');
                        Block(text.Substring(i + 1, Math.Max(0, close - i - 1)), cond, persist, ihp, level + 1);
                        i = close + 1;
                        continue;
                    }
                    if (Word(text, i, "let")) {
                        i += 3;
                        int end = FindTop(text, i, ';');
                        if (end < 0) end = text.Length;
                        string stmt = text.Substring(i, end - i);
                        i = end + 1;
                        int eq = stmt.IndexOf('=');
                        if (eq < 0) continue;
                        string name = stmt.Substring(0, eq).Trim().TrimStart('$');
                        string value = stmt.Substring(eq + 1).Trim();
                        if (Word(value, 0, "call")) {
                            string ret = Inline(value.Substring(4), cond, persist, ihp, level);
                            AddLet(name, ret != null ? Locals("$" + ret) : "0", cond, persist, ihp);
                        } else AddLet(name, Locals(value), cond, persist, ihp);
                        continue;
                    }
                    if (Word(text, i, "call")) {
                        i += 4;
                        int end = FindTop(text, i, ';');
                        if (end < 0) end = text.Length;
                        Inline(text.Substring(i, end - i), cond, persist, ihp, level);
                        i = end + 1;
                        continue;
                    }
                    if (Word(text, i, "switch") || Word(text, i, "for") || Word(text, i, "while")) {
                        Warnings.Add("zss: '" + text.Substring(i, Math.Min(12, text.Length - i)) + "' not supported in statedef " + Def.No);
                        int open = FindTop(text, i, '{');
                        if (open < 0) break;
                        i = Match(text, open, '{', '}') + 1;
                        continue;
                    }
                    // sctrl: name { params } — or an expression statement `sysvar(1) := 0;`
                    int brace = FindTop(text, i, '{');
                    int semi = FindTop(text, i, ';');
                    if (semi >= 0 && (brace < 0 || semi < brace)) {
                        var ec = new StateController { Type = ExprType, Name = "expr" };
                        ec.Params["value"] = Locals(text.Substring(i, semi - i).Trim());
                        Finish(ec, cond, persist, ihp);
                        i = semi + 1;
                        continue;
                    }
                    if (brace < 0) break;
                    string type = text.Substring(i, brace - i).Trim().ToLowerInvariant();
                    int cl = Match(text, brace, '{', '}');
                    string parms = text.Substring(brace + 1, Math.Max(0, cl - brace - 1));
                    i = cl + 1;
                    if (type == "sctrl") {           // `ignoreHitPause{ sctrl{} }` style wrapper
                        Block(parms, cond, persist, ihp, level + 1);
                        continue;
                    }
                    AddController(type, parms, cond, persist, ihp);
                }
            }

            string Inline(string callText, string cond, int persist, int ihp, int level) {
                var m = Regex.Match(callText.Trim(), @"^([A-Za-z_][\w\.]*)\s*\((.*)\)\s*;?\s*$", RegexOptions.Singleline);
                if (!m.Success) return null;
                Function f;
                if (!Functions.TryGetValue(m.Groups[1].Value, out f)) {
                    Warnings.Add("zss: unknown function " + m.Groups[1].Value);
                    return null;
                }
                if (depth > 8) return null;
                var args = SplitTop(m.Groups[2].Value, ',');
                string prefix = "fn" + (++inlineCount) + "_" + f.Name + "_";
                // rename the function's own locals first, then substitute the parameters
                string body = Regex.Replace(f.Body, @"\$([A-Za-z_]\w*)", mm => {
                    string n = mm.Groups[1].Value;
                    int pi = f.Params.FindIndex(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
                    if (pi >= 0) return "\u0001" + pi + "\u0002";
                    return "$" + prefix + n;
                });
                body = Regex.Replace(body, @"\blet\s+([A-Za-z_]\w*)", mm => "let " + prefix + mm.Groups[1].Value);
                body = Regex.Replace(body, "\u0001(\\d+)\u0002", mm => {
                    int pi = int.Parse(mm.Groups[1].Value);
                    return "(" + (pi < args.Count ? args[pi].Trim() : "0") + ")";
                });
                depth++;
                Block(body, cond, persist, ihp, level + 1);
                depth--;
                return f.Rets.Count > 0 ? prefix + f.Rets[0] : null;
            }

            void AddLet(string name, string value, string cond, int persist, int ihp) {
                var c = new StateController { Type = LetType, Name = "let " + name };
                c.Params["name"] = name.ToLowerInvariant();
                c.Params["value"] = value;
                Finish(c, cond, persist, ihp);
            }

            void AddController(string type, string parms, string cond, int persist, int ihp) {
                var c = new StateController { Type = type, Name = type };
                foreach (var p in SplitTop(parms, ';')) {
                    string t = p.Trim();
                    if (t.Length == 0) continue;
                    int colon = t.IndexOf(':');
                    if (colon < 0) continue;
                    string k = t.Substring(0, colon).Trim().ToLowerInvariant();
                    string v = Locals(t.Substring(colon + 1).Trim());
                    if (k == "persistent") { persist = MugenDef.Atoi(v); continue; }
                    if (k == "ignorehitpause") { ihp = MugenDef.Atoi(v); continue; }
                    if (k.Length > 0 && !c.Params.ContainsKey(k)) c.Params[k] = v;
                }
                Finish(c, cond, persist, ihp);
            }

            void Finish(StateController c, string cond, int persist, int ihp) {
                c.Persistent = persist < 0 ? 1 : persist;
                c.IgnoreHitPause = ihp;
                c.TriggerAll.Add(Expr.Parse(string.IsNullOrWhiteSpace(cond) ? "1" : cond));
                Def.Controllers.Add(c);
            }
        }

        static string And(string a, string b) {
            if (string.IsNullOrWhiteSpace(a)) return "(" + b + ")";
            return a + " && (" + b + ")";
        }

        static string StripComments(string s) {
            var sb = new StringBuilder(s.Length);
            bool inStr = false;
            for (int i = 0; i < s.Length; i++) {
                char c = s[i];
                if (c == '"') inStr = !inStr;
                if (c == '\n') inStr = false;
                if (c == '#' && !inStr) {
                    while (i < s.Length && s[i] != '\n') i++;
                    if (i < s.Length) sb.Append('\n');
                    continue;
                }
                if (c != '\r') sb.Append(c);
            }
            return sb.ToString();
        }

        static void Skip(string s, ref int i) {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ';')) i++;
        }

        static bool Word(string s, int i, string w) {
            if (i + w.Length > s.Length) return false;
            if (string.Compare(s, i, w, 0, w.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
            if (i + w.Length < s.Length && (char.IsLetterOrDigit(s[i + w.Length]) || s[i + w.Length] == '_')) return false;
            if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_')) return false;
            return true;
        }

        /// <summary>Index of <paramref name="ch"/> at paren depth 0 outside strings.</summary>
        static int FindTop(string s, int from, char ch) {
            int d = 0; bool str = false;
            for (int i = from; i < s.Length; i++) {
                char c = s[i];
                if (c == '"') { str = !str; continue; }
                if (str) continue;
                if (c == ch && d == 0) return i;
                if (c == '(' || c == '[') d++;
                else if (c == ')' || c == ']') d = Math.Max(0, d - 1);
            }
            return -1;
        }

        static int Match(string s, int open, char o, char c) {
            int d = 0; bool str = false;
            for (int i = open; i < s.Length; i++) {
                char ch = s[i];
                if (ch == '"') { str = !str; continue; }
                if (str) continue;
                if (ch == o) d++;
                else if (ch == c) { d--; if (d == 0) return i; }
            }
            return s.Length;
        }

        static List<string> SplitTop(string s, char sep) {
            var list = new List<string>();
            int d = 0, start = 0; bool str = false;
            for (int i = 0; i < s.Length; i++) {
                char c = s[i];
                if (c == '"') { str = !str; continue; }
                if (str) continue;
                if (c == '(' || c == '[' || c == '{') d++;
                else if (c == ')' || c == ']' || c == '}') d = Math.Max(0, d - 1);
                else if (c == sep && d == 0) { list.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            list.Add(s.Substring(start));
            return list;
        }
    }
}
