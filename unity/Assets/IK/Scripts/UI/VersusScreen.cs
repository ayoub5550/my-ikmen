using System;
using UnityEngine;
using UnityEngine.UI;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// The VS screen (`[VS Screen]` + `[VersusBGdef]`): both fighters' big portraits
    /// (`pN.anim`, the character's action 0, scaled by motif/char localcoord and clipped to
    /// `pN.window`), their names, "Match N" and "Next Stage: …", the animated VS logo of the
    /// background, shown for `time` ticks. A tap (or any button) skips it after half a second.
    /// </summary>
    public class VersusScreen : FrontEndScreen {
        public MatchSetup Setup { get; private set; }
        public Action onDone;
        readonly MotifView.AnimNode[] portrait = new MotifView.AnimNode[2];
        readonly MotifView.TextNode[] names = new MotifView.TextNode[2];
        readonly MotifAssets.CharInfo[] info = new MotifAssets.CharInfo[2];
        MotifView.TextNode match, stage, hint;
        bool finished;

        MotifVersus Vs => View.Motif.Versus;
        public int Duration => Vs.Time > 0 ? Vs.Time : 150;

        public override void Build(RectTransform parent) {
            CreateView(parent, "Versus", "VersusBG");
            FadeInTicks = Vs.FadeInTime;
            for (int i = 0; i < 2; i++) {
                var lay = (i == 0 ? Vs.P1 : Vs.P2).Portrait.Layout;
                var win = View.Window(View.Top, "window" + (i + 1), lay);
                var origin = lay.HasWindow ? new Vector2(lay.Window[0], lay.Window[1]) : Vector2.zero;
                portrait[i] = View.NewAnim("portrait" + (i + 1), win, origin);
                names[i] = View.NewText("name" + (i + 1));
            }
            match = View.NewText("match");
            stage = View.NewText("stage");
            hint = View.NewText("hint");
            View.Hit("skip", View.Width / 2f, View.Height / 2f, View.Width, View.Height, () => Skip());
            FinishBuild();
        }

        /// <summary>Sets who fights (call before showing).</summary>
        public void Begin(MatchSetup setup) {
            Setup = setup;
            finished = false;
            for (int i = 0; i < 2; i++) {
                var p = setup.Players[i];
                info[i] = MotifAssets.Char(p.CharGroup, p.CharDef);
                if (info[i] != null && info[i].Character != null) portrait[i].Play(info[i].Character.Air, Mathf.Max(0, (i == 0 ? Vs.P1 : Vs.P2).Portrait.AnimNo), info[i]);
            }
            Draw();
        }

        void Draw() {
            if (Setup == null) return;
            for (int i = 0; i < 2; i++) {
                var vp = i == 0 ? Vs.P1 : Vs.P2;
                var lay = vp.Portrait.Layout;
                var ci = info[i];
                if (ci == null || ci.Character == null) { portrait[i].Hide(); names[i].Hide(); continue; }
                float k = View.Width / Mathf.Max(1f, ci.LocalW);
                int pal = Setup.Players[i].Palette;
                portrait[i].Draw((g, n) => { var s = ci.Sprite(g, n, pal, out var raw); return (s, raw); },
                                 lay.OffsetX, lay.OffsetY, lay.ScaleX * k, lay.ScaleY * k, lay.Facing);
                names[i].Set(vp.Name, 0f, 0f, ci.Character.DisplayName);
            }
            string m = Loc.Arabic ? string.Format(Loc.T("fe.match"), Setup.MatchNo)
                                  : FightHud.ReplaceFirst(Vs.Match.Text.Length > 0 ? Vs.Match.Text : "Match %i", "%i", Setup.MatchNo.ToString());
            match.Set(Vs.Match, 0f, 0f, m, null, Loc.Arabic);
            string stageName = MotifAssets.StageName(Setup.StageDef);
            string st = Loc.Arabic ? string.Format(Loc.T("fe.stage"), stageName)
                                   : FightHud.ReplaceFirst(Vs.Stage.Text.Length > 0 ? Vs.Stage.Text : "Stage: %s", "%s", stageName);
            stage.Set(Vs.Stage, Vs.StagePos[0], Vs.StagePos[1], st, null, Loc.Arabic || !MotifView.TextNode.BitmapCanDraw(MotifAssets.Font(Vs.Stage.FontIndex), st, 0));
            hint.Set(FightText.Read(null, "", "", 2, 0), View.Width / 2f, View.Height - 120f, Ticks > 30 ? Loc.T("fe.tapToSkip") : "", new Color(1f, 1f, 1f, 0.7f), true);
        }

        protected override void OnTick() {
            portrait[0].Tick(); portrait[1].Tick();
            Draw();
            if (Ticks >= Duration) Finish();
        }

        public void Skip() { if (Ticks > 30) Finish(); }

        void Finish() {
            if (finished) return;
            finished = true;
            onDone?.Invoke();
        }

        public override void OnMenuKey(MenuKey key) {
            if (key == MenuKey.Confirm || key == MenuKey.Cancel) Skip();
        }

        /// <summary>Motif-space rect of portrait window <paramref name="side"/>, for the rendered check.</summary>
        public Rect PortraitRect(int side) {
            var l = (side == 0 ? Vs.P1 : Vs.P2).Portrait.Layout;
            if (l.HasWindow) return new Rect(l.Window[0], l.Window[1], l.Window[2], l.Window[3]);
            return new Rect(l.OffsetX - 200f, l.OffsetY - 400f, 400f, 400f);
        }
    }
}
