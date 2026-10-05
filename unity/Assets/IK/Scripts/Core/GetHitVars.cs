using System;

namespace IK.Core {
    /// <summary>
    /// What a character remembers about the hit it is currently taking: the MUGEN `GetHitVar`
    /// table. Written by <see cref="Fighter.ApplyHit"/> when a HitDef connects and read by the
    /// get-hit states and by the `gethitvar(...)` trigger.
    ///
    /// Port of the `GetHitVar` struct of `engine/ikemen-go/src/char.go`, MUGEN 1.0 subset.
    /// </summary>
    public class GetHitVars {
        public HitAttr Attr;
        public Reaction AnimType = Reaction.Light;
        public HitKind GroundType = HitKind.High;
        public HitKind AirType = HitKind.High;

        public int Damage;
        public int HitCount;
        public int FallCount;
        /// <summary>Ticks the receiver still shakes for (`pausetime[1]`): animation frozen.</summary>
        public int HitShakeTime;
        /// <summary>Ticks of hit stun left (`hittime`).</summary>
        public int HitTime;
        public int SlideTime;
        public int CtrlTime;
        public int HitId = -1;
        public int ChainId = -1;
        public int PlayerId = -1;

        public float XVel, YVel, XAccel, YAccel;
        public float XOff, YOff;

        public bool Guarded;
        public bool Fall;
        public bool FallFlag;              // `hitfall`: this hit knocks the receiver down
        public int FallDamage;
        public float FallXVel, FallYVel;
        public bool FallRecover = true;
        public int FallRecoverTime = 4;
        public bool FallKill = true;
        public bool Down;                  // lying on the ground
        public bool DownBounce;
        public int HitPower;
        public int JugglePoints;
        public bool IsBound;
        /// <summary>dev.5: the HitDef's `fall.envshake.*`, played by FallEnvShake.</summary>
        public int FallEnvShakeTime;
        public float FallEnvShakeFreq = 60f, FallEnvShakeAmpl = -4f, FallEnvShakePhase = float.NaN;
        /// <summary>Attacker's facing at the moment of the hit (`p2facing` handling).</summary>
        public int AttackerFacing = 1;

        public void Clear() {
            Attr = HitAttr.None;
            AnimType = Reaction.Light;
            GroundType = AirType = HitKind.High;
            Damage = HitCount = FallCount = 0;
            HitShakeTime = HitTime = SlideTime = CtrlTime = 0;
            HitId = ChainId = PlayerId = -1;
            XVel = YVel = XAccel = YAccel = XOff = YOff = 0f;
            Guarded = Fall = FallFlag = Down = DownBounce = IsBound = false;
            FallDamage = 0;
            FallXVel = FallYVel = 0f;
            FallRecover = true;
            FallRecoverTime = 4;
            FallKill = true;
            HitPower = 0;
            JugglePoints = 0;
            AttackerFacing = 1;
        }

        /// <summary>`gethitvar(name)`. Unknown names return 0 and are reported by the caller.</summary>
        public bool TryGet(string name, out float value) {
            value = 0f;
            if (string.IsNullOrEmpty(name)) return false;
            switch (name.Trim().ToLowerInvariant()) {
                case "animtype": value = (int)AnimType; return true;
                case "air.animtype": value = (int)AnimType; return true;
                case "ground.animtype": value = (int)AnimType; return true;
                case "groundtype": case "ground.type": value = (int)GroundType; return true;
                case "airtype": case "air.type": value = (int)AirType; return true;
                case "damage": value = Damage; return true;
                case "hitcount": value = HitCount; return true;
                case "fallcount": value = FallCount; return true;
                case "hitshaketime": value = HitShakeTime; return true;
                case "hittime": value = HitTime; return true;
                case "slidetime": value = SlideTime; return true;
                case "ctrltime": value = CtrlTime; return true;
                case "recovertime": value = FallRecoverTime; return true;
                case "xoff": value = XOff; return true;
                case "yoff": value = YOff; return true;
                case "xvel": value = XVel; return true;
                case "yvel": value = YVel; return true;
                case "xaccel": value = XAccel; return true;
                case "yaccel": value = YAccel; return true;
                case "chainid": value = ChainId; return true;
                case "hitid": case "id": value = HitId; return true;
                case "guarded": value = Guarded ? 1 : 0; return true;
                case "isbound": value = IsBound ? 1 : 0; return true;
                case "fall": value = Fall ? 1 : 0; return true;
                case "fall.damage": value = FallDamage; return true;
                case "fall.xvel": value = FallXVel; return true;
                case "fall.yvel": value = FallYVel; return true;
                case "fall.recover": value = FallRecover ? 1 : 0; return true;
                case "fall.recovertime": value = FallRecoverTime; return true;
                case "fall.kill": value = FallKill ? 1 : 0; return true;
                case "fall.envshake.time": value = 0; return true;
                case "down.bounce": case "fall.bounce": value = DownBounce ? 1 : 0; return true;
                case "hitpower": value = HitPower; return true;
                case "playerno": case "playerid": value = PlayerId; return true;
            }
            return false;
        }
    }
}
