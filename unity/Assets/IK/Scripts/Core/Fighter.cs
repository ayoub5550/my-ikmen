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
    public partial class Fighter : IExprContext {
        public readonly MugenCharacter Character;
        public CharConstants Const;
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
        /// <summary>Air jumps already used since leaving the ground (char.go `airJumpCount`).</summary>
        public int AirJumpCount;
        /// <summary>`sysvar(1)`: the jump direction the common states latch (common1.cns.zss).</summary>
        public int SysVar1;
        /// <summary>`AssertSpecial` flags of the current tick (char.go `specialFlag`). They are
        /// set while a state runs and read by the hard-coded keys of the next tick.</summary>
        public bool NoWalk, NoJump, NoCrouch, NoStand, NoBrake, NoAirJump, NoAutoTurn, Intro, NoKO;
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
            InitEx();
            ChangeState(0, "spawn");
            if (AnimNo < 0) ChangeAnim(0);      // state 0 picks its animation on its first tick
        }

        static readonly System.Random rng = new System.Random(12345);
        static float DefaultRandom() => rng.Next(0, 1000);

        // ---- input -------------------------------------------------------------

        /// <summary>Feeds one tick of player input (already facing-relative B/F).</summary>
        public void SetInput(CmdKey k) {
            previousKeys = keys;
            keys = k;
            Commands.Facing = Facing;
            Commands.Step(k);
        }

        public bool Held(CmdKey k) => (keys & k) == k;
        public bool Pressed(CmdKey k) => (keys & k) == k && (previousKeys & k) != k;

        // ---- main loop ---------------------------------------------------------

        /// <summary>One MUGEN frame.</summary>
        public void Tick() {
            Time++;
            StateTime++;

            TickEx();
            After.Record(this);
            if (!IsHelper) CommonStates.BasicActions(this);   // the engine's hard-coded keys (char.go)
            ClearSpecialFlags();               // char.go clears specialFlag right after them
            // the special states: -3 (players, own states only), -2 (players), -1 (.cmd)
            if (!IsHelper && ForeignStates == null) RunState(-3);
            if (!IsHelper) RunState(-2);
            if (!IsHelper || KeyCtrl) RunState(-1);   // the .cmd state: commands into state changes

            RunCurrentState();
            int stateBeforePhysics = StateNo;
            ApplyPhysics();
            // char.go runs a state entered by the engine itself (landing) in the same frame
            if (StateNo != stateBeforePhysics) RunCurrentState();
            // dev.4: the animation freezes while the character is shaking from a hit
            if (Ghv.HitShakeTime <= 0) Anim?.Tick();
        }

        /// <summary>Runs the current state, following ChangeState chains like MUGEN does.</summary>
        void RunCurrentState() {
            int guard = 0, before;
            do {
                before = StateNo;
                if (ActiveStates != null && ActiveStates.Get(StateNo) != null) RunState(StateNo);
                else if (HitStates.IsHandled(StateNo)) HitStates.Apply(this);
                else CommonStates.Apply(this);  // a common state the character did not override
                guard++;
            } while (StateNo != before && guard < 8);
        }

        void ClearSpecialFlags() {
            NoWalk = NoJump = NoCrouch = NoStand = NoBrake = NoAirJump = NoAutoTurn = Intro = NoKO = false;
        }

        /// <summary>Sets one `AssertSpecial` flag by name.</summary>
        public void AssertSpecial(string flag) {
            if (string.IsNullOrEmpty(flag)) return;
            switch (flag.Trim().ToLowerInvariant()) {
                case "nowalk": NoWalk = true; break;
                case "nojump": NoJump = true; break;
                case "nocrouch": NoCrouch = true; break;
                case "nostand": NoStand = true; break;
                case "nobrake": NoBrake = true; break;
                case "noairjump": NoAirJump = true; break;
                case "noautoturn": NoAutoTurn = true; break;
                case "intro": Intro = true; break;
                case "noko": NoKO = true; break;
            }
        }

        /// <summary>The state file in force: the character's own, or the attacker's during a
        /// `p2stateno` sequence (dev.4).</summary>
        public CnsFile ActiveStates => ForeignStates ?? States;

        void RunState(int no) {
            var def = ActiveStates?.Get(no);
            if (def == null) return;
            if (no >= 0) ApplyStatedefParams(def);
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
                    if (c.Type == "selfstate") SelfStateRestore();
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
                    } else {
                        // the short form KFM uses: `var(1) = 0`, `fvar(2) = 1.5`
                        foreach (var kv in c.Params) {
                            string key = kv.Key.Trim().ToLowerInvariant();
                            bool isF = key.StartsWith("fvar(");
                            if (!isF && !key.StartsWith("var(")) continue;
                            int open = key.IndexOf('('), close = key.IndexOf(')');
                            if (open < 0 || close < open) continue;
                            int idx = EvalInt(key.Substring(open + 1, close - open - 1));
                            if (isF) {
                                float value = EvalFloat(kv.Value);
                                if (idx >= 0 && idx < FVars.Length) FVars[idx] = add ? FVars[idx] + value : value;
                            } else {
                                int value = EvalInt(kv.Value);
                                if (idx >= 0 && idx < Vars.Length) Vars[idx] = add ? Vars[idx] + value : value;
                            }
                        }
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
                case "hitdef":
                    LastHitDef = c.Get("attr", "") + " dmg=" + c.Get("damage", "0");
                    HitDefCount++;
                    Hit = HitDef.Read(c, EvalFloat, Const);
                    HitDefActive = Hit.IsValid;
                    foreach (var unknown in Hit.Unsupported) UnknownControllers.Add("hitdef." + unknown);
                    break;
                case "assertspecial":
                    AssertSpecial(c.Get("flag"));
                    AssertSpecial(c.Get("flag2"));
                    AssertSpecial(c.Get("flag3"));
                    break;
                case "lifeadd": {
                    int add = EvalInt(c.Get("value"));
                    bool canKill = !c.Has("kill") || EvalInt(c.Get("kill")) != 0;
                    Life = Math.Max(canKill ? 0 : 1, Math.Min(Const.Life, Life + add));
                    if (Life <= 0 && !NoKO) KO = true;
                    break;
                }
                case "lifeset":
                    Life = Math.Max(0, Math.Min(Const.Life, EvalInt(c.Get("value"))));
                    if (Life <= 0 && !NoKO) KO = true;
                    break;
                case "powerset":
                    Power = Math.Max(0, Math.Min(PowerMax, EvalInt(c.Get("value"))));
                    break;
                case "attackmulset":
                    AttackMul = EvalFloat(c.Get("value"));
                    break;
                case "defencemulset":
                    DefenceMul = EvalFloat(c.Get("value"));
                    break;
                case "hitfallset": {
                    int v = c.Has("value") ? EvalInt(c.Get("value")) : -1;
                    if (v >= 0) Ghv.FallFlag = v != 0;
                    if (c.Has("xvel")) Ghv.FallXVel = EvalFloat(c.Get("xvel"));
                    if (c.Has("yvel")) Ghv.FallYVel = EvalFloat(c.Get("yvel"));
                    break;
                }
                case "hitfalldamage":
                    if (Ghv.FallDamage > 0) {
                        Life = Math.Max(Ghv.FallKill ? 0 : 1, Life - Ghv.FallDamage);
                        if (Life <= 0 && !NoKO) KO = true;
                    }
                    break;
                case "hitfallvel":
                    if (!float.IsNaN(Ghv.FallXVel)) VelX = Ghv.FallXVel * Facing;
                    VelY = Ghv.FallYVel;
                    break;
                case "hitvelset":
                    if (!c.Has("x") || EvalInt(c.Get("x")) != 0) VelX = Ghv.XVel;
                    if (c.Has("y") && EvalInt(c.Get("y")) != 0) VelY = Ghv.YVel;
                    break;
                case "gravity":
                    VelY += Const.YAccel;
                    break;
                default:
                    if (!ApplyEx(c)) UnknownControllers.Add(c.Type);
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
            ClearHitDef();                       // MUGEN: a HitDef lives until the state changes
            var def = ActiveStates?.Get(no);
            if (def != null) ApplyStatedefParams(def);
            else if (HitStates.IsHandled(no)) HitStates.Enter(this, no);
            else CommonStates.EnterCommon(this, no);
        }

        /// <summary>Does the character have this action? (`SelfAnimExist`)</summary>
        public bool HasAnim(int no) => Character != null && Character.Air != null && Character.Air.Get(no) != null;

        public void ChangeAnim(int no, int elem = 1) {
            if (no < 0) return;
            var anim = Character?.Air?.Get(no);
            if (anim == null) return;
            AnimNo = no;
            Anim = anim.Instance();
            Anim.Reset();
            for (int i = 1; i < elem && i < anim.Frames.Count; i++) Anim.Tick();
        }

        /// <summary>
        /// char.go `posUpdate`: the position is moved with the *current* velocity first, and
        /// only then friction or gravity are applied — a tick's state controllers therefore
        /// see the velocity they set, and the friction of the next tick. After that the
        /// engine's own landing rule runs: an airborne character that has crossed the ground
        /// going down enters state 52 (except from the backwards hop, state 105).
        /// </summary>
        void ApplyPhysics() {
            if (BindTimeLeft > 0 && BoundTo != null) {
                BindTimeLeft--;
                PosX = BoundTo.PosX + BindOffX * BoundTo.Facing;
                PosY = BoundTo.PosY + BindOffY;
            } else if (!PosFreezeOn) {
                PosX += VelX * Facing;
                PosY += VelY;
            }

            switch (Phys) {
                case Physics.Stand:
                    VelX *= Const.StandFriction;
                    if (Math.Abs(VelX) < PhysicsEpsilon) VelX = 0f;
                    break;
                case Physics.Crouch:
                    VelX *= Const.CrouchFriction;
                    break;
                case Physics.Air:
                    VelY += Const.YAccel;
                    break;
            }

            if (Phys == Physics.Air && VelY > 0f && PosY >= 0f && StateNo != CommonStates.HopBack)
                ChangeState(CommonStates.JumpLand, "landed");
        }

        /// <summary>Velocities below this are snapped to zero (char.go `1/originLs`).</summary>
        public const float PhysicsEpsilon = 1f;

        public bool OnGround => PosY >= -0.001f;

        // ---- expression context -------------------------------------------------

        public float Random999() => randomSource();

        public bool TryTrigger(string name, string arg, float argValue, out float value) {
            value = 0f;
            switch (name.ToLowerInvariant()) {
                case "command": value = arg != null && ActiveCommands.Active(arg) ? 1 : 0; return true;
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
                case "powermax": value = PowerMax; return true;
                case "anim": value = AnimNo; return true;
                case "animtime": value = Anim != null ? Anim.AnimTime : 0; return true;
                case "animelem": value = Anim != null && Anim.AnimElemTime((int)argValue) == 0 ? 1 : 0; return true;
                case "animelemno": value = Anim != null ? Anim.CurrentElement + 1 : 0; return true;
                case "animelemtime": value = Anim != null ? Anim.AnimElemTime((int)argValue) : 0; return true;
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
                case "roundstate": value = Engine != null ? (int)Engine.State : 2; return true;
                case "roundno": value = Engine != null ? Engine.RoundNo : 1; return true;
                case "roundsexisted": value = Engine != null ? Engine.RoundNo - 1 : 0; return true;
                case "matchno": value = 1; return true;
                case "matchover": value = Engine != null && Engine.MatchOver ? 1 : 0; return true;
                case "win": value = Engine != null && Engine.Wins[PlayerNo] > 0 ? 1 : 0; return true;
                case "lose": value = Engine != null && Engine.Wins[1 - PlayerNo] > 0 ? 1 : 0; return true;
                case "movecontact": value = MoveContactFlag; return true;
                case "movehit": value = MoveHitFlag; return true;
                case "moveguarded": value = MoveGuardedFlag; return true;
                case "movereversed": value = MoveReversedFlag; return true;
                case "hitcount": value = HitCount; return true;
                case "uniqhitcount": value = UniqHitCount; return true;
                case "hitdefattr": {
                    // `hitdefattr = SCA, NA` — true while this HitDef is live and matches
                    if (!HitDefActive || Hit == null) { value = 0; return true; }
                    if (string.IsNullOrEmpty(arg)) { value = 1; return true; }
                    var parts = MugenDef.SplitCsv(arg);
                    var stateMask = HitAttr.None;
                    if (parts.Length > 0)
                        foreach (char ch in parts[0]) {
                            if (ch == 'S' || ch == 's') stateMask |= HitAttr.StandAttack;
                            else if (ch == 'C' || ch == 'c') stateMask |= HitAttr.CrouchAttack;
                            else if (ch == 'A' || ch == 'a') stateMask |= HitAttr.AirAttack;
                        }
                    if (stateMask == HitAttr.None) stateMask = HitAttr.StateMask;
                    var kinds = HitAttr.None;
                    for (int i = 1; i < parts.Length; i++) kinds |= HitDef.ParseAttr("S," + parts[i]) & ~HitAttr.StateMask;
                    value = Hit.MatchesAttr(stateMask, kinds) ? 1 : 0;
                    return true;
                }
                case "hitpausetime": value = HitPauseTime; return true;
                case "inguarddist": value = InGuardDist ? 1 : 0; return true;
                case "hitover": value = HitOver ? 1 : 0; return true;
                case "hitshakeover": value = HitShakeOver ? 1 : 0; return true;
                case "hitfall": value = HitFall ? 1 : 0; return true;
                case "gethitvar": {
                    float v;
                    if (Ghv.TryGet(arg, out v)) { value = v; return true; }
                    UnknownTriggers.Add("gethitvar(" + arg + ")");
                    value = 0;
                    return true;
                }
                case "p2dist x": {
                    var t = Opponent;
                    value = t != null ? (t.PosX - PosX) * Facing : 160f;
                    return true;
                }
                case "p2dist y": {
                    var t = Opponent;
                    value = t != null ? t.PosY - PosY : 0f;
                    return true;
                }
                case "p2bodydist x": {
                    var t = Opponent;
                    if (t == null) { value = 160f; return true; }
                    float front = Type == StateType.Air ? Const.AirFront : Const.GroundFront;
                    float tFront = t.Type == StateType.Air ? t.Const.AirFront : t.Const.GroundFront;
                    value = (Math.Abs(t.PosX - PosX) - front - tFront) * ((t.PosX - PosX) * Facing >= 0 ? 1 : -1);
                    return true;
                }
                case "p2bodydist y": {
                    var t = Opponent;
                    value = t != null ? t.PosY - PosY : 0f;
                    return true;
                }
                case "p2statetype": {
                    var t = Opponent;
                    value = t != null ? MatchFlag(arg, StateTypeLetter(t.Type)) : 0f;
                    return true;
                }
                case "p2movetype": {
                    var t = Opponent;
                    value = t != null ? MatchFlag(arg, MoveTypeLetter(t.Move)) : 0f;
                    return true;
                }
                case "p2stateno": {
                    var t = Opponent;
                    value = t != null ? t.StateNo : 0f;
                    return true;
                }
                case "p2life": {
                    var t = Opponent;
                    value = t != null ? t.Life : 0f;
                    return true;
                }
                case "backedgedist": {
                    float bound = Engine != null && Engine.Stage != null ? Engine.Stage.LeftBound : -200f;
                    float other = Engine != null && Engine.Stage != null ? Engine.Stage.RightBound : 200f;
                    value = Facing >= 0 ? PosX - bound : other - PosX;
                    return true;
                }
                case "frontedgedist": {
                    float left = Engine != null && Engine.Stage != null ? Engine.Stage.LeftBound : -200f;
                    float right = Engine != null && Engine.Stage != null ? Engine.Stage.RightBound : 200f;
                    value = Facing >= 0 ? right - PosX : PosX - left;
                    return true;
                }
                case "backedgebodydist":
                case "frontedgebodydist": {
                    float left = Engine != null && Engine.Stage != null ? Engine.Stage.LeftBound : -200f;
                    float right = Engine != null && Engine.Stage != null ? Engine.Stage.RightBound : 200f;
                    bool front = name.ToLowerInvariant().StartsWith("front");
                    float edge = (front == (Facing >= 0)) ? right - PosX : PosX - left;
                    value = edge - (Type == StateType.Air ? Const.AirFront : Const.GroundFront);
                    return true;
                }
                case "screenpos x": value = Engine != null && Engine.Camera != null ? PosX - Engine.Camera.X : PosX; return true;
                case "screenpos y": value = PosY; return true;
                case "cameraPos x": case "camerapos x": value = Engine != null && Engine.Camera != null ? Engine.Camera.X : 0f; return true;
                case "camerapos y": value = Engine != null && Engine.Camera != null ? Engine.Camera.Y : 0f; return true;
                case "canrecover": value = Ghv.FallRecover ? 1 : 0; return true;
                case "timemod": value = 0; return true;
                case "roundstowin": value = Engine != null ? Engine.RoundsToWin : 2; return true;
                case "time left": case "timeleft": value = Engine != null ? Engine.TimeLeft : 99; return true;
                case "const": value = ConstOf(arg); return true;
                case "pi": value = (float)Math.PI; return true;
                case "true": value = 1; return true;
                case "false": value = 0; return true;
                case "s": case "c": case "a": case "l":                  // bare flag letters
                case "i": case "h": case "n": case "u":
                    value = 0;
                    return true;
            }
            if (TryTriggerEx(name.ToLowerInvariant(), arg, argValue, out value)) return true;
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
                case "size.air.back": return Const.AirBack;
                case "size.air.front": return Const.AirFront;
                case "size.attack.dist": return Const.AttackDist;
                case "size.proj.attack.dist": return Const.ProjAttackDist;
                case "data.airjuggle": return Const.AirJuggle;
                case "data.liedown.time": return Const.LieDownTime;
                case "data.power": return PowerMax;
            }
            return ConstFromHeader(key);
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
