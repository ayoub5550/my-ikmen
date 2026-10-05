using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// The title screen of the screenpack (`[Title Info]` + `[TitleBGdef]` of system.def):
    /// the animated sky / clouds / logo background and the main menu, right-aligned at
    /// `menu.pos` with `menu.item.spacing`, the active item in `menu.item.active.font`, and the
    /// cursor sounds of system.snd. Items are tappable; D-pad / buttons also drive the cursor.
    /// English labels use the motif's own names and bitmap font; Arabic labels use the UI font.
    /// </summary>
    public class TitleScreen : FrontEndScreen {
        public class Item {
            public string Id, MotifKey, Fallback, LocKey;
            public MotifView.TextNode Label;
            public Button Hit;
        }

        public readonly List<Item> Items = new List<Item>();
        public int Cursor { get; private set; }
        public Action<string> onSelect;
        MotifView.TextNode footerLeft, footerRight;
        float spacing;
        int top;

        static readonly string[,] Menu = {
            // id          motif menu.itemname key       fallback       Loc key
            { "arcade",   "menuarcade",                 "ARCADE",      "fe.arcade" },
            { "versus",   "menuversus",                 "VS MODE",     "fe.versus" },
            { "training", "menupractice.training",      "TRAINING",    "fe.training" },
            { "survival", "menumission.survival",       "SURVIVAL",    "fe.survival" },
            { "watch",    "menuwatch",                  "WATCH MODE",  "fe.watch" },
            { "options",  "options",                    "OPTIONS",     "fe.options" },
            { "credits",  "credits",                    "CREDITS",     "fe.credits" },
            { "exit",     "exit",                       "EXIT",        "fe.exit" },
        };

        public override void Build(RectTransform parent) {
            CreateView(parent, "Title", "TitleBG");
            var t = View.Motif.Title;
            FadeInTicks = t.FadeInTime;
            int n = Menu.GetLength(0);
            // menu.window.visibleitems is 6 in ikemen1; on a touch screen every item must be
            // reachable without scrolling, so the spacing shrinks a little when all fit
            float avail = Mathf.Max(100f, View.Height - 20f - t.MenuPos[1]);
            spacing = t.ItemSpacing[1] > 0 ? t.ItemSpacing[1] : 54f;
            if ((n - 1) * spacing > avail) spacing = Mathf.Max(42f, avail / (n - 1));

            // footer bar (footer.overlay.window / col / alpha of [Title Info])
            var bar = UIKit.Image(View.Top, "footerBar", new Vector2(0f, 1f), MotifView.Ui(View.Width / 2f, View.Height - 10f),
                                  new Vector2(View.Width, 20f), null, new Color(0f, 0f, 64f / 255f, 100f / 255f));
            footerLeft = View.NewText("footerLeft");
            footerRight = View.NewText("footerRight");

            for (int i = 0; i < n; i++) {
                int idx = i;
                var item = new Item { Id = Menu[i, 0], MotifKey = Menu[i, 1], Fallback = Menu[i, 2], LocKey = Menu[i, 3] };
                item.Label = View.NewText("item:" + item.Id);
                item.Hit = View.Hit("hit:" + item.Id, 0, 0, 480f, spacing, () => Tap(idx));
                Items.Add(item);
            }
            Refresh();
            FinishBuild();
        }

        /// <summary>Label of an item in the current language.</summary>
        public string LabelOf(Item it) => Loc.Arabic ? Loc.T(it.LocKey) : View.Motif.Title.ItemName(it.MotifKey, it.Fallback);

        public void Refresh() {
            var t = View.Motif.Title;
            int visible = Mathf.Max(1, Mathf.Min(Items.Count, Mathf.FloorToInt((View.Height - 20f - t.MenuPos[1]) / spacing) + 1));
            if (Cursor < top) top = Cursor;
            if (Cursor >= top + visible) top = Cursor - visible + 1;
            for (int i = 0; i < Items.Count; i++) {
                var it = Items[i];
                bool on = i >= top && i < top + visible;
                it.Hit.gameObject.SetActive(on);
                if (!on) { it.Label.Hide(); continue; }
                float y = t.MenuPos[1] + (i - top) * spacing;
                var font = i == Cursor ? t.ActiveFont : t.ItemFont;
                it.Label.Set(font, t.MenuPos[0], y, LabelOf(it), null, Loc.Arabic);
                // the tap target covers the label row (text baseline at y, right-aligned)
                var rt = (RectTransform)it.Hit.transform;
                rt.anchoredPosition = MotifView.Ui(t.MenuPos[0] - 240f, y - spacing * 0.4f);
                rt.sizeDelta = new Vector2(480f, spacing);
            }
            var small = FightText.Read(null, "", "", 2, 1);
            footerLeft.Set(small, 6f, View.Height - 1f, "my-ikmen dev.5 · Ikemen GO (MIT) · screenpack CC BY 3.0", new Color(0.75f, 0.75f, 0.75f), true);
            var right = FightText.Read(null, "", "", 2, -1);
            footerRight.Set(right, View.Width - 6f, View.Height - 1f, "v0.1.0-dev.5", new Color(0.75f, 0.75f, 0.75f), true);
        }

        protected override void OnShow() {
            Refresh();
        }

        void Tap(int i) {
            if (i != Cursor) { Cursor = i; Refresh(); }
            Activate();
        }

        public void MoveCursor(int delta) {
            Cursor = (Cursor + delta + Items.Count) % Items.Count;
            MotifAssets.PlaySnd(View.Motif.Title.CursorMoveSnd);
            Refresh();
        }

        public void Activate() {
            MotifAssets.PlaySnd(View.Motif.Title.CursorDoneSnd);
            onSelect?.Invoke(Items[Cursor].Id);
        }

        public void Select(string id) {
            for (int i = 0; i < Items.Count; i++) if (Items[i].Id == id) { Cursor = i; Refresh(); return; }
        }

        public override void OnMenuKey(MenuKey key) {
            switch (key) {
                case MenuKey.Up: MoveCursor(-1); break;
                case MenuKey.Down: MoveCursor(1); break;
                case MenuKey.Confirm: Activate(); break;
            }
        }

        /// <summary>Motif-space rect (left, top, w, h) of the logo, for the rendered check.</summary>
        public Rect LogoRect {
            get {
                // [TitleBG Title Logo] start = -250,155 from the screen centre; the logo sprite 0,0
                var s = MotifAssets.MotifSprite(0, 0, out var raw);
                if (raw == null) return new Rect(View.Width / 2f - 400f, 80f, 500f, 300f);
                float x = View.Width / 2f - 250f - raw.X, y = 155f - raw.Y;
                return new Rect(x, y, raw.Width, raw.Height);
            }
        }
    }
}
