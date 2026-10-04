using System;
using System.Collections.Generic;

namespace IK.Core {
    public enum TransType { Default, None, Add, Add1, Sub, SubAdd }

    /// <summary>One element of an action: a sprite reference plus its timing and flags.</summary>
    public class AnimFrame {
        public int Time = -1;
        public int Group = -1, Number;
        public int Xoffset, Yoffset;
        public TransType Trans = TransType.None;
        public byte SrcAlpha = 255, DstAlpha = 0;
        public int Hscale = 1, Vscale = 1;     // -1 when the H / V flip flag is set
        public float Xscale = 1f, Yscale = 1f, Angle;
        public List<float[]> Clsn1 = new List<float[]>();   // attack boxes  (l,t,r,b)
        public List<float[]> Clsn2 = new List<float[]>();   // hurt boxes
    }

    /// <summary>
    /// A MUGEN action (`[Begin Action n]` in a .air file) and its playback state.
    /// Ported from Ikemen GO `engine/ikemen-go/src/anim.go` (ReadAnimFrame, ReadAnimation,
    /// Animation.Action). Frame timing is in 60 Hz ticks and `Time = -1` means "stay here".
    /// </summary>
    public class MugenAnimation {
        public int No;
        public readonly List<AnimFrame> Frames = new List<AnimFrame>();
        public int LoopStart;
        public int TotalTime, LoopTime, PreLoopTime;
        public int CopyAction = -1;

        // playback state
        public int CurrentElement;      // 0-based
        public int ElementTime;
        public int Time;
        public bool LoopEnd;

        public AnimFrame CurrentFrame =>
            Frames.Count == 0 ? null : Frames[Math.Min(Math.Max(CurrentElement, 0), Frames.Count - 1)];

        public void Reset() {
            CurrentElement = 0; ElementTime = 0; Time = 0; LoopEnd = false;
        }

        /// <summary>Advances the action by one 60 Hz tick (Ikemen `Animation.Action`).</summary>
        public void Tick() {
            if (Frames.Count == 0) { LoopEnd = true; return; }
            if (CurrentFrame.Time <= 0) Next();
            if (CurrentElement < Frames.Count) {
                ElementTime++;
                if (ElementTime >= CurrentFrame.Time) {
                    Next();
                    if (CurrentElement >= Frames.Count) CurrentElement = LoopStart;
                }
            } else {
                CurrentElement = LoopStart;
            }
            if (TotalTime != -1 && Time >= TotalTime) Time = TotalTime - LoopTime;
            Time++;
            if (TotalTime != -1 && Time >= TotalTime) LoopEnd = true;
        }

        void Next() {
            if (TotalTime != -1 || CurrentElement < Frames.Count - 1) {
                ElementTime = 0;
                while (true) {
                    CurrentElement++;
                    if ((TotalTime == -1 && CurrentElement == Frames.Count - 1) ||
                        CurrentElement >= Frames.Count || CurrentFrame.Time > 0) break;
                }
            }
        }

        /// <summary>Total playing time in ticks; an infinite frame counts as 1 (Ikemen `GetLength`).</summary>
        public int Length {
            get {
                int len = 0;
                foreach (var f in Frames) len += f.Time == -1 ? 1 : Math.Max(0, f.Time);
                return len;
            }
        }
    }

    /// <summary>Every action of a .air file, keyed by action number.</summary>
    public class AirFile {
        public readonly Dictionary<int, MugenAnimation> Actions = new Dictionary<int, MugenAnimation>();
        public readonly List<int> Order = new List<int>();

        public MugenAnimation Get(int no) => Actions.TryGetValue(no, out var a) ? a : null;

        public static AirFile Parse(byte[] bytes) => Parse(MugenDef.DecodeText(bytes));

        public static AirFile Parse(string text) {
            var air = new AirFile();
            var lines = MugenDef.SplitLines(text);
            int i = 0;
            while (i < lines.Length) {
                // find "[Begin Action n]"
                string line = MugenDef.StripComment(lines[i]).Trim();
                if (line.Length < 2 || line[0] != '[') { i++; continue; }
                int close = line.IndexOf(']');
                string inner = (close > 0 ? line.Substring(1, close - 1) : line.Substring(1)).Trim();
                var low = inner.ToLowerInvariant();
                if (!low.StartsWith("begin action")) { i++; continue; }
                int no = MugenDef.Atoi(inner.Substring("begin action".Length).Trim());
                i++;
                var anim = ReadAnimation(lines, ref i);
                anim.No = no;
                if (!air.Actions.ContainsKey(no)) {
                    air.Actions[no] = anim;
                    air.Order.Add(no);
                }
            }
            air.ResolveCopyActions();
            return air;
        }

        void ResolveCopyActions() {
            foreach (var no in Order) {
                var a = Actions[no];
                if (a.CopyAction < 0) continue;
                if (Actions.TryGetValue(a.CopyAction, out var src) && src != a && src.CopyAction < 0) {
                    a.Frames.Clear();
                    a.Frames.AddRange(src.Frames);
                    a.LoopStart = src.LoopStart;
                    a.TotalTime = src.TotalTime;
                    a.LoopTime = src.LoopTime;
                    a.PreLoopTime = src.PreLoopTime;
                }
            }
        }

        /// <summary>Reads one action body up to the next `[`. Mirrors Ikemen `ReadAnimation`.</summary>
        static MugenAnimation ReadAnimation(string[] lines, ref int i) {
            var a = new MugenAnimation();
            int ols = 0;
            List<float[]> clsn1 = new List<float[]>(), clsn1d = new List<float[]>();
            List<float[]> clsn2 = new List<float[]>(), clsn2d = new List<float[]>();
            bool def1 = true, def2 = true;

            for (; i < lines.Length; i++) {
                if (lines[i].Length > 0 && lines[i][0] == '[') break;
                string line = MugenDef.StripComment(lines[i]).Trim().ToLowerInvariant();

                if (line.StartsWith("copy action ")) {
                    string numStr = line.Substring(12).Trim();
                    a.CopyAction = numStr.Length == 0 ? -1 : MugenDef.Atoi(numStr);
                    return a;
                }

                var af = ReadAnimFrame(line);
                if (af != null) {
                    ols = a.LoopStart;
                    if (def1) clsn1 = clsn1d;
                    if (def2) clsn2 = clsn2d;
                    af.Clsn1 = clsn1;
                    af.Clsn2 = clsn2;
                    a.Frames.Add(af);
                    def1 = def2 = true;
                } else if (line.StartsWith("loopstart")) {
                    a.LoopStart = a.Frames.Count;
                } else if (line.StartsWith("clsn") && line.Length >= 5) {
                    int colon = line.IndexOf(':');
                    if (colon < 0) continue;
                    int size = MugenDef.Atoi(line.Substring(colon + 1));
                    if (size < 0) continue;
                    List<float[]> clsn;
                    if (line[4] == '1') {
                        clsn1 = NewBoxes(size);
                        clsn = clsn1;
                        if (line.Length >= 12 && line.Substring(5, 7) == "default") clsn1d = clsn1;
                        def1 = false;
                    } else if (line[4] == '2') {
                        clsn2 = NewBoxes(size);
                        clsn = clsn2;
                        if (line.Length >= 12 && line.Substring(5, 7) == "default") clsn2d = clsn2;
                        def2 = false;
                    } else continue;
                    if (size == 0) continue;
                    i++;
                    for (int n = 0; n < size && i < lines.Length;) {
                        string box = MugenDef.StripComment(lines[i]).Trim().ToLowerInvariant();
                        if (box.Length == 0) { i++; continue; }
                        if (box.Length < 4 || !box.StartsWith("clsn")) break;
                        int eq = box.IndexOf('=');
                        if (eq < 0) break;
                        var ary = MugenDef.SplitCsv(box.Substring(eq + 1));
                        if (ary.Length < 4) break;
                        int l = MugenDef.Atoi(ary[0]), t = MugenDef.Atoi(ary[1]);
                        int rr = MugenDef.Atoi(ary[2]), b = MugenDef.Atoi(ary[3]);
                        if (l > rr) { var tmp = l; l = rr; rr = tmp; }
                        if (t > b) { var tmp = t; t = b; b = tmp; }
                        clsn[n][0] = l; clsn[n][1] = t; clsn[n][2] = rr; clsn[n][3] = b;
                        n++; i++;
                    }
                    i--;
                }
            }

            if (a.LoopStart >= a.Frames.Count) a.LoopStart = ols;
            if (a.Frames.Count == 0) {
                // nothing to time
            } else if (a.Frames[a.Frames.Count - 1].Time == -1) {
                a.TotalTime = -1;
            } else {
                int tmp = 0;
                for (int f = 0; f < a.Frames.Count; f++) {
                    if (a.Frames[f].Time == -1) {
                        a.TotalTime = 0;
                        a.LoopTime = -tmp;
                        a.PreLoopTime = 0;
                    }
                    a.TotalTime += a.Frames[f].Time;
                    if (f < a.LoopStart) { a.PreLoopTime += a.Frames[f].Time; tmp += a.Frames[f].Time; }
                    else a.LoopTime += a.Frames[f].Time;
                }
                if (a.TotalTime == -1) a.PreLoopTime = 0;
            }
            return a;
        }

        static List<float[]> NewBoxes(int size) {
            var l = new List<float[]>(size);
            for (int i = 0; i < size; i++) l.Add(new float[4]);
            return l;
        }

        /// <summary>
        /// One element line: `group, number, x, y, time [, flip [, alpha [, xscale [, yscale
        /// [, angle]]]]]`. Returns null for any line that is not an element.
        /// </summary>
        public static AnimFrame ReadAnimFrame(string line) {
            if (string.IsNullOrEmpty(line)) return null;
            if ((line[0] < '0' || line[0] > '9') && line[0] != '-') return null;
            var ary = MugenDef.SplitCsv(line, 10);
            if (ary.Length < 5) return null;

            var af = new AnimFrame {
                Group = MugenDef.Atoi(ary[0]),
                Number = MugenDef.Atoi(ary[1]),
                Xoffset = MugenDef.Atoi(ary[2]),
                Yoffset = MugenDef.Atoi(ary[3]),
                Time = MugenDef.Atoi(ary[4])
            };

            if (ary.Length >= 6 && ary[5].Length > 0) {
                foreach (var ch in ary[5]) {
                    if (ch == 'H' || ch == 'h') { af.Hscale = -1; af.Xoffset *= -1; }
                    else if (ch == 'V' || ch == 'v') { af.Vscale = -1; af.Yoffset *= -1; }
                }
            }

            if (ary.Length >= 7 && ary[6].Length > 0) {
                string a = ary[6];
                int ia = a.IndexOfAny(new[] { 'A', 'S', 'a', 's' });
                if (ia >= 0) a = a.Substring(ia);
                a = a.ToLowerInvariant();
                if (a == "a1") { af.Trans = TransType.Add1; af.SrcAlpha = 255; af.DstAlpha = 128; }
                else if (a.StartsWith("sas")) { af.Trans = TransType.SubAdd; ParseAlpha(a, 3, 255, 255, out af.SrcAlpha, out af.DstAlpha); }
                else if (a.StartsWith("sa")) { af.Trans = TransType.SubAdd; af.SrcAlpha = 255; af.DstAlpha = 255; }
                else if (a.StartsWith("as")) { af.Trans = TransType.Add; ParseAlpha(a, 2, 255, 0, out af.SrcAlpha, out af.DstAlpha); }
                else if (a.StartsWith("ss")) { af.Trans = TransType.Sub; ParseAlpha(a, 2, 255, 255, out af.SrcAlpha, out af.DstAlpha); }
                else if (a.Length >= 1 && a[0] == 'a') { af.Trans = TransType.Add; af.SrcAlpha = 255; af.DstAlpha = 255; }
                else if (a.Length >= 1 && a[0] == 's') { af.Trans = TransType.Sub; af.SrcAlpha = 255; af.DstAlpha = 255; }
            }

            if (ary.Length >= 8 && ary[7].Length > 0 && MugenDef.IsNumeric(ary[7])) af.Xscale = MugenDef.Atof(ary[7]);
            if (ary.Length >= 9 && ary[8].Length > 0 && MugenDef.IsNumeric(ary[8])) af.Yscale = MugenDef.Atof(ary[8]);
            if (ary.Length >= 10 && ary[9].Length > 0 && MugenDef.IsNumeric(ary[9])) af.Angle = MugenDef.Atof(ary[9]);
            return af;
        }

        static void ParseAlpha(string a, int start, byte defSrc, byte defDst, out byte src, out byte dst) {
            src = defSrc; dst = defDst;
            int i = start, alp = 0;
            // Ikemen assigns the parsed value unconditionally: "AS" with no digits means 0,
            // not 255. Ported as is so an .air behaves exactly like it does in the engine.
            for (; i < a.Length && a[i] >= '0' && a[i] <= '9'; i++) alp = alp * 10 + (a[i] - '0');
            alp &= 0x3fff;
            src = (byte)Math.Min(alp, 255);
            if (i < a.Length && a[i] == 'd') {
                i++;
                if (i < a.Length && a[i] >= '0' && a[i] <= '9') {
                    alp = 0;
                    for (; i < a.Length && a[i] >= '0' && a[i] <= '9'; i++) alp = alp * 10 + (a[i] - '0');
                    alp &= 0x3fff;
                    dst = (byte)Math.Min(alp, 255);
                }
            }
        }
    }
}
