using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using IK.Settings;

namespace IK.UI {
    /// <summary>
    /// Settings → Controls → Edit layout: drag any control, resize the selected one,
    /// snap to an 8-unit grid, see the safe area and the HUD no-go zone, reset, save or
    /// cancel. Positions are stored in reference units relative to the control's anchor
    /// corner, so a saved layout survives a different resolution or aspect.
    /// </summary>
    public class LayoutEditor : MonoBehaviour {
        public RectTransform Root { get; private set; }
        public Action onClose;
        public Action onSaved;

        public const float Grid = 8f;
        ControlLayout working;
        ControlId selected = ControlId.LP;
        readonly Dictionary<ControlId, RectTransform> handles = new Dictionary<ControlId, RectTransform>();
        RectTransform board;
        Text status;
        Slider sizeSlider, opacitySlider;
        GameSettings S => SettingsStore.Current;

        public ControlLayout Working => working;

        public void Build(RectTransform parent) {
            Root = UIKit.Panel(parent, "LayoutEditor", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                               new Color(0.03f, 0.04f, 0.07f, 0.92f));
            working = S.ActiveLayout().Clone();

            UIKit.Text(Root, "title", new Vector2(0.5f, 1f), new Vector2(0, -36), new Vector2(600, 48),
                       Loc.T("layout.title"), 30, TextAnchor.MiddleCenter, Skin.Highlight);
            status = UIKit.Text(Root, "status", new Vector2(0.5f, 1f), new Vector2(0, -76), new Vector2(900, 36),
                                Loc.T("layout.hint"), 20, TextAnchor.MiddleCenter, Skin.Text);

            board = UIKit.Panel(Root, "Board", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            // safe area + HUD no-go visualisation
            var safe = SafeRef();
            UIKit.Panel(board, "HudZone", new Vector2(0, 0.78f), new Vector2(1, 1), Vector2.zero, Vector2.zero,
                        new Color(0.9f, 0.2f, 0.2f, 0.12f));

            foreach (var c in working.controls) AddHandle(c, safe);

            sizeSlider = UIKit.Slider(Root, "size", new Vector2(0.5f, 0f), new Vector2(-150, 56), new Vector2(300, 34),
                                      0.6f, 1.6f, 1f, v => Resize(v));
            opacitySlider = UIKit.Slider(Root, "opacity", new Vector2(0.5f, 0f), new Vector2(220, 56), new Vector2(240, 34),
                                         0.2f, 1f, 1f, v => SetOpacity(v));
            UIKit.Button(Root, "reset", new Vector2(0f, 0f), new Vector2(150, 56), new Vector2(260, 64),
                         Loc.T("layout.resetDefault"), ResetDefault, 22);
            UIKit.Button(Root, "save", new Vector2(1f, 0f), new Vector2(-110, 56), new Vector2(180, 64),
                         Loc.T("common.save"), Save, 24);
            UIKit.Button(Root, "cancel", new Vector2(1f, 0f), new Vector2(-300, 56), new Vector2(180, 64),
                         Loc.T("common.cancel"), () => { onClose?.Invoke(); }, 24);
            Select(ControlId.LP);
        }

        public static Rect SafeRef() {
            float h = ControlLayout.RefHeight;
            float scale = Screen.height > 0 ? h / Screen.height : 1f;
            var safe = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return new Rect(0, 0, ControlLayout.RefWidth, h);
            return new Rect(0, 0, safe.width * scale, safe.height * scale);
        }

        void AddHandle(ControlPlacement c, Rect safe) {
            var r = working.RectOf(c, safe, S.buttonSize);
            var rt = UIKit.Rect(board, "H" + c.id, Vector2.zero, new Vector2(r.center.x, r.center.y),
                                new Vector2(r.width, r.height));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Skin.Round;
            img.color = new Color(1, 1, 1, c.visible ? 0.85f : 0.35f);
            img.raycastTarget = true;
            UIKit.Text(rt, "l", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(r.width, r.height),
                       c.id.ToString(), Mathf.RoundToInt(r.height * 0.26f));
            var drag = rt.gameObject.AddComponent<DragHandle>();
            drag.editor = this;
            drag.id = c.id;
            handles[c.id] = rt;
        }

        public void Select(ControlId id) {
            selected = id;
            foreach (var kv in handles) {
                var img = kv.Value.GetComponent<Image>();
                var c = img.color;
                img.color = new Color(kv.Key == id ? 1f : 0.75f, kv.Key == id ? 0.9f : 0.75f, kv.Key == id ? 0.55f : 0.75f, c.a);
            }
            var placement = working.Get(id);
            if (placement != null) {
                if (sizeSlider != null) sizeSlider.SetValueWithoutNotify(placement.size / 130f);
                if (opacitySlider != null) opacitySlider.SetValueWithoutNotify(placement.opacity);
            }
        }

        /// <summary>Moves a control by a screen-space delta, snapped to the grid.</summary>
        public void Move(ControlId id, Vector2 deltaRefUnits) {
            var c = working.Get(id);
            if (c == null) return;
            c.position = ControlLayout.Snap(c.position + deltaRefUnits, Grid);
            Refresh();
        }

        void Resize(float scale) {
            var c = working.Get(selected);
            if (c == null) return;
            c.size = Mathf.Max(ControlLayout.MinTarget, 130f * scale);
            Refresh();
        }

        void SetOpacity(float v) {
            var c = working.Get(selected);
            if (c == null) return;
            c.opacity = v;
            Refresh();
        }

        public void Refresh() {
            var safe = SafeRef();
            foreach (var kv in handles) {
                var c = working.Get(kv.Key);
                var r = working.RectOf(c, safe, S.buttonSize);
                kv.Value.anchoredPosition = new Vector2(r.center.x, r.center.y);
                kv.Value.sizeDelta = new Vector2(r.width, r.height);
                var img = kv.Value.GetComponent<Image>();
                img.color = new Color(img.color.r, img.color.g, img.color.b, c.opacity * (c.visible ? 0.85f : 0.35f));
            }
            string error;
            bool ok = working.Validate(safe, S.buttonSize, out error);
            UIKit.SetText(status, ok ? Loc.T("layout.hint") : Loc.T("layout.invalid") + ": " + error);
            if (status != null) status.color = ok ? Skin.Text : new Color(1f, 0.5f, 0.4f);
        }

        public bool CanSave(out string error) => working.Validate(SafeRef(), S.buttonSize, out error);

        public void Save() {
            string error;
            if (!CanSave(out error)) { Refresh(); return; }
            S.SaveLayout(working);
            SettingsStore.MarkChanged();
            SettingsStore.Flush();
            onSaved?.Invoke();
            onClose?.Invoke();
        }

        public void ResetDefault() {
            working = ControlLayout.Preset(S.layoutPreset);
            Refresh();
        }

        public void SetVisible(bool v) { if (Root != null) Root.gameObject.SetActive(v); }
        public bool Visible => Root != null && Root.gameObject.activeSelf;

        /// <summary>Pointer drag on a control handle.</summary>
        public class DragHandle : MonoBehaviour, IPointerDownHandler, IDragHandler {
            public LayoutEditor editor;
            public ControlId id;
            public void OnPointerDown(PointerEventData e) => editor.Select(id);
            public void OnDrag(PointerEventData e) {
                var canvas = GetComponentInParent<Canvas>();
                float scale = canvas != null ? canvas.scaleFactor : 1f;
                editor.Move(id, e.delta / Mathf.Max(0.0001f, scale));
            }
        }
    }
}
