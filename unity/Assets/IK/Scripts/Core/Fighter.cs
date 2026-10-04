using System;
using System.Collections.Generic;

namespace IK.Core {
    public enum StateType { Standing, Crouching, Air, LieDown, Unchanged }
    public enum MoveType { Idle, Attack, BeingHit, Unchanged }
    public enum Physics { Stand, Crouch, Air, None, Unchanged }

    /// <summary>
    /// A character running its own CNS states: the dev.3 engine.
    ///
    /// One <see cref="Tick"/> is one MUGEN frame (60 Hz) and does what the engine does:
    /// state -1 (the .cmd file) → the current state's controllers → physics → animation.
    /// Hit detection, the opponent and the stage come in dev.4; `HitDef` is parsed and
    /// stored here but nothing is hit yet.
    ///
    /// Movement states (0-109) are the MUGEN common states. Elecbyte's `common1.cns` is not
    /// redistributable and Ikemen replaced it with its own ZSS script, so they are
    /// implemented natively in <see cref="CommonStates"/> with the same state numbers,
    /// animations and velocities, and a character's own override of a common state (KFM has
    /// none below 110) still wins.
    /// </summary>
    public class Fighter : IExprContext {
        public readonly MugenCharacter Character;
        public readonly CharConstants Const;
        public readonly CnsFile States;
        public readonly CmdFile Cmd;
        public readonly CommandEngine Commands;

        // ---- state -------------------------------------------------------------
        public int StateNo { get; private set; }
        public int PrevStateNo { get; private set; }
        public int StateTime { get; private set; }
        public int Time;                       // total ticks alive
        public StateType Type = StateType.Standing;
        public MoveType Move = MoveType.Idle;
        public Physics Phys = Physics.Stand;
        public bool Ctrl = true;
        public int Facing = 1;                 // +1 right, -1 left
        public float PosX, PosY;               // MUGEN units, y negative = above ground
        public float VelX, VelY;
        public int Life, Power;
        public int AnimNo = -1;
        public MugenAnimation Anim;
        public int AirJumpsLeft;
        public readonly int[] Vars = new int[60];
        public readonly float[] FVars = new float[40];
        public string LastHitDef;              // dev.3: recorded, not applied

        /// <summary>Names of triggers a state asked for that this engine does not implement.</summary>
        public readonly HashSet<string> UnknownTriggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Controller types seen but not implemented (dev.4+ work).</summary>
        public readonly HashSet<string> UnknownControllers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Last state change, for debugging and the training HUD.</summary>
        public string LastTransition = "";

        CmdKey keys, previousKeys;
        readonly Dictionary<StateController, int> ranThisState = new Dictionary<StateController, int>();
        Func<float> randomSource;

        public Fighter(MugenCharacter character, CnsFile states, CmdFile cmd, Func<float> random = null) {
            Character = character;
            States = states;
            Cmd = cmd;
            Const = CharConstants.From(states?.Header);
            Commands = new CommandEngine(cmd);
            randomSource = random ?? DefaultRandom;
            Life = Const.Life;
            AirJumpsLeft = Const.AirJumpNum;
            ChangeState(0, "spawn");
        }

        static readonly System.Random rng = new System.Random(12345);
        static float DefaultRandom() => rng.Next(0, 1000);

        // ---- input -------------------------------------------------------------

        /// <summary>Feeds one tick of player input (already facing-relative B/F).</summary>
        public void SetInput(CmdKey k) {
            previousKeys = keys;
            keys = k;
            Commands.TrackCharges(k);
            Commands.Step(k);
        }

        public bool Held(CmdKey k) => (keys & k) == k;
        public bool Pressed(CmdKey k) => (keys & k) == k && (previousKeys & k) != k;

        // ---- main loop ---------------------------------------------------------

        /// <summary>One MUGEN frame.</summary>
        public void Tick() {
            Time++;
            StateTime++;

            RunState(-1);                  // the .cmd state: commands into state changes
            int guard = 0;
            int before;
            do {
                before = StateNo;
                RunState(StateNo);
                guard++;
            } while (StateNo != before && guard < 8);   // a ChangeState runs the new state too

            CommonStates.Apply(this);      // common movement states (0-109)
            ApplyPhysics();
            Anim?.Tick();
        }

        void RunState(int no) {
            var def = States?.Get(no);
            if (def == null) return;
            if (no != -1) ApplyStatedefParams(def);
            foreach (var c in def.Controllers) {
                if (c.Persistent == 0) {
                    ranThisState.TryGetValue(c, out var ran);
                    if (ran > 0) continue;
                }
                if (!c.TriggersPass(this)) continue;
                if (c.Persistent == 0) ranThisState[c] = 1;
                int stateBefore = StateNo;
                Apply(c);
                if (StateNo != stateBefore) return;    // ChangeState: the rest is skipped
            }
        }

        bool statedefApplied;
        void ApplyStatedefParams(StateDef def) {
            if (statedefApplied) return;
            statedefApplied = true;
            if (def.Has("type")) Type = ParseStateType(def.Get("type"), Type);
            if (def.Has("movetype")) Move = ParseMoveType(def.Get("movetype"), Move);
            if (def.Has("physics")) Phys = ParsePhysics(def.Get("physics"), Phys);
            if (def.Has("anim")) ChangeAnim(EvalInt(def.Get("anim")));
            if (def.Has("ctrl")) Ctrl = EvalInt(def.Get("ctrl")) != 0;
            if (def.Has("velset")) {
                var parts = MugenDef.SplitCsv(def.Get("velset"));
                if (parts.Length >= 1 && parts[0].Length > 0) VelX = EvalFloat(parts[0]);
                if (parts.Length >= 2 && parts[1].Length > 0) VelY = EvalFloat(parts[1]);
            }
            if (def.Has("poweradd")) Power = Math.Max(0, Power + EvalInt(def.Get("poweradd")));
        }

        // ---- state controllers -------------------------------------------------

        void Apply(StateController c) {
            switch (c.Type) {
                case "changestate":
                case "selfstate":
                    if (c.Has("anim")) ChangeAnim(EvalInt(c.Get("anim")));
                    if (c.Has("ctrl")) Ctrl = EvalInt(c.Get("ctrl")) != 0;
                    ChangeState(EvalInt(c.Get("value")), c.Type + " in state " + StateNo + " (" + c.Name + ")");
                    break;
                case "changeanim":
                case "changeanim2":
                    ChangeAnim(EvalInt(c.Get("value")), EvalInt(c.Get("elem", "1")));
                    break;
                case "velset": {
                    var parts = MugenDef.SplitCsv(c.Get("value", c.Get("x") + "," + c.Get("y")));
                    if (c.Has("x")) VelX = EvalFloat(c.Get("x"));
                    if (c.Has("y")) VelY = EvalFloat(c.Get("y"));
                    if (c.Has("value")) {
                        if (parts.Length >= 1 && parts[0].Length > 0) VelX = EvalFloat(parts[0]);
                        if (parts.Length >= 2 && parts[1].Length > 0) VelY = EvalFloat(parts[1]);
                    }
                    break;
                }
                case "veladd": {
                    if (c.Has("x")) VelX += EvalFloat(c.Get("x"));
                    if (c.Has("y")) VelY += EvalFloat(c.Get("y"));
                    if (c.Has("value")) {
                        var parts = MugenDef.SplitCsv(c.Get("value"));
                        if (parts.Length >= 1 && parts[0].Length > 0) VelX += EvalFloat(parts[0]);
                        if (parts.Length >= 2 && parts[1].Length > 0) VelY += EvalFloat(parts[1]);
                    }
                    break;
                }
                case "velmul":
                    if (c.Has("x")) VelX *= EvalFloat(c.Get("x"));
                    if (c.Has("y")) VelY *= EvalFloat(c.Get("y"));
                    break;
                case "posset":
                    if (c.Has("x")) PosX = EvalFloat(c.Get("x"));
                    if (c.Has("y")) PosY = EvalFloat(c.Get("y"));
                    break;
                case "posadd":
                    if (c.Has("x")) PosX += EvalFloat(c.Get("x")) * Facing;
                    if (c.Has("y")) PosY += EvalFloat(c.Get("y"));
                    break;
                case "ctrlset":
                    Ctrl = EvalInt(c.Get("value")) != 0;
                    break;
                case "statetypeset":
                    if (c.Has("statetype")) Type = ParseStateType(c.Get("statetype"), Type);
                    if (c.Has("movetype")) Move = ParseMoveType(c.Get("movetype"), Move);
                    if (c.Has("physics")) Phys = ParsePhysics(c.Get("physics"), Phys);
                    break;
                case "turn":
                    Facing = -Facing;
                    break;
                case "varset":
                case "varadd": {
                    bool add = c.Type == "varadd";
                    if (c.Has("v")) {
                        int idx = EvalInt(c.Get("v"));
                        int value = EvalInt(c.Get("value"));
                        if (idx >= 0 && idx < Vars.Length) Vars[idx] = add ? Vars[idx] + value : value;
                    } else if (c.Has("fv")) {
                        int idx = EvalInt(c.Get("fv"));
                        float value = EvalFloat(c.Get("value"));
                        if (idx >= 0 && idx < FVars.Length) FVars[idx] = add ? FVars[idx] + value : value;
                    }
                    break;
                }
                case "varrandom": {
                    int idx = EvalInt(c.Get("v"));
                    var range = MugenDef.SplitCsv(c.Get("range", "0,1000"));
                    int lo = range.Length > 0 ? MugenDef.Atoi(range[0]) : 0;
                    int hi = range.Length > 1 ? MugenDef.Atoi(range[1]) : 1000;
                    if (idx >= 0 && idx < Vars.Length) Vars[idx] = lo + (int)(randomSource() / 1000f * Math.Max(0, hi - lo));
                    break;
                }
                case "poweradd":
                    Power = Math.Max(0, Power + EvalInt(c.Get("value")));
                    break;
                case "playsnd":
                    LastSound = c.Get("value");
                    break;
                case "hitdef":
                    LastHitDef = c.Get("attr", "") + " dmg=" + c.Get("damage", "0");
                    HitDefCount++;
                    break;
                case "null":
                case "assertspecial":
                case "sprpriority":
                case "playerpush":
                case "width":
                case "screenbound":
                case "makedust":
                case "afterimage":
                case "afterimagetime":
                case "envshake":
                case "hitby":
                case "nothitby":
                case "palfx":
                case "allpalfx":
                case "bgpalfx":
                case "explod":
                case "removeexplod":
                case "modifyexplod":
                case "helper":
                case "destroyself":
                case "selfanimexist":
                case "gravity":
                    if (c.Type == "gravity") VelY += Const.YAccel;
                    break;                  // deliberately ignored in dev.3
                default:
                    UnknownControllers.Add(c.Type);
                    break;
            }
        }

        public string LastSound { get; private set; }
        public int HitDefCount { get; private set; }

        // ---- transitions -------------------------------------------------------

        public void ChangeState(int no, string reason = "") {
            PrevStateNo = StateNo;
            StateNo = no;
            StateTime = 0;
            statedefApplied = false;
            ranThisState.Clear();
            LastTransition = reason;
            var def = States?.Get(no);
            if (def != null) ApplyStatedefParams(def);
            else CommonStates.EnterCommon(this, no);
        }

        public void ChangeAnim(int no, int elem = 1) {
            if (no < 0) return;
            var anim = Character?.Air?.Get(no);
            if (anim == null) return;
            AnimNo = no;
            Anim = anim;
            Anim.Reset();
            for (int i = 1; i < elem && i < anim.Frames.Count; i++) Anim.Tick();
        }

        void ApplyPhysics() {
            switch (Phys) {
                case Physics.Stand:
                    VelX *= Const.StandFriction;
                    if (Math.Abs(VelX) < Const.StandFrictionThreshold * 0.1f) VelX = 0f;
                    break;
                case Physics.Crouch:
                    VelX *= Const.CrouchFriction;
                    if (Math.Abs(VelX) < Const.CrouchFrictionThreshold) VelX = 0f;
                    break;
                case Physics.Air:
                    VelY += Const.YAccel;
                    break;
            }
            PosX += VelX * Facing;
            PosY += VelY;
            if (PosY > 0f) { PosY = 0f; }       // the ground is y = 0
        }

        public bool OnGround => PosY >= -0.001f;

        // ---- expression context -------------------------------------------------

        public float Random999() => randomSource();

        public bool TryTrigger(string name, string arg, float argValue, out float value) {
            value = 0f;
            switch (name.ToLowerInvariant()) {
                case "command": value = arg != null && Commands.Active(arg) ? 1 : 0; return true;
                case "stateno": value = StateNo; return true;
                case "prevstateno": value = PrevStateNo; return true;
                case "time":
                case "statetime": value = StateTime; return true;
                case "gametime": value = Time; return true;
                case "ctrl": value = Ctrl ? 1 : 0; return true;
                case "alive": value = Life > 0 ? 1 : 0; return true;
                case "life": value = Life; return true;
                case "lifemax": value = Const.Life; return true;
                case "power": value = Power; return true;
                case "powermax": value = 3000; return true;
                case "anim": value = AnimNo; return true;
                case "animtime": value = Anim != null ? (Anim.TotalTime == -1 ? -1 : Anim.Time - Anim.TotalTime) : 0; return true;
                case "animelem": value = Anim != null && Anim.CurrentElement + 1 == (int)argValue ? 1 : 0; return true;
                case "animelemno": value = Anim != null ? Anim.CurrentElement + 1 : 0; return true;
                case "animelemtime": value = Anim != null ? Anim.ElementTime : 0; return true;
                case "animexist":
                case "selfanimexist": value = Character?.Air?.Get((int)argValue) != null ? 1 : 0; return true;
                case "vel x": value = VelX; return true;
                case "vel y": value = VelY; return true;
                case "pos x": value = PosX; return true;
                case "pos y": value = PosY; return true;
                case "facing": value = Facing; return true;
                case "statetype": value = MatchFlag(arg, StateTypeLetter(Type)); return true;
                case "movetype": value = MatchFlag(arg, MoveTypeLetter(Move)); return true;
                case "physics": value = MatchFlag(arg, PhysicsLetter(Phys)); return true;
                case "var": {
                    int i = (int)argValue;
                    value = i >= 0 && i < Vars.Length ? Vars[i] : 0;
                    return true;
                }
                case "fvar": {
                    int i = (int)argValue;
                    value = i >= 0 && i < FVars.Length ? FVars[i] : 0;
                    return true;
                }
                case "random": value = randomSource(); return true;
                case "roundstate": value = 2; return true;               // always "fighting" in dev.3
                case "roundno": value = 1; return true;
                case "matchno": value = 1; return true;
                case "ishelper": value = 0; return true;
                case "numhelper":
                case "numexplod":
                case "numproj":
                case "numtarget":
                case "numenemy": value = 0; return true;
                case "movecontact":
                case "movehit":
                case "moveguarded":
                case "movereversed": value = 0; return true;             // dev.4
                case "hitcount":
                case "uniqhitcount": value = 0; return true;
                case "hitdefattr": value = 0; return true;
                case "hitpausetime": value = 0; return true;
                case "inguarddist": value = 0; return true;
                case "p2bodydist x":
                case "p2dist x": value = 160; return true;               // no opponent yet
                case "p2bodydist y":
                case "p2dist y": value = 0; return true;
                case "p2statetype":
                case "p2movetype": value = 0; return true;
                case "backedgedist":
                case "frontedgedist":
                case "backedgebodydist":
                case "frontedgebodydist": value = 200; return true;
                case "screenpos x": value = PosX; return true;
                case "screenpos y": value = PosY; return true;
                case "canrecover": value = 1; return true;
                case "const": value = ConstOf(arg); return true;
                case "pi": value = (float)Math.PI; return true;
                case "true": value = 1; return true;
                case "false": value = 0; return true;
                case "s": case "c": case "a": case "l":                  // bare flag letters
                case "i": case "h": case "n": case "u":
                    value = 0;
                    return true;
            }
            UnknownTriggers.Add(name);
            return false;
        }

        float ConstOf(string key) {
            if (string.IsNullOrEmpty(key)) return 0f;
            switch (key.ToLowerInvariant()) {
                case "data.life": return Const.Life;
                case "data.attack": return Const.Attack;
                case "data.defence": return Const.Defence;
                case "size.ground.back": return Const.GroundBack;
                case "size.ground.front": return Const.GroundFront;
                case "size.height": return Const.Height;
                case "velocity.walk.fwd.x": return Const.WalkFwd;
                case "velocity.walk.back.x": return Const.WalkBack;
                case "velocity.run.fwd.x": return Const.RunFwdX;
                case "velocity.jump.y": return Const.JumpNeuY;
                case "velocity.jump.neu.x": return Const.JumpNeuX;
                case "velocity.jump.back.x": return Const.JumpBack;
                case "velocity.jump.fwd.x": return Const.JumpFwd;
                case "movement.yaccel": return Const.YAccel;
                case "movement.stand.friction": return Const.StandFriction;
                case "movement.crouch.friction": return Const.CrouchFriction;
                case "movement.airjump.num": return Const.AirJumpNum;
                case "movement.airjump.height": return Const.AirJumpHeight;
            }
            return 0f;
        }

        static float MatchFlag(string arg, char actual) {
            if (string.IsNullOrEmpty(arg)) return 0f;
            foreach (var ch in arg)
                if (char.ToUpperInvariant(ch) == char.ToUpperInvariant(actual)) return 1f;
            return 0f;
        }

        public static char StateTypeLetter(StateType t) =>
            t == StateType.Standing ? 'S' : t == StateType.Crouching ? 'C' : t == StateType.Air ? 'A' : 'L';
        public static char MoveTypeLetter(MoveType t) =>
            t == MoveType.Attack ? 'A' : t == MoveType.BeingHit ? 'H' : 'I';
        public static char PhysicsLetter(Physics p) =>
            p == Physics.Stand ? 'S' : p == Physics.Crouch ? 'C' : p == Physics.Air ? 'A' : 'N';

        public static StateType ParseStateType(string s, StateType fallback) {
            if (string.IsNullOrEmpty(s)) return fallback;
            switch (char.ToUpperInvariant(s.Trim()[0])) {
                case 'S': return StateType.Standing;
                case 'C': return StateType.Crouching;
                case 'A': return StateType.Air;
                case 'L': return StateType.LieDown;
                case 'U': return fallback;
            }
            return fallback;
        }
        public static MoveType ParseMoveType(string s, MoveType fallback) {
            if (string.IsNullOrEmpty(s)) return fallback;
            switch (char.ToUpperInvariant(s.Trim()[0])) {
                case 'A': return MoveType.Attack;
                case 'I': return MoveType.Idle;
                case 'H': return MoveType.BeingHit;
            }
            return fallback;
        }
        public static Physics ParsePhysics(string s, Physics fallback) {
            if (string.IsNullOrEmpty(s)) return fallback;
            switch (char.ToUpperInvariant(s.Trim()[0])) {
                case 'S': return Physics.Stand;
                case 'C': return Physics.Crouch;
                case 'A': return Physics.Air;
                case 'N': return Physics.None;
            }
            return fallback;
        }

        // ---- expression helpers -------------------------------------------------

        readonly Dictionary<string, Expr> exprCache = new Dictionary<string, Expr>();

        public float EvalFloat(string expression) {
            if (string.IsNullOrWhiteSpace(expression)) return 0f;
            if (!exprCache.TryGetValue(expression, out var e)) {
                e = Expr.Parse(expression);
                exprCache[expression] = e;
            }
            float v = e.Eval(this);
            foreach (var n in e.UnknownNames) UnknownTriggers.Add(n);
            return v;
        }

        public int EvalInt(string expression) => (int)Math.Round(EvalFloat(expression));
    }
}
