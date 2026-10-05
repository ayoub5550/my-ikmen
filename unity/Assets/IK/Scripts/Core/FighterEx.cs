using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>
    /// dev.5 half of <see cref="Fighter"/>: helpers, trigger redirection, targets, the
    /// state controllers dev.3/dev.4 left inert (Helper, Explod, Projectile, PlaySnd,
    /// SuperPause, Pause, EnvShake, PalFX, AfterImage, HitOverride, ReversalDef, NotHitBy,
    /// Bind*, Trans, AngleDraw, ...) and the triggers that go with them.
    /// Reference: `engine/ikemen-go/src/char.go` and `bytecode.go` (each controller's `Run`).
    /// </summary>
    public partial class Fighter : IRedirectContext, IAssignContext {
        public void Assign(string name, int index, float value) {
            switch (name) {
                case "var": if (index >= 0 && index < Vars.Length) Vars[index] = (int)value; return;
                case "fvar": if (index >= 0 && index < FVars.Length) FVars[index] = value; return;
                case "sysvar":
                    if (index == 1) SysVar1 = (int)value;
                    if (index >= 0 && index < SysVars.Length) SysVars[index] = (int)value; return;
                case "sysfvar": if (index >= 0 && index < SysFVars.Length) SysFVars[index] = value; return;
            }
            if (name.StartsWith("zss__", StringComparison.Ordinal)) ZssLocals[name.Substring(5)] = value;
        }

        // ---- helpers ---------------------------------------------------------------
        public bool IsHelper;
        public int HelperId;
        public string HelperName = "";
        public Fighter Parent;
        Fighter rootChar;
        public Fighter Root => IsHelper && rootChar != null ? rootChar : this;
        public bool Destroyed;
        /// <summary>Helper with `keyctrl = 1`: reads the root's commands.</summary>
        public bool KeyCtrl;
        /// <summary>Helper `type = player`: counts as a player for the camera/push.</summary>
        public bool HelperIsPlayer;
        /// <summary>The fighter whose states this one runs (p2stateno / TargetState).</summary>
        public Fighter StateOwner;

        // ---- drawing parameters set by controllers ---------------------------------
        public float DrawAngle;
        public bool AngleDrawOn;
        public float DrawScaleX = 1f, DrawScaleY = 1f;
        public bool TransOn;
        public TransType TransMode = TransType.Default;
        public int TransSrc = 255, TransDst = 0;
        public float DrawOffsetX, DrawOffsetY;
        public readonly PalFx PalFx = new PalFx();
        public readonly AfterImage After = new AfterImage();
        public int PaletteNo = 1;
        /// <summary>RemapPal pairs (source group,index → dest group,index) recorded for the renderer.</summary>
        public readonly Dictionary<int, int> PalRemap = new Dictionary<int, int>();

        // ---- per-tick flags ---------------------------------------------------------
        public bool ScreenBoundOn = true, MoveCameraX = true, MoveCameraY = true;
        public bool PlayerPushOn = true;
        public bool PosFreezeOn;
        public float WidthFrontAdd, WidthBackAdd;

        // ---- hit modifiers -----------------------------------------------------------
        public readonly HitBySlot[] HitBy = { new HitBySlot(), new HitBySlot() };
        public readonly HitOverrideSlot[] HitOverrides = new HitOverrideSlot[8];
        public HitDef Reversal;
        public bool ReversalActive;
        public HitAttr ReversalAttr;

        public readonly List<Fighter> Targets = new List<Fighter>();
        public readonly int[] SysVars = new int[5];
        public readonly float[] SysFVars = new float[5];
        public int AiLevel;
        public int PauseMoveTime, SuperMoveTime;
        /// <summary>Tick of the last projectile hit/contact/guard per projectile id (owner side).</summary>
        public readonly Dictionary<int, int> ProjHitAt = new Dictionary<int, int>(), ProjContactAt = new Dictionary<int, int>(),
            ProjGuardedAt = new Dictionary<int, int>();
        public string VictoryQuote = "";
        /// <summary>ZSS `let` locals of the state being run (reset for every state run).</summary>
        public readonly Dictionary<string, float> ZssLocals = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Ikemen maps (`map(name)`, MapSet, MapAdd).</summary>
        public readonly Dictionary<string, float> Maps = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Creates a helper of <paramref name="parent"/> (Helper controller).</summary>
        public Fighter(Fighter parent, int helperId, int stateNo) {
            Character = parent.Character;
            States = parent.Root.States;
            Cmd = parent.Cmd;
            Const = parent.Root.Const;
            Commands = new CommandEngine(null);
            randomSource = parent.randomSource;
            IsHelper = true;
            HelperId = helperId;
            Parent = parent;
            rootChar = parent.Root;
            PlayerNo = parent.PlayerNo;
            Engine = parent.Engine;
            Scl = parent.Root.Scl;
            Life = Const.Life;
            Power = parent.Root.Power;
            PaletteNo = parent.PaletteNo;
            AiLevel = parent.Root.AiLevel;
            for (int i = 0; i < HitOverrides.Length; i++) HitOverrides[i] = new HitOverrideSlot();
            Ctrl = false;
            // the engine places the helper, then enters its state (FightEngine.CreateHelper)
        }

        public int SuperMoveLeft, PauseMoveLeft;

        /// <summary>
        /// dev.5: world units per character unit. A character keeps its position, velocity and
        /// constants in its own `localcoord` units (all of its states are written in them); the
        /// engine works in the stage's units. Scl = stage localcoord width / character
        /// localcoord width: 1 for KFM on a 320-wide stage, 0.25 for kfm720 (1280x720) there.
        /// Ikemen GO: `localscl` in char.go.
        /// </summary>
        public float Scl = 1f;
        public float WorldX { get { return PosX * Scl; } set { PosX = value / Scl; } }
        public float WorldY { get { return PosY * Scl; } set { PosY = value / Scl; } }

        void InitEx() {
            for (int i = 0; i < HitOverrides.Length; i++) if (HitOverrides[i] == null) HitOverrides[i] = new HitOverrideSlot();
        }

        /// <summary>Counters of the dev.5 modifiers, once per tick (before the states run).</summary>
        public void TickEx() {
            InitEx();
            PalFx.Tick();
            foreach (var s in HitBy) if (s.Time > 0) s.Time--;
            foreach (var h in HitOverrides) if (h.Time > 0) h.Time--;
            for (int i = Targets.Count - 1; i >= 0; i--) {
                var t = Targets[i];
                if (t == null || t.Destroyed || (t.Move != MoveType.BeingHit && !t.Ghv.IsBound)) Targets.RemoveAt(i);
            }
            // per-tick flags reset each frame, the state sets them again (char.go `unsetSCF`)
            ScreenBoundOn = MoveCameraX = MoveCameraY = true;
            PlayerPushOn = true;
            PosFreezeOn = false;
            AngleDrawOn = false;
            TransOn = false;
            WidthFrontAdd = WidthBackAdd = 0f;
            DrawOffsetX = DrawOffsetY = 0f;
        }

        // ---- redirection ------------------------------------------------------------

        public IExprContext Redirect(string kind, int id, bool hasId) {
            switch (kind) {
                case "parent": return IsHelper ? Parent : null;
                case "root": return Root;
                case "helper": return Engine != null ? Engine.FindHelper(Root, hasId ? id : -1) : null;
                case "target": {
                    foreach (var t in Targets)
                        if (!hasId || id < 0 || t.Ghv.HitId == id) return t;
                    return null;
                }
                case "partner": return null;
                case "enemy":
                case "enemynear":
                case "p2": {
                    if (Engine == null) return null;
                    if (hasId && id > 0) return null;      // 1 vs 1: only one enemy
                    return Engine.Opponent(Root);
                }
                case "playerid": return Engine != null ? Engine.FindById(id) : null;
                case "stateowner": return StateOwner ?? this;
                case "helperindex": return Engine != null ? Engine.FindHelperByIndex(Root, id) : null;
            }
            return null;
        }

        // ---- sounds -----------------------------------------------------------------

        public void QueueSound(bool common, int group, int index, int channel = -1, float volume = 1f,
                               bool lowPriority = false, float freqMul = 1f, float pan = 0f, bool loop = false) {
            if (group < 0) return;
            if (Engine != null)
                Engine.Sounds.Add(new SoundEvent {
                    Owner = Root, Common = common, Group = group, Index = index, Channel = channel,
                    Volume = volume, LowPriority = lowPriority, FreqMul = freqMul, Pan = pan, Loop = loop,
                });
            if (!common) PendingSounds.Add(new[] { group, index });
        }

        /// <summary>`F5, 0` / `S5, 0` / `5, 0` → (common?, group, index). PlaySnd: no prefix = own .snd.</summary>
        static bool ParseSoundRef(string v, Func<string, float> eval, out int group, out int index) {
            group = -1; index = 0;
            bool common = false;
            var p = MugenDef.SplitCsv(v);
            if (p.Length == 0) return false;
            string g = p[0].Trim();
            if (g.Length > 1 && (g[0] == 'F' || g[0] == 'f') && !char.IsLetter(g[1])) { common = true; g = g.Substring(1); }
            else if (g.Length > 1 && (g[0] == 'S' || g[0] == 's') && !char.IsLetter(g[1])) g = g.Substring(1);
            group = (int)Math.Round(eval(g));
            if (p.Length > 1 && p[1].Trim().Length > 0) index = (int)Math.Round(eval(p[1]));
            return common;
        }

        /// <summary>`F30` (fightfx) / `30` (own .air) → source + number.</summary>
        SpriteSource ParseAnimRef(string v, out int no) {
            v = (v ?? "").Trim();
            if (v.Length > 1 && (v[0] == 'F' || v[0] == 'f') && !char.IsLetter(v[1])) {
                no = EvalInt(v.Substring(1));
                return SpriteSource.FightFx;
            }
            if (v.Length > 1 && (v[0] == 'S' || v[0] == 's') && !char.IsLetter(v[1])) v = v.Substring(1);
            no = EvalInt(v);
            return SpriteSource.Character;
        }

        float[] EvalPair(StateController c, string key, float dx, float dy) {
            var r = new[] { dx, dy };
            if (!c.Has(key)) return r;
            var p = MugenDef.SplitCsv(c.Get(key));
            if (p.Length > 0 && p[0].Trim().Length > 0) r[0] = EvalFloat(p[0]);
            if (p.Length > 1 && p[1].Trim().Length > 0) r[1] = EvalFloat(p[1]);
            return r;
        }

        int ParamInt(StateController c, string key, int def) => c.Has(key) ? EvalInt(c.Get(key)) : def;
        float ParamFloat(StateController c, string key, float def) => c.Has(key) ? EvalFloat(c.Get(key)) : def;

        public static bool AttrMatches(HitAttr slot, HitAttr attack) {
            if ((slot & attack & HitAttr.StateMask) == 0) return false;
            var slotClass = slot & (HitAttr.ClassMask | HitAttr.KindMask);
            if (slotClass == HitAttr.None) return true;
            return (slot & attack & HitAttr.ClassMask) != 0 && (slot & attack & HitAttr.KindMask) != 0;
        }

        /// <summary>NotHitBy / HitBy: may an attack with this attribute hit this character?</summary>
        public bool HittableBy(HitAttr attack) {
            foreach (var s in HitBy) {
                if (!s.Active) continue;
                bool match = AttrMatches(s.Attr, attack);
                if (s.Not && match) return false;
                if (!s.Not && !match) return false;
            }
            return true;
        }

        /// <summary>The HitOverride slot this attack triggers, or null.</summary>
        public HitOverrideSlot OverrideFor(HitAttr attack) {
            InitEx();
            foreach (var h in HitOverrides) if (h.Active && AttrMatches(h.Attr, attack)) return h;
            return null;
        }

        IEnumerable<Fighter> TargetsById(StateController c) {
            int id = c.Has("id") ? EvalInt(c.Get("id")) : -1;
            var list = new List<Fighter>();
            foreach (var t in Targets) if (id < 0 || t.Ghv.HitId == id) list.Add(t);
            return list;
        }

        // ---- the dev.5 state controllers -------------------------------------------------

        /// <summary>Returns false for a controller this engine does not know.</summary>
        bool ApplyEx(StateController c) {
            InitEx();
            switch (c.Type) {
                case ZssFile.LetType:
                    ZssLocals[c.Get("name")] = EvalFloat(c.Get("value"));
                    return true;
                case ZssFile.ExprType:
                    EvalFloat(c.Get("value"));
                    return true;
                case "mapset":
                case "mapadd": {
                    string key = MugenDef.Unquote(c.Get("map")).Trim().Lc();
                    float v = ParamFloat(c, "value", 0f);
                    var target = ParamInt(c, "type", 0) == 1 ? Root : this;
                    float old;
                    target.Maps.TryGetValue(key, out old);
                    target.Maps[key] = c.Type == "mapadd" ? old + v : v;
                    return true;
                }
                case "helper": SpawnHelper(c); return true;
                case "destroyself":
                    if (IsHelper) Destroyed = true;
                    return true;
                case "explod": Engine?.AddExplod(MakeExplod(c)); return true;
                case "modifyexplod": ModifyExplod(c); return true;
                case "removeexplod": {
                    int id = c.Has("id") ? EvalInt(c.Get("id")) : -1;
                    Engine?.RemoveExplods(this, id);
                    return true;
                }
                case "projectile": SpawnProjectile(c); return true;
                case "playsnd": {
                    int g, n;
                    bool common = ParseSoundRef(c.Get("value"), EvalFloat, out g, out n);
                    float vol = c.Has("volumescale") ? EvalFloat(c.Get("volumescale")) / 100f
                              : c.Has("volume") ? Math.Max(0f, 1f + EvalFloat(c.Get("volume")) / 100f) : 1f;
                    LastSound = c.Get("value");
                    QueueSound(common, g, n, ParamInt(c, "channel", -1), vol, ParamInt(c, "lowpriority", 0) != 0,
                               ParamFloat(c, "freqmul", 1f), ParamFloat(c, "pan", 0f) / 160f * Facing,
                               ParamInt(c, "loop", 0) != 0);
                    return true;
                }
                case "stopsnd":
                    Engine?.StopChannels.Add(new KeyValuePair<Fighter, int>(Root, ParamInt(c, "channel", -1)));
                    return true;
                case "sndpan": return true;
                case "superpause":
                case "pause": DoPause(c, c.Type == "superpause"); return true;
                case "envshake":
                    Engine?.EnvShake(ParamInt(c, "time", 1), ParamFloat(c, "freq", 60f), ParamFloat(c, "ampl", -4f),
                                     c.Has("phase") ? EvalFloat(c.Get("phase")) : float.NaN);
                    return true;
                case "palfx": {
                    var fx = PalFx.FromController(c, EvalFloat);
                    PalFx.CopyFrom(fx);
                    return true;
                }
                case "allpalfx":
                    if (Engine != null) Engine.AllPalFx.CopyFrom(PalFx.FromController(c, EvalFloat));
                    return true;
                case "bgpalfx":
                    if (Engine != null) Engine.BgPalFx.CopyFrom(PalFx.FromController(c, EvalFloat));
                    return true;
                case "afterimage": ReadAfterImage(c); return true;
                case "afterimagetime":
                    After.Time = ParamInt(c, "time", ParamInt(c, "value", 1));
                    return true;
                case "hitoverride": {
                    int slot = Math.Max(0, Math.Min(7, ParamInt(c, "slot", 0)));
                    var h = HitOverrides[slot];
                    h.Attr = HitDef.ParseAttr(c.Get("attr"));
                    h.StateNo = ParamInt(c, "stateno", -1);
                    h.Time = ParamInt(c, "time", 1);
                    h.ForceAir = ParamInt(c, "forceair", 0) != 0;
                    if (h.StateNo < 0) h.Time = 0;
                    return true;
                }
                case "reversaldef":
                    Reversal = HitDef.Read(c, EvalFloat, Const);
                    ReversalAttr = HitDef.ParseAttr(c.Get("reversal.attr"));
                    ReversalActive = ReversalAttr != HitAttr.None;
                    return true;
                case "nothitby":
                case "hitby": {
                    int slot = c.Has("value2") ? 1 : 0;
                    var s = HitBy[slot];
                    s.Attr = HitDef.ParseAttr(c.Get(slot == 1 ? "value2" : "value", "SCA"));
                    s.Not = c.Type == "nothitby";
                    s.Time = ParamInt(c, "time", 1);
                    if (s.Not && s.Attr == (HitAttr.StateMask) && !c.Get("value").Contains(","))
                        UnhittableTime = Math.Max(UnhittableTime, 0);
                    return true;
                }
                case "hitadd": {
                    int v = ParamInt(c, "value", 0);
                    HitCount += v; UniqHitCount += v;
                    return true;
                }
                case "movehitreset": ClearMoveHit(); return true;
                case "posfreeze": PosFreezeOn = ParamInt(c, "value", 1) != 0; return true;
                case "screenbound":
                    ScreenBoundOn = ParamInt(c, "value", 0) != 0;
                    if (c.Has("movecamera")) {
                        var mc = EvalPair(c, "movecamera", 0, 0);
                        MoveCameraX = mc[0] != 0; MoveCameraY = mc[1] != 0;
                    } else MoveCameraX = MoveCameraY = false;
                    return true;
                case "playerpush": PlayerPushOn = ParamInt(c, "value", 1) != 0; return true;
                case "width": {
                    var e = EvalPair(c, c.Has("edge") ? "edge" : "value", 0, 0);
                    if (c.Has("player")) e = EvalPair(c, "player", 0, 0);
                    WidthFrontAdd = e[0]; WidthBackAdd = e[1];
                    return true;
                }
                case "sprpriority": SprPriority = ParamInt(c, "value", 0); return true;
                case "trans": {
                    TransOn = true;
                    TransMode = ParseTrans(c.Get("trans", "default"));
                    var a = EvalPair(c, "alpha", 256, 0);
                    TransSrc = (int)a[0]; TransDst = (int)a[1];
                    return true;
                }
                case "angledraw":
                    AngleDrawOn = true;
                    if (c.Has("value")) DrawAngle = EvalFloat(c.Get("value"));
                    if (c.Has("scale")) { var s = EvalPair(c, "scale", 1, 1); DrawScaleX = s[0]; DrawScaleY = s[1]; }
                    return true;
                case "angleset": DrawAngle = ParamFloat(c, "value", 0f); return true;
                case "angleadd": DrawAngle += ParamFloat(c, "value", 0f); return true;
                case "anglemul": DrawAngle *= ParamFloat(c, "value", 1f); return true;
                case "offset":
                    DrawOffsetX = ParamFloat(c, "x", 0f); DrawOffsetY = ParamFloat(c, "y", 0f);
                    return true;
                case "attackdist": Const.AttackDist = ParamFloat(c, "value", Const.AttackDist); return true;
                case "parentvarset":
                case "parentvaradd": {
                    var p = Parent;
                    if (p == null) return true;
                    p.ApplyVar(c, this, c.Type == "parentvaradd");
                    return true;
                }
                case "varrangeset": {
                    int first = ParamInt(c, "first", 0);
                    if (c.Has("fvalue")) {
                        float v = EvalFloat(c.Get("fvalue"));
                        int last = ParamInt(c, "last", FVars.Length - 1);
                        for (int i = Math.Max(0, first); i <= last && i < FVars.Length; i++) FVars[i] = v;
                    } else {
                        int v = ParamInt(c, "value", 0);
                        int last = ParamInt(c, "last", Vars.Length - 1);
                        for (int i = Math.Max(0, first); i <= last && i < Vars.Length; i++) Vars[i] = v;
                    }
                    return true;
                }
                case "bindtoparent":
                case "bindtoroot": {
                    var to = c.Type == "bindtoparent" ? Parent : (IsHelper ? Root : null);
                    if (to == null) return true;
                    var pos = EvalPair(c, "pos", 0, 0);
                    WorldX = to.WorldX + pos[0] * Scl * to.Facing;
                    WorldY = to.WorldY + pos[1] * Scl;
                    if (c.Has("facing")) {
                        int f = EvalInt(c.Get("facing"));
                        if (f > 0) Facing = to.Facing; else if (f < 0) Facing = -to.Facing;
                    }
                    return true;
                }
                case "bindtotarget": {
                    foreach (var t in TargetsById(c)) {
                        var pos = EvalPair(c, "pos", 0, 0);
                        string anchor = "foot";
                        var parts = MugenDef.SplitCsv(c.Get("pos"));
                        if (parts.Length > 2) anchor = parts[2].Trim().Lc();
                        float y = t.WorldY + pos[1] * Scl;
                        if (anchor == "head") y -= t.Const.Height * t.Scl;
                        else if (anchor == "mid") y -= t.Const.Height * t.Scl / 2f;
                        WorldX = t.WorldX + pos[0] * Scl * t.Facing;
                        WorldY = y;
                        break;
                    }
                    return true;
                }
                case "targetlifeadd":
                    foreach (var t in TargetsById(c)) {
                        int add = EvalInt(c.Get("value"));
                        if (c.Has("absolute") && EvalInt(c.Get("absolute")) == 0 && add < 0) add = -t.ComputeDamage(-add, this);
                        bool canKill = !c.Has("kill") || EvalInt(c.Get("kill")) != 0;
                        t.Life = Math.Max(canKill ? 0 : Math.Min(1, t.Life), Math.Min(t.Const.Life, t.Life + add));
                        if (t.Life <= 0 && !t.NoKO) t.KO = true;
                    }
                    return true;
                case "targetpoweradd":
                    foreach (var t in TargetsById(c)) t.Power = Math.Max(0, Math.Min(t.PowerMax, t.Power + EvalInt(c.Get("value"))));
                    return true;
                case "targetstate":
                    foreach (var t in TargetsById(c)) {
                        t.ForeignStates = Root.States;
                        t.StateOwner = this;
                        t.ChangeState(EvalInt(c.Get("value")), "targetstate from " + Id);
                    }
                    return true;
                case "targetfacing":
                    foreach (var t in TargetsById(c)) t.Facing = EvalInt(c.Get("value")) >= 0 ? Facing : -Facing;
                    return true;
                case "targetveladd":
                    foreach (var t in TargetsById(c)) {
                        if (c.Has("x")) t.VelX += EvalFloat(c.Get("x"));
                        if (c.Has("y")) t.VelY += EvalFloat(c.Get("y"));
                    }
                    return true;
                case "targetvelset":
                    foreach (var t in TargetsById(c)) {
                        if (c.Has("x")) t.VelX = EvalFloat(c.Get("x"));
                        if (c.Has("y")) t.VelY = EvalFloat(c.Get("y"));
                    }
                    return true;
                case "targetbind":
                    foreach (var t in TargetsById(c)) {
                        var pos = EvalPair(c, "pos", 0, 0);
                        t.WorldX = WorldX + pos[0] * Scl * Facing;
                        t.WorldY = WorldY + pos[1] * Scl;
                        t.Ghv.IsBound = true;
                        t.BindTimeLeft = Math.Max(1, ParamInt(c, "time", 1));
                        t.BoundTo = this; t.BindOffX = pos[0] * Scl; t.BindOffY = pos[1] * Scl;
                    }
                    return true;
                case "targetdrop": {
                    int keep = ParamInt(c, "excludeid", -1);
                    bool keepOne = ParamInt(c, "keepone", 1) != 0;
                    var kept = new List<Fighter>();
                    foreach (var t in Targets) {
                        if (keep >= 0 && t.Ghv.HitId == keep) { kept.Add(t); continue; }
                        t.Ghv.IsBound = false;
                    }
                    Targets.Clear();
                    if (keepOne && kept.Count > 0) Targets.Add(kept[0]); else Targets.AddRange(kept);
                    return true;
                }
                case "gamemakeanim": {
                    var e = new Explod { Owner = this, Source = SpriteSource.FightFx, IsSystem = true };
                    int no = ParamInt(c, "value", 0);
                    bool under = ParamInt(c, "under", 0) != 0;
                    var pos = EvalPair(c, "pos", 0, 0);
                    e.AnimNo = no; e.OffX = pos[0]; e.OffY = pos[1]; e.UnderStage = under;
                    e.PosX = WorldX + pos[0] * Facing * Scl; e.PosY = WorldY + pos[1] * Scl; e.Facing = Facing;
                    Engine?.AddExplod(e);
                    return true;
                }
                case "makedust": return true;          // the screenpack has no dust animation
                case "forcefeedback":
                    if (Engine != null) Engine.Vibrate = Math.Max(Engine.Vibrate, ParamInt(c, "time", 60));
                    return true;
                case "victoryquote": VictoryQuote = c.Get("value"); return true;
                case "remappal": {
                    var src = EvalPair(c, "source", 1, 1);
                    var dst = EvalPair(c, "dest", 1, 1);
                    PalRemap[(int)src[0] * 1000 + (int)src[1]] = (int)dst[0] * 1000 + (int)dst[1];
                    if ((int)src[0] == 1 && (int)src[1] == 1) PaletteNo = Math.Max(1, (int)dst[1]);
                    return true;
                }
                case "fallenvshake":
                    if (Ghv.FallEnvShakeTime > 0 && Engine != null) {
                        Engine.EnvShake(Ghv.FallEnvShakeTime, Ghv.FallEnvShakeFreq, Ghv.FallEnvShakeAmpl, Ghv.FallEnvShakePhase);
                        Ghv.FallEnvShakeTime = 0;
                    }
                    return true;
                case "displaytoclipboard":
                case "appendtoclipboard":
                case "clearclipboard":
                case "null":
                case "sndpan2":
                case "zoom":
                case "cameractrl":
                    return true;
                case "changeanim2": {
                    var owner = StateOwner;
                    int no = EvalInt(c.Get("value"));
                    var anim = owner != null && owner.Character != null && owner.Character.Air != null ? owner.Character.Air.Get(no) : null;
                    if (anim != null) { AnimNo = no; Anim = anim.Instance(); Anim.Reset(); AnimFromOwner = owner; }
                    else ChangeAnim(no);
                    return true;
                }
                case "lifebaraction": case "powerbar": case "redlifeadd": case "redlifeset": case "dizzypointsadd":
                case "guardpointsadd": case "scoreadd": case "matchrestart":
                case "assertinput": case "text": case "rankadd": case "modifysnd": case "modifybgctrl":
                case "modifystagevar": case "playbgm": case "groundleveloffset": case "printtoconsole":
                case "depth": case "shiftinput": case "transformclsn": case "modifyplayer": case "dialogue": case "loadfile": case "savefile":
                    return true;   // Ikemen-only controllers: accepted so a character does not break
            }
            return false;
        }

        /// <summary>The character whose .air an anim came from after ChangeAnim2 (null = own).</summary>
        public Fighter AnimFromOwner;
        public int BindTimeLeft;
        public Fighter BoundTo;
        public float BindOffX, BindOffY;

        void ApplyVar(StateController c, Fighter evaluator, bool add) {
            if (c.Has("v")) {
                int idx = evaluator.EvalInt(c.Get("v"));
                int value = evaluator.EvalInt(c.Get("value"));
                if (idx >= 0 && idx < Vars.Length) Vars[idx] = add ? Vars[idx] + value : value;
            } else if (c.Has("fv")) {
                int idx = evaluator.EvalInt(c.Get("fv"));
                float value = evaluator.EvalFloat(c.Get("value"));
                if (idx >= 0 && idx < FVars.Length) FVars[idx] = add ? FVars[idx] + value : value;
            } else {
                foreach (var kv in c.Params) {
                    string key = kv.Key.Trim().Lc();
                    bool isF = key.StartsWith("fvar(");
                    if (!isF && !key.StartsWith("var(")) continue;
                    int open = key.IndexOf('('), close = key.IndexOf(')');
                    if (open < 0 || close < open) continue;
                    int idx = evaluator.EvalInt(key.Substring(open + 1, close - open - 1));
                    if (isF) { float v = evaluator.EvalFloat(kv.Value); if (idx >= 0 && idx < FVars.Length) FVars[idx] = add ? FVars[idx] + v : v; }
                    else { int v = evaluator.EvalInt(kv.Value); if (idx >= 0 && idx < Vars.Length) Vars[idx] = add ? Vars[idx] + v : v; }
                }
            }
        }

        public static TransType ParseTrans(string s) {
            switch ((s ?? "").Trim().Lc()) {
                case "none": return TransType.None;
                case "add": return TransType.Add;
                case "addalpha": return TransType.Add;
                case "add1": return TransType.Add1;
                case "sub": return TransType.Sub;
                default: return TransType.Default;
            }
        }

        void ReadAfterImage(StateController c) {
            After.Time = ParamInt(c, "time", 1);
            After.Length = ParamInt(c, "length", 20);
            After.TimeGap = Math.Max(1, ParamInt(c, "timegap", 1));
            After.FrameGap = Math.Max(1, ParamInt(c, "framegap", 4));
            After.Trans = ParseTrans(c.Get("trans", "add"));
            if (After.Trans == TransType.Default) After.Trans = TransType.Add;
            var a = EvalPair(c, "paladd", 10, 10);
            After.PalAdd[0] = (int)a[0]; After.PalAdd[1] = (int)a[1];
            if (c.Has("paladd")) { var p = MugenDef.SplitCsv(c.Get("paladd")); if (p.Length > 2) After.PalAdd[2] = EvalInt(p[2]); }
            if (c.Has("palmul")) {
                var p = MugenDef.SplitCsv(c.Get("palmul"));
                for (int i = 0; i < 3 && i < p.Length; i++) if (p[i].Trim().Length > 0) After.PalMulF[i] = EvalFloat(p[i]);
            }
            After.Frames.Clear();
        }

        // ---- Helper -------------------------------------------------------------

        void SpawnHelper(StateController c) {
            if (Engine == null) return;
            int id = ParamInt(c, "id", 0);
            int stateNo = ParamInt(c, "stateno", 0);
            var h = Engine.CreateHelper(this, id, stateNo, hh => {
                hh.HelperName = MugenDef.Unquote(c.Get("name", ""));
                hh.KeyCtrl = ParamInt(c, "keyctrl", 0) != 0;
                hh.HelperIsPlayer = c.Get("helpertype", "normal").Trim().Lc() == "player";
                hh.PauseMoveTime = ParamInt(c, "pausemovetime", 0);
                hh.SuperMoveTime = ParamInt(c, "supermovetime", 0);
                if (hh.KeyCtrl) hh.SharedCommands = Root.Commands;
                int facing = ParamInt(c, "facing", 1);
                var pos = EvalPair(c, "pos", 0, 0);
                string postype = c.Get("postype", "p1").Trim().Lc();
                PlaceRelative(postype, pos[0], pos[1], facing, out hh.PosX, out hh.PosY, out hh.Facing);
                if (c.Has("size.xscale") || c.Has("size.ground.front") || c.Has("size.height") || c.Has("size.ground.back")) {
                    var cc = (CharConstants)Const.Clone();
                    if (c.Has("size.ground.back")) cc.GroundBack = EvalFloat(c.Get("size.ground.back"));
                    if (c.Has("size.ground.front")) cc.GroundFront = EvalFloat(c.Get("size.ground.front"));
                    if (c.Has("size.air.back")) cc.AirBack = EvalFloat(c.Get("size.air.back"));
                    if (c.Has("size.air.front")) cc.AirFront = EvalFloat(c.Get("size.air.front"));
                    if (c.Has("size.height")) cc.Height = EvalFloat(c.Get("size.height"));
                    hh.Const = cc;
                }
                if (c.Has("ownpal") && EvalInt(c.Get("ownpal")) != 0) hh.PaletteNo = PaletteNo;
            });
        }

        /// <summary>The `postype` rule shared by Helper, Explod and Projectile.</summary>
        /// <summary>Like <see cref="PlaceRelativeWorld"/> but in this character's own units (helpers).</summary>
        void PlaceRelative(string postype, float x, float y, int facingParam, out float px, out float py, out int facing) {
            PlaceRelativeWorld(postype, x, y, facingParam, out px, out py, out facing);
            px /= Scl; py /= Scl;
        }

        /// <summary>The `postype` rule shared by Helper, Explod and Projectile, in world units.
        /// <paramref name="x"/>/<paramref name="y"/> are in this character's units.</summary>
        void PlaceRelativeWorld(string postype, float x, float y, int facingParam, out float px, out float py, out int facing) {
            x *= Scl; y *= Scl;
            var enemy = Engine != null ? Engine.Opponent(Root) : null;
            float camL = Engine != null && Engine.Camera != null ? Engine.Camera.X - Engine.ScreenWidth / 2f : -160f;
            float camR = camL + (Engine != null ? Engine.ScreenWidth : 320f);
            switch (postype) {
                case "p2":
                    if (enemy != null) {
                        px = enemy.WorldX + x * enemy.Facing; py = enemy.WorldY + y;
                        facing = facingParam >= 0 ? enemy.Facing : -enemy.Facing;
                        return;
                    }
                    goto default;
                case "front":
                    px = (Facing >= 0 ? camR : camL) + x * Facing; py = y;
                    facing = facingParam >= 0 ? Facing : -Facing;
                    return;
                case "back":
                    px = (Facing >= 0 ? camL : camR) + x * Facing; py = y;
                    facing = facingParam >= 0 ? Facing : -Facing;
                    return;
                case "left":
                    px = camL + x; py = y; facing = facingParam >= 0 ? 1 : -1;
                    return;
                case "right":
                    px = camR + x; py = y; facing = facingParam >= 0 ? 1 : -1;
                    return;
                default:
                    px = WorldX + x * Facing; py = WorldY + y;
                    facing = facingParam >= 0 ? Facing : -Facing;
                    return;
            }
        }

        /// <summary>A keyctrl helper reads its root's command state.</summary>
        public CommandEngine SharedCommands;
        public CommandEngine ActiveCommands => SharedCommands ?? Commands;

        // ---- Explod ------------------------------------------------------------------

        Explod MakeExplod(StateController c) {
            var e = new Explod { Owner = this };
            int no;
            e.Source = ParseAnimRef(c.Get("anim", "0"), out no);
            e.AnimNo = no;
            e.Id = ParamInt(c, "id", -1);
            e.PosType = c.Get("postype", "p1").Trim().Lc();
            var pos = EvalPair(c, "pos", 0, 0);
            e.OffX = pos[0]; e.OffY = pos[1];
            int facingParam = ParamInt(c, "facing", 1);
            e.VFacing = ParamInt(c, "vfacing", 1) < 0 ? -1 : 1;
            PlaceRelativeWorld(e.PosType, pos[0], pos[1], facingParam, out e.PosX, out e.PosY, out e.Facing);
            e.OffX = pos[0] * Scl; e.OffY = pos[1] * Scl;
            e.ScaleX *= 1f; 
            e.ScreenSpace = e.PosType == "left" || e.PosType == "right" || e.PosType == "front" || e.PosType == "back" || e.PosType == "none";
            if (e.ScreenSpace && Engine != null && Engine.Camera != null) e.PosX -= Engine.Camera.X;
            e.BindTarget = e.PosType == "p2" ? (Engine != null ? Engine.Opponent(Root) : null) : this;
            var vel = EvalPair(c, c.Has("vel") ? "vel" : "velocity", 0, 0);
            e.VelX = vel[0] * e.Facing * Scl; e.VelY = vel[1] * Scl;
            var acc = EvalPair(c, "accel", 0, 0);
            e.AccelX = acc[0] * e.Facing * Scl; e.AccelY = acc[1] * Scl;
            e.BindTime = ParamInt(c, "bindtime", 0);
            e.RemoveTime = ParamInt(c, "removetime", -2);
            e.SprPriority = ParamInt(c, "sprpriority", 0);
            e.OnTop = ParamInt(c, "ontop", 0) != 0;
            e.UnderStage = ParamInt(c, "under", 0) != 0;
            var sc = EvalPair(c, "scale", 1, 1);
            e.ScaleX = sc[0]; e.ScaleY = sc[1];
            e.Angle = ParamFloat(c, "angle", 0f);
            e.RemoveOnGetHit = ParamInt(c, "removeongethit", 0) != 0;
            e.PauseMoveTime = ParamInt(c, "pausemovetime", 0);
            e.SuperMoveTime = ParamInt(c, "supermovetime", 0);
            e.IgnoreHitPause = ParamInt(c, "ignorehitpause", 1) != 0;
            if (c.Has("trans")) e.Trans = ParseTrans(c.Get("trans"));
            if (c.Has("alpha")) { var a = EvalPair(c, "alpha", 256, 0); e.AlphaSrc = (int)a[0]; e.AlphaDst = (int)a[1]; if (e.Trans == TransType.Default) e.Trans = TransType.Add; }
            e.OwnPal = ParamInt(c, "ownpal", 0) != 0;
            if (c.Has("palfx.time") || c.Has("palfx.add") || c.Has("palfx.mul")) {
                foreach (var kv in c.Params)
                    if (kv.Key.StartsWith("palfx.", StringComparison.OrdinalIgnoreCase)) e.PalFx.ReadParam(kv.Key.Substring(6), kv.Value, EvalFloat);
                if (e.PalFx.Time == 0) e.PalFx.Time = -1;
            }
            return e;
        }

        void ModifyExplod(StateController c) {
            if (Engine == null) return;
            int id = ParamInt(c, "id", -1);
            foreach (var e in Engine.Explods) {
                if (e.Owner != this || (id >= 0 && e.Id != id)) continue;
                if (c.Has("pos")) {
                    var pos = EvalPair(c, "pos", e.OffX / Scl, e.OffY / Scl);
                    e.OffX = pos[0] * Scl; e.OffY = pos[1] * Scl;
                    if (e.BindTime == 0 && e.BindTarget != null) { e.PosX = e.BindTarget.WorldX + e.OffX * e.Facing; e.PosY = e.BindTarget.WorldY + e.OffY; }
                }
                if (c.Has("vel") || c.Has("velocity")) { var v = EvalPair(c, c.Has("vel") ? "vel" : "velocity", 0, 0); e.VelX = v[0] * e.Facing * Scl; e.VelY = v[1] * Scl; }
                if (c.Has("accel")) { var a = EvalPair(c, "accel", 0, 0); e.AccelX = a[0] * e.Facing * Scl; e.AccelY = a[1] * Scl; }
                if (c.Has("bindtime")) e.BindTime = EvalInt(c.Get("bindtime"));
                if (c.Has("removetime")) { e.RemoveTime = EvalInt(c.Get("removetime")); e.Time = 0; }
                if (c.Has("sprpriority")) e.SprPriority = EvalInt(c.Get("sprpriority"));
                if (c.Has("ontop")) e.OnTop = EvalInt(c.Get("ontop")) != 0;
                if (c.Has("scale")) { var s = EvalPair(c, "scale", 1, 1); e.ScaleX = s[0]; e.ScaleY = s[1]; }
                if (c.Has("angle")) e.Angle = EvalFloat(c.Get("angle"));
                if (c.Has("trans")) e.Trans = ParseTrans(c.Get("trans"));
                if (c.Has("alpha")) { var a = EvalPair(c, "alpha", 256, 0); e.AlphaSrc = (int)a[0]; e.AlphaDst = (int)a[1]; }
                if (c.Has("facing")) e.Facing = EvalInt(c.Get("facing")) >= 0 ? Facing : -Facing;
                if (c.Has("anim")) {
                    int no; e.Source = ParseAnimRef(c.Get("anim"), out no); e.AnimNo = no; e.Anim = null;
                }
            }
        }

        // ---- Projectile ----------------------------------------------------------------

        void SpawnProjectile(StateController c) {
            if (Engine == null) return;
            var p = new Projectile { Owner = this };
            p.Hit = HitDef.Read(c, EvalFloat, Const);
            if (p.Hit.Attr == HitAttr.None) p.Hit.Attr = HitAttr.StandAttack | HitAttr.Special | HitAttr.Projectile;
            p.ProjId = ParamInt(c, "projid", 0);
            int no;
            p.Source = ParseAnimRef(c.Get("projanim", "0"), out no); p.AnimNo = no;
            if (c.Has("projhitanim")) { p.HitSource = ParseAnimRef(c.Get("projhitanim"), out no); p.HitAnim = no; }
            if (c.Has("projremanim")) { p.RemSource = ParseAnimRef(c.Get("projremanim"), out no); p.RemAnim = no; }
            else { p.RemSource = p.HitSource; p.RemAnim = p.HitAnim; }
            if (c.Has("projcancelanim")) { p.CancelSource = ParseAnimRef(c.Get("projcancelanim"), out no); p.CancelAnim = no; }
            else { p.CancelSource = p.RemSource; p.CancelAnim = p.RemAnim; }
            var off = EvalPair(c, "offset", 0, 0);
            string postype = c.Get("postype", "p1").Trim().Lc();
            PlaceRelativeWorld(postype, off[0], off[1], 1, out p.PosX, out p.PosY, out p.Facing);
            var vel = EvalPair(c, "velocity", 0, 0);
            p.VelX = vel[0] * Scl; p.VelY = vel[1] * Scl;
            var acc = EvalPair(c, "accel", 0, 0);
            p.AccelX = acc[0] * Scl; p.AccelY = acc[1] * Scl;
            var vm = EvalPair(c, "velmul", 1, 1);
            p.VelMulX = vm[0]; p.VelMulY = vm[1];
            var rv = EvalPair(c, "projremvelocity", 0, 0);
            p.RemVelX = rv[0] * Scl; p.RemVelY = rv[1] * Scl;
            var sc = EvalPair(c, "projscale", 1, 1);
            p.ScaleX = sc[0] * Scl; p.ScaleY = sc[1] * Scl;
            p.HitsLeft = ParamInt(c, "projhits", 1);
            p.MissTime = ParamInt(c, "projmisstime", 0);
            p.Priority = ParamInt(c, "projpriority", 1);
            p.SprPriority = ParamInt(c, "projsprpriority", 3);
            p.EdgeBound = ParamFloat(c, "projedgebound", 40f) * Scl;
            p.StageBound = ParamFloat(c, "projstagebound", 40f) * Scl;
            var hb = EvalPair(c, "projheightbound", -240, 1);
            p.HeightLow = hb[0] * Scl; p.HeightHigh = hb[1] * Scl;
            p.RemoveTime = ParamInt(c, "projremovetime", -1);
            p.RemoveOnHit = ParamInt(c, "projremove", 1) != 0;
            p.PauseMoveTime = ParamInt(c, "pausemovetime", 0);
            p.SuperMoveTime = ParamInt(c, "supermovetime", 0);
            if (c.Has("palfx.time")) foreach (var kv in c.Params)
                if (kv.Key.StartsWith("palfx.", StringComparison.OrdinalIgnoreCase)) p.PalFx.ReadParam(kv.Key.Substring(6), kv.Value, EvalFloat);
            Engine.AddProjectile(p);
        }

        // ---- Pause / SuperPause ---------------------------------------------------------

        void DoPause(StateController c, bool super) {
            if (Engine == null) return;
            int time = ParamInt(c, "time", super ? 30 : 30);
            int moveTime = ParamInt(c, "movetime", 0);
            if (super) {
                Engine.StartSuperPause(this, time, moveTime, ParamInt(c, "darken", 1) != 0,
                                       ParamFloat(c, "p2defmul", 1f), ParamInt(c, "unhittable", 1) != 0);
                Power = Math.Max(0, Math.Min(PowerMax, Power + ParamInt(c, "poweradd", 0)));
                // the flash animation, from fightfx unless `anim = S…`
                string animRef = c.Get("anim", "30");
                if (animRef.Trim() != "-1") {
                    // SuperPause: no prefix = fightfx, `S` = the character's own .air
                    string ar = animRef.Trim();
                    bool own = ar.Length > 1 && (ar[0] == 'S' || ar[0] == 's') && !char.IsLetter(ar[1]);
                    int no; ParseAnimRef(ar, out no);
                    var src = own ? SpriteSource.Character : SpriteSource.FightFx;
                    if (no >= 0) {
                        var pos = EvalPair(c, "pos", 0, 0);
                        var e = new Explod {
                            Owner = this, Source = src, AnimNo = no, IsSystem = true, OnTop = true,
                            PosX = WorldX + pos[0] * Facing * Scl, PosY = WorldY + pos[1] * Scl, Facing = Facing,
                            SuperMoveTime = 9999, PauseMoveTime = 9999, SprPriority = 5,
                        };
                        Engine.AddExplod(e);
                    }
                }
                string snd = c.Get("sound", "");
                if (snd.Trim().Length > 0) {
                    int g, n;
                    var parts = snd.Trim();
                    bool own = parts.Length > 1 && (parts[0] == 'S' || parts[0] == 's');
                    ParseSoundRef(snd, EvalFloat, out g, out n);
                    QueueSound(!own, g, n);
                }
            } else {
                Engine.StartPause(this, time, moveTime, ParamInt(c, "endcmdbuftime", 0));
            }
        }

        // ---- the dev.5 triggers ---------------------------------------------------------

        bool TryTriggerEx(string name, string arg, float argValue, out float value) {
            value = 0f;
            var e = Engine;
            if (name.StartsWith("zss__", StringComparison.Ordinal)) {
                ZssLocals.TryGetValue(name.Substring(5), out value);
                return true;
            }
            switch (name) {
                case "ishelper": value = IsHelper && (argValue <= 0 || HelperId == (int)argValue) ? 1 : 0; return true;
                case "numhelper": value = e != null ? e.CountHelpers(Root, argValue <= 0 ? -1 : (int)argValue) : 0; return true;
                case "numexplod": value = e != null ? e.CountExplods(this, argValue <= 0 ? -1 : (int)argValue) : 0; return true;
                case "numproj": value = e != null ? e.CountProjectiles(Root, -1) : 0; return true;
                case "numprojid": value = e != null ? e.CountProjectiles(Root, (int)argValue) : 0; return true;
                case "numtarget": {
                    int n = 0, id = (int)argValue;
                    foreach (var t in Targets) if (id <= 0 || t.Ghv.HitId == id) n++;
                    value = n; return true;
                }
                case "numenemy": value = e != null && e.Opponent(Root) != null ? 1 : 0; return true;
                case "numpartner": value = 0; return true;
                case "id": value = Id; return true;
                case "playeridexist": value = e != null && e.FindById((int)argValue) != null ? 1 : 0; return true;
                case "parentdist x": value = Parent != null ? (Parent.WorldX - WorldX) * Facing / Scl : 0; return true;
                case "parentdist y": value = Parent != null ? (Parent.WorldY - WorldY) / Scl : 0; return true;
                case "rootdist x": value = (Root.PosX - PosX) * Facing; return true;
                case "rootdist y": value = Root.PosY - PosY; return true;
                case "teamside": value = PlayerNo + 1; return true;
                case "teammode": value = arg == null ? 0 : (arg.Trim().Lc() == "single" ? 1 : 0); return true;
                case "name": value = StrEq(arg, Character != null ? Character.Name : ""); return true;
                case "p1name": value = StrEq(arg, Root.Character != null ? Root.Character.Name : ""); return true;
                case "p2name": {
                    var o = e != null ? e.Opponent(Root) : null;
                    value = o != null && o.Character != null ? StrEq(arg, o.Character.Name) : 0; return true;
                }
                case "p3name": case "p4name": value = 0; return true;
                case "authorname": value = StrEq(arg, Character != null ? Character.Author : ""); return true;
                case "palno": value = PaletteNo; return true;
                case "ailevel": value = Root.AiLevel; return true;
                case "ailevelf": value = Root.AiLevel; return true;
                case "standby": value = 0; return true;
                case "pos z": case "vel z": case "p2dist z": case "p2bodydist z": case "screenpos z": case "camerapos z": value = 0; return true;
                case "mugenversion": {
                    string mv = Character != null ? Character.MugenVersion ?? "" : "";
                    float major = mv.StartsWith("1") ? 1 : mv.Length == 0 ? 1 : 0;
                    value = arg != null && arg.Trim().Lc() == "minor" ? (mv.StartsWith("1.1") ? 1 : 0) : major;
                    return true;
                }
                case "const240p": value = argValue * (Character != null ? Character.LocalCoordHeight / 240f : 1f); return true;
                case "const480p": value = argValue * (Character != null ? Character.LocalCoordHeight / 480f : 0.5f); return true;
                case "const720p": value = argValue * (Character != null ? Character.LocalCoordHeight / 720f : 1f / 3f); return true;
                case "const1080p": value = argValue * (Character != null ? Character.LocalCoordHeight / 1080f : 0.25f); return true;
                case "map": {
                    float v;
                    value = arg != null && Maps.TryGetValue(arg.Trim().Lc(), out v) ? v : 0f;
                    return true;
                }
                case "ishometeam": value = PlayerNo == 0 ? 1 : 0; return true;
                case "matchno": value = e != null ? e.MatchNo : 1; return true;
                case "drawgame": value = e != null && e.MatchOver && e.Wins[0] == e.Wins[1] ? 1 : 0; return true;
                case "winko": value = e != null && e.RoundWinner == PlayerNo + 1 && e.LastRoundKO ? 1 : 0; return true;
                case "wintime": value = e != null && e.RoundWinner == PlayerNo + 1 && !e.LastRoundKO ? 1 : 0; return true;
                case "winperfect": value = e != null && e.RoundWinner == PlayerNo + 1 && Root.Life >= Root.LifeMax ? 1 : 0; return true;
                case "loseko": value = e != null && e.RoundWinner == 2 - PlayerNo && e.LastRoundKO ? 1 : 0; return true;
                case "losetime": value = e != null && e.RoundWinner == 2 - PlayerNo && !e.LastRoundKO ? 1 : 0; return true;
                case "hitvel x": value = Ghv.XVel; return true;
                case "hitvel y": value = Ghv.YVel; return true;
                case "gameheight": case "screenheight": value = (e != null ? e.ScreenWidth * 0.75f : 240f) / Scl; return true;
                case "gamewidth": case "screenwidth": value = (e != null ? e.ScreenWidth : 320f) / Scl; return true;
                case "tickspersecond": value = 60; return true;
                case "sysvar": { int i = (int)argValue; value = i == 1 ? SysVar1 : i >= 0 && i < SysVars.Length ? SysVars[i] : 0; return true; }
                case "jugglepoints": value = Root.JugglePoints; return true;
                case "sysfvar": { int i = (int)argValue; value = i >= 0 && i < SysFVars.Length ? SysFVars[i] : 0; return true; }
                case "pausetime": value = e != null ? e.PauseTimeFor(this) : 0; return true;
                case "majorversion": value = 1; return true;
                case "ikemenversion": value = 0; return true;
                case "receiveddamage": value = Ghv.Damage; return true;
                case "receivedhits": value = Ghv.HitCount; return true;
                case "camerazoom": value = 1; return true;
                case "leftedge": value = e != null && e.Camera != null ? e.Camera.X - e.ScreenWidth / 2f : -160f; return true;
                case "rightedge": value = e != null && e.Camera != null ? e.Camera.X + e.ScreenWidth / 2f : 160f; return true;
                case "topedge": value = e != null && e.Camera != null ? e.Camera.Y - 240f : -240f; return true;
                case "bottomedge": value = e != null && e.Camera != null ? e.Camera.Y : 0f; return true;
                case "backedge": value = Facing >= 0 ? (e != null && e.Camera != null ? e.Camera.X - e.ScreenWidth / 2f : -160f)
                                                     : (e != null && e.Camera != null ? e.Camera.X + e.ScreenWidth / 2f : 160f); return true;
                case "frontedge": value = Facing >= 0 ? (e != null && e.Camera != null ? e.Camera.X + e.ScreenWidth / 2f : 160f)
                                                      : (e != null && e.Camera != null ? e.Camera.X - e.ScreenWidth / 2f : -160f); return true;
                case "stagevar": value = 0; return true;
                case "isassertedmatch": case "isasserted": value = 0; return true;
                case "projcanceltime": case "projhittime": case "projcontacttime": case "projguardedtime": {
                    var dict = name == "projhittime" ? Root.ProjHitAt : name == "projguardedtime" ? Root.ProjGuardedAt : Root.ProjContactAt;
                    int at;
                    value = dict.TryGetValue((int)argValue, out at) ? Root.Time - at : -1;
                    return true;
                }
            }
            // `ProjContact1200 = 1`, `ProjHit = 1, < 15`, `ProjGuarded`: true within 15 ticks
            foreach (var prefix in ProjPrefixes) {
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string rest = name.Substring(prefix.Length);
                int id = 0;
                if (rest.Length > 0 && !int.TryParse(rest, out id)) break;
                var dict = prefix == "projhit" ? Root.ProjHitAt : prefix == "projguarded" ? Root.ProjGuardedAt : Root.ProjContactAt;
                value = 0;
                foreach (var kv in dict)
                    if ((rest.Length == 0 || kv.Key == id) && Root.Time - kv.Value <= 15) { value = 1; break; }
                return true;
            }
            return false;
        }

        static readonly string[] ProjPrefixes = { "projcontact", "projguarded", "projhit" };

        static float StrEq(string a, string b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase) ? 1f : 0f;

        /// <summary>Generic `const(section.key[.x|.y])` from the character's own .cns header.</summary>
        float ConstFromHeader(string key) {
            var hdr = States != null ? States.Header : null;
            if (hdr == null || string.IsNullOrEmpty(key)) return 0f;
            string k = key.Lc();
            int dot = k.IndexOf('.');
            if (dot <= 0) return 0f;
            string section = k.Substring(0, dot);
            string rest = k.Substring(dot + 1);
            int idx = 0;
            if (rest.EndsWith(".x")) { rest = rest.Substring(0, rest.Length - 2); idx = 0; }
            else if (rest.EndsWith(".y")) { rest = rest.Substring(0, rest.Length - 2); idx = 1; }
            var sec = hdr[section];
            if (sec == null) return 0f;
            string raw = sec.Get(rest, null);
            if (raw == null) return 0f;
            var parts = MugenDef.SplitCsv(raw);
            if (idx >= parts.Length || !MugenDef.IsNumeric(parts[idx])) return 0f;
            return MugenDef.Atof(parts[idx]);
        }
    }
}
