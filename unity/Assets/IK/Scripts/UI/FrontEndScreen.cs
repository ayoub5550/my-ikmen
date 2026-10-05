using System;
using UnityEngine;
using IK.Input;

namespace IK.UI {
    /// <summary>Menu intents decoded from an <see cref="InputFrame"/> (D-pad / buttons) or the Android back key.</summary>
    public enum MenuKey { None, Up, Down, Left, Right, Confirm, Cancel }

    /// <summary>
    /// Base of the motif-driven front-end screens (title, select, VS, victory, continue,
    /// results, credits): a <see cref="MotifView"/>, a fixed 60 Hz tick (MUGEN timing, also
    /// used by the motif background) and menu-key decoding from the input router frames with
    /// press edges and auto-repeat. Every screen is also fully usable by tapping.
    /// </summary>
    public abstract class FrontEndScreen : MonoBehaviour {
        public MotifView View { get; protected set; }
        public RectTransform Root => View != null ? View.Root : null;
        public bool Visible => Root != null && Root.gameObject.activeSelf;
        /// <summary>Ticks since the screen was shown.</summary>
        public int Ticks { get; private set; }

        float acc;
        InputFrame prev;
        MenuKey held = MenuKey.None;
        int heldTicks;

        public abstract void Build(RectTransform parent);

        UnityEngine.UI.Image fade;
        /// <summary>Fade-in length in ticks (motif `fadein.time`).</summary>
        protected int FadeInTicks;

        /// <summary>Creates the motif view (and its fade overlay) for this screen.</summary>
        string bgPrefix;
        bool bgBuilt;

        protected void CreateView(RectTransform parent, string name, string bgPrefix) {
            View = new MotifView(parent, name);
            this.bgPrefix = bgPrefix;
            EnsureBackground();
            fade = UIKit.Image(View.Root, "fade", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000, 4000), null, new Color(0, 0, 0, 0));
        }

        /// <summary>Builds the motif background once system.sff has been decoded (MotifAssets.Ready).</summary>
        protected void EnsureBackground() {
            if (bgBuilt || View == null || string.IsNullOrEmpty(bgPrefix) || !MotifAssets.SpritesReady) return;
            bgBuilt = true;
            View.SetBackground(bgPrefix);
        }

        /// <summary>Keeps the fade overlay above everything built after <see cref="CreateView"/>.</summary>
        protected void FinishBuild() { if (fade != null) fade.transform.SetAsLastSibling(); }

        void UpdateFade() {
            if (fade == null) return;
            float a = FadeInTicks > 0 ? Mathf.Clamp01(1f - Ticks / (float)FadeInTicks) : 0f;
            fade.color = new Color(0, 0, 0, a);
            fade.enabled = a > 0.001f;
        }

        public virtual void SetVisible(bool v) {
            if (Root == null) return;
            bool was = Root.gameObject.activeSelf;
            Root.gameObject.SetActive(v);
            if (v && !was) {
                Ticks = 0; acc = 0f; held = MenuKey.None;
                if (View != null) View.ResetBackground();
                OnShow();
                UpdateFade();
            }
        }

        protected virtual void OnShow() { }
        protected virtual void OnTick() { }
        public virtual void OnMenuKey(MenuKey key) { }

        /// <summary>Runs one 60 Hz tick now (rendered tests step screens deterministically).</summary>
        public void Step(int ticks = 1) {
            EnsureBackground();
            for (int i = 0; i < ticks; i++) {
                if (View != null) View.Tick();
                Ticks++;
                OnTick();
                UpdateFade();
            }
        }

        protected virtual void Update() {
            if (!Visible) return;
            acc += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            const float dt = 1f / 60f;
            while (acc >= dt) { acc -= dt; Step(); }
        }

        /// <summary>Router frame (60 Hz) while this screen is current.</summary>
        public void Feed(InputFrame f) {
            var key = Decode(f);
            var edge = Decode(f, prev);
            prev = f;
            if (edge != MenuKey.None) { held = edge; heldTicks = 0; OnMenuKey(edge); return; }
            if (key == held && (key == MenuKey.Up || key == MenuKey.Down || key == MenuKey.Left || key == MenuKey.Right)) {
                heldTicks++;
                // auto-repeat: after 20 ticks, every 6 ticks (Ikemen menu feel)
                if (heldTicks >= 20 && (heldTicks - 20) % 6 == 0) OnMenuKey(key);
            } else if (key == MenuKey.None) held = MenuKey.None;
        }

        /// <summary>Intent held in this frame (directions first, then buttons).</summary>
        public static MenuKey Decode(InputFrame f) {
            if (f.U) return MenuKey.Up;
            if (f.D) return MenuKey.Down;
            if (f.L) return MenuKey.Left;
            if (f.R) return MenuKey.Right;
            if (f.m || f.w) return MenuKey.Cancel;
            if (f.a || f.b || f.c || f.x || f.y || f.z || f.s) return MenuKey.Confirm;
            return MenuKey.None;
        }

        /// <summary>Intent newly pressed in this frame compared to the previous one.</summary>
        public static MenuKey Decode(InputFrame f, InputFrame p) {
            if (f.U && !p.U) return MenuKey.Up;
            if (f.D && !p.D) return MenuKey.Down;
            if (f.L && !p.L) return MenuKey.Left;
            if (f.R && !p.R) return MenuKey.Right;
            if ((f.m && !p.m) || (f.w && !p.w)) return MenuKey.Cancel;
            bool now = f.a || f.b || f.c || f.x || f.y || f.z || f.s;
            bool before = p.a || p.b || p.c || p.x || p.y || p.z || p.s;
            if (now && !before) return MenuKey.Confirm;
            return MenuKey.None;
        }

        protected virtual void OnDestroy() { if (View != null) View.Dispose(); }
    }
}
