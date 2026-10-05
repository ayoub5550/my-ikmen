using System;
using UnityEngine;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// The victory screen (`[Victory Screen]` + `[VictoryBGdef]`) shown after a won match: the
    /// winner's portrait (`p1.spr` = 9000,2 in ikemen1, falling back to 9000,1 for MUGEN 1.0
    /// characters that have no 9000,2), the loser behind it at `p2.lose.brightness`, the
    /// winner's name and the win quote. Ikemen always puts the winner on the left. A draw shows
    /// both names and "Draw". Ends after `time` ticks or on a tap.
    /// </summary>
    public class VictoryScreen : FrontEndScreen {
        public Action onDone;
        public MatchResult Result { get; private set; }
        public MatchSetup Setup { get; private set; }
        RectTransform win1, win2;
        MotifView.SpriteNode winner, loser;
        MotifView.TextNode nameText, quote, hint;
        bool finished;
        MotifVictory V => View.Motif.Victory;
        public int Duration => V.Time > 0 ? V.Time : 300;

        public override void Build(RectTransform parent) {
            CreateView(parent, "Victory", "VictoryBG");
            FadeInTicks = V.FadeInTime;
            // p2 (loser) is layerno 1, under the winner
            win2 = View.Window(View.Top, "window2", V.P2Portrait.Layout);
            loser = new MotifView.SpriteNode(win2, "loser", WinOrigin(V.P2Portrait.Layout));
            win1 = View.Window(View.Top, "window1", V.P1Portrait.Layout);
            winner = new MotifView.SpriteNode(win1, "winner", WinOrigin(V.P1Portrait.Layout));
            nameText = View.NewText("name");
            quote = View.NewText("quote");
            hint = View.NewText("hint");
            View.Hit("skip", View.Width / 2f, View.Height / 2f, View.Width, View.Height, () => Skip());
            FinishBuild();
        }

        static Vector2 WinOrigin(FightLayout l) => l.HasWindow ? new Vector2(l.Window[0], l.Window[1]) : Vector2.zero;

        public void Begin(MatchSetup setup, MatchResult result) {
            Setup = setup; Result = result; finished = false;
            Draw();
        }

        void Draw() {
            if (Setup == null || Result == null) return;
            int w = Result.Winner < 0 ? 0 : Result.Winner;
            int l = 1 - w;
            DrawPortrait(winner, V.P1Portrait, Setup.Players[w], 1f);
            DrawPortrait(loser, V.P2Portrait, Setup.Players[l], V.P2LoseBrightness / 100f);
            var wi = MotifAssets.Char(Setup.Players[w].CharGroup, Setup.Players[w].CharDef);
            string wname = wi != null && wi.Character != null ? wi.Character.DisplayName : "";
            if (Result.Winner < 0) {
                var li = MotifAssets.Char(Setup.Players[l].CharGroup, Setup.Players[l].CharDef);
                wname += " / " + (li != null && li.Character != null ? li.Character.DisplayName : "");
            }
            nameText.Set(V.P1Name, 0f, 0f, wname);
            string q = Result.Winner < 0 ? Loc.T("fe.draw") : (Loc.Arabic ? Loc.T("fe.winner") : (V.WinQuote.Text.Length > 0 ? V.WinQuote.Text : "Winner!"));
            quote.Set(V.WinQuote, 0f, 0f, q, null, Loc.Arabic);
            hint.Set(FightText.Read(null, "", "", 2, -1), View.Width - 30f, View.Height - 20f, Ticks > 30 ? Loc.T("fe.tapToSkip") : "", new Color(1f, 1f, 1f, 0.7f), true);
        }

        void DrawPortrait(MotifView.SpriteNode node, FightAnimLayout lay, PlayerSetup p, float bright) {
            var ci = MotifAssets.Char(p.CharGroup, p.CharDef);
            if (ci == null || ci.Character == null) { node.Hide(); return; }
            int g = lay.SprGroup >= 0 ? lay.SprGroup : 9000, n = lay.HasSprite ? lay.SprNumber : 2;
            var s = ci.Sprite(g, n, p.Palette, out var raw);
            if (s == null) s = ci.Sprite(9000, 1, p.Palette, out raw);
            float k = View.Width / Mathf.Max(1f, ci.LocalW);
            var L = lay.Layout;
            node.Set(s, raw, L.OffsetX, L.OffsetY, L.ScaleX * k, L.ScaleY * k, L.Facing, new Color(bright, bright, bright, 1f));
        }

        protected override void OnTick() {
            Draw();
            if (Ticks >= Duration) Finish();
        }

        public void Skip() { if (Ticks > 30) Finish(); }

        void Finish() {
            if (finished) return;
            finished = true;
            onDone?.Invoke();
        }

        public override void OnMenuKey(MenuKey key) { if (key == MenuKey.Confirm || key == MenuKey.Cancel) Skip(); }
    }
}
