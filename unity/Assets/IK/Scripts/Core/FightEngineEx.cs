using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>
    /// dev.5 half of <see cref="FightEngine"/>: every character on the field (players and
    /// helpers), explods and hit sparks, projectiles, Pause / SuperPause, EnvShake, the global
    /// palette effects and the sounds of the tick.
    /// Reference: `engine/ikemen-go/src/system.go` (`action`, `pause`, `superpause`,
    /// `envShake`), `char.go` (`Explod`, `Projectile`, helpers).
    /// </summary>
    public partial class FightEngine {
        /// <summary>Every character in the fight: players first, then helpers in creation order.</summary>
        public readonly List<Fighter> Chars = new List<Fighter>();
        public readonly List<Explod> Explods = new List<Explod>();
        public readonly List<Projectile> Projectiles = new List<Projectile>();
        /// <summary>Sounds requested this tick (cleared at the start of every tick).</summary>
        public readonly List<SoundEvent> Sounds = new List<SoundEvent>();
        /// <summary>StopSnd requests of this tick: owner and channel (-1 = all).</summary>
        public readonly List<KeyValuePair<Fighter, int>> StopChannels = new List<KeyValuePair<Fighter, int>>();

        /// <summary>fightfx.air / fightfx.sff (hit sparks, superpause flash, `F` animations).</summary>
        public AirFile FxAir;
        public SffFile FxSff;

        public readonly PalFx AllPalFx = new PalFx();
        public readonly PalFx BgPalFx = new PalFx();

        /// <summary>`matchno`: set by the front end for arcade/survival ladders.</summary>
        public int MatchNo = 1;
        /// <summary>The last round ended by a KO (false = time over).</summary>
        public bool LastRoundKO;
        /// <summary>ForceFeedback ticks left (the front end vibrates the phone).</summary>
        public int Vibrate;

        // ---- pause ----------------------------------------------------------------
        public int SuperPauseTime, PauseTime;
        public Fighter SuperPauseOwner, PauseOwner;
        public bool SuperPauseDarken;
        public float SuperPauseP2DefMul = 1f;
        public bool Paused => SuperPauseTime > 0 || PauseTime > 0;

        // ---- EnvShake -----------------------------------------------------------------
        public int ShakeTime;
        public float ShakeFreq, ShakeAmpl, ShakePhase;
        /// <summary>Vertical screen offset of this tick in stage units (renderer adds it).</summary>
        public float ShakeY { get; private set; }

        int nextHelperId = 1000;
        public const int MaxHelpersPerPlayer = 56;
        public const int MaxExplods = 512;

        // ---- helpers ----------------------------------------------------------------

        public Fighter CreateHelper(Fighter parent, int helperId, int stateNo, Action<Fighter> init) {
            if (CountHelpers(parent.Root, -1) >= MaxHelpersPerPlayer) return null;
            var h = new Fighter(parent, helperId, stateNo);
            h.Id = nextHelperId++;
            h.Engine = this;
            init?.Invoke(h);
            Chars.Add(h);
            h.ChangeState(stateNo, "helper " + helperId + " spawned by " + parent.Id);
            if (h.AnimNo < 0) h.ChangeAnim(0);
            return h;
        }

        public Fighter FindHelper(Fighter root, int helperId) {
            foreach (var c in Chars)
                if (c.IsHelper && !c.Destroyed && c.Root == root && (helperId < 0 || c.HelperId == helperId)) return c;
            return null;
        }

        public Fighter FindHelperByIndex(Fighter root, int index) {
            int i = 0;
            foreach (var c in Chars) {
                if (!c.IsHelper || c.Destroyed || c.Root != root) continue;
                if (i++ == index) return c;
            }
            return null;
        }

        public Fighter FindById(int id) {
            foreach (var c in Chars) if (!c.Destroyed && c.Id == id) return c;
            return null;
        }

        public int CountHelpers(Fighter root, int helperId) {
            int n = 0;
            foreach (var c in Chars)
                if (c.IsHelper && !c.Destroyed && c.Root == root && (helperId < 0 || c.HelperId == helperId)) n++;
            return n;
        }

        void RemoveDestroyedHelpers() {
            for (int i = Chars.Count - 1; i >= 0; i--) {
                var c = Chars[i];
                if (!c.IsHelper || !c.Destroyed) continue;
                Chars.RemoveAt(i);
                foreach (var o in Chars) {
                    o.Targets.Remove(c);
                    if (o.Parent == c) o.Parent = null;
                }
            }
        }

        void ClearRoundEntities() {
            for (int i = Chars.Count - 1; i >= 0; i--) if (Chars[i].IsHelper) Chars.RemoveAt(i);
            Explods.Clear();
            Projectiles.Clear();
            SuperPauseTime = PauseTime = 0;
            SuperPauseOwner = PauseOwner = null;
            ShakeTime = 0;
            ShakeY = 0f;
            AllPalFx.Clear();
            BgPalFx.Clear();
        }

        // ---- explods and sparks ---------------------------------------------------------

        public void AddExplod(Explod e) {
            if (e == null || Explods.Count >= MaxExplods) return;
            ResolveAnim(e);
            if (e.Anim == null) return;
            Explods.Add(e);
        }

        public void AddSpark(Fighter attacker, int no, bool fromChar, float x, float y, int facing) {
            var e = new Explod {
                Owner = attacker, Source = fromChar ? SpriteSource.Character : SpriteSource.FightFx,
                AnimNo = no, PosX = x, PosY = y, Facing = facing, IsSystem = true, OnTop = true,
                SprPriority = 5, RemoveTime = -2,
            };
            AddExplod(e);
        }

        public void RemoveExplods(Fighter owner, int id) {
            foreach (var e in Explods) if (e.Owner == owner && (id < 0 || e.Id == id) && !e.IsSystem) e.Removed = true;
        }

        public void RemoveOnGetHit(Fighter owner) {
            foreach (var e in Explods) if (e.Owner == owner && e.RemoveOnGetHit) e.Removed = true;
        }

        public int CountExplods(Fighter owner, int id) {
            int n = 0;
            foreach (var e in Explods) if (!e.Removed && !e.IsSystem && e.Owner == owner && (id < 0 || e.Id == id)) n++;
            return n;
        }

        MugenAnimation LookupAnim(SpriteSource src, Fighter owner, int no) {
            AirFile air = src == SpriteSource.FightFx ? FxAir : owner != null && owner.Character != null ? owner.Character.Air : null;
            var a = air != null ? air.Get(no) : null;
            return a != null ? a.Instance() : null;
        }

        void ResolveAnim(Explod e) {
            if (e.Anim != null) return;
            e.Anim = LookupAnim(e.Source, e.Owner, e.AnimNo);
        }

        bool EntityFrozen(Fighter owner, int superMove, int pauseMove, bool ignoreHitPause, bool system) {
            if (SuperPauseTime > 0 && superMove <= 0 && !(system && owner == SuperPauseOwner)) return true;
            if (PauseTime > 0 && pauseMove <= 0 && !(system && owner == PauseOwner)) return true;
            if (!ignoreHitPause && owner != null && owner.InHitPause) return true;
            return false;
        }

        void TickExplods(bool paused) {
            for (int i = 0; i < Explods.Count; i++) {
                var e = Explods[i];
                if (e.Removed) continue;
                if (paused && EntityFrozen(e.Owner, e.SuperMoveTime, e.PauseMoveTime, e.IgnoreHitPause, e.IsSystem && e.SuperMoveTime > 0)) continue;
                if (!paused && !e.IgnoreHitPause && e.Owner != null && e.Owner.InHitPause) continue;
                ResolveAnim(e);
                if (e.Anim == null) { e.Removed = true; continue; }
                var bt = e.BindTarget;
                if (e.BindTime != 0 && bt != null && !e.ScreenSpace && !bt.Destroyed) {
                    e.PosX = bt.WorldX + e.OffX * bt.Facing;
                    e.PosY = bt.WorldY + e.OffY;
                    if (e.BindTime > 0) e.BindTime--;
                } else {
                    e.PosX += e.VelX;
                    e.PosY += e.VelY;
                    e.VelX += e.AccelX;
                    e.VelY += e.AccelY;
                }
                if (e.Time > 0) e.Anim.Tick();
                e.Time++;
                e.PalFx.Tick();
            }
            Explods.RemoveAll(x => x.Finished);
        }

        // ---- projectiles ---------------------------------------------------------------------

        public void AddProjectile(Projectile p) {
            p.Anim = LookupAnim(p.Source, p.Owner, p.AnimNo);
            if (p.Anim == null) return;
            Projectiles.Add(p);
        }

        public int CountProjectiles(Fighter root, int projId) {
            int n = 0;
            foreach (var p in Projectiles)
                if (!p.Dead && p.Owner != null && p.Owner.Root == root && (projId < 0 || p.ProjId == projId)) n++;
            return n;
        }

        void SwitchProjectile(Projectile p, Projectile.Phase phase, SpriteSource src, int anim) {
            p.State = phase;
            p.VelX = p.RemVelX; p.VelY = p.RemVelY;
            p.AccelX = p.AccelY = 0f;
            if (anim < 0) { p.State = Projectile.Phase.Dead; return; }
            var a = LookupAnim(src, p.Owner, anim);
            if (a == null) { p.State = Projectile.Phase.Dead; return; }
            p.Anim = a;
        }

        void TickProjectiles(bool paused) {
            float camL = Camera != null ? Camera.X - ScreenWidth / 2f : -160f;
            float camR = camL + ScreenWidth;
            foreach (var p in Projectiles) {
                if (p.Dead) continue;
                if (paused && EntityFrozen(p.Owner, p.SuperMoveTime, p.PauseMoveTime, true, false)) continue;
                if (p.HitPause > 0) { p.HitPause--; continue; }
                p.PosX += p.VelX * p.Facing;
                p.PosY += p.VelY;
                if (p.State == Projectile.Phase.Flying) {
                    p.VelX = p.VelX * p.VelMulX + p.AccelX;
                    p.VelY = p.VelY * p.VelMulY + p.AccelY;
                    if (p.MissTimer > 0) p.MissTimer--;
                    p.Time++;
                    bool expired = p.RemoveTime >= 0 && p.Time >= p.RemoveTime;
                    bool offScreen = p.PosX < camL - p.EdgeBound || p.PosX > camR + p.EdgeBound;
                    bool offStage = Stage != null && (p.PosX < Stage.BoundLeft - p.StageBound || p.PosX > Stage.BoundRight + p.StageBound);
                    bool offHeight = p.PosY < p.HeightLow || p.PosY > p.HeightHigh;
                    if (expired) SwitchProjectile(p, Projectile.Phase.Removing, p.RemSource, p.RemAnim);
                    else if (offScreen || offStage || offHeight) p.State = Projectile.Phase.Dead;
                } else if (p.Anim == null || (p.Anim.LoopEnd && p.Anim.TotalTime != -1) || p.Anim.TotalTime == -1 && p.Time > 600) {
                    p.State = Projectile.Phase.Dead;
                } else p.Time++;
                if (!p.Dead && p.Anim != null) p.Anim.Tick();
                p.PalFx.Tick();
            }
            Projectiles.RemoveAll(x => x.Dead);
        }

        /// <summary>World boxes of an animation frame at a position (projectiles).</summary>
        public static List<float[]> BoxesOf(MugenAnimation anim, int group, float x, float y, int facing, float sx, float sy) {
            var result = new List<float[]>();
            var frame = anim != null ? anim.CurrentFrame : null;
            if (frame == null) return result;
            var boxes = group == 1 ? frame.Clsn1 : frame.Clsn2;
            foreach (var b in boxes) {
                float l, r;
                if (facing >= 0) { l = x + b[0] * sx; r = x + b[2] * sx; }
                else { l = x - b[2] * sx; r = x - b[0] * sx; }
                result.Add(new[] { l, y + b[1] * sy, r, y + b[3] * sy });
            }
            return result;
        }

        void CheckProjectileHits() {
            // projectile against projectile: the lower priority is cancelled
            for (int i = 0; i < Projectiles.Count; i++) {
                var a = Projectiles[i];
                if (a.State != Projectile.Phase.Flying) continue;
                for (int j = i + 1; j < Projectiles.Count; j++) {
                    var b = Projectiles[j];
                    if (b.State != Projectile.Phase.Flying || a.Owner == null || b.Owner == null) continue;
                    if (a.Owner.PlayerNo == b.Owner.PlayerNo) continue;
                    var ba = BoxesOf(a.Anim, 1, a.PosX, a.PosY, a.Facing, a.ScaleX, a.ScaleY);
                    var bb = BoxesOf(b.Anim, 1, b.PosX, b.PosY, b.Facing, b.ScaleX, b.ScaleY);
                    if (ba.Count == 0) ba = BoxesOf(a.Anim, 2, a.PosX, a.PosY, a.Facing, a.ScaleX, a.ScaleY);
                    if (bb.Count == 0) bb = BoxesOf(b.Anim, 2, b.PosX, b.PosY, b.Facing, b.ScaleX, b.ScaleY);
                    if (!Fighter.BoxesOverlap(ba, bb)) continue;
                    if (a.Priority > b.Priority) { a.Priority--; Cancel(b); }
                    else if (b.Priority > a.Priority) { b.Priority--; Cancel(a); }
                    else { Cancel(a); Cancel(b); }
                }
            }
            // projectile against characters
            foreach (var p in Projectiles) {
                if (p.State != Projectile.Phase.Flying || p.MissTimer > 0 || p.Owner == null) continue;
                var hd = p.Hit;
                if (hd == null || !hd.IsValid) continue;
                var boxes = BoxesOf(p.Anim, 1, p.PosX, p.PosY, p.Facing, p.ScaleX, p.ScaleY);
                if (boxes.Count == 0) continue;
                foreach (var b in new List<Fighter>(Chars)) {
                    if (b.Destroyed || b.PlayerNo == p.Owner.PlayerNo) continue;
                    if (b.UnhittableTime > 0 || !b.HittableBy(hd.Attr)) continue;
                    if (!HitFlagAllows(hd, b)) continue;
                    if (!Fighter.BoxesOverlap(boxes, b.WorldClsn(2))) continue;
                    bool guarded = CanGuard(b, hd);
                    hd.Targets.Clear();
                    b.ApplyHit(p.Owner, hd, guarded);
                    var root = p.Owner.Root;
                    root.ProjContactAt[p.ProjId] = root.Time;
                    if (guarded) root.ProjGuardedAt[p.ProjId] = root.Time; else root.ProjHitAt[p.ProjId] = root.Time;
                    if (!guarded && !p.Owner.Targets.Contains(b)) p.Owner.Targets.Add(b);
                    HitsThisTick.Add(new[] { p.Owner.PlayerNo, b.PlayerNo, guarded ? 1 : 0 });
                    float front = b.Type == StateType.Air ? b.Const.AirFront : b.Const.GroundFront;
                    int sn = guarded ? hd.GuardSparkNo : hd.SparkNo;
                    if (sn >= 0) AddSpark(p.Owner, sn, guarded ? hd.GuardSparkFromChar : hd.SparkFromChar,
                                          b.WorldX - p.Facing * front * b.Scl + p.Facing * hd.SparkXY[0] * p.Owner.Scl, p.PosY + hd.SparkXY[1] * p.Owner.Scl, p.Facing);
                    var snd = guarded ? hd.GuardSound : hd.HitSound;
                    if (snd[0] >= 0) p.Owner.QueueSound(!(guarded ? hd.GuardSoundFromChar : hd.HitSoundFromChar), snd[0], snd[1]);
                    p.HitPause = Math.Max(0, guarded ? hd.GuardPauseTime[0] : hd.PauseTime[0]);
                    p.HitsLeft--;
                    if (p.HitsLeft <= 0) {
                        if (p.RemoveOnHit) SwitchProjectile(p, Projectile.Phase.Hit, p.HitSource, p.HitAnim);
                    } else p.MissTimer = p.MissTime;
                    break;
                }
            }
        }

        void Cancel(Projectile p) {
            var root = p.Owner != null ? p.Owner.Root : null;
            SwitchProjectile(p, Projectile.Phase.Canceled, p.CancelSource, p.CancelAnim);
            if (root != null) root.ProjContactAt[p.ProjId] = root.Time;
        }

        // ---- ReversalDef -----------------------------------------------------------------------

        bool TryReversal(Fighter a, Fighter b, HitDef hd) {
            if (!b.ReversalActive || b.Reversal == null) return false;
            if (!Fighter.AttrMatches(b.ReversalAttr, hd.Attr)) return false;
            if (!Fighter.BoxesOverlap(b.WorldClsn(1), a.WorldClsn(1))) return false;
            var r = b.Reversal;
            b.ReversalActive = false;
            a.HitDefActive = false;
            a.MoveReversedFlag = 1;
            b.MoveContactFlag = 1; b.MoveHitFlag = 1;
            b.HitPauseTime = Math.Max(0, r.PauseTime[0]);
            a.HitPauseTime = Math.Max(0, r.PauseTime[1]);
            a.Ghv.HitId = r.Id;
            if (!b.Targets.Contains(a)) b.Targets.Add(a);
            if (r.SparkNo >= 0) AddSpark(b, r.SparkNo, r.SparkFromChar, (a.WorldX + b.WorldX) / 2f, b.WorldY + r.SparkXY[1] * b.Scl, b.Facing);
            if (r.HitSound[0] >= 0) b.QueueSound(!r.HitSoundFromChar, r.HitSound[0], r.HitSound[1]);
            if (r.P1StateNo >= 0) b.ChangeState(r.P1StateNo, "reversaldef");
            if (r.P2StateNo >= 0) {
                a.ForeignStates = r.P2GetP1State ? b.Root.States : null;
                a.StateOwner = r.P2GetP1State ? b : null;
                a.ChangeState(r.P2StateNo, "reversed by " + b.Id);
            }
            return true;
        }

        // ---- Pause / SuperPause ----------------------------------------------------------------

        public void StartSuperPause(Fighter owner, int time, int moveTime, bool darken, float p2DefMul, bool unhittable) {
            SuperPauseTime = Math.Max(1, time);
            SuperPauseOwner = owner;
            SuperPauseDarken = darken;
            SuperPauseP2DefMul = p2DefMul;
            owner.SuperMoveLeft = Math.Max(0, moveTime);
            foreach (var c in Chars) if (c != owner) c.SuperMoveLeft = c.SuperMoveTime;
            if (unhittable) owner.UnhittableTime = Math.Max(owner.UnhittableTime, SuperPauseTime + moveTime);
        }

        public void StartPause(Fighter owner, int time, int moveTime, int endCmdBufTime) {
            PauseTime = Math.Max(1, time);
            PauseOwner = owner;
            owner.PauseMoveLeft = Math.Max(0, moveTime);
            foreach (var c in Chars) if (c != owner) c.PauseMoveLeft = c.PauseMoveTime;
        }

        bool IsFrozen(Fighter f) {
            if (SuperPauseTime > 0) {
                if (f.SuperMoveLeft > 0) { f.SuperMoveLeft--; return false; }
                return true;
            }
            if (PauseTime > 0) {
                if (f.PauseMoveLeft > 0) { f.PauseMoveLeft--; return false; }
                return true;
            }
            return false;
        }

        void StepPause() {
            // a pause started this tick by a state counts from the next tick
            if (SuperPauseTime > 0) { SuperPauseTime--; if (SuperPauseTime == 0) SuperPauseOwner = null; }
            else if (PauseTime > 0) { PauseTime--; if (PauseTime == 0) PauseOwner = null; }
        }

        public int PauseTimeFor(Fighter f) => SuperPauseTime > 0 ? SuperPauseTime : PauseTime;

        // ---- EnvShake ----------------------------------------------------------------------------

        public void EnvShake(int time, float freq, float ampl, float phase) {
            ShakeTime = Math.Max(0, time);
            ShakeFreq = Math.Max(0f, freq) * (float)Math.PI / 180f;
            ShakeAmpl = ampl;
            ShakePhase = float.IsNaN(phase) ? (freq >= 90f ? 0f : (float)Math.PI / 2f) : phase * (float)Math.PI / 180f;
        }

        void StepShake() {
            if (ShakeTime <= 0) { ShakeY = 0f; return; }
            ShakeY = (float)Math.Sin(ShakePhase) * ShakeAmpl;
            ShakePhase += ShakeFreq;
            ShakeTime--;
        }

        // ---- drawing order -----------------------------------------------------------------------

        /// <summary>Everything to draw this frame, back to front: helpers/players by
        /// `sprpriority`, explods and projectiles mixed in by their own priority.</summary>
        public List<object> DrawList() => DrawList(new List<object>());

        readonly List<KeyValuePair<int, object>> drawScratch = new List<KeyValuePair<int, object>>();
        static readonly Comparison<KeyValuePair<int, object>> byDrawKey = (x, y) => x.Key.CompareTo(y.Key);

        /// <summary>dev.7: allocation-free draw order into a caller-owned list (same order as before).</summary>
        public List<object> DrawList(List<object> result) {
            var list = drawScratch;
            list.Clear();
            result.Clear();
            int order = 0;
            for (int i = 0; i < Explods.Count; i++) { var e = Explods[i]; if (!e.Removed && !e.OnTop) list.Add(new KeyValuePair<int, object>(e.SprPriority * 4096 + order++, e)); }
            for (int i = 0; i < Chars.Count; i++) { var c = Chars[i]; if (!c.Destroyed) list.Add(new KeyValuePair<int, object>(c.SprPriority * 4096 + order++ + (c.Move == MoveType.Attack ? 2048 : 0), c)); }
            for (int i = 0; i < Projectiles.Count; i++) { var p = Projectiles[i]; if (!p.Dead) list.Add(new KeyValuePair<int, object>(p.SprPriority * 4096 + order++, p)); }
            list.Sort(byDrawKey);
            for (int i = 0; i < list.Count; i++) result.Add(list[i].Value);
            for (int i = 0; i < Explods.Count; i++) { var e = Explods[i]; if (!e.Removed && e.OnTop) result.Add(e); }
            list.Clear();
            return result;
        }
    }
}
