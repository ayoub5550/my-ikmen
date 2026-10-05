using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IK.Settings;
using IK.UI;

namespace IK.Input {
    /// <summary>
    /// The on-screen control set: direction control on the left, six attack buttons plus
    /// START/PAUSE (and optional D/W and macros) on the right, built from a
    /// <see cref="ControlLayout"/> in reference units and re-anchored to
    /// <c>Screen.safeArea</c> whenever the safe area or resolution changes.
    /// Produces the raw (pre-SOCD, pre-assist) touch part of an <see cref="InputFrame"/>;
    /// <see cref="InputRouter"/> owns the 60 Hz sampling.
    /// </summary>
    public class TouchControls : MonoBehaviour {
        public static TouchControls Instance { get; private set; }

        public RectTransform Root { get; private set; }
        public DirectionPad Direction { get; private set; }
        public IReadOnlyList<HoldButton> Buttons => buttons;

        readonly List<HoldButton> buttons = new List<HoldButton>();
        readonly Dictionary<ControlId, HoldButton> byId = new Dictionary<ControlId, HoldButton>();
        readonly HashSet<ControlId> pressed = new HashSet<ControlId>();

        ControlLayout layout;
        GameSettings settings;
        Rect lastSafeArea;
        Vector2Int lastScreen;
        bool visible = true;

        void Awake() { Instance = this; }

        /// <summary>Rebuilds every control from the current settings + layout.</summary>
        public void Build(RectTransform parent, GameSettings s) {
            settings = s;
            layout = s.ActiveLayout();
            if (Root != null) DestroyImmediate(Root.gameObject);
            buttons.Clear(); byId.Clear(); pressed.Clear();

            Root = UIKit.Panel(parent, "TouchRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            ApplySafeArea(Screen.safeArea, Screen.width, Screen.height);

            var safeRef = SafeRef();
            // direction control
            var dirGo = new GameObject("Direction", typeof(RectTransform));
            dirGo.transform.SetParent(Root, false);
            var dirRt = dirGo.GetComponent<RectTransform>();
            dirRt.anchorMin = Vector2.zero; dirRt.anchorMax = Vector2.one;
            dirRt.offsetMin = dirRt.offsetMax = Vector2.zero;
            Direction = dirGo.AddComponent<DirectionPad>();
            Direction.mode = s.directionMode;
            Direction.deadZone = s.stickDeadZone;
            Direction.onSectorChanged = _ => Haptic(false);
            Direction.Build(dirRt, layout.Get(ControlId.Direction), safeRef, s.buttonSize, s.controlsOpacity);

            // attack buttons
            AddButton(ControlId.LP, "LP", Skin.Punch, safeRef);
            AddButton(ControlId.MP, "MP", Skin.Punch, safeRef);
            AddButton(ControlId.HP, "HP", Skin.Punch, safeRef);
            AddButton(ControlId.LK, "LK", Skin.Kick, safeRef);
            AddButton(ControlId.MK, "MK", Skin.Kick, safeRef);
            AddButton(ControlId.HK, "HK", Skin.Kick, safeRef);
            AddButton(ControlId.Start, "START", Skin.Plate, safeRef);
            AddButton(ControlId.Pause, "II", Skin.Plate, safeRef);
            if (s.showDW) { AddButton(ControlId.D, "D", Skin.Plate, safeRef); AddButton(ControlId.W, "W", Skin.Plate, safeRef); }
            if (s.macroButtons) { AddButton(ControlId.MacroXY, "x+y", Skin.Punch, safeRef); AddButton(ControlId.MacroAB, "a+b", Skin.Kick, safeRef); }

            SetVisible(visible);
        }

        void AddButton(ControlId id, string label, Color tint, Rect safeRef) {
            var placement = layout.Get(id);
            if (placement == null || !placement.visible) return;
            var r = layout.RectOf(placement, safeRef, settings.buttonSize);
            var rt = UIKit.Rect(Root, "Btn" + id, Vector2.zero, new Vector2(r.center.x - safeRef.xMin, r.center.y - safeRef.yMin),
                                new Vector2(r.width, r.height));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = id == ControlId.Start || id == ControlId.Pause ? Skin.Frame : Skin.Round;
            img.type = (id == ControlId.Start || id == ControlId.Pause) ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = id != ControlId.Start && id != ControlId.Pause;
            img.raycastTarget = true;
            float alpha = Mathf.Clamp01(settings.controlsOpacity * placement.opacity);
            img.color = new Color(tint.r, tint.g, tint.b, alpha);

            var text = UIKit.Text(rt, "label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(r.width, r.height),
                                  label, Mathf.RoundToInt(r.height * 0.34f), TextAnchor.MiddleCenter, Skin.Text);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;

            var hb = rt.gameObject.AddComponent<HoldButton>();
            hb.SetBaseColor(img.color);
            hb.slideToPress = () => settings.slideToPress;
            hb.onHold = held => OnHold(id, held);
            buttons.Add(hb);
            byId[id] = hb;
        }

        void OnHold(ControlId id, bool held) {
            if (held) { pressed.Add(id); Haptic(true); } else pressed.Remove(id);
        }

        void Haptic(bool strong) {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (settings == null || settings.haptics == Haptics.Off) return;
            if (!strong && settings.haptics != Haptics.Strong) return;
            Handheld.Vibrate();
#endif
        }

        /// <summary>Safe area expressed in the canvas reference units of the touch root.</summary>
        public Rect SafeRef() {
            float h = ControlLayout.RefHeight;
            float scale = Screen.height > 0 ? h / Screen.height : 1f;
            var safe = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return new Rect(0, 0, ControlLayout.RefWidth, h);
            return new Rect(0, 0, safe.width * scale, safe.height * scale);
        }

        public void ApplySafeArea(Rect safe, int width, int height) {
            if (Root == null || width <= 0 || height <= 0) return;
            Root.anchorMin = new Vector2(safe.xMin / width, safe.yMin / height);
            Root.anchorMax = new Vector2(safe.xMax / width, safe.yMax / height);
            Root.offsetMin = Root.offsetMax = Vector2.zero;
            lastSafeArea = safe;
            lastScreen = new Vector2Int(width, height);
        }

        public void SetVisible(bool v) {
            visible = v;
            if (Root != null) Root.gameObject.SetActive(v);
            if (!v) ReleaseAll();
        }

        public bool Visible => visible;

        /// <summary>Releases every control — pause, focus loss, hide. A fresh press is then required.</summary>
        public void ReleaseAll() {
            foreach (var b in buttons) b.Release();
            pressed.Clear();
            if (Direction != null) Direction.ReleaseAll();
        }

        void OnApplicationFocus(bool focus) { if (!focus) ReleaseAll(); }
        void OnApplicationPause(bool paused) { if (paused) ReleaseAll(); }

        void Update() {
            if (Root == null) return;
            if (lastSafeArea != Screen.safeArea || lastScreen != new Vector2Int(Screen.width, Screen.height))
                ApplySafeArea(Screen.safeArea, Screen.width, Screen.height);
        }

        /// <summary>Raw touch contribution for this tick (before SOCD and button assist).</summary>
        public InputFrame Sample() {
            var f = new InputFrame();
            if (!visible) return f;
            if (Direction != null) {
                f.U = Direction.U; f.D = Direction.D; f.L = Direction.L; f.R = Direction.R;
                f.analog = Direction.Analog;
            }
            f.x = Held(ControlId.LP) || Held(ControlId.MacroXY);
            f.y = Held(ControlId.MP) || Held(ControlId.MacroXY);
            f.z = Held(ControlId.HP);
            f.a = Held(ControlId.LK) || Held(ControlId.MacroAB);
            f.b = Held(ControlId.MK) || Held(ControlId.MacroAB);
            f.c = Held(ControlId.HK);
            f.s = Held(ControlId.Start);
            f.m = Held(ControlId.Pause);
            f.d = Held(ControlId.D);
            f.w = Held(ControlId.W);
            return f;
        }

        public bool Held(ControlId id) => byId.TryGetValue(id, out var b) && b.IsHeld;
        public HoldButton Get(ControlId id) => byId.TryGetValue(id, out var b) ? b : null;
    }
}
