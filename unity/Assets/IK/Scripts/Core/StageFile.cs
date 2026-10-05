using System;
using System.Collections.Generic;
using System.IO;

namespace IK.Core {
    // dev.4 stage loader. Port of Ikemen GO v1.0.0:
    //   engine/ikemen-go/src/stage.go   newStage, loadStage, readBackGround, bgAction,
    //                                   backGround.reset, bgCtrl.read, Stage.action
    //   engine/ikemen-go/src/camera.go  newStageCamera, Camera.Reset/Init/action/XBound
    //   engine/ikemen-go/src/system.go  xmin/xmax player limits (screenleft/screenright)
    // Pure C#: no UnityEngine types. Values are kept in stage local coordinates.

    /// <summary>Go `BgType`.</summary>
    public enum StageBgType { Normal, Anim, Parallax, Video, Dummy }

    /// <summary>Go `BgcType`.</summary>
    public enum StageBgCtrlType { Null, Anim, Visible, Enable, PalFX, PosSet, PosAdd, RemapPal, SinX, SinY, VelSet, VelAdd }

    /// <summary>Go `bgAction`: velocity / sine motion state of one BG element (or of the stage).</summary>
    public class StageBgAction {
        public float[] Offset = new float[2];
        public float[] SinOffset = new float[2];
        public float[] Pos = new float[2];
        public float[] Vel = new float[2];
        public float[] Radius = new float[2];
        public int[] SinTime = new int[2];
        public int[] SinLoopTime = new int[2];

        public void Clear() {
            for (int i = 0; i < 2; i++) {
                Offset[i] = 0f; SinOffset[i] = 0f; Pos[i] = 0f; Vel[i] = 0f; Radius[i] = 0f;
                SinTime[i] = 0; SinLoopTime[i] = 0;
            }
        }

        /// <summary>Go `bgAction.action`.</summary>
        public void Action(bool updateTime) {
            for (int i = 0; i < 2; i++) {
                Pos[i] += Vel[i];
                if (SinLoopTime[i] > 0) {
                    SinOffset[i] = Radius[i] * (float)Math.Sin(
                        2.0 * Math.PI * (double)SinTime[i] / (double)SinLoopTime[i]);
                    if (updateTime) {
                        SinTime[i]++;
                        if (SinTime[i] >= SinLoopTime[i]) SinTime[i] = 0;
                    }
                } else {
                    SinOffset[i] = 0f;
                }
                Offset[i] = Pos[i] + SinOffset[i];
            }
        }
    }

    /// <summary>One [BG name] element (Go `backGround`, created by `readBackGround`).</summary>
    public class StageBackground {
        public string Name = "";                 // section name after "BG "
        public string TypeRaw = "";              // the `type` value as written
        public StageBgType Type = StageBgType.Normal;
        public int Id;
        public int LayerNo;
        public int ActionNo = -1;
        public bool HasSprite;                   // a spriteno line was used (one-frame animation)
        public int[] SpriteNo = new int[2];      // group, image
        public float[] Start = new float[2];
        public float[] Delta = new float[] { 1f, 1f };
        public string TransRaw = "";
        public TransType Trans = TransType.Default;
        public int SrcAlpha = 255, DstAlpha = 0;
        /// <summary>Go `anim.mask`: -1 = no mask (default), 0 = colour 0 is transparent.</summary>
        public int Mask = -1;
        public bool Masked => Mask == 0;
        public int[] Tile = new int[2];          // xflag, yflag (x &lt; 0 becomes int.MaxValue)
        /// <summary>`tilespacing` as written in the def.</summary>
        public int[] TileSpacing = new int[2];
        /// <summary>Go adds the sprite size to the spacing when the SFF is loaded; equals TileSpacing otherwise.</summary>
        public int[] TileSpacingResolved = new int[2];
        public int[] Window = new int[] { -32768, -32768, 65535, 65535 };   // Go `startrect`
        public bool HasMaskWindow;
        public float[] WindowDelta = new float[2];
        public float[] Velocity = new float[2];  // Go `startv`
        public bool PositionLink;
        public float[] ScaleStart = new float[] { 1f, 1f };
        public float[] ScaleDelta = new float[2];
        public int[] Width = new int[2];         // parallax only
        public float[] XScale = new float[] { 1f, 1f };   // parallax `xscale` (Go `rasterx`)
        public float YScaleStart = 100f, YScaleDelta;
        public float[] ZoomDelta = new float[] { 1f, float.MaxValue };
        public bool ZoomDeltaSet;
        public float[] ZoomScaleDelta = new float[] { float.MaxValue, float.MaxValue };
        public float XBottomZoomDelta = float.MaxValue;
        public float XShear, Angle, XAngle, YAngle, FocalLength = 2048f;
        public bool AutoResizeParallax, AutoResizeParallaxSet;
        public bool RoundPos;
        public string VideoPath = "";
        // sin.x / sin.y: radius, loop time, start tick (Go startrad / startsinlt / startsint)
        public float[] SinRadius = new float[2];
        public int[] SinLoopTime = new int[2];
        public int[] SinStartTime = new int[2];

        // runtime state
        public bool Visible = true, Enabled = true;
        public readonly StageBgAction Bga = new StageBgAction();
        /// <summary>Own copy of the action `ActionNo` (null for sprite / dummy elements).</summary>
        public MugenAnimation Animation;

        /// <summary>Current draw offset added to Start (Go `bga.offset`).</summary>
        public float OffsetX => Bga.Offset[0];
        public float OffsetY => Bga.Offset[1];

        /// <summary>Go `backGround.reset`.</summary>
        public void Reset() {
            Bga.Clear();
            for (int i = 0; i < 2; i++) {
                Bga.Vel[i] = Velocity[i];
                Bga.Radius[i] = SinRadius[i];
                Bga.SinTime[i] = SinStartTime[i];
                Bga.SinLoopTime[i] = SinLoopTime[i];
            }
            Visible = true;
            Enabled = true;
            if (Animation != null) Animation.Reset();
        }

        /// <summary>
        /// One 60 Hz tick of this element, as done per element in Go `Stage.action`:
        /// `bga.action(enabled)` then `anim.Action()` when enabled. Position-linked sine
        /// offsets are added by <see cref="StageDefinition.Tick"/>.
        /// </summary>
        public void Tick() {
            Bga.Action(Enabled);
            if (Enabled && Animation != null) Animation.Tick();
        }

        /// <summary>Sprite currently shown: the animation frame, or SpriteNo. Group -1 = nothing.</summary>
        public int CurrentGroup {
            get {
                if (Animation != null) { var f = Animation.CurrentFrame; return f != null ? f.Group : -1; }
                return HasSprite ? SpriteNo[0] : -1;
            }
        }

        public int CurrentNumber {
            get {
                if (Animation != null) { var f = Animation.CurrentFrame; return f != null ? f.Number : 0; }
                return HasSprite ? SpriteNo[1] : 0;
            }
        }
    }

    /// <summary>A [BGCtrl] block, recorded as read by Go `bgCtrl.read` (not simulated yet).</summary>
    public class StageBgCtrl {
        public string Name = "";
        public string TypeRaw = "";
        public StageBgCtrlType Type = StageBgCtrlType.Null;
        public bool Is3D;
        public int StartTime, EndTime, LoopTime = -1;
        public float X = float.NaN, Y = float.NaN;
        public int[] Value = new int[3];
        public int[] Source = new int[] { -1, 0 };
        public int[] Dest = new int[] { -1, 0 };
        public bool PositionLink;
        public int SCtrlId;
        /// <summary>`ctrlid` of this block, or of the enclosing [BGCtrlDef] when absent. -1 / empty = all.</summary>
        public List<int> CtrlIds = new List<int>();
        /// <summary>Indices into StageDefinition.Backgrounds this controller acts on.</summary>
        public List<int> Targets = new List<int>();
        public string DefName = "";              // enclosing [BGCtrlDef name]
        public Dictionary<string, string> Raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A [BGCtrlDef] block.</summary>
    public class StageBgCtrlDef {
        public string Name = "";
        public int LoopTime = -1;
        public List<int> CtrlIds = new List<int>();
        public List<int> Targets = new List<int>();
    }

    /// <summary>Go `stageShadow` ([Shadow] and [Reflection]).</summary>
    public class StageShadow {
        public int Intensity;
        public int[] Color = new int[3];         // r, g, b after clamping
        public float XScale = 1f, YScale;
        public int FadeEnd, FadeBegin;           // `fade.range = end, begin`
        public float XShear, Angle, XAngle, YAngle;
        public float[] Offset = new float[2];
        public float[] Window = new float[4];
        public float YDelta = 1f;
        public int LayerNo;
        public bool Reflect;                     // [Shadow] reflect (MUGEN 1.0 docs), recorded only
    }

    public class StagePlayerStart {
        public int StartX, StartY, StartZ, Facing;
    }

    /// <summary>Everything a stage .def describes (Go `Stage` + `stageCamera`).</summary>
    public class StageDefinition {
        public const int MaxPlayers = 8;         // Go MaxSimul*2 fighters (attached chars ignored)

        public MugenDef Def;
        public string DefFile = "";

        // [Info]
        public string Name = "", DisplayName = "", Author = "", VersionDate = "";
        public string MugenVersionRaw = "", IkemenVersionRaw = "";
        public int[] MugenVersion = new int[] { 0, 5 };
        public int[] IkemenVersion = new int[3];
        public List<string> AttachedChars = new List<string>();
        public bool RoundPos;

        // [StageInfo]
        public int ZOffset;
        public int ZOffsetLink = -1;
        public bool Hires;
        public bool AutoTurn = true, ResetBG = true;
        public int[] LocalCoord = new int[] { 320, 240 };
        public float XScale = 1f, YScale = 1f;

        // [Camera]
        public int StartX, StartY;
        public int BoundLeft, BoundRight, BoundHigh, BoundLow;
        public float VerticalFollow = 0.2f;
        public int FloorTension;
        public int Tension = 50;
        public float TensionVel = 1f;
        public int TensionHigh, TensionLow;
        public bool YTensionEnable;
        public int OverdrawHigh, OverdrawLow;
        public int CutHigh, CutLow = int.MinValue;
        public float StartZoom = 1f, ZoomIn = 1f, ZoomOut = 1f;
        public bool AutoCenter, AutoZoom, ZoomAnchorBottom, LowestCap;
        public float ZoomInDelay, ZoomInSpeed = 1f, ZoomOutSpeed = 1f, YScrollSpeed = 1f;
        public float BoundHighZoomDelta, VerticalFollowZoomDelta;
        public float Fov = 40f, YShift, Near = 0.1f, Far = 10000f;

        // [Scaling]
        public bool HasScaling;
        public float TopZ, BotZ, TopScale = 1f, BotScale = 1f, DepthToScreen = 1f;

        // [Bound]
        public int ScreenLeft = 15, ScreenRight = 15;

        // [PlayerInfo]
        public readonly StagePlayerStart[] Players = new StagePlayerStart[MaxPlayers];
        public int PartnerSpacing = 25;
        public float LeftBound = -1000f, RightBound = 1000f, TopBound, BotBound;

        // [Shadow] / [Reflection]
        public readonly StageShadow Shadow = new StageShadow();
        public readonly StageShadow Reflection = new StageShadow();

        // [Music]
        public string BgMusic = "";
        public int BgmVolume = 100;
        public string BgmLoopStart = "", BgmLoopEnd = "";
        public float BgmRatio = 0.3f;
        public int BgmTrigger;

        // [BGdef]
        public string SpriteFile = "";
        public string ModelFile = "";
        public bool DebugBG;
        public int[] BgClearColor = new int[3];

        public readonly Dictionary<string, float> Constants = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public readonly List<StageBackground> Backgrounds = new List<StageBackground>();
        public readonly List<StageBgCtrlDef> BgCtrlDefs = new List<StageBgCtrlDef>();
        public readonly List<StageBgCtrl> BgCtrls = new List<StageBgCtrl>();
        /// <summary>Actions found in the def ([Begin Action n]); null if none.</summary>
        public AirFile Animations;
        /// <summary>Stage sprites, when loaded through <see cref="Load"/> with sprites on.</summary>
        public SffFile Sprites;
        /// <summary>Stage-wide bgAction (Go `s.bga`, used for zoffsetlink).</summary>
        public readonly StageBgAction Bga = new StageBgAction();
        public int StageTime;

        // ---- helpers the fight engine reads ---------------------------------------

        public StagePlayerStart P1Start => Players[0];
        public StagePlayerStart P2Start => Players[1];
        /// <summary>boundleft, boundright, boundhigh, boundlow.</summary>
        public int[] CameraBounds => new int[] { BoundLeft, BoundRight, BoundHigh, BoundLow };

        public StageDefinition() {
            for (int i = 0; i < MaxPlayers; i++) Players[i] = new StagePlayerStart();
            Players[0].StartX = -70;
            Players[1].StartX = 70;
            // Go newStage
            Shadow.Intensity = 128;
            Shadow.XScale = 1f; Shadow.YDelta = 1f; Shadow.YScale = 0.4f;
            Reflection.Color[0] = 255; Reflection.Color[1] = 255; Reflection.Color[2] = 255;
            Reflection.XScale = 1f; Reflection.YDelta = 1f; Reflection.YScale = 1f;
        }

        // ---- loading -----------------------------------------------------------------

        /// <summary>Reads a stage def (and, when asked, its SFF) through a resource source.</summary>
        public static StageDefinition Load(IResourceSource source, string defFile, bool loadSprites = true) {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var bytes = source.Read(defFile);
            if (bytes == null) throw new FileNotFoundException("stage def not found: " + defFile);
            SffFile sff = null;
            var stage = Parse(MugenDef.DecodeText(bytes), defFile, null);
            if (loadSprites && stage.SpriteFile.Length > 0) {
                var sffBytes = source.Read(stage.SpriteFile);
                if (sffBytes == null) {
                    // Go LoadFile also searches "<def dir>/", "" and "data/"; try the bare name.
                    sffBytes = source.Read(FileNameOnly(stage.SpriteFile));
                }
                if (sffBytes != null) {
                    sff = SffFile.Load(sffBytes, false);
                    // reparse so the sprite-dependent parts (tilespacing, version) are resolved
                    stage = Parse(MugenDef.DecodeText(bytes), defFile, sff);
                }
            }
            return stage;
        }

        static string FileNameOnly(string path) {
            var p = path.Replace('\\', '/');
            int s = p.LastIndexOf('/');
            return s >= 0 ? p.Substring(s + 1) : p;
        }

        public static StageDefinition Parse(byte[] bytes, string defFile = "", SffFile sff = null) =>
            Parse(MugenDef.DecodeText(bytes), defFile, sff);

        /// <summary>Port of Go `loadStage` (minus music playback, models, attached chars loading).</summary>
        public static StageDefinition Parse(string text, string defFile = "", SffFile sff = null) {
            var s = new StageDefinition();
            s.DefFile = defFile ?? "";
            s.Def = MugenDef.Parse(text);
            s.Sprites = sff;
            var air = AirFile.Parse(text);
            s.Animations = air.Actions.Count > 0 ? air : null;

            MugenDef.Section sec;

            // Info group
            sec = s.Def["info"];
            if (sec != null) {
                s.Name = sec.Has("name") ? MugenDef.Unquote(sec.Get("name")) : s.DefFile;
                s.DisplayName = sec.Has("displayname") ? MugenDef.Unquote(sec.Get("displayname")) : s.Name;
                s.Author = MugenDef.Unquote(sec.Get("author"));
                s.VersionDate = sec.Get("versiondate");
                s.MugenVersionRaw = sec.Get("mugenversion");
                s.IkemenVersionRaw = sec.Get("ikemenversion");
                s.MugenVersion = ParseMugenVersion(s.MugenVersionRaw);
                s.IkemenVersion = ParseIkemenVersion(s.IkemenVersionRaw);
                if (s.IkemenVersion[0] == 0 && s.IkemenVersion[1] == 0 && s.MugenVersion[0] != 1)
                    s.RoundPos = true;
                foreach (var kv in sec.Lines)
                    if (kv.Key.StartsWith("attachedchar") && kv.Value.Length > 0) s.AttachedChars.Add(kv.Value);
            }

            // StageInfo group (before the others so localcoord is known)
            sec = s.Def["stageinfo"];
            float xs = float.NaN, ys = float.NaN;
            if (sec != null) {
                ReadI32(sec, "zoffset", ref s.ZOffset);
                ReadI32(sec, "zoffsetlink", ref s.ZOffsetLink);
                ReadBool(sec, "hires", ref s.Hires);
                ReadBool(sec, "autoturn", ref s.AutoTurn);
                ReadBool(sec, "resetbg", ref s.ResetBG);
                ReadInts(sec, "localcoord", s.LocalCoord, true);
                ReadF32(sec, "xscale", ref xs);
                ReadF32(sec, "yscale", ref ys);
            }
            if (float.IsNaN(xs)) xs = 1f; else if (s.Hires) xs *= 2f;
            if (float.IsNaN(ys)) ys = 1f; else if (s.Hires) ys *= 2f;
            s.XScale = xs; s.YScale = ys;
            if (s.LocalCoord[0] != 320) {
                float coordRatio = (float)s.LocalCoord[0] / 320f;
                s.LeftBound *= coordRatio;
                s.RightBound *= coordRatio;
                s.ScreenLeft = (int)((float)s.ScreenLeft * coordRatio);
                s.ScreenRight = (int)((float)s.ScreenRight * coordRatio);
                s.PartnerSpacing = (int)((float)s.PartnerSpacing * coordRatio);
                s.Players[0].StartX = (int)((float)s.Players[0].StartX * coordRatio);
                s.Players[1].StartX = (int)((float)s.Players[1].StartX * coordRatio);
            }

            // Constants group
            sec = s.Def["constants"];
            if (sec != null)
                foreach (var kv in sec.Lines)
                    if (!s.Constants.ContainsKey(kv.Key)) s.Constants[kv.Key] = MugenDef.Atof(kv.Value);

            // Scaling group: MUGEN 1.x removed the z-axis, Ikemen 1.0 adds it back
            sec = s.Def["scaling"];
            if (sec != null && (s.MugenVersion[0] != 1 || s.IkemenVersion[0] >= 1)) {
                s.HasScaling = true;
                ReadF32(sec, "topz", ref s.TopZ);
                ReadF32(sec, "botz", ref s.BotZ);
                ReadF32(sec, "topscale", ref s.TopScale);
                ReadF32(sec, "botscale", ref s.BotScale);
                ReadF32(sec, "depthtoscreen", ref s.DepthToScreen);
            }

            // Bound group
            sec = s.Def["bound"];
            if (sec != null) {
                ReadI32(sec, "screenleft", ref s.ScreenLeft);
                ReadI32(sec, "screenright", ref s.ScreenRight);
            }

            // PlayerInfo group
            sec = s.Def["playerinfo"];
            if (sec != null) {
                ReadI32(sec, "partnerspacing", ref s.PartnerSpacing);
                for (int i = 0; i < MaxPlayers; i++) {
                    var p = s.Players[i];
                    if (i >= 2) {
                        p.StartX = s.Players[i - 2].StartX + s.PartnerSpacing * (2 * (i % 2) - 1);
                        p.StartY = s.Players[i % 2].StartY;
                        p.StartZ = s.Players[i % 2].StartZ;
                        p.Facing = 1 - 2 * (i % 2);
                    }
                    string n = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ReadI32(sec, "p" + n + "startx", ref p.StartX);
                    ReadI32(sec, "p" + n + "starty", ref p.StartY);
                    ReadI32(sec, "p" + n + "startz", ref p.StartZ);
                    ReadI32(sec, "p" + n + "facing", ref p.Facing);
                }
                ReadF32(sec, "leftbound", ref s.LeftBound);
                ReadF32(sec, "rightbound", ref s.RightBound);
                ReadF32(sec, "topbound", ref s.TopBound);
                ReadF32(sec, "botbound", ref s.BotBound);
            }

            // Camera group
            sec = s.Def["camera"];
            if (sec != null) {
                ReadI32(sec, "startx", ref s.StartX);
                ReadI32(sec, "starty", ref s.StartY);
                ReadI32(sec, "boundleft", ref s.BoundLeft);
                ReadI32(sec, "boundright", ref s.BoundRight);
                ReadI32(sec, "boundhigh", ref s.BoundHigh);
                ReadI32(sec, "boundlow", ref s.BoundLow);
                ReadF32(sec, "verticalfollow", ref s.VerticalFollow);
                ReadI32(sec, "floortension", ref s.FloorTension);
                ReadI32(sec, "tension", ref s.Tension);
                ReadF32(sec, "tensionvel", ref s.TensionVel);
                ReadI32(sec, "overdrawhigh", ref s.OverdrawHigh);
                ReadI32(sec, "overdrawlow", ref s.OverdrawLow);
                ReadI32(sec, "cuthigh", ref s.CutHigh);
                ReadI32(sec, "cutlow", ref s.CutLow);
                ReadF32(sec, "startzoom", ref s.StartZoom);
                ReadF32(sec, "fov", ref s.Fov);
                ReadF32(sec, "yshift", ref s.YShift);
                ReadF32(sec, "near", ref s.Near);
                ReadF32(sec, "far", ref s.Far);
                ReadBool(sec, "autocenter", ref s.AutoCenter);
                ReadF32(sec, "yscrollspeed", ref s.YScrollSpeed);
                ReadF32(sec, "verticalfollowzoomdelta", ref s.VerticalFollowZoomDelta);
                ReadBool(sec, "lowestcap", ref s.LowestCap);
                ReadF32(sec, "zoomin", ref s.ZoomIn);
                ReadF32(sec, "zoomout", ref s.ZoomOut);
                bool zoomAnchorOk = sec.Has("zoomanchor");
                if (MugenDef.Unquote(sec.Get("zoomanchor")).ToLowerInvariant() == "bottom") s.ZoomAnchorBottom = true;
                ReadBool(sec, "autozoom", ref s.AutoZoom);
                if (s.AutoZoom) {
                    // Go uses sys.cam.LegacyZoomMax (1) and LegacyZoomMin (5/8)
                    if (s.ZoomIn == 1f) s.ZoomIn = 1f;
                    if (s.ZoomOut == 1f) s.ZoomOut = 5f / 8f;
                    if (!zoomAnchorOk) s.ZoomAnchorBottom = true;
                    s.ZoomInDelay = 25f;
                    s.ZoomInSpeed = 0.4f;
                    s.ZoomOutSpeed = 0.4f;
                    s.BoundHighZoomDelta = 1f;
                }
                ReadF32(sec, "zoomindelay", ref s.ZoomInDelay);
                ReadF32(sec, "zoominspeed", ref s.ZoomInSpeed);
                ReadF32(sec, "zoomoutspeed", ref s.ZoomOutSpeed);
                ReadF32(sec, "boundhighzoomdelta", ref s.BoundHighZoomDelta);
                if (ReadI32(sec, "tensionlow", ref s.TensionLow)) {
                    s.YTensionEnable = true;
                    ReadI32(sec, "tensionhigh", ref s.TensionHigh);
                }
            }

            // Music group (only the classic keys are recorded; playback is not ported)
            sec = s.Def["music"];
            if (sec != null) {
                s.BgMusic = MugenDef.Unquote(sec.Get("bgmusic"));
                if (sec.Get("bgmvolume").Length > 0) s.BgmVolume = MugenDef.Atoi(sec.Get("bgmvolume"));
                else if (sec.Get("bgvolume").Length > 0) s.BgmVolume = MugenDef.Atoi(sec.Get("bgvolume"));
                s.BgmLoopStart = sec.Get("bgmloopstart");
                s.BgmLoopEnd = sec.Get("bgmloopend");
                ReadF32(sec, "bgmratio", ref s.BgmRatio);
                ReadI32(sec, "bgmtrigger", ref s.BgmTrigger);
            }

            // BGDef group
            sec = s.Def["bgdef"];
            if (sec != null) {
                s.SpriteFile = MugenDef.Unquote(sec.Get("spr"));
                s.ModelFile = MugenDef.Unquote(sec.Get("model"));
                // SFF v2.01 did not exist before MUGEN 1.1
                if (sff != null && sff.VersionHigh == 2 && sff.VersionLo2 == 1) {
                    s.MugenVersion[0] = 1; s.MugenVersion[1] = 1;
                }
                if (s.ModelFile.Length > 0 && s.IkemenVersion[0] == 0 && s.IkemenVersion[1] == 0) {
                    s.IkemenVersion[0] = 1; s.IkemenVersion[1] = 0;
                }
                ReadBool(sec, "debugbg", ref s.DebugBG);
                ReadInts(sec, "bgclearcolor", s.BgClearColor, true);
                ReadBool(sec, "roundpos", ref s.RoundPos);
            }

            // Shadow group
            sec = s.Def["shadow"];
            if (sec != null) {
                var d = s.Shadow;
                int tmp = 0;
                if (ReadI32(sec, "intensity", ref tmp)) d.Intensity = Clamp(tmp, 0, 255);
                int[] rgb = new int[3];
                ReadInts(sec, "color", rgb, true);
                for (int i = 0; i < 3; i++) rgb[i] = Clamp(rgb[i], 0, 255);
                // Go disables the colour parameter specifically in MUGEN 1.1 stages
                if (s.IkemenVersion[0] == 0 && s.IkemenVersion[1] == 0 && s.MugenVersion[0] == 1 && s.MugenVersion[1] == 1) {
                    rgb[0] = 0; rgb[1] = 0; rgb[2] = 0;
                }
                d.Color = rgb;
                ReadShadowCommon(sec, d);
                ReadBool(sec, "reflect", ref d.Reflect);
            }

            // Reflection group
            sec = s.Def["reflection"];
            if (sec != null) {
                var d = s.Reflection;
                int tmp = 0;
                if (ReadI32(sec, "intensity", ref tmp)) d.Intensity = Clamp(tmp, 0, 255);
                int[] rgb = new int[3];
                ReadInts(sec, "color", rgb, true);
                for (int i = 0; i < 3; i++) rgb[i] = Clamp(rgb[i], 0, 255);
                d.Color = rgb;
                if (ReadI32(sec, "layerno", ref tmp)) d.LayerNo = Clamp(tmp, -1, 0);
                ReadShadowCommon(sec, d);
            }

            // BG group: every [BG xxx] section, in file order
            StageBackground bglink = null;
            foreach (var bs in s.Def.Sections) {
                string baseName, subName;
                SplitSectionName(bs.RawName, out baseName, out subName);
                if (subName == null || baseName != "bg") continue;
                if (s.Backgrounds.Count > 0 && !s.Backgrounds[s.Backgrounds.Count - 1].PositionLink)
                    bglink = s.Backgrounds[s.Backgrounds.Count - 1];
                var bg = ReadBackground(bs, subName, bglink, sff, air, s.RoundPos);
                s.Backgrounds.Add(bg);
            }

            if (s.AutoZoom) {
                foreach (var b in s.Backgrounds) {
                    if (b.Type == StageBgType.Parallax && !b.AutoResizeParallaxSet) b.AutoResizeParallax = true;
                    if (!b.ZoomDeltaSet) b.ZoomDelta[0] = float.MaxValue;
                }
            }

            // BGCtrlDef / BGCtrl
            StageBgCtrlDef cdef = new StageBgCtrlDef();
            for (int i = 0; i < s.Backgrounds.Count; i++) cdef.Targets.Add(i);
            foreach (var cs in s.Def.Sections) {
                string baseName, subName;
                SplitSectionName(cs.RawName, out baseName, out subName);
                string sub = subName != null ? subName : "";
                if (baseName == "bgctrldef") {
                    cdef = new StageBgCtrlDef { Name = sub };
                    var ids = ReadI32Csv(cs, "ctrlid");
                    cdef.CtrlIds.AddRange(ids);
                    cdef.Targets.AddRange(s.ResolveTargets(ids));
                    ReadI32(cs, "looptime", ref cdef.LoopTime);
                    s.BgCtrlDefs.Add(cdef);
                } else if (baseName == "bgctrl" || baseName == "bgctrl3d") {
                    var c = new StageBgCtrl { Name = sub, DefName = cdef.Name, LoopTime = cdef.LoopTime, Is3D = baseName == "bgctrl3d" };
                    c.CtrlIds.AddRange(cdef.CtrlIds);
                    c.Targets.AddRange(cdef.Targets);
                    var ids = ReadI32Csv(cs, "ctrlid");
                    if (ids.Count > 0) {
                        c.CtrlIds.Clear(); c.CtrlIds.AddRange(ids);
                        c.Targets.Clear();
                        if (!c.Is3D) c.Targets.AddRange(s.ResolveTargets(ids));
                    }
                    ReadBgCtrl(cs, c);
                    s.BgCtrls.Add(c);
                }
            }

            // position links add the start of the linked element; zoffsetlink moves the floor
            int link = 0, zlink = -1;
            for (int i = 0; i < s.Backgrounds.Count; i++) {
                var b = s.Backgrounds[i];
                if (b.PositionLink && i > 0) {
                    b.Start[0] += s.Backgrounds[link].Start[0];
                    b.Start[1] += s.Backgrounds[link].Start[1];
                } else {
                    link = i;
                }
                if (s.ZOffsetLink >= 0 && zlink < 0 && b.Id == s.ZOffsetLink) {
                    zlink = i;
                    s.ZOffset += (int)(b.Start[1] * s.YScale);
                }
            }

            // Go resets the BGs at round start (Stage.reset); doing it here makes Tick usable at once.
            s.Reset();
            return s;
        }

        /// <summary>Go `Stage.reset` (BG part).</summary>
        public void Reset() {
            Bga.Clear();
            foreach (var b in Backgrounds) b.Reset();
            StageTime = 0;
        }

        /// <summary>
        /// One 60 Hz stage tick: the BG part of Go `Stage.action` followed by `Stage.tick`.
        /// BGCtrl blocks are only recorded, not run.
        /// </summary>
        public void Tick() {
            Bga.Action(true);
            int link = 0, zlink = -1;
            for (int i = 0; i < Backgrounds.Count; i++) {
                var b = Backgrounds[i];
                b.Bga.Action(b.Enabled);
                if (i > 0 && b.PositionLink) {
                    float o0 = Backgrounds[link].Bga.SinOffset[0];
                    float o1 = Backgrounds[link].Bga.SinOffset[1];
                    if (Hires) { o0 /= 2f; o1 /= 2f; }
                    b.Bga.Offset[0] += o0;
                    b.Bga.Offset[1] += o1;
                } else {
                    link = i;
                }
                if (ZOffsetLink >= 0 && zlink < 0 && b.Id == ZOffsetLink) {
                    zlink = i;
                    Bga.Offset[1] += b.Bga.Offset[1];
                }
                if (b.Enabled && b.Animation != null) b.Animation.Tick();
            }
            StageTime++;
        }

        /// <summary>Go `Stage.getBg`: indices of every BG with this id.</summary>
        public List<int> FindBackgrounds(int id) {
            var r = new List<int>();
            for (int i = 0; i < Backgrounds.Count; i++) if (Backgrounds[i].Id == id) r.Add(i);
            return r;
        }

        List<int> ResolveTargets(List<int> ids) {
            var r = new List<int>();
            if (ids.Count > 0 && (ids.Count > 1 || ids[0] != -1)) {
                var seen = new HashSet<int>();
                foreach (var id in ids) {
                    if (!seen.Add(id)) continue;
                    r.AddRange(FindBackgrounds(id));
                }
            } else {
                for (int i = 0; i < Backgrounds.Count; i++) r.Add(i);
            }
            return r;
        }

        // ---- [BG] ------------------------------------------------------------------

        /// <summary>Port of Go `readBackGround`.</summary>
        static StageBackground ReadBackground(MugenDef.Section sec, string name, StageBackground link,
                                              SffFile sff, AirFile air, bool stageRoundPos) {
            var bg = new StageBackground { Name = name };
            string typ = sec.Get("type");
            bg.TypeRaw = typ;
            bg.RoundPos = stageRoundPos;
            if (typ.Length == 0) return bg;
            switch (typ[0]) {
                case 'N': case 'n': bg.Type = StageBgType.Normal; break;
                case 'A': case 'a': bg.Type = StageBgType.Anim; break;
                case 'P': case 'p': bg.Type = StageBgType.Parallax; break;
                case 'V': case 'v': bg.Type = StageBgType.Video; bg.Mask = 0; break;
                case 'D': case 'd': bg.Type = StageBgType.Dummy; break;
                default: return bg;
            }
            int tmp = 0;
            ReadI32(sec, "layerno", ref bg.LayerNo);

            if (bg.Type == StageBgType.Video) {
                // video playback is not ported; the path is recorded
                bg.VideoPath = MugenDef.Unquote(sec.Get("path"));
            } else if (bg.Type != StageBgType.Dummy) {
                bool hasAnim = false;
                if ((bg.Type != StageBgType.Normal || sec.Get("spriteno").Length == 0) &&
                    ReadI32(sec, "actionno", ref bg.ActionNo)) {
                    MugenAnimation a = air != null ? air.Get(bg.ActionNo) : null;
                    if (a == null) throw new InvalidDataException("Invalid BG action: " + bg.ActionNo);
                    bg.Animation = CloneAnimation(a);   // Go AnimationTable.get returns a copy
                    hasAnim = true;
                }
                if (hasAnim) {
                    if (bg.Type == StageBgType.Normal) bg.Type = StageBgType.Anim;
                } else {
                    if (ReadInts(sec, "spriteno", bg.SpriteNo, true)) bg.HasSprite = true;
                    if (ReadI32(sec, "mask", ref tmp)) bg.Mask = tmp != 0 ? 0 : -1;
                }
            }
            ReadBool(sec, "positionlink", ref bg.PositionLink);
            if (bg.PositionLink && link != null) {
                bg.Velocity[0] = link.Velocity[0]; bg.Velocity[1] = link.Velocity[1];
                bg.Delta[0] = link.Delta[0]; bg.Delta[1] = link.Delta[1];
            }
            if (sec.Has("autoresizeparallax")) {
                bg.AutoResizeParallaxSet = true;
                ReadBool(sec, "autoresizeparallax", ref bg.AutoResizeParallax);
            }
            ReadFloats(sec, "start", bg.Start, true);
            if (!bg.PositionLink) ReadFloats(sec, "delta", bg.Delta, true);
            ReadFloats(sec, "scalestart", bg.ScaleStart, true);
            ReadFloats(sec, "scaledelta", bg.ScaleDelta, true);
            ReadF32Stage(sec, "xshear", ref bg.XShear);
            ReadF32Stage(sec, "angle", ref bg.Angle);
            ReadF32Stage(sec, "xangle", ref bg.XAngle);
            ReadF32Stage(sec, "yangle", ref bg.YAngle);
            ReadF32Stage(sec, "focallength", ref bg.FocalLength);
            ReadF32Stage(sec, "xbottomzoomdelta", ref bg.XBottomZoomDelta);
            ReadFloats(sec, "zoomscaledelta", bg.ZoomScaleDelta, true);
            if (ReadFloats(sec, "zoomdelta", bg.ZoomDelta, true)) bg.ZoomDeltaSet = true;
            if (bg.ZoomDelta[0] != float.MaxValue && bg.ZoomDelta[1] == float.MaxValue) bg.ZoomDelta[1] = bg.ZoomDelta[0];

            // transparency
            if (sec.Has("trans")) {
                string data = sec.Get("trans");
                bg.TransRaw = data;
                switch (data.ToLowerInvariant()) {
                    case "add": bg.Mask = 0; bg.Trans = TransType.Add; bg.SrcAlpha = 255; bg.DstAlpha = 255; break;
                    case "add1": bg.Mask = 0; bg.Trans = TransType.Add; bg.SrcAlpha = 255; bg.DstAlpha = 128; break;
                    case "addalpha": bg.Mask = 0; bg.Trans = TransType.Add; bg.SrcAlpha = 255; bg.DstAlpha = 0; break;
                    case "sub": bg.Mask = 0; bg.Trans = TransType.Sub; bg.SrcAlpha = 255; bg.DstAlpha = 255; break;
                    case "subadd": bg.Mask = 0; bg.Trans = TransType.SubAdd; bg.SrcAlpha = 255; bg.DstAlpha = 255; break;
                    case "none":   // MUGEN treats it as Default
                    case "default": bg.Trans = TransType.Default; bg.SrcAlpha = 255; bg.DstAlpha = 0; break;
                    default: throw new InvalidDataException("Invalid trans type: " + data);
                }
            }
            if ((bg.Trans == TransType.Add || bg.Trans == TransType.Sub) && sec.Has("alpha")) {
                int[] sd = new int[] { bg.SrcAlpha, bg.DstAlpha };
                if (ReadInts(sec, "alpha", sd, true)) {
                    bg.SrcAlpha = Clamp(sd[0], 0, 255);
                    bg.DstAlpha = Clamp(sd[1], 0, 255);
                }
            }

            if (ReadInts(sec, "tile", bg.Tile, true)) {
                if (bg.Type == StageBgType.Parallax) bg.Tile[1] = 0;
                if (bg.Tile[0] < 0) bg.Tile[0] = int.MaxValue;
            }
            if (bg.Type == StageBgType.Parallax) {
                if (!ReadInts(sec, "width", bg.Width, true)) ReadFloats(sec, "xscale", bg.XScale, true);
                ReadF32(sec, "yscalestart", ref bg.YScaleStart);
                ReadF32(sec, "yscaledelta", ref bg.YScaleDelta);
                bg.TileSpacingResolved[0] = bg.TileSpacing[0];
                bg.TileSpacingResolved[1] = bg.TileSpacing[1];
            } else {
                ReadInts(sec, "tilespacing", bg.TileSpacing, false);
                bg.TileSpacingResolved[0] = bg.TileSpacing[0];
                bg.TileSpacingResolved[1] = bg.TileSpacing[1];
                if (bg.ActionNo < 0 && bg.HasSprite) {
                    if (sff != null && bg.SpriteNo[0] >= 0 && bg.SpriteNo[1] >= 0) {
                        var spr = sff.Get(bg.SpriteNo[0], bg.SpriteNo[1]);
                        if (spr != null) {
                            bg.TileSpacingResolved[0] += spr.Width;
                            bg.TileSpacingResolved[1] += spr.Height;
                        }
                    }
                } else {
                    if (bg.TileSpacing[0] == 0) bg.Tile[0] = 0;
                    if (bg.TileSpacing[1] == 0) bg.Tile[1] = 0;
                }
            }
            // MUGEN only accepts window / maskwindow with at least 4 values
            ReadIntsMinLength(sec, "window", bg.Window);
            if (ReadIntsMinLength(sec, "maskwindow", bg.Window)) bg.HasMaskWindow = true;
            ReadFloats(sec, "windowdelta", bg.WindowDelta, true);
            ReadI32(sec, "id", ref bg.Id);
            ReadFloats(sec, "velocity", bg.Velocity, true);
            for (int i = 0; i < 2; i++) {
                string key = i == 0 ? "sin.x" : "sin.y";
                float[] rs = new float[] { float.NaN, float.NaN, float.NaN };
                if (ReadFloats(sec, key, rs, true)) {
                    if (!float.IsNaN(rs[0])) bg.SinRadius[i] = rs[0];
                    if (!float.IsNaN(rs[1])) {
                        int[] it = new int[] { 0, 0 };
                        ReadInts(sec, key, it, true);
                        bg.SinLoopTime[i] = it[1];
                    }
                    if (bg.SinLoopTime[i] > 0 && !float.IsNaN(rs[2])) {
                        int st = (int)(rs[2] * (float)bg.SinLoopTime[i] / 360f) % bg.SinLoopTime[i];
                        if (st < 0) st += bg.SinLoopTime[i];
                        bg.SinStartTime[i] = st;
                    }
                }
            }
            ReadBool(sec, "roundpos", ref bg.RoundPos);
            return bg;
        }

        /// <summary>Port of Go `bgCtrl.read` (recording only).</summary>
        static void ReadBgCtrl(MugenDef.Section sec, StageBgCtrl c) {
            foreach (var kv in sec.Lines) if (!c.Raw.ContainsKey(kv.Key)) c.Raw[kv.Key] = kv.Value;
            string data = sec.Get("type").ToLowerInvariant();
            c.TypeRaw = sec.Get("type");
            bool xy = false, srcdst = false, palfx = false;
            switch (data) {
                case "anim": c.Type = StageBgCtrlType.Anim; break;
                case "visible": c.Type = StageBgCtrlType.Visible; break;
                case "enable": c.Type = StageBgCtrlType.Enable; break;
                case "null": c.Type = StageBgCtrlType.Null; break;
                case "palfx": c.Type = StageBgCtrlType.PalFX; palfx = true; break;
                case "posset": c.Type = StageBgCtrlType.PosSet; xy = true; break;
                case "posadd": c.Type = StageBgCtrlType.PosAdd; xy = true; break;
                case "remappal": c.Type = StageBgCtrlType.RemapPal; srcdst = true; break;
                case "sinx": c.Type = StageBgCtrlType.SinX; break;
                case "siny": c.Type = StageBgCtrlType.SinY; break;
                case "velset": c.Type = StageBgCtrlType.VelSet; xy = true; break;
                case "veladd": c.Type = StageBgCtrlType.VelAdd; xy = true; break;
                default: throw new InvalidDataException("Invalid BGCtrl type: " + data);
            }
            ReadI32(sec, "time", ref c.StartTime);
            c.EndTime = c.StartTime;
            int[] t = new int[] { c.StartTime, c.EndTime, c.LoopTime };
            ReadInts(sec, "time", t, true);
            c.StartTime = t[0]; c.EndTime = t[1]; c.LoopTime = t[2];
            ReadBool(sec, "positionlink", ref c.PositionLink);
            if (xy) {
                ReadF32Stage(sec, "x", ref c.X);
                ReadF32Stage(sec, "y", ref c.Y);
            } else if (srcdst) {
                ReadInts(sec, "source", c.Source, true);
                ReadInts(sec, "dest", c.Dest, true);
            } else if (palfx) {
                // PalFX parameters stay in Raw until BG PalFX is ported
            } else {
                float v = c.X;
                if (ReadF32(sec, "value", ref v)) {
                    c.X = v;
                    ReadInts(sec, "value", c.Value, true);
                }
            }
            ReadI32(sec, "sctrlid", ref c.SCtrlId);
        }

        static void ReadShadowCommon(MugenDef.Section sec, StageShadow d) {
            ReadF32(sec, "xscale", ref d.XScale);
            ReadF32(sec, "yscale", ref d.YScale);
            int[] fr = new int[] { d.FadeEnd, d.FadeBegin };
            ReadInts(sec, "fade.range", fr, true);
            d.FadeEnd = fr[0]; d.FadeBegin = fr[1];
            ReadF32(sec, "xshear", ref d.XShear);
            ReadF32(sec, "angle", ref d.Angle);
            ReadF32(sec, "xangle", ref d.XAngle);
            ReadF32(sec, "yangle", ref d.YAngle);
            ReadFloats(sec, "offset", d.Offset, true);
            ReadFloats(sec, "window", d.Window, true);
            ReadF32(sec, "ydelta", ref d.YDelta);
        }

        /// <summary>Shallow copy of an action with fresh playback state (frames are shared, read-only).</summary>
        public static MugenAnimation CloneAnimation(MugenAnimation src) {
            var a = new MugenAnimation();
            a.No = src.No;
            a.Frames.AddRange(src.Frames);
            a.LoopStart = src.LoopStart;
            a.TotalTime = src.TotalTime;
            a.LoopTime = src.LoopTime;
            a.PreLoopTime = src.PreLoopTime;
            a.CopyAction = src.CopyAction;
            a.Reset();
            return a;
        }

        // ---- Go IniSection readers --------------------------------------------------

        /// <summary>
        /// Go `SectionName`: "[BG Floor]" gives base "bg" and sub "Floor"; a name without a
        /// space has no sub-name.
        /// </summary>
        static void SplitSectionName(string raw, out string baseName, out string subName) {
            string n = raw ?? "";
            int sp = n.IndexOf(' ');
            if (sp >= 0) {
                baseName = n.Substring(0, sp).ToLowerInvariant();
                subName = n.Substring(sp + 1);
            } else {
                baseName = n.ToLowerInvariant();
                subName = null;
            }
        }

        /// <summary>
        /// Go `ReadI32` (forStage=false: every comma part trimmed) or `readI32ForStage`
        /// (forStage=true: left-trimmed, and reading stops after a part containing whitespace).
        /// Only the parts present are written. False when the key is missing or empty.
        /// </summary>
        public static bool ReadInts(MugenDef.Section sec, string key, int[] outv, bool forStage) {
            string str = sec.Get(key);
            if (str.Length == 0) return false;
            string[] parts = str.Split(',');
            for (int i = 0; i < parts.Length && i < outv.Length; i++) {
                string p = forStage ? parts[i].TrimStart() : parts[i].Trim();
                if (p.Length > 0) outv[i] = MugenDef.Atoi(p);
                if (forStage && HasWhiteSpace(p)) break;
            }
            return true;
        }

        public static bool ReadFloats(MugenDef.Section sec, string key, float[] outv, bool forStage) {
            string str = sec.Get(key);
            if (str.Length == 0) return false;
            string[] parts = str.Split(',');
            for (int i = 0; i < parts.Length && i < outv.Length; i++) {
                string p = forStage ? parts[i].TrimStart() : parts[i].Trim();
                if (p.Length > 0) outv[i] = MugenDef.Atof(p);
                if (forStage && HasWhiteSpace(p)) break;
            }
            return true;
        }

        static bool ReadIntsMinLength(MugenDef.Section sec, string key, int[] outv) {
            string str = sec.Get(key);
            if (str.Length == 0) return false;
            int n = 0;
            foreach (var p in str.Split(',')) if (p.Trim().Length > 0) n++;
            if (n < outv.Length) return false;
            return ReadInts(sec, key, outv, true);
        }

        static List<int> ReadI32Csv(MugenDef.Section sec, string key) {
            var r = new List<int>();
            string str = sec.Get(key);
            if (str.Length == 0) return r;
            foreach (var part in str.Split(',')) {
                string p = part.TrimStart();
                if (p.Length > 0) r.Add(MugenDef.Atoi(p));
                if (HasWhiteSpace(p)) break;
            }
            return r;
        }

        static bool HasWhiteSpace(string s) {
            for (int i = 0; i < s.Length; i++) if (char.IsWhiteSpace(s[i])) return true;
            return false;
        }

        static bool ReadI32(MugenDef.Section sec, string key, ref int v) {
            int[] t = new int[] { v };
            bool r = ReadInts(sec, key, t, false);
            v = t[0];
            return r;
        }

        static bool ReadF32(MugenDef.Section sec, string key, ref float v) {
            float[] t = new float[] { v };
            bool r = ReadFloats(sec, key, t, false);
            v = t[0];
            return r;
        }

        static bool ReadF32Stage(MugenDef.Section sec, string key, ref float v) {
            float[] t = new float[] { v };
            bool r = ReadFloats(sec, key, t, true);
            v = t[0];
            return r;
        }

        static bool ReadBool(MugenDef.Section sec, string key, ref bool v) {
            string str = sec.Get(key);
            if (str.Length == 0) return false;
            string p = str.Split(',')[0].Trim();
            if (p.Length > 0) v = MugenDef.Atoi(p) != 0;
            return true;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>Go `ParseMugenVersion`: only 1.0 and 1.1 survive, everything else is 0.5.</summary>
        public static int[] ParseMugenVersion(string s) {
            int[] ver = new int[2];
            string[] parts = (s ?? "").Split('.');
            for (int i = 0; i < parts.Length && i < 2; i++) {
                int v;
                if (int.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.None,
                                 System.Globalization.CultureInfo.InvariantCulture, out v) && v <= 65535) {
                    ver[i] = v;
                } else {
                    return new int[] { 0, 5 };
                }
            }
            if (ver[0] == 1 && (ver[1] == 0 || ver[1] == 1)) return ver;
            return new int[] { 0, 5 };
        }

        /// <summary>Go `ParseIkemenVersion`.</summary>
        public static int[] ParseIkemenVersion(string s) {
            int[] ver = new int[3];
            if (string.IsNullOrEmpty(s)) return ver;
            string[] parts = s.Split('.');
            for (int i = 0; i < parts.Length && i < 3; i++) {
                int v;
                if (int.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.None,
                                 System.Globalization.CultureInfo.InvariantCulture, out v) && v <= 65535) {
                    ver[i] = v;
                } else {
                    break;
                }
            }
            return ver;
        }
    }

    /// <summary>
    /// Horizontal fighting camera: port of the X part of Go `Camera.Reset`, `Camera.Init`,
    /// `Camera.action` (Fighting_View) and `Camera.XBound`, plus the player x limits of
    /// system.go (`xmin`/`xmax` from screenleft/screenright).
    /// Simplifications, all deliberate for dev.4:
    ///  - zoom is fixed at 1.0 (zoomin = zoomout = 1, as if `ZoomActive` were off), so
    ///    reduceZoomSpeed and the zoom-in delay never change anything;
    ///  - everything is in stage units with localscl = 1 (`screenWidth` is the visible width
    ///    in stage units, normally LocalCoord[0]);
    ///  - the vertical axis is not simulated (Y stays at starty).
    /// Go's `sys.gameWidth/3200` smoothing step becomes `screenWidth/3200`, and gameLogicSpeed = 60.
    /// </summary>
    public class StageCamera {
        public readonly StageDefinition Stage;
        public float ScreenWidth;
        public float X, Y;                   // Go Camera.Pos
        public float Scale = 1f;
        public float BoundL, BoundR;         // Go boundL / boundR
        public float XMin, XMax;
        public float MinLeft, MaxRight;
        public float HalfWidth;
        public int Tension;
        public float TensionVel;
        public bool AutoCenter;
        float zoomIn = 1f, zoomOut = 1f;
        bool snapToTarget;
        // last known extremes (Go SaveRestoreTracking)
        float prevLeftest, prevRightest;

        public StageCamera(StageDefinition stage, float screenWidth) {
            Stage = stage;
            ScreenWidth = screenWidth;
            Tension = stage.Tension;
            TensionVel = stage.TensionVel;
            AutoCenter = stage.AutoCenter;
            Init();
        }

        /// <summary>Go `Camera.Reset` (X part, zoomout = 1 so the MUGEN 1.1 zoom margin is 0).</summary>
        public void Reset() {
            BoundL = (float)(Stage.BoundLeft - Stage.StartX);
            BoundR = (float)(Stage.BoundRight - Stage.StartX);
            HalfWidth = ScreenWidth / 2f;
            XMin = BoundL - HalfWidth;
            XMax = BoundR + HalfWidth;
            TensionVel = Math.Max(Math.Min(TensionVel, 20f), 0f);
            MaxRight = (float)Stage.BoundRight + HalfWidth / zoomOut;
            MinLeft = (float)Stage.BoundLeft - HalfWidth / zoomOut;
        }

        /// <summary>Go `Camera.Init`.</summary>
        public void Init() {
            Reset();
            Scale = 1f;   // zoom fixed at 1 (Go uses startzoom)
            X = Stage.StartX;
            Y = Stage.StartY;
            prevLeftest = X; prevRightest = X;
            snapToTarget = true;
        }

        /// <summary>Go `Camera.XBound`.</summary>
        public float XBound(float scl, float x) {
            float lo = BoundL - HalfWidth + HalfWidth / scl;
            float hi = BoundR + HalfWidth - HalfWidth / scl;
            return x < lo ? lo : (x > hi ? hi : x);
        }

        /// <summary>Left edge of the visible area (Go ScreenPos[0] with no BG offset).</summary>
        public float ScreenLeftEdge => X - HalfWidth / Scale;
        public float ScreenRightEdge => ScreenLeftEdge + ScreenWidth / Scale;

        /// <summary>Go system.go `xmin`: the leftmost x a player may stand on.</summary>
        public float PlayerXMin {
            get {
                float xmin = ScreenLeftEdge + Stage.ScreenLeft;
                float xmax = ScreenLeftEdge + ScreenWidth / Scale - Stage.ScreenRight;
                if (xmin > xmax) xmin = (xmin + xmax) / 2f;
                if (Math.Abs(MinLeft - xmin) < 0.0001f) xmin = MinLeft;
                return xmin;
            }
        }

        /// <summary>Go system.go `xmax`.</summary>
        public float PlayerXMax {
            get {
                float xmin = ScreenLeftEdge + Stage.ScreenLeft;
                float xmax = ScreenLeftEdge + ScreenWidth / Scale - Stage.ScreenRight;
                if (xmin > xmax) xmax = (xmin + xmax) / 2f;
                if (Math.Abs(MaxRight - xmax) < 0.0001f) xmax = MaxRight;
                return xmax;
            }
        }

        /// <summary>Two-player convenience overload (edge widths and velocities 0).</summary>
        public float Update(float p1x, float p2x) {
            return Update(Math.Min(p1x, p2x), Math.Max(p1x, p2x), 0f, 0f);
        }

        /// <summary>
        /// One tick of Go `Camera.action` (Fighting_View, X only) followed by the quarter-pixel
        /// rounding system.go applies when zoom is off. <paramref name="leftest"/> /
        /// <paramref name="rightest"/> are the outermost player edges (char.go: pos +/- edge width).
        /// Returns the new camera X.
        /// </summary>
        public float Update(float leftest, float rightest, float leftestVel, float rightestVel) {
            if (float.IsNaN(leftest) || float.IsNaN(rightest)) { leftest = prevLeftest; rightest = prevRightest; }
            prevLeftest = leftest; prevRightest = rightest;

            float x = X, scale = Scale;
            float tension = Math.Max(0f, (float)Tension);
            float oldLeft = x - HalfWidth / scale, oldRight = x + HalfWidth / scale;
            float targetLeft = oldLeft, targetRight = oldRight;

            if (AutoCenter) {
                targetLeft = Math.Min(Math.Max((leftest + rightest) / 2f - HalfWidth / scale, MinLeft), MaxRight - 2f * HalfWidth / scale);
                targetRight = targetLeft + 2f * HalfWidth / scale;
            }

            if (leftest < targetLeft + tension) {
                float diff = targetLeft - Math.Max(leftest - tension, MinLeft);
                targetLeft = Math.Max(leftest - tension, MinLeft);
                targetRight = Math.Max(oldRight - diff, Math.Min(rightest + tension, MaxRight));
            } else if (rightest > targetRight - tension) {
                float diff = targetRight - Math.Min(rightest + tension, MaxRight);
                targetRight = Math.Min(rightest + tension, MaxRight);
                targetLeft = Math.Min(oldLeft - diff, Math.Max(leftest - tension, MinLeft));
            }

            float sl = (float)Stage.ScreenLeft, sr = (float)Stage.ScreenRight;
            if (HalfWidth * 2f / (targetRight - targetLeft) < zoomOut) {
                float rLeft = Math.Max(targetLeft + tension - leftest, 0f);
                float rRight = Math.Max(rightest - (targetRight - tension), 0f);
                float diff = 2f * ((targetRight - targetLeft) / 2f - HalfWidth / zoomOut);
                if (rLeft > rRight) {
                    float diff2 = rLeft - rRight;
                    targetRight -= Math.Min(diff2, diff);
                    diff -= Math.Min(diff2, diff);
                } else if (rRight > rLeft) {
                    float diff2 = rRight - rLeft;
                    targetLeft += Math.Min(diff2, diff);
                    diff -= Math.Min(diff2, diff);
                }
                targetLeft += diff / 2f;
                targetRight -= diff / 2f;
                if (leftest - targetLeft < sl) {
                    float d = Math.Min(sl - (leftest - targetLeft), targetLeft - MinLeft);
                    if (targetRight - rightest < sr) {
                        float d2 = Math.Min(sr - (targetRight - rightest), MaxRight - targetRight);
                        d = d - d2;
                    }
                    targetLeft -= d;
                    targetRight -= d;
                } else if (targetRight - rightest < sr) {
                    float d = Math.Min(sr - (targetRight - rightest), MaxRight - targetRight);
                    targetLeft += d;
                    targetRight += d;
                }
            }
            // Zoom-in block of Go action: with zoomin = 1 the window is never narrower than
            // the screen, so it cannot apply.

            float newLeft = oldLeft, newRight = oldRight;
            const float logicScale = 1f;   // 60 / gameLogicSpeed
            if (snapToTarget) {
                newLeft = targetLeft; newRight = targetRight;
            } else {
                float diff = ScreenWidth / 3200f;
                for (int i = 0; i < 3; i++) {
                    newLeft = newLeft + (targetLeft - newLeft) * 0.05f * logicScale * TensionVel;
                    newRight = newRight + (targetRight - newRight) * 0.05f * logicScale * TensionVel;
                    float diffLeft = targetLeft - newLeft;
                    float diffRight = targetRight - newRight;

                    if (Math.Abs(diffLeft) <= diff * logicScale * TensionVel) newLeft = targetLeft;
                    else if (diffLeft > 0f) newLeft += diff * logicScale * TensionVel;
                    else newLeft -= diff * logicScale * TensionVel;
                    if (newLeft - oldLeft > 0f && newLeft - oldLeft < rightestVel) newLeft = Math.Min(oldLeft + rightestVel, targetLeft);
                    else if (newLeft - oldLeft < 0f && newLeft - oldLeft > leftestVel) newLeft = Math.Max(oldLeft + leftestVel, targetLeft);

                    if (Math.Abs(diffRight) <= diff * logicScale * TensionVel) newRight = targetRight;
                    else if (diffRight > 0f) newRight += diff * logicScale * TensionVel;
                    else newRight -= diff * logicScale * TensionVel;
                    if (newRight - oldRight > 0f && newRight - oldRight < rightestVel) newRight = Math.Min(oldRight + rightestVel, targetRight);
                    else if (newRight - oldRight < 0f && newRight - oldRight > leftestVel) newRight = Math.Max(oldRight + leftestVel, targetRight);
                }
            }
            // newScale = min(halfWidth*2/(newRight-newLeft), zoomin); with zoom fixed it stays 1 and
            // reduceZoomSpeed returns its input (zoominspeed/zoomoutspeed treated as >= 1).
            float newX = (newLeft + newRight) / 2f;
            snapToTarget = false;

            // system.go: "Lower the precision to prevent errors in Pos X" when zoom is off
            newX = (float)(Math.Ceiling((double)newX * 4.0 - 0.5) / 4.0);
            X = newX;
            Scale = 1f;
            return X;
        }
    }
}
