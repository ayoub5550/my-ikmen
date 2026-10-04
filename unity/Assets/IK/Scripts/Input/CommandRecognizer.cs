using System.Collections.Generic;
using UnityEngine;

namespace IK.Input {
    /// <summary>
    /// A deliberately small MUGEN command recogniser: enough to prove that the touch layer
    /// can actually perform QCF / QCB / DP / super / x+y / $F,x at 60 Hz with
    /// <c>command.time = 15</c>, before the real CMD buffer exists in dev.3.
    /// Syntax understood (subset of kfm.cmd):
    ///   <c>~D, DF, F, x</c>   — release/press directions then a button
    ///   <c>$F, x</c>          — direction in any 4-way "4-way detection" sense (treated as F)
    ///   <c>x+y</c>            — simultaneous buttons
    /// Directions are matched against the *facing-independent* L/R here (B = L, F = R),
    /// which is what the input test scene shows; the fight engine will supply facing later.
    /// </summary>
    public class CommandRecognizer {
        public class Command {
            public string name;
            public string definition;
            public List<Step> steps = new List<Step>();
            public int time = 15;
        }

        public struct Step {
            public string token;      // D, DF, F, B, DB, U, UF, UB, N or buttons like "x" / "x+y"
            public bool release;      // '~' prefix: the element must be released
            public bool isButton;
        }

        readonly List<Command> commands = new List<Command>();
        readonly List<InputFrame> buffer = new List<InputFrame>();
        readonly List<string> recognised = new List<string>();
        public int BufferSize = 60;

        public IReadOnlyList<string> Recognised => recognised;
        public IReadOnlyList<Command> Commands => commands;

        public Command Add(string name, string definition, int time = 15) {
            var c = new Command { name = name, definition = definition, time = time };
            foreach (var raw in definition.Split(',')) {
                string t = raw.Trim();
                if (t.Length == 0) continue;
                var step = new Step();
                if (t[0] == '~') { step.release = true; t = t.Substring(1); }
                if (t[0] == '$' || t[0] == '/') t = t.Substring(1);
                step.token = t;
                step.isButton = IsButtonToken(t);
                c.steps.Add(step);
            }
            commands.Add(c);
            return c;
        }

        static bool IsButtonToken(string t) {
            foreach (var part in t.Split('+')) {
                if (part.Length != 1 || "abcxyzsdwm".IndexOf(part[0]) < 0) return false;
            }
            return true;
        }

        /// <summary>The default KFM set from assets/screenpack/chars/kfm/kfm.cmd.</summary>
        public static CommandRecognizer Kfm() {
            var r = new CommandRecognizer();
            r.Add("QCF+x (Kouken)", "~D, DF, F, x");
            r.Add("QCB+y (Kouken back)", "~D, DB, B, y");
            r.Add("DP+x (Shoryuken)", "~F, D, DF, x");
            r.Add("Super (QCFx2+x)", "~D, DF, F, D, DF, F, x");
            r.Add("x+y", "x+y");
            r.Add("$F, x", "$F, x");
            return r;
        }

        public void Push(InputFrame f) {
            buffer.Add(f);
            if (buffer.Count > BufferSize) buffer.RemoveAt(0);
            // Longest match wins, like a CMD file where the super is tested before the
            // special that is a prefix of it (otherwise QCF would always eat QCFx2).
            Command best = null;
            foreach (var c in commands) {
                if (!Matches(c)) continue;
                if (best == null || c.steps.Count > best.steps.Count) best = c;
            }
            if (best != null) {
                recognised.Add(best.name);
                if (recognised.Count > 20) recognised.RemoveAt(0);
                buffer.Clear();           // consume, like a command buffer flush
            }
        }

        public void Clear() { buffer.Clear(); recognised.Clear(); }

        /// <summary>True when the command's steps appear in order inside the last <c>time</c> ticks.</summary>
        public bool Matches(Command c) {
            int window = Mathf.Min(c.time + 1, buffer.Count);
            if (window <= 0 || c.steps.Count == 0) return false;
            int start = buffer.Count - window;
            int step = 0;
            for (int i = start; i < buffer.Count; i++) {
                if (step >= c.steps.Count) break;
                if (StepMatches(c.steps[step], buffer[i])) step++;
            }
            // the last step must land on the final frame (the button that triggers the move)
            return step >= c.steps.Count && StepMatches(c.steps[c.steps.Count - 1], buffer[buffer.Count - 1]);
        }

        static bool StepMatches(Step s, InputFrame f) {
            if (s.isButton) {
                bool all = true;
                foreach (var part in s.token.Split('+')) all &= Button(f, part[0]);
                return s.release ? !all : all;
            }
            bool dir = Direction(f, s.token);
            return s.release ? !dir : dir;
        }

        static bool Button(InputFrame f, char b) {
            switch (b) {
                case 'a': return f.a; case 'b': return f.b; case 'c': return f.c;
                case 'x': return f.x; case 'y': return f.y; case 'z': return f.z;
                case 's': return f.s; case 'd': return f.d; case 'w': return f.w;
                case 'm': return f.m;
            }
            return false;
        }

        static bool Direction(InputFrame f, string token) {
            switch (token) {
                case "U": return f.U && !f.L && !f.R;
                case "D": return f.D && !f.L && !f.R;
                case "B": return f.L && !f.U && !f.D;
                case "F": return f.R && !f.U && !f.D;
                case "DB": return f.D && f.L;
                case "DF": return f.D && f.R;
                case "UB": return f.U && f.L;
                case "UF": return f.U && f.R;
                case "N": return !f.U && !f.D && !f.L && !f.R;
            }
            return false;
        }
    }
}
