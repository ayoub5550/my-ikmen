using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>
    /// The 14 logical inputs of a MUGEN player as a bit field. This is what the touch /
    /// gamepad layer of dev.1 produces; B and F are already facing-relative.
    /// </summary>
    [Flags]
    public enum CmdKey {
        None = 0,
        U = 1 << 0, D = 1 << 1, B = 1 << 2, F = 1 << 3,
        a = 1 << 4, b = 1 << 5, c = 1 << 6,
        x = 1 << 7, y = 1 << 8, z = 1 << 9,
        s = 1 << 10, d = 1 << 11, w = 1 << 12, m = 1 << 13
    }

    /// <summary>
    /// A single key of a command step. Mirrors `CommandKey` of
    /// `engine/ikemen-go/src/input.go`: the four plain directions, the six diagonals, the
    /// two absolute directions L/R, neutral N and the ten buttons.
    /// </summary>
    public enum CK {
        U, D, B, F, L, R, UB, UF, DB, DF, UL, UR, DL, DR, N,
        a, b, c, x, y, z, s, d, w, m
    }

    /// <summary>One key inside a command step, with its MUGEN prefix symbols.</summary>
    public struct CommandStepKey : IEquatable<CommandStepKey> {
        public CK Key;
        public bool Slash;        // '/'  key must be held
        public bool Tilde;        // '~'  key release
        public bool Dollar;       // '$'  direction matched loosely (4-way)
        public int ChargeTime;    // '~30B' / '/30B'

        public bool IsDirection => Key >= CK.U && Key <= CK.N;
        public bool IsDirectionPress => !Tilde && IsDirection;
        public bool IsDirectionRelease => Tilde && IsDirection;
        public bool IsButtonPress => !Tilde && Key >= CK.a;
        public bool IsButtonRelease => Tilde && Key >= CK.a;

        public bool Equals(CommandStepKey o) =>
            Key == o.Key && Slash == o.Slash && Tilde == o.Tilde && Dollar == o.Dollar && ChargeTime == o.ChargeTime;
        public override bool Equals(object o) => o is CommandStepKey k && Equals(k);
        public override int GetHashCode() =>
            (int)Key | (Slash ? 1 << 8 : 0) | (Tilde ? 1 << 9 : 0) | (Dollar ? 1 << 10 : 0) | (ChargeTime << 11);

        public override string ToString() =>
            (Tilde ? "~" : "") + (Slash ? "/" : "") + (ChargeTime > 0 ? ChargeTime.ToString() : "") +
            (Dollar ? "$" : "") + Key;
    }

    /// <summary>
    /// One comma-separated step of a command. Keys inside it are simultaneous (`+`) or
    /// alternatives (`|`); `&gt;` forbids any other key change before the step.
    /// </summary>
    public class CommandStep {
        public readonly List<CommandStepKey> Keys = new List<CommandStepKey>();
        public bool Greater;
        public bool OrLogic;

        /// <summary>A step that is exactly one direction (press or release).</summary>
        public bool IsSingleDirection =>
            Keys.Count == 1 && (Keys[0].IsDirectionPress || Keys[0].IsDirectionRelease);

        /// <summary>Ikemen `CommandStep.EqualSteps` — used by the `&gt;` expansion.</summary>
        public bool EqualSteps(CommandStep n) {
            if (n == null || Greater != n.Greater || Keys.Count != n.Keys.Count) return false;
            for (int i = 0; i < Keys.Count; i++) if (!Keys[i].Equals(n.Keys[i])) return false;
            return true;
        }

        /// <summary>
        /// Ikemen `CommandStep.IsDirToButton`: may this step and the next one complete on the
        /// same tick? True for "direction then button press", which is what makes
        /// `~D, DF, F, x` come out when the player hits F and x together.
        /// </summary>
        public bool IsDirToButton(CommandStep next) {
            if (next == null) return false;
            foreach (var k in next.Keys) if (k.Slash) return false;
            foreach (var k in Keys) if (k.IsButtonPress || k.IsButtonRelease) return false;
            foreach (var k in Keys) foreach (var n in next.Keys) if (k.Key == n.Key) return false;
            foreach (var n in next.Keys) if (n.IsButtonPress) return true;
            foreach (var k in Keys) {
                if (!k.IsDirectionRelease) continue;
                foreach (var n in next.Keys) if (!n.IsDirectionRelease) return true;
            }
            return false;
        }

        public CommandStep Clone() {
            var s = new CommandStep { Greater = Greater, OrLogic = OrLogic };
            s.Keys.AddRange(Keys);
            return s;
        }
    }

    /// <summary>
    /// One `[Command]` of a .cmd file plus its matching state. The matching model is
    /// Ikemen's: every step has its own `completed` flag and timer, the whole command has a
    /// time window (`time`) and, once finished, stays readable for `buffer.time` ticks —
    /// which is what `command = "x"` sees in a trigger.
    /// </summary>
    public class MugenCommand {
        public string Name = "";
        public string Source = "";
        public readonly List<CommandStep> Steps = new List<CommandStep>();
        public int MaxTime = 15;        // `time`
        public int MaxBufTime = 1;      // `buffer.time`
        public int MaxStepTime = -1;    // `steptime`, -1 = same as time
        public bool AutoGreater = true;

        internal bool[] completed = new bool[0];
        internal int[] stepTimers = new int[0];
        internal readonly List<int> loopOrder = new List<int>();
        internal int curTime, curBufTime;
        internal bool completeFrame;

        /// <summary>True while the command is buffered (what triggers read).</summary>
        public bool IsActive => curBufTime > 0;
        /// <summary>True only on the tick the last step completed.</summary>
        public bool CompletedThisTick => completeFrame;
        /// <summary>Per-step completion flags, for tests and the training HUD.</summary>
        public bool[] CompletedFlags => completed;
        public int StepsCompleted {
            get { int n = 0; foreach (var c in completed) if (c) n++; return n; }
        }

        public void Clear(bool bufReset) {
            curTime = 0;
            if (bufReset) curBufTime = 0;
            for (int i = 0; i < completed.Length; i++) completed[i] = false;
            for (int i = 0; i < stepTimers.Length; i++) stepTimers[i] = 0;
        }

        /// <summary>Rebuilds the per-step trackers and the evaluation order.</summary>
        public void Prepare() {
            if (AutoGreater) AutoGreaterExpand();
            completed = new bool[Steps.Count];
            stepTimers = new int[Steps.Count];
            loopOrder.Clear();
            // Reverse order stops one input from completing two steps at once; a
            // "direction then button" run is walked forwards so it can.
            for (int i = Steps.Count - 1; i >= 0;) {
                if (i > 0 && Steps[i - 1].IsDirToButton(Steps[i])) {
                    int start = i - 1, end = i;
                    while (start > 0 && Steps[start - 1].IsDirToButton(Steps[start])) start--;
                    for (int j = start; j <= end; j++) loopOrder.Add(j);
                    i = start - 1;
                } else {
                    loopOrder.Add(i);
                    i--;
                }
            }
        }

        /// <summary>`F, F` becomes `F, &gt;~F, &gt;F` (Ikemen `AutoGreaterExpand`).</summary>
        void AutoGreaterExpand() {
            if (Steps.Count < 2) return;
            bool need = false;
            for (int i = 1; i < Steps.Count; i++)
                if (Steps[i - 1].IsSingleDirection && Steps[i].IsSingleDirection && Steps[i - 1].EqualSteps(Steps[i])) {
                    need = true;
                    break;
                }
            if (!need) return;

            var expanded = new List<CommandStep> { Steps[0] };
            for (int i = 1; i < Steps.Count; i++) {
                var prev = Steps[i - 1];
                var cur = Steps[i];
                if (prev.IsSingleDirection && cur.IsSingleDirection && prev.EqualSteps(cur)) {
                    var release = new CommandStep { Greater = true };
                    var k = cur.Keys[0];
                    release.Keys.Add(new CommandStepKey { Key = k.Key, Tilde = !k.Tilde, Dollar = k.Dollar });
                    expanded.Add(release);
                    var again = cur.Clone();
                    again.Greater = true;
                    expanded.Add(again);
                } else expanded.Add(cur);
            }
            Steps.Clear();
            Steps.AddRange(expanded);
        }

        public override string ToString() => Name + " [" + Source + "]";
    }

    /// <summary>
    /// How long ago each key was pressed or released, in ticks: a positive value is "held
    /// for n ticks" (1 = pressed this tick), a negative value is "released n ticks ago"
    /// (-1 = released this tick). Port of Ikemen's `InputBuffer` (`input.go`), including the
    /// conflicting-direction rules that make `D` false while `DF` is held and `$D` true.
    /// </summary>
    public class InputBuffer {
        public int Ub = -1, Db = -1, Bb = -1, Fb = -1, Lb = -1, Rb = -1, Nb = -1;
        public int ab = -1, bb = -1, cb = -1, xb = -1, yb = -1, zb = -1, sb = -1, db = -1, wb = -1, mb = -1;
        public int Up = -1, Dp = -1, Bp = -1, Fp = -1, Lp = -1, Rp = -1, Np = -1;
        public int ap = -1, bp = -1, cp = -1, xp = -1, yp = -1, zp = -1, sp = -1, dp = -1, wp = -1, mp = -1;

        public void Reset() {
            Ub = Db = Bb = Fb = Lb = Rb = Nb = -1;
            ab = bb = cb = xb = yb = zb = sb = db = wb = mb = -1;
            Up = Dp = Bp = Fp = Lp = Rp = Np = -1;
            ap = bp = cp = xp = yp = zp = sp = dp = wp = mp = -1;
        }

        static void Update(bool held, ref int buffer) {
            if (held != (buffer > 0)) { buffer = held ? 1 : -1; return; }
            buffer += held ? 1 : -1;
        }

        /// <summary>One tick of input. B/F are facing-relative, L/R absolute.</summary>
        public void Step(CmdKey keys, int facing = 1) {
            bool U = (keys & CmdKey.U) != 0, D = (keys & CmdKey.D) != 0;
            bool B = (keys & CmdKey.B) != 0, F = (keys & CmdKey.F) != 0;
            bool L = facing >= 0 ? B : F;
            bool R = facing >= 0 ? F : B;
            Step(U, D, L, R, B, F,
                 (keys & CmdKey.a) != 0, (keys & CmdKey.b) != 0, (keys & CmdKey.c) != 0,
                 (keys & CmdKey.x) != 0, (keys & CmdKey.y) != 0, (keys & CmdKey.z) != 0,
                 (keys & CmdKey.s) != 0, (keys & CmdKey.d) != 0, (keys & CmdKey.w) != 0,
                 (keys & CmdKey.m) != 0);
        }

        public void Step(bool U, bool D, bool L, bool R, bool B, bool F,
                         bool a, bool b, bool c, bool x, bool y, bool z,
                         bool s, bool d, bool w, bool m) {
            Up = Ub; Dp = Db; Lp = Lb; Rp = Rb; Bp = Bb; Fp = Fb; Np = Nb;
            ap = ab; bp = bb; cp = cb; xp = xb; yp = yb; zp = zb; sp = sb; dp = db; wp = wb; mp = mb;

            Update(U, ref Ub); Update(D, ref Db); Update(L, ref Lb); Update(R, ref Rb);
            Update(B, ref Bb); Update(F, ref Fb);
            Update(!(U || D || L || R || B || F), ref Nb);

            Update(a, ref ab); Update(b, ref bb); Update(c, ref cb);
            Update(x, ref xb); Update(y, ref yb); Update(z, ref zb);
            Update(s, ref sb); Update(d, ref db); Update(w, ref wb); Update(m, ref mb);
        }

        static int Min(int a, int b) => a < b ? a : b;
        static int Min(int a, int b, int c) => Min(Min(a, b), c);
        static int Min(int a, int b, int c, int d) => Min(Min(a, b), Min(c, d));
        static int Max(int a, int b) => a > b ? a : b;
        static int Max(int a, int b, int c) => Max(Max(a, b), c);
        static int Abs(int a) => a < 0 ? -a : a;

        int ButtonBuf(CK k) {
            switch (k) {
                case CK.a: return ab; case CK.b: return bb; case CK.c: return cb;
                case CK.x: return xb; case CK.y: return yb; case CK.z: return zb;
                case CK.s: return sb; case CK.d: return db; case CK.w: return wb; case CK.m: return mb;
            }
            return 0;
        }
        int ButtonPrev(CK k) {
            switch (k) {
                case CK.a: return ap; case CK.b: return bp; case CK.c: return cp;
                case CK.x: return xp; case CK.y: return yp; case CK.z: return zp;
                case CK.s: return sp; case CK.d: return dp; case CK.w: return wp; case CK.m: return mp;
            }
            return 0;
        }

        /// <summary>
        /// Ikemen `InputBuffer.State`: how many ticks ago the symbol became true, 1 meaning
        /// "this tick". 0 or negative means it is not true now.
        /// </summary>
        public int State(CommandStepKey ck) {
            // held, strict directions
            if (!ck.Tilde && !ck.Dollar) {
                switch (ck.Key) {
                    case CK.U: return Min(-Max(Bb, Db, Fb), Ub);
                    case CK.D: return Min(-Max(Bb, Ub, Fb), Db);
                    case CK.B: return Min(-Max(Db, Ub, Fb), Bb);
                    case CK.F: return Min(-Max(Db, Ub, Bb), Fb);
                    case CK.L: return Min(-Max(Db, Ub, Rb), Lb);
                    case CK.R: return Min(-Max(Db, Ub, Lb), Rb);
                    case CK.UF: return Min(-Max(Db, Bb), Min(Ub, Fb));
                    case CK.UB: return Min(-Max(Db, Fb), Min(Ub, Bb));
                    case CK.DF: return Min(-Max(Ub, Bb), Min(Db, Fb));
                    case CK.DB: return Min(-Max(Ub, Fb), Min(Db, Bb));
                    case CK.UL: return Min(-Max(Db, Rb), Min(Ub, Lb));
                    case CK.UR: return Min(-Max(Db, Lb), Min(Ub, Rb));
                    case CK.DL: return Min(-Max(Ub, Rb), Min(Db, Lb));
                    case CK.DR: return Min(-Max(Ub, Lb), Min(Db, Rb));
                    case CK.N: return Nb;
                }
            }
            // held, loose ($) directions — MUGEN ignores conflicting directions here
            if (!ck.Tilde && ck.Dollar) {
                int any = Min(Abs(Ub), Abs(Db), Abs(Bb), Abs(Fb));
                int anyLR = Min(Abs(Ub), Abs(Db), Abs(Lb), Abs(Rb));
                switch (ck.Key) {
                    case CK.U: if (Ub > 0) return any; break;
                    case CK.D: if (Db > 0) return any; break;
                    case CK.B: if (Bb > 0) return any; break;
                    case CK.F: if (Fb > 0) return any; break;
                    case CK.L: if (Lb > 0) return anyLR; break;
                    case CK.R: if (Rb > 0) return anyLR; break;
                    case CK.UB: if (Ub > 0 && Bb > 0) return any; break;
                    case CK.UF: if (Ub > 0 && Fb > 0) return any; break;
                    case CK.DB: if (Db > 0 && Bb > 0) return any; break;
                    case CK.DF: if (Db > 0 && Fb > 0) return any; break;
                    case CK.UL: if (Ub > 0 && Lb > 0) return anyLR; break;
                    case CK.UR: if (Ub > 0 && Rb > 0) return anyLR; break;
                    case CK.DL: if (Db > 0 && Lb > 0) return anyLR; break;
                    case CK.DR: if (Db > 0 && Rb > 0) return anyLR; break;
                }
            }
            // released directions
            if (ck.Tilde && !ck.Dollar) {
                switch (ck.Key) {
                    case CK.U: if (Ub < 0 || Up > 0) return -Min(-Max(Bb, Db, Fb), Ub); break;
                    case CK.D: if (Db < 0 || Dp > 0) return -Min(-Max(Bb, Ub, Fb), Db); break;
                    case CK.B: if (Bb < 0 || Bp > 0) return -Min(-Max(Db, Ub, Fb), Bb); break;
                    case CK.F: if (Fb < 0 || Fp > 0) return -Min(-Max(Db, Ub, Bb), Fb); break;
                    case CK.L: if (Lb < 0 || Lp > 0) return -Min(-Max(Db, Ub, Rb), Lb); break;
                    case CK.R: if (Rb < 0 || Rp > 0) return -Min(-Max(Db, Ub, Lb), Rb); break;
                    case CK.UF: if ((Ub < 0 || Up > 0) && (Fb < 0 || Fp > 0)) return -Min(-Max(Db, Bb), Min(Ub, Fb)); break;
                    case CK.UB: if ((Ub < 0 || Up > 0) && (Bb < 0 || Bp > 0)) return -Min(-Max(Db, Fb), Min(Ub, Bb)); break;
                    case CK.DF: if ((Db < 0 || Dp > 0) && (Fb < 0 || Fp > 0)) return -Min(-Max(Ub, Bb), Min(Db, Fb)); break;
                    case CK.DB: if ((Db < 0 || Dp > 0) && (Bb < 0 || Bp > 0)) return -Min(-Max(Ub, Fb), Min(Db, Bb)); break;
                    case CK.UL: if ((Ub < 0 || Up > 0) && (Lb < 0 || Lp > 0)) return -Min(-Max(Db, Rb), Min(Ub, Lb)); break;
                    case CK.UR: if ((Ub < 0 || Up > 0) && (Rb < 0 || Rp > 0)) return -Min(-Max(Db, Lb), Min(Ub, Rb)); break;
                    case CK.DL: if ((Db < 0 || Dp > 0) && (Lb < 0 || Lp > 0)) return -Min(-Max(Ub, Rb), Min(Db, Lb)); break;
                    case CK.DR: if ((Db < 0 || Dp > 0) && (Rb < 0 || Rp > 0)) return -Min(-Max(Ub, Lb), Min(Db, Rb)); break;
                    case CK.N: return -Nb;
                }
            }
            // released, loose ($) directions
            if (ck.Tilde && ck.Dollar) {
                switch (ck.Key) {
                    case CK.U: if (Ub < 0 || Up > 0) return -Ub; break;
                    case CK.D: if (Db < 0 || Dp > 0) return -Db; break;
                    case CK.B: if (Bb < 0 || Bp > 0) return -Bb; break;
                    case CK.F: if (Fb < 0 || Fp > 0) return -Fb; break;
                    case CK.L: if (Lb < 0 || Lp > 0) return -Lb; break;
                    case CK.R: if (Rb < 0 || Rp > 0) return -Rb; break;
                }
            }
            // buttons
            if (ck.Key >= CK.a) {
                if (!ck.Tilde) return ButtonBuf(ck.Key);
                if (ButtonBuf(ck.Key) < 0 || ButtonPrev(ck.Key) > 0) return -ButtonBuf(ck.Key);
                return 0;
            }
            return 0;
        }

        /// <summary>Ikemen `InputBuffer.StateCharge`: how long the key was charged.</summary>
        public int StateCharge(CommandStepKey ck) {
            Func<int, int> ignoreRecent = buf => buf == 1 ? int.MinValue : buf;
            if (!ck.Tilde && ck.Dollar) {
                switch (ck.Key) {
                    case CK.U: return Ub; case CK.D: return Db; case CK.B: return Bb;
                    case CK.F: return Fb; case CK.L: return Lb; case CK.R: return Rb;
                }
            }
            if (ck.Tilde && ck.Dollar) {
                switch (ck.Key) {
                    case CK.U: return Up; case CK.D: return Dp; case CK.B: return Bp;
                    case CK.F: return Fp; case CK.L: return Lp; case CK.R: return Rp;
                }
            }
            if (!ck.Tilde && !ck.Dollar) {
                switch (ck.Key) {
                    case CK.U: return Max(0, Min(-Max(Db, Bb, Fb), Ub));
                    case CK.D: return Max(0, Min(-Max(Ub, Bb, Fb), Db));
                    case CK.B: return Max(0, Min(-Max(Ub, Db, Fb), Bb));
                    case CK.F: return Max(0, Min(-Max(Ub, Db, Bb), Fb));
                    case CK.L: return Max(0, Min(-Max(Ub, Db, Rb), Lb));
                    case CK.R: return Max(0, Min(-Max(Ub, Db, Lb), Rb));
                    case CK.UF: return Max(0, Min(-Max(Db, Bb), Min(Ub, Fb)));
                    case CK.UB: return Max(0, Min(-Max(Db, Fb), Min(Ub, Bb)));
                    case CK.DF: return Max(0, Min(-Max(Ub, Bb), Min(Db, Fb)));
                    case CK.DB: return Max(0, Min(-Max(Ub, Fb), Min(Db, Bb)));
                }
            }
            if (ck.Tilde && !ck.Dollar) {
                switch (ck.Key) {
                    case CK.U: return Max(0, -ignoreRecent(Min(-Max(Db, Bb, Fb), Ub)));
                    case CK.D: return Max(0, -ignoreRecent(Min(-Max(Ub, Bb, Fb), Db)));
                    case CK.B: return Max(0, -ignoreRecent(Min(-Max(Ub, Db, Fb), Bb)));
                    case CK.F: return Max(0, -ignoreRecent(Min(-Max(Ub, Db, Bb), Fb)));
                    case CK.L: return Max(0, -ignoreRecent(Min(-Max(Ub, Db, Rb), Lb)));
                    case CK.R: return Max(0, -ignoreRecent(Min(-Max(Ub, Db, Lb), Rb)));
                }
            }
            if (ck.Key >= CK.a) {
                int buf = ButtonBuf(ck.Key);
                return ck.Tilde ? Max(0, -buf) : Max(0, buf);
            }
            return 0;
        }
    }

    /// <summary>
    /// Reads a .cmd file: `[Defaults]`, every `[Command]` block, and the `[Statedef -1]`
    /// state controllers (parsed by <see cref="CnsFile"/>, which reads the same syntax).
    /// </summary>
    public class CmdFile {
        public readonly List<MugenCommand> Commands = new List<MugenCommand>();
        public CnsFile States;
        public int DefaultTime = 15, DefaultBufferTime = 1, DefaultStepTime = -1;

        public static CmdFile Parse(byte[] bytes) => Parse(MugenDef.DecodeText(bytes));

        public static CmdFile Parse(string text) {
            var cmd = new CmdFile { States = CnsFile.Parse(text) };
            string section = null;
            MugenCommand current = null;
            var pending = new List<MugenCommand>();

            foreach (var raw in MugenDef.SplitLines(text)) {
                var line = MugenDef.StripComment(raw).Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[') {
                    int close = line.IndexOf(']');
                    section = (close > 0 ? line.Substring(1, close - 1) : line.Substring(1)).Trim().ToLowerInvariant();
                    if (section == "command") {
                        current = new MugenCommand();
                        cmd.Commands.Add(current);
                        pending.Add(current);
                    } else current = null;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                if (section == "defaults") {
                    if (key == "command.time") cmd.DefaultTime = Math.Max(1, MugenDef.Atoi(value));
                    else if (key == "command.buffer.time") cmd.DefaultBufferTime = Math.Max(1, MugenDef.Atoi(value));
                    else if (key == "command.steptime") cmd.DefaultStepTime = MugenDef.Atoi(value);
                    continue;
                }
                if (current == null) continue;
                switch (key) {
                    case "name": current.Name = MugenDef.Unquote(value); break;
                    case "command": current.Source = value; ReadCommandSymbols(current, value); break;
                    case "time": current.MaxTime = Math.Max(1, MugenDef.Atoi(value)); break;
                    case "buffer.time": current.MaxBufTime = Math.Max(1, MugenDef.Atoi(value)); break;
                    case "steptime": current.MaxStepTime = MugenDef.Atoi(value); break;
                }
            }

            // [Defaults] may appear after the commands: fill in what was not set explicitly.
            foreach (var c in pending) {
                if (c.MaxTime == 15 && cmd.DefaultTime != 15) c.MaxTime = cmd.DefaultTime;
                if (c.MaxBufTime == 1 && cmd.DefaultBufferTime != 1) c.MaxBufTime = cmd.DefaultBufferTime;
                if (c.MaxStepTime < 0) c.MaxStepTime = cmd.DefaultStepTime;
                ApplyBackwardCompatibility(c);
                c.Prepare();
            }
            return cmd;
        }

        public MugenCommand Get(string name) {
            foreach (var c in Commands)
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>Every command with that name (KFM defines e.g. `TripleKFPalm` twice).</summary>
        public List<MugenCommand> All(string name) {
            var list = new List<MugenCommand>();
            foreach (var c in Commands)
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) list.Add(c);
            return list;
        }

        /// <summary>
        /// Compiles `~30$D, DF, F, x+y` style text into steps — port of Ikemen's
        /// `Command.ReadCommandSymbols`. Unknown symbols are skipped, as MUGEN does.
        /// </summary>
        public static void ReadCommandSymbols(MugenCommand cmd, string text) {
            cmd.Steps.Clear();
            if (string.IsNullOrWhiteSpace(text)) return;

            foreach (var rawStep in text.Split(',')) {
                var stepText = rawStep.Trim();
                var step = new CommandStep();
                cmd.Steps.Add(step);
                string[] parts;
                if (stepText.IndexOf('|') >= 0) { step.OrLogic = true; parts = stepText.Split('|'); }
                else parts = stepText.Split('+');

                foreach (var rawPart in parts) {
                    var part = rawPart.Trim();
                    if (part.Length == 0) continue;
                    bool slash = false, tilde = false, dollar = false;
                    int charge = 0;
                    int i = 0;

                    // prefix symbols
                    bool scanning = true;
                    while (scanning && i < part.Length) {
                        switch (part[i]) {
                            case '>': step.Greater = true; i++; break;
                            case '~':
                                tilde = true; i++;
                                charge = Math.Max(charge, ReadNumber(part, ref i));
                                break;
                            case '/':
                                slash = true; i++;
                                charge = Math.Max(charge, ReadNumber(part, ref i));
                                break;
                            case '$': dollar = true; i++; break;
                            case ' ': i++; break;
                            default: scanning = false; break;
                        }
                    }
                    if (i >= part.Length) continue;

                    // the key itself (diagonals are one key)
                    char c0 = part[i];
                    CK key;
                    bool isDir = true;
                    switch (c0) {
                        case 'U': key = CK.U; break;
                        case 'D': key = CK.D; break;
                        case 'B': key = CK.B; break;
                        case 'F': key = CK.F; break;
                        case 'L': key = CK.L; break;
                        case 'R': key = CK.R; break;
                        case 'N': key = CK.N; break;
                        default: isDir = false; key = CK.N; break;
                    }
                    if (isDir) {
                        if ((c0 == 'U' || c0 == 'D') && i + 1 < part.Length) {
                            char c1 = part[i + 1];
                            if (c1 == 'B' || c1 == 'F' || c1 == 'L' || c1 == 'R') {
                                if (c0 == 'U') key = c1 == 'B' ? CK.UB : c1 == 'F' ? CK.UF : c1 == 'L' ? CK.UL : CK.UR;
                                else key = c1 == 'B' ? CK.DB : c1 == 'F' ? CK.DF : c1 == 'L' ? CK.DL : CK.DR;
                            }
                        }
                        step.Keys.Add(new CommandStepKey {
                            Key = key, Slash = slash, Tilde = tilde, Dollar = dollar, ChargeTime = charge
                        });
                        continue;
                    }
                    switch (c0) {
                        case 'a': key = CK.a; break; case 'b': key = CK.b; break; case 'c': key = CK.c; break;
                        case 'x': key = CK.x; break; case 'y': key = CK.y; break; case 'z': key = CK.z; break;
                        case 's': key = CK.s; break; case 'd': key = CK.d; break; case 'w': key = CK.w; break;
                        case 'm': key = CK.m; break;
                        default: continue;              // unknown symbol: skip it
                    }
                    step.Keys.Add(new CommandStepKey {
                        Key = key, Slash = slash, Tilde = tilde, Dollar = false, ChargeTime = charge
                    });
                }
            }
            // drop steps that produced no key at all (e.g. a trailing comma)
            cmd.Steps.RemoveAll(s => s.Keys.Count == 0);
        }

        static int ReadNumber(string s, ref int i) {
            int n = 0;
            bool any = false;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') { n = n * 10 + (s[i] - '0'); i++; any = true; }
            return any ? n : 0;
        }

        /// <summary>
        /// Ikemen `ApplyBackwardCompatibility`: `/x+y` means both keys held, and a command
        /// made only of held keys always has a buffer time of 1.
        /// </summary>
        public static void ApplyBackwardCompatibility(MugenCommand cmd) {
            foreach (var step in cmd.Steps) {
                if (step.OrLogic) continue;
                bool hasSlash = false;
                foreach (var k in step.Keys) if (k.Slash) { hasSlash = true; break; }
                if (!hasSlash) continue;
                for (int i = 0; i < step.Keys.Count; i++) {
                    if (step.Keys[i].Slash) continue;
                    var k = step.Keys[i];
                    k.Slash = true;
                    step.Keys[i] = k;
                }
            }
            bool holdOnly = cmd.Steps.Count > 0;
            foreach (var step in cmd.Steps) {
                foreach (var k in step.Keys) if (!k.Slash) { holdOnly = false; break; }
                if (!holdOnly) break;
            }
            if (holdOnly) cmd.MaxBufTime = 1;
        }
    }

    /// <summary>
    /// Matches a character's commands against the 60 Hz input stream — port of Ikemen's
    /// `Command.Step` and `CommandList`. Feed it one <see cref="CmdKey"/> per logic tick;
    /// <see cref="Active"/> is then what `command = "name"` reads in a trigger.
    /// </summary>
    public class CommandEngine {
        readonly List<MugenCommand> commands = new List<MugenCommand>();
        public readonly InputBuffer Buffer = new InputBuffer();
        public int Facing = 1;

        public IReadOnlyList<MugenCommand> Commands => commands;
        /// <summary>Commands that completed on the last tick, for the training HUD.</summary>
        public readonly List<string> JustCompleted = new List<string>();

        public CommandEngine(CmdFile file) {
            if (file != null) commands.AddRange(file.Commands);
        }

        public void Reset() {
            foreach (var c in commands) c.Clear(true);
            Buffer.Reset();
            JustCompleted.Clear();
        }

        public bool Active(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var c in commands)
                if (c.IsActive && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Feeds one tick of input (B/F already facing-relative).</summary>
        public void Step(CmdKey keys) {
            Buffer.Step(keys, Facing);
            JustCompleted.Clear();
            foreach (var c in commands) {
                StepCommand(c);
                if (c.completeFrame) JustCompleted.Add(c.Name);
            }
        }

        void StepCommand(MugenCommand c) {
            if (c.curBufTime > 0) c.curBufTime--;
            if (c.Steps.Count == 0) return;

            int maxStep = c.MaxStepTime > 0 ? c.MaxStepTime : 0;
            bool anyDone = false;
            for (int i = 0; i < c.Steps.Count; i++) {
                if (!c.completed[i]) continue;
                c.stepTimers[i]++;
                if (maxStep > 0 && c.stepTimers[i] > maxStep) {
                    c.completed[i] = false;
                    c.stepTimers[i] = 0;
                    continue;
                }
                anyDone = true;
            }
            if (anyDone) c.curTime++;
            else if (c.curTime > 0) c.Clear(false);

            foreach (var i in c.loopOrder) {
                if (i > 0 && !c.completed[i - 1]) continue;
                var step = c.Steps[i];
                bool matched;
                if (step.OrLogic) {
                    matched = false;
                    foreach (var k in step.Keys) {
                        if (KeyOk(k)) { matched = true; break; }
                    }
                } else {
                    matched = true;
                    foreach (var k in step.Keys) {
                        if (!KeyOk(k)) { matched = false; break; }
                    }
                }

                if (step.Greater && i > 0 && c.Steps.Count >= 2 && c.completed[i - 1] && !c.completed[i]) {
                    if (GreaterCheckFail(c, i)) {
                        matched = false;
                        c.completed[i - 1] = false;
                        c.stepTimers[i - 1] = 0;
                    }
                }

                if (!matched) continue;
                c.completed[i] = true;
                c.stepTimers[i] = 0;
                if (i > 0) { c.completed[i - 1] = false; c.stepTimers[i - 1] = 0; }
                if (i == 0) c.curTime = 0;
            }

            c.completeFrame = c.completed.Length > 0 && c.completed[c.completed.Length - 1];
            if (!c.completeFrame && c.curTime < c.MaxTime) return;

            c.Clear(false);
            if (c.completeFrame) c.curBufTime = Math.Max(c.curBufTime, c.MaxBufTime);
        }

        bool KeyOk(CommandStepKey k) {
            int t = Buffer.State(k);
            bool ok = k.Slash ? t > 0 : t == 1;
            if (ok && k.ChargeTime > 1 && Buffer.StateCharge(k) < k.ChargeTime) ok = false;
            return ok;
        }

        static readonly CK[] GreaterButtons = { CK.a, CK.b, CK.c, CK.x, CK.y, CK.z, CK.s, CK.d, CK.w, CK.m };
        static readonly CK[] GreaterBF = { CK.B, CK.F, CK.UF, CK.UB, CK.DF, CK.DB };
        static readonly CK[] GreaterLR = { CK.L, CK.R, CK.UL, CK.UR, CK.DL, CK.DR };

        /// <summary>Ikemen `GreaterCheckFail`: did any key change that the step does not allow?</summary>
        bool GreaterCheckFail(MugenCommand c, int i) {
            bool useLR = false;
            foreach (var sk in c.Steps[i].Keys)
                if (sk.Key == CK.L || sk.Key == CK.R || sk.Key == CK.UL || sk.Key == CK.UR ||
                    sk.Key == CK.DL || sk.Key == CK.DR) { useLR = true; break; }

            Func<CK, bool> checkKey = k => {
                if (Buffer.State(new CommandStepKey { Key = k, Tilde = false }) == 1) {
                    foreach (var sk in c.Steps[i].Keys) if (sk.Key == k && !sk.Tilde) return false;
                    return true;
                }
                if (Buffer.State(new CommandStepKey { Key = k, Tilde = true }) == 1) {
                    foreach (var sk in c.Steps[i].Keys) if (sk.Key == k && sk.Tilde) return false;
                    return true;
                }
                return false;
            };

            if (checkKey(CK.U) || checkKey(CK.D)) return true;
            foreach (var k in useLR ? GreaterLR : GreaterBF) if (checkKey(k)) return true;
            foreach (var k in GreaterButtons) if (checkKey(k)) return true;
            return false;
        }
    }
}
