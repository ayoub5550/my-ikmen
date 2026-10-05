using UnityEngine;

namespace IK.Input {
    /// <summary>
    /// One tick of logical input for one player: the 14 inputs Ikemen GO reads
    /// (<c>engine/ikemen-go/src/input.go</c>, <c>InputBits</c>) plus the raw analog stick.
    /// Touch, gamepad and keyboard all produce this same struct.
    /// </summary>
    [System.Serializable]
    public struct InputFrame {
        public bool U, D, L, R;          // directions (the engine converts L/R to B/F by facing)
        public bool a, b, c;             // kicks: light / medium / heavy
        public bool x, y, z;             // punches: light / medium / heavy
        public bool s;                   // start / taunt
        public bool d, w;                // Ikemen extra buttons
        public bool m;                   // menu / pause
        public Vector2 analog;           // raw stick, before quantisation (diagnostics / dead zone tuning)

        /// <summary>Bit order of <c>InputBits</c> in input.go: U D L R a b c x y z s d w m.</summary>
        public int ToBits() {
            int v = 0;
            if (U) v |= 1 << 0;
            if (D) v |= 1 << 1;
            if (L) v |= 1 << 2;
            if (R) v |= 1 << 3;
            if (a) v |= 1 << 4;
            if (b) v |= 1 << 5;
            if (c) v |= 1 << 6;
            if (x) v |= 1 << 7;
            if (y) v |= 1 << 8;
            if (z) v |= 1 << 9;
            if (s) v |= 1 << 10;
            if (d) v |= 1 << 11;
            if (w) v |= 1 << 12;
            if (m) v |= 1 << 13;
            return v;
        }

        public static InputFrame FromBits(int v) {
            var f = new InputFrame();
            f.U = (v & (1 << 0)) != 0;
            f.D = (v & (1 << 1)) != 0;
            f.L = (v & (1 << 2)) != 0;
            f.R = (v & (1 << 3)) != 0;
            f.a = (v & (1 << 4)) != 0;
            f.b = (v & (1 << 5)) != 0;
            f.c = (v & (1 << 6)) != 0;
            f.x = (v & (1 << 7)) != 0;
            f.y = (v & (1 << 8)) != 0;
            f.z = (v & (1 << 9)) != 0;
            f.s = (v & (1 << 10)) != 0;
            f.d = (v & (1 << 11)) != 0;
            f.w = (v & (1 << 12)) != 0;
            f.m = (v & (1 << 13)) != 0;
            return f;
        }

        public bool AnyButton => a || b || c || x || y || z || s || d || w || m;
        public bool AnyDirection => U || D || L || R;
        public bool Any => AnyButton || AnyDirection;

        /// <summary>Button part only, in Ikemen's <c>ButtonAssistCheck</c> order: a b c x y z s d w.</summary>
        public bool[] Buttons() => new[] { a, b, c, x, y, z, s, d, w };

        public InputFrame WithButtons(bool[] v) {
            a = v[0]; b = v[1]; c = v[2]; x = v[3]; y = v[4]; z = v[5]; s = v[6]; d = v[7]; w = v[8];
            return this;
        }

        public InputFrame Or(InputFrame o) {
            var f = this;
            f.U |= o.U; f.D |= o.D; f.L |= o.L; f.R |= o.R;
            f.a |= o.a; f.b |= o.b; f.c |= o.c;
            f.x |= o.x; f.y |= o.y; f.z |= o.z;
            f.s |= o.s; f.d |= o.d; f.w |= o.w; f.m |= o.m;
            if (o.analog.sqrMagnitude > f.analog.sqrMagnitude) f.analog = o.analog;
            return f;
        }

        /// <summary>Short MUGEN-style notation, e.g. "DF x+y" — used by the input display and tests.</summary>
        public override string ToString() {
            string dir = "N";
            if (U && L) dir = "UB"; else if (U && R) dir = "UF"; else if (D && L) dir = "DB";
            else if (D && R) dir = "DF"; else if (U) dir = "U"; else if (D) dir = "D";
            else if (L) dir = "B"; else if (R) dir = "F";
            string btn = "";
            if (x) btn += "x"; if (y) btn += "y"; if (z) btn += "z";
            if (a) btn += "a"; if (b) btn += "b"; if (c) btn += "c";
            if (s) btn += "s"; if (d) btn += "d"; if (w) btn += "w"; if (m) btn += "m";
            return btn.Length > 0 ? dir + " " + btn : dir;
        }
    }
}
