using System;

namespace IK.Core {
    /// <summary>
    /// The guard states (120-155) and the get-hit states (5000-5150).
    ///
    /// Same approach as <see cref="CommonStates"/>: Elecbyte's `common1.cns` is not
    /// redistributable, and Ikemen GO replaced it with a script in its own language
    /// (`engine/ikemen-go/data/common1.cns.zss`). These states are therefore implemented
    /// natively in C# with the same state numbers, animations, transitions and timings, and a
    /// character that defines one of them in its own .cns still wins (<see cref="Fighter"/>
    /// only calls in here for states the character does not define). KFM defines none of them.
    ///
    /// Coordinates are MUGEN's: y grows downwards, the ground is y = 0, so a character in the
    /// air has a negative y and `pos y >= air.gethit.groundlevel` is the engine's own way of
    /// asking "has the falling body reached the floor".
    /// </summary>
    public static class HitStates {
        // guard
        public const int GuardStart = 120;
        public const int StandGuard = 130, CrouchGuard = 131, AirGuard = 132;
        public const int GuardEnd = 140;
        public const int StandGuardShake = 150, StandGuardKnock = 151;
        public const int CrouchGuardShake = 152, CrouchGuardKnock = 153;
        public const int AirGuardShake = 154, AirGuardKnock = 155;
        // get hit
        public const int StandShake = 5000, StandKnock = 5001;
        public const int CrouchShake = 5010, CrouchKnock = 5011;
        public const int AirShake = 5020, AirKnocked = 5030, AirTransition = 5035;
        public const int AirRecover = 5040, AirFalling = 5050;
        public const int TripShake = 5070, TripKnock = 5071;
        public const int DownShake = 5080, DownKnock = 5081;
        public const int HitGround = 5100, GroundBounce = 5101;
        public const int LieDown = 5110, GetUp = 5120, Defeated = 5150;

        public static bool IsGuardState(int no) =>
            no == GuardStart || no == StandGuard || no == CrouchGuard || no == AirGuard ||
            no == GuardEnd || (no >= StandGuardShake && no <= AirGuardKnock);

        public static bool IsGetHitState(int no) =>
            no == StandShake || no == StandKnock || no == CrouchShake || no == CrouchKnock ||
            no == AirShake || no == AirKnocked || no == AirTransition || no == AirRecover ||
            no == AirFalling || no == TripShake || no == TripKnock || no == DownShake ||
            no == DownKnock || no == HitGround || no == GroundBounce || no == LieDown ||
            no == GetUp || no == Defeated;

        public static bool IsHandled(int no) => IsGuardState(no) || IsGetHitState(no);

        /// <summary>Which shaking state a hit sends the character to, by its state type.</summary>
        public static int GetHitShakeState(Fighter f) {
            if (f.Type == StateType.LieDown) return DownShake;
            if (f.Type == StateType.Air) return AirShake;
            if (f.Type == StateType.Crouching) return CrouchShake;
            if (f.Ghv.GroundType == HitKind.Trip) return TripShake;
            return StandShake;
        }

        /// <summary>Which guard-hit state a guarded hit sends the character to.</summary>
        public static int GuardShakeState(Fighter f) {
            if (f.Type == StateType.Air) return AirGuardShake;
            if (f.Type == StateType.Crouching) return CrouchGuardShake;
            return StandGuardShake;
        }

        /// <summary>Which guarding state the character holds while blocking.</summary>
        public static int GuardStateFor(Fighter f) {
            if (f.Type == StateType.Air) return AirGuard;
            if (f.Type == StateType.Crouching) return CrouchGuard;
            return StandGuard;
        }

        // ---- statedef parameters -------------------------------------------------

        public static void Enter(Fighter f, int no) {
            switch (no) {
                case GuardStart:
                    SetPhysicsFromType(f);
                    f.ChangeAnim(120 + TypeOffset(f));
                    break;
                case StandGuard:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.ChangeAnim(130);
                    break;
                case CrouchGuard:
                    f.Type = StateType.Crouching; f.Phys = Physics.Crouch; f.ChangeAnim(131);
                    break;
                case AirGuard:
                    f.Type = StateType.Air; f.Phys = Physics.None; f.ChangeAnim(132);
                    break;
                case GuardEnd:
                    SetPhysicsFromType(f);
                    f.Ctrl = true;
                    f.ChangeAnim(140 + TypeOffset(f));
                    break;

                case StandGuardShake:
                    f.Type = StateType.Standing; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    f.VelX = f.VelY = 0f; f.ChangeAnim(150);
                    break;
                case StandGuardKnock:
                    f.Type = StateType.Standing; f.Move = MoveType.BeingHit; f.Phys = Physics.Stand;
                    f.ChangeAnim(150);
                    break;
                case CrouchGuardShake:
                    f.Type = StateType.Crouching; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    f.VelX = f.VelY = 0f; f.ChangeAnim(151);
                    break;
                case CrouchGuardKnock:
                    f.Type = StateType.Crouching; f.Move = MoveType.BeingHit; f.Phys = Physics.Crouch;
                    f.ChangeAnim(151);
                    break;
                case AirGuardShake:
                    f.Type = StateType.Air; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    f.VelX = f.VelY = 0f; f.ChangeAnim(152);
                    break;
                case AirGuardKnock:
                    f.Type = StateType.Air; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    f.ChangeAnim(152);
                    break;

                case StandShake:
                    f.Type = StateType.Standing; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    f.VelX = f.VelY = 0f; f.Ctrl = false;
                    f.ChangeAnim(HitAnim(f, f.Ghv.GroundType == HitKind.High ? 5000 : 5010));
                    break;
                case StandKnock:
                    f.Type = StateType.Standing; f.Move = MoveType.BeingHit; f.Phys = Physics.Stand;
                    break;
                case CrouchShake:
                    f.Type = StateType.Crouching; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    f.VelX = f.VelY = 0f; f.Ctrl = false;
                    f.ChangeAnim(HitAnim(f, 5020));
                    break;
                case CrouchKnock:
                    f.Type = StateType.Crouching; f.Move = MoveType.BeingHit; f.Phys = Physics.Crouch;
                    break;
                case AirShake:
                    f.Type = StateType.Air; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    f.VelX = f.VelY = 0f; f.Ctrl = false;
                    f.ChangeAnim(HitAnim(f, f.Ghv.AirType == HitKind.High ? 5000 : 5010));
                    break;
                case AirKnocked:
                    f.Type = StateType.Air; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    f.Ctrl = false;
                    if (f.HasAnim(5030)) f.ChangeAnim(5030);
                    break;
                case AirTransition:
                    f.Type = StateType.Air; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    if (f.HasAnim(5035)) f.ChangeAnim(5035);
                    break;
                case AirRecover:
                    f.Type = StateType.Air; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    if (f.HasAnim(5040)) f.ChangeAnim(5040);
                    break;
                case AirFalling:
                    f.Type = StateType.Air; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    if (f.HasAnim(5050)) f.ChangeAnim(5050);
                    break;
                case TripShake:
                    f.Type = StateType.Air; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    f.VelX = f.VelY = 0f;
                    if (f.HasAnim(5070)) f.ChangeAnim(5070);
                    break;
                case TripKnock:
                    f.Type = StateType.Air; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    break;
                case DownShake:
                    f.Type = StateType.LieDown; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    f.VelX = f.VelY = 0f;
                    if (f.HasAnim(5080)) f.ChangeAnim(5080);
                    break;
                case DownKnock:
                    f.Type = StateType.LieDown; f.Move = MoveType.BeingHit; f.Phys = Physics.Crouch;
                    break;
                case HitGround:
                case GroundBounce:
                case LieDown:
                case Defeated:
                    f.Type = StateType.LieDown; f.Move = MoveType.BeingHit; f.Phys = Physics.None;
                    break;
                case GetUp:
                    f.Type = StateType.LieDown; f.Move = MoveType.Idle; f.Phys = Physics.None;
                    break;
            }
        }

        // ---- per-tick controllers -------------------------------------------------

        public static void Apply(Fighter f) {
            if (f.States != null && f.States.Get(f.StateNo) != null) return;   // the character owns it
            int t = f.StateTime;
            var ghv = f.Ghv;
            var buf = f.Commands.Buffer;

            switch (f.StateNo) {
                // ---- guarding -------------------------------------------------
                case GuardStart:
                    if (t == 0) { SetPhysicsFromType(f); f.ChangeAnim(120 + TypeOffset(f)); }
                    if (AnimDone(f)) f.ChangeState(130 + TypeOffset(f), "guard started");
                    break;

                case StandGuard:
                    if (f.AnimNo != 130) f.ChangeAnim(130);
                    if (buf.Db > 0) f.ChangeState(CrouchGuard, "guard low");
                    if (!Guarding(f)) f.ChangeState(GuardEnd, "stopped guarding");
                    break;

                case CrouchGuard:
                    if (f.AnimNo != 131) f.ChangeAnim(131);
                    if (buf.Db <= 0) f.ChangeState(StandGuard, "guard high");
                    if (!Guarding(f)) f.ChangeState(GuardEnd, "stopped guarding");
                    break;

                case AirGuard:
                    if (f.AnimNo != 132) f.ChangeAnim(132);
                    AirGuardLand(f);
                    if (f.StateNo == AirGuard && !Guarding(f)) f.ChangeState(GuardEnd, "stopped guarding");
                    break;

                case GuardEnd:
                    if (t == 0) { SetPhysicsFromType(f); f.ChangeAnim(140 + TypeOffset(f)); f.Ctrl = true; }
                    if (AnimDone(f)) { f.ChangeState(CommonStates.Stand, "guard end finished"); f.Ctrl = true; }
                    break;

                // ---- guard hit ------------------------------------------------
                case StandGuardShake:
                    if (f.AnimNo != 150) f.ChangeAnim(150);
                    if (f.HitShakeOver) f.ChangeState(buf.Db > 0 ? CrouchGuardKnock : StandGuardKnock, "guard shake over");
                    break;
                case CrouchGuardShake:
                    if (f.AnimNo != 151) f.ChangeAnim(151);
                    if (f.HitShakeOver) f.ChangeState(buf.Db > 0 ? CrouchGuardKnock : StandGuardKnock, "guard shake over");
                    break;
                case AirGuardShake:
                    if (f.AnimNo != 152) f.ChangeAnim(152);
                    if (f.HitShakeOver) f.ChangeState(AirGuardKnock, "air guard shake over");
                    break;

                case StandGuardKnock:
                case CrouchGuardKnock:
                    GuardKnockedBack(f, f.StateNo == StandGuardKnock ? StandGuard : CrouchGuard);
                    break;

                case AirGuardKnock:
                    if (t == 0) { f.VelX = ghv.XVel; f.VelY = ghv.YVel; }
                    if (t == ghv.CtrlTime) f.Ctrl = true;
                    AirGuardLand(f);
                    break;

                // ---- get hit: shaking -----------------------------------------
                case StandShake:
                    if ((t == 0 && (ghv.YVel != 0f || ghv.Fall)) || f.PosY != 0f) f.Type = StateType.Air;
                    if (f.HitShakeOver)
                        f.ChangeState(ghv.YVel != 0f || ghv.Fall ? AirKnocked : StandKnock, "hit shake over");
                    break;

                case CrouchShake:
                    if ((t == 0 && (ghv.YVel != 0f || ghv.Fall)) || f.PosY != 0f) f.Type = StateType.Air;
                    if (f.HitShakeOver)
                        f.ChangeState(ghv.YVel != 0f || ghv.Fall ? AirKnocked : CrouchKnock, "hit shake over");
                    break;

                case AirShake:
                    if (f.HitShakeOver) f.ChangeState(AirKnocked, "air hit shake over");
                    break;

                case TripShake:
                    if (f.HitShakeOver) f.ChangeState(TripKnock, "trip shake over");
                    break;

                case DownShake:
                    if (f.HitShakeOver) f.ChangeState(ghv.YVel == 0f ? DownKnock : AirKnocked, "down shake over");
                    break;

                // ---- get hit: knocked back on the ground -----------------------
                case StandKnock:
                case CrouchKnock: {
                    bool crouch = f.StateNo == CrouchKnock;
                    if (t == 0) f.VelX = ghv.XVel;
                    if (AnimDone(f))
                        f.ChangeAnim(HitAnim(f, crouch ? 5025 : (ghv.GroundType == HitKind.Low ? 5015 : 5005)));
                    if (t >= ghv.SlideTime) f.VelX *= 0.6f;
                    if (f.HitOver) {
                        f.VelX = 0f;
                        f.DefenceMul = 1f;
                        f.Move = MoveType.Idle;
                        f.ChangeState(crouch ? CommonStates.Crouch : CommonStates.Stand, "hit over");
                        f.Ctrl = true;
                    }
                    break;
                }

                case DownKnock:
                    if (t == 0) f.VelX = ghv.XVel;
                    if (f.HitOver) { f.VelX = 0f; f.ChangeState(LieDown, "down hit over"); }
                    break;

                // ---- get hit: in the air ---------------------------------------
                case AirKnocked:
                    if (t == 0) { f.VelX = ghv.XVel; f.VelY = ghv.YVel; }
                    else { f.VelY += ghv.YAccel; f.VelX += ghv.XAccel; }
                    if (f.HitOver || (f.VelY > 0f && f.PosY >= f.Const.AirGetHitGroundLevel))
                        f.ChangeState(f.HitFall ? AirFalling : AirRecover, "air knock resolved");
                    else if (AnimDone(f)) f.ChangeState(AirTransition, "air knock anim over");
                    break;

                case AirTransition:
                    if (t > 0) f.VelY += ghv.YAccel;
                    if (f.HitOver || AnimDone(f) || (f.VelY > 0f && f.PosY >= f.Const.AirGetHitGroundLevel))
                        f.ChangeState(f.HitFall ? AirFalling : AirRecover, "air transition over");
                    break;

                case AirRecover:
                    if (!f.Alive) f.ChangeState(AirFalling, "ko while recovering");
                    if (t > 0) f.VelY += ghv.YAccel;
                    if (f.HitOver) { f.Ctrl = true; f.Move = MoveType.Idle; }
                    if (f.VelY > 0f && f.PosY >= 0f) {
                        f.PosY = 0f; f.VelY = 0f;
                        f.ChangeState(CommonStates.JumpLand, "landed after air hit");
                    }
                    break;

                case AirFalling:
                    if (t > 0) f.VelY += ghv.YAccel;
                    if (f.VelY > 0f && f.PosY >= f.Const.AirGetHitGroundLevel)
                        f.ChangeState(HitGround, "fell to the ground");
                    break;

                case TripKnock:
                    if (t == 0) { f.VelX = ghv.XVel; f.VelY = ghv.YVel; }
                    else f.VelY += ghv.YAccel;
                    if (f.VelY > 0f && f.PosY >= f.Const.AirGetHitTripGroundLevel)
                        f.ChangeState(LieDown, "tripped to the ground");
                    break;

                // ---- get hit: on the ground ------------------------------------
                case HitGround:
                    if (t == 0) {
                        if (f.HasAnim(5100)) f.ChangeAnim(5100);
                        f.PosY = 0f;
                        f.VelY = 0f;
                        f.VelX *= 0.75f;
                        if (ghv.FallYVel == 0f) { f.ChangeState(LieDown, "no bounce"); break; }
                    }
                    if (t == 3 && ghv.FallDamage > 0) {
                        f.Life = Math.Max(ghv.FallKill ? 0 : 1, f.Life - ghv.FallDamage);
                        if (f.Life <= 0) f.KO = true;
                    }
                    f.VelX = 0f;    // posfreeze
                    if (AnimDone(f)) f.ChangeState(GroundBounce, "ground hit anim over");
                    break;

                case GroundBounce:
                    if (t == 0) {
                        if (f.HasAnim(5160)) f.ChangeAnim(5160);
                        f.VelY = ghv.FallYVel;
                        if (!float.IsNaN(ghv.FallXVel)) f.VelX = ghv.FallXVel;
                        f.PosY = f.Const.DownBounceOffsetY;
                        f.PosX += f.Const.DownBounceOffsetX * f.Facing;
                    } else f.VelY += f.Const.DownBounceYAccel;
                    if (f.VelY > 0f && f.PosY >= f.Const.DownBounceGroundLevel)
                        f.ChangeState(LieDown, "bounce finished");
                    break;

                case LieDown:
                    if (t == 0) {
                        if (f.HasAnim(5110)) f.ChangeAnim(5110);
                        f.PosY = 0f;
                        f.VelY = 0f;
                        if (ghv.FallDamage > 0 && f.PrevStateNo != HitGround) {
                            f.Life = Math.Max(ghv.FallKill ? 0 : 1, f.Life - ghv.FallDamage);
                            if (f.Life <= 0) f.KO = true;
                        }
                    }
                    f.VelX *= 0.85f;
                    if (Math.Abs(f.VelX) < f.Const.DownFrictionThreshold) f.VelX = 0f;
                    if (!f.Alive) { f.ChangeState(Defeated, "knocked out"); break; }
                    if (t >= f.Const.LieDownTime) f.ChangeState(GetUp, "lie-down time over");
                    break;

                case GetUp:
                    if (t == 0) {
                        if (f.HasAnim(5120)) f.ChangeAnim(5120);
                        f.VelX = 0f;
                        f.Move = MoveType.Idle;
                    }
                    f.UnhittableTime = Math.Max(f.UnhittableTime, 1);
                    if (AnimDone(f)) {
                        f.Ghv.FallFlag = false;
                        f.UnhittableTime = Math.Max(f.UnhittableTime, 3);
                        f.ChangeState(CommonStates.Stand, "got up");
                        f.Ctrl = true;
                    }
                    break;

                case Defeated:
                    if (t == 0 && f.HasAnim(5140)) f.ChangeAnim(5140);
                    f.VelX *= 0.85f;
                    if (Math.Abs(f.VelX) < f.Const.DownFrictionThreshold) f.VelX = 0f;
                    f.SprPriority = -3;
                    f.Ctrl = false;
                    break;
            }
        }

        // ---- helpers -----------------------------------------------------------

        /// <summary>The guard-hit knock-back shared by states 151 and 153.</summary>
        static void GuardKnockedBack(Fighter f, int nextState) {
            int t = f.StateTime;
            var ghv = f.Ghv;
            if (t == 0) f.VelX = ghv.XVel;
            if (t == ghv.SlideTime || f.HitOver) f.VelX = 0f;
            if (t == ghv.CtrlTime) f.Ctrl = true;
            if (f.HitOver) { f.ChangeState(nextState, "guard hit over"); f.Ctrl = true; }
        }

        /// <summary>Landing out of an air guard (states 132 and 155).</summary>
        static void AirGuardLand(Fighter f) {
            f.VelY += f.Const.YAccel;
            if (f.PosY >= 0f && f.VelY > 0f) {
                f.VelY = 0f;
                f.PosY = 0f;
                bool keepGuarding = f.Commands.Buffer.Bb > 0 && f.InGuardDist;
                f.ChangeState(keepGuarding ? StandGuard : CommonStates.JumpLand, "air guard landed");
            }
        }

        /// <summary>Is the character still holding the guard direction against a live threat?</summary>
        static bool Guarding(Fighter f) => f.Commands.Buffer.Bb > 0 && f.InGuardDist;

        /// <summary>`anim = base + getHitVar(animtype)`, falling back when the action is missing.</summary>
        static int HitAnim(Fighter f, int baseAnim) {
            int wanted = baseAnim + (int)f.Ghv.AnimType;
            if (f.HasAnim(wanted)) return wanted;
            if (f.HasAnim(baseAnim)) return baseAnim;
            return f.AnimNo;
        }

        static int TypeOffset(Fighter f) =>
            f.Type == StateType.Crouching ? 1 : f.Type == StateType.Air ? 2 : 0;

        static void SetPhysicsFromType(Fighter f) {
            if (f.Type == StateType.Standing) f.Phys = Physics.Stand;
            else if (f.Type == StateType.Crouching) f.Phys = Physics.Crouch;
            else if (f.Type == StateType.Air) f.Phys = Physics.Air;
        }

        static bool AnimDone(Fighter f) => f.Anim != null && f.Anim.AnimTime == 0;
    }
}
