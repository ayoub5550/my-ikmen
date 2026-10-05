using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>
    /// A palette effect (`PalFX`, `AllPalFX`, `BGPalFX`, HitDef `palfx.*`, Explod `palfx`).
    /// Values follow MUGEN: add/mul are 0..256 per channel (mul 256 = unchanged), `sinadd`
    /// adds a sine wave with `period` ticks, `color` 0..256 desaturates (256 = full colour),
    /// `invertall` inverts. The renderer turns <see cref="Current"/> into shader parameters.
    /// Reference: char.go `PalFX`, `palFX.go` in Ikemen GO.
    /// </summary>
    public class PalFx {
        public int Time;                 // -1 = forever, 0 = off
        public int Elapsed;
        public int[] Add = { 0, 0, 0 };
        public int[] Mul = { 256, 256, 256 };
        public int[] SinAdd = { 0, 0, 0 };
        public int SinPeriod;
        public float Color = 256f;
        public bool Invert;

        public bool Active => Time < 0 || (Time > 0 && Elapsed < Time);

        public void Clear() {
            Time = 0; Elapsed = 0; SinPeriod = 0; Color = 256f; Invert = false;
            for (int i = 0; i < 3; i++) { Add[i] = 0; Mul[i] = 256; SinAdd[i] = 0; }
        }

        public void CopyFrom(PalFx o) {
            Time = o.Time; Elapsed = 0; SinPeriod = o.SinPeriod; Color = o.Color; Invert = o.Invert;
            for (int i = 0; i < 3; i++) { Add[i] = o.Add[i]; Mul[i] = o.Mul[i]; SinAdd[i] = o.SinAdd[i]; }
        }

        public void Tick() { if (Time > 0 && Elapsed < Time) Elapsed++; }

        /// <summary>Reads one parameter of a PalFX controller (`time`, `add`, `mul`, `sinadd`, `color`, `invertall`).</summary>
        public void ReadParam(string key, string val, Func<string, float> eval) {
            var p = MugenDef.SplitCsv(val);
            int I(int i, int def) => i < p.Length && p[i].Length > 0 ? (int)Math.Round(eval(p[i])) : def;
            switch (key.Trim().Lc()) {
                case "time": Time = I(0, 0); Elapsed = 0; break;
                case "add": for (int i = 0; i < 3; i++) Add[i] = I(i, 0); break;
                case "mul": for (int i = 0; i < 3; i++) Mul[i] = I(i, 256); break;
                case "sinadd": for (int i = 0; i < 3; i++) SinAdd[i] = I(i, 0); SinPeriod = I(3, 0); break;
                case "color": Color = I(0, 256); break;
                case "invertall": Invert = I(0, 0) != 0; break;
            }
        }

        public static PalFx FromController(StateController c, Func<string, float> eval) {
            var fx = new PalFx();
            foreach (var kv in c.Params) fx.ReadParam(kv.Key, kv.Value, eval);
            return fx;
        }

        /// <summary>The effect at this tick as add (0..1+) and mul (0..1+) per channel, plus
        /// saturation 0..1 and the invert flag. Off = add 0, mul 1, sat 1.</summary>
        public void Current(float[] add, float[] mul, out float saturation, out bool invert) {
            if (!Active) {
                for (int i = 0; i < 3; i++) { add[i] = 0f; mul[i] = 1f; }
                saturation = 1f; invert = false;
                return;
            }
            float sin = 0f;
            if (SinPeriod != 0) sin = (float)Math.Sin(2 * Math.PI * Elapsed / SinPeriod);
            for (int i = 0; i < 3; i++) {
                add[i] = (Add[i] + SinAdd[i] * sin) / 256f;
                mul[i] = Mul[i] / 256f;
            }
            saturation = Math.Max(0f, Math.Min(1f, Color / 256f));
            invert = Invert;
        }
    }

    /// <summary>A sound the engine asks the front end to play this tick.</summary>
    public struct SoundEvent {
        public Fighter Owner;           // null for engine sounds
        /// <summary>True: from the screenpack's fight.snd (`F` prefix); false: the owner's .snd.</summary>
        public bool Common;
        public int Group, Index;
        public int Channel;             // -1 = any free channel
        public float Volume;            // 0..1
        public bool LowPriority;
        public float FreqMul;
        public float Pan;               // -1..1 relative to the owner
        public bool Loop;
    }

    /// <summary>A sprite reference resolved by the renderer: which archive and which image.</summary>
    public enum SpriteSource { Character, FightFx }

    /// <summary>
    /// An `Explod`: a free-standing animation owned by a character (char.go `Explod`).
    /// Positions are in stage units like a fighter's; a screen-space explod (`postype` left /
    /// right / front / back) is stored relative to the camera and converted by the engine.
    /// </summary>
    public class Explod {
        public int Id = -1;
        public Fighter Owner;
        public Fighter BindTarget;
        public SpriteSource Source;
        public int AnimNo;
        public MugenAnimation Anim;
        public string PosType = "p1";
        public bool ScreenSpace;
        public float OffX, OffY;           // the `pos` parameter
        public float PosX, PosY;           // world (or screen, when ScreenSpace) position
        public float VelX, VelY, AccelX, AccelY;
        public int Facing = 1, VFacing = 1;
        public int BindTime;
        public int RemoveTime = -2;        // -2 = when the animation ends, -1 = never
        public int SprPriority;
        public bool OnTop, UnderStage;
        public float ScaleX = 1f, ScaleY = 1f, Angle;
        public bool RemoveOnGetHit;
        public int PauseMoveTime, SuperMoveTime;
        public TransType Trans = TransType.Default;
        public int AlphaSrc = 255, AlphaDst = 0;
        public bool IgnoreHitPause = true;
        public bool OwnPal;
        public PalFx PalFx = new PalFx();
        public int Time;
        public bool Removed;
        /// <summary>Hit spark / superpause animation created by the engine itself.</summary>
        public bool IsSystem;

        public bool Finished =>
            Removed || (RemoveTime == -2 && Anim != null && Anim.LoopEnd && Anim.TotalTime != -1) ||
            (RemoveTime >= 0 && Time >= RemoveTime) || Anim == null;
    }

    /// <summary>
    /// A `Projectile` (char.go `Projectile`): an animation with its own HitDef that flies,
    /// hits a limited number of times and plays its hit/remove/cancel animations.
    /// </summary>
    public class Projectile {
        public enum Phase { Flying, Hit, Removing, Canceled, Dead }

        public Fighter Owner;
        public int ProjId;
        public HitDef Hit;
        public int AnimNo, HitAnim = -1, RemAnim = -1, CancelAnim = -1;
        public SpriteSource Source, HitSource, RemSource, CancelSource;
        public MugenAnimation Anim;
        public float PosX, PosY, VelX, VelY, AccelX, AccelY;
        public float VelMulX = 1f, VelMulY = 1f;
        public float RemVelX, RemVelY;
        public int Facing = 1;
        public float ScaleX = 1f, ScaleY = 1f;
        public int HitsLeft = 1;
        public int MissTime;
        public int MissTimer;
        public int Priority = 1;
        public int SprPriority = 3;
        public float EdgeBound = 40f, StageBound = 40f;
        public float HeightLow = -240f, HeightHigh = 1f;
        public int RemoveTime = -1;
        public bool RemoveOnHit = true;
        public int PauseMoveTime, SuperMoveTime;
        public int HitPause;
        public int Time;
        public Phase State = Phase.Flying;
        public bool ShadowOn = true;
        public PalFx PalFx = new PalFx();

        public bool Dead => State == Phase.Dead;
    }

    /// <summary>One remembered frame of an `AfterImage` trail.</summary>
    public struct AfterImageFrame {
        public AnimFrame Frame;
        public float PosX, PosY;
        public int Facing;
        public float Angle;
    }

    /// <summary>`AfterImage` / `AfterImageTime` state of a character (char.go `AfterImage`).</summary>
    public class AfterImage {
        public int Time;                 // ticks left, -1 forever, 0 off
        public int Length = 20;
        public int TimeGap = 1, FrameGap = 4;
        public TransType Trans = TransType.Add;
        public int[] PalBright = { 30, 30, 30 }, PalContrast = { 255, 255, 255 }, PalPostBright = { 0, 0, 0 };
        public int[] PalAdd = { 10, 10, 25 }, PalMul = { 65, 65, 75 };   // mul in 1/100
        public float[] PalMulF = { 0.65f, 0.65f, 0.75f };
        public readonly List<AfterImageFrame> Frames = new List<AfterImageFrame>();
        int tick;

        public bool Active => Time != 0;

        public void Record(Fighter f) {
            if (!Active) { if (Frames.Count > 0) Frames.Clear(); return; }
            if (Time > 0) Time--;
            tick++;
            if (TimeGap > 1 && tick % TimeGap != 0) return;
            var frame = f.Anim != null ? f.Anim.CurrentFrame : null;
            if (frame == null) return;
            Frames.Insert(0, new AfterImageFrame { Frame = frame, PosX = f.PosX, PosY = f.PosY, Facing = f.Facing, Angle = f.DrawAngle });
            int keep = Math.Max(1, Length);
            if (Frames.Count > keep) Frames.RemoveRange(keep, Frames.Count - keep);
        }

        /// <summary>Frames to draw: every `framegap`-th remembered frame, oldest last.</summary>
        public IEnumerable<KeyValuePair<int, AfterImageFrame>> Visible() {
            int gap = Math.Max(1, FrameGap);
            for (int i = gap; i < Frames.Count; i += gap) yield return new KeyValuePair<int, AfterImageFrame>(i / gap, Frames[i]);
        }
    }

    /// <summary>`HitOverride` slot (char.go `HitOverride`).</summary>
    public class HitOverrideSlot {
        public HitAttr Attr;
        public int StateNo = -1;
        public int Time;
        public bool ForceAir;
        public bool Active => Time != 0 && Attr != HitAttr.None;
    }

    /// <summary>`NotHitBy` / `HitBy` slot.</summary>
    public class HitBySlot {
        public HitAttr Attr;
        public bool Not;     // true: NotHitBy
        public int Time;
        public bool Active => Time != 0;
    }
}
