using System;
using System.Collections.Generic;

namespace IK.Core {
    // Port of the fight screen ("lifebar") loader of Ikemen GO v1.0.0.
    // Reference: engine/ikemen-go/src/fightscreen.go (loadFightScreen, readLifeBar, readPowerBar,
    // readFightScreenFace/Name/WinIcon/Time/Combo/Round, readFSText, readFSBgTextSnd,
    // readMultipleValues*) and engine/ikemen-go/src/common.go (Layout.Read, AnimLayout.Read,
    // AnimTextSnd.Read). In v1.0.0 the file that older Ikemen versions called lifebar.go is
    // fightscreen.go; the names below follow the v1.0.0 source.
    //
    // Loader only: no UnityEngine, no rendering, no font/sprite decoding. Sprite numbers, font
    // indices, sound pairs and timings are exposed as plain data for the fight screen to draw.

    /// <summary>Small helpers that mirror IniSection.ReadI32 / ReadF32 / ReadBool / getText.</summary>
    public static class FightIni {
        /// <summary>
        /// Go `IniSection.ReadI32`: false when the key is missing or empty; otherwise each
        /// non-empty comma-separated value overwrites the matching output slot (others keep
        /// their previous value).
        /// </summary>
        public static bool ReadInts(MugenDef.Section s, string key, int[] outv) {
            if (s == null) return false;
            string str = s.Get(key, "");
            if (str.Length == 0) return false;
            string[] parts = str.Split(',');
            for (int i = 0; i < parts.Length && i < outv.Length; i++) {
                string p = parts[i].Trim();
                if (p.Length > 0) outv[i] = MugenDef.Atoi(p);
            }
            return true;
        }

        public static bool ReadInt(MugenDef.Section s, string key, ref int v) {
            int[] tmp = new int[] { v };
            bool ok = ReadInts(s, key, tmp);
            v = tmp[0];
            return ok;
        }

        public static bool ReadFloats(MugenDef.Section s, string key, float[] outv) {
            if (s == null) return false;
            string str = s.Get(key, "");
            if (str.Length == 0) return false;
            string[] parts = str.Split(',');
            for (int i = 0; i < parts.Length && i < outv.Length; i++) {
                string p = parts[i].Trim();
                if (p.Length > 0) outv[i] = MugenDef.Atof(p);
            }
            return true;
        }

        public static bool ReadFloat(MugenDef.Section s, string key, ref float v) {
            float[] tmp = new float[] { v };
            bool ok = ReadFloats(s, key, tmp);
            v = tmp[0];
            return ok;
        }

        public static bool ReadBool(MugenDef.Section s, string key, ref bool v) {
            if (s == null) return false;
            string str = s.Get(key, "");
            if (str.Length == 0) return false;
            string first = str.Split(',')[0].Trim();
            if (first.Length > 0) v = MugenDef.Atoi(first) != 0;
            return true;
        }

        /// <summary>
        /// Go `IniSection.getText`: strips the surrounding quotes; an unquoted value is returned
        /// as-is (Go reports an error but every caller in the fight screen ignores it).
        /// </summary>
        public static string GetText(MugenDef.Section s, string key) {
            string str = s.Get(key, "");
            if (str.Length >= 2 && str[0] == '"' && str[str.Length - 1] == '"')
                return str.Substring(1, str.Length - 2);
            return str;
        }

        /// <summary>
        /// Numbered keys of the form `prefix + name + digits + "."` (Go readMultipleValues /
        /// readMultipleFSText). Returns the distinct numbers, sorted ascending.
        /// Note: Go matches with an unanchored regexp; here the prefix is anchored, which gives
        /// the same set for every key present in the shipped fight.def.
        /// </summary>
        public static List<int> NumberedKeys(MugenDef.Section s, string prefix, string name) {
            var result = new List<int>();
            if (s == null) return result;
            string start = prefix + name;
            for (int i = 0; i < s.Lines.Count; i++) {
                string k = s.Lines[i].Key;
                if (!k.StartsWith(start, StringComparison.Ordinal)) continue;
                int j = start.Length;
                int d = j;
                while (d < k.Length && k[d] >= '0' && k[d] <= '9') d++;
                if (d == j || d >= k.Length || k[d] != '.') continue;
                int v = MugenDef.Atoi(k.Substring(j, d - j));
                if (!result.Contains(v)) result.Add(v);
            }
            result.Sort();
            return result;
        }
    }

    /// <summary>Port of `Layout` (common.go): offset, facing, layer, scale, rotation, window.</summary>
    public class FightLayout {
        public float OffsetX, OffsetY;
        public int Facing = 1, VFacing = 1;
        public int LayerNo;
        public float ScaleX = 1f, ScaleY = 1f;
        public float Angle, XAngle, YAngle;
        public float XShear;
        public float FocalLength = 2048f;
        public string Projection = "orthographic";
        /// <summary>x, y, width, height after the Go normalisation; only valid when HasWindow.</summary>
        public int[] Window = new int[4];
        /// <summary>False means "whole screen" (Go uses sys.scrrect).</summary>
        public bool HasWindow;

        public FightLayout() { }
        public FightLayout(int layerNo) { LayerNo = layerNo; }

        /// <summary>Port of `Layout.Read(pre, is)`.</summary>
        public void Read(MugenDef.Section s, string pre) {
            if (s == null) return;
            float[] off = new float[] { OffsetX, OffsetY };
            FightIni.ReadFloats(s, pre + "offset", off);
            OffsetX = off[0]; OffsetY = off[1];
            string f = s.Get(pre + "facing", "");
            if (f.Length > 0) Facing = MugenDef.Atoi(f) < 0 ? -1 : 1;
            string vf = s.Get(pre + "vfacing", "");
            if (vf.Length > 0) VFacing = MugenDef.Atoi(vf) < 0 ? -1 : 1;
            int ln = LayerNo;
            FightIni.ReadInt(s, pre + "layerno", ref ln);
            LayerNo = Math.Min(2, ln);
            float[] sc = new float[] { ScaleX, ScaleY };
            FightIni.ReadFloats(s, pre + "scale", sc);
            ScaleX = sc[0]; ScaleY = sc[1];
            FightIni.ReadFloat(s, pre + "angle", ref Angle);
            FightIni.ReadFloat(s, pre + "xangle", ref XAngle);
            FightIni.ReadFloat(s, pre + "yangle", ref YAngle);
            FightIni.ReadFloat(s, pre + "xshear", ref XShear);
            FightIni.ReadFloat(s, pre + "focallength", ref FocalLength);
            if (s.Has(pre + "projection")) {
                string p = s.Get(pre + "projection", "").Trim().ToLowerInvariant();
                if (p == "orthographic" || p == "perspective" || p == "perspective2") Projection = p;
            }
            int[] w = new int[4];
            if (FightIni.ReadInts(s, pre + "window", w)) {
                if (w[2] < w[0]) { int t = w[2]; w[2] = w[0]; w[0] = t; }
                if (w[3] < w[1]) { int t = w[3]; w[3] = w[1]; w[1] = t; }
                w[2] -= w[0];
                w[3] -= w[1];
                Window = w;
                HasWindow = true;
            } else {
                Window = new int[4];
                HasWindow = false;
            }
        }
    }

    /// <summary>
    /// Port of `AnimLayout` (common.go): either a single sprite (`.spr = g,n`) or an AIR action
    /// (`.anim = n`, only accepted when the action exists in the fight.def animation table),
    /// plus its layout.
    /// </summary>
    public class FightAnimLayout {
        public int SprGroup = -1, SprNumber;
        public bool HasSprite;
        /// <summary>AIR action number, -1 when none (or when the action does not exist).</summary>
        public int AnimNo = -1;
        public FightLayout Layout;

        public FightAnimLayout() { Layout = new FightLayout(0); }
        public FightAnimLayout(int layerNo) { Layout = new FightLayout(layerNo); }

        /// <summary>True when Go would have animation frames (len(anim.frames) > 0).</summary>
        public bool HasFrames => HasSprite || AnimNo >= 0;

        public static FightAnimLayout Read(MugenDef.Section s, string pre, AirFile table, int layerNo) {
            var al = new FightAnimLayout(layerNo);
            al.ReadInto(s, pre, table, layerNo);
            return al;
        }

        /// <summary>Port of `AnimLayout.Read`.</summary>
        public void ReadInto(MugenDef.Section s, string pre, AirFile table, int layerNo) {
            if (s == null) return;
            int[] gn = new int[] { 0, 0 };
            if (FightIni.ReadInts(s, pre + "spr", gn)) {
                SprGroup = gn[0]; SprNumber = gn[1];
                HasSprite = true;
                AnimNo = -1;
                Layout = new FightLayout(layerNo);
            }
            int n = 0;
            if (FightIni.ReadInt(s, pre + "anim", ref n)) {
                bool exists = table == null || table.Get(n) != null;
                if (exists) {
                    AnimNo = n;
                    HasSprite = false;
                    Layout = new FightLayout(layerNo);
                }
            }
            Layout.Read(s, pre);
        }
    }

    /// <summary>Port of `FSText` / `readFSText`: `.font = idx, bank, align, r, g, b, a, height`, `.text`, layout.</summary>
    public class FightText {
        /// <summary>Go default {-1, 0, align, -1, -1, -1, 255, -1}.</summary>
        public int[] Font = new int[] { -1, 0, 0, -1, -1, -1, 255, -1 };
        public string Text = "";
        public bool HasText;
        public FightLayout Layout;

        public int FontIndex => Font[0];
        public int FontBank => Font[1];
        public int FontAlign => Font[2];

        public static FightText Read(MugenDef.Section s, string pre, string defaultText, int layerNo, int align) {
            var t = new FightText();
            t.Font[2] = align;
            t.Layout = new FightLayout(layerNo);
            if (s == null) { t.Text = defaultText ?? ""; return t; }
            FightIni.ReadInts(s, pre + "font", t.Font);
            if (s.Has(pre + "text")) {
                t.Text = FightIni.GetText(s, pre + "text");
                t.HasText = true;
            } else {
                t.Text = defaultText ?? "";
            }
            t.Layout.Read(s, pre);
            return t;
        }
    }

    /// <summary>
    /// Port of `AnimTextSnd` (common.go): the generic `<prefix>.anim/.spr/.text/.font/.snd/
    /// .displaytime` element used by the round announcements.
    /// </summary>
    public class FightElement {
        public int[] Snd = new int[] { -1, 0 };
        public FightText Text;
        public FightAnimLayout Anim;
        /// <summary>Go default -2 (= unlimited).</summary>
        public int DisplayTime = -2;

        public bool HasSound => Snd[0] >= 0;

        public static FightElement Read(MugenDef.Section s, string pre, AirFile table, int layerNo) {
            var e = new FightElement();
            FightIni.ReadInts(s, pre + "snd", e.Snd);
            e.Text = FightText.Read(s, pre, "", layerNo, 0);
            e.Anim = new FightAnimLayout(layerNo);
            e.Anim.ReadInto(s, pre, table, layerNo);
            FightIni.ReadInt(s, pre + "displaytime", ref e.DisplayTime);
            return e;
        }
    }

    /// <summary>Port of `FSBgTextSnd` / `readFSBgTextSnd` (win type banners such as p1.perfect.).</summary>
    public class FightBgTextSnd {
        public int[] Pos = new int[2];
        public FightText Text;
        public FightAnimLayout Bg;
        public int Time, DisplayTime, SndTime;
        public int[] Snd = new int[] { -1, 0 };

        public static FightBgTextSnd Read(MugenDef.Section s, string pre, AirFile table, int layerNo) {
            var b = new FightBgTextSnd();
            FightIni.ReadInts(s, pre + "pos", b.Pos);
            b.Text = FightText.Read(s, pre + "text.", "", layerNo, 0);
            b.Bg = FightAnimLayout.Read(s, pre + "bg.", table, layerNo);
            FightIni.ReadInt(s, pre + "time", ref b.Time);
            FightIni.ReadInt(s, pre + "displaytime", ref b.DisplayTime);
            FightIni.ReadInts(s, pre + "snd", b.Snd);
            b.SndTime = b.Time;
            FightIni.ReadInt(s, pre + "sndtime", ref b.SndTime);
            return b;
        }
    }

    /// <summary>Port of `LifeBar` / `readLifeBar`.</summary>
    public class LifeBarDef {
        public int[] Pos = new int[2];
        public int[] RangeX = new int[2];
        public int[] RangeY = new int[2];
        public FightAnimLayout Bg0, Bg1, Bg2, Top, Mid, Shift, Warn;
        /// <summary>front elements keyed by life percentage (0 = `front.`, 25 = `front25.` ...).</summary>
        public SortedDictionary<float, FightAnimLayout> Front = new SortedDictionary<float, FightAnimLayout>();
        /// <summary>red life elements keyed by red value.</summary>
        public SortedDictionary<int, FightAnimLayout> Red = new SortedDictionary<int, FightAnimLayout>();
        public SortedDictionary<int, FightText> Value = new SortedDictionary<int, FightText>();
        public SortedDictionary<int, FightText> RedValue = new SortedDictionary<int, FightText>();
        public int[] WarnRange = new int[2];
        // Section-wide (unprefixed) mid behaviour; Go defaults from newLifeBar.
        public bool MidShift;
        public bool MidFreeze = true;
        public int MidDelay = 30;
        public float MidMult = 1f;
        public float MidSteps = 8f;
        public bool ScaleFill;
        public bool LeaderOnTop;

        /// <summary>The plain `front.` element (key 0).</summary>
        public FightAnimLayout FrontDefault => Front.ContainsKey(0f) ? Front[0f] : null;

        public static LifeBarDef Read(MugenDef.Section s, string pre, AirFile at) {
            var lb = new LifeBarDef();
            FightIni.ReadInts(s, pre + "pos", lb.Pos);
            FightIni.ReadInts(s, pre + "range.x", lb.RangeX);
            FightIni.ReadInts(s, pre + "range.y", lb.RangeY);
            lb.Bg0 = FightAnimLayout.Read(s, pre + "bg0.", at, 0);
            lb.Bg1 = FightAnimLayout.Read(s, pre + "bg1.", at, 0);
            lb.Bg2 = FightAnimLayout.Read(s, pre + "bg2.", at, 0);
            lb.Top = FightAnimLayout.Read(s, pre + "top.", at, 0);
            lb.Mid = FightAnimLayout.Read(s, pre + "mid.", at, 0);
            lb.Shift = FightAnimLayout.Read(s, pre + "shift.", at, 0);
            lb.Warn = FightAnimLayout.Read(s, pre + "warn.", at, 0);

            foreach (int k in FightIni.NumberedKeys(s, pre, "front"))
                lb.Front[(float)k] = FightAnimLayout.Read(s, pre + "front" + k + ".", at, 0);
            if (!lb.Front.ContainsKey(0f)) lb.Front[0f] = FightAnimLayout.Read(s, pre + "front.", at, 0);

            foreach (int k in FightIni.NumberedKeys(s, pre, "red"))
                lb.Red[k] = FightAnimLayout.Read(s, pre + "red" + k + ".", at, 0);
            if (!lb.Red.ContainsKey(0)) lb.Red[0] = FightAnimLayout.Read(s, pre + "red.", at, 0);

            lb.Value[0] = FightText.Read(s, pre + "value.", "%d", 0, 0);
            foreach (int k in FightIni.NumberedKeys(s, pre, "value"))
                lb.Value[k] = FightText.Read(s, pre + "value" + k + ".", "%d", 0, 0);
            lb.RedValue[0] = FightText.Read(s, pre + "red.value.", "%d", 0, 0);
            foreach (int k in FightIni.NumberedKeys(s, pre, "red.value"))
                lb.RedValue[k] = FightText.Read(s, pre + "red.value" + k + ".", "%d", 0, 0);

            FightIni.ReadBool(s, "mid.shift", ref lb.MidShift);
            FightIni.ReadBool(s, "mid.freeze", ref lb.MidFreeze);
            FightIni.ReadInt(s, "mid.delay", ref lb.MidDelay);
            FightIni.ReadFloat(s, "mid.mult", ref lb.MidMult);
            FightIni.ReadFloat(s, "mid.steps", ref lb.MidSteps);
            lb.MidSteps = Math.Max(1f, lb.MidSteps);

            FightIni.ReadInts(s, pre + "warn.range", lb.WarnRange);
            FightIni.ReadBool(s, pre + "scalefill", ref lb.ScaleFill);
            FightIni.ReadBool(s, "leaderontop", ref lb.LeaderOnTop);
            return lb;
        }
    }

    /// <summary>Port of `PowerBar` / `readPowerBar`. Map key -1 stands for the `max` variant.</summary>
    public class PowerBarDef {
        public const int MaxKey = -1;
        public int[] Pos = new int[2];
        public int[] RangeX = new int[2];
        public int[] RangeY = new int[2];
        public SortedDictionary<int, FightAnimLayout> Bg0 = new SortedDictionary<int, FightAnimLayout>();
        public FightAnimLayout Bg1, Bg2, Top, Mid, Shift;
        public SortedDictionary<int, FightAnimLayout> Front = new SortedDictionary<int, FightAnimLayout>();
        public SortedDictionary<int, FightText> Counter = new SortedDictionary<int, FightText>();
        public SortedDictionary<int, FightText> Value = new SortedDictionary<int, FightText>();
        public int CounterRounding = 1000;
        public int ValueRounding = 1;
        /// <summary>level1..level9 sounds, [i] = {group, number}; {-1,-1} when unset.</summary>
        public int[][] LevelSnd = new int[9][];
        public int[] LevelMaxSnd = new int[] { -1, -1 };
        public bool LevelBars, ScaleFill, LeaderOnTop;

        // Go readPBKeys: `pre+name` followed by a number or "max" and a dot.
        static List<KeyValuePair<int, string>> PBKeys(MugenDef.Section s, string pre, string name) {
            var result = new List<KeyValuePair<int, string>>();
            var seen = new HashSet<int>();
            string prefix = (pre + name).ToLowerInvariant();
            for (int i = 0; i < s.Lines.Count; i++) {
                string k = s.Lines[i].Key;
                if (!k.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string rest = k.Substring(prefix.Length);
                int dot = rest.IndexOf('.');
                if (dot <= 0) continue;
                string keyStr = rest.Substring(0, dot);
                if (string.Equals(keyStr, "max", StringComparison.OrdinalIgnoreCase)) {
                    if (seen.Add(MaxKey)) result.Add(new KeyValuePair<int, string>(MaxKey, keyStr));
                } else if (MugenDef.IsNumeric(keyStr)) {
                    int key = MugenDef.Atoi(keyStr);
                    if (seen.Add(key)) result.Add(new KeyValuePair<int, string>(key, keyStr));
                }
            }
            return result;
        }

        public static PowerBarDef Read(MugenDef.Section s, string pre, AirFile at) {
            var pb = new PowerBarDef();
            for (int i = 0; i < pb.LevelSnd.Length; i++) pb.LevelSnd[i] = new int[] { -1, -1 };
            FightIni.ReadInts(s, pre + "pos", pb.Pos);
            FightIni.ReadInts(s, pre + "range.x", pb.RangeX);
            FightIni.ReadInts(s, pre + "range.y", pb.RangeY);

            foreach (var kv in PBKeys(s, pre, "bg0"))
                pb.Bg0[kv.Key] = FightAnimLayout.Read(s, pre + "bg0" + kv.Value + ".", at, 0);
            if (!pb.Bg0.ContainsKey(0)) pb.Bg0[0] = FightAnimLayout.Read(s, pre + "bg0.", at, 0);
            pb.Bg1 = FightAnimLayout.Read(s, pre + "bg1.", at, 0);
            pb.Bg2 = FightAnimLayout.Read(s, pre + "bg2.", at, 0);
            pb.Mid = FightAnimLayout.Read(s, pre + "mid.", at, 0);
            pb.Top = FightAnimLayout.Read(s, pre + "top.", at, 0);
            foreach (var kv in PBKeys(s, pre, "front"))
                pb.Front[kv.Key] = FightAnimLayout.Read(s, pre + "front" + kv.Value + ".", at, 0);
            if (!pb.Front.ContainsKey(0)) pb.Front[0] = FightAnimLayout.Read(s, pre + "front.", at, 0);

            pb.Shift = FightAnimLayout.Read(s, pre + "shift.", at, 0);
            pb.Counter[0] = FightText.Read(s, pre + "counter.", "%i", 0, 0);
            foreach (var kv in PBKeys(s, pre, "counter"))
                pb.Counter[kv.Key] = FightText.Read(s, pre + "counter" + kv.Value + ".", "%i", 0, 0);
            pb.Value[0] = FightText.Read(s, pre + "value.", "%d", 0, 0);
            foreach (var kv in PBKeys(s, pre, "value"))
                pb.Value[kv.Key] = FightText.Read(s, pre + "value" + kv.Value + ".", "%d", 0, 0);

            FightIni.ReadInt(s, pre + "counter.format.rounding", ref pb.CounterRounding);
            FightIni.ReadInt(s, pre + "value.format.power.rounding", ref pb.ValueRounding);
            if (pb.CounterRounding < 1) pb.CounterRounding = 1000;
            if (pb.ValueRounding < 1) pb.ValueRounding = 1;

            for (int i = 0; i < pb.LevelSnd.Length; i++) {
                if (!FightIni.ReadInts(s, pre + "level" + (i + 1) + ".snd", pb.LevelSnd[i]))
                    FightIni.ReadInts(s, "level" + (i + 1) + ".snd", pb.LevelSnd[i]);
            }
            if (!FightIni.ReadInts(s, pre + "levelmax.snd", pb.LevelMaxSnd))
                FightIni.ReadInts(s, "levelmax.snd", pb.LevelMaxSnd);

            FightIni.ReadBool(s, pre + "levelbars", ref pb.LevelBars);
            FightIni.ReadBool(s, pre + "scalefill", ref pb.ScaleFill);
            FightIni.ReadBool(s, "leaderontop", ref pb.LeaderOnTop);
            return pb;
        }
    }

    /// <summary>Port of `FightScreenFace` / `readFightScreenFace` (leader part + teammate layout).</summary>
    public class FaceDef {
        public int[] Pos = new int[2];
        public FightAnimLayout Bg, Bg0, Bg1, Bg2, Top, Ko;
        public int[] FaceSpr = new int[] { -1, 0 };
        public FightLayout FaceLayout;
        public bool FacePalShare = true, FacePalFxShare, FaceDarkenShare, LeaderOnTop;
        public int[] TeammatePos = new int[2];
        public int[] TeammateSpacing = new int[2];
        public FightAnimLayout TeammateBg, TeammateBg0, TeammateBg1, TeammateBg2, TeammateTop, TeammateKo;
        public int[] TeammateFaceSpr = new int[] { -1, 0 };
        public FightLayout TeammateFaceLayout;
        public bool TeammateKoHide, TeammateFacePalShare = true;

        public static FaceDef Read(MugenDef.Section s, string pre, AirFile at) {
            var fa = new FaceDef();
            FightIni.ReadInts(s, pre + "pos", fa.Pos);
            fa.Bg = FightAnimLayout.Read(s, pre + "bg.", at, 0);
            fa.Bg0 = FightAnimLayout.Read(s, pre + "bg0.", at, 0);
            fa.Bg1 = FightAnimLayout.Read(s, pre + "bg1.", at, 0);
            fa.Bg2 = FightAnimLayout.Read(s, pre + "bg2.", at, 0);
            fa.Top = FightAnimLayout.Read(s, pre + "top.", at, 0);
            fa.Ko = FightAnimLayout.Read(s, pre + "ko.", at, 0);
            FightIni.ReadInts(s, pre + "face.spr", fa.FaceSpr);
            fa.FaceLayout = new FightLayout(0);
            fa.FaceLayout.Read(s, pre + "face.");
            FightIni.ReadBool(s, pre + "face.palshare", ref fa.FacePalShare);
            FightIni.ReadBool(s, pre + "face.palfxshare", ref fa.FacePalFxShare);
            FightIni.ReadBool(s, pre + "face.darkenshare", ref fa.FaceDarkenShare);
            FightIni.ReadBool(s, "leaderontop", ref fa.LeaderOnTop);
            FightIni.ReadInts(s, pre + "teammate.pos", fa.TeammatePos);
            FightIni.ReadInts(s, pre + "teammate.spacing", fa.TeammateSpacing);
            fa.TeammateBg = FightAnimLayout.Read(s, pre + "teammate.bg.", at, 0);
            fa.TeammateBg0 = FightAnimLayout.Read(s, pre + "teammate.bg0.", at, 0);
            fa.TeammateBg1 = FightAnimLayout.Read(s, pre + "teammate.bg1.", at, 0);
            fa.TeammateBg2 = FightAnimLayout.Read(s, pre + "teammate.bg2.", at, 0);
            fa.TeammateTop = FightAnimLayout.Read(s, pre + "teammate.top.", at, 0);
            fa.TeammateKo = FightAnimLayout.Read(s, pre + "teammate.ko.", at, 0);
            FightIni.ReadInts(s, pre + "teammate.face.spr", fa.TeammateFaceSpr);
            fa.TeammateFaceLayout = new FightLayout(0);
            fa.TeammateFaceLayout.Read(s, pre + "teammate.face.");
            FightIni.ReadBool(s, pre + "teammate.ko.hide", ref fa.TeammateKoHide);
            FightIni.ReadBool(s, pre + "teammate.face.palshare", ref fa.TeammateFacePalShare);
            return fa;
        }
    }

    /// <summary>Port of `FightScreenName` / `readFightScreenName`.</summary>
    public class NameDef {
        public int[] Pos = new int[2];
        public FightText Name;
        public FightAnimLayout Bg, Top;
        public int[] TeammatePos = new int[2];
        public int[] TeammateSpacing = new int[2];
        public FightText TeammateName;
        public FightAnimLayout TeammateBg;
        public bool TeammateKoHide, LeaderOnTop;

        public static NameDef Read(MugenDef.Section s, string pre, AirFile at) {
            var nm = new NameDef();
            FightIni.ReadInts(s, pre + "pos", nm.Pos);
            FightIni.ReadBool(s, "leaderontop", ref nm.LeaderOnTop);
            nm.Name = FightText.Read(s, pre + "name.", "", 0, 0);
            nm.Bg = FightAnimLayout.Read(s, pre + "bg.", at, 0);
            nm.Top = FightAnimLayout.Read(s, pre + "top.", at, 0);
            FightIni.ReadInts(s, pre + "teammate.pos", nm.TeammatePos);
            FightIni.ReadInts(s, pre + "teammate.spacing", nm.TeammateSpacing);
            nm.TeammateName = FightText.Read(s, pre + "teammate.name.", "", 0, 0);
            nm.TeammateBg = FightAnimLayout.Read(s, pre + "teammate.bg.", at, 0);
            FightIni.ReadBool(s, pre + "teammate.ko.hide", ref nm.TeammateKoHide);
            return nm;
        }
    }

    /// <summary>Go WinType order (fightscreen.go), used to index win icons and win banners.</summary>
    public enum FightWinType { Normal, Special, Hyper, Cheese, Time, Throw, Suicide, Teammate, Perfect, Clutch, NumTypes }

    /// <summary>Port of `FightScreenWinIcon` / `readFightScreenWinIcon`.</summary>
    public class WinIconDef {
        public int[] Pos = new int[2];
        public int[] IconOffset = new int[2];
        public int UseIconUpTo = 4;
        public FightText Counter;
        public FightAnimLayout Bg0, Top;
        /// <summary>Indexed by (int)FightWinType.</summary>
        public FightAnimLayout[] Icon = new FightAnimLayout[(int)FightWinType.NumTypes];

        static readonly string[] IconPrefixes = {
            "n.", "s.", "h.", "c.", "t.", "throw.", "suicide.", "teammate.", "perfect.", "clutch."
        };

        public static WinIconDef Read(MugenDef.Section s, string pre, AirFile at) {
            var wi = new WinIconDef();
            FightIni.ReadInts(s, pre + "pos", wi.Pos);
            FightIni.ReadInts(s, pre + "iconoffset", wi.IconOffset);
            FightIni.ReadInt(s, "useiconupto", ref wi.UseIconUpTo);
            wi.Counter = FightText.Read(s, pre + "counter.", "%i", 0, 0);
            wi.Bg0 = FightAnimLayout.Read(s, pre + "bg0.", at, 0);
            wi.Top = FightAnimLayout.Read(s, pre + "top.", at, 0);
            for (int i = 0; i < IconPrefixes.Length; i++)
                wi.Icon[i] = FightAnimLayout.Read(s, pre + IconPrefixes[i], at, 0);
            return wi;
        }
    }

    /// <summary>Port of `FightScreenTime` / `readFightScreenTime` (unprefixed section).</summary>
    public class TimeDef {
        public int[] Pos = new int[2];
        /// <summary>counter. (key 0) and counterN. variants keyed by remaining count.</summary>
        public SortedDictionary<int, FightText> Counter = new SortedDictionary<int, FightText>();
        public FightAnimLayout Bg, Top;
        /// <summary>Ticks per displayed count (Go default 60).</summary>
        public int FramesPerCount = 60;

        public static TimeDef Read(MugenDef.Section s, AirFile at) {
            var ti = new TimeDef();
            FightIni.ReadInts(s, "pos", ti.Pos);
            ti.Counter[0] = FightText.Read(s, "counter.", "", 0, 0);
            foreach (int k in FightIni.NumberedKeys(s, "", "counter"))
                ti.Counter[k] = FightText.Read(s, "counter" + k + ".", "", 0, 0);
            ti.Bg = FightAnimLayout.Read(s, "bg.", at, 0);
            ti.Top = FightAnimLayout.Read(s, "top.", at, 0);
            FightIni.ReadInt(s, "framespercount", ref ti.FramesPerCount);
            return ti;
        }
    }

    /// <summary>Shake parameters of the combo counter / text (Go ComboShake, data only).</summary>
    public class ComboShakeDef {
        public int Time;
        public float Freq = 60f, Phase, Ampl, Scale = 1f, Dir, DirAdd, Decay = 1f;
    }

    /// <summary>Port of `FightScreenCombo` / `readFightScreenCombo` (Ikemen-version path: team1./team2.).</summary>
    public class ComboDef {
        public int[] Pos = new int[2];
        public float StartX;
        public SortedDictionary<int, FightText> Counter = new SortedDictionary<int, FightText>();
        public SortedDictionary<int, FightText> Text = new SortedDictionary<int, FightText>();
        public FightAnimLayout Bg, Top;
        public int DisplayTime = 90;
        public float ShowSpeed = 8f, HideSpeed = 4f;
        public string Separator = "";
        public int Places;
        public bool AutoAlign = true;
        public ComboShakeDef CounterShake = new ComboShakeDef();
        public ComboShakeDef TextShake = new ComboShakeDef();

        public static ComboDef Read(MugenDef.Section s, string pre, AirFile at, int side, int localcoordX) {
            var co = new ComboDef();
            FightIni.ReadInts(s, pre + "pos", co.Pos);
            FightIni.ReadFloat(s, pre + "start.x", ref co.StartX);
            if (side == 1) {
                if (pre == "team2.") {
                    float startX = co.StartX;
                    co.StartX = (float)localcoordX - startX;
                    if (co.StartX >= 0) co.StartX = Math.Min((float)co.Pos[0] - startX, 0f);
                } else {
                    co.Pos[0] = localcoordX - co.Pos[0];
                }
            }
            int align = 0;
            if (pre.Length == 0) align = side == 0 ? 1 : -1;

            co.Counter[0] = FightText.Read(s, pre + "counter.", "%i", 2, align);
            foreach (int k in FightIni.NumberedKeys(s, pre, "counter"))
                co.Counter[k] = FightText.Read(s, pre + "counter" + k + ".", "%i", 2, align);

            bool old = false;
            if (FightIni.ReadBool(s, pre + "counter.shake", ref old) && old) {
                co.CounterShake.Time = 8;
                co.CounterShake.Phase = 90f;
                co.CounterShake.Scale = 1.35f;
            }
            FightIni.ReadInt(s, pre + "counter.time", ref co.CounterShake.Time);
            float mult = 0f;
            if (FightIni.ReadFloat(s, pre + "counter.mult", ref mult))
                co.CounterShake.Scale = 1f + (float)co.CounterShake.Time * mult;
            ReadShake(s, pre + "counter.shake.", co.CounterShake);

            co.Text[0] = FightText.Read(s, pre + "text.", "", 2, align);
            foreach (int k in FightIni.NumberedKeys(s, pre, "text"))
                co.Text[k] = FightText.Read(s, pre + "text" + k + ".", "", 2, align);
            ReadShake(s, pre + "text.shake.", co.TextShake);

            co.Bg = FightAnimLayout.Read(s, pre + "bg0.", at, 2);
            co.Top = FightAnimLayout.Read(s, pre + "top.", at, 2);
            FightIni.ReadInt(s, pre + "displaytime", ref co.DisplayTime);
            FightIni.ReadFloat(s, pre + "showspeed", ref co.ShowSpeed);
            co.ShowSpeed = Math.Max(1f, co.ShowSpeed);
            FightIni.ReadFloat(s, pre + "hidespeed", ref co.HideSpeed);
            co.Separator = FightIni.GetText(s, "format.decimal.separator");
            FightIni.ReadInt(s, "format.decimal.places", ref co.Places);
            FightIni.ReadBool(s, pre + "autoalign", ref co.AutoAlign);
            return co;
        }

        static void ReadShake(MugenDef.Section s, string pre, ComboShakeDef sh) {
            FightIni.ReadInt(s, pre + "time", ref sh.Time);
            // Go calls setDefaultPhase() when freq is given; the phase is a runtime concern and
            // is only recorded here when written explicitly.
            FightIni.ReadFloat(s, pre + "freq", ref sh.Freq);
            FightIni.ReadFloat(s, pre + "phase", ref sh.Phase);
            FightIni.ReadFloat(s, pre + "ampl", ref sh.Ampl);
            FightIni.ReadFloat(s, pre + "scale", ref sh.Scale);
            FightIni.ReadFloat(s, pre + "dir", ref sh.Dir);
            FightIni.ReadFloat(s, pre + "diradd", ref sh.DirAdd);
            FightIni.ReadFloat(s, pre + "decay", ref sh.Decay);
        }
    }

    /// <summary>One win / ai.win / ai.lose announcement per side (Go ResultAnnouncement).</summary>
    public class ResultAnnouncementDef {
        public FightElement[] Text = new FightElement[2];
        public FightAnimLayout[] Top = new FightAnimLayout[2];
        /// <summary>[side][0..31]; entries are null when the bg is not defined.</summary>
        public FightAnimLayout[][] Bg = new FightAnimLayout[][] { new FightAnimLayout[32], new FightAnimLayout[32] };
    }

    /// <summary>Round start/end fade (Go readLbFade, data only).</summary>
    public class FightFadeDef {
        public int Time;
        public int[] Col = new int[3];
        public int[] Snd = new int[] { -1, 0 };
        public int AnimNo = -1;

        public static FightFadeDef Read(MugenDef.Section s, string pre, AirFile at) {
            var f = new FightFadeDef();
            FightIni.ReadInt(s, pre + "time", ref f.Time);
            FightIni.ReadInts(s, pre + "col", f.Col);
            FightIni.ReadInts(s, pre + "snd", f.Snd);
            int anim = -1;
            FightIni.ReadInt(s, pre + "anim", ref anim);
            if (at != null && at.Get(anim) != null) f.AnimNo = anim;
            return f;
        }
    }

    /// <summary>Port of `FightScreenRound` / `readFightScreenRound` (unprefixed section).</summary>
    public class RoundDef {
        public int[] Pos = new int[2];
        public int StartWaitTime = 30;
        public int RoundTime, RoundSndTime;
        /// <summary>round1. .. round9.</summary>
        public FightElement[] Round = new FightElement[9];
        public FightElement RoundDefault, RoundSingle, RoundFinal;
        public FightAnimLayout RoundDefaultTop, RoundSingleTop, RoundFinalTop;
        public FightAnimLayout[] RoundDefaultBg = new FightAnimLayout[32];
        public FightAnimLayout[] RoundSingleBg = new FightAnimLayout[32];
        public FightAnimLayout[] RoundFinalBg = new FightAnimLayout[32];
        public int FightTime, FightSndTime;
        public FightElement Fight;
        public FightAnimLayout FightTop;
        public FightAnimLayout[] FightBg = new FightAnimLayout[32];
        public int CtrlTime = 30;
        public int KoTime, KoSndTime, DkoTime, DkoSndTime, ToTime, ToSndTime;
        public bool DkoShowDraw;
        public FightElement Ko, Dko, To;
        public FightAnimLayout KoTop, DkoTop, ToTop;
        public FightAnimLayout[] KoBg = new FightAnimLayout[32];
        public FightAnimLayout[] DkoBg = new FightAnimLayout[32];
        public FightAnimLayout[] ToBg = new FightAnimLayout[32];
        public int SlowTime = 60, SlowFadeTime = 45;
        public float SlowSpeed = 0.25f;
        public int OverWaitTime = 45, OverHitTime = 10, OverWinTime = 45, OverForceWinTime = 900, OverTime = 210;
        public int WinTime, WinSndTime;
        /// <summary>[0] = win., [1] = win2., [2] = win3., [3] = win4.</summary>
        public ResultAnnouncementDef[] Win = new ResultAnnouncementDef[4];
        public ResultAnnouncementDef[] AiWin = new ResultAnnouncementDef[4];
        public ResultAnnouncementDef[] AiLose = new ResultAnnouncementDef[4];
        public bool[][] AiWinExists = new bool[][] { new bool[4], new bool[4] };
        public bool[][] AiLoseExists = new bool[][] { new bool[4], new bool[4] };
        public FightElement Draw;
        public FightAnimLayout DrawTop;
        public FightAnimLayout[] DrawBg = new FightAnimLayout[32];
        /// <summary>Win type banners: [type] for p1, [type + NumTypes] for p2.</summary>
        public FightBgTextSnd[] WinType = new FightBgTextSnd[(int)FightWinType.NumTypes * 2];
        public FightFadeDef FadeIn, FadeOut;
        public int ShutterTime = 15;
        public int[] ShutterCol = new int[3];
        public int CallFightTime = 60;
        public int ClutchThreshold = 10;

        static readonly string[] WinTypePrefixes = {
            "n.", "s.", "h.", "c.", "t.", "throw.", "suicide.", "teammate.", "perfect.", "clutch."
        };

        static void ReadBgArray(MugenDef.Section s, string pre, AirFile at, int ln, FightAnimLayout[] arr) {
            for (int i = 0; i < arr.Length; i++) arr[i] = FightAnimLayout.Read(s, pre + "bg" + i + ".", at, ln);
        }

        static bool SectionExists(MugenDef.Section s, string pre) {
            return s.Has(pre + "text") || s.Has(pre + "spr") || s.Has(pre + "anim");
        }

        public static RoundDef Read(MugenDef.Section s, AirFile at) {
            var ro = new RoundDef();
            FightIni.ReadInts(s, "pos", ro.Pos);
            FightIni.ReadInt(s, "start.waittime", ref ro.StartWaitTime);
            if (ro.StartWaitTime < 1) ro.StartWaitTime = 1;
            FightIni.ReadInt(s, "round.time", ref ro.RoundTime);
            ro.RoundSndTime = ro.RoundTime;
            FightIni.ReadInt(s, "round.sndtime", ref ro.RoundSndTime);
            for (int i = 0; i < ro.Round.Length; i++)
                ro.Round[i] = FightElement.Read(s, "round" + (i + 1) + ".", at, 2);
            ro.RoundDefault = FightElement.Read(s, "round.default.", at, 2);
            ro.RoundDefaultTop = FightAnimLayout.Read(s, "round.default.top.", at, 2);
            ReadBgArray(s, "round.default.", at, 2, ro.RoundDefaultBg);
            ro.RoundSingle = FightElement.Read(s, "round.single.", at, 2);
            ro.RoundSingleTop = FightAnimLayout.Read(s, "round.single.top.", at, 2);
            ReadBgArray(s, "round.single.", at, 2, ro.RoundSingleBg);
            ro.RoundFinal = FightElement.Read(s, "round.final.", at, 2);
            ro.RoundFinalTop = FightAnimLayout.Read(s, "round.final.top.", at, 2);
            ReadBgArray(s, "round.final.", at, 2, ro.RoundFinalBg);

            FightIni.ReadInt(s, "fight.time", ref ro.FightTime);
            ro.FightSndTime = ro.FightTime;
            FightIni.ReadInt(s, "fight.sndtime", ref ro.FightSndTime);
            ro.Fight = FightElement.Read(s, "fight.", at, 2);
            ro.FightTop = FightAnimLayout.Read(s, "fight.top.", at, 2);
            ReadBgArray(s, "fight.", at, 2, ro.FightBg);
            FightIni.ReadInt(s, "ctrl.time", ref ro.CtrlTime);
            if (ro.CtrlTime < 1) ro.CtrlTime = 1;

            FightIni.ReadInt(s, "ko.time", ref ro.KoTime);
            ro.KoSndTime = ro.KoTime;
            FightIni.ReadInt(s, "ko.sndtime", ref ro.KoSndTime);
            ro.Ko = FightElement.Read(s, "ko.", at, 1);
            ro.KoTop = FightAnimLayout.Read(s, "ko.top.", at, 1);
            ReadBgArray(s, "ko.", at, 2, ro.KoBg);
            ro.DkoTime = ro.KoTime; ro.DkoSndTime = ro.KoSndTime;
            ro.ToTime = ro.KoTime; ro.ToSndTime = ro.KoSndTime;
            FightIni.ReadInt(s, "dko.time", ref ro.DkoTime);
            FightIni.ReadInt(s, "dko.sndtime", ref ro.DkoSndTime);
            FightIni.ReadBool(s, "dko.showdraw", ref ro.DkoShowDraw);
            ro.Dko = FightElement.Read(s, "dko.", at, 1);
            ro.DkoTop = FightAnimLayout.Read(s, "dko.top.", at, 1);
            ReadBgArray(s, "dko.", at, 2, ro.DkoBg);
            FightIni.ReadInt(s, "to.time", ref ro.ToTime);
            FightIni.ReadInt(s, "to.sndtime", ref ro.ToSndTime);
            ro.To = FightElement.Read(s, "to.", at, 1);
            ro.ToTop = FightAnimLayout.Read(s, "to.top.", at, 1);
            ReadBgArray(s, "to.", at, 2, ro.ToBg);

            FightIni.ReadInt(s, "slow.time", ref ro.SlowTime);
            ro.SlowFadeTime = (int)((float)ro.SlowTime * 0.75f);
            FightIni.ReadInt(s, "slow.fadetime", ref ro.SlowFadeTime);
            if (ro.SlowFadeTime > ro.SlowTime) ro.SlowFadeTime = ro.SlowTime;
            FightIni.ReadFloat(s, "slow.speed", ref ro.SlowSpeed);
            ro.SlowSpeed = Math.Min(1f, Math.Max(0.01f, ro.SlowSpeed));
            FightIni.ReadInt(s, "over.hittime", ref ro.OverHitTime);
            if (ro.OverHitTime < 1) ro.OverHitTime = 1;
            FightIni.ReadInt(s, "over.waittime", ref ro.OverWaitTime);
            if (ro.OverWaitTime < 1) ro.OverWaitTime = 1;
            FightIni.ReadInt(s, "over.wintime", ref ro.OverWinTime);
            if (ro.OverWinTime < 1) ro.OverWinTime = 1;
            FightIni.ReadInt(s, "over.forcewintime", ref ro.OverForceWinTime);
            if (ro.OverForceWinTime < 1) ro.OverForceWinTime = 1;
            FightIni.ReadInt(s, "over.time", ref ro.OverTime);
            if (ro.OverTime < 1) ro.OverTime = 1;
            FightIni.ReadInt(s, "win.time", ref ro.WinTime);
            ro.WinSndTime = ro.WinTime;
            FightIni.ReadInt(s, "win.sndtime", ref ro.WinSndTime);

            for (int i = 0; i < 4; i++) {
                ro.Win[i] = new ResultAnnouncementDef();
                ro.AiWin[i] = new ResultAnnouncementDef();
                ro.AiLose[i] = new ResultAnnouncementDef();
            }
            string[] suffixes = { "", "2", "3", "4" };
            for (int side = 0; side < 2; side++) {
                string p = "p" + (side + 1) + ".";
                ReadResults(s, at, side, p, "win", ro.Win, null, suffixes);
                ReadResults(s, at, side, p, "ai.win", ro.AiWin, ro.AiWinExists[side], suffixes);
                ReadResults(s, at, side, p, "ai.lose", ro.AiLose, ro.AiLoseExists[side], suffixes);
            }

            ro.Draw = FightElement.Read(s, "draw.", at, 1);
            ro.DrawTop = FightAnimLayout.Read(s, "draw.top.", at, 1);
            ReadBgArray(s, "draw.", at, 1, ro.DrawBg);
            int nt = (int)FightWinType.NumTypes;
            for (int i = 0; i < nt; i++) {
                ro.WinType[i] = FightBgTextSnd.Read(s, "p1." + WinTypePrefixes[i], at, 0);
                ro.WinType[i + nt] = FightBgTextSnd.Read(s, "p2." + WinTypePrefixes[i], at, 0);
            }
            ro.FadeIn = FightFadeDef.Read(s, "fadein.", at);
            ro.FadeOut = FightFadeDef.Read(s, "fadeout.", at);
            FightIni.ReadInt(s, "shutter.time", ref ro.ShutterTime);
            FightIni.ReadInt(s, "clutch.threshold", ref ro.ClutchThreshold);
            FightIni.ReadInts(s, "shutter.col", ro.ShutterCol);
            FightIni.ReadInt(s, "callfight.time", ref ro.CallFightTime);
            return ro;
        }

        // The win / ai.win / ai.lose loop of readFightScreenRound. Entries 3 and 4 that are not
        // defined copy entry 2 (Go copies the struct value; here the reference is shared).
        static void ReadResults(MugenDef.Section s, AirFile at, int side, string p, string name,
                                ResultAnnouncementDef[] data, bool[] exists, string[] suffixes) {
            for (int idx = 0; idx < suffixes.Length; idx++) {
                string pre = name + suffixes[idx] + ".";
                string pPre = p + name + suffixes[idx] + ".";
                bool ex = SectionExists(s, pPre) || SectionExists(s, pre);
                if (idx >= 2 && !ex) {
                    data[idx].Text[side] = data[1].Text[side];
                    data[idx].Top[side] = data[1].Top[side];
                    data[idx].Bg[side] = data[1].Bg[side];
                    if (exists != null) exists[idx] = exists[1];
                    continue;
                }
                if (ex && exists != null) exists[idx] = true;
                bool usePlayer = SectionExists(s, pPre);
                data[idx].Text[side] = FightElement.Read(s, usePlayer ? pPre : pre, at, 1);
                bool usePlayerTop = SectionExists(s, pPre + "top.");
                data[idx].Top[side] = FightAnimLayout.Read(s, usePlayerTop ? pPre + "top." : pre + "top.", at, 1);
                var bgs = new FightAnimLayout[32];
                for (int j = 0; j < 32; j++) {
                    string pBg = pPre + "bg" + j + ".";
                    string bg = pre + "bg" + j + ".";
                    bool pEx = SectionExists(s, pBg);
                    if (pEx || SectionExists(s, bg))
                        bgs[j] = FightAnimLayout.Read(s, pEx ? pBg : bg, at, 1);
                }
                data[idx].Bg[side] = bgs;
            }
        }
    }

    /// <summary>[Files] of fight.def.</summary>
    public class FightFiles {
        public string Sff = "", Snd = "";
        public string FightFxSff = "", FightFxAir = "", CommonSnd = "";
        /// <summary>fontN = path, keyed by N (Go parseFonts; no upper limit).</summary>
        public SortedDictionary<int, string> Fonts = new SortedDictionary<int, string>();
        /// <summary>fontN.height, when given.</summary>
        public SortedDictionary<int, int> FontHeights = new SortedDictionary<int, int>();
        /// <summary>fx1, fx2 ... extra common effect .def files.</summary>
        public List<string> Fx = new List<string>();
    }

    /// <summary>The parsed fight screen definition (fight.def).</summary>
    public class FightDef {
        public MugenDef Def;
        public IResourceSource Source;
        public string MotifName = "", Author = "";
        public int LocalcoordX = 320, LocalcoordY = 240;
        public float FightFxScale = 1f;
        public FightFiles Files = new FightFiles();
        /// <summary>The [Begin Action n] animations embedded in fight.def.</summary>
        public AirFile Animations;
        public LifeBarDef[] LifeBar = new LifeBarDef[2];
        public PowerBarDef[] PowerBar = new PowerBarDef[2];
        public FaceDef[] Face = new FaceDef[2];
        public NameDef[] Names = new NameDef[2];
        public WinIconDef[] WinIcon = new WinIconDef[2];
        public ComboDef[] Combo = new ComboDef[2];
        public TimeDef Time;
        public RoundDef Round;

        /// <summary>Number of [sections] in the file, including [Begin Action n].</summary>
        public int SectionCount => Def != null ? Def.Sections.Count : 0;

        public static FightDef Load(byte[] def, IResourceSource res) {
            return Parse(MugenDef.DecodeText(def), res);
        }

        public static FightDef Parse(string text, IResourceSource res) {
            var fd = new FightDef();
            fd.Source = res;
            fd.Def = MugenDef.Parse(text);
            fd.Animations = AirFile.Parse(text);
            AirFile at = fd.Animations;

            // Like Go, every section kind is read once: the first match wins.
            var info = fd.Def["info"];
            if (info != null) {
                fd.MotifName = FightIni.GetText(info, "name");
                fd.Author = FightIni.GetText(info, "author");
                int[] lc = new int[] { fd.LocalcoordX, fd.LocalcoordY };
                FightIni.ReadInts(info, "localcoord", lc);
                fd.LocalcoordX = lc[0]; fd.LocalcoordY = lc[1];
            }
            var files = fd.Def["files"];
            if (files != null) ReadFiles(files, fd.Files);
            var ffx = fd.Def["fightfx"];
            if (ffx != null) FightIni.ReadFloat(ffx, "scale", ref fd.FightFxScale);

            var s = fd.Def["lifebar"];
            if (s != null) {
                fd.LifeBar[0] = LifeBarDef.Read(s, "p1.", at);
                fd.LifeBar[1] = LifeBarDef.Read(s, "p2.", at);
            }
            s = fd.Def["powerbar"];
            if (s != null) {
                fd.PowerBar[0] = PowerBarDef.Read(s, "p1.", at);
                fd.PowerBar[1] = PowerBarDef.Read(s, "p2.", at);
            }
            s = fd.Def["face"];
            if (s != null) {
                fd.Face[0] = FaceDef.Read(s, "p1.", at);
                fd.Face[1] = FaceDef.Read(s, "p2.", at);
            }
            s = fd.Def["name"];
            if (s != null) {
                fd.Names[0] = NameDef.Read(s, "p1.", at);
                fd.Names[1] = NameDef.Read(s, "p2.", at);
            }
            s = fd.Def["winicon"];
            if (s != null) {
                fd.WinIcon[0] = WinIconDef.Read(s, "p1.", at);
                fd.WinIcon[1] = WinIconDef.Read(s, "p2.", at);
            }
            s = fd.Def["time"];
            if (s != null) fd.Time = TimeDef.Read(s, at);
            s = fd.Def["combo"];
            if (s != null) {
                fd.Combo[0] = ComboDef.Read(s, "team1.", at, 0, fd.LocalcoordX);
                fd.Combo[1] = ComboDef.Read(s, "team2.", at, 1, fd.LocalcoordX);
            }
            s = fd.Def["round"];
            if (s != null) fd.Round = RoundDef.Read(s, at);
            return fd;
        }

        static void ReadFiles(MugenDef.Section s, FightFiles f) {
            f.Sff = s.Get("sff", "");
            f.Snd = s.Get("snd", "");
            f.FightFxSff = s.Get("fightfx.sff", "");
            f.FightFxAir = s.Get("fightfx.air", "");
            f.CommonSnd = s.Get("common.snd", "");
            for (int i = 0; i < s.Lines.Count; i++) {
                string k = s.Lines[i].Key;
                string v = s.Lines[i].Value;
                if (k.StartsWith("fx", StringComparison.Ordinal) && k.Length > 2 && MugenDef.IsNumeric(k.Substring(2))) {
                    if (v.Length > 0 && !f.Fx.Contains(v)) f.Fx.Add(v);
                    continue;
                }
                if (!k.StartsWith("font", StringComparison.Ordinal)) continue;
                string rest = k.Substring(4);
                int j = 0;
                while (j < rest.Length && rest[j] >= '0' && rest[j] <= '9') j++;
                if (j == 0) continue;
                int idx = MugenDef.Atoi(rest.Substring(0, j));
                string tail = rest.Substring(j);
                if (tail.Length == 0) {
                    if (!f.Fonts.ContainsKey(idx)) f.Fonts[idx] = v;
                } else if (tail == ".height") {
                    if (!f.FontHeights.ContainsKey(idx)) f.FontHeights[idx] = MugenDef.Atoi(v);
                }
            }
        }

        /// <summary>Bytes of the fight screen sprite file ([Files] sff) through the resource source.</summary>
        public byte[] ReadSffBytes() { return ReadFile(Files.Sff); }
        public byte[] ReadSndBytes() { return ReadFile(Files.Snd); }

        byte[] ReadFile(string name) {
            if (Source == null || string.IsNullOrEmpty(name)) return null;
            return Source.Read(name);
        }
    }

    /// <summary>
    /// Runtime model of one side of the fight screen: the values the bars show plus the life
    /// bar damage animation. The animation is a literal port of `LifeBar.step` (fightscreen.go),
    /// with the reference bar equal to the bar itself (single play):
    ///   - TopLife ("front" bar) halves its distance to the real life every tick when life drops,
    ///     and snaps up immediately when life rises;
    ///   - MidLife ("mid" bar) holds while the player is in get-hit (mid.freeze = 1), then waits
    ///     mid.delay ticks before draining towards the life by 1/mid.steps of the gap per tick.
    /// </summary>
    public class LifeBarState {
        public int Life, MaxLife, Power, MaxPower, RoundsWon, Time;

        // LifeBar.step state (fractions of MaxLife). Go newLifeBar defaults.
        public float TopLife;
        public float OldLife = 1f;
        public float MidLife = 1f;
        public float MidLifeMin = 1f;
        public int MidLifeTime;
        bool lastGetHit;

        // Section parameters (Go defaults).
        public bool MidFreeze = true;
        public int MidDelay = 30;
        public float MidMult = 1f;
        public float MidSteps = 8f;
        /// <summary>Go: len(lb.mid.anim.frames) > 0.</summary>
        public bool HasMid = true;

        public LifeBarState() { }

        public LifeBarState(LifeBarDef def, int maxLife, int maxPower) {
            MaxLife = maxLife; Life = maxLife;
            MaxPower = maxPower;
            if (def != null) {
                MidFreeze = def.MidFreeze;
                MidDelay = def.MidDelay;
                MidMult = def.MidMult;
                MidSteps = Math.Max(1f, def.MidSteps);
                HasMid = def.Mid != null && def.Mid.HasFrames;
            }
        }

        public float LifeFraction => MaxLife > 0 ? (float)Life / (float)MaxLife : 0f;
        public float PowerFraction => MaxPower > 0 ? (float)Power / (float)MaxPower : 0f;

        /// <summary>Advance one tick. getHit = (receivedHits != 0 || moveType == H) && !over_ko.</summary>
        public void Step(bool getHit) {
            float life = LifeFraction;
            if (TopLife > life) TopLife += (life - TopLife) / 2f;
            else TopLife = life;

            if (!MidFreeze && getHit && !lastGetHit && HasMid) {
                MidLifeTime = MidDelay;
                MidLife = OldLife;
                MidLifeMin = OldLife;
            }
            lastGetHit = getHit;
            if (MidFreeze && getHit && HasMid) {
                if (MidLifeTime < MidDelay) {
                    MidLifeTime = MidDelay;
                    MidLife = OldLife;
                    MidLifeMin = OldLife;
                }
            } else {
                if (MidLifeTime > 0) MidLifeTime--;
                if (HasMid && MidLifeTime <= 0 && life < MidLifeMin) {
                    MidLifeMin += (life - MidLifeMin) * (1f / (12f - (life - MidLifeMin) * 144f)) * MidMult;
                    if (MidLifeMin < life) MidLifeMin = life;
                } else {
                    MidLifeMin = life;
                }
                if ((!HasMid || MidLifeTime <= 0) && MidLife > MidLifeMin)
                    MidLife += (MidLifeMin - MidLife) / MidSteps;
                OldLife = life;
            }

            float mlmin = Math.Max(MidLifeMin, life);
            if (MidLife < mlmin) MidLife += (mlmin - MidLife) / 2f;
        }
    }
}
