using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using IK.Settings;

namespace IK.Input {
    /// <summary>
    /// The left-hand direction control in all three modes (§3.1): fixed 8-way D-pad,
    /// floating stick (origin where the thumb lands) and fixed stick. All three produce the
    /// same 8-way quantised digital direction through <see cref="InputLogic.Quantise8"/>,
    /// because MUGEN commands are digital.
    /// The whole left zone is a raycast target so a missed thumb still steers.
    /// </summary>
    public class DirectionPad : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler {
        public DirectionMode mode = DirectionMode.DPad;
        /// <summary>dev.7: 0 modern (glass disc, arrows that light up), 1 classic cross.</summary>
        public int style;
        readonly Image[] arrows = new Image[4];     // up, right, down, left
        float baseOpacity = 1f;
        public float deadZone = 0.25f;
        public System.Action<int> onSectorChanged;     // -1 = neutral, else sector index

        public bool U, D, L, R;
        public Vector2 Analog { get; private set; }

        RectTransform zone;        // full touch zone (left 45 %)
        RectTransform baseRt, knobRt;
        Image baseImg, knobImg;
        float radius = 110f;
        int activePointer = HoldButton.NoPointer;
        Vector2 origin;            // in zone-local coordinates
        int lastSector = -1;

        public void Build(RectTransform parent, ControlPlacement placement, Rect safeRef, float sizeScale, float opacity) {
            zone = UI.UIKit.Panel(parent, "DirectionZone", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                                  new Color(0, 0, 0, 0f));
            zone.GetComponent<Image>().raycastTarget = true;
            Rebuild(placement, safeRef, sizeScale, opacity);
        }

        /// <summary>(Re)creates the visuals for the current placement/mode.</summary>
        public void Rebuild(ControlPlacement placement, Rect safeRef, float sizeScale, float opacity) {
            if (baseRt != null) DestroyImmediate(baseRt.gameObject);
            float size = Mathf.Max(ControlLayout.MinTarget, placement.size * sizeScale);
            radius = size * 0.42f;

            // Left 45 % of the safe area is the direction zone (modes 1/2 only use it as origin).
            zone.anchorMin = new Vector2(0, 0);
            // dev.7: the top 22 % stays free for the HUD / menu buttons now that the controls are
            // drawn above the screens (a thumb never steers from up there)
            zone.anchorMax = new Vector2(0.45f, 0.78f);
            zone.offsetMin = Vector2.zero;
            zone.offsetMax = Vector2.zero;

            var centre = new Vector2(Mathf.Lerp(safeRef.xMin, safeRef.xMax, placement.anchor.x) + placement.position.x,
                                     Mathf.Lerp(safeRef.yMin, safeRef.yMax, placement.anchor.y) + placement.position.y);
            baseRt = UI.UIKit.Rect(zone, "DirBase", new Vector2(0, 0), centre - new Vector2(safeRef.xMin, safeRef.yMin), new Vector2(size, size));
            baseImg = baseRt.gameObject.AddComponent<Image>();
            baseImg.raycastTarget = false;
            baseImg.preserveAspect = true;
            baseOpacity = Mathf.Clamp01(opacity * placement.opacity);
            for (int i = 0; i < 4; i++) arrows[i] = null;
            if (style == 0) {
                baseImg.sprite = UI.Skin.Disc;
                baseImg.color = new Color(UI.Skin.Face.r, UI.Skin.Face.g, UI.Skin.Face.b, 0.5f * baseOpacity);
                var ring = Child(baseRt, "ring", UI.Skin.RingOf(0.03f), size, Vector2.zero);
                ring.color = new Color(1f, 1f, 1f, 0.38f * baseOpacity + 0.1f);
                var inner = Child(baseRt, "inner", UI.Skin.RingOf(0.05f), size * 0.5f, Vector2.zero);
                inner.color = new Color(1f, 1f, 1f, 0.12f * baseOpacity);
                for (int i = 0; i < 4; i++) {
                    float ang = -90f * i;                       // up, right, down, left
                    var dir = Quaternion.Euler(0, 0, ang) * Vector3.up;
                    var a = Child(baseRt, "arrow" + i, UI.Skin.Arrow, size * 0.17f, (Vector2)dir * size * 0.36f);
                    a.rectTransform.localRotation = Quaternion.Euler(0, 0, ang);
                    arrows[i] = a;
                }
                UpdateArrows();
            } else {
                baseImg.color = new Color(1, 1, 1, opacity * placement.opacity);
                baseImg.sprite = mode == DirectionMode.DPad ? CrossSprite() : UI.Skin.Ring();
            }

            float knobSize = size * (style == 0 ? 0.40f : 0.42f);
            knobRt = UI.UIKit.Rect(baseRt, "DirKnob", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(knobSize, knobSize));
            knobImg = knobRt.gameObject.AddComponent<Image>();
            knobImg.raycastTarget = false;
            knobImg.preserveAspect = true;
            if (style == 0) {
                knobImg.sprite = UI.Skin.Disc;
                knobImg.color = new Color(0.85f, 0.88f, 0.95f, 0.30f + 0.25f * baseOpacity);
                var kr = Child(knobRt, "ring", UI.Skin.RingOf(0.1f), knobSize, Vector2.zero);
                kr.color = new Color(1f, 1f, 1f, 0.9f);
                var ks = Child(knobRt, "sheen", UI.Skin.Sheen, knobSize, Vector2.zero);
                ks.color = new Color(1f, 1f, 1f, 0.35f);
            } else {
                knobImg.sprite = UI.Skin.Round;
                knobImg.color = new Color(1, 1, 1, Mathf.Min(1f, opacity * placement.opacity + 0.15f));
            }
            knobRt.gameObject.SetActive(mode != DirectionMode.FloatingStick);
            if (mode == DirectionMode.FloatingStick) baseRt.gameObject.SetActive(false);
        }

        static Image Child(RectTransform parent, string name, Sprite sprite, float size, Vector2 pos) {
            var rt = UI.UIKit.Rect(parent, name, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Lights the arrows of the held direction (modern style).</summary>
        void UpdateArrows() {
            for (int i = 0; i < 4; i++) {
                var a = arrows[i];
                if (a == null) continue;
                bool lit = i == 0 ? U : i == 1 ? R : i == 2 ? D : L;
                var c = lit ? UI.Skin.LP : new Color(1f, 1f, 1f, 0.55f * baseOpacity + 0.15f);
                if (a.color != c) a.color = c;
                var sc = lit ? new Vector3(1.25f, 1.25f, 1f) : Vector3.one;
                if (a.rectTransform.localScale != sc) a.rectTransform.localScale = sc;
            }
        }

        public void OnPointerDown(PointerEventData e) {
            if (activePointer != HoldButton.NoPointer) return;
            activePointer = e.pointerId;
            if (mode == DirectionMode.FloatingStick) {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(zone, e.position, e.pressEventCamera, out var local);
                baseRt.anchoredPosition = local - zone.rect.min;
                baseRt.gameObject.SetActive(true);
                knobRt.gameObject.SetActive(true);
            }
            origin = baseRt.anchoredPosition;
            Drag(e);
        }

        public void OnDrag(PointerEventData e) { if (e.pointerId == activePointer) Drag(e); }

        public void OnPointerUp(PointerEventData e) {
            if (e.pointerId != activePointer) return;
            activePointer = HoldButton.NoPointer;
            ClearDirection();
            if (mode == DirectionMode.FloatingStick && baseRt != null) baseRt.gameObject.SetActive(false);
            if (knobRt != null) knobRt.anchoredPosition = Vector2.zero;
        }

        void Drag(PointerEventData e) {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(zone, e.position, e.pressEventCamera, out var local);
            Vector2 delta = (local - zone.rect.min) - origin;
            Analog = delta / Mathf.Max(1f, radius);
            if (Analog.magnitude > 1f) Analog = Analog.normalized;
            InputLogic.Quantise8(Analog, deadZone, out U, out D, out L, out R);
            int sector = (U || D || L || R) ? InputLogic.SectorIndex(Mathf.Atan2(Analog.y, Analog.x) * Mathf.Rad2Deg) : -1;
            if (sector != lastSector) {
                lastSector = sector;
                onSectorChanged?.Invoke(sector);
            }
            if (knobRt != null) knobRt.anchoredPosition = Analog * radius * 0.6f;
            UpdateArrows();
        }

        /// <summary>Tests / screenshots: shows and outputs a direction as if a thumb held it.</summary>
        public void Simulate(Vector2 analog) {
            if (analog.sqrMagnitude < 1e-6f) { ClearDirection(); return; }
            Analog = analog.magnitude > 1f ? analog.normalized : analog;
            InputLogic.Quantise8(Analog, deadZone, out U, out D, out L, out R);
            if (knobRt != null) knobRt.anchoredPosition = Analog * radius * 0.6f;
            UpdateArrows();
        }

        public void ClearDirection() {
            U = D = L = R = false;
            Analog = Vector2.zero;
            lastSector = -1;
            if (knobRt != null) knobRt.anchoredPosition = Vector2.zero;
            UpdateArrows();
        }

        public void ReleaseAll() {
            activePointer = HoldButton.NoPointer;
            ClearDirection();
            if (mode == DirectionMode.FloatingStick && baseRt != null) baseRt.gameObject.SetActive(false);
        }

        /// <summary>Simple 8-way cross plate drawn in code (no imported art to strip).</summary>
        static Sprite cross;
        public static Sprite CrossSprite() {
            if (cross != null) return cross;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float c = size * 0.5f, arm = size * 0.19f, len = size * 0.47f;
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    float dx = Mathf.Abs(x + 0.5f - c), dy = Mathf.Abs(y + 0.5f - c);
                    bool inCross = (dx < arm && dy < len) || (dy < arm && dx < len);
                    bool rim = inCross && (Mathf.Abs(dx - arm) < 3f || Mathf.Abs(dy - arm) < 3f ||
                                           Mathf.Abs(dx - len) < 3f || Mathf.Abs(dy - len) < 3f);
                    if (!inCross) { px[y * size + x] = new Color32(0, 0, 0, 0); continue; }
                    px[y * size + x] = rim ? new Color32(255, 214, 150, 255) : new Color32(60, 64, 78, 235);
                }
            }
            tex.SetPixels32(px); tex.Apply();
            cross = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
            cross.name = "IKCross";
            return cross;
        }
    }
}
