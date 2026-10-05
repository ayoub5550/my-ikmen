using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>Phases of a round (MUGEN's `roundstate`).</summary>
    public enum RoundState {
        Intro = 0,        // characters walking in / intro states
        Announce = 1,     // "Round N" ... "Fight!"
        Fighting = 2,     // both players in control
        Over = 3,         // someone was knocked out or the time ran out
        WinPose = 4,      // winner's pose, loser on the floor
    }

    /// <summary>
    /// The fight: two fighters on a stage, the hit system that connects them, the camera, the
    /// lifebar state and the round flow.
    ///
    /// One <see cref="Tick"/> is one MUGEN frame (60 Hz) and follows the order of the Go engine's
    /// main loop (`system.go action()`): input → the engine's own key handling → the characters'
    /// states → physics → **hit detection** → pushing and screen bounds → camera → round logic.
    /// Hit detection happens after the states have run, so a HitDef set this tick can connect on
    /// the same tick, exactly as in MUGEN.
    /// </summary>
    public partial class FightEngine {
        public readonly Fighter[] Players = new Fighter[2];
        public StageDefinition Stage;
        public StageCamera Camera;
        public FightDef Fight;
        public readonly LifeBarState[] Bars = new LifeBarState[2];

        /// <summary>Screen width in stage units (320 for a 4:3 MUGEN stage).</summary>
        public float ScreenWidth = 320f;

        public RoundState State = RoundState.Announce;
        public int RoundNo = 1;
        public int StateTime;
        public readonly int[] Wins = { 0, 0 };
        /// <summary>Rounds needed to win the match (`roundstowin`).</summary>
        public int RoundsToWin = 2;
        /// <summary>Round timer in counts (seconds). -1 disables the timer.</summary>
        public int TimerCount = 99;
        public int FramesPerCount = 60;
        int timerTicks;
        /// <summary>0 = none yet, 1 = P1 won the round, 2 = P2, 3 = draw.</summary>
        public int RoundWinner;
        public bool MatchOver;
        public int Tick_ = 0;
        /// <summary>Ticks since this round started (Go's `sys.intro` clock for the HUD).</summary>
        public int RoundTick { get; private set; }

        /// <summary>Ticks the announcement phase lasts (`fight.def` round.default.displaytime).</summary>
        public int AnnounceTime = 60;
        /// <summary>Ticks between the KO and the win pose (`over.waittime`).</summary>
        public int OverTime = 60;
        /// <summary>Ticks the win pose lasts before the next round.</summary>
        public int WinPoseTime = 120;

        /// <summary>Hits that connected this tick: attacker index, target index, guarded flag.</summary>
        public readonly List<int[]> HitsThisTick = new List<int[]>();

        public Fighter P1 => Players[0];
        public Fighter P2 => Players[1];

        public FightEngine(Fighter p1, Fighter p2, StageDefinition stage = null, FightDef fight = null) {
            Players[0] = p1;
            Players[1] = p2;
            for (int i = 0; i < 2; i++) {
                if (Players[i] == null) continue;
                Players[i].PlayerNo = i;
                Players[i].Engine = this;
                Players[i].JugglePoints = Players[i].Const.AirJuggle;
                float stageW = stage != null && stage.LocalCoord != null && stage.LocalCoord[0] > 0 ? stage.LocalCoord[0] : 320f;
                float charW = Players[i].Character != null && Players[i].Character.LocalCoordWidth > 0 ? Players[i].Character.LocalCoordWidth : 320f;
                Players[i].Scl = stageW / charW;
                Chars.Add(Players[i]);
            }
            Stage = stage;
            Fight = fight;
            if (stage != null && stage.LocalCoord != null && stage.LocalCoord[0] > 0) ScreenWidth = stage.LocalCoord[0];
            if (stage != null) Camera = new StageCamera(stage, ScreenWidth);
            if (fight != null) {
                if (fight.Time != null && fight.Time.FramesPerCount > 0) FramesPerCount = fight.Time.FramesPerCount;
                for (int i = 0; i < 2; i++)
                    Bars[i] = new LifeBarState(fight.LifeBar != null ? fight.LifeBar[i] : null,
                                               Players[i] != null ? Players[i].LifeMax : 1000, 3000);
            } else {
                for (int i = 0; i < 2; i++)
                    Bars[i] = new LifeBarState(null, Players[i] != null ? Players[i].LifeMax : 1000, 3000);
            }
            StartRound(1);
        }

        public Fighter Opponent(Fighter f) {
            if (f == null) return null;
            var r = f.Root;
            return r == Players[0] ? Players[1] : r == Players[1] ? Players[0] : (r.PlayerNo == 0 ? Players[1] : Players[0]);
        }

        // ---- round flow ---------------------------------------------------------

        public void StartRound(int no) {
            RoundNo = no;
            RoundTick = 0;
            State = RoundState.Announce;
            StateTime = 0;
            RoundWinner = 0;
            ClearRoundEntities();
            timerTicks = TimerCount > 0 ? TimerCount * FramesPerCount : -1;
            for (int i = 0; i < 2; i++) {
                var f = Players[i];
                if (f == null) continue;
                f.ResetForRound(true);
                f.Facing = i == 0 ? 1 : -1;
                if (Stage != null) {
                    var start = i == 0 ? Stage.P1Start : Stage.P2Start;
                    if (start != null) {
                        f.WorldX = start.StartX;
                        f.WorldY = start.StartY;
                        if (start.Facing != 0) f.Facing = start.Facing;
                    }
                } else {
                    f.PosX = i == 0 ? -70f : 70f;
                    f.PosY = 0f;
                }
                if (Bars[i] != null) {
                    Bars[i].Life = f.Life;
                    Bars[i].MaxLife = f.LifeMax;
                    Bars[i].RoundsWon = Wins[i];
                    Bars[i].TopLife = 1f;
                    Bars[i].MidLife = 1f;
                    Bars[i].OldLife = 1f;
                }
            }
            if (Camera != null) Camera.Reset();
            // Go `Stage.reset()` runs at the start of a round when the stage asks for it
            // (`[BGdef] resetbg`), so the background animations restart with the round.
            if (Stage != null && Stage.ResetBG) Stage.Reset();
        }

        /// <summary>Seconds left on the clock, or -1 when the timer is disabled.</summary>
        public int TimeLeft => timerTicks < 0 ? -1 : (timerTicks + FramesPerCount - 1) / FramesPerCount;

        public bool RoundOver => State == RoundState.Over || State == RoundState.WinPose;

        // ---- main loop ----------------------------------------------------------

        /// <summary>One frame of the fight. Inputs are already facing-relative per player.</summary>
        public void Tick(CmdKey p1Input, CmdKey p2Input) {
            Tick_++;
            RoundTick++;
            HitsThisTick.Clear();
            Sounds.Clear();
            StopChannels.Clear();
            if (Vibrate > 0) Vibrate--;
            bool paused = Paused;
            var snapshot = new List<Fighter>(Chars);
            foreach (var c in snapshot) c.BeginTick();

            // 1. input: only while the players have control of the round
            bool inputAllowed = State == RoundState.Fighting || State == RoundState.Over;
            Players[0]?.SetInput(inputAllowed ? p1Input : CmdKey.None);
            Players[1]?.SetInput(inputAllowed ? p2Input : CmdKey.None);

            if (!paused) {
                // 2. facing: a grounded character with control turns to the opponent (char.go `turn`)
                for (int i = 0; i < 2; i++) AutoTurn(Players[i], Players[1 - i]);

                // 3. guard distance of this tick, before the states run (`inguarddist`)
                for (int i = 0; i < 2; i++) UpdateGuardDistance(Players[i], Players[1 - i]);
                if (State == RoundState.Fighting)
                    for (int i = 0; i < 2; i++) EnterGuardIfHolding(Players[i]);
            }

            // 4. the characters (players first, then helpers in creation order)
            foreach (var f in snapshot) {
                if (f == null || f.Destroyed) continue;
                if (IsFrozen(f)) continue;
                f.TickHitTimers();
                if (f.InHitPause) { f.HitPauseTime--; continue; }
                f.Tick();
            }
            StepPause();

            // 5. projectiles and explods move, then hit detection
            TickProjectiles(paused);
            if (!paused && (State == RoundState.Fighting || State == RoundState.Over)) {
                var all = new List<Fighter>(Chars);
                foreach (var a in all) {
                    if (a.Destroyed || !a.HitDefActive) continue;
                    foreach (var b in all) {
                        if (b == a || b.Destroyed || b.PlayerNo == a.PlayerNo) continue;
                        CheckHits(a, b);
                    }
                }
                CheckProjectileHits();
            }
            TickExplods(paused);
            RemoveDestroyedHelpers();

            if (!paused) {
                // 6. pushing and the screen bounds
                PushApart();
                ClampToStage();

                // 7. camera
                if (Camera != null && Players[0] != null && Players[1] != null)
                    Camera.Update(Players[0].WorldX, Players[1].WorldX);

                // 8. the stage's own animations and scrolling state (Go `Stage.action`)
                if (Stage != null) Stage.Tick();

                // 9. round state machine
                StepRound();
            }
            StepShake();
            AllPalFx.Tick();
            BgPalFx.Tick();
            StepBars();
        }

        // ---- hit detection --------------------------------------------------------

        void CheckHits(Fighter a, Fighter b) {
            if (a == null || b == null) return;
            var hd = a.Hit;
            if (!a.HitDefActive || hd == null || !hd.IsValid || hd.Spent) return;
            if (a.Move != MoveType.Attack) return;
            if (b.UnhittableTime > 0) return;
            if (hd.Targets.Contains(b.Id)) return;          // this HitDef already connected
            if (!b.HittableBy(hd.Attr)) return;
            if (b.IsHelper && b.WorldClsn(2).Count == 0) return;
            if (TryReversal(a, b, hd)) return;
            if (!HitFlagAllows(hd, b)) return;
            if (!JuggleAllows(a, b, hd)) return;
            if (!Fighter.BoxesOverlap(a.WorldClsn(1), b.WorldClsn(2))) return;

            bool guarded = CanGuard(b, hd);
            b.ApplyHit(a, hd, guarded);
            a.RegisterHit(b, hd, guarded);
            HitsThisTick.Add(new[] { a.PlayerNo, b.PlayerNo, guarded ? 1 : 0 });
            if (SuperPauseP2DefMul != 1f) SuperPauseP2DefMul = 1f;
            if (hd.HitOnce > 0) a.HitDefActive = false;
            // corner push: the attacker is pushed back when the receiver is against a wall
            ApplyCornerPush(a, b, hd, guarded);
        }

        /// <summary>`hitflag`: does this attack connect with the receiver's current state?</summary>
        public static bool HitFlagAllows(HitDef hd, Fighter b) {
            switch (b.Type) {
                case StateType.Standing: if ((hd.Flag & HitFlag.High) == 0) return false; break;
                case StateType.Crouching: if ((hd.Flag & HitFlag.Low) == 0) return false; break;
                case StateType.Air: if ((hd.Flag & HitFlag.Air) == 0) return false; break;
                case StateType.LieDown: if ((hd.Flag & HitFlag.Down) == 0) return false; break;
            }
            bool beingHit = b.Move == MoveType.BeingHit;
            if ((hd.Flag & HitFlag.MustNotBeHit) != 0 && beingHit) return false;
            if ((hd.Flag & HitFlag.MustBeHit) != 0 && !beingHit) return false;
            // "F": only an attack with the F flag catches a falling opponent
            if ((hd.Flag & HitFlag.Fall) == 0 && b.Ghv.FallFlag && beingHit) return false;
            return true;
        }

        /// <summary>An airborne opponent can only be juggled while the attacker has points left.</summary>
        public static bool JuggleAllows(Fighter a, Fighter b, HitDef hd) {
            if (b.Type != StateType.Air || b.Move != MoveType.BeingHit) return true;
            return a.JugglePoints >= hd.AirJuggle;
        }

        /// <summary>
        /// Is the receiver blocking this attack? It must be able to guard the attack's
        /// `guardflag` for its own state type, be holding back and not be attacking.
        /// </summary>
        public static bool CanGuard(Fighter b, HitDef hd) {
            if (hd.Guard == GuardFlag.None) return false;
            if (b.Move == MoveType.Attack) return false;
            bool holdingBack = b.Commands.Buffer.Bb > 0;
            bool alreadyGuarding = HitStates.IsGuardState(b.StateNo);
            if (!holdingBack && !alreadyGuarding) return false;
            if (!b.Ctrl && !alreadyGuarding) return false;
            switch (b.Type) {
                case StateType.Air: return (hd.Guard & GuardFlag.Air) != 0;
                case StateType.Crouching: return (hd.Guard & GuardFlag.Low) != 0;
                case StateType.LieDown: return false;
                default: return (hd.Guard & GuardFlag.High) != 0;
            }
        }

        /// <summary>char.go `checkCornerPush`: an attack on a cornered opponent pushes the attacker.</summary>
        void ApplyCornerPush(Fighter a, Fighter b, HitDef hd, bool guarded) {
            if (Stage == null) return;
            float push = guarded ? hd.GuardCornerPush
                       : b.Type == StateType.Air ? hd.AirCornerPush : hd.GroundCornerPush;
            if (float.IsNaN(push) || push == 0f) return;
            float left = Stage.BoundLeft, right = Stage.BoundRight;
            bool cornered = b.WorldX <= left + (b.Const.GroundBack + 1f) * b.Scl || b.WorldX >= right - (b.Const.GroundFront + 1f) * b.Scl;
            if (cornered) a.VelX = -Math.Abs(push) * 0.5f;
        }

        // ---- guarding --------------------------------------------------------------

        void UpdateGuardDistance(Fighter f, Fighter enemy) {
            if (f == null || enemy == null) { if (f != null) f.InGuardDist = false; return; }
            f.InGuardDist = false;
            foreach (var c in Chars) {
                if (c.PlayerNo == f.PlayerNo || c.Destroyed) continue;
                var hd = c.Hit;
                bool threat = c.HitDefActive && hd != null && hd.IsValid && c.Move == MoveType.Attack;
                if (!threat) continue;
                float dist = Math.Abs(f.WorldX - c.WorldX);
                float guardDist = (hd.GuardDistX[0] > 0f ? hd.GuardDistX[0] : c.Const.AttackDist) * c.Scl;
                if (dist <= guardDist) { f.InGuardDist = true; return; }
            }
            foreach (var p in Projectiles) {
                if (p.State != Projectile.Phase.Flying || p.Owner == null || p.Owner.PlayerNo == f.PlayerNo) continue;
                if (Math.Abs(f.WorldX - p.PosX) <= p.Owner.Const.ProjAttackDist * p.Owner.Scl) { f.InGuardDist = true; return; }
            }
        }

        /// <summary>char.go: holding back inside the guard distance starts the guard state.</summary>
        void EnterGuardIfHolding(Fighter f) {
            if (f == null || !f.InGuardDist || !f.Ctrl) return;
            if (f.Move != MoveType.Idle) return;
            if (HitStates.IsGuardState(f.StateNo)) return;
            if (f.Commands.Buffer.Bb <= 0) return;
            f.ChangeState(HitStates.GuardStart, "guard started (inguarddist)");
        }

        // ---- pushing and bounds -------------------------------------------------------

        /// <summary>Push boxes may not overlap: both characters are moved apart equally.</summary>
        public void PushApart() {
            var a = Players[0];
            var b = Players[1];
            if (a == null || b == null) return;
            if (a.Type == StateType.LieDown || b.Type == StateType.LieDown) return;
            var ba = a.PushBox();
            var bb = b.PushBox();
            bool overlapX = ba[0] < bb[2] && bb[0] < ba[2];
            bool overlapY = ba[1] < bb[3] && bb[1] < ba[3];
            if (!overlapX || !overlapY) return;
            float overlap = a.WorldX <= b.WorldX ? ba[2] - bb[0] : bb[2] - ba[0];
            if (overlap <= 0f) return;
            float half = overlap / 2f;
            if (a.WorldX <= b.WorldX) { a.WorldX -= half; b.WorldX += half; }
            else { a.WorldX += half; b.WorldX -= half; }
        }

        /// <summary>Characters stay inside the stage's player bounds and on the screen.</summary>
        public void ClampToStage() {
            if (Stage == null) return;
            for (int i = 0; i < 2; i++) {
                var f = Players[i];
                if (f == null) continue;
                float min = Stage.LeftBound, max = Stage.RightBound;
                if (Camera != null) {
                    min = Math.Max(min, Camera.PlayerXMin);
                    max = Math.Min(max, Camera.PlayerXMax);
                }
                if (f.WorldX < min) f.WorldX = min;
                if (f.WorldX > max) f.WorldX = max;
            }
        }

        // ---- round state machine ---------------------------------------------------

        void StepRound() {
            StateTime++;
            switch (State) {
                case RoundState.Intro:
                    if (StateTime >= 1) { State = RoundState.Announce; StateTime = 0; }
                    break;

                case RoundState.Announce:
                    for (int i = 0; i < 2; i++) if (Players[i] != null) Players[i].Ctrl = false;
                    if (StateTime >= AnnounceTime) {
                        State = RoundState.Fighting;
                        StateTime = 0;
                        for (int i = 0; i < 2; i++) if (Players[i] != null) Players[i].Ctrl = true;
                    }
                    break;

                case RoundState.Fighting: {
                    if (timerTicks > 0) timerTicks--;
                    bool p1Ko = Players[0] != null && Players[0].Life <= 0;
                    bool p2Ko = Players[1] != null && Players[1].Life <= 0;
                    if (p1Ko || p2Ko) {
                        LastRoundKO = true;
                        RoundWinner = p1Ko && p2Ko ? 3 : p1Ko ? 2 : 1;
                        State = RoundState.Over;
                        StateTime = 0;
                    } else if (timerTicks == 0) {
                        LastRoundKO = false;
                        int l1 = Players[0] != null ? Players[0].Life : 0;
                        int l2 = Players[1] != null ? Players[1].Life : 0;
                        RoundWinner = l1 == l2 ? 3 : l1 > l2 ? 1 : 2;
                        State = RoundState.Over;
                        StateTime = 0;
                    }
                    break;
                }

                case RoundState.Over:
                    if (StateTime >= OverTime) {
                        State = RoundState.WinPose;
                        StateTime = 0;
                        if (RoundWinner == 1 || RoundWinner == 3) Wins[0]++;
                        if (RoundWinner == 2 || RoundWinner == 3) Wins[1]++;
                        if (Wins[0] >= RoundsToWin || Wins[1] >= RoundsToWin) MatchOver = true;
                        // the winner takes the win pose (state 180), the loser stays down
                        int winner = RoundWinner == 1 ? 0 : RoundWinner == 2 ? 1 : -1;
                        if (winner >= 0 && Players[winner] != null && Players[winner].Life > 0)
                            Players[winner].ChangeState(180, "round won");
                    }
                    break;

                case RoundState.WinPose:
                    if (!MatchOver && StateTime >= WinPoseTime) StartRound(RoundNo + 1);
                    break;
            }
        }

        void StepBars() {
            for (int i = 0; i < 2; i++) {
                var f = Players[i];
                var bar = Bars[i];
                if (f == null || bar == null) continue;
                bar.Life = f.Life;
                bar.Power = f.Power;
                bar.RoundsWon = Wins[i];
                bar.Time = TimeLeft;
                bar.Step(f.Move == MoveType.BeingHit && State != RoundState.Over);
            }
        }

        // ---- facing ---------------------------------------------------------------

        /// <summary>A grounded character with control faces the opponent (`noautoturn` blocks it).</summary>
        static void AutoTurn(Fighter f, Fighter enemy) {
            if (f == null || enemy == null) return;
            if (!f.Ctrl || f.NoAutoTurn) return;
            if (f.Type == StateType.Air || f.Type == StateType.LieDown) return;
            if (f.Move != MoveType.Idle) return;
            int want = enemy.WorldX >= f.WorldX ? 1 : -1;
            if (want != f.Facing) f.Facing = want;
        }
    }
}
