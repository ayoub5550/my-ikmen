using UnityEngine;
using UnityEngine.UI;

namespace IK.UI {
    /// <summary>
    /// The visual identity of the on-screen UI: a generated arcade frame (bevelled plate
    /// with a bright rim) plus round button plates, so nothing depends on an imported
    /// sprite that could be stripped from the player. Colours are picked to sit on top of
    /// the MUGEN screenpack art without fighting the lifebars.
    /// </summary>
    public static class Skin {
        public static readonly Color Accent = new Color32(230, 126, 34, 255);     // orange
        public static readonly Color Highlight = new Color32(255, 214, 150, 255);
        public static readonly Color Text = new Color32(255, 243, 224, 255);
        public static readonly Color Plate = new Color32(28, 30, 38, 255);
        public static readonly Color Punch = new Color32(214, 93, 60, 255);
        public static readonly Color Kick = new Color32(66, 133, 190, 255);

        static Sprite frame, round;

        /// <summary>9-sliced bevelled plate used by menu rows and buttons.</summary>
        public static Sprite Frame {
            get {
                if (frame != null) return frame;
                const int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++) {
                    for (int x = 0; x < size; x++) {
                        int edge = Mathf.Min(x, y, size - 1 - x, size - 1 - y);
                        Color c = Plate;
                        if (edge == 0) c = new Color32(8, 8, 12, 255);
                        else if (edge <= 2) c = (y > size - 5 || x < 3) ? Highlight : (Color)Accent * 0.55f;
                        else if (edge == 3) c = new Color32(8, 8, 12, 255);
                        else c = Plate * (((x + y) % 4 == 0) ? 1.15f : 1f);
                        c.a = 1f;
                        px[y * size + x] = c;
                    }
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                frame = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100, 0,
                                      SpriteMeshType.FullRect, new Vector4(5, 5, 5, 5));
                frame.name = "IKFrame";
                return frame;
            }
        }

        /// <summary>Round arcade button face with a rim; used for the attack buttons and the stick knob.</summary>
        public static Sprite Round {
            get {
                if (round != null) return round;
                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[size * size];
                float c0 = size * 0.5f;
                for (int y = 0; y < size; y++) {
                    for (int x = 0; x < size; x++) {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c0, c0)) / c0;
                        Color col;
                        float alpha;
                        if (d > 1f) { col = Color.clear; alpha = 0f; }
                        else if (d > 0.90f) { col = Color.white; alpha = Mathf.Clamp01((1f - d) * size * 0.25f); }
                        else if (d > 0.82f) { col = new Color(1f, 1f, 1f, 1f); alpha = 1f; }
                        else {
                            // soft top-lit dome
                            float lit = Mathf.Clamp01(0.55f + 0.45f * ((y - c0) / c0) * 0.8f - d * 0.25f);
                            col = new Color(lit, lit, lit, 1f);
                            alpha = 1f;
                        }
                        col.a = alpha;
                        px[y * size + x] = col;
                    }
                }
                tex.SetPixels32(px); tex.Apply();
                round = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
                round.name = "IKRound";
                return round;
            }
        }

        /// <summary>Ring sprite for the floating stick base.</summary>
        public static Sprite Ring(float innerFrac = 0.78f) {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float c0 = size * 0.5f;
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c0, c0)) / c0;
                    bool inside = d <= 0.98f && d >= innerFrac;
                    float edge = Mathf.Clamp01((0.98f - d) * size * 0.2f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(inside ? 255 * edge : 0));
                }
            }
            tex.SetPixels32(px); tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
            s.name = "IKRing";
            return s;
        }

        // ------------------------------------------------------------------ dev.7 modern controls

        /// <summary>Per-button colours of the modern style (punch = warm, kick = cool).</summary>
        public static readonly Color LP = new Color32(255, 189, 74, 255);
        public static readonly Color MP = new Color32(255, 128, 56, 255);
        public static readonly Color HP = new Color32(255, 64, 92, 255);
        public static readonly Color LK = new Color32(84, 220, 255, 255);
        public static readonly Color MK = new Color32(64, 145, 255, 255);
        public static readonly Color HK = new Color32(140, 104, 255, 255);
        public static readonly Color Neutral = new Color32(236, 240, 248, 255);
        public static readonly Color Face = new Color32(14, 16, 24, 255);

        static Font labelFont;
        /// <summary>Rubik Bold (SIL OFL) for button labels and numbers; Arabic capable.</summary>
        public static Font LabelFont {
            get {
                if (labelFont == null) labelFont = Resources.Load<Font>("fonts/Rubik-Bold");
                return labelFont != null ? labelFont : UIKit.Font;
            }
        }

        static Sprite disc, glow, sheen, pill, pillRing, arrow;
        static readonly System.Collections.Generic.Dictionary<int, Sprite> rings = new System.Collections.Generic.Dictionary<int, Sprite>();

        delegate float Shape(float x, float y);      // returns (alpha, 0..1) at a texel centre in [-1,1]^2

        static Sprite Make(string name, int size, Shape shape, Vector4 border = default) {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = name };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(shape(u, v));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            var spr = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect, border);
            spr.name = name;
            return spr;
        }

        /// <summary>Anti-aliased coverage of a signed distance (in texels) — 1 inside, 0 outside.</summary>
        static float Cover(float sdTexels) => Mathf.Clamp01(0.5f - sdTexels);

        const int Hi = 256;
        static float Px => 2f / Hi;                 // one texel in [-1,1] units

        /// <summary>Filled anti-aliased circle (white; tinted by the Image colour).</summary>
        public static Sprite Disc => disc != null ? disc : disc = Make("IKDisc", Hi, (x, y) => {
            float d = Mathf.Sqrt(x * x + y * y);
            return Cover((d - 0.985f) / Px);
        });

        /// <summary>Anti-aliased ring; thickness as a fraction of the radius.</summary>
        public static Sprite RingOf(float thickness) {
            int key = Mathf.RoundToInt(thickness * 1000);
            if (rings.TryGetValue(key, out var r) && r != null) return r;
            float outer = 0.985f, inner = outer - thickness;
            r = Make("IKRing" + key, Hi, (x, y) => {
                float d = Mathf.Sqrt(x * x + y * y);
                return Mathf.Min(Cover((d - outer) / Px), Cover((inner - d) / Px));
            });
            rings[key] = r;
            return r;
        }

        /// <summary>Soft radial glow for pressed buttons.</summary>
        public static Sprite Glow => glow != null ? glow : glow = Make("IKGlow", 128, (x, y) => {
            float d = Mathf.Sqrt(x * x + y * y);
            float t = Mathf.Clamp01(1f - d);
            return t * t * 0.9f;
        });

        /// <summary>Glass highlight: a soft ellipse in the upper half of a disc.</summary>
        public static Sprite Sheen => sheen != null ? sheen : sheen = Make("IKSheen", Hi, (x, y) => {
            float d = Mathf.Sqrt(x * x + y * y);
            float inside = Cover((d - 0.86f) / Px);
            float ex = x / 0.72f, ey = (y - 0.38f) / 0.42f;
            float e = Mathf.Clamp01(1f - (ex * ex + ey * ey));
            return inside * e * 0.85f;
        });

        static float RoundRectSd(float x, float y, float hw, float hh, float r) {
            float qx = Mathf.Abs(x) - hw + r, qy = Mathf.Abs(y) - hh + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>9-sliced capsule plate (START, pause, menu chips).</summary>
        public static Sprite Pill => pill != null ? pill : pill = Make("IKPill", 128, (x, y) =>
            Cover(RoundRectSd(x, y, 0.985f, 0.985f, 0.985f) / (2f / 128f)), new Vector4(62, 62, 62, 62));

        /// <summary>9-sliced capsule outline.</summary>
        public static Sprite PillRing => pillRing != null ? pillRing : pillRing = Make("IKPillRing", 128, (x, y) => {
            float sd = RoundRectSd(x, y, 0.985f, 0.985f, 0.985f) / (2f / 128f);
            return Mathf.Min(Cover(sd), Cover(-sd - 6f));
        }, new Vector4(62, 62, 62, 62));

        /// <summary>Rounded chevron pointing up (direction pad arrows).</summary>
        public static Sprite Arrow => arrow != null ? arrow : arrow = Make("IKArrow", 128, (x, y) => {
            // isoceles triangle apex (0,0.62), base y=-0.42, half width 0.72, slightly rounded
            float px2 = 2f / 128f;
            float d1 = ((y - 0.62f) * 0.72f + Mathf.Abs(x) * 1.04f) / Mathf.Sqrt(0.72f * 0.72f + 1.04f * 1.04f);
            float d2 = -0.42f - y;
            float sd = Mathf.Max(d1, d2) + 0.06f;
            return Cover(sd / px2);
        });

        public static void Apply(Image image, float opacity = 1f) {
            image.sprite = Frame;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            image.color = new Color(1, 1, 1, opacity);
        }
    }
}
