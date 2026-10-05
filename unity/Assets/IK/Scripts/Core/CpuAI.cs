using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace IK.Core {
    /// <summary>
    /// The computer opponent. MUGEN's own CPU does nothing more than press keys for a
    /// character (characters with their own AI read `AILevel` and drive themselves); this
    /// one reads the character's .cmd to learn which commands start which attack states,
    /// measures each attack's reach from its Clsn1 boxes, and then plays like a person:
    /// walks in, guards when the enemy attacks, punishes, uses specials from range and
    /// keeps pressing while the enemy is in hit stun. Difficulty 1..8 changes reaction time,
    /// guard rate, aggression and how often specials are used.
    ///
    /// It outputs one facing-relative <see cref="CmdKey"/> per tick, exactly what a pad would.
    /// </summary>
    public class CpuAI {
        public class Move {
            public string Command;
            public int StateNo;
            public char StateType = 'S';     // S/C/A the move must start from
            public int PowerNeeded;
            public float Reach = 40f;          // Clsn1 front extent in stage units
            public float ReachY = 0f;          // top of the attack box (negative = above the axis)
            public int Startup = 6;            // ticks to the first Clsn1
            public bool IsSpecial;             // a motion command (more than one step)
            public bool IsThrow;
            public bool IsProjectile;
            public List<CmdKey> Keys = new List<CmdKey>();
            public override string ToString() => Command + "->" + StateNo + " reach " + Reach;
        }

        public readonly List<Move> Moves = new List<Move>();
        public int Level = 4;
        readonly Queue<CmdKey> queue = new Queue<CmdKey>();
        readonly Random rng;
        int thinkTimer, holdTimer;
        CmdKey hold;
        public string LastDecision = "";

        public CpuAI(Fighter me, int level, int seed = 7) {
            Level = Math.Max(1, Math.Min(8, level));
            rng = new Random(seed + level * 31);
            if (me != null) Learn(me);
        }

        // ---- learning the character ---------------------------------------------------------

        static readonly Regex CommandRx = new Regex("command\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
        static readonly Regex PowerRx = new Regex("power\\s*>=?\\s*(\\d+)", RegexOptions.IgnoreCase);
        static readonly Regex TypeRx = new Regex("statetype\\s*=\\s*([SCA])", RegexOptions.IgnoreCase);
        static readonly Regex NotTypeRx = new Regex("statetype\\s*!=\\s*([SCA])", RegexOptions.IgnoreCase);

        void Learn(Fighter me) {
            var cmdState = me.States != null ? me.States.Get(-1) : null;
            if (cmdState == null || me.Cmd == null) return;
            var byName = new Dictionary<string, MugenCommand>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in me.Cmd.Commands) if (!byName.ContainsKey(c.Name)) byName[c.Name] = c;
            var seen = new HashSet<string>();
            foreach (var ctrl in cmdState.Controllers) {
                if (ctrl.Type != "changestate") continue;
                int stateNo;
                if (!int.TryParse(ctrl.Get("value").Trim(), out stateNo)) continue;
                var target = me.States.Get(stateNo);
                if (target == null) continue;
                string moveType = target.Get("movetype", "I").Trim().ToUpperInvariant();
                if (moveType.Length == 0 || moveType[0] != 'A') continue;
                string text = TriggerText(ctrl);
                var m = CommandRx.Match(text);
                if (!m.Success) continue;
                string cmdName = m.Groups[1].Value;
                MugenCommand cmd;
                if (!byName.TryGetValue(cmdName, out cmd)) continue;
                string key = cmdName + ":" + stateNo;
                if (!seen.Add(key)) continue;
                var mv = new Move { Command = cmdName, StateNo = stateNo };
                var tm = TypeRx.Match(text);
                if (tm.Success) mv.StateType = char.ToUpperInvariant(tm.Groups[1].Value[0]);
                else if (NotTypeRx.IsMatch(text)) mv.StateType = 'S';
                var pm = PowerRx.Match(text);
                if (pm.Success) int.TryParse(pm.Groups[1].Value, out mv.PowerNeeded);
                mv.IsSpecial = cmd.Steps.Count > 1;
                MeasureReach(me, target, mv);
                mv.IsThrow = IsThrowState(target);
                mv.IsProjectile = HasController(me.States, stateNo, "projectile", 3) || (mv.IsSpecial && mv.Reach > 150f);
                mv.Keys = KeysFor(cmd);
                if (mv.Keys.Count == 0) continue;
                Moves.Add(mv);
            }
        }

        static string TriggerText(StateController c) {
            var parts = new List<string>();
            foreach (var t in c.TriggerAll) parts.Add(t.Source ?? "");
            foreach (var g in c.TriggerGroups.Values) foreach (var t in g) parts.Add(t.Source ?? "");
            return string.Join(" && ", parts);
        }

        static bool IsThrowState(StateDef s) {
            foreach (var c in s.Controllers)
                if (c.Type == "hitdef" && c.Get("attr").ToUpperInvariant().Contains("T")) return true;
            return false;
        }

        static bool HasController(CnsFile states, int no, string type, int depth) {
            var s = states.Get(no);
            if (s == null || depth <= 0) return false;
            foreach (var c in s.Controllers) {
                if (c.Type == type) return true;
                if (c.Type == "changestate") {
                    int next;
                    if (int.TryParse(c.Get("value").Trim(), out next) && next != no && HasController(states, next, type, depth - 1)) return true;
                }
            }
            return false;
        }

        static void MeasureReach(Fighter me, StateDef s, Move mv) {
            int animNo;
            if (!int.TryParse(s.Get("anim", "-1").Trim(), out animNo)) {
                foreach (var c in s.Controllers)
                    if (c.Type == "changeanim" && int.TryParse(c.Get("value").Trim(), out animNo)) break;
            }
            var anim = me.Character != null && me.Character.Air != null ? me.Character.Air.Get(animNo) : null;
            if (anim == null) return;
            int t = 0;
            bool found = false;
            float reach = 0f, top = 0f;
            foreach (var f in anim.Frames) {
                foreach (var b in f.Clsn1) {
                    reach = Math.Max(reach, Math.Max(b[0], b[2]));
                    top = Math.Min(top, Math.Min(b[1], b[3]));
                    if (!found) { found = true; mv.Startup = t; }
                }
                t += Math.Max(1, f.Time);
            }
            if (found) { mv.Reach = reach; mv.ReachY = top; }
            else { mv.Reach = 0f; }
        }

        /// <summary>A key sequence that completes the command for a fighter facing right
        /// (B/F are facing-relative, so it works for both sides).</summary>
        public static List<CmdKey> KeysFor(MugenCommand cmd) {
            var keys = new List<CmdKey>();
            CmdKey held = CmdKey.None;   // keys still held by '/'
            for (int i = 0; i < cmd.Steps.Count; i++) {
                var st = cmd.Steps[i];
                CmdKey press = CmdKey.None, release = CmdKey.None;
                int charge = 0;
                bool hold = false;
                foreach (var k in st.Keys) {
                    var bits = Bits(k.Key);
                    if (k.Tilde) { release |= bits; charge = Math.Max(charge, k.ChargeTime); }
                    else { press |= bits; if (k.Slash) hold = true; }
                    if (st.OrLogic) break;
                }
                if (release != CmdKey.None) {
                    for (int t = 0; t < Math.Max(1, charge) + 1; t++) keys.Add(release | held);
                    keys.Add(held);
                }
                if (press != CmdKey.None) {
                    // a fresh press needs the key up first when the last frame already had it
                    if (keys.Count > 0 && (keys[keys.Count - 1] & press) != 0 && !hold) keys.Add(held);
                    keys.Add(press | held);
                    keys.Add(press | held);
                    if (hold) held |= press;
                }
            }
            if (keys.Count > 40) keys.RemoveRange(40, keys.Count - 40);
            return keys;
        }

        static CmdKey Bits(CK k) {
            switch (k) {
                case CK.U: return CmdKey.U;
                case CK.D: return CmdKey.D;
                case CK.B: case CK.L: return CmdKey.B;
                case CK.F: case CK.R: return CmdKey.F;
                case CK.UB: case CK.UL: return CmdKey.U | CmdKey.B;
                case CK.UF: case CK.UR: return CmdKey.U | CmdKey.F;
                case CK.DB: case CK.DL: return CmdKey.D | CmdKey.B;
                case CK.DF: case CK.DR: return CmdKey.D | CmdKey.F;
                case CK.a: return CmdKey.a;
                case CK.b: return CmdKey.b;
                case CK.c: return CmdKey.c;
                case CK.x: return CmdKey.x;
                case CK.y: return CmdKey.y;
                case CK.z: return CmdKey.z;
                case CK.s: return CmdKey.s;
                case CK.d: return CmdKey.d;
                case CK.w: return CmdKey.w;
                case CK.m: return CmdKey.m;
            }
            return CmdKey.None;
        }

        // ---- playing --------------------------------------------------------------------

        double R() => rng.NextDouble();

        float GuardRate => 0.15f + Level * 0.1f;          // 0.25 .. 0.95
        float Aggression => 0.35f + Level * 0.06f;        // 0.41 .. 0.83
        int ReactionTicks => Math.Max(1, 16 - Level * 2);  // 14 .. 1
        float SpecialRate => 0.1f + Level * 0.07f;

        /// <summary>One tick of CPU input for <paramref name="me"/> against <paramref name="enemy"/>.</summary>
        public CmdKey Tick(Fighter me, Fighter enemy, FightEngine engine) {
            if (me == null || enemy == null) return CmdKey.None;
            if (engine != null && engine.State != RoundState.Fighting) { queue.Clear(); holdTimer = 0; return CmdKey.None; }

            // distances in my own units (characters may use different localcoords)
            float dist = (Math.Abs(enemy.WorldX - me.WorldX) - me.Const.GroundFront * me.Scl - enemy.Const.GroundFront * enemy.Scl) / me.Scl;
            bool threat = enemy.Move == MoveType.Attack && enemy.HitDefActive && dist < enemy.Const.AttackDist * enemy.Scl / me.Scl * 0.8f;
            if (engine != null)
                foreach (var p in engine.Projectiles)
                    if (p.State == Projectile.Phase.Flying && p.Owner != null && p.Owner.PlayerNo != me.PlayerNo &&
                        Math.Abs(p.PosX - me.WorldX) < 120f * me.Scl) threat = true;

            // a combo in progress: keep the planned keys going
            if (queue.Count > 0) return queue.Dequeue();

            // guarding is a reaction, not a plan
            if (threat && me.Move != MoveType.Attack && me.Type != StateType.Air) {
                if (holdTimer > 0 && (hold & CmdKey.B) != 0) { holdTimer--; return hold; }
                if (R() < GuardRate) {
                    bool low = enemy.Type == StateType.Crouching;
                    hold = CmdKey.B | (low ? CmdKey.D : CmdKey.None);
                    holdTimer = 6 + Level;
                    LastDecision = "guard";
                    return hold;
                }
            }
            if (holdTimer > 0) { holdTimer--; return hold; }

            // pressure: the enemy is reeling, press the next attack as soon as possible
            bool enemyStunned = enemy.Move == MoveType.BeingHit && !enemy.HitOver;
            if (!me.Ctrl && !(enemyStunned && me.Move == MoveType.Attack)) return CmdKey.None;

            if (--thinkTimer > 0) return CmdKey.None;
            thinkTimer = ReactionTicks + rng.Next(0, Math.Max(1, 10 - Level));

            char myType = me.Type == StateType.Air ? 'A' : me.Type == StateType.Crouching ? 'C' : 'S';
            var usable = new List<Move>();
            foreach (var m in Moves) {
                if (m.PowerNeeded > me.Power) continue;
                if (m.StateType != myType && !(m.StateType == 'C' && myType == 'S') && !(m.StateType == 'S' && myType == 'C')) continue;
                if (m.IsThrow && (dist > 12f || enemy.Type == StateType.Air || enemy.Move == MoveType.BeingHit)) continue;
                usable.Add(m);
            }
            // in range: attack
            var inRange = new List<Move>();
            foreach (var m in usable) {
                float reach = m.Reach - me.Const.GroundFront + 4f;
                if (m.IsProjectile) { if (dist > 60f) inRange.Add(m); continue; }
                if (m.Reach > 0f && reach >= dist) inRange.Add(m);
            }
            if (inRange.Count > 0 && (enemyStunned || R() < Aggression)) {
                var pick = Pick(inRange, enemyStunned);
                Plan(pick.Keys);
                LastDecision = "attack " + pick.Command;
                return queue.Count > 0 ? queue.Dequeue() : CmdKey.None;
            }
            if (myType == 'A') return CmdKey.None;

            // approach, retreat or jump in
            double r = R();
            if (dist > 30f) {
                if (r < 0.12 + Level * 0.01) { PlanHold(CmdKey.U | CmdKey.F, 3); LastDecision = "jump in"; }
                else if (r < 0.2 && HasCommand("FF")) { PlanHold(CmdKey.F, 2); queue.Enqueue(CmdKey.None); PlanHold(CmdKey.F, 6); LastDecision = "dash"; }
                else { PlanHold(CmdKey.F, 8 + rng.Next(0, 10)); LastDecision = "walk in"; }
            } else {
                if (r < 0.15) { PlanHold(CmdKey.B, 8); LastDecision = "back off"; }
                else if (r < 0.25) { PlanHold(CmdKey.D | CmdKey.B, 10); LastDecision = "crouch guard"; }
                else if (r < 0.32) { PlanHold(CmdKey.U, 2); LastDecision = "jump"; }
                else { PlanHold(CmdKey.F, 4); LastDecision = "step in"; }
            }
            return queue.Count > 0 ? queue.Dequeue() : CmdKey.None;
        }

        bool HasCommand(string name) {
            foreach (var m in Moves) if (string.Equals(m.Command, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        Move Pick(List<Move> moves, bool comboing) {
            double total = 0;
            var w = new double[moves.Count];
            for (int i = 0; i < moves.Count; i++) {
                var m = moves[i];
                double x = 1.0;
                if (m.IsSpecial) x *= SpecialRate * 3;
                if (m.PowerNeeded > 0) x *= 0.5 + Level * 0.1;
                if (comboing) x *= m.Startup <= 6 ? 3 : 0.5;     // fast moves link
                if (m.IsThrow) x *= 2;
                w[i] = x; total += x;
            }
            double r = R() * total;
            for (int i = 0; i < moves.Count; i++) { r -= w[i]; if (r <= 0) return moves[i]; }
            return moves[moves.Count - 1];
        }

        void Plan(List<CmdKey> keys) { foreach (var k in keys) queue.Enqueue(k); }
        void PlanHold(CmdKey k, int n) { for (int i = 0; i < n; i++) queue.Enqueue(k); }
    }
}
