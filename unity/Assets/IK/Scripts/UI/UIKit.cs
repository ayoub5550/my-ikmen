using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace IK.UI {
    /// <summary>
    /// Builds uGUI from code (no prefabs): one Canvas, Scale With Screen Size, reference
    /// 1280x720, match = height — the LibreQuake v0.2.0 arrangement that survived every
    /// aspect ratio we tested. Labels go through <see cref="Loc"/> so Arabic is shaped.
    /// </summary>
    public static class UIKit {
        public static readonly Vector2 Reference = new Vector2(1280, 720);

        static Font font;
        public static Font Font {
            get {
                if (font != null) return font;
                // Amiri (OFL) ships in Resources: it covers Arabic presentation forms + Latin.
                font = Resources.Load<Font>("fonts/Amiri-Regular");
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "DejaVu Sans" }, 24);
                return font;
            }
        }

        public static Canvas CreateCanvas(string name, int sortOrder = 0) {
            var go = new GameObject(name);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sortOrder;
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = Reference;
            sc.matchWidthOrHeight = 1f;           // match height: landscape stays consistent
            go.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
            return c;
        }

        public static void EnsureEventSystem() {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        public static RectTransform Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                          Vector2 offsetMin, Vector2 offsetMax, Color? color = null) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            if (color.HasValue) {
                var img = go.AddComponent<Image>();
                img.color = color.Value;
                img.raycastTarget = false;
            }
            return rt;
        }

        /// <summary>Rect anchored to a corner, positioned by its centre (pivot 0.5).</summary>
        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size,
                                  Sprite sprite = null, Color? color = null) {
            var rt = Rect(parent, name, anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color ?? Color.white;
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size,
                                string text, int fontSize, TextAnchor align = TextAnchor.MiddleCenter, Color? color = null) {
            var rt = Rect(parent, name, anchor, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = fontSize;
            t.text = Loc.Shape(text);
            t.alignment = align;
            t.color = color ?? Skin.Text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.9f);
            sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        /// <summary>Sets a label's text, shaping Arabic first. Use everywhere instead of Text.text.</summary>
        public static void SetText(Text label, string text) {
            if (label != null) label.text = Loc.Shape(text);
        }

        public static Button Button(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size,
                                    string label, System.Action onClick, int fontSize = 28) {
            var rt = Rect(parent, name, anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            Skin.Apply(img);
            img.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.1f, 0.95f);
            colors.pressedColor = new Color(0.65f, 0.55f, 0.4f);
            colors.selectedColor = new Color(1.1f, 1.05f, 0.95f);
            b.colors = colors;
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var t = Text(rt, "label", new Vector2(0.5f, 0.5f), Vector2.zero, size, label, fontSize);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = new Vector2(6, 4);
            t.rectTransform.offsetMax = new Vector2(-6, -4);
            return b;
        }

        public static Slider Slider(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size,
                                    float min, float max, float value, System.Action<float> onChange) {
            var rt = Rect(parent, name, anchor, pos, size);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.10f, 0.9f);
            var s = rt.gameObject.AddComponent<Slider>();
            var fillArea = Panel(rt, "fillarea", Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
            var fill = Panel(fillArea, "fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Skin.Accent);
            s.fillRect = fill;
            var handleArea = Panel(rt, "handlearea", Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0));
            var handle = Panel(handleArea, "handle", new Vector2(0, 0), new Vector2(0, 1), new Vector2(-14, 0), new Vector2(14, 0), Skin.Highlight);
            handle.GetComponent<Image>().raycastTarget = true;
            s.handleRect = handle;
            s.targetGraphic = handle.GetComponent<Image>();
            s.minValue = min; s.maxValue = max; s.value = value;
            if (onChange != null) s.onValueChanged.AddListener(v => onChange(v));
            return s;
        }
    }
}
