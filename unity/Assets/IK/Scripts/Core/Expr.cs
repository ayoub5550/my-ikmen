using System;
using System.Collections.Generic;
using System.Globalization;

namespace IK.Core {
    /// <summary>
    /// What a MUGEN expression can ask about the world. Implemented by the fighter at
    /// runtime and by the test doubles in EditMode.
    /// </summary>
    public interface IExprContext {
        /// <summary>Value of a trigger/redirect, e.g. `anim`, `vel x`, `command`.
        /// <paramref name="arg"/> carries the argument of `command = "x"` style triggers or
        /// of `var(3)`. Return false from <c>TryTrigger</c> for an unknown name.</summary>
        bool TryTrigger(string name, string arg, float argValue, out float value);
        /// <summary>Random number 0..999, so tests can make it deterministic.</summary>
        float Random999();
    }

    /// <summary>
    /// MUGEN trigger-expression parser and evaluator (the part of `compiler.go` a character
    /// actually needs). Numbers are floats; "true" is non-zero, as in MUGEN.
    ///
    /// Supported: `|| ^^ && | ^ &`, `= != &lt; &lt;= &gt; &gt;=` (including the range forms
    /// `x = [a,b]`, `x = (a,b)`, `x != [a,b)`), `+ - * / % **`, unary `- ! ~`, parentheses,
    /// comma-separated tuples compared as `attr = S, NA`, the functions abs/floor/ceil/
    /// min/max/sin/cos/tan/atan/asin/acos/exp/ln/log/sign/ifelse/random/fmod/cond, and any
    /// trigger the context knows. An unknown name evaluates to 0 and is recorded in
    /// <see cref="Expr.UnknownNames"/> instead of throwing, so one unsupported trigger never
    /// kills a whole character.
    /// </summary>
    public class Expr {
        readonly Node root;
        public readonly string Source;
        /// <summary>Names this expression asked for that the context did not know.</summary>
        public readonly HashSet<string> UnknownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Expr(Node root, string source) { this.root = root; Source = source; }

        public static Expr Parse(string text) {
            var tokens = Tokenize(text ?? "");
            var p = new Parser(tokens);
            var node = p.ParseExpression();
            return new Expr(node, text);
        }

        public float Eval(IExprContext ctx) => root != null ? root.Eval(ctx, this) : 0f;
        public bool EvalBool(IExprContext ctx) => Math.Abs(Eval(ctx)) > 0.0001f;

        internal void NoteUnknown(string name) => UnknownNames.Add(name);

        // ------------------------------------------------------------------ tokens

        internal enum T { Number, Name, Op, LParen, RParen, LBracket, RBracket, Comma, String, End }

        internal struct Token {
            public T Type;
            public string Text;
            public float Number;
            public override string ToString() => Type + ":" + Text;
        }

        static readonly string[] Operators = {
            "||", "^^", "&&", "**", "!=", "<=", ">=", ":=", "=", "<", ">", "+", "-", "*", "/", "%",
            "|", "^", "&", "!", "~"
        };

        internal static List<Token> Tokenize(string s) {
            var list = new List<Token>();
            int i = 0;
            while (i < s.Length) {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '"') {
                    int end = s.IndexOf('"', i + 1);
                    if (end < 0) end = s.Length - 1;
                    list.Add(new Token { Type = T.String, Text = s.Substring(i + 1, Math.Max(0, end - i - 1)) });
                    i = end + 1;
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))) {
                    int start = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                    if (i < s.Length && (s[i] == 'e' || s[i] == 'E')) {
                        int save = i;
                        i++;
                        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                        if (i < s.Length && char.IsDigit(s[i])) { while (i < s.Length && char.IsDigit(s[i])) i++; }
                        else i = save;
                    }
                    float.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
                    list.Add(new Token { Type = T.Number, Text = s.Substring(start, i - start), Number = v });
                    continue;
                }
                if (char.IsLetter(c) || c == '_') {
                    int start = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.')) i++;
                    list.Add(new Token { Type = T.Name, Text = s.Substring(start, i - start) });
                    continue;
                }
                if (c == '(') { list.Add(new Token { Type = T.LParen, Text = "(" }); i++; continue; }
                if (c == ')') { list.Add(new Token { Type = T.RParen, Text = ")" }); i++; continue; }
                if (c == '[') { list.Add(new Token { Type = T.LBracket, Text = "[" }); i++; continue; }
                if (c == ']') { list.Add(new Token { Type = T.RBracket, Text = "]" }); i++; continue; }
                if (c == ',') { list.Add(new Token { Type = T.Comma, Text = "," }); i++; continue; }
                bool matched = false;
                foreach (var op in Operators) {
                    if (i + op.Length <= s.Length && string.CompareOrdinal(s, i, op, 0, op.Length) == 0) {
                        list.Add(new Token { Type = T.Op, Text = op });
                        i += op.Length;
                        matched = true;
                        break;
                    }
                }
                if (!matched) i++;   // skip anything we do not understand, MUGEN is forgiving
            }
            list.Add(new Token { Type = T.End, Text = "" });
            return list;
        }

        // ------------------------------------------------------------------ nodes

        internal abstract class Node {
            public abstract float Eval(IExprContext ctx, Expr owner);
        }

        class Const : Node {
            public float Value;
            public override float Eval(IExprContext ctx, Expr owner) => Value;
        }

        class StringConst : Node {
            public string Value;
            public override float Eval(IExprContext ctx, Expr owner) => 0f;
        }

        class Trigger : Node {
            public string Name;              // "anim", "vel x", "command"
            public string StringArg;
            public Node Arg;
            public override float Eval(IExprContext ctx, Expr owner) {
                float argValue = Arg != null ? Arg.Eval(ctx, owner) : 0f;
                if (ctx != null && ctx.TryTrigger(Name, StringArg, argValue, out var v)) return v;
                owner.NoteUnknown(Name);
                return 0f;
            }
        }

        class Unary : Node {
            public string Op;
            public Node A;
            public override float Eval(IExprContext ctx, Expr owner) {
                float a = A.Eval(ctx, owner);
                switch (Op) {
                    case "-": return -a;
                    case "!": return Math.Abs(a) > 0.0001f ? 0f : 1f;
                    case "~": return ~(int)a;
                }
                return a;
            }
        }

        class Binary : Node {
            public string Op;
            public Node A, B;
            public override float Eval(IExprContext ctx, Expr owner) {
                // short-circuit like MUGEN does
                if (Op == "&&") return Bool(A.Eval(ctx, owner)) && Bool(B.Eval(ctx, owner)) ? 1f : 0f;
                if (Op == "||") return Bool(A.Eval(ctx, owner)) || Bool(B.Eval(ctx, owner)) ? 1f : 0f;
                if (Op == "^^") return Bool(A.Eval(ctx, owner)) ^ Bool(B.Eval(ctx, owner)) ? 1f : 0f;
                float a = A.Eval(ctx, owner), b = B.Eval(ctx, owner);
                switch (Op) {
                    case "+": return a + b;
                    case "-": return a - b;
                    case "*": return a * b;
                    case "/": return Math.Abs(b) < 1e-9f ? 0f : a / b;
                    case "%": return Math.Abs(b) < 1e-9f ? 0f : (int)a % (int)b;
                    case "**": return (float)Math.Pow(a, b);
                    case "=": return Math.Abs(a - b) < 0.0001f ? 1f : 0f;
                    case "!=": return Math.Abs(a - b) >= 0.0001f ? 1f : 0f;
                    case "<": return a < b ? 1f : 0f;
                    case "<=": return a <= b ? 1f : 0f;
                    case ">": return a > b ? 1f : 0f;
                    case ">=": return a >= b ? 1f : 0f;
                    case "&": return (int)a & (int)b;
                    case "|": return (int)a | (int)b;
                    case "^": return (int)a ^ (int)b;
                }
                return 0f;
            }
            static bool Bool(float v) => Math.Abs(v) > 0.0001f;
        }

        /// <summary>`value = [a,b]` / `value != (a,b]` — MUGEN's range comparison.</summary>
        class Range : Node {
            public Node Value, Low, High;
            public bool IncludeLow, IncludeHigh, Negate;
            public override float Eval(IExprContext ctx, Expr owner) {
                float v = Value.Eval(ctx, owner), lo = Low.Eval(ctx, owner), hi = High.Eval(ctx, owner);
                bool inside = (IncludeLow ? v >= lo : v > lo) && (IncludeHigh ? v <= hi : v < hi);
                return (Negate ? !inside : inside) ? 1f : 0f;
            }
        }

        /// <summary>
        /// `AnimElem = n`, `AnimElem = n, m` and `AnimElem = n, &gt;= m`. MUGEN reads this as a
        /// comparison on `AnimElemTime(n)`: plain `= n` means "the element starts this tick"
        /// (time 0), the second term compares the elapsed time instead.
        /// </summary>
        class AnimElemCmp : Node {
            public Node Element;
            public Node Compare;        // null = compare with 0
            public string Op = "=";     // operator of the second term
            public bool Negate;         // `AnimElem != n`
            public override float Eval(IExprContext ctx, Expr owner) {
                float elem = Element != null ? Element.Eval(ctx, owner) : 0f;
                float t;
                if (ctx == null || !ctx.TryTrigger("animelemtime", null, elem, out t)) {
                    owner.NoteUnknown("animelem");
                    return 0f;
                }
                float want = Compare != null ? Compare.Eval(ctx, owner) : 0f;
                bool ok;
                switch (Op) {
                    case "!=": ok = Math.Abs(t - want) >= 0.0001f; break;
                    case "<": ok = t < want; break;
                    case "<=": ok = t <= want; break;
                    case ">": ok = t > want; break;
                    case ">=": ok = t >= want; break;
                    default: ok = Math.Abs(t - want) < 0.0001f; break;
                }
                if (Negate) ok = !ok;
                return ok ? 1f : 0f;
            }
        }

        class Call : Node {
            public string Name;
            public List<Node> Args = new List<Node>();
            public override float Eval(IExprContext ctx, Expr owner) {
                float A(int i) => i < Args.Count ? Args[i].Eval(ctx, owner) : 0f;
                switch (Name.ToLowerInvariant()) {
                    case "abs": return Math.Abs(A(0));
                    case "floor": return (float)Math.Floor(A(0));
                    case "ceil": return (float)Math.Ceiling(A(0));
                    case "min": return Math.Min(A(0), A(1));
                    case "max": return Math.Max(A(0), A(1));
                    case "sin": return (float)Math.Sin(A(0));
                    case "cos": return (float)Math.Cos(A(0));
                    case "tan": return (float)Math.Tan(A(0));
                    case "asin": return (float)Math.Asin(A(0));
                    case "acos": return (float)Math.Acos(A(0));
                    case "atan": return (float)Math.Atan(A(0));
                    case "exp": return (float)Math.Exp(A(0));
                    case "ln": return A(0) > 0 ? (float)Math.Log(A(0)) : 0f;
                    case "log": return A(0) > 0 && A(1) > 0 ? (float)(Math.Log(A(1)) / Math.Log(A(0))) : 0f;
                    case "sign": return Math.Sign(A(0));
                    case "fmod": return Math.Abs(A(1)) < 1e-9f ? 0f : A(0) % A(1);
                    case "ifelse":
                    case "cond": return Math.Abs(A(0)) > 0.0001f ? A(1) : A(2);
                    case "random": return ctx != null ? ctx.Random999() : 0f;
                    case "const":
                    case "gethitvar":
                    case "var":
                    case "fvar":
                    case "sysvar":
                    case "sysfvar":
                    case "animelemtime":
                    case "animelemno":
                    case "timemod":
                    case "stagevar":
                    case "numtarget":
                    case "numexplod":
                    case "numhelper":
                    case "numprojid":
                    case "teammode": {
                        // `const(movement.yaccel)` parses as a trigger name, `var("x")` as a
                        // string — both are really just the argument's text.
                        string sa = null;
                        bool nameArg = false;
                        if (Args.Count > 0) {
                            if (Args[0] is StringConst sc) { sa = sc.Value; nameArg = true; }
                            else if (Args[0] is Trigger tr) { sa = tr.Name; nameArg = true; }
                        }
                        float arg = nameArg ? 0f : A(0);
                        if (ctx != null && ctx.TryTrigger(Name, sa, arg, out var v)) return v;
                        owner.NoteUnknown(Name);
                        return 0f;
                    }
                    default: {
                        if (ctx != null && ctx.TryTrigger(Name, null, A(0), out var v)) return v;
                        owner.NoteUnknown(Name);
                        return 0f;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ parser

        class Parser {
            readonly List<Token> t;
            int i;
            public Parser(List<Token> tokens) { t = tokens; }

            Token Cur => t[i];
            bool IsOp(string op) => Cur.Type == T.Op && Cur.Text == op;
            void Next() { if (i < t.Count - 1) i++; }

            public Node ParseExpression() => ParseOr();

            Node ParseOr() {
                var a = ParseXor();
                while (IsOp("||")) { Next(); a = new Binary { Op = "||", A = a, B = ParseXor() }; }
                return a;
            }
            Node ParseXor() {
                var a = ParseAnd();
                while (IsOp("^^")) { Next(); a = new Binary { Op = "^^", A = a, B = ParseAnd() }; }
                return a;
            }
            Node ParseAnd() {
                var a = ParseBitOr();
                while (IsOp("&&")) { Next(); a = new Binary { Op = "&&", A = a, B = ParseBitOr() }; }
                return a;
            }
            Node ParseBitOr() {
                var a = ParseBitXor();
                while (IsOp("|")) { Next(); a = new Binary { Op = "|", A = a, B = ParseBitXor() }; }
                return a;
            }
            Node ParseBitXor() {
                var a = ParseBitAnd();
                while (IsOp("^")) { Next(); a = new Binary { Op = "^", A = a, B = ParseBitAnd() }; }
                return a;
            }
            Node ParseBitAnd() {
                var a = ParseEquality();
                while (IsOp("&")) { Next(); a = new Binary { Op = "&", A = a, B = ParseEquality() }; }
                return a;
            }

            Node ParseEquality() {
                var a = ParseComparison();
                while (IsOp("=") || IsOp("!=") || IsOp(":=")) {
                    string op = Cur.Text == ":=" ? "=" : Cur.Text;
                    Next();
                    // range form:  x = [1,5)   /   x != (0,3]
                    if (Cur.Type == T.LBracket || (Cur.Type == T.LParen && LooksLikeRange())) {
                        bool incLow = Cur.Type == T.LBracket;
                        Next();
                        var lo = ParseExpression();
                        if (Cur.Type == T.Comma) Next();
                        var hi = ParseExpression();
                        bool incHigh = Cur.Type == T.RBracket;
                        if (Cur.Type == T.RBracket || Cur.Type == T.RParen) Next();
                        a = new Range { Value = a, Low = lo, High = hi, IncludeLow = incLow, IncludeHigh = incHigh, Negate = op == "!=" };
                        continue;
                    }
                    var b = ParseComparison();
                    // tuple comparison (`attr = S, NA`): the extra terms are flags we do not
                    // evaluate numerically — keep the first comparison, skip the rest.
                    while (Cur.Type == T.Comma) { Next(); ParseComparison(); }
                    a = new Binary { Op = op, A = a, B = b };
                }
                return a;
            }

            /// <summary>`(` starts a range only when it is followed by `expr , expr )`.</summary>
            bool LooksLikeRange() {
                int depth = 0;
                for (int j = i; j < t.Count; j++) {
                    if (t[j].Type == T.LParen || t[j].Type == T.LBracket) depth++;
                    else if (t[j].Type == T.RParen || t[j].Type == T.RBracket) {
                        depth--;
                        if (depth == 0) return false;
                    } else if (t[j].Type == T.Comma && depth == 1) return true;
                    else if (t[j].Type == T.End) return false;
                }
                return false;
            }

            Node ParseComparison() {
                var a = ParseAdditive();
                while (IsOp("<") || IsOp("<=") || IsOp(">") || IsOp(">=")) {
                    string op = Cur.Text;
                    Next();
                    a = new Binary { Op = op, A = a, B = ParseAdditive() };
                }
                return a;
            }

            Node ParseAdditive() {
                var a = ParseMultiplicative();
                while (IsOp("+") || IsOp("-")) {
                    string op = Cur.Text;
                    Next();
                    a = new Binary { Op = op, A = a, B = ParseMultiplicative() };
                }
                return a;
            }

            Node ParseMultiplicative() {
                var a = ParsePower();
                while (IsOp("*") || IsOp("/") || IsOp("%")) {
                    string op = Cur.Text;
                    Next();
                    a = new Binary { Op = op, A = a, B = ParsePower() };
                }
                return a;
            }

            Node ParsePower() {
                var a = ParseUnary();
                if (IsOp("**")) { Next(); return new Binary { Op = "**", A = a, B = ParsePower() }; }
                return a;
            }

            Node ParseUnary() {
                if (IsOp("-")) { Next(); return new Unary { Op = "-", A = ParseUnary() }; }
                if (IsOp("!")) { Next(); return new Unary { Op = "!", A = ParseUnary() }; }
                if (IsOp("~")) { Next(); return new Unary { Op = "~", A = ParseUnary() }; }
                if (IsOp("+")) { Next(); return ParseUnary(); }
                return ParsePrimary();
            }

            Node ParsePrimary() {
                switch (Cur.Type) {
                    case T.Number: {
                        var n = new Const { Value = Cur.Number };
                        Next();
                        return n;
                    }
                    case T.String: {
                        var n = new StringConst { Value = Cur.Text };
                        Next();
                        return n;
                    }
                    case T.LParen: {
                        Next();
                        var inner = ParseExpression();
                        if (Cur.Type == T.RParen) Next();
                        return inner;
                    }
                    case T.Name:
                        return ParseName();
                }
                Next();
                return new Const { Value = 0f };
            }

            static readonly HashSet<string> TwoWordTriggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "vel", "pos", "p2dist", "p2bodydist", "p1dist", "p1bodydist", "screenpos",
                "parentdist", "rootdist", "animelem", "projhittime", "projcontacttime",
                "projguardedtime", "projhit", "projcontact", "projguarded"
            };

            Node ParseName() {
                string name = Cur.Text;
                Next();

                // function call: name( args )
                if (Cur.Type == T.LParen) {
                    Next();
                    var call = new Call { Name = name };
                    if (Cur.Type != T.RParen) {
                        call.Args.Add(ParseExpression());
                        while (Cur.Type == T.Comma) { Next(); call.Args.Add(ParseExpression()); }
                    }
                    if (Cur.Type == T.RParen) Next();
                    return call;
                }

                // two-word triggers: `vel x`, `pos y`, `p2bodydist x`, `animelem = 3`
                if (TwoWordTriggers.Contains(name) && Cur.Type == T.Name &&
                    (Cur.Text.Length == 1 || string.Equals(Cur.Text, "x", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(Cur.Text, "y", StringComparison.OrdinalIgnoreCase))) {
                    name = name + " " + Cur.Text;
                    Next();
                }

                // `AnimElem = n [, [op] m]` — a comparison on AnimElemTime(n), not on a value
                if (string.Equals(name, "animelem", StringComparison.OrdinalIgnoreCase) &&
                    Cur.Type == T.Op && (Cur.Text == "=" || Cur.Text == "!=")) {
                    bool negate = Cur.Text == "!=";
                    Next();
                    var node = new AnimElemCmp { Negate = negate, Element = ParseComparison() };
                    if (Cur.Type == T.Comma) {
                        Next();
                        if (Cur.Type == T.Op && (Cur.Text == "=" || Cur.Text == "!=" || Cur.Text == "<" ||
                                                 Cur.Text == "<=" || Cur.Text == ">" || Cur.Text == ">=")) {
                            node.Op = Cur.Text;
                            Next();
                        }
                        node.Compare = ParseComparison();
                    }
                    return node;
                }

                // `command = "name"` and other string-valued triggers are handled by the
                // equality parser: keep the trigger here, the string shows up as its B side.
                if (Cur.Type == T.Op && (Cur.Text == "=" || Cur.Text == "!=") &&
                    i + 1 < t.Count && t[i + 1].Type == T.String) {
                    string op = Cur.Text;
                    Next();
                    string value = Cur.Text;
                    Next();
                    var trig = new Trigger { Name = name, StringArg = value };
                    return op == "=" ? (Node)trig : new Unary { Op = "!", A = trig };
                }

                // bare state flags: `statetype = A`, `movetype = I`, `hitdefattr = SC, NA`
                if (Cur.Type == T.Op && (Cur.Text == "=" || Cur.Text == "!=") &&
                    i + 1 < t.Count && t[i + 1].Type == T.Name && IsFlagTrigger(name)) {
                    string op = Cur.Text;
                    Next();
                    string value = Cur.Text;
                    Next();
                    while (Cur.Type == T.Comma) { Next(); if (Cur.Type == T.Name) Next(); }   // attr lists
                    var trig = new Trigger { Name = name, StringArg = value };
                    return op == "=" ? (Node)trig : new Unary { Op = "!", A = trig };
                }

                return new Trigger { Name = name };
            }

            static bool IsFlagTrigger(string name) {
                switch (name.ToLowerInvariant()) {
                    case "statetype":
                    case "movetype":
                    case "physics":
                    case "hitdefattr":
                    case "p2statetype":
                    case "p2movetype":
                    case "hitpausetime":
                        return true;
                }
                return false;
            }
        }
    }
}
