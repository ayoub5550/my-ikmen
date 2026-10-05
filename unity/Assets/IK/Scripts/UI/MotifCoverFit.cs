using UnityEngine;

namespace IK.UI {
    /// <summary>
    /// dev.8: scales a localcoord-sized rect so that it covers its parent (no black bars on
    /// 20:9 phones). Used for the motif background layers; the menus stay in the height-fitted
    /// motif area so nothing interactive is cropped.
    /// </summary>
    public class MotifCoverFit : MonoBehaviour {
        public float Width = 1280f, Height = 720f;
        RectTransform rt, parent;
        Vector2 lastSize;

        void Awake() { rt = (RectTransform)transform; parent = rt.parent as RectTransform; Apply(); }
        void LateUpdate() { Apply(); }

        public void Set(float w, float h) { Width = w; Height = h; lastSize = new Vector2(-1f, -1f); Apply(); }

        public void Apply() {
            if (rt == null || parent == null) return;
            var size = parent.rect.size;
            if (size == lastSize && size.x > 0f) return;
            lastSize = size;
            float s = size.x > 0f && size.y > 0f
                ? Mathf.Max(size.x / Mathf.Max(1f, Width), size.y / Mathf.Max(1f, Height))
                : 720f / Mathf.Max(1f, Height);
            rt.localScale = new Vector3(s, s, 1f);
        }
    }
}
