using UnityEngine;
using UnityEngine.UI;
using IK.UI;

namespace IK.Input {
    /// <summary>
    /// dev.7 modern look of an on-screen button: a dark glass face (the raycast target, owned
    /// by <see cref="HoldButton"/>), a coloured fill that lights up when pressed, a coloured
    /// anti-aliased ring, a soft glow, a glass sheen and a bold label. Pressing scales the
    /// button down a little and lights fill + glow, so a press reads at a glance even under a
    /// thumb. Everything is generated in code (no imported art that IL2CPP could strip).
    /// </summary>
    public class ButtonLook : MonoBehaviour {
        public Image Face, Fill, Ring, GlowImg, SheenImg;
        public Text Label;
        public Color Tint = Color.white;
        public float Opacity = 1f;
        bool pressed;

        const float IdleFill = 0.16f, PressedFill = 0.92f;

        /// <summary>Builds the layers on <paramref name="rt"/> (which already has the face Image).</summary>
        public static ButtonLook Build(RectTransform rt, Image face, string label, Color tint, float opacity, bool pill, float labelSize) {
            var look = rt.gameObject.AddComponent<ButtonLook>();
            look.Face = face;
            look.Tint = tint;
            look.Opacity = opacity;
            face.sprite = pill ? Skin.Pill : Skin.Disc;
            face.type = pill ? Image.Type.Sliced : Image.Type.Simple;
            face.preserveAspect = !pill;
            if (pill) face.pixelsPerUnitMultiplier = 128f / Mathf.Max(8f, rt.sizeDelta.y) * 100f / 100f;

            look.GlowImg = Layer(rt, "glow", Skin.Glow, 1.45f, false);
            look.Fill = Layer(rt, "fill", pill ? Skin.Pill : Skin.Disc, 1f, pill);
            look.SheenImg = Layer(rt, "sheen", Skin.Sheen, 1f, false);
            look.Ring = Layer(rt, "ring", pill ? Skin.PillRing : Skin.RingOf(0.085f), 1f, pill);
            if (pill) {
                look.Fill.pixelsPerUnitMultiplier = face.pixelsPerUnitMultiplier;
                look.Ring.pixelsPerUnitMultiplier = face.pixelsPerUnitMultiplier;
                look.SheenImg.enabled = false;
            }

            var t = UIKit.Text(rt, "label", new Vector2(0.5f, 0.5f), Vector2.zero, rt.sizeDelta, label,
                               Mathf.RoundToInt(labelSize), TextAnchor.MiddleCenter, Color.white);
            t.font = Skin.LabelFont;
            t.fontStyle = FontStyle.Normal;
            t.raycastTarget = false;
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            sh.effectDistance = new Vector2(0f, -2f);
            look.Label = t;
            look.Apply();
            return look;
        }

        static Image Layer(RectTransform parent, string name, Sprite sprite, float scale, bool sliced) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f - scale * 0.5f, 0.5f - scale * 0.5f);
            rt.anchorMax = new Vector2(0.5f + scale * 0.5f, 0.5f + scale * 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = !sliced;
            img.raycastTarget = false;
            return img;
        }

        public void SetPressed(bool on) {
            if (pressed == on) return;
            pressed = on;
            Apply();
        }

        public void SetOpacity(float o) { Opacity = o; Apply(); }

        void Apply() {
            float o = Mathf.Clamp01(Opacity);
            if (Face != null) Face.color = new Color(Skin.Face.r, Skin.Face.g, Skin.Face.b, Mathf.Clamp01(0.62f * o + (pressed ? 0.2f : 0f)));
            if (Fill != null) Fill.color = new Color(Tint.r, Tint.g, Tint.b, (pressed ? PressedFill : IdleFill) * Mathf.Max(o, 0.6f));
            if (Ring != null) Ring.color = new Color(Mathf.Lerp(Tint.r, 1f, pressed ? 0.45f : 0f), Mathf.Lerp(Tint.g, 1f, pressed ? 0.45f : 0f),
                                                    Mathf.Lerp(Tint.b, 1f, pressed ? 0.45f : 0f), Mathf.Clamp01(0.95f * o + 0.05f));
            if (GlowImg != null) { GlowImg.color = new Color(Tint.r, Tint.g, Tint.b, 0.75f); GlowImg.enabled = pressed; }
            if (SheenImg != null) SheenImg.color = new Color(1f, 1f, 1f, (pressed ? 0.10f : 0.22f) * o);
            if (Label != null) Label.color = pressed ? new Color(0.06f, 0.06f, 0.09f, 1f) : new Color(1f, 1f, 1f, Mathf.Clamp01(0.4f + o));
            transform.localScale = pressed ? new Vector3(0.93f, 0.93f, 1f) : Vector3.one;
        }
    }
}
