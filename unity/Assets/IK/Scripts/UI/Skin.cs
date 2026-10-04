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

        public static void Apply(Image image, float opacity = 1f) {
            image.sprite = Frame;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            image.color = new Color(1, 1, 1, opacity);
        }
    }
}
