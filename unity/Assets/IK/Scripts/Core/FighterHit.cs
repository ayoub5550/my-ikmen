using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>
    /// The hit half of <see cref="Fighter"/>: the active HitDef, hit pause, the GetHitVars of the
    /// hit being taken, damage, juggling and the collision boxes in world coordinates.
    ///
    /// Behaviour follows `engine/ikemen-go/src/char.go` (`hitResultCheck`, `clsnCheck`,
    /// `computeDamage`, `hitPause`, `jugglePoints`) and the standard MUGEN 1.0 documentation of
    /// HitDef / GetHitVar. The actual collision loop that pairs two fighters lives in
    /// <see cref="FightEngine"/>; everything here is per-character state.
    /// </summary>
    public partial class Fighter {
        /// <summary>The fight this character belongs to (null while running alone, as in dev.3).</summary>
        public FightEngine Engine;
        /// <summary>0 for P1, 1 for P2.</summary>
        public int PlayerNo;
        /// <summary>Player id (`id` trigger, `playerid` redirection). Players get 1 and 2 (as in
        /// dev.4); helpers get unique ids from the engine.</summary>
        public int Id { get { return assignedId != 0 ? assignedId : PlayerNo + 1; } set { assignedId = value; } }
        int assignedId;

        /// <summary>The HitDef the current state has set, or null.</summary>
        public HitDef Hit;
        /// <summary>The HitDef is active for collision checks (`atktmp`): it was set in an
        /// attacking state and has not been cleared by a state change.</summary>
        public bool HitDefActive;

        /// <summary>Ticks of hit pause left. While &gt; 0 the character's state and animation freeze.</summary>
        public int HitPauseTime;
        /// <summary>`GetHitVar` table of the hit currently being taken.</summary>
        public readonly GetHitVars Ghv = new GetHitVars();

        public int MoveContactFlag, MoveHitFlag, MoveGuardedFlag, MoveReversedFlag;
        public int HitCount, UniqHitCount;
        /// <summary>Juggle points left for the current combo (`[Data] airjuggle`).</summary>
        public int JugglePoints;
        public float AttackMul = 1f, DefenceMul = 1f;
        /// <summary>Set by the engine for one tick when the character is inside an enemy's guard distance.</summary>
        public bool InGuardDist;
        /// <summary>True while the character has been knocked out.</summary>
        public bool KO;
        /// <summary>Ticks the character may not be hit for (`NotHitBy` / throws), 0 = hittable.</summary>
        public int UnhittableTime;
        /// <summary>Sprite priority set by the last hit (recorded for the renderer).</summary>
        public int SprPriority;
        /// <summary>The last hit spark the engine should draw: group number and position.</summary>
        public int LastSparkNo = -1;
        public float LastSparkX, LastSparkY;
        /// <summary>Sounds the fight engine should play this tick (group, number).</summary>
        public readonly List<int[]> PendingSounds = new List<int[]>();

        /// <summary>States of the attacker, used while this character runs a `p2stateno`.</summary>
        public CnsFile ForeignStates;

        public bool Alive => Life > 0;
        public int LifeMax => Const.Life;
        public int PowerMax = 3000;
        /// <summary>The other fighter, or null while running alone (dev.3 training screen).</summary>
        public Fighter Opponent => Engine != null ? Engine.Opponent(this) : null;

        // ---- per-tick bookkeeping ----------------------------------------------

        /// <summary>
        /// char.go: a character in hit pause does not run its states, physics or animation.
        /// The counters that keep running are the hit-shake timer and the pause itself.
        /// </summary>
        public bool InHitPause => HitPauseTime > 0;

        /// <summary>Called by the engine at the start of every tick, before the states run.</summary>
        public void BeginTick() {
            LastSparkNo = -1;
            PendingSounds.Clear();
            if (UnhittableTime > 0) UnhittableTime--;
        }

        /// <summary>Counters that advance even inside hit pause (char.go `tick`).</summary>
        public void TickHitTimers() {
            if (Ghv.HitShakeTime > 0) Ghv.HitShakeTime--;
            else if (Ghv.HitTime > 0) Ghv.HitTime--;
        }

        /// <summary>`hitover`: the hit stun has run out.</summary>
        public bool HitOver => Ghv.HitShakeTime <= 0 && Ghv.HitTime <= 0;
        /// <summary>`hitshakeover`: the freeze part of the hit has run out.</summary>
        public bool HitShakeOver => Ghv.HitShakeTime <= 0;
        /// <summary>`hitfall`: the current hit knocks this character down.</summary>
        public bool HitFall => Ghv.FallFlag;

        public void ClearMoveHit() {
            MoveContactFlag = MoveHitFlag = MoveGuardedFlag = MoveReversedFlag = 0;
        }

        /// <summary>char.go `clearHitDef`: a state change invalidates the HitDef.</summary>
        public void ClearHitDef() {
            HitDefActive = false;
            ReversalActive = false;
            if (Hit != null) Hit.Targets.Clear();
        }

        // ---- collision boxes ----------------------------------------------------

        /// <summary>
        /// The character's Clsn boxes of the current animation element in world units.
        /// MUGEN boxes are written facing right around the character's axis, so a left-facing
        /// character mirrors them: `x' = pos.x - right`, `x'' = pos.x - left`.
        /// </summary>
        public List<float[]> WorldClsn(int group) {
            var result = new List<float[]>();
            var frame = Anim != null ? Anim.CurrentFrame : null;
            if (frame == null) return result;
            var boxes = group == 1 ? frame.Clsn1 : frame.Clsn2;
            for (int i = 0; i < boxes.Count; i++) {
                var b = boxes[i];
                float l, r;
                if (Facing >= 0) { l = PosX + b[0]; r = PosX + b[2]; }
                else { l = PosX - b[2]; r = PosX - b[0]; }
                // world units (dev.5: a character's boxes are in its own localcoord)
                result.Add(new[] { l * Scl, (PosY + b[1]) * Scl, r * Scl, (PosY + b[3]) * Scl });
            }
            return result;
        }

        /// <summary>Do any of these boxes overlap any of those? (sys.clsnOverlap, 2D part)</summary>
        public static bool BoxesOverlap(List<float[]> a, List<float[]> b) {
            for (int i = 0; i < a.Count; i++) {
                var x = a[i];
                for (int j = 0; j < b.Count; j++) {
                    var y = b[j];
                    if (x[0] < y[2] && y[0] < x[2] && x[1] < y[3] && y[1] < x[3]) return true;
                }
            }
            return false;
        }

        /// <summary>The character's push box (`[Size] ground.front/back`, `air.front/back`).</summary>
        public float[] PushBox() {
            float front = Type == StateType.Air ? Const.AirFront : Const.GroundFront;
            float back = Type == StateType.Air ? Const.AirBack : Const.GroundBack;
            float l = Facing >= 0 ? PosX - back : PosX - front;
            float r = Facing >= 0 ? PosX + front : PosX + back;
            return new[] { l * Scl, (PosY - Const.Height) * Scl, r * Scl, PosY * Scl };
        }

        // ---- taking a hit --------------------------------------------------------

        /// <summary>
        /// Applies a connecting HitDef to this character. `guarded` has already been decided by
        /// the engine. Returns the damage actually dealt.
        ///
        /// Order follows char.go: GetHitVars first, then damage, then the state change, so that
        /// a `p2stateno` state already sees the new GetHitVars.
        /// </summary>
        public int ApplyHit(Fighter attacker, HitDef hd, bool guarded) {
            bool air = Type == StateType.Air;
            Ghv.Attr = hd.Attr;
            Ghv.AnimType = air ? hd.AirAnimType : hd.AnimType;
            Ghv.GroundType = hd.GroundType;
            Ghv.AirType = hd.AirType;
            Ghv.Guarded = guarded;
            Ghv.HitId = hd.Id;
            Ghv.ChainId = hd.Id;
            Ghv.PlayerId = attacker != null ? attacker.Id : -1;
            Ghv.AttackerFacing = attacker != null ? attacker.Facing : 1;
            Ghv.XAccel = hd.XAccel;
            Ghv.YAccel = hd.YAccel;
            Ghv.DownBounce = hd.DownBounce;
            Ghv.FallDamage = hd.FallDamage;
            Ghv.FallXVel = hd.FallXVelocity;
            Ghv.FallYVel = hd.FallYVelocity;
            Ghv.FallRecover = hd.FallRecover;
            Ghv.FallRecoverTime = hd.FallRecoverTime;
            Ghv.FallKill = hd.FallKill;
            Ghv.FallEnvShakeTime = hd.FallEnvShakeTime;
            Ghv.FallEnvShakeFreq = hd.FallEnvShakeFreq;
            Ghv.FallEnvShakeAmpl = hd.FallEnvShakeAmpl;
            Ghv.FallEnvShakePhase = hd.FallEnvShakePhase;
            StateOwner = null;
            if (!guarded && hd.HitPalFx != null) PalFx.CopyFrom(hd.HitPalFx);
            if (Engine != null) Engine.RemoveOnGetHit(this);

            if (guarded) {
                Ghv.HitShakeTime = Math.Max(0, hd.GuardPauseTime[1]);
                Ghv.HitTime = air ? hd.AirGuardCtrlTime : hd.GuardHitTime;
                Ghv.SlideTime = hd.GuardSlideTime;
                Ghv.CtrlTime = air ? hd.AirGuardCtrlTime : hd.GuardCtrlTime;
                Ghv.XVel = (air ? hd.AirGuardVelocity[0] : hd.GuardVelocity[0]) * -Ghv.AttackerFacing * Facing;
                Ghv.YVel = air ? hd.AirGuardVelocity[1] : hd.GuardVelocity[1];
                Ghv.FallFlag = false;
            } else {
                Ghv.HitShakeTime = Math.Max(0, hd.PauseTime[1]);
                Ghv.HitTime = air ? hd.AirHitTime : hd.GroundHitTime;
                Ghv.SlideTime = hd.GroundSlideTime;
                Ghv.CtrlTime = hd.GroundHitTime;
                Ghv.XVel = (air ? hd.AirVelocity[0] : hd.GroundVelocity[0]) * -Ghv.AttackerFacing * Facing;
                Ghv.YVel = air ? hd.AirVelocity[1] : hd.GroundVelocity[1];
                Ghv.FallFlag = air ? hd.AirFall > 0 : hd.GroundFall;
                Ghv.Fall = Ghv.FallFlag;
                Ghv.HitCount++;
            }
            Ghv.JugglePoints = hd.AirJuggle;
            if (attacker != null && attacker.Scl != Scl) {
                float k = attacker.Scl / Scl;       // attacker units → my units
                Ghv.XVel *= k; Ghv.YVel *= k; Ghv.XAccel *= k; Ghv.YAccel *= k;
                if (!float.IsNaN(Ghv.FallXVel)) Ghv.FallXVel *= k;
                Ghv.FallYVel *= k;
            }

            // damage
            int raw = guarded ? hd.GuardDamage : hd.HitDamage;
            int damage = ComputeDamage(raw, attacker);
            bool mayKill = guarded ? hd.GuardKill : hd.Kill;
            if (damage > 0) {
                if (!mayKill && damage >= Life) damage = Math.Max(0, Life - 1);
                Life = Math.Max(0, Life - damage);
            }
            Ghv.Damage = damage;
            Ghv.HitPower = guarded ? hd.GuardGetPower : hd.HitGetPower;
            Power = Math.Min(PowerMax, Power + (guarded ? hd.GuardGivePower : hd.HitGivePower));

            // facing: the receiver turns to the attacker unless the HitDef says otherwise
            if (hd.P2Facing != 0 && attacker != null) Facing = hd.P2Facing > 0 ? attacker.Facing : -attacker.Facing;
            else if (attacker != null && !NoAutoTurn) Facing = attacker.WorldX > WorldX ? 1 : -1;

            if (hd.P2SprPriority != 0) SprPriority = hd.P2SprPriority;
            if (hd.ForceStand == 1 && Type == StateType.Crouching) Type = StateType.Standing;
            if (hd.ForceCrouch == 1 && Type == StateType.Standing) Type = StateType.Crouching;

            // juggling: an air hit costs the attacker juggle points
            if (air && attacker != null && !guarded) attacker.JugglePoints -= hd.AirJuggle;

            // state change
            var ovr = guarded ? null : OverrideFor(hd.Attr);
            if (ovr != null) {
                if (ovr.ForceAir) { Type = StateType.Air; }
                ForeignStates = null;
                ChangeState(ovr.StateNo, "hitoverride");
            } else if (!guarded && hd.P2StateNo >= 0 && attacker != null) {
                ForeignStates = hd.P2GetP1State ? attacker.Root.States : States;
                StateOwner = hd.P2GetP1State ? attacker : null;
                ChangeState(hd.P2StateNo, "p2stateno from player " + attacker.Id);
            } else {
                int next = guarded ? HitStates.GuardShakeState(this) : HitStates.GetHitShakeState(this);
                ChangeState(next, guarded ? "guard hit" : "hit");
            }
            if (Life <= 0 && !NoKO) KO = true;
            return damage;
        }

        /// <summary>char.go `computeDamage`: damage × attacker attack% × receiver defence%.</summary>
        public int ComputeDamage(int raw, Fighter attacker) {
            if (raw <= 0) return 0;
            float atk = attacker != null ? attacker.Const.Attack / 100f * attacker.AttackMul : 1f;
            float def = Const.Defence / 100f * DefenceMul;
            if (def <= 0f) def = 1f;
            return (int)Math.Floor(raw * atk / def);
        }

        /// <summary>Called on the attacker when its HitDef connects.</summary>
        public void RegisterHit(Fighter target, HitDef hd, bool guarded) {
            MoveContactFlag = 1;
            if (guarded) MoveGuardedFlag = 1; else MoveHitFlag = 1;
            if (!guarded) { HitCount++; UniqHitCount++; }
            Power = Math.Min(PowerMax, Power + (guarded ? hd.GuardGetPower : hd.HitGetPower));
            HitPauseTime = Math.Max(HitPauseTime, Math.Max(0, guarded ? hd.GuardPauseTime[0] : hd.PauseTime[0]));
            if (hd.HitOnce > 0) hd.Spent = true;
            if (target != null && !hd.Targets.Contains(target.Id)) hd.Targets.Add(target.Id);
            if (hd.P1Facing != 0 && target != null) Facing = hd.P1Facing > 0 ? Facing : -Facing;
            if (hd.P1GetP2Facing != 0 && target != null)
                Facing = hd.P1GetP2Facing > 0 ? target.Facing : -target.Facing;
            if (hd.P1StateNo >= 0) ChangeState(hd.P1StateNo, "p1stateno");
            if (target != null && !Targets.Contains(target)) Targets.Add(target);
            LastSparkNo = guarded ? hd.GuardSparkNo : hd.SparkNo;
            if (target != null) {
                // MUGEN: x from p2's front edge (negative = deeper into p2), y from p1's axis
                float front = target.Type == StateType.Air ? target.Const.AirFront : target.Const.GroundFront;
                LastSparkX = target.WorldX - Facing * front * target.Scl + Facing * hd.SparkXY[0] * Scl;
                LastSparkY = WorldY + hd.SparkXY[1] * Scl;
                if (Engine != null && LastSparkNo >= 0)
                    Engine.AddSpark(this, LastSparkNo, guarded ? hd.GuardSparkFromChar : hd.SparkFromChar, LastSparkX, LastSparkY, Facing);
                if (Engine != null && !guarded && hd.EnvShakeTime > 0)
                    Engine.EnvShake(hd.EnvShakeTime, hd.EnvShakeFreq, hd.EnvShakeAmpl, hd.EnvShakePhase);
            }
            var snd = guarded ? hd.GuardSound : hd.HitSound;
            bool ownSnd = guarded ? hd.GuardSoundFromChar : hd.HitSoundFromChar;
            if (snd[0] >= 0) QueueSound(!ownSnd, snd[0], snd[1]);
        }

        /// <summary>Restores the character's own state file after a `p2stateno` sequence ends.</summary>
        public void SelfStateRestore() { ForeignStates = null; }

        /// <summary>Resets everything a new round resets (char.go `roundInit`).</summary>
        public void ResetForRound(bool keepPower) {
            Life = Const.Life;
            if (!keepPower) Power = 0;
            KO = false;
            Ghv.Clear();
            ClearMoveHit();
            ClearHitDef();
            HitPauseTime = 0;
            HitCount = UniqHitCount = 0;
            JugglePoints = Const.AirJuggle;
            AttackMul = DefenceMul = 1f;
            VelX = VelY = 0f;
            AirJumpCount = 0;
            ForeignStates = null;
            StateOwner = null;
            Targets.Clear();
            PalFx.Clear();
            After.Time = 0; After.Frames.Clear();
            BindTimeLeft = 0; BoundTo = null;
            InitEx();
            foreach (var h in HitOverrides) h.Time = 0;
            foreach (var hb in HitBy) hb.Time = 0;
            ChangeState(0, "round reset");
            ChangeAnim(0);
        }
    }
}
