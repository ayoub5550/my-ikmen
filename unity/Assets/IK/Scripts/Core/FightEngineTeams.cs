using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>Team modes of a match (Ikemen GO `TeamMode`; Simul is not in dev.6).</summary>
    public enum TeamMode { Single, Turns, Tag }

    /// <summary>
    /// dev.6: teams. Each side has 1-4 members; <see cref="FightEngine.Players"/> always holds
    /// the member on the field, so the rest of the engine (hits, camera, HUD) stays 1-vs-1.
    ///
    /// Turns (Ikemen `TM_Turns`): one round per KO. The loser's next member comes in with full
    /// life, the winner keeps its life plus the recovery of defaultConfig.ini
    /// (`Turns.Recovery.Base` = 0 % of max life + `Turns.Recovery.Bonus` = 27.5 % scaled by the
    /// time left). The match ends when a side has no member left.
    ///
    /// Tag (Ikemen `TM_Tag`, `Tag.LoseOnKO = 0`): the round is lost only when every member of a
    /// side is KO. The `w` button (Ikemen's TagShiftFwd) swaps the member on the field with the
    /// next living partner when it is standing idle with control; a KO'd member is replaced by
    /// the next partner after it has hit the floor. Members waiting outside recover life
    /// slowly. Simplification against Ikemen's `data/tag.zss`: the partner appears where the
    /// leaving member stood instead of running in from the screen edge (documented in DEV6.md).
    /// </summary>
    public partial class FightEngine {
        public TeamMode Teams = TeamMode.Single;
        public readonly List<Fighter>[] Team = { new List<Fighter>(), new List<Fighter>() };
        /// <summary>Turns: members of each side already defeated.</summary>
        public readonly int[] Eliminated = { 0, 0 };
        /// <summary>defaultConfig.ini Turns.Recovery.Base / .Bonus (percent).</summary>
        public float TurnsRecoveryBase = 0f, TurnsRecoveryBonus = 27.5f;
        /// <summary>Ticks between two tag swaps of one side.</summary>
        public int TagCooldown = 30;
        /// <summary>Ticks a KO'd tag member lies on the floor before the partner comes in.</summary>
        public int TagKoDelay = 50;
        /// <summary>Life per second a waiting tag member gets back.</summary>
        public int TagRecoveryPerSecond = 6;
        /// <summary>Last swap of the tick per side (HUD flash): the fighter that came in.</summary>
        public readonly Fighter[] TaggedIn = new Fighter[2];

        readonly int[] tagTimer = { 0, 0 };
        readonly int[] koTimer = { 0, 0 };
        readonly int[] keepLife = { 0, 0 };
        int roundTicksAtStart;

        /// <summary>
        /// Gives a side its team. The first member starts on the field. Call before the first
        /// <see cref="StartRound"/> (the constructor's own player stays member 0 when it is in
        /// the list).
        /// </summary>
        public void SetTeam(int side, IList<Fighter> members) {
            var list = Team[side];
            list.Clear();
            foreach (var m in members) if (m != null && !list.Contains(m)) list.Add(m);
            if (list.Count == 0) return;
            for (int k = 0; k < list.Count; k++) {
                var f = list[k];
                f.PlayerNo = side;
                f.Engine = this;
                f.Id = side + 1 + 2 * k;
                f.JugglePoints = f.Const.AirJuggle;
                float stageW = Stage != null && Stage.LocalCoord != null && Stage.LocalCoord[0] > 0 ? Stage.LocalCoord[0] : 320f;
                float charW = f.Character != null && f.Character.LocalCoordWidth > 0 ? f.Character.LocalCoordWidth : 320f;
                f.Scl = stageW / charW;
            }
            PutOnField(side, list[0]);
            Eliminated[side] = 0;
        }

        public int TeamSize(int side) => Math.Max(1, Team[side].Count);

        /// <summary>Living members of a side (the one on the field included).</summary>
        public int Alive(int side) {
            if (Team[side].Count == 0) return Players[side] != null && Players[side].Life > 0 ? 1 : 0;
            if (Teams == TeamMode.Turns) return Team[side].Count - Eliminated[side];
            int n = 0;
            foreach (var f in Team[side]) if (f.Life > 0) n++;
            return n;
        }

        /// <summary>Has this side lost the round? (every tag member KO, else the fighter KO)</summary>
        bool SideOut(int side) {
            var p = Players[side];
            if (Teams == TeamMode.Tag && Team[side].Count > 1) {
                foreach (var f in Team[side]) if (f.Life > 0) return false;
                return true;
            }
            return p != null && p.Life <= 0;
        }

        /// <summary>Time over: the side with more life (tag: the team's life share) wins.</summary>
        float SideLifeShare(int side) {
            if (Teams == TeamMode.Tag && Team[side].Count > 1) {
                float sum = 0f, max = 0f;
                foreach (var f in Team[side]) { sum += Math.Max(0, f.Life); max += f.LifeMax; }
                return max > 0 ? sum / max : 0f;
            }
            var p = Players[side];
            return p != null ? (float)p.Life / Math.Max(1, p.LifeMax) : 0f;
        }

        void PutOnField(int side, Fighter incoming) {
            var outgoing = Players[side];
            if (outgoing == incoming) { if (!Chars.Contains(incoming)) Chars.Insert(Math.Min(side, Chars.Count), incoming); return; }
            int at = outgoing != null ? Chars.IndexOf(outgoing) : -1;
            if (outgoing != null) {
                Chars.Remove(outgoing);
                foreach (var c in Chars) if (c.IsHelper && c.Root == outgoing) c.Destroyed = true;
                foreach (var c in Chars) c.Targets.Remove(outgoing);
                outgoing.Targets.Clear();
            }
            Players[side] = incoming;
            if (at < 0) at = Math.Min(side, Chars.Count);
            Chars.Insert(Math.Min(at, Chars.Count), incoming);
            if (Bars[side] != null) Bars[side].MaxLife = incoming.LifeMax;
        }

        // ---- round hooks (called by FightEngine.StartRound / StepRound) -------------------

        /// <summary>Before a round: who is on the field and whose life carries over.</summary>
        void TeamsBeforeRound() {
            roundTicksAtStart = TimerCount > 0 ? TimerCount * FramesPerCount : 0;
            if (RoundNo <= 1) { Eliminated[0] = Eliminated[1] = 0; lastRecovery[0] = lastRecovery[1] = 0; }
            for (int s = 0; s < 2; s++) {
                keepLife[s] = 0; tagTimer[s] = 0; koTimer[s] = 0; TaggedIn[s] = null;
                var team = Team[s];
                if (team.Count == 0) continue;
                if (Teams == TeamMode.Turns) {
                    int idx = Math.Min(Eliminated[s], team.Count - 1);
                    var next = team[idx];
                    if (next == Players[s] && RoundNo > 1 && next.Life > 0) keepLife[s] = next.Life;
                    PutOnField(s, next);
                } else {
                    PutOnField(s, team[0]);
                    if (Teams == TeamMode.Tag) foreach (var f in team) if (f != team[0]) { f.ResetForRound(true); }
                }
            }
        }

        /// <summary>Turns: the winner keeps its life plus the recovery (after ResetForRound).</summary>
        void TeamsAfterReset(int side, Fighter f) {
            if (Teams != TeamMode.Turns || keepLife[side] <= 0) return;
            f.Life = Math.Min(f.LifeMax, keepLife[side] + lastRecovery[side]);
        }

        readonly int[] lastRecovery = { 0, 0 };

        /// <summary>Turns: the round winner's recovery, computed when the round ends.</summary>
        void TurnsRoundEnded(int winnerSide) {
            lastRecovery[0] = lastRecovery[1] = 0;
            if (winnerSide < 0) return;
            var f = Players[winnerSide];
            if (f == null) return;
            float timeShare = roundTicksAtStart > 0 && timerTicks > 0 ? (float)timerTicks / roundTicksAtStart : 0f;
            float pct = TurnsRecoveryBase + TurnsRecoveryBonus * timeShare;
            lastRecovery[winnerSide] = (int)Math.Round(f.LifeMax * pct / 100f);
        }

        /// <summary>Over → WinPose for the team modes. Returns true when it handled the round.</summary>
        bool TeamsCountRound() {
            if (Teams != TeamMode.Turns || (Team[0].Count <= 1 && Team[1].Count <= 1)) return false;
            if (RoundWinner == 2 || RoundWinner == 3) Eliminated[0]++;
            if (RoundWinner == 1 || RoundWinner == 3) Eliminated[1]++;
            TurnsRoundEnded(RoundWinner == 1 ? 0 : RoundWinner == 2 ? 1 : -1);
            Wins[0] = Eliminated[1];
            Wins[1] = Eliminated[0];
            MatchOver = Eliminated[0] >= TeamSize(0) || Eliminated[1] >= TeamSize(1);
            return true;
        }

        /// <summary>Every fighting tick: tag swaps, KO replacement, recovery of waiting members.</summary>
        void TeamsTick(CmdKey p1Input, CmdKey p2Input) {
            if (Teams != TeamMode.Tag) return;
            for (int s = 0; s < 2; s++) {
                TaggedIn[s] = null;
                var team = Team[s];
                if (team.Count < 2) continue;
                if (tagTimer[s] > 0) tagTimer[s]--;
                var cur = Players[s];
                // waiting members recover
                if (RoundTick % 10 == 0)
                    foreach (var f in team) if (f != cur && f.Life > 0 && f.Life < f.LifeMax)
                        f.Life = Math.Min(f.LifeMax, f.Life + Math.Max(1, TagRecoveryPerSecond / 6));
                if (State != RoundState.Fighting) continue;
                if (cur.Life <= 0) {
                    if (++koTimer[s] >= TagKoDelay) { var n = NextAlive(s); if (n != null) SwapIn(s, n, true); }
                    continue;
                }
                koTimer[s] = 0;
                var input = s == 0 ? p1Input : p2Input;
                bool wantsTag = (input & CmdKey.w) != 0 && !tagHeld[s];
                tagHeld[s] = (input & CmdKey.w) != 0;
                if (wantsTag) TryTag(s);
                else if (cur.AiLevel > 0 && RoundTick % 30 == 0 && cur.Life * 100 < cur.LifeMax * 35) {
                    // the CPU tags out when low and a partner is healthier (tag.zss TagAISwitch idea)
                    var n = NextAlive(s);
                    if (n != null && n.Life > cur.Life) TryTag(s);
                }
            }
        }

        readonly bool[] tagHeld = { false, false };

        /// <summary>Can the fighter on the field of this side tag out right now?</summary>
        public bool CanTag(int side) {
            if (Teams != TeamMode.Tag || Team[side].Count < 2 || State != RoundState.Fighting) return false;
            var cur = Players[side];
            if (cur == null || cur.Life <= 0 || tagTimer[side] > 0 || Paused) return false;
            if (!cur.Ctrl || cur.Move != MoveType.Idle || cur.Type != StateType.Standing) return false;
            return NextAlive(side) != null;
        }

        public bool TryTag(int side) {
            if (!CanTag(side)) return false;
            SwapIn(side, NextAlive(side), false);
            return true;
        }

        Fighter NextAlive(int side) {
            var team = Team[side];
            int at = team.IndexOf(Players[side]);
            for (int k = 1; k < team.Count; k++) {
                var f = team[(at + k) % team.Count];
                if (f.Life > 0) return f;
            }
            return null;
        }

        void SwapIn(int side, Fighter incoming, bool afterKo) {
            var outgoing = Players[side];
            float x = outgoing.WorldX;
            int facing = outgoing.Facing;
            int life = incoming.Life;
            int power = outgoing.Power;
            PutOnField(side, incoming);
            incoming.ResetForRound(true);
            incoming.Life = life;
            incoming.Power = power;                 // Team.PowerShare = 1
            incoming.WorldX = x;
            incoming.WorldY = 0f;
            incoming.Facing = facing;
            incoming.Ctrl = true;
            if (afterKo) outgoing.Life = 0;
            tagTimer[side] = TagCooldown;
            koTimer[side] = 0;
            TaggedIn[side] = incoming;
        }
    }
}
