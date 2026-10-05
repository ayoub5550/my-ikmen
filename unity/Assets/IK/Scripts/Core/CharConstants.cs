using System;

namespace IK.Core {
    /// <summary>
    /// The `[Data]`, `[Size]`, `[Velocity]` and `[Movement]` constants of a character's .cns.
    /// Values are in MUGEN units per tick, exactly as the file writes them.
    /// </summary>
    public class CharConstants {
        public int Life = 1000, Attack = 100, Defence = 100;
        public float GroundBack = 15, GroundFront = 16, AirBack = 12, AirFront = 12, Height = 60;

        public float WalkFwd = 2.4f, WalkBack = -2.2f;
        public float RunFwdX = 4.6f, RunFwdY = 0f;
        public float RunBackX = -4.5f, RunBackY = -3.8f;
        public float JumpNeuX = 0f, JumpNeuY = -8.4f;
        public float JumpBack = -2.55f, JumpFwd = 2.5f;
        public float RunJumpFwdX = 4f, RunJumpFwdY = -8.1f;
        public float RunJumpBackX = -2.55f, RunJumpBackY = -8.1f;
        public float AirJumpNeuY = -8.1f, AirJumpBack = -2.55f, AirJumpFwd = 2.5f;

        public int AirJumpNum = 1;
        public float AirJumpHeight = 35f;
        public float YAccel = 0.44f;
        public float StandFriction = 0.85f, CrouchFriction = 0.82f;
        public float StandFrictionThreshold = 2f, CrouchFrictionThreshold = 0.05f;

        public static CharConstants From(MugenDef def) {
            var c = new CharConstants();
            if (def == null) return c;
            var data = def["data"];
            if (data != null) {
                c.Life = data.GetInt("life", c.Life);
                c.Attack = data.GetInt("attack", c.Attack);
                c.Defence = data.GetInt("defence", c.Defence);
            }
            var size = def["size"];
            if (size != null) {
                c.GroundBack = size.GetFloat("ground.back", c.GroundBack);
                c.GroundFront = size.GetFloat("ground.front", c.GroundFront);
                c.AirBack = size.GetFloat("air.back", c.AirBack);
                c.AirFront = size.GetFloat("air.front", c.AirFront);
                c.Height = size.GetFloat("height", c.Height);
            }
            var vel = def["velocity"];
            if (vel != null) {
                c.WalkFwd = vel.GetFloat("walk.fwd", c.WalkFwd);
                c.WalkBack = vel.GetFloat("walk.back", c.WalkBack);
                Pair(vel.Get("run.fwd"), ref c.RunFwdX, ref c.RunFwdY);
                Pair(vel.Get("run.back"), ref c.RunBackX, ref c.RunBackY);
                Pair(vel.Get("jump.neu"), ref c.JumpNeuX, ref c.JumpNeuY);
                c.JumpBack = vel.GetFloat("jump.back", c.JumpBack);
                c.JumpFwd = vel.GetFloat("jump.fwd", c.JumpFwd);
                Pair(vel.Get("runjump.fwd"), ref c.RunJumpFwdX, ref c.RunJumpFwdY);
                Pair(vel.Get("runjump.back"), ref c.RunJumpBackX, ref c.RunJumpBackY);
                float dummy = 0f;
                Pair(vel.Get("airjump.neu"), ref dummy, ref c.AirJumpNeuY);
                c.AirJumpBack = vel.GetFloat("airjump.back", c.AirJumpBack);
                c.AirJumpFwd = vel.GetFloat("airjump.fwd", c.AirJumpFwd);
            }
            var mov = def["movement"];
            if (mov != null) {
                c.AirJumpNum = mov.GetInt("airjump.num", c.AirJumpNum);
                c.AirJumpHeight = mov.GetFloat("airjump.height", c.AirJumpHeight);
                c.YAccel = mov.GetFloat("yaccel", c.YAccel);
                c.StandFriction = mov.GetFloat("stand.friction", c.StandFriction);
                c.CrouchFriction = mov.GetFloat("crouch.friction", c.CrouchFriction);
                c.StandFrictionThreshold = mov.GetFloat("stand.friction.threshold", c.StandFrictionThreshold);
                c.CrouchFrictionThreshold = mov.GetFloat("crouch.friction.threshold", c.CrouchFrictionThreshold);
            }
            return c;
        }

        static void Pair(string value, ref float x, ref float y) {
            if (string.IsNullOrEmpty(value)) return;
            var parts = MugenDef.SplitCsv(value);
            if (parts.Length >= 1 && MugenDef.IsNumeric(parts[0])) x = MugenDef.Atof(parts[0]);
            if (parts.Length >= 2 && MugenDef.IsNumeric(parts[1])) y = MugenDef.Atof(parts[1]);
        }
    }
}
