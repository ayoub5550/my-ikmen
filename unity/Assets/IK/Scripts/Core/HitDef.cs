using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>How the hit character reacts (`animtype`). The value is the offset added to the
    /// base get-hit animation, which is why the order matters.</summary>
    public enum Reaction { Light = 0, Medium = 1, Hard = 2, Back = 3, Up = 4, DiagUp = 5, Unknown = -1 }

    /// <summary>`ground.type` / `air.type`.</summary>
    public enum HitKind { Unknown = -1, None = 0, High = 1, Low = 2, Trip = 3 }

    /// <summary>`priority` second parameter.</summary>
    public enum TradeType { Hit, Miss, Dodge }

    /// <summary>Attribute bits of a HitDef: the state type it comes from and the attack class.</summary>
    [Flags]
    public enum HitAttr {
        None = 0,
        // state type the attack is made from (first attribute field: S, C, A)
        StandAttack = 1 << 0, CrouchAttack = 1 << 1, AirAttack = 1 << 2,
        // attack class: Normal / Special / Hyper
        Normal = 1 << 3, Special = 1 << 4, Hyper = 1 << 5,
        // attack kind: Attack / Throw / Projectile
        Attack = 1 << 6, Throw = 1 << 7, Projectile = 1 << 8,
        StateMask = StandAttack | CrouchAttack | AirAttack,
        ClassMask = Normal | Special | Hyper,
        KindMask = Attack | Throw | Projectile,
    }

    /// <summary>`hitflag`: which state types of the receiver this attack connects with.</summary>
    [Flags]
    public enum HitFlag {
        None = 0,
        High = 1 << 0,        // H: receiver standing
        Low = 1 << 1,         // L: receiver crouching
        Air = 1 << 2,         // A: receiver in the air
        Down = 1 << 3,        // D: receiver lying down
        Fall = 1 << 4,        // F: can hit a falling receiver
        MustNotBeHit = 1 << 5,// -: only connects if the receiver is not already being hit
        MustBeHit = 1 << 6,   // +: only connects if the receiver is already being hit
    }

    /// <summary>`guardflag`: which guard state types can block this attack.</summary>
    [Flags]
    public enum GuardFlag { None = 0, High = 1 << 0, Low = 1 << 1, Air = 1 << 2 }

    /// <summary>
    /// One `HitDef` state controller, parsed into values the fight engine can apply.
    ///
    /// Port of the `HitDef` struct and its defaults in `engine/ikemen-go/src/char.go`
    /// (`HitDef.reset` / `finalizeParams`), restricted to the MUGEN 1.0 parameter set that the
    /// shipped characters use. Fields the engine does not simulate yet (sparks, palette effects,
    /// environment shake, snapping) are parsed and stored so they can be used later without
    /// another parsing pass, and <see cref="Unsupported"/> lists any key that was not understood.
    /// </summary>
    public class HitDef {
        public const int IErr = int.MinValue;      // "not set" marker, Ikemen's IErr
        public const float FErr = float.NaN;

        public HitAttr Attr;
        public HitFlag Flag = HitFlag.High | HitFlag.Low | HitFlag.Air | HitFlag.Fall;
        public GuardFlag Guard = GuardFlag.None;
        /// <summary>-1 = friendly fire only, 0 = both, 1 = enemies (default).</summary>
        public int AffectTeam = 1;

        public Reaction AnimType = Reaction.Light;
        public Reaction AirAnimType = Reaction.Unknown;
        public Reaction FallAnimType = Reaction.Unknown;

        public int Priority = 4;
        public TradeType PriorityType = TradeType.Hit;

        public int HitDamage, GuardDamage;
        public int[] PauseTime = { 0, 0 };          // p1 (attacker), p2 (receiver) shake
        public int[] GuardPauseTime = { IErr, IErr };

        public int SparkNo = IErr, GuardSparkNo = IErr;
        public float[] SparkXY = { 0f, 0f };
        public int[] HitSound = { -1, 0 }, GuardSound = { -1, 0 };
        /// <summary>dev.5: `S` prefix — spark from the attacker's own .air / sound from its own
        /// .snd. Without it sparks come from fightfx and sounds from fight.snd.</summary>
        public bool SparkFromChar, GuardSparkFromChar, HitSoundFromChar, GuardSoundFromChar;
        /// <summary>dev.5: `envshake.*` (on hit) and `fall.envshake.*` (when the target lands).</summary>
        public int EnvShakeTime, FallEnvShakeTime;
        public float EnvShakeFreq = 60f, EnvShakeAmpl = -4f, EnvShakePhase = float.NaN;
        public float FallEnvShakeFreq = 60f, FallEnvShakeAmpl = -4f, FallEnvShakePhase = float.NaN;
        /// <summary>dev.5: `palfx.*` applied to the target on hit.</summary>
        public PalFx HitPalFx;

        public HitKind GroundType = HitKind.High;
        public HitKind AirType = HitKind.Unknown;

        public int GroundSlideTime, GuardSlideTime = IErr;
        public int GroundHitTime, GuardHitTime = IErr;
        public int AirHitTime = 20, DownHitTime = 20;
        public int GuardCtrlTime = IErr, AirGuardCtrlTime = IErr;

        public float[] GuardDistX = { 0f, 0f }, GuardDistY = { 0f, 0f };

        public float XAccel, YAccel = 0.35f;
        public float[] GroundVelocity = { 0f, 0f };
        public float[] GuardVelocity = { FErr, 0f };
        public float[] AirVelocity = { 0f, 0f };
        public float[] AirGuardVelocity = { FErr, FErr };
        public float[] DownVelocity = { FErr, FErr };

        public float GroundCornerPush = FErr, AirCornerPush = FErr, DownCornerPush = FErr;
        public float GuardCornerPush = FErr, AirGuardCornerPush = FErr;

        public int AirJuggle = IErr;
        public int P1SprPriority = IErr, P2SprPriority = 0;
        public int P1StateNo = -1, P2StateNo = -1;
        public bool P2GetP1State = true;
        public int P1GetP2Facing, P1Facing, P2Facing;
        public int ForceStand = IErr, ForceCrouch = IErr;

        public bool GroundFall, DownBounce;
        public int AirFall = IErr;
        public bool FallRecover = true;
        public int FallRecoverTime = 4;
        public int FallDamage;
        public float FallXVelocity = FErr, FallYVelocity = -4.5f;
        public bool FallKill = true, Kill = true, GuardKill = true;

        public int Id, ChainId = -1;
        public readonly List<int> NoChainId = new List<int>();
        public int NumHits = 1;
        public int HitOnce = -1;
        public int HitGetPower = IErr, GuardGetPower = IErr;
        public int HitGivePower = IErr, GuardGivePower = IErr;

        /// <summary>Keys of the controller this parser did not understand (reported, never silent).</summary>
        public readonly List<string> Unsupported = new List<string>();

        /// <summary>True once the HitDef has connected and `hitonce` forbids a second contact.</summary>
        public bool Spent;
        /// <summary>Ids of the characters this HitDef already connected with.</summary>
        public readonly List<int> Targets = new List<int>();

        public bool IsValid => Attr != HitAttr.None;

        // ---- parsing -----------------------------------------------------------

        /// <summary>
        /// Reads the parameters of a `HitDef` controller. `eval` evaluates an expression in the
        /// context of the attacker, so triggers such as `ifelse(var(1),…)` work inside a HitDef.
        /// </summary>
        public static HitDef Read(StateController c, Func<string, float> eval, CharConstants cns) {
            var hd = new HitDef();
            if (cns != null) {
                hd.SparkNo = cns.SparkNo;
                hd.GuardSparkNo = cns.GuardSparkNo;
                hd.AirJuggle = IErr;
            }
            foreach (var kv in c.Params) {
                string key = kv.Key.Trim().ToLowerInvariant();
                string val = kv.Value;
                switch (key) {
                    case "attr": hd.Attr = ParseAttr(val); break;
                    case "hitflag": hd.Flag = ParseHitFlag(val); break;
                    case "guardflag": hd.Guard = ParseGuardFlag(val); break;
                    case "affectteam": hd.AffectTeam = ParseAffectTeam(val); break;
                    case "animtype": hd.AnimType = ParseReaction(val, Reaction.Light); break;
                    case "air.animtype": hd.AirAnimType = ParseReaction(val, Reaction.Unknown); break;
                    case "fall.animtype": hd.FallAnimType = ParseReaction(val, Reaction.Unknown); break;
                    case "priority": {
                        var p = MugenDef.SplitCsv(val);
                        if (p.Length > 0 && p[0].Length > 0) hd.Priority = (int)Math.Round(eval(p[0]));
                        if (p.Length > 1 && p[1].Length > 0) hd.PriorityType = ParseTradeType(p[1]);
                        break;
                    }
                    case "damage": {
                        var p = MugenDef.SplitCsv(val);
                        if (p.Length > 0 && p[0].Length > 0) hd.HitDamage = (int)Math.Round(eval(p[0]));
                        hd.GuardDamage = p.Length > 1 && p[1].Length > 0 ? (int)Math.Round(eval(p[1])) : 0;
                        break;
                    }
                    case "pausetime": ReadInts(val, eval, hd.PauseTime); break;
                    case "guard.pausetime": ReadInts(val, eval, hd.GuardPauseTime); break;
                    case "sparkno": hd.SparkNo = ReadSparkNo(val, eval, hd.SparkNo); hd.SparkFromChar = HasPrefix(val, 'S'); break;
                    case "guard.sparkno": hd.GuardSparkNo = ReadSparkNo(val, eval, hd.GuardSparkNo); hd.GuardSparkFromChar = HasPrefix(val, 'S'); break;
                    case "sparkxy": ReadFloats(val, eval, hd.SparkXY); break;
                    case "hitsound": hd.HitSoundFromChar = ReadSound(val, eval, hd.HitSound); break;
                    case "guardsound": hd.GuardSoundFromChar = ReadSound(val, eval, hd.GuardSound); break;
                    case "envshake.time": hd.EnvShakeTime = (int)Math.Round(eval(val)); break;
                    case "envshake.freq": hd.EnvShakeFreq = eval(val); break;
                    case "envshake.ampl": hd.EnvShakeAmpl = eval(val); break;
                    case "envshake.phase": hd.EnvShakePhase = eval(val); break;
                    case "fall.envshake.time": hd.FallEnvShakeTime = (int)Math.Round(eval(val)); break;
                    case "fall.envshake.freq": hd.FallEnvShakeFreq = eval(val); break;
                    case "fall.envshake.ampl": hd.FallEnvShakeAmpl = eval(val); break;
                    case "fall.envshake.phase": hd.FallEnvShakePhase = eval(val); break;
                    case "palfx.time": case "palfx.mul": case "palfx.add": case "palfx.color":
                    case "palfx.sinadd": case "palfx.invertall":
                        if (hd.HitPalFx == null) hd.HitPalFx = new PalFx();
                        hd.HitPalFx.ReadParam(key.Substring(6), val, eval);
                        break;
                    case "ground.type": hd.GroundType = ParseHitKind(val, HitKind.High); break;
                    case "air.type": hd.AirType = ParseHitKind(val, HitKind.Unknown); break;
                    case "ground.slidetime": hd.GroundSlideTime = (int)Math.Round(eval(val)); break;
                    case "guard.slidetime": hd.GuardSlideTime = (int)Math.Round(eval(val)); break;
                    case "ground.hittime": hd.GroundHitTime = (int)Math.Round(eval(val)); break;
                    case "guard.hittime": hd.GuardHitTime = (int)Math.Round(eval(val)); break;
                    case "air.hittime": hd.AirHitTime = (int)Math.Round(eval(val)); break;
                    case "down.hittime": hd.DownHitTime = (int)Math.Round(eval(val)); break;
                    case "guard.ctrltime": hd.GuardCtrlTime = (int)Math.Round(eval(val)); break;
                    case "airguard.ctrltime": hd.AirGuardCtrlTime = (int)Math.Round(eval(val)); break;
                    case "guard.dist": ReadFloats(val, eval, hd.GuardDistX); break;
                    case "guard.dist.x": ReadFloats(val, eval, hd.GuardDistX); break;
                    case "guard.dist.y": ReadFloats(val, eval, hd.GuardDistY); break;
                    case "xaccel": hd.XAccel = eval(val); break;
                    case "yaccel": hd.YAccel = eval(val); break;
                    case "ground.velocity": ReadFloats(val, eval, hd.GroundVelocity); break;
                    case "guard.velocity": ReadFloats(val, eval, hd.GuardVelocity); break;
                    case "air.velocity": ReadFloats(val, eval, hd.AirVelocity); break;
                    case "airguard.velocity": ReadFloats(val, eval, hd.AirGuardVelocity); break;
                    case "down.velocity": ReadFloats(val, eval, hd.DownVelocity); break;
                    case "ground.cornerpush.veloff": hd.GroundCornerPush = eval(val); break;
                    case "air.cornerpush.veloff": hd.AirCornerPush = eval(val); break;
                    case "down.cornerpush.veloff": hd.DownCornerPush = eval(val); break;
                    case "guard.cornerpush.veloff": hd.GuardCornerPush = eval(val); break;
                    case "airguard.cornerpush.veloff": hd.AirGuardCornerPush = eval(val); break;
                    case "air.juggle": hd.AirJuggle = (int)Math.Round(eval(val)); break;
                    case "p1sprpriority": hd.P1SprPriority = (int)Math.Round(eval(val)); break;
                    case "p2sprpriority": hd.P2SprPriority = (int)Math.Round(eval(val)); break;
                    case "p1stateno": hd.P1StateNo = (int)Math.Round(eval(val)); break;
                    case "p2stateno": hd.P2StateNo = (int)Math.Round(eval(val)); break;
                    case "p2getp1state": hd.P2GetP1State = eval(val) != 0f; break;
                    case "p1getp2facing": hd.P1GetP2Facing = (int)Math.Round(eval(val)); break;
                    case "p1facing": hd.P1Facing = (int)Math.Round(eval(val)); break;
                    case "p2facing": hd.P2Facing = (int)Math.Round(eval(val)); break;
                    case "forcestand": hd.ForceStand = eval(val) != 0f ? 1 : 0; break;
                    case "forcecrouch": hd.ForceCrouch = eval(val) != 0f ? 1 : 0; break;
                    case "fall": hd.GroundFall = eval(val) != 0f; break;
                    case "air.fall": hd.AirFall = eval(val) != 0f ? 1 : 0; break;
                    case "fall.recover": hd.FallRecover = eval(val) != 0f; break;
                    case "fall.recovertime": hd.FallRecoverTime = (int)Math.Round(eval(val)); break;
                    case "fall.damage": hd.FallDamage = (int)Math.Round(eval(val)); break;
                    case "fall.xvelocity": hd.FallXVelocity = eval(val); break;
                    case "fall.yvelocity": hd.FallYVelocity = eval(val); break;
                    case "fall.kill": hd.FallKill = eval(val) != 0f; break;
                    case "kill": hd.Kill = eval(val) != 0f; break;
                    case "guard.kill": hd.GuardKill = eval(val) != 0f; break;
                    case "down.bounce": hd.DownBounce = eval(val) != 0f; break;
                    case "id": hd.Id = (int)Math.Round(eval(val)); break;
                    case "chainid": hd.ChainId = (int)Math.Round(eval(val)); break;
                    case "nochainid": {
                        foreach (var part in MugenDef.SplitCsv(val))
                            if (part.Length > 0) hd.NoChainId.Add((int)Math.Round(eval(part)));
                        break;
                    }
                    case "numhits": hd.NumHits = (int)Math.Round(eval(val)); break;
                    case "hitonce": hd.HitOnce = eval(val) != 0f ? 1 : 0; break;
                    case "getpower": {
                        var p = MugenDef.SplitCsv(val);
                        if (p.Length > 0 && p[0].Length > 0) hd.HitGetPower = (int)Math.Round(eval(p[0]));
                        if (p.Length > 1 && p[1].Length > 0) hd.GuardGetPower = (int)Math.Round(eval(p[1]));
                        break;
                    }
                    case "givepower": {
                        var p = MugenDef.SplitCsv(val);
                        if (p.Length > 0 && p[0].Length > 0) hd.HitGivePower = (int)Math.Round(eval(p[0]));
                        if (p.Length > 1 && p[1].Length > 0) hd.GuardGivePower = (int)Math.Round(eval(p[1]));
                        break;
                    }
                    // known but not simulated yet: recorded so nothing is silently dropped
                    case "mindist": case "maxdist": case "snap": case "snaptime":
                    case "p1facing.x": case "persistent": case "ignorehitpause":
                    case "attack.width": case "attack.depth": case "sparkangle": case "sparkscale":
                    case "guard.sparkangle": case "guard.sparkscale":
                    case "hitsound.channel": case "guardsound.channel":
                    case "dizzypoints": case "guardpoints": case "redlife": case "score":
                    case "forcenofall": case "fall.envshake.mul": case "envshake.mul":
                    case "guard.dist.z": case "attack.z.width": case "p2clsncheck": case "p2clsnrequire":
                        break;
                    default:
                        if (!key.StartsWith("trigger") && key != "type" && key.Length > 0)
                            hd.Unsupported.Add(key);
                        break;
                }
            }
            hd.Finalize(cns);
            return hd;
        }

        /// <summary>
        /// Fills in the parameters that default to another parameter's value, following
        /// `HitDef.finalizeParams` in char.go: the guard values copy the hit values, the air
        /// reaction copies the ground reaction, and `air.type` copies `ground.type`.
        /// </summary>
        public void Finalize(CharConstants cns) {
            if (GuardPauseTime[0] == IErr) GuardPauseTime[0] = PauseTime[0];
            if (GuardPauseTime[1] == IErr) GuardPauseTime[1] = PauseTime[1];
            if (GuardHitTime == IErr) GuardHitTime = GroundHitTime;
            if (GuardSlideTime == IErr) GuardSlideTime = GuardHitTime;
            if (GuardCtrlTime == IErr) GuardCtrlTime = GuardHitTime;
            if (AirGuardCtrlTime == IErr) AirGuardCtrlTime = GuardCtrlTime;
            if (AirAnimType == Reaction.Unknown) AirAnimType = AnimType;
            if (FallAnimType == Reaction.Unknown)
                FallAnimType = AirAnimType == Reaction.Up ? Reaction.Up : Reaction.Back;
            if (AirType == HitKind.Unknown) AirType = GroundType;
            if (AirFall == IErr) AirFall = GroundFall ? 1 : 0;
            if (float.IsNaN(GuardVelocity[0])) GuardVelocity[0] = GroundVelocity[0];
            if (float.IsNaN(AirGuardVelocity[0])) AirGuardVelocity[0] = AirVelocity[0] * 1.5f;
            if (float.IsNaN(AirGuardVelocity[1])) AirGuardVelocity[1] = AirVelocity[1] * 0.5f;
            if (float.IsNaN(DownVelocity[0])) DownVelocity[0] = AirVelocity[0];
            if (float.IsNaN(DownVelocity[1])) DownVelocity[1] = AirVelocity[1];
            if (AirJuggle == IErr) AirJuggle = 0;
            if (HitOnce < 0) HitOnce = (Attr & HitAttr.Throw) != 0 ? 1 : 0;
            if (P1SprPriority == IErr) P1SprPriority = 1;
            // corner push defaults (char.go): normal attacks push, throws and guards push less
            if (float.IsNaN(GroundCornerPush)) GroundCornerPush = (Attr & HitAttr.Throw) != 0 ? 0f : 1.5f * GuardVelocity[0];
            if (float.IsNaN(AirCornerPush)) AirCornerPush = GroundCornerPush;
            if (float.IsNaN(DownCornerPush)) DownCornerPush = GroundCornerPush;
            if (float.IsNaN(GuardCornerPush)) GuardCornerPush = GroundCornerPush;
            if (float.IsNaN(AirGuardCornerPush)) AirGuardCornerPush = GuardCornerPush;
            if (cns != null && HitGetPower == IErr) HitGetPower = (int)(HitDamage * cns.PowerMultiplierHit);
            if (cns != null && GuardGetPower == IErr) GuardGetPower = (int)(HitDamage * cns.PowerMultiplierGuard);
            if (cns != null && HitGivePower == IErr) HitGivePower = (int)(HitDamage * cns.PowerMultiplierGive);
            if (cns != null && GuardGivePower == IErr) GuardGivePower = (int)(HitDamage * cns.PowerMultiplierGuardGive);
            if (HitGetPower == IErr) HitGetPower = 0;
            if (GuardGetPower == IErr) GuardGetPower = 0;
            if (HitGivePower == IErr) HitGivePower = 0;
            if (GuardGivePower == IErr) GuardGivePower = 0;
        }

        // ---- attribute helpers ---------------------------------------------------

        /// <summary>`attr = S, NA` → state type flags plus one class/kind pair per code.</summary>
        public static HitAttr ParseAttr(string s) {
            var result = HitAttr.None;
            if (string.IsNullOrEmpty(s)) return result;
            var parts = MugenDef.SplitCsv(s);
            if (parts.Length > 0) {
                foreach (char ch in parts[0]) {
                    switch (char.ToUpperInvariant(ch)) {
                        case 'S': result |= HitAttr.StandAttack; break;
                        case 'C': result |= HitAttr.CrouchAttack; break;
                        case 'A': result |= HitAttr.AirAttack; break;
                    }
                }
            }
            for (int i = 1; i < parts.Length; i++) {
                string code = parts[i].Trim().ToUpperInvariant();
                if (code.Length < 2) continue;
                switch (code[0]) {
                    case 'N': result |= HitAttr.Normal; break;
                    case 'S': result |= HitAttr.Special; break;
                    case 'H': case 'U': result |= HitAttr.Hyper; break;
                }
                switch (code[1]) {
                    case 'A': result |= HitAttr.Attack; break;
                    case 'T': result |= HitAttr.Throw; break;
                    case 'P': result |= HitAttr.Projectile; break;
                }
            }
            return result;
        }

        public static HitFlag ParseHitFlag(string s) {
            var f = HitFlag.None;
            if (string.IsNullOrEmpty(s)) return HitFlag.High | HitFlag.Low | HitFlag.Air | HitFlag.Fall;
            foreach (char ch in s) {
                switch (char.ToUpperInvariant(ch)) {
                    case 'H': f |= HitFlag.High; break;
                    case 'L': f |= HitFlag.Low; break;
                    case 'M': f |= HitFlag.High | HitFlag.Low; break;
                    case 'A': f |= HitFlag.Air; break;
                    case 'D': f |= HitFlag.Down; break;
                    case 'F': f |= HitFlag.Fall; break;
                    case '-': f |= HitFlag.MustNotBeHit; break;
                    case '+': f |= HitFlag.MustBeHit; break;
                }
            }
            return f;
        }

        public static GuardFlag ParseGuardFlag(string s) {
            var f = GuardFlag.None;
            if (string.IsNullOrEmpty(s)) return f;
            foreach (char ch in s) {
                switch (char.ToUpperInvariant(ch)) {
                    case 'H': f |= GuardFlag.High; break;
                    case 'L': f |= GuardFlag.Low; break;
                    case 'M': f |= GuardFlag.High | GuardFlag.Low; break;
                    case 'A': f |= GuardFlag.Air; break;
                }
            }
            return f;
        }

        static int ParseAffectTeam(string s) {
            if (string.IsNullOrEmpty(s)) return 1;
            switch (char.ToUpperInvariant(s.Trim()[0])) {
                case 'F': return -1;   // friendly
                case 'B': return 0;    // both
                default: return 1;     // enemy
            }
        }

        public static Reaction ParseReaction(string s, Reaction fallback) {
            if (string.IsNullOrEmpty(s)) return fallback;
            switch (s.Trim().ToLowerInvariant()) {
                case "light": return Reaction.Light;
                case "medium": case "med": return Reaction.Medium;
                case "hard": case "heavy": return Reaction.Hard;
                case "back": return Reaction.Back;
                case "up": return Reaction.Up;
                case "diagup": return Reaction.DiagUp;
            }
            return fallback;
        }

        public static HitKind ParseHitKind(string s, HitKind fallback) {
            if (string.IsNullOrEmpty(s)) return fallback;
            switch (s.Trim().ToLowerInvariant()) {
                case "none": return HitKind.None;
                case "high": return HitKind.High;
                case "low": return HitKind.Low;
                case "trip": return HitKind.Trip;
            }
            return fallback;
        }

        static TradeType ParseTradeType(string s) {
            if (string.IsNullOrEmpty(s)) return TradeType.Hit;
            switch (s.Trim().ToLowerInvariant()) {
                case "miss": return TradeType.Miss;
                case "dodge": return TradeType.Dodge;
                default: return TradeType.Hit;
            }
        }

        static int ReadSparkNo(string val, Func<string, float> eval, int fallback) {
            if (string.IsNullOrWhiteSpace(val)) return fallback;
            string v = val.Trim();
            // "S10" means a spark from the character's own animations
            if (v.Length > 1 && (v[0] == 'S' || v[0] == 's' || v[0] == 'F' || v[0] == 'f')) {
                int n;
                if (int.TryParse(v.Substring(1).Trim(), out n)) return n;
            }
            return (int)Math.Round(eval(v));
        }

        static bool HasPrefix(string val, char p) {
            if (string.IsNullOrWhiteSpace(val)) return false;
            var v = val.TrimStart();
            return v.Length > 1 && char.ToUpperInvariant(v[0]) == p && (char.IsDigit(v[1]) || v[1] == ' ' || v[1] == '-');
        }

        /// <summary>`hitsound = S5, 0` / `5, 0`; returns true for the `S` (own .snd) prefix.
        /// `F` is accepted too and means fight.snd, the default.</summary>
        public static bool ReadSound(string val, Func<string, float> eval, int[] into) {
            var parts = MugenDef.SplitCsv(val);
            bool own = false;
            for (int i = 0; i < into.Length && i < parts.Length; i++) {
                string p = parts[i].Trim();
                if (i == 0 && p.Length > 1 && (p[0] == 'S' || p[0] == 's') && !char.IsLetter(p[1])) { own = true; p = p.Substring(1); }
                else if (i == 0 && p.Length > 1 && (p[0] == 'F' || p[0] == 'f') && !char.IsLetter(p[1])) p = p.Substring(1);
                if (p.Length > 0) into[i] = (int)Math.Round(eval(p));
            }
            return own;
        }

        static void ReadInts(string val, Func<string, float> eval, int[] into) {
            var parts = MugenDef.SplitCsv(val);
            for (int i = 0; i < into.Length && i < parts.Length; i++)
                if (parts[i].Length > 0) into[i] = (int)Math.Round(eval(parts[i]));
        }

        static void ReadFloats(string val, Func<string, float> eval, float[] into) {
            var parts = MugenDef.SplitCsv(val);
            for (int i = 0; i < into.Length && i < parts.Length; i++)
                if (parts[i].Length > 0) into[i] = eval(parts[i]);
        }

        /// <summary>`hitdefattr = SCA, NA, SA` style comparison used by the trigger of the same name.</summary>
        public bool MatchesAttr(HitAttr stateMask, HitAttr kindMask) {
            if ((Attr & HitAttr.StateMask & stateMask) == 0) return false;
            var wanted = kindMask & (HitAttr.ClassMask | HitAttr.KindMask);
            if (wanted == HitAttr.None) return true;
            // every requested class/kind pair must be matched by this HitDef
            var classWanted = wanted & HitAttr.ClassMask;
            var kindWanted = wanted & HitAttr.KindMask;
            if (classWanted != HitAttr.None && (Attr & classWanted) == 0) return false;
            if (kindWanted != HitAttr.None && (Attr & kindWanted) == 0) return false;
            return true;
        }
    }
}
