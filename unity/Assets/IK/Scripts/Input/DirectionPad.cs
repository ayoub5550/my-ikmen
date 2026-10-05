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
            zone.anchorMax = new Vector2(0.45f, 1f);
            zone.offsetMin = Vector2.zero;
            zone.offsetMax = Vector2.zero;

            var centre = new Vector2(Mathf.Lerp(safeRef.xMin, safeRef.xMax, placement.anchor.x) + placement.position.x,
                                     Mathf.Lerp(safeRef.yMin, safeRef.yMax, placement.anchor.y) + placement.position.y);
            baseRt = UI.UIKit.Rect(zone, "DirBase", new Vector2(0, 0), centre - new Vector2(safeRef.xMin, safeRef.yMin), new Vector2(size, size));
            baseImg = baseRt.gameObject.AddComponent<Image>();
            baseImg.raycastTarget = false;
            baseImg.color = new Color(1, 1, 1, opacity * placement.opacity);
            baseImg.sprite = mode == DirectionMode.DPad ? CrossSprite() : UI.Skin.Ring();
            baseImg.preserveAspect = true;

            knobRt = UI.UIKit.Rect(baseRt, "DirKnob", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.42f, size * 0.42f));
            knobImg = knobRt.gameObject.AddComponent<Image>();
            knobImg.sprite = UI.Skin.Round;
            knobImg.raycastTarget = false;
            knobImg.color = new Color(1, 1, 1, Mathf.Min(1f, opacity * placement.opacity + 0.15f));
            knobRt.gameObject.SetActive(mode != DirectionMode.FloatingStick);
            if (mode == DirectionMode.FloatingStick) baseRt.gameObject.SetActive(false);
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
        }

        public void ClearDirection() {
            U = D = L = R = false;
            Analog = Vector2.zero;
            lastSector = -1;
            if (knobRt != null) knobRt.anchoredPosition = Vector2.zero;
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
