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

        // ---- dev.4: the constants the hit system needs -------------------------
        /// <summary>`[Data] fall.defence_up`: percentage the defence rises by after a knockdown.</summary>
        public float FallDefenceUp = 50f;
        /// <summary>`[Data] liedown.time`: ticks spent lying down before getting up.</summary>
        public int LieDownTime = 60;
        /// <summary>`[Data] airjuggle`: juggle points the character starts a combo with.</summary>
        public int AirJuggle = 15;
        /// <summary>`[Data] sparkno` / `guard.sparkno`: the HitDef defaults.</summary>
        public int SparkNo = 2, GuardSparkNo = 40;

        /// <summary>`[Size] attack.dist`: how far in front a guardable attack is felt (`inguarddist`).</summary>
        public float AttackDist = 160f;
        /// <summary>`[Size] attack.z.width` is ignored (2D); `[Size] attack.width` is not a MUGEN 1.0 key.</summary>
        public float ProjAttackDist = 90f;

        /// <summary>`[Movement] air.gethit.groundlevel`: y at which a falling character lands.</summary>
        public float AirGetHitGroundLevel = 25f;
        public float AirGetHitGroundRecoverGroundThreshold = -20f;
        public float AirGetHitGroundRecoverGroundLevel = 10f;
        public float AirGetHitAirRecoverThreshold = -1f;
        public float AirGetHitAirRecoverYAccel = 0.35f;
        public float AirGetHitTripGroundLevel = 15f;
        public float DownBounceOffsetX = 0f, DownBounceOffsetY = 20f;
        public float DownBounceYAccel = 0.4f;
        public float DownBounceGroundLevel = 12f;
        public float DownFrictionThreshold = 0.05f;

        // Power the attacker and the receiver gain, as fractions of the damage dealt
        // (MUGEN: getpower defaults to damage, givepower to damage/2; the guard variants halve them).
        public float PowerMultiplierHit = 1f, PowerMultiplierGuard = 0.5f;
        public float PowerMultiplierGive = 0.5f, PowerMultiplierGuardGive = 0.25f;

        public static CharConstants From(MugenDef def) {
            var c = new CharConstants();
            if (def == null) return c;
            var data = def["data"];
            if (data != null) {
                c.Life = data.GetInt("life", c.Life);
                c.Attack = data.GetInt("attack", c.Attack);
                c.Defence = data.GetInt("defence", c.Defence);
                c.FallDefenceUp = data.GetFloat("fall.defence_up", c.FallDefenceUp);
                c.LieDownTime = data.GetInt("liedown.time", c.LieDownTime);
                c.AirJuggle = data.GetInt("airjuggle", c.AirJuggle);
                c.SparkNo = data.GetInt("sparkno", c.SparkNo);
                c.GuardSparkNo = data.GetInt("guard.sparkno", c.GuardSparkNo);
            }
            var size = def["size"];
            if (size != null) {
                c.GroundBack = size.GetFloat("ground.back", c.GroundBack);
                c.GroundFront = size.GetFloat("ground.front", c.GroundFront);
                c.AirBack = size.GetFloat("air.back", c.AirBack);
                c.AirFront = size.GetFloat("air.front", c.AirFront);
                c.Height = size.GetFloat("height", c.Height);
                c.AttackDist = size.GetFloat("attack.dist", c.AttackDist);
                c.ProjAttackDist = size.GetFloat("proj.attack.dist", c.ProjAttackDist);
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
                c.AirGetHitGroundLevel = mov.GetFloat("air.gethit.groundlevel", c.AirGetHitGroundLevel);
                c.AirGetHitGroundRecoverGroundThreshold =
                    mov.GetFloat("air.gethit.groundrecover.ground.threshold", c.AirGetHitGroundRecoverGroundThreshold);
                c.AirGetHitGroundRecoverGroundLevel =
                    mov.GetFloat("air.gethit.groundrecover.groundlevel", c.AirGetHitGroundRecoverGroundLevel);
                c.AirGetHitAirRecoverThreshold =
                    mov.GetFloat("air.gethit.airrecover.threshold", c.AirGetHitAirRecoverThreshold);
                c.AirGetHitAirRecoverYAccel =
                    mov.GetFloat("air.gethit.airrecover.yaccel", c.AirGetHitAirRecoverYAccel);
                c.AirGetHitTripGroundLevel = mov.GetFloat("air.gethit.trip.groundlevel", c.AirGetHitTripGroundLevel);
                Pair(mov.Get("down.bounce.offset"), ref c.DownBounceOffsetX, ref c.DownBounceOffsetY);
                c.DownBounceYAccel = mov.GetFloat("down.bounce.yaccel", c.DownBounceYAccel);
                c.DownBounceGroundLevel = mov.GetFloat("down.bounce.groundlevel", c.DownBounceGroundLevel);
                c.DownFrictionThreshold = mov.GetFloat("down.friction.threshold", c.DownFrictionThreshold);
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
