using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>One `[State …]` block: a controller type, its triggers and its parameters.</summary>
    public class StateController {
        public string Type = "";
        public string Name = "";
        /// <summary>`triggerall` lines — every one of them must be true.</summary>
        public readonly List<Expr> TriggerAll = new List<Expr>();
        /// <summary>trigger1..triggerN groups: the group is an AND, the groups are OR-ed.</summary>
        public readonly Dictionary<int, List<Expr>> TriggerGroups = new Dictionary<int, List<Expr>>();
        public readonly Dictionary<string, string> Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>`persistent` (default 1): 0 = run only once per state entry.</summary>
        public int Persistent = 1;
        public int IgnoreHitPause;

        public string Get(string key, string fallback = "") =>
            Params.TryGetValue(key, out var v) ? v : fallback;

        public bool Has(string key) => Params.ContainsKey(key);

        /// <summary>Evaluates triggerall + the trigger groups exactly as MUGEN does.</summary>
        public bool TriggersPass(IExprContext ctx) {
            foreach (var t in TriggerAll) if (!t.EvalBool(ctx)) return false;
            if (TriggerGroups.Count == 0) return TriggerAll.Count > 0;
            foreach (var kv in TriggerGroups) {
                bool all = true;
                foreach (var t in kv.Value) {
                    if (!t.EvalBool(ctx)) { all = false; break; }
                }
                if (all) return true;
            }
            return false;
        }

        public override string ToString() => Type + " (" + Name + ")";
    }

    /// <summary>A `[Statedef n]` with its parameters and controllers.</summary>
    public class StateDef {
        public int No;
        public readonly Dictionary<string, string> Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<StateController> Controllers = new List<StateController>();

        public string Get(string key, string fallback = "") =>
            Params.TryGetValue(key, out var v) ? v : fallback;
        public bool Has(string key) => Params.ContainsKey(key);
        public override string ToString() => "Statedef " + No + " (" + Controllers.Count + " controllers)";
    }

    /// <summary>
    /// Reader for MUGEN state files (.cns and the `[Statedef -1]` block of a .cmd).
    /// Sections are `[Statedef n]` followed by `[State n, label]` blocks; inside a block,
    /// `type`, `trigger*` and `persistent` have meaning, everything else is a parameter the
    /// controller interprets. Expressions are parsed eagerly with <see cref="Expr"/> so a
    /// syntax problem shows up at load time, not in the middle of a match.
    /// </summary>
    public class CnsFile {
        public readonly List<StateDef> States = new List<StateDef>();
        public readonly Dictionary<int, StateDef> ByNumber = new Dictionary<int, StateDef>();
        /// <summary>Non-state sections such as `[Data]`, `[Size]`, `[Velocity]`, `[Movement]`.</summary>
        public MugenDef Header;

        public StateDef Get(int no) => ByNumber.TryGetValue(no, out var s) ? s : null;

        public static CnsFile Parse(byte[] bytes) => Parse(MugenDef.DecodeText(bytes));

        public static CnsFile Parse(string text) {
            var cns = new CnsFile { Header = MugenDef.Parse(text) };
            StateDef state = null;
            StateController controller = null;

            foreach (var raw in MugenDef.SplitLines(text)) {
                var line = MugenDef.StripComment(raw).Trim();
                if (line.Length == 0) continue;

                if (line[0] == '[') {
                    int close = line.IndexOf(']');
                    string inner = (close > 0 ? line.Substring(1, close - 1) : line.Substring(1)).Trim();
                    string low = inner.ToLowerInvariant();
                    if (low.StartsWith("statedef")) {
                        state = new StateDef { No = MugenDef.Atoi(inner.Substring("statedef".Length).Trim()) };
                        controller = null;
                        cns.States.Add(state);
                        if (!cns.ByNumber.ContainsKey(state.No)) cns.ByNumber[state.No] = state;
                        else {
                            // a redefined state replaces the previous one, like MUGEN
                            cns.ByNumber[state.No] = state;
                        }
                    } else if (low.StartsWith("state ") && state != null) {
                        controller = new StateController { Name = inner.Substring(6).Trim() };
                        state.Controllers.Add(controller);
                    } else {
                        state = null;
                        controller = null;
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                if (controller != null) {
                    if (key == "type") { controller.Type = value.Trim().ToLowerInvariant(); continue; }
                    if (key == "persistent") { controller.Persistent = MugenDef.Atoi(value); continue; }
                    if (key == "ignorehitpause") { controller.IgnoreHitPause = MugenDef.Atoi(value); continue; }
                    if (key == "triggerall") { controller.TriggerAll.Add(Expr.Parse(value)); continue; }
                    if (key.StartsWith("trigger") && key.Length > 7) {
                        int group = MugenDef.Atoi(key.Substring(7));
                        if (!controller.TriggerGroups.TryGetValue(group, out var list)) {
                            list = new List<Expr>();
                            controller.TriggerGroups[group] = list;
                        }
                        list.Add(Expr.Parse(value));
                        continue;
                    }
                    if (!controller.Params.ContainsKey(key)) controller.Params[key] = value;
                    continue;
                }

                if (state != null && !state.Params.ContainsKey(key)) state.Params[key] = value;
            }
            return cns;
        }

        /// <summary>Merges another state file into this one (character states over common states).</summary>
        public void Merge(CnsFile other) {
            if (other == null) return;
            foreach (var s in other.States) {
                States.Add(s);
                ByNumber[s.No] = s;
            }
        }

        /// <summary>Every trigger name used anywhere, for coverage reporting.</summary>
        public HashSet<string> TriggerNames() {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in States)
                foreach (var c in s.Controllers) {
                    foreach (var t in c.TriggerAll) CollectNames(t, names);
                    foreach (var g in c.TriggerGroups.Values)
                        foreach (var t in g) CollectNames(t, names);
                }
            return names;
        }

        static void CollectNames(Expr e, HashSet<string> into) {
            // Expr keeps the source text; a cheap tokenizer pass is enough for reporting.
            foreach (var tok in Expr.Tokenize(e.Source ?? ""))
                if (tok.Type == Expr.T.Name) into.Add(tok.Text);
        }
    }
}
