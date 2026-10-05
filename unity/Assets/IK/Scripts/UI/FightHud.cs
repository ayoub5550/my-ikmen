using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// A screenpack bitmap font: the `[FNT v2]` `.def` of `[Files] fontN` plus the SFF that
    /// holds one sprite per character. Port of `loadFntV2` / `LoadFntSff` / `CharWidth` /
    /// `TextWidth` (engine/ikemen-go/src/font.go).
    ///
    /// Only `Type = bitmap` is supported: the glyph of character `c` is the sprite
    /// `(bank, c)` of the font SFF (group 0 for the `BankType = palette` fonts this
    /// screenpack ships), and its MUGEN axis is the pen position.
    /// </summary>
    public class HudFont : IDisposable {
        /// <summary>`[Def] Size`: the width used for a space and the height of a text line.</summary>
        public int SizeX, SizeY;
        /// <summary>`[Def] Spacing`: extra pixels between glyphs and between lines.</summary>
        public int SpacingX, SpacingY;
        /// <summary>`[Def] Offset`: drawing offset applied to the whole string.</summary>
        public int OffsetX, OffsetY;
        /// <summary>`[Def] Type`, lowercase. Anything but "bitmap" is unsupported here.</summary>
        public string Type = "bitmap";
        /// <summary>`[Def] BankType`, lowercase. "sprite" picks the glyph group by bank.</summary>
        public string BankType = "palette";
        /// <summary>The glyph sprites.</summary>
        public SffFile Sff;

        /// <summary>
        /// Private sprite cache. <see cref="MugenAssetCache"/> keys its sprites by the sprite's
        /// index inside its own SFF, so glyphs of two different font SFFs (or of a font SFF and
        /// fight.sff) would collide in one shared cache. Each font therefore gets its own.
        /// </summary>
        public readonly MugenAssetCache Cache = new MugenAssetCache();

        /// <summary>True when the font has glyphs to draw with.</summary>
        public bool Ready => Sff != null && Type == "bitmap";

        /// <summary>
        /// Reads `path` (a `[Files] fontN` entry) through <paramref name="src"/> and the SFF it
        /// names, resolved next to the font def exactly as Ikemen's SearchFile does for the
        /// shipped motif layout. Returns null when either file is missing.
        /// </summary>
        public static HudFont Load(IResourceSource src, string path) {
            if (src == null || string.IsNullOrEmpty(path)) return null;
            var defBytes = src.Read(path);
            if (defBytes == null) return null;
            var def = MugenDef.Parse(defBytes);
            var s = def["def"];
            if (s == null) return null;

            var f = new HudFont();
            f.Type = s.Get("type", "bitmap").Trim().ToLowerInvariant();
            if (s.Has("banktype")) f.BankType = s.Get("banktype", "").Trim().ToLowerInvariant();
            var size = new int[2];
            FightIni.ReadInts(s, "size", size);
            f.SizeX = size[0]; f.SizeY = size[1];
            var spacing = new int[2];
            FightIni.ReadInts(s, "spacing", spacing);
            f.SpacingX = spacing[0]; f.SpacingY = spacing[1];
            var offset = new int[2];
            FightIni.ReadInts(s, "offset", offset);
            f.OffsetX = offset[0]; f.OffsetY = offset[1];

            var file = s.Get("file", "").Trim();
            if (f.Type != "bitmap" || file.Length == 0) return f;        // truetype: unsupported
            var sffBytes = src.Read(SiblingPath(path, file));
            if (sffBytes == null) sffBytes = src.Read(file);             // flattened sources
            if (sffBytes == null) return f;
            // isCharacter = false: the 0,0 palette quirk only applies to character SFFs.
            f.Sff = SffFile.Load(sffBytes, false);
            return f;
        }

        /// <summary>`file` resolved in the directory of `defPath` ("a/b/x.def" + "y.sff" -> "a/b/y.sff").</summary>
        public static string SiblingPath(string defPath, string file) {
            var p = defPath.Replace('\\', '/');
            int slash = p.LastIndexOf('/');
            return slash < 0 ? file : p.Substring(0, slash + 1) + file;
        }

        /// <summary>The sprite of character <paramref name="c"/>, or null when the font has none.</summary>
        public SffSprite Glyph(char c, int bank = 0) {
            if (Sff == null) return null;
            int group = BankType == "sprite" ? bank : 0;
            return Sff.Get(group, c);
        }

        /// <summary>Go `Fnt.CharWidth`: a space is `Size[0]` wide, a missing glyph is 0 wide.</summary>
        public int CharWidth(char c, int bank = 0) {
            if (c == ' ') return SizeX;
            var g = Glyph(c, bank);
            return g != null ? g.Width : 0;
        }

        /// <summary>
        /// Go `Fnt.TextWidth`: every glyph contributes its width, and every glyph but the last
        /// also contributes the font spacing — unless width + spacing would be negative, which
        /// MUGEN skips entirely.
        /// </summary>
        public int TextWidth(string txt, int bank = 0, int spacingXAdd = 0) {
            if (string.IsNullOrEmpty(txt)) return 0;
            int w = 0;
            for (int i = 0; i < txt.Length; i++) {
                int cw = CharWidth(txt[i], bank);
                if (cw + SpacingX + spacingXAdd <= 0) continue;
                w += cw;
                if (i < txt.Length - 1) w += SpacingX + spacingXAdd;
            }
            return w;
        }

        public void Dispose() { Cache.Dispose(); }
    }

    /// <summary>
    /// The real MUGEN / Ikemen fight HUD: the life and power bars, the round timer, the round
    /// announcements, the win icons and the fighters' names, drawn from the screenpack's own
    /// artwork (`data/fight.def` + `data/fight.sff` + its fonts) with uGUI Images.
    ///
    /// Everything is positioned from the numbers in fight.def; nothing here invents a
    /// coordinate. The geometry is a port of engine/ikemen-go/src/fightscreen.go
    /// (`LifeBar.draw`, `PowerBar.draw`, `FightScreenTime.draw`, `FightScreenWinIcon.draw`,
    /// `FightScreenName.draw`, `FightScreenRound.draw`, `calcBarFillRect`) together with
    /// `Layout.DrawAnim` / `Layout.DrawText` (common.go) and `Fnt.DrawText` (font.go).
    ///
    /// Coordinate space: fight.def's `[Info] localcoord` (1280x720 for the shipped motif).
    /// The HUD root is a <paramref name="scale"/>-times-localcoord rect centred in the parent,
    /// with the top-left of the local screen as the origin and y growing downwards, which is
    /// how the engine places lifebar elements (`pos + offset`, plus the screen centring offset
    /// that the root rect supplies here).
    ///
    /// This class is a plain C# object, not a MonoBehaviour: the owner builds it, calls
    /// <see cref="Draw"/> once per drawn frame and <see cref="Dispose"/> at the end.
    /// </summary>
    public class FightHud : IDisposable {
        // ------------------------------------------------------------------ nested types

        /// <summary>
        /// A private playhead over one of fight.def's actions. The actions in
        /// <see cref="FightDef.Animations"/> are shared between every element that names them,
        /// so the HUD must not tick them in place; this cursor loads its own state into the
        /// shared action, reuses the ported <see cref="MugenAnimation.Tick"/>, and reads the
        /// state back out.
        /// </summary>
        class AnimCursor {
            /// <summary>Catch-up limit so a long-idle cursor cannot stall a frame.</summary>
            const int MaxCatchUp = 1200;

            readonly MugenAnimation anim;
            int element, elementTime, time, ticks;
            bool loopEnd;

            public AnimFrame Frame { get; private set; }
            public bool Ended => loopEnd;

            public AnimCursor(MugenAnimation a) { anim = a; Reset(); }

            public void Reset() {
                element = 0; elementTime = 0; time = 0; ticks = 0; loopEnd = false;
                Frame = anim != null && anim.Frames.Count > 0 ? anim.Frames[0] : null;
            }

            /// <summary>Puts the playhead exactly <paramref name="t"/> ticks into the action.</summary>
            public void SeekTo(int t) {
                if (anim == null) return;
                if (t < ticks) Reset();
                int guard = 0;
                while (ticks < t && guard++ < MaxCatchUp) Advance();
                ticks = t;
            }

            void Advance() {
                anim.CurrentElement = element;
                anim.ElementTime = elementTime;
                anim.Time = time;
                anim.LoopEnd = loopEnd;
                anim.Tick();
                element = anim.CurrentElement;
                elementTime = anim.ElementTime;
                time = anim.Time;
                loopEnd = anim.LoopEnd;
                Frame = anim.CurrentFrame;
                ticks++;
            }
        }

        /// <summary>One sprite-or-animation element of fight.def, drawn as one uGUI Image.</summary>
        class Element {
            public Image Img;
            public RectTransform Rt;
            /// <summary>Clipping parent (a RectMask2D) for the bar fills; null when unclipped.</summary>
            public RectTransform Clip;
            public AnimCursor Cursor;
            /// <summary>The action this cursor was built for, so it is rebuilt when the key changes.</summary>
            public int CursorAnim = int.MinValue;
        }

        /// <summary>A string drawn with a screenpack font: one pooled Image per glyph.</summary>
        class TextNode {
            public RectTransform Rt;
            public readonly List<Image> Glyphs = new List<Image>();
        }

        /// <summary>The Images of one life or power bar.</summary>
        class BarNodes {
            public Element Bg0, Bg1, Bg2, Mid, Front, Top, Warn;
        }

        /// <summary>One round announcement: its bg layers, its main element and its top layer.</summary>
        class Announcement {
            public Element[] Bg = new Element[0];
            public Element Main;
            public Element Top;
            public TextNode Text;
        }

        // ------------------------------------------------------------------ state

        readonly FightDef fight;
        readonly SffFile sff;
        readonly MugenAssetCache cache;
        readonly float uiScale;
        readonly int localW, localH;
        /// <summary>Go `FightScreen.fnt_scale`: 0.5 for a `doubleres` motif, 1 otherwise.</summary>
        readonly float fntScale = 1f;

        RectTransform root;
        readonly RectTransform[] layers = new RectTransform[3];
        readonly Dictionary<int, HudFont> fonts = new Dictionary<int, HudFont>();

        readonly BarNodes[] life = { new BarNodes(), new BarNodes() };
        readonly BarNodes[] power = { new BarNodes(), new BarNodes() };
        readonly TextNode[] powerCounter = new TextNode[2];
        readonly TextNode[] nameText = new TextNode[2];
        readonly Element[][] winBg = new Element[2][];
        readonly Element[][] winIcon = new Element[2][];
        readonly Element[][] winTop = new Element[2][];
        Element timeBg, timeTop;
        TextNode timeCounter;
        Announcement roundAnn, fightAnn, koAnn, dkoAnn, toAnn;
        readonly Announcement[] winAnn = new Announcement[2];

        /// <summary>True once the HUD has artwork to draw; false when fight.def or its SFF is missing.</summary>
        public bool Ready { get; private set; }

        /// <summary>Why a part of the HUD could not be built (fonts, SFF). Null when all is well.</summary>
        public string LoadError { get; private set; }

        /// <summary>uGUI pixels per fight.def local unit.</summary>
        public float Scale => uiScale;

        /// <summary>The root rect of the HUD, sized <c>localcoord * scale</c>.</summary>
        public RectTransform Root => root;

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// Builds every HUD node under <paramref name="parent"/>. <paramref name="sprites"/> is
        /// the decoded `[Files] sff` of <paramref name="fight"/>, <paramref name="cache"/> turns
        /// its sprites into Unity sprites, and <paramref name="scale"/> is uGUI pixels per
        /// fight.def local unit (1 when the canvas reference matches `[Info] localcoord`).
        /// </summary>
        public FightHud(RectTransform parent, FightDef fight, SffFile sprites,
                        MugenAssetCache cache, float scale = 1f) {
            this.fight = fight;
            this.sff = sprites;
            this.cache = cache;
            this.uiScale = scale;
            localW = fight != null ? fight.LocalcoordX : 320;
            localH = fight != null ? fight.LocalcoordY : 240;
            if (fight != null) {
                // Go readFightScreen: doubleres halves the font scale, nothing else does.
                bool doubleRes = false;
                FightIni.ReadBool(fight.Def != null ? fight.Def["info"] : null, "doubleres", ref doubleRes);
                if (doubleRes) fntScale = 0.5f;
            }
            Ready = fight != null && sprites != null && parent != null;
            if (!Ready) {
                LoadError = parent == null ? "no parent" : fight == null ? "no fight.def" : "no fight.sff";
                return;
            }
            try {
                Build(parent);
            } catch (Exception e) {
                Ready = false;
                LoadError = e.Message;
            }
        }

        void Build(RectTransform parent) {
            root = UIKit.Rect(parent, "fighthud", new Vector2(0.5f, 0.5f), Vector2.zero,
                              new Vector2(localW * uiScale, localH * uiScale));
            // Layer containers in Go's draw order: layerno 0 first, 2 (the round call) on top.
            for (int i = 0; i < 3; i++)
                layers[i] = UIKit.Panel(root, "layer" + i, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // The Go draw order inside a layer: lifebars, powerbars, names, time, win icons,
            // and the round announcements last (FightScreen.draw, fightscreen.go:5631).
            for (int i = 0; i < 2; i++) BuildLifeBar(life[i], fight.LifeBar[i], "life" + i);
            for (int i = 0; i < 2; i++) BuildPowerBar(power[i], fight.PowerBar[i], "power" + i);
            for (int i = 0; i < 2; i++) {
                var pb = fight.PowerBar[i];
                if (pb != null && pb.Counter.ContainsKey(0) && FontFor(pb.Counter[0]) != null)
                    powerCounter[i] = NewText("powercounter" + i, pb.Counter[0].Layout.LayerNo);
                var nm = fight.Names[i];
                if (nm != null && nm.Name != null && FontFor(nm.Name) != null)
                    nameText[i] = NewText("name" + i, nm.Name.Layout.LayerNo);
            }
            BuildTime();
            for (int i = 0; i < 2; i++) BuildWinIcon(i);
            BuildRound();
        }

        void BuildLifeBar(BarNodes n, LifeBarDef lb, string name) {
            if (lb == null) return;
            n.Bg0 = NewElement(name + ".bg0", lb.Bg0, false);
            n.Bg1 = NewElement(name + ".bg1", lb.Bg1, false);
            n.Bg2 = NewElement(name + ".bg2", lb.Bg2, false);
            n.Mid = NewElement(name + ".mid", lb.Mid, true);
            // One Image serves every frontN variant: only its sprite and layout change per frame.
            n.Front = NewElement(name + ".front", AnyFront(lb), true);
            n.Top = NewElement(name + ".top", lb.Top, false);
            n.Warn = NewElement(name + ".warn", lb.Warn, false);
        }

        void BuildPowerBar(BarNodes n, PowerBarDef pb, string name) {
            if (pb == null) return;
            n.Bg0 = NewElement(name + ".bg0", pb.Bg0.ContainsKey(0) ? pb.Bg0[0] : null, false);
            n.Bg1 = NewElement(name + ".bg1", pb.Bg1, false);
            n.Bg2 = NewElement(name + ".bg2", pb.Bg2, false);
            n.Mid = NewElement(name + ".mid", pb.Mid, true);
            n.Front = NewElement(name + ".front", AnyFront(pb), true);
            n.Top = NewElement(name + ".top", pb.Top, false);
        }

        /// <summary>Any defined front variant, so the Image exists even when `front.` itself is empty.</summary>
        static FightAnimLayout AnyFront(LifeBarDef lb) {
            foreach (var kv in lb.Front) if (kv.Value != null && kv.Value.HasFrames) return kv.Value;
            return lb.FrontDefault;
        }

        static FightAnimLayout AnyFront(PowerBarDef pb) {
            foreach (var kv in pb.Front) if (kv.Value != null && kv.Value.HasFrames) return kv.Value;
            return pb.Front.ContainsKey(0) ? pb.Front[0] : null;
        }

        void BuildTime() {
            var ti = fight.Time;
            if (ti == null) return;
            timeBg = NewElement("time.bg", ti.Bg, false);
            timeTop = NewElement("time.top", ti.Top, false);
            if (ti.Counter.ContainsKey(0) && FontFor(ti.Counter[0]) != null)
                timeCounter = NewText("time.counter", ti.Counter[0].Layout.LayerNo);
        }

        void BuildWinIcon(int side) {
            var wi = fight.WinIcon[side];
            if (wi == null) return;
            int slots = Math.Max(0, wi.UseIconUpTo);
            winBg[side] = new Element[slots];
            winIcon[side] = new Element[slots];
            winTop[side] = new Element[slots];
            for (int i = 0; i < slots; i++) {
                winBg[side][i] = NewElement("winicon" + side + ".bg" + i, wi.Bg0, false);
                // Only the "normal win" icon is drawn: the engine does not record win types yet.
                winIcon[side][i] = NewElement("winicon" + side + ".icon" + i, wi.Icon[(int)FightWinType.Normal], false);
                winTop[side][i] = NewElement("winicon" + side + ".top" + i, wi.Top, false);
            }
        }

        void BuildRound() {
            var ro = fight.Round;
            if (ro == null) return;
            roundAnn = NewAnnouncement("round", ro.RoundDefault, ro.RoundDefaultTop, ro.RoundDefaultBg);
            fightAnn = NewAnnouncement("fight", ro.Fight, ro.FightTop, ro.FightBg);
            koAnn = NewAnnouncement("ko", ro.Ko, ro.KoTop, ro.KoBg);
            dkoAnn = NewAnnouncement("dko", ro.Dko, ro.DkoTop, ro.DkoBg);
            toAnn = NewAnnouncement("to", ro.To, ro.ToTop, ro.ToBg);
            // win. is per side: p1.win. and p2.win. have their own offsets and alignment.
            var win = ro.Win[0];
            for (int side = 0; side < 2; side++)
                winAnn[side] = NewAnnouncement("win" + side, win != null ? win.Text[side] : null,
                                               win != null ? win.Top[side] : null,
                                               win != null ? win.Bg[side] : null);
        }

        Announcement NewAnnouncement(string name, FightElement main, FightAnimLayout top, FightAnimLayout[] bg) {
            var a = new Announcement();
            if (bg != null) {
                var used = new List<Element>();
                // Go draws bg31..bg0, so the lowest index ends up on top: create in the same order.
                for (int i = bg.Length - 1; i >= 0; i--)
                    if (bg[i] != null && bg[i].HasFrames) used.Add(NewElement(name + ".bg" + i, bg[i], false));
                a.Bg = used.ToArray();
            }
            if (main != null) {
                a.Main = NewElement(name + ".main", main.Anim, false);
                if (FontFor(main.Text) != null) a.Text = NewText(name + ".text", main.Text.Layout.LayerNo);
            }
            a.Top = NewElement(name + ".top", top, false);
            return a;
        }

        /// <summary>
        /// Creates the Image (and, for a bar fill, the RectMask2D that clips it) of one element.
        /// Returns null when the def does not define the element, so Draw can skip it.
        /// </summary>
        Element NewElement(string name, FightAnimLayout al, bool clipped) {
            if (al == null || !al.HasFrames) return null;
            var parent = layers[Mathf.Clamp(al.Layout.LayerNo, 0, 2)];
            var e = new Element();
            if (clipped) {
                e.Clip = UIKit.Rect(parent, name + ".clip", new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
                e.Clip.pivot = new Vector2(0f, 1f);
                e.Clip.gameObject.AddComponent<RectMask2D>();
                parent = e.Clip;
            }
            e.Img = UIKit.Image(parent, name, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            e.Rt = e.Img.rectTransform;
            e.Img.enabled = false;
            return e;
        }

        TextNode NewText(string name, int layerNo) {
            var t = new TextNode();
            t.Rt = UIKit.Rect(layers[Mathf.Clamp(layerNo, 0, 2)], name, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            t.Rt.pivot = new Vector2(0f, 1f);
            return t;
        }

        /// <summary>The font an FSText names, loaded on first use. Null when it has none.</summary>
        HudFont FontFor(FightText t) {
            if (t == null || t.FontIndex < 0 || fight == null) return null;
            if (fonts.TryGetValue(t.FontIndex, out var cached)) return cached;
            HudFont f = null;
            string path;
            if (fight.Files.Fonts.TryGetValue(t.FontIndex, out path)) {
                try {
                    f = HudFont.Load(fight.Source, path);
                } catch (Exception e) {
                    LoadError = "font " + t.FontIndex + ": " + e.Message;
                }
                if (f != null && !f.Ready) {
                    LoadError = "font " + t.FontIndex + " (" + path + ") has no bitmap glyphs";
                    f = null;
                }
            }
            fonts[t.FontIndex] = f;
            return f;
        }

        // ------------------------------------------------------------------ pure layout maths

        /// <summary>Go `range_x != [2]int32{0,0}`: an all-zero range means "do not clip on this axis".</summary>
        public static bool RangeIsSet(int[] range) {
            return range != null && range.Length >= 2 && (range[0] != 0 || range[1] != 0);
        }

        /// <summary>
        /// Go `calcBarFillRect`: the length of a completely full bar, in local units.
        /// The range is inclusive on both ends, hence the +1.
        /// </summary>
        public static float BarFillLength(int[] range) {
            if (!RangeIsSet(range)) return 0f;
            int r0 = Math.Min(range[0], range[1]), r1 = Math.Max(range[0], range[1]);
            return r1 - r0 + 1;
        }

        /// <summary>Go `calcBarFillRect` size: the filled length for `fill` (0..1), in local units.</summary>
        public static float BarWidth(int[] range, float fill) {
            return BarFillLength(range) * fill;
        }

        /// <summary>
        /// Go `calcBarFillRect` start: the low edge of the filled part, in local units.
        /// A descending range (`range[0] &gt; range[1]`, as p1's lifebar has) anchors the fill at
        /// its high edge and grows towards the low one, so the start moves with the fill.
        /// </summary>
        public static float BarStart(int pos, int[] range, float fill) {
            if (!RangeIsSet(range)) return pos;
            bool descending = range[0] > range[1];
            int r0 = Math.Min(range[0], range[1]), r1 = Math.Max(range[0], range[1]);
            if (descending) return pos + r1 + 1 - BarWidth(range, fill);
            return pos + r0;
        }

        /// <summary>
        /// The uGUI anchored position of an element: `pos + layout.offset` in local units,
        /// with the top-left of the local screen as the origin and y growing downwards.
        /// </summary>
        public static Vector2 ElementPosition(int[] pos, FightLayout layout, float scale) {
            float x = (pos != null && pos.Length > 0 ? pos[0] : 0) + (layout != null ? layout.OffsetX : 0f);
            float y = (pos != null && pos.Length > 1 ? pos[1] : 0) + (layout != null ? layout.OffsetY : 0f);
            return new Vector2(x * scale, -y * scale);
        }

        /// <summary>
        /// Go `LifeBar.draw`: the `frontN` variant in use is the highest key N with
        /// `life &gt;= N/100`; key 0 is the plain `front.` element.
        /// </summary>
        public static float LifeFrontKey(ICollection<float> keys, float life) {
            float best = 0f;
            if (keys == null) return best;
            foreach (var k in keys) if (k > best && life >= k / 100f) best = k;
            return best;
        }

        /// <summary>
        /// Go `resolvePBKey`: the highest key not above the current power, or the `max` key
        /// (<see cref="PowerBarDef.MaxKey"/>) when the bar is at `powerMax`.
        /// </summary>
        public static int PowerBarKey(ICollection<int> keys, int power, int powerMax) {
            int best = 0;
            bool hasMax = false;
            if (keys == null) return best;
            foreach (var k in keys) {
                if (k == PowerBarDef.MaxKey) { hasMax = true; continue; }
                if (k > best && power >= k) best = k;
            }
            return hasMax && power >= powerMax ? PowerBarDef.MaxKey : best;
        }

        /// <summary>
        /// Go `FightScreenTime.draw`: the `counterN` variant is the highest key not above the
        /// remaining count; an infinite timer (count &lt; 0) takes the highest key of all.
        /// </summary>
        public static int TimeCounterKey(ICollection<int> keys, int timeval) {
            int best = 0;
            if (keys == null) return best;
            foreach (var k in keys)
                if (k > best && (timeval < 0 || timeval >= k)) best = k;
            return best;
        }

        /// <summary>
        /// Go `PowerBar.draw`: with `levelbars` the bar shows the progress inside the current
        /// 1000-point level (and stays full once the last level is reached); without it, the
        /// plain power fraction.
        /// </summary>
        public static float LevelBarFill(int power, int powerMax, bool levelBars) {
            if (powerMax <= 0) return 0f;
            if (!levelBars) return power / (float)powerMax;
            int level = power / 1000;
            return power / 1000f - Math.Min((float)level, powerMax / 1000f - 1f);
        }

        /// <summary>
        /// Ticks since the round intro started: the engine's own per-round clock
        /// (<see cref="FightEngine.RoundTick"/>), -1 while the characters are still walking in.
        /// It has to be the round clock and not the state clock, otherwise the clock restarts
        /// when the round ends and the "Fight!" call would be announced a second time over the KO.
        /// </summary>
        public static int IntroTick(RoundState state, int roundTick) {
            return state == RoundState.Intro ? -1 : roundTick;
        }

        /// <summary>
        /// Ticks since the round ended (the KO / time-over moment), or -1 while the round runs.
        /// `RoundState.Over` lasts `overTime` ticks and is followed by the win pose.
        /// </summary>
        public static int OutroTick(RoundState state, int stateTime, int overTime) {
            switch (state) {
                case RoundState.Over: return stateTime;
                case RoundState.WinPose: return overTime + stateTime;
                default: return -1;
            }
        }

        /// <summary>
        /// Go `AnimTextSnd.Draw` + `AnimTextSnd.End`: an element appears `startTime` ticks into
        /// its phase; a positive `displayTime` hides it again that many ticks later, a negative
        /// one (the -2 default) keeps it until its animation has played out.
        /// </summary>
        public static bool ElementVisible(int phaseTick, int startTime, int displayTime, int animLength) {
            int t = phaseTick - startTime;
            if (t < 0) return false;
            if (displayTime > 0) return t <= displayTime;
            if (displayTime == 0) return t <= 0;
            return animLength <= 0 || t < animLength;
        }

        /// <summary>
        /// Go `Fnt.DrawText`: the pen starts at the text position plus the font's own offset,
        /// shifted left by half the string (align 0) or by all of it (align &lt; 0).
        /// </summary>
        public static float TextPenX(HudFont f, string txt, float x, float xscl, int align) {
            if (f == null) return x;
            float pen = x + f.OffsetX * xscl;
            int w = f.TextWidth(txt);
            if (align == 0) pen -= w * xscl * 0.5f;
            else if (align < 0) pen -= w * xscl;
            return pen;
        }

        /// <summary>
        /// Go `Fnt.DrawText`: the y a caller gives is the bottom of the line, so the pen (where
        /// each glyph's axis lands) sits a font height above it.
        /// </summary>
        public static float TextPenY(HudFont f, float y, float yscl) {
            return f == null ? y : y + (f.OffsetY - f.SizeY + 1) * yscl;
        }

        /// <summary>Go `OldSprintf` as the fight screen uses it: the first `token` becomes `value`.</summary>
        public static string ReplaceFirst(string text, string token, string value) {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            int i = text.IndexOf(token, StringComparison.Ordinal);
            return i < 0 ? text : text.Substring(0, i) + value + text.Substring(i + token.Length);
        }

        /// <summary>Length of an action in ticks (an endless last frame counts as the whole action).</summary>
        public static int AnimDuration(MugenAnimation a) {
            if (a == null || a.Frames.Count == 0) return 0;
            return a.TotalTime > 0 ? a.TotalTime : a.Length;
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Shows or hides the whole HUD.</summary>
        public void SetVisible(bool on) {
            if (root != null) root.gameObject.SetActive(on);
        }

        /// <summary>True when the HUD is built and its root is active.</summary>
        public bool Visible => root != null && root.gameObject.activeSelf;

        /// <summary>
        /// Draws one frame of the HUD from the engine state. Safe to call more than once per
        /// engine tick: every animation playhead is driven from the engine's own tick counters,
        /// never incremented per call.
        /// </summary>
        public void Draw(FightEngine engine) {
            if (!Ready || engine == null || root == null) return;
            for (int i = 0; i < 2; i++) {
                DrawLifeBar(i, engine);
                DrawPowerBar(i, engine);
                DrawName(i, engine);
                DrawWinIcons(i, engine);
            }
            DrawTime(engine);
            DrawRound(engine);
        }

        void DrawLifeBar(int side, FightEngine engine) {
            var lb = fight.LifeBar[side];
            var bar = engine.Bars[side];
            var n = life[side];
            if (lb == null || bar == null) { HideBar(n); return; }

            int tick = engine.Tick_;
            float top = Mathf.Clamp01(bar.TopLife);      // Go lbr.toplife: the animated front bar
            float mid = Mathf.Clamp01(bar.MidLife);      // Go lbr.midlife: the delayed damage bar
            float lifeNow = Mathf.Clamp01(bar.LifeFraction);

            ShowElement(n.Bg0, lb.Bg0, lb.Pos, 1f, 1f, tick, null);
            ShowElement(n.Bg1, lb.Bg1, lb.Pos, 1f, 1f, tick, null);
            ShowElement(n.Bg2, lb.Bg2, lb.Pos, 1f, 1f, tick, null);

            // scalefill scales the sprite instead of shrinking the clipping rectangle.
            float frontScaleX = 1f, frontScaleY = 1f, midScaleX = 1f, midScaleY = 1f;
            bool vertical = RangeIsSet(lb.RangeY);
            if (lb.ScaleFill) {
                if (vertical) { frontScaleY = top; midScaleY = mid; }
                else { frontScaleX = top; midScaleX = mid; }
            }
            var frontRect = BarRect(lb.Pos, lb.RangeX, lb.RangeY, lb.ScaleFill ? 1f : top);
            var midRect = BarRect(lb.Pos, lb.RangeX, lb.RangeY, lb.ScaleFill ? 1f : mid);
            if (!lb.ScaleFill) {
                // Go: the mid bar only covers what the front bar has already lost.
                if (vertical) {
                    if (lb.RangeY[0] < lb.RangeY[1]) midRect.y += frontRect.height;
                    midRect.height -= Mathf.Min(midRect.height, frontRect.height);
                } else {
                    if (lb.RangeX[0] < lb.RangeX[1]) midRect.x += frontRect.width;
                    midRect.width -= Mathf.Min(midRect.width, frontRect.width);
                }
            }

            ShowElement(n.Mid, lb.Mid, lb.Pos, midScaleX, midScaleY, tick, midRect);

            float key = LifeFrontKey(lb.Front.Keys, lifeNow);
            var front = lb.Front.ContainsKey(key) ? lb.Front[key] : lb.FrontDefault;
            ShowElement(n.Front, front, lb.Pos, frontScaleX, frontScaleY, tick, frontRect);

            ShowElement(n.Top, lb.Top, lb.Pos, 1f, 1f, tick, null);
            bool warn = lifeNow <= lb.WarnRange[0] / 100f && lifeNow >= lb.WarnRange[1] / 100f;
            ShowElement(warn ? n.Warn : null, lb.Warn, lb.Pos, 1f, 1f, tick, null);
            if (!warn) Hide(n.Warn);
        }

        void DrawPowerBar(int side, FightEngine engine) {
            var pb = fight.PowerBar[side];
            var f = engine.Players[side];
            var n = power[side];
            if (pb == null || f == null) { HideBar(n); return; }

            int tick = engine.Tick_;
            int pbval = f.Power, pmax = f.PowerMax;
            float fill = Mathf.Clamp01(LevelBarFill(pbval, pmax, pb.LevelBars));

            int bgKey = PowerBarKey(pb.Bg0.Keys, pbval, pmax);
            ShowElement(n.Bg0, pb.Bg0.ContainsKey(bgKey) ? pb.Bg0[bgKey] : null, pb.Pos, 1f, 1f, tick, null);
            ShowElement(n.Bg1, pb.Bg1, pb.Pos, 1f, 1f, tick, null);
            ShowElement(n.Bg2, pb.Bg2, pb.Pos, 1f, 1f, tick, null);

            float sx = 1f, sy = 1f;
            bool vertical = RangeIsSet(pb.RangeY);
            if (pb.ScaleFill) { if (vertical) sy = fill; else sx = fill; }
            var rect = BarRect(pb.Pos, pb.RangeX, pb.RangeY, pb.ScaleFill ? 1f : fill);

            // The engine has no separate "mid power" bar state, so mid is drawn at the fill.
            ShowElement(n.Mid, pb.Mid, pb.Pos, sx, sy, tick, rect);
            int frontKey = PowerBarKey(pb.Front.Keys, pbval, pmax);
            ShowElement(n.Front, pb.Front.ContainsKey(frontKey) ? pb.Front[frontKey] : null,
                        pb.Pos, sx, sy, tick, rect);
            ShowElement(n.Top, pb.Top, pb.Pos, 1f, 1f, tick, null);

            if (powerCounter[side] != null) {
                int cKey = PowerBarKey(pb.Counter.Keys, pbval, pmax);
                var txt = pb.Counter.ContainsKey(cKey) ? pb.Counter[cKey] : pb.Counter[0];
                var fnt = FontFor(txt);
                string s = ReplaceFirst(txt.Text, "%i", (pbval / Math.Max(1, pb.CounterRounding)).ToString());
                SetText(powerCounter[side], fnt, txt, pb.Pos, s);
            }
        }

        void DrawName(int side, FightEngine engine) {
            if (nameText[side] == null) return;
            var nm = fight.Names[side];
            var f = engine.Players[side];
            if (nm == null || f == null || f.Character == null) { ClearText(nameText[side]); return; }
            // Go draws sys.cgi[pn].lifebarname, which is the character's displayname.
            SetText(nameText[side], FontFor(nm.Name), nm.Name, nm.Pos, f.Character.DisplayName);
        }

        void DrawTime(FightEngine engine) {
            var ti = fight.Time;
            if (ti == null) return;
            int tick = engine.Tick_;
            ShowElement(timeBg, ti.Bg, ti.Pos, 1f, 1f, tick, null);
            if (timeCounter != null) {
                int timeval = engine.TimeLeft;
                // Go: "o" (the infinity glyph of the Timer font) when the timer is disabled.
                string s = timeval < 0 ? "o" : timeval.ToString();
                int key = TimeCounterKey(ti.Counter.Keys, timeval);
                var txt = ti.Counter.ContainsKey(key) ? ti.Counter[key] : ti.Counter[0];
                SetText(timeCounter, FontFor(txt), txt, ti.Pos, s);
            }
            ShowElement(timeTop, ti.Top, ti.Pos, 1f, 1f, tick, null);
        }

        void DrawWinIcons(int side, FightEngine engine) {
            var wi = fight.WinIcon[side];
            if (wi == null || winBg[side] == null) return;
            int tick = engine.Tick_;
            int slots = winBg[side].Length;
            int bgCount = Math.Min(wi.UseIconUpTo, engine.RoundsToWin);
            int wins = Math.Min(engine.Wins[side], slots);
            for (int i = 0; i < slots; i++) {
                var pos = new[] { wi.Pos[0] + wi.IconOffset[0] * i, wi.Pos[1] + wi.IconOffset[1] * i };
                ShowElement(i < bgCount ? winBg[side][i] : null, wi.Bg0, pos, 1f, 1f, tick, null);
                if (i >= bgCount) Hide(winBg[side][i]);
                var icon = wi.Icon[(int)FightWinType.Normal];
                ShowElement(i < wins ? winIcon[side][i] : null, icon, pos, 1f, 1f, tick, null);
                if (i >= wins) Hide(winIcon[side][i]);
                ShowElement(i < bgCount ? winTop[side][i] : null, wi.Top, pos, 1f, 1f, tick, null);
                if (i >= bgCount) Hide(winTop[side][i]);
            }
        }

        void DrawRound(FightEngine engine) {
            var ro = fight.Round;
            if (ro == null) return;
            int intro = IntroTick(engine.State, engine.RoundTick);
            int outro = OutroTick(engine.State, engine.StateTime, engine.OverTime);

            // "Round N": starts round.time ticks into the intro (Go handleRoundIntro).
            var roundEl = ro.RoundDefault;
            string roundText = ReplaceFirst(roundEl != null ? roundEl.Text.Text : "", "%i", engine.RoundNo.ToString());
            DrawAnnouncement(roundAnn, roundEl, ro.RoundDefaultTop, ro.RoundDefaultBg, ro.Pos,
                             intro, ro.RoundTime, roundText);

            // "Fight!": callfight.time ticks after the round call started.
            DrawAnnouncement(fightAnn, ro.Fight, ro.FightTop, ro.FightBg, ro.Pos,
                             intro, ro.RoundTime + ro.CallFightTime, ro.Fight != null ? ro.Fight.Text.Text : "");

            // KO / Double KO / Time over: which one the def shows follows how the round ended.
            // Both fighters still standing when the round is over means the clock ran out.
            bool p1Down = engine.Players[0] != null && engine.Players[0].Life <= 0;
            bool p2Down = engine.Players[1] != null && engine.Players[1].Life <= 0;
            bool isDko = p1Down && p2Down;
            bool isTo = !p1Down && !p2Down;
            DrawAnnouncement(koAnn, !isDko && !isTo ? ro.Ko : null, ro.KoTop, ro.KoBg, ro.Pos,
                             outro, ro.KoTime, ro.Ko != null ? ro.Ko.Text.Text : "");
            DrawAnnouncement(dkoAnn, isDko ? ro.Dko : null, ro.DkoTop, ro.DkoBg, ro.Pos,
                             outro, ro.DkoTime, ro.Dko != null ? ro.Dko.Text.Text : "");
            DrawAnnouncement(toAnn, isTo ? ro.To : null, ro.ToTop, ro.ToBg, ro.Pos,
                             outro, ro.ToTime, ro.To != null ? ro.To.Text.Text : "");

            // Winner announcement: win.time ticks after the round ended, on the winner's side.
            var win = ro.Win[0];
            int winner = engine.RoundWinner;             // 1 = p1, 2 = p2, 3 = draw
            for (int side = 0; side < 2; side++) {
                bool won = winner == side + 1;
                var winEl = won && win != null ? win.Text[side] : null;
                string winText = "";
                if (winEl != null) {
                    var f = engine.Players[side];
                    winText = ReplaceFirst(winEl.Text.Text, "%s",
                                           f != null && f.Character != null ? f.Character.DisplayName : "");
                }
                DrawAnnouncement(winAnn[side], winEl, win != null ? win.Top[side] : null,
                                 win != null ? win.Bg[side] : null, ro.Pos, outro, ro.WinTime, winText);
            }
        }

        void DrawAnnouncement(Announcement a, FightElement main, FightAnimLayout top,
                              FightAnimLayout[] bg, int[] pos, int phaseTick, int startTime, string text) {
            if (a == null) return;
            int animLen = 0;
            if (main != null && main.Anim != null && main.Anim.AnimNo >= 0 && fight.Animations != null)
                animLen = AnimDuration(fight.Animations.Get(main.Anim.AnimNo));
            bool on = main != null && phaseTick >= 0 &&
                      ElementVisible(phaseTick, startTime, main.DisplayTime, animLen);
            if (!on) { HideAnnouncement(a); return; }

            int t = phaseTick - startTime;
            int idx = 0;
            if (bg != null)
                for (int i = bg.Length - 1; i >= 0; i--) {
                    if (bg[i] == null || !bg[i].HasFrames) continue;
                    if (idx < a.Bg.Length) ShowElement(a.Bg[idx], bg[i], pos, 1f, 1f, t, null);
                    idx++;
                }
            for (; idx < a.Bg.Length; idx++) Hide(a.Bg[idx]);

            // Go AnimTextSnd.Draw: an animation wins over the text, never both.
            bool hasAnim = main.Anim != null && main.Anim.HasFrames;
            if (hasAnim) {
                ShowElement(a.Main, main.Anim, pos, 1f, 1f, t, null);
                ClearText(a.Text);
            } else {
                Hide(a.Main);
                SetText(a.Text, FontFor(main.Text), main.Text, pos, text);
            }
            ShowElement(a.Top, top, pos, 1f, 1f, t, null);
        }

        void HideAnnouncement(Announcement a) {
            if (a == null) return;
            for (int i = 0; i < a.Bg.Length; i++) Hide(a.Bg[i]);
            Hide(a.Main);
            Hide(a.Top);
            ClearText(a.Text);
        }

        // ------------------------------------------------------------------ node plumbing

        /// <summary>The clipping rectangle of a bar fill, in local units (x right, y down).</summary>
        Rect BarRect(int[] pos, int[] rangeX, int[] rangeY, float fill) {
            float x = 0f, w = localW, y = 0f, h = localH;
            if (RangeIsSet(rangeX)) {
                x = BarStart(pos[0], rangeX, fill);
                w = BarWidth(rangeX, fill);
            }
            if (RangeIsSet(rangeY)) {
                y = BarStart(pos[1], rangeY, fill);
                h = BarWidth(rangeY, fill);
            }
            return new Rect(x, y, Mathf.Max(0f, w), Mathf.Max(0f, h));
        }

        /// <summary>
        /// Positions and shows one element. <paramref name="clip"/> is the clipping rectangle in
        /// local units, or null to leave the element unclipped.
        /// </summary>
        void ShowElement(Element e, FightAnimLayout al, int[] pos, float xscl, float yscl,
                         int tick, Rect? clip) {
            if (e == null) return;
            if (al == null || !al.HasFrames) { Hide(e); return; }

            SffSprite spr = null;
            AnimFrame frame = null;
            if (al.HasSprite) {
                spr = sff.Get(al.SprGroup, al.SprNumber);
            } else if (fight.Animations != null) {
                var anim = fight.Animations.Get(al.AnimNo);
                if (anim != null) {
                    if (e.Cursor == null || e.CursorAnim != al.AnimNo) {
                        e.Cursor = new AnimCursor(anim);
                        e.CursorAnim = al.AnimNo;
                    }
                    e.Cursor.SeekTo(Math.Max(0, tick));
                    frame = e.Cursor.Frame;
                    if (frame != null && frame.Group >= 0) spr = sff.Get(frame.Group, frame.Number);
                }
            }
            var unity = spr != null ? cache.SpriteFor(sff, spr) : null;
            if (unity == null) { Hide(e); return; }

            var lay = al.Layout;
            float fx = lay.Facing, fy = lay.VFacing;
            float ox = frame != null ? frame.Xoffset * lay.ScaleX * fx : 0f;
            float oy = frame != null ? frame.Yoffset * lay.ScaleY * fy : 0f;
            float lx = pos[0] + lay.OffsetX + ox;
            float ly = pos[1] + lay.OffsetY + oy;

            if (e.Clip != null) {
                var r = clip ?? new Rect(0f, 0f, localW, localH);
                e.Clip.anchoredPosition = new Vector2(r.x * uiScale, -r.y * uiScale);
                e.Clip.sizeDelta = new Vector2(r.width * uiScale, r.height * uiScale);
                lx -= r.x;
                ly -= r.y;
            }

            e.Img.sprite = unity;
            e.Img.enabled = true;
            e.Rt.pivot = new Vector2(spr.Width > 0 ? (float)spr.X / spr.Width : 0.5f,
                                     spr.Height > 0 ? 1f - (float)spr.Y / spr.Height : 0.5f);
            e.Rt.sizeDelta = new Vector2(spr.Width * uiScale, spr.Height * uiScale);
            e.Rt.anchoredPosition = new Vector2(lx * uiScale, -ly * uiScale);
            float hs = frame != null ? frame.Hscale * Math.Abs(frame.Xscale) : 1f;
            float vs = frame != null ? frame.Vscale * Math.Abs(frame.Yscale) : 1f;
            e.Rt.localScale = new Vector3(lay.ScaleX * xscl * fx * hs, lay.ScaleY * yscl * fy * vs, 1f);
        }

        static void Hide(Element e) {
            if (e != null && e.Img != null) e.Img.enabled = false;
        }

        static void HideBar(BarNodes n) {
            Hide(n.Bg0); Hide(n.Bg1); Hide(n.Bg2); Hide(n.Mid); Hide(n.Front); Hide(n.Top); Hide(n.Warn);
        }

        /// <summary>
        /// Lays a string out with a screenpack font: one Image per glyph, each with its MUGEN
        /// axis on the pen, advancing by the glyph width plus the font spacing (Go `Fnt.DrawText`).
        /// </summary>
        void SetText(TextNode node, HudFont font, FightText txt, int[] pos, string s) {
            if (node == null) return;
            if (font == null || !font.Ready || txt == null || string.IsNullOrEmpty(s)) { ClearText(node); return; }

            var lay = txt.Layout;
            float xscl = lay.ScaleX * fntScale;
            float yscl = lay.ScaleY * fntScale;
            float penX = TextPenX(font, s, pos[0] + lay.OffsetX, xscl, txt.FontAlign);
            float penY = TextPenY(font, pos[1] + lay.OffsetY, yscl);
            var color = TextColor(txt);

            int used = 0;
            for (int i = 0; i < s.Length; i++) {
                char c = s[i];
                var g = font.Glyph(c, txt.FontBank);
                if (g != null && !g.IsBlank) {
                    var unity = font.Cache.SpriteFor(font.Sff, g);
                    if (unity != null) {
                        var img = GlyphImage(node, used++);
                        img.sprite = unity;
                        img.color = color;
                        img.enabled = true;
                        var rt = img.rectTransform;
                        rt.pivot = new Vector2(g.Width > 0 ? (float)g.X / g.Width : 0.5f,
                                               g.Height > 0 ? 1f - (float)g.Y / g.Height : 0.5f);
                        rt.sizeDelta = new Vector2(g.Width * xscl * uiScale, g.Height * yscl * uiScale);
                        rt.anchoredPosition = new Vector2(penX * uiScale, -penY * uiScale);
                    }
                }
                // Go drawChar: a missing glyph (and a space) still advances by the font width.
                penX += (g != null && !g.IsBlank ? g.Width : font.SizeX) * xscl + xscl * font.SpacingX;
            }
            for (int i = used; i < node.Glyphs.Count; i++) node.Glyphs[i].enabled = false;
        }

        /// <summary>
        /// Go `readFSText`: `font = idx, bank, align, r, g, b, a` tints a sprite font when all
        /// three colour components are given; otherwise the glyphs keep their own colours.
        /// </summary>
        static Color TextColor(FightText txt) {
            if (txt == null) return Color.white;
            if (txt.Font[3] < 0 || txt.Font[4] < 0 || txt.Font[5] < 0) return Color.white;
            int a = txt.Font[6] < 0 ? 255 : txt.Font[6];
            return new Color(txt.Font[3] / 255f, txt.Font[4] / 255f, txt.Font[5] / 255f, a / 255f);
        }

        Image GlyphImage(TextNode node, int index) {
            while (node.Glyphs.Count <= index) {
                var img = UIKit.Image(node.Rt, "glyph" + node.Glyphs.Count, new Vector2(0f, 1f),
                                      Vector2.zero, Vector2.zero);
                img.enabled = false;
                node.Glyphs.Add(img);
            }
            return node.Glyphs[index];
        }

        static void ClearText(TextNode node) {
            if (node == null) return;
            for (int i = 0; i < node.Glyphs.Count; i++) node.Glyphs[i].enabled = false;
        }

        // ------------------------------------------------------------------ teardown

        /// <summary>Destroys the HUD objects and the private font caches. The caller keeps its own cache.</summary>
        public void Dispose() {
            foreach (var kv in fonts) if (kv.Value != null) kv.Value.Dispose();
            fonts.Clear();
            if (root != null) {
                var go = root.gameObject;
                if (Application.isPlaying) UnityEngine.Object.Destroy(go);
                else UnityEngine.Object.DestroyImmediate(go);
                root = null;
            }
            Ready = false;
        }
    }
}
