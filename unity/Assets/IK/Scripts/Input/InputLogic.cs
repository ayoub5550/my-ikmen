using UnityEngine;

namespace IK.Input {
    /// <summary>
    /// Pure input maths, no Unity scene state: 8-way quantisation, SOCD resolution and
    /// button assist. Ported 1:1 from Ikemen GO (<c>src/input.go</c>:
    /// <c>InputReader.SocdResolution</c>, <c>InputReader.ButtonAssistCheck</c>) so the touch
    /// layer feeds the engine exactly what the reference engine would.
    /// Everything here is unit-tested in <c>IK.Tests.InputLogicTests</c>.
    /// </summary>
    public static class InputLogic {
        /// <summary>Sector widths from docs/TOUCH_AND_SETTINGS.md §3.1: cardinals 50°, diagonals 40°.</summary>
        public const float CardinalSector = 50f;
        public const float DiagonalSector = 40f;

        /// <summary>
        /// Quantise a stick/pad vector to the 8 MUGEN directions.
        /// Returns (U, D, L, R); all false inside the dead zone.
        /// Angle 0° = right (+x), measured counter-clockwise.
        /// </summary>
        public static void Quantise8(Vector2 v, float deadZone, out bool U, out bool D, out bool L, out bool R) {
            U = D = L = R = false;
            if (v.sqrMagnitude <= deadZone * deadZone) return;
            float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            // Sector table: centres every 45°, cardinals are wider than diagonals.
            int sector = SectorIndex(angle);
            switch (sector) {
                case 0: R = true; break;                 // F
                case 1: R = true; U = true; break;       // UF
                case 2: U = true; break;                 // U
                case 3: L = true; U = true; break;       // UB
                case 4: L = true; break;                 // B
                case 5: L = true; D = true; break;       // DB
                case 6: D = true; break;                 // D
                case 7: R = true; D = true; break;       // DF
            }
        }

        /// <summary>
        /// Index 0..7 (F, UF, U, UB, B, DB, D, DF) for an angle in degrees, using the
        /// 50°/40° sector widths. Boundaries: a cardinal owns ±25°, a diagonal ±20°.
        /// </summary>
        public static int SectorIndex(float angle) {
            angle = Mathf.Repeat(angle, 360f);
            for (int i = 0; i < 8; i++) {
                float centre = i * 45f;
                float half = (i % 2 == 0 ? CardinalSector : DiagonalSector) * 0.5f;
                float delta = Mathf.Abs(Mathf.DeltaAngle(angle, centre));
                if (delta <= half) return i;
            }
            // Gaps between sectors (cardinal 50 + diagonal 40 = 90 > 2*45 means there are no
            // gaps; this is only a safety net): snap to the nearest centre.
            return Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 45f) % 8;
        }

        /// <summary>
        /// SOCD resolver with the five Ikemen methods. Keeps the "which direction was pressed
        /// first" state, exactly like <c>InputReader.SocdFirst</c>.
        /// 0 = allow both, 1 = last wins, 2 = absolute priority (U over D, F over B),
        /// 3 = first wins, 4 (default) = deny both.
        /// </summary>
        public class Socd {
            readonly bool[] first = new bool[4];   // U, D, B(L), F(R)
            readonly bool[] allow = { true, true, true, true };

            public void Reset() {
                for (int i = 0; i < 4; i++) { first[i] = false; allow[i] = true; }
            }

            public void Resolve(int method, ref bool U, ref bool D, ref bool L, ref bool R) {
                Pair(method, U, D, 0, 1);
                Pair(method, L, R, 2, 3);
                U = U && allow[0];
                D = D && allow[1];
                L = L && allow[2];
                R = R && allow[3];
            }

            // iA/iB are the indices of the opposing pair: (U,D) or (B,F).
            void Pair(int method, bool a, bool b, int iA, int iB) {
                if (method == 1 || method == 3) {
                    if (a || b) {
                        if (!a) first[iA] = false;
                        if (!b) first[iB] = false;
                        if (!first[iA] && !first[iB]) {
                            // Ikemen marks the direction that is *not* the "preferred" one here;
                            // for U/D it flags D, for B/F it flags B (see input.go resolveUD/resolveBF).
                            if (iA == 0) { if (b) first[iB] = true; else first[iA] = true; }
                            else { if (a) first[iA] = true; else first[iB] = true; }
                        }
                    } else {
                        first[iA] = false; first[iB] = false;
                    }
                }
                if (a && b) {
                    switch (method) {
                        case 0:                                  // allow both
                            allow[iA] = true; allow[iB] = true; break;
                        case 1:                                  // last direction wins
                            if (iA == 0) {
                                if (first[iA]) { allow[iA] = false; allow[iB] = true; }
                                else { allow[iA] = true; allow[iB] = false; }
                            } else {
                                if (first[iB]) { allow[iA] = true; allow[iB] = false; }
                                else { allow[iA] = false; allow[iB] = true; }
                            }
                            break;
                        case 2:                                  // absolute priority: U over D, F over B
                            if (iA == 0) { allow[iA] = true; allow[iB] = false; }
                            else { allow[iA] = false; allow[iB] = true; }
                            break;
                        case 3:                                  // first direction wins
                            if (iA == 0) {
                                if (first[iA]) { allow[iA] = true; allow[iB] = false; }
                                else { allow[iA] = false; allow[iB] = true; }
                            } else {
                                if (first[iB]) { allow[iA] = false; allow[iB] = true; }
                                else { allow[iA] = true; allow[iB] = false; }
                            }
                            break;
                        default:                                 // 4: deny both
                            allow[iA] = false; allow[iB] = false; break;
                    }
                } else {
                    allow[iA] = true; allow[iB] = true;
                }
            }
        }

        /// <summary>
        /// Ikemen's one-frame leniency for simultaneous presses (<c>ButtonAssistCheck</c>):
        /// a press is reported one tick later, and a button pressed in the same tick as a
        /// buffered one is reported with it — that is what makes touch "x+y" practical.
        /// Order: a b c x y z s d w.
        /// </summary>
        public class ButtonAssist {
            bool[] buffer = new bool[9];

            public void Reset() { buffer = new bool[9]; }

            /// <param name="enabled">setting Input.ButtonAssist</param>
            /// <param name="paused">assist is disabled outside a match / while paused, like Ikemen</param>
            public bool[] Check(bool[] current, bool enabled = true, bool paused = false) {
                if (!enabled || paused) { buffer = new bool[9]; return (bool[])current.Clone(); }
                bool prevAny = false;
                for (int i = 0; i < buffer.Length; i++) if (buffer[i]) { prevAny = true; break; }
                var result = new bool[9];
                for (int i = 0; i < buffer.Length; i++) result[i] = buffer[i] || (current[i] && prevAny);
                buffer = (bool[])current.Clone();
                return result;
            }
        }
    }
}
