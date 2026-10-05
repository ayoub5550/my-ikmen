using System;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// Arcade continue (`[Continue Screen]` + `[ContinueBGdef]`): "CONTINUE?" with YES / NO
    /// (`yes.active.font` marks the choice), the 9 → 0 counter action (`counter.anim` 900) and
    /// its per-digit sounds at `counter.N.skiptime`. A tap on YES / NO answers; when the counter
    /// reaches `counter.endtime` the answer is NO. The character's continue states
    /// (`p1.state`) need the fight engine and are not played here.
    /// </summary>
    public class ContinueScreen : FrontEndScreen {
        public Action<bool> onAnswer;
        public bool YesSelected { get; private set; } = true;
        public int Digit { get; private set; } = 9;
        MotifView.AnimNode counter;
        MotifView.TextNode title, yes, no, credits;
        Image overlay;
        bool answered;
        int lastDigit;
        MotifContinue C => View.Motif.Continue;

        public override void Build(RectTransform parent) {
            CreateView(parent, "Continue", "ContinueBG");
            overlay = UIKit.Image(View.Layer0, "overlay", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000, 4000), null, new Color(0f, 0f, 0f, 0.5f));
            counter = View.NewAnim("counter");
            title = View.NewText("continue");
            yes = View.NewText("yes");
            no = View.NewText("no");
            credits = View.NewText("credits");
            float y = C.Pos[1] + (C.Yes.Layout != null ? C.Yes.Layout.OffsetY : 60f);
            View.Hit("yesHit", C.Pos[0] + (C.Yes.Layout != null ? C.Yes.Layout.OffsetX : -80f), y - 20f, 150f, 80f, () => { YesSelected = true; Answer(); });
            View.Hit("noHit", C.Pos[0] + (C.No.Layout != null ? C.No.Layout.OffsetX : 80f), y - 20f, 150f, 80f, () => { YesSelected = false; Answer(); });
            FinishBuild();
        }

        protected override void OnShow() {
            YesSelected = true; answered = false; Digit = 9; lastDigit = 10;
            counter.Play(View.Motif.Animations, C.Counter.AnimNo);
            Draw();
        }

        void Draw() {
            var p = C.Pos;
            title.Set(C.Continue, p[0], p[1], Loc.Arabic ? Loc.T("fe.continue") : C.Continue.Text, null, Loc.Arabic);
            yes.Set(YesSelected ? C.YesActive : C.Yes, p[0], p[1], Loc.Arabic ? Loc.T("fe.yes") : (YesSelected ? C.YesActive : C.Yes).Text, null, Loc.Arabic);
            no.Set(!YesSelected ? C.NoActive : C.No, p[0], p[1], Loc.Arabic ? Loc.T("fe.no") : (!YesSelected ? C.NoActive : C.No).Text, null, Loc.Arabic);
            var cl = C.Counter.Layout;
            counter.Draw(MotifView.MotifLookup, cl.OffsetX, cl.OffsetY, cl.ScaleX, cl.ScaleY);
        }

        protected override void OnTick() {
            counter.Tick();
            Digit = C.DigitAt(Ticks);
            if (Digit != lastDigit && Digit >= 0) {
                MotifAssets.PlaySnd(C.CounterSnd[Digit]);
                lastDigit = Digit;
            }
            Draw();
            if (Digit < 0 && !answered) { YesSelected = false; Answer(); }
        }

        public void Answer() {
            if (answered) return;
            answered = true;
            MotifAssets.PlaySnd(YesSelected ? C.DoneSnd : C.CancelSnd);
            onAnswer?.Invoke(YesSelected);
        }

        public override void OnMenuKey(MenuKey key) {
            switch (key) {
                case MenuKey.Left: case MenuKey.Right: case MenuKey.Up: case MenuKey.Down:
                    YesSelected = !YesSelected; MotifAssets.PlaySnd(C.MoveSnd); Draw(); break;
                case MenuKey.Confirm: Answer(); break;
                case MenuKey.Cancel: YesSelected = false; Answer(); break;
            }
        }
    }
}
