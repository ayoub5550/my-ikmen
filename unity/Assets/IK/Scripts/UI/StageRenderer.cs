using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// Draws the backgrounds of a <see cref="StageDefinition"/> with uGUI graphics.
    ///
    /// Port of the position maths of Go `backGround.draw` (engine/ikemen-go/src/stage.go,
    /// lines ~520-760) for the case the dev.4 fight screen runs in: no camera zoom
    /// (<c>drawscl = 1</c>), no stage scale (<c>stgscl = 1,1</c>), no screen shake, no stage
    /// model and no hires stage, which is what reduces the Go formula to
    ///
    ///     x = start.x + bga.offset.x - camera.x * delta.x
    ///     y = start.y + bga.offset.y - camera.y * delta.y
    ///
    /// Everything else (tiling, the draw order of the layers, the parallax trapezoid, the
    /// stage z-offset as the floor line) follows the same source. What is *not* ported yet is
    /// listed in docs/DEV4.md: zoom, shake, window/maskwindow deltas, per-element PalFX,
    /// BGCtrl execution, rotation/shear/projection and stage models.
    /// </summary>
    public class StageRenderer : IDisposable {
        /// <summary>Pixels per stage unit; the fight screen owns this value.</summary>
        public float Scale = 2.6f;
        /// <summary>UI y (from the bottom of the stage rect) of the stage floor, char y = 0.</summary>
        public float FloorY = 92f;
        /// <summary>Safety cap so a broken tilespacing cannot spawn thousands of images.</summary>
        public int MaxTilesPerAxis = 96;

        readonly StageDefinition stage;
        readonly MugenAssetCache cache;
        readonly RectTransform back, front;
        readonly List<Element> elements = new List<Element>();

        /// <summary>Number of background elements that have a usable sprite.</summary>
        public int DrawnElements { get; private set; }

        class Element {
            public StageBackground Bg;
            public RectTransform Container;
            public readonly List<Image> Tiles = new List<Image>();
            public ParallaxGraphic Parallax;
        }

        public StageRenderer(RectTransform backLayer, RectTransform frontLayer,
                             StageDefinition stage, MugenAssetCache cache) {
            this.back = backLayer;
            this.front = frontLayer;
            this.stage = stage;
            this.cache = cache;
            if (stage == null) return;

            // one container per element, created in def order: uGUI draws siblings in order,
            // which is exactly MUGEN's draw order inside a layer
            foreach (var bg in stage.Backgrounds) {
                var parent = bg.LayerNo >= 1 && frontLayer != null ? frontLayer : backLayer;
                if (parent == null) continue;
                var go = new GameObject("bg " + bg.Name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = Vector2.zero;
                elements.Add(new Element { Bg = bg, Container = rt });
            }
        }

        // ---- pure layout maths (unit tested) --------------------------------------

        /// <summary>Go: `x = bg.start[0] + bg.xofs - pos[0]*bg.delta[0] + bg.bga.offset[0]`.</summary>
        public static float ElementX(StageBackground bg, float cameraX) =>
            bg.Start[0] + bg.Bga.Offset[0] - cameraX * bg.Delta[0];

        /// <summary>Go: `y = bg.start[1] - (pos[1]*bg.delta[1]) + bg.bga.offset[1]`.</summary>
        public static float ElementY(StageBackground bg, float cameraY) =>
            bg.Start[1] + bg.Bga.Offset[1] - cameraY * bg.Delta[1];

        /// <summary>
        /// Distance between two tiles on one axis. Go resolves `tilespacing` at load time by
        /// adding the sprite size for sprite-based elements (see StageFile.cs), so the already
        /// resolved value is used and only falls back to the sprite size when it is zero.
        /// </summary>
        public static int TileStep(StageBackground bg, int axis, int spriteSize) {
            int step = bg.TileSpacingResolved[axis];
            if (step <= 0) step = spriteSize;
            return step > 0 ? step : 0;
        }

        /// <summary>
        /// Tile indices (relative to the untiled sprite) needed to cover <paramref name="min"/>
        /// to <paramref name="max"/> when the base copy sits at <paramref name="origin"/>.
        /// `tile = 1` (or negative, MUGEN's "infinite") fills the range; `tile = n &gt; 1`
        /// draws n copies forward, which is how MUGEN documents the parameter.
        /// </summary>
        public static void TileRange(int tileFlag, int step, float origin, float min, float max,
                                     int cap, out int first, out int count) {
            first = 0; count = 1;
            if (tileFlag == 0 || step <= 0) return;
            if (tileFlag > 1) { count = Mathf.Min(tileFlag, cap); return; }
            first = Mathf.FloorToInt((min - origin) / step);
            int last = Mathf.CeilToInt((max - origin) / step);
            count = Mathf.Clamp(last - first + 1, 1, cap);
        }

        /// <summary>Stage screen coordinates (y counted down from the top) to UI coordinates.</summary>
        public Vector2 ToUi(float x, float y) =>
            new Vector2(x * Scale, FloorY + (stage.ZOffset - y) * Scale);

        // ---- drawing ---------------------------------------------------------------

        /// <summary>
        /// Positions every element for this frame. <paramref name="viewWidth"/> and
        /// <paramref name="viewHeight"/> are the visible area in stage units, used to know how
        /// far a tiled element has to repeat.
        /// </summary>
        public void Draw(float cameraX, float cameraY, float viewWidth, float viewHeight) {
            if (stage == null) return;
            DrawnElements = 0;
            float halfW = viewWidth / 2f;

            foreach (var el in elements) {
                var bg = el.Bg;
                bool on = bg.Visible && bg.Enabled;
                var sprite = on ? SpriteOf(bg) : null;
                if (sprite == null) {
                    el.Container.gameObject.SetActive(false);
                    continue;
                }
                el.Container.gameObject.SetActive(true);
                DrawnElements++;

                float x = ElementX(bg, cameraX), y = ElementY(bg, cameraY);
                var pos = ToUi(x, y);
                float w = sprite.Width * Scale, h = sprite.Height * Scale;
                // MUGEN draws from the sprite axis; scalestart is the element's own scale
                float sx = bg.ScaleStart[0], sy = bg.ScaleStart[1];
                float alpha = ElementAlpha(bg);

                if (bg.Type == StageBgType.Parallax) {
                    DrawParallax(el, sprite, pos, w * sx, h * sy, alpha);
                    continue;
                }

                int stepX = TileStep(bg, 0, sprite.Width);
                int stepY = TileStep(bg, 1, sprite.Height);
                // x is measured from the centre of the screen, y from its top edge
                TileRange(bg.Tile[0], stepX, x, -halfW - sprite.Width, halfW + sprite.Width,
                          MaxTilesPerAxis, out int firstX, out int countX);
                TileRange(bg.Tile[1], stepY, y, -sprite.Height, viewHeight + sprite.Height,
                          MaxTilesPerAxis, out int firstY, out int countY);

                int used = 0;
                for (int iy = 0; iy < countY; iy++) {
                    for (int ix = 0; ix < countX; ix++) {
                        var img = TileImage(el, used++);
                        var rt = img.rectTransform;
                        img.sprite = cache.SpriteFor(stage.Sprites, sprite);
                        img.color = new Color(1f, 1f, 1f, alpha);
                        img.material = IsAdditive(bg) ? AdditiveMaterial : Crisp ? NormalMaterial : null;
                        img.enabled = img.sprite != null;
                        rt.sizeDelta = new Vector2(w * Mathf.Abs(sx), h * Mathf.Abs(sy));
                        rt.pivot = new Vector2(sprite.Width > 0 ? (float)sprite.X / sprite.Width : 0.5f,
                                               sprite.Height > 0 ? 1f - (float)sprite.Y / sprite.Height : 0.5f);
                        rt.localScale = new Vector3(Mathf.Sign(sx == 0f ? 1f : sx),
                                                    Mathf.Sign(sy == 0f ? 1f : sy), 1f);
                        rt.anchoredPosition = new Vector2(
                            pos.x + (firstX + ix) * stepX * Scale,
                            pos.y - (firstY + iy) * stepY * Scale);
                    }
                }
                for (int i = used; i < el.Tiles.Count; i++) el.Tiles[i].enabled = false;
            }
        }

        /// <summary>
        /// Shared additive material for `trans = add / add1 / addalpha` elements
        /// (Resources/shaders/UIAdditive.shader). Null when the shader is missing, in which case
        /// the element falls back to plain alpha.
        /// </summary>
        public static Material AdditiveMaterial {
            get {
                if (additive == null) {
                    var shader = Resources.Load<Shader>("shaders/UIAdditive");
                    if (shader != null) additive = new Material(shader) { name = "IK UI additive" };
                }
                return additive;
            }
        }
        static Material additive;

        /// <summary>
        /// dev.7: one shared IK/UIPalFx material (normal blend, no palette effect) for the stage,
        /// so the Crisp filter applies to backgrounds too. Shared = batchable like the default.
        /// </summary>
        public static Material NormalMaterial {
            get {
                if (normal == null) {
                    var shader = Resources.Load<Shader>("shaders/UIPalFx");
                    if (shader != null) normal = new Material(shader) { name = "IK UI stage" };
                }
                return normal;
            }
        }
        static Material normal;

        /// <summary>Set by RenderQuality: the stage uses <see cref="NormalMaterial"/> only for Crisp
        /// (the default UI shader is cheaper; on a software rasteriser Crisp cost ~6 ms a frame).</summary>
        public static bool Crisp;

        /// <summary>True when MUGEN would blend this element additively.</summary>
        public static bool IsAdditive(StageBackground bg) =>
            bg.Trans == TransType.Add || bg.Trans == TransType.Add1 || bg.Trans == TransType.SubAdd;

        /// <summary>
        /// How much of the element is kept: MUGEN's `alpha = src, dst` source factor. With the
        /// additive material this is the additive weight; without it, plain transparency.
        /// `sub` has no uGUI equivalent and is approximated with half transparency (docs/DEV4.md).
        /// </summary>
        public static float ElementAlpha(StageBackground bg) {
            switch (bg.Trans) {
                case TransType.None:
                    return 1f;
                case TransType.Add:
                case TransType.Add1:
                case TransType.SubAdd:
                    // `alpha = src, dst`: src is how much of the source is kept
                    return Mathf.Clamp01(bg.SrcAlpha / 255f);
                case TransType.Sub:
                    return 0.5f;
                default:
                    return 1f;
            }
        }

        SffSprite SpriteOf(StageBackground bg) {
            if (stage.Sprites == null) return null;
            int g = bg.CurrentGroup, n = bg.CurrentNumber;
            if (g < 0) return null;
            var spr = stage.Sprites.Get(g, n);
            return spr != null && !spr.IsBlank ? spr : null;
        }

        Image TileImage(Element el, int index) {
            while (el.Tiles.Count <= index) {
                var go = new GameObject("tile" + el.Tiles.Count, typeof(RectTransform));
                go.transform.SetParent(el.Container, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                el.Tiles.Add(img);
            }
            var tile = el.Tiles[index];
            tile.enabled = true;
            return tile;
        }

        void DrawParallax(Element el, SffSprite sprite, Vector2 pos, float width, float height,
                          float alpha) {
            if (el.Parallax == null) {
                var go = new GameObject("parallax", typeof(RectTransform));
                go.transform.SetParent(el.Container, false);
                el.Parallax = go.AddComponent<ParallaxGraphic>();
                el.Parallax.raycastTarget = false;
                var prt = (RectTransform)go.transform;
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
                prt.pivot = new Vector2(0.5f, 0.5f);
            }
            var bg = el.Bg;
            // Go uses `width = top, bottom` in pixels when given, otherwise `xscale`
            float top = bg.XScale[0], bottom = bg.XScale[1];
            if (bg.Width[0] != 0 || bg.Width[1] != 0) {
                float w0 = sprite.Width > 0 ? sprite.Width : 1f;
                top = bg.Width[0] / w0;
                bottom = bg.Width[1] / w0;
            }
            var g = el.Parallax;
            var tex = cache.CachedTextureFor(sprite, stage.Sprites.PaletteFor(sprite));
            bool dirty = g.Texture != tex || g.TopScale != top || g.BottomScale != bottom;
            if (g.Texture != tex) { g.Texture = tex; g.SetMaterialDirty(); }
            g.TopScale = top;
            g.BottomScale = bottom;
            g.color = new Color(1f, 1f, 1f, alpha);
            g.material = IsAdditive(bg) ? AdditiveMaterial : Crisp ? NormalMaterial : null;
            var rt = g.rectTransform;
            rt.sizeDelta = new Vector2(width, height);
            // the axis of a parallax sprite is its top-centre in MUGEN, like a normal sprite
            rt.pivot = new Vector2(sprite.Width > 0 ? (float)sprite.X / sprite.Width : 0.5f,
                                   sprite.Height > 0 ? 1f - (float)sprite.Y / sprite.Height : 0.5f);
            rt.anchoredPosition = pos;
            if (dirty) g.SetVerticesDirty();      // size changes dirty the mesh by themselves
        }

        public void Dispose() {
            foreach (var el in elements)
                if (el.Container != null) UnityEngine.Object.Destroy(el.Container.gameObject);
            elements.Clear();
        }
    }

    /// <summary>
    /// A parallax background: the sprite is drawn as a stack of horizontal strips whose width
    /// grows linearly from <see cref="TopScale"/> at the top to <see cref="BottomScale"/> at the
    /// bottom. That is what MUGEN's `xscale` / `width` parallax does per raster line
    /// (Go `backGround.draw` passes `xras` into `anim.Draw`), and drawing it as strips keeps the
    /// perspective correct instead of the diagonal seam a single stretched quad would give.
    /// </summary>
    public class ParallaxGraphic : MaskableGraphic {
        public Texture2D Texture;
        public float TopScale = 1f;
        public float BottomScale = 1f;
        /// <summary>Strips used for the gradient; the sprite height is the natural choice.</summary>
        public int Strips = 64;

        public override Texture mainTexture => Texture != null ? Texture : s_WhiteTexture;

        protected override void OnPopulateMesh(VertexHelper vh) {
            vh.Clear();
            if (Texture == null) return;
            var r = GetPixelAdjustedRect();
            int n = Mathf.Clamp(Strips, 1, 256);
            var c = color;
            for (int i = 0; i < n; i++) {
                float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                float y0 = r.yMax - r.height * t0, y1 = r.yMax - r.height * t1;
                float s0 = Mathf.Lerp(TopScale, BottomScale, t0);
                float s1 = Mathf.Lerp(TopScale, BottomScale, t1);
                float cx = r.center.x;
                float hw0 = r.width * s0 / 2f, hw1 = r.width * s1 / 2f;
                int v = vh.currentVertCount;
                vh.AddVert(new Vector3(cx - hw0, y0), c, new Vector2(0f, 1f - t0));
                vh.AddVert(new Vector3(cx + hw0, y0), c, new Vector2(1f, 1f - t0));
                vh.AddVert(new Vector3(cx + hw1, y1), c, new Vector2(1f, 1f - t1));
                vh.AddVert(new Vector3(cx - hw1, y1), c, new Vector2(0f, 1f - t1));
                vh.AddTriangle(v, v + 1, v + 2);
                vh.AddTriangle(v + 2, v + 3, v);
            }
        }
    }
}
