using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace IK.Input {
    /// <summary>
    /// A true hold button (charge moves need holds, not taps).
    /// Rules carried over from LibreQuake v0.2.0 and extended for a fighting game:
    /// the finger that pressed it is the only one that can release it, a second finger
    /// cannot steal it, sliding off keeps it held, and <c>OnDisable</c> releases.
    /// Slide-to-press: a finger already pressing a neighbour may also press this button
    /// when it slides onto it (§3.2), which is how arcade players plink x+y with one thumb.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class HoldButton : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler {

        public System.Action<bool> onHold;
        /// <summary>Set by TouchControls: may this button be pressed by a sliding finger?</summary>
        public System.Func<bool> slideToPress;

        public const int NoPointer = int.MinValue;
        public int pointerId = NoPointer;
        public bool IsHeld { get; private set; }
        /// <summary>dev.7 modern look; when set it draws the pressed state instead of the tint.</summary>
        public ButtonLook look;

        Image image;
        Color baseColor;
        Outline outline;
        Text label;
        Color labelBase = Color.white;

        void Awake() {
            image = GetComponent<Image>();
            baseColor = image.color;
            outline = gameObject.AddComponent<Outline>();
            outline.effectColor = UI.Skin.Highlight;
            outline.effectDistance = new Vector2(4, -4);
            outline.enabled = false;
            label = GetComponentInChildren<Text>();
            if (label != null) labelBase = label.color;
        }

        public void OnPointerDown(PointerEventData e) => Press(e != null ? e.pointerId : NoPointer);

        public void OnPointerUp(PointerEventData e) {
            if (e == null || e.pointerId == pointerId) Release();
        }

        public void OnPointerEnter(PointerEventData e) {
            if (e == null || IsHeld) return;
            bool dragging = e.pointerPress != null || e.dragging || e.pressPosition != Vector2.zero;
            if (!dragging) return;
            if (slideToPress != null && !slideToPress()) return;
            Press(e.pointerId);                       // the neighbour keeps its own press
        }

        /// <summary>Sliding off a held button keeps it held (deliberate, matches LibreQuake).</summary>
        public void OnPointerExit(PointerEventData e) { }

        public void Press(int id) {
            if (IsHeld) return;                       // a second finger must not steal it
            IsHeld = true;
            pointerId = id;
            if (look != null) { look.SetPressed(true); onHold?.Invoke(true); return; }
            if (image != null) image.color = PressedColor(baseColor);
            if (outline != null) outline.enabled = true;
            if (label != null) label.color = UI.Skin.Highlight;
            onHold?.Invoke(true);
        }

        public void Release() {
            if (!IsHeld) return;
            IsHeld = false;
            pointerId = NoPointer;
            if (look != null) { look.SetPressed(false); onHold?.Invoke(false); return; }
            if (image != null) image.color = baseColor;
            if (outline != null) outline.enabled = false;
            if (label != null) label.color = labelBase;
            onHold?.Invoke(false);
        }

        public static Color PressedColor(Color c) =>
            new Color(Mathf.Min(1f, c.r * 1.6f + 0.25f), Mathf.Min(1f, c.g * 1.5f + 0.2f),
                      Mathf.Min(1f, c.b * 1.2f + 0.1f), Mathf.Min(1f, c.a + 0.35f));

        public void SetHeld(bool v) { if (v) Press(NoPointer); else Release(); }

        /// <summary>Re-reads the idle colour after a skin/opacity change.</summary>
        public void SetBaseColor(Color c) {
            baseColor = c;
            if (look != null) return;
            if (!IsHeld && image != null) image.color = c;
        }

        void OnDisable() => Release();
    }
}
