using System;

namespace IK.Core {
    /// <summary>
    /// The MUGEN common states (0-106) and the engine's hard-coded key handling.
    ///
    /// Why not read `common1.cns`? Elecbyte's file is not redistributable and Ikemen GO
    /// replaced it with its own script (`engine/ikemen-go/data/common1.cns.zss`), a different
    /// language. So these states are implemented here in C# with the same numbers, animations,
    /// constants and timings as that script, and <see cref="BasicActions"/> is a port of the
    /// hard-coded transitions of `char.go` (`actionPrepare`) that send a controllable
    /// character into walk / crouch / jump. A character that defines one of these states in
    /// its own .cns still wins — <see cref="Fighter"/> only calls in here for states it did
    /// not find.
    ///
    /// Animations follow the real common states, not the state numbers: jumping uses
    /// 41/42/43 (neutral / forward / back), landing uses 47. KFM has exactly those.
    /// </summary>
    public static class CommonStates {
        public const int Stand = 0;
        public const int StandToCrouch = 10, Crouch = 11, CrouchToStand = 12;
        public const int Walk = 20;
        public const int JumpStart = 40, AirJumpStart = 45, JumpUp = 50, JumpDown = 51, JumpLand = 52;
        public const int RunFwd = 100, HopBack = 105, HopBackLand = 106;

        public static bool IsCommon(int no) {
            switch (no) {
                case Stand: case StandToCrouch: case Crouch: case CrouchToStand: case Walk:
                case JumpStart: case AirJumpStart: case JumpUp: case JumpDown: case JumpLand:
                case RunFwd: case HopBack: case HopBackLand:
                    return true;
            }
            return false;
        }

        // ---- hard-coded keys (char.go: actionPrepare) --------------------------

        /// <summary>
        /// The transitions the engine itself performs for a character that has control:
        /// jump, air jump, crouch, stand up, walk and braking. Runs before state -1.
        /// </summary>
        public static void BasicActions(Fighter f) {
            var buf = f.Commands.Buffer;
            if (f.Ctrl) {
                if (!f.NoJump && f.Type == StateType.Standing && buf.Ub > 0) {
                    if (f.StateNo != JumpStart) f.ChangeState(JumpStart, "hardcoded: jump");
                } else if (!f.NoAirJump && f.Type == StateType.Air && buf.Ub == 1 &&
                           f.PosY <= -f.Const.AirJumpHeight && f.AirJumpCount < f.Const.AirJumpNum) {
                    if (f.StateNo != AirJumpStart || f.StateTime > 0) {
                        f.AirJumpCount++;
                        f.ChangeState(AirJumpStart, "hardcoded: air jump");
                    }
                } else if (!f.NoCrouch && f.Type == StateType.Standing && buf.Db > 0) {
                    if (f.StateNo != StandToCrouch) {
                        if (f.StateNo != RunFwd) f.VelX = 0f;
                        f.ChangeState(StandToCrouch, "hardcoded: crouch");
                    }
                } else if (!f.NoStand && f.Type == StateType.Crouching && buf.Db <= 0) {
                    if (f.StateNo != CrouchToStand) f.ChangeState(CrouchToStand, "hardcoded: stand up");
                } else if (!f.NoWalk && f.Type == StateType.Standing && (buf.Fb > 0) != (buf.Bb > 0)) {
                    if (f.StateNo != Walk) f.ChangeState(Walk, "hardcoded: walk");
                }
            }
            // braking does not need ctrl
            if (!f.NoBrake && f.StateNo == Walk && (buf.Bb > 0) == (buf.Fb > 0)) f.ChangeState(Stand, "hardcoded: brake");
            if (f.Type != StateType.Air) f.AirJumpCount = 0;
        }

        // ---- statedef parameters ------------------------------------------------

        /// <summary>Applied when a common state is entered (its `[Statedef]` line).</summary>
        public static void EnterCommon(Fighter f, int no) {
            // every common1 movement state is movetype I (statedef default, bytecode.go)
            if (IsCommon(no)) f.Move = MoveType.Idle;
            switch (no) {
                case Stand:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand;
                    break;
                case StandToCrouch:
                    f.Type = StateType.Crouching; f.Phys = Physics.Crouch; f.ChangeAnim(10);
                    break;
                case Crouch:
                    f.Type = StateType.Crouching; f.Phys = Physics.Crouch; f.ChangeAnim(11);
                    break;
                case CrouchToStand:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.ChangeAnim(12);
                    break;
                case Walk:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand;
                    break;
                case JumpStart:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.Ctrl = false; f.ChangeAnim(40);
                    break;
                case AirJumpStart:
                    f.Type = StateType.Air; f.Phys = Physics.None; f.Ctrl = false;
                    f.VelX = 0f; f.VelY = 0f;
                    f.ChangeAnim(f.HasAnim(44) ? 44 : 41);
                    break;
                case JumpUp:
                case JumpDown:
                    f.Type = StateType.Air; f.Phys = Physics.Air;
                    break;
                case JumpLand:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.Ctrl = false; f.ChangeAnim(47);
                    break;
                case RunFwd:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.ChangeAnim(100);
                    break;
                case HopBack:
                    f.Type = StateType.Air; f.Phys = Physics.Air; f.Ctrl = false; f.ChangeAnim(105);
                    break;
                case HopBackLand:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.Ctrl = false; f.ChangeAnim(47);
                    break;
            }
        }

        // ---- per-tick controllers -----------------------------------------------

        /// <summary>The controllers of the common state the character is in.</summary>
        public static void Apply(Fighter f) {
            if (f.States != null && f.States.Get(f.StateNo) != null) return;   // the character owns it
            var buf = f.Commands.Buffer;
            int t = f.StateTime;

            switch (f.StateNo) {
                case Stand:
                    if (f.AnimNo != 0 && !(f.AnimNo == 5 && AnimRunning(f))) f.ChangeAnim(0);
                    if (t == 0) f.VelY = 0f;
                    if (t == 4 || Math.Abs(f.VelX) < f.Const.StandFrictionThreshold) f.VelX = 0f;
                    break;

                case StandToCrouch:
                    if (t == 0) f.VelX *= 0.75f;
                    if (Math.Abs(f.VelX) < f.Const.CrouchFrictionThreshold) f.VelX = 0f;
                    if (AnimDone(f)) f.ChangeState(Crouch, "crouch anim finished");
                    break;

                case Crouch:
                    if (Math.Abs(f.VelX) < f.Const.CrouchFrictionThreshold) f.VelX = 0f;
                    break;

                case CrouchToStand:
                    if (AnimDone(f)) f.ChangeState(Stand, "stand-up anim finished");
                    break;

                case Walk:
                    if (buf.Bb > 0) f.VelX = f.Const.WalkBack;
                    else if (buf.Fb > 0) f.VelX = f.Const.WalkFwd;
                    if (f.VelX > 0f) { if (f.AnimNo != 20) f.ChangeAnim(20); }
                    else if (f.VelX < 0f) { if (f.AnimNo != 21) f.ChangeAnim(f.HasAnim(21) ? 21 : 20); }
                    break;

                case JumpStart:
                    if (t == 0) f.SysVar1 = 0;
                    if (buf.Bb > 0) f.SysVar1 = -1;
                    else if (buf.Fb > 0) f.SysVar1 = 1;
                    if (AnimDone(f)) {
                        float x = f.SysVar1 == 0 ? f.Const.JumpNeuX
                                : f.SysVar1 > 0 ? (f.PrevStateNo == RunFwd ? f.Const.RunJumpFwdX : f.Const.JumpFwd)
                                : f.Const.JumpBack;
                        f.VelX = x;
                        f.VelY = f.Const.JumpNeuY;
                        f.ChangeState(JumpUp, "jump launched");
                        f.Ctrl = true;
                    }
                    break;

                case AirJumpStart:
                    if (t == 0) f.SysVar1 = 0;
                    if (buf.Bb > 0) f.SysVar1 = -1;
                    else if (buf.Fb > 0) f.SysVar1 = 1;
                    if (t == 2) {
                        f.VelX = f.SysVar1 == 0 ? f.Const.JumpNeuX
                               : f.SysVar1 > 0 ? f.Const.AirJumpFwd : f.Const.AirJumpBack;
                        f.VelY = f.Const.AirJumpNeuY;
                        f.ChangeState(JumpUp, "air jump launched");
                        f.Ctrl = true;
                    }
                    break;

                case JumpUp:
                    if (t == 0) {
                        f.SysVar1 = 0;
                        f.ChangeAnim(f.VelX == 0f ? 41 : f.VelX > 0f ? 42 : 43);
                    }
                    if (f.VelY > -2f && f.AnimNo >= 41 && f.AnimNo <= 43 && f.HasAnim(f.AnimNo + 3))
                        f.ChangeAnim(f.AnimNo + 3);
                    if (f.VelY > 0f && f.PosY >= 0f) f.ChangeState(JumpLand, "landed");
                    break;

                case JumpDown:
                    if (f.VelY > 0f && f.PosY >= 0f) f.ChangeState(JumpLand, "landed");
                    break;

                case JumpLand:
                    if (t == 0) { f.VelY = 0f; f.PosY = 0f; }
                    else if (t == 3) f.Ctrl = true;
                    if (Math.Abs(f.VelX) < f.Const.StandFrictionThreshold) f.VelX = 0f;
                    if (AnimDone(f)) { f.ChangeState(Stand, "landing anim finished"); f.Ctrl = true; }
                    break;

                case RunFwd:
                    f.VelX = f.Const.RunFwdX;
                    f.AssertSpecial("nowalk");
                    f.AssertSpecial("noautoturn");
                    if (buf.Fb <= 0) f.ChangeState(Stand, "run released");
                    break;

                case HopBack:
                    if (t == 0) { f.VelX = f.Const.RunBackX; f.VelY = f.Const.RunBackY; }
                    else if (t == 2) f.Ctrl = true;
                    if (f.VelY > 0f && f.PosY >= 0f) f.ChangeState(HopBackLand, "hop landed");
                    break;

                case HopBackLand:
                    if (Math.Abs(f.VelX) < f.Const.StandFrictionThreshold) f.VelX = 0f;
                    if (t == 0) { f.VelY = 0f; f.PosY = 0f; }
                    else if (t == 7) { f.ChangeState(Stand, "hop landing finished"); f.Ctrl = true; }
                    break;
            }
        }

        /// <summary>`AnimTime = 0`: the action is on its last tick.</summary>
        static bool AnimDone(Fighter f) => f.Anim != null && f.Anim.AnimTime == 0;
        static bool AnimRunning(Fighter f) => f.Anim != null && f.Anim.AnimTime < 0;
    }
}
