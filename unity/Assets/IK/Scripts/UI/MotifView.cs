using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// A motif-space drawing surface for one front-end screen: a localcoord-sized rect
    /// (1280x720 for ikemen1) centred and scaled into the screen, with the motif background
    /// (`[&lt;Prefix&gt;BGdef]`, drawn by the stage <see cref="StageRenderer"/>) behind and in
    /// front, and helpers to place motif sprites, actions and texts at motif coordinates
    /// (x right, y down, origin at the top-left of the motif screen — Ikemen's convention).
    ///
    /// Text uses the motif's bitmap font through <see cref="HudFont"/> (the same glyph code
    /// as the fight HUD); strings the bitmap font cannot show (Arabic) fall back to the UI
    /// TrueType font at the same position and colour.
    /// </summary>
    public class MotifView : IDisposable {
        public readonly RectTransform Root;     // full screen, black outside the motif rect
        public readonly RectTransform Area;     // localcoord rect (height-fitted, clipped)
        public readonly RectTransform BgBack, Layer0, BgFront, Top;
        /// <summary>dev.8: localcoord rects scaled to cover the whole screen, holding the background layers.</summary>
        public readonly RectTransform BackCover, FrontCover, TopArea;
        public StageDefinition Background { get; private set; }
        public float Width => Motif.LocalCoord[0];
        public float Height => Motif.LocalCoord[1];
        public Motif Motif => MotifAssets.Motif;
        StageRenderer renderer;

        public MotifView(RectTransform parent, string name) {
            Root = UIKit.Panel(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.black);
            Root.GetComponent<Image>().raycastTarget = true;   // a screen swallows taps behind it
            // dev.8: the background is drawn in rects that cover the whole screen (20:9 phones
            // showed black bars beside the 16:9 art); menus and texts stay in the height-fitted
            // motif area. Order: back background, layer 0, front background, top.
            BackCover = MotifRect("bgCoverBack", true, false);
            Area = MotifRect("motif", false, true);
            FrontCover = MotifRect("bgCoverFront", true, false);
            TopArea = MotifRect("motifTop", false, true);
            BgBack = Layer(BackCover, "bgBack");
            Layer0 = Layer(Area, "layer0");
            BgFront = Layer(FrontCover, "bgFront");
            Top = Layer(TopArea, "top");
        }

        RectTransform MotifRect(string name, bool cover, bool clip) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(Root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Width, Height);
            // the canvas is 720 units high (UIKit reference): fit the motif by height
            float s = 720f / Mathf.Max(1f, Height);
            rt.localScale = new Vector3(s, s, 1f);
            if (clip) go.AddComponent<RectMask2D>();
            if (cover) {
                var fit = go.AddComponent<MotifCoverFit>();
                fit.Set(Width, Height);
            }
            return rt;
        }

        static RectTransform Layer(RectTransform parent, string name) => UIKit.Panel(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        /// <summary>Builds the `[&lt;prefix&gt;def]` background ("TitleBG", "SelectBG", ...).</summary>
        public void SetBackground(string prefix) {
            if (renderer != null) renderer.Dispose();
            renderer = null;
            Background = Motif.Background(prefix);
            if (Background == null || Background.Sprites == null) return;
            renderer = new StageRenderer(BgBack, BgFront, Background, MotifAssets.MotifCache) {
                Scale = 1f, FloorY = Height
            };
            var clear = Background.BgClearColor;
            Root.GetComponent<Image>().color = Color.black;
            if (clear != null && (clear[0] | clear[1] | clear[2]) != 0) {
                var img = BackCover.GetComponent<Image>();
                if (img == null) img = BackCover.gameObject.AddComponent<Image>();
                img.color = new Color(clear[0] / 255f, clear[1] / 255f, clear[2] / 255f);
                img.raycastTarget = false;
            }
            renderer.Draw(0f, 0f, Width, Height);
        }

        public int DrawnBackgroundElements => renderer != null ? renderer.DrawnElements : 0;

        /// <summary>One 60 Hz tick of the background (velocities, sin offsets, actions).</summary>
        public void Tick() {
            if (Background == null || renderer == null) return;
            Background.Tick();
            renderer.Draw(0f, 0f, Width, Height);
        }

        public void ResetBackground() { if (Background != null) Background.Reset(); }

        // ------------------------------------------------------------------ coordinates

        /// <summary>Motif point (x right, y down from the top-left) as a UI anchored position in <see cref="Area"/>.</summary>
        public static Vector2 Ui(float x, float y) => new Vector2(x, -y);

        /// <summary>A clipping window `x, y, w, h` (FightLayout.Window) in motif coordinates.</summary>
        public RectTransform Window(Transform parent, string name, FightLayout layout) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            if (layout != null && layout.HasWindow) {
                rt.anchoredPosition = Ui(layout.Window[0], layout.Window[1]);
                rt.sizeDelta = new Vector2(layout.Window[2], layout.Window[3]);
                go.AddComponent<RectMask2D>();
            } else {
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(Width, Height);
            }
            return rt;
        }

        // ------------------------------------------------------------------ elements

        /// <summary>A drawable motif sprite (one Image), positioned by its MUGEN axis.</summary>
        public class SpriteNode {
            public readonly Image Img;
            public readonly RectTransform Rt;
            readonly Vector2 origin;          // motif position of the parent's top-left
            public SpriteNode(Transform parent, string name, Vector2 parentOrigin) {
                Img = UIKit.Image(parent, name, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
                Img.enabled = false;
                Rt = Img.rectTransform;
                origin = parentOrigin;
            }

            /// <summary>Draws <paramref name="s"/> with its axis at motif (x, y); facing -1 mirrors.</summary>
            public void Set(Sprite sprite, SffSprite s, float x, float y, float sx, float sy, int facing = 1, Color? tint = null) {
                if (sprite == null || s == null) { Img.enabled = false; return; }
                Img.sprite = sprite;
                Img.enabled = true;
                Img.color = tint ?? Color.white;
                Rt.pivot = new Vector2(s.Width > 0 ? (float)s.X / s.Width : 0.5f, s.Height > 0 ? 1f - (float)s.Y / s.Height : 0.5f);
                Rt.sizeDelta = new Vector2(s.Width * Mathf.Abs(sx), s.Height * Mathf.Abs(sy));
                Rt.localScale = new Vector3(facing < 0 ? -1f : 1f, sy < 0 ? -1f : 1f, 1f);
                Rt.anchoredPosition = Ui(x - origin.x, y - origin.y);
            }

            public void Hide() { Img.enabled = false; }
        }

        /// <summary>An action player (system.def `[Begin Action]` or a character action).</summary>
        public class AnimNode {
            public readonly SpriteNode Node;
            public MugenAnimation Anim { get; private set; }
            int animNo = int.MinValue;
            object owner;
            public AnimNode(Transform parent, string name, Vector2 origin) { Node = new SpriteNode(parent, name, origin); }

            /// <summary>Switches to action <paramref name="no"/> of <paramref name="table"/> (restarts only on change).</summary>
            public void Play(AirFile table, int no, object tableOwner = null) {
                if (table == null) { Anim = null; animNo = int.MinValue; return; }
                if (no == animNo && tableOwner == owner && Anim != null) return;
                var a = table.Get(no);
                Anim = a != null ? a.Instance() : null;
                if (Anim != null) Anim.Reset();
                animNo = no; owner = tableOwner;
            }

            public void Tick() { if (Anim != null) Anim.Tick(); }

            /// <summary>Draws the current frame: lookup(group, number) gives the Unity + raw sprite.</summary>
            public void Draw(Func<int, int, (Sprite, SffSprite)> lookup, float x, float y, float sx, float sy, int facing = 1, Color? tint = null) {
                var f = Anim != null ? Anim.CurrentFrame : null;
                if (f == null || f.Group < 0 || lookup == null) { Node.Hide(); return; }
                var (sprite, raw) = lookup(f.Group, f.Number);
                float fx = f.Hscale < 0 ? -1 : 1;
                float ox = f.Xoffset * sx * facing, oy = f.Yoffset * sy;
                float esx = sx * (f.Xscale == 0 ? 1f : f.Xscale), esy = sy * (f.Yscale == 0 ? 1f : f.Yscale);
                Node.Set(sprite, raw, x + ox, y + oy, esx, esy * (f.Vscale < 0 ? -1 : 1), facing * (int)fx, tint);
            }

            public void Hide() => Node.Hide();
        }

        /// <summary>A line of text in a motif font (bitmap glyphs) with a TrueType fallback.</summary>
        public class TextNode {
            public readonly RectTransform Rt;
            readonly List<Image> glyphs = new List<Image>();
            readonly Text fallback;
            readonly Vector2 origin;
            public string Value { get; private set; } = "";
            public bool UsedBitmap { get; private set; }

            /// <summary>dev.8: upper bound of the TrueType fallback height (0 = none).</summary>
            public float MaxTtfHeight;

            public TextNode(Transform parent, string name, Vector2 parentOrigin) {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                Rt = (RectTransform)go.transform;
                Rt.anchorMin = Rt.anchorMax = new Vector2(0f, 1f);
                Rt.pivot = new Vector2(0f, 1f);
                Rt.sizeDelta = Vector2.zero;
                origin = parentOrigin;
                fallback = UIKit.Text(Rt, "ttf", new Vector2(0f, 1f), Vector2.zero, new Vector2(10, 10), "", 24);
                fallback.enabled = false;
            }

            /// <summary>
            /// Writes <paramref name="s"/> at motif (x, y) — the bottom of the line, as in Go
            /// `Fnt.DrawText` — with the font, bank, alignment and tint of <paramref name="t"/>.
            /// </summary>
            public void Set(FightText t, float x, float y, string s, Color? colorOverride = null, bool forceTtf = false) {
                Value = s ?? "";
                if (t == null) t = FightText.Read(null, "", "", 2, 0);
                var font = MotifAssets.Font(t.FontIndex);
                float xs = t.Layout != null ? t.Layout.ScaleX : 1f, ys = t.Layout != null ? t.Layout.ScaleY : 1f;
                float px = x + (t.Layout != null ? t.Layout.OffsetX : 0f), py = y + (t.Layout != null ? t.Layout.OffsetY : 0f);
                var color = colorOverride ?? Tint(t);
                bool bitmap = !forceTtf && font != null && BitmapCanDraw(font, Value, t.FontBank);
                UsedBitmap = bitmap && Value.Length > 0;
                int used = 0;
                if (bitmap) {
                    fallback.enabled = false;
                    float pen = FightHud.TextPenX(font, Value, px, xs, t.FontAlign);
                    float penY = FightHud.TextPenY(font, py, ys);
                    foreach (char c in Value) {
                        var g = font.Glyph(c, t.FontBank);
                        if (g != null && !g.IsBlank) {
                            var spr = font.Cache.SpriteFor(font.Sff, g);
                            if (spr != null) {
                                var img = Glyph(used++);
                                img.sprite = spr;
                                img.color = color;
                                img.enabled = true;
                                var rt = img.rectTransform;
                                rt.pivot = new Vector2(g.Width > 0 ? (float)g.X / g.Width : 0.5f, g.Height > 0 ? 1f - (float)g.Y / g.Height : 0.5f);
                                rt.sizeDelta = new Vector2(g.Width * xs, g.Height * ys);
                                rt.anchoredPosition = Ui(pen - origin.x, penY - origin.y);
                            }
                        }
                        pen += (g != null && !g.IsBlank ? g.Width : font.SizeX) * xs + xs * font.SpacingX;
                    }
                } else {
                    // TrueType fallback: same anchor point and alignment, height from the font size
                    float h = font != null ? Mathf.Max(18f, font.SizeY * ys * 1.25f) : 30f * ys;
                    if (MaxTtfHeight > 0f) h = Mathf.Min(h, MaxTtfHeight);
                    fallback.enabled = Value.Length > 0;
                    fallback.fontSize = Mathf.RoundToInt(h);
                    fallback.color = color;
                    UIKit.SetText(fallback, Value);
                    var rt = fallback.rectTransform;
                    rt.sizeDelta = new Vector2(1200f, h * 1.5f);
                    float pivotX = t.FontAlign > 0 ? 0f : (t.FontAlign < 0 ? 1f : 0.5f);
                    rt.pivot = new Vector2(pivotX, 0.15f);
                    fallback.alignment = t.FontAlign > 0 ? TextAnchor.LowerLeft : (t.FontAlign < 0 ? TextAnchor.LowerRight : TextAnchor.LowerCenter);
                    rt.anchoredPosition = Ui(px - origin.x, py - origin.y);
                }
                for (int i = used; i < glyphs.Count; i++) glyphs[i].enabled = false;
            }

            public void Hide() {
                Value = "";
                fallback.enabled = false;
                foreach (var g in glyphs) g.enabled = false;
            }

            /// <summary>True when every non-space character has a glyph in the bitmap font.</summary>
            public static bool BitmapCanDraw(HudFont f, string s, int bank) {
                if (f == null || !f.Ready) return false;
                foreach (char c in s) {
                    if (c == ' ') continue;
                    if (c > 0x7e) return false;
                    if (f.Glyph(c, bank) == null) return false;
                }
                return true;
            }

            Image Glyph(int i) {
                while (glyphs.Count <= i) {
                    var img = UIKit.Image(Rt, "g" + glyphs.Count, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
                    img.enabled = false;
                    glyphs.Add(img);
                }
                return glyphs[i];
            }
        }

        /// <summary>Go `readFSText` tint: `font = idx, bank, align, r, g, b` colours the glyphs.</summary>
        public static Color Tint(FightText t) {
            if (t == null || t.Font[3] < 0 || t.Font[4] < 0 || t.Font[5] < 0) return Color.white;
            int a = t.Font[6] < 0 ? 255 : t.Font[6];
            return new Color(Mathf.Min(255, t.Font[3]) / 255f, Mathf.Min(255, t.Font[4]) / 255f, Mathf.Min(255, t.Font[5]) / 255f, a / 255f);
        }

        public SpriteNode NewSprite(string name, Transform parent = null) => new SpriteNode(parent ?? Top, name, Vector2.zero);
        public AnimNode NewAnim(string name, Transform parent = null, Vector2 origin = default) => new AnimNode(parent ?? Top, name, origin);
        public TextNode NewText(string name, Transform parent = null) => new TextNode(parent ?? Top, name, Vector2.zero);

        /// <summary>Motif SFF lookup for <see cref="AnimNode.Draw"/>.</summary>
        public static (Sprite, SffSprite) MotifLookup(int g, int n) {
            var s = MotifAssets.MotifSprite(g, n, out var raw);
            return (s, raw);
        }

        /// <summary>
        /// An invisible tap target at motif rect (x, y) = centre, (w, h) size. Menus are
        /// navigable by tapping these as well as by D-pad / buttons.
        /// </summary>
        public Button Hit(string name, float cx, float cy, float w, float h, Action onTap, Transform parent = null) {
            var rt = UIKit.Rect(parent ?? Top, name, new Vector2(0f, 1f), Ui(cx, cy), new Vector2(w, h));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = img;
            if (onTap != null) b.onClick.AddListener(() => onTap());
            return b;
        }

        /// <summary>Screen-pixel rect of a motif rect (top-left x, y; size w, h) — for rendered checks.</summary>
        public RectInt PixelRect(float x, float y, float w, float h) {
            var corners = new Vector3[4];
            Area.GetWorldCorners(corners);
            float sx = (corners[2].x - corners[0].x) / Width, sy = (corners[2].y - corners[0].y) / Height;
            float left = corners[0].x + x * sx, top = corners[1].y - y * sy;
            return new RectInt(Mathf.FloorToInt(left), Mathf.FloorToInt(top - h * sy), Mathf.CeilToInt(w * sx), Mathf.CeilToInt(h * sy));
        }

        public void Dispose() {
            if (renderer != null) renderer.Dispose();
            renderer = null;
        }
    }
}
