using System;

namespace IK.Core {
    /// <summary>
    /// The MUGEN common movement states (0-109), implemented natively.
    ///
    /// Why not read `common1.cns`? Elecbyte's file is not redistributable and Ikemen GO
    /// replaced it with its own ZSS script (`data/common1.cns.zss`), a different language.
    /// So the standard states are implemented here with the same numbers, animations and
    /// constants a character expects (`[Velocity]` / `[Movement]` from its own .cns), and a
    /// character that defines one of these states itself still wins — <see cref="Fighter"/>
    /// only calls into this class for states it did not find in the CNS files.
    ///
    /// Implemented: 0 stand · 5 turn · 6 crouch turn · 10 stand→crouch · 11 crouch ·
    /// 12 crouch→stand · 20 walk · 40 jump start · 50 jump up · 52 jump land ·
    /// 100 run forward · 105 hop back · 106 hop back land.
    /// </summary>
    public static class CommonStates {
        public const int Stand = 0, Turn = 5, CrouchTurn = 6;
        public const int StandToCrouch = 10, Crouch = 11, CrouchToStand = 12;
        public const int Walk = 20, JumpStart = 40, JumpUp = 50, JumpLand = 52;
        public const int RunFwd = 100, HopBack = 105, HopBackLand = 106;

        /// <summary>Is this state number one of the common movement states?</summary>
        public static bool IsCommon(int no) =>
            no == Stand || no == Turn || no == CrouchTurn || no == StandToCrouch || no == Crouch ||
            no == CrouchToStand || no == Walk || no == JumpStart || no == JumpUp || no == JumpLand ||
            no == RunFwd || no == HopBack || no == HopBackLand;

        /// <summary>Statedef parameters of a common state, applied when it is entered.</summary>
        public static void EnterCommon(Fighter f, int no) {
            switch (no) {
                case Stand:
                    f.Type = StateType.Standing; f.Move = MoveType.Idle; f.Phys = Physics.Stand;
                    f.Ctrl = true; f.ChangeAnim(0);
                    break;
                case Turn:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.Ctrl = false; f.ChangeAnim(5);
                    break;
                case CrouchTurn:
                    f.Type = StateType.Crouching; f.Phys = Physics.Crouch; f.Ctrl = false; f.ChangeAnim(6);
                    break;
                case StandToCrouch:
                    f.Type = StateType.Crouching; f.Phys = Physics.Crouch; f.Ctrl = false; f.ChangeAnim(10);
                    break;
                case Crouch:
                    f.Type = StateType.Crouching; f.Phys = Physics.Crouch; f.Ctrl = true; f.ChangeAnim(11);
                    break;
                case CrouchToStand:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.Ctrl = false; f.ChangeAnim(12);
                    break;
                case Walk:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.Ctrl = true;
                    break;
                case JumpStart:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.Ctrl = false; f.ChangeAnim(40);
                    break;
                case JumpUp:
                    f.Type = StateType.Air; f.Phys = Physics.Air; f.Ctrl = true; f.ChangeAnim(50);
                    break;
                case JumpLand:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.Ctrl = false; f.ChangeAnim(52);
                    break;
                case RunFwd:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.Ctrl = false; f.ChangeAnim(100);
                    break;
                case HopBack:
                    f.Type = StateType.Air; f.Phys = Physics.Air; f.Ctrl = false; f.ChangeAnim(105);
                    f.VelX = f.Const.RunBackX; f.VelY = f.Const.RunBackY;
                    break;
                case HopBackLand:
                    f.Type = StateType.Standing; f.Phys = Physics.Stand; f.Ctrl = false; f.ChangeAnim(106);
                    break;
            }
        }

        /// <summary>Per-tick behaviour of the common states, run after the CNS controllers.</summary>
        public static void Apply(Fighter f) {
            // A character that implements the state itself owns it completely.
            if (f.States?.Get(f.StateNo) != null) return;

            switch (f.StateNo) {
                case Stand: Standing(f); break;
                case Turn: AfterAnim(f, Stand); break;
                case CrouchTurn: AfterAnim(f, Crouch); break;
                case StandToCrouch: AfterAnim(f, Crouch); break;
                case Crouch: Crouching(f); break;
                case CrouchToStand: AfterAnim(f, Stand); break;
                case Walk: Walking(f); break;
                case JumpStart: JumpStarting(f); break;
                case JumpUp: InAir(f); break;
                case JumpLand: AfterAnim(f, Stand); break;
                case RunFwd: Running(f); break;
                case HopBack:
                    if (f.OnGround && f.VelY >= 0f) { f.VelX = 0; f.VelY = 0; f.ChangeState(HopBackLand, "landed"); }
                    break;
                case HopBackLand: AfterAnim(f, Stand); break;
            }
        }

        static void AfterAnim(Fighter f, int next) {
            if (f.Anim == null || f.Anim.TotalTime < 0) { if (f.StateTime > 12) f.ChangeState(next, "timeout"); return; }
            if (f.Anim.Time >= f.Anim.TotalTime) f.ChangeState(next, "animation finished");
        }

        static void Standing(Fighter f) {
            if (!f.Ctrl) return;
            if (f.Held(CmdKey.U)) { f.ChangeState(JumpStart, "up pressed"); return; }
            if (f.Held(CmdKey.D)) { f.ChangeState(StandToCrouch, "down pressed"); return; }
            if (f.Held(CmdKey.F) || f.Held(CmdKey.B)) { f.ChangeState(Walk, "direction held"); return; }
            if (f.AnimNo != 0) f.ChangeAnim(0);
        }

        static void Crouching(Fighter f) {
            if (!f.Ctrl) return;
            if (!f.Held(CmdKey.D)) { f.ChangeState(CrouchToStand, "down released"); return; }
            if (f.AnimNo != 11) f.ChangeAnim(11);
        }

        static void Walking(Fighter f) {
            if (!f.Ctrl) return;
            if (f.Held(CmdKey.U)) { f.ChangeState(JumpStart, "up pressed"); return; }
            if (f.Held(CmdKey.D)) { f.ChangeState(StandToCrouch, "down pressed"); return; }
            if (f.Held(CmdKey.F)) {
                f.VelX = f.Const.WalkFwd;
                if (f.AnimNo != 20) f.ChangeAnim(20);
            } else if (f.Held(CmdKey.B)) {
                f.VelX = f.Const.WalkBack;
                if (f.AnimNo != 21) f.ChangeAnim(f.Character?.Air?.Get(21) != null ? 21 : 20);
            } else {
                f.VelX = 0f;
                f.ChangeState(Stand, "direction released");
            }
        }

        /// <summary>State 40: the crouch before the jump, then the launch velocity.</summary>
        static void JumpStarting(Fighter f) {
            if (f.StateTime < 3) return;                       // MUGEN's 3-tick jump start
            float x = 0f;
            if (f.Held(CmdKey.F)) x = f.Const.JumpFwd;
            else if (f.Held(CmdKey.B)) x = f.Const.JumpBack;
            f.VelX = x;
            f.VelY = f.Const.JumpNeuY;
            f.AirJumpsLeft = f.Const.AirJumpNum;
            f.ChangeState(JumpUp, "jump launched");
        }

        static void InAir(Fighter f) {
            if (f.VelY > 0f && f.OnGround) {
                f.VelX = 0f; f.VelY = 0f;
                f.ChangeState(JumpLand, "landed");
                return;
            }
            // air jump
            if (f.AirJumpsLeft > 0 && f.Pressed(CmdKey.U) && f.PosY < -f.Const.AirJumpHeight) {
                f.AirJumpsLeft--;
                f.VelY = f.Const.AirJumpNeuY;
                if (f.Held(CmdKey.F)) f.VelX = f.Const.AirJumpFwd;
                else if (f.Held(CmdKey.B)) f.VelX = f.Const.AirJumpBack;
                f.ChangeAnim(50);
            }
            // the jump animation has an up part (anim 50) and a down part (anim 51)
            if (f.VelY > 0f && f.AnimNo != 51 && f.Character?.Air?.Get(51) != null) f.ChangeAnim(51);
        }

        static void Running(Fighter f) {
            f.VelX = f.Const.RunFwdX;
            if (!f.Held(CmdKey.F)) f.ChangeState(Stand, "run released");
        }
    }
}
