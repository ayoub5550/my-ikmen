using System;
using UnityEngine;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// End-of-run screens: the arcade Win Screen (`[Win Screen]` `wintext`, "Congratulations!"),
    /// the Survival Results (`[Survival Results Screen]` `winstext`, "Rounds survived: %i") and
    /// Game Over (the motif's `gameover.def` storyboard is not ported; a text screen stands in).
    /// The player's character stands in the middle in its stand action. Ends after the motif
    /// time or on a tap, then the flow returns to the title.
    /// </summary>
    public class ResultsScreen : FrontEndScreen {
        public enum Kind { Win, Survival, GameOver, TimeAttack }
        public Kind Mode { get; private set; }
        public int Count { get; private set; }
        public Action onDone;
        public string Text { get; private set; } = "";
        MotifView.TextNode text, hint;
        MotifView.AnimNode pose;
        MotifAssets.CharInfo who;
        int pal = 1;
        bool finished;

        public int Duration {
            get {
                var m = View.Motif;
                if (Mode == Kind.Win) return m.Win.ShowTime > 0 ? m.Win.ShowTime : 300;
                if (Mode == Kind.Survival) return m.Survival.ShowTime > 0 ? m.Survival.ShowTime : 300;
                return 240;
            }
        }

        public override void Build(RectTransform parent) {
            CreateView(parent, "Results", "WinBG");
            pose = View.NewAnim("pose");
            text = View.NewText("text");
            hint = View.NewText("hint");
            View.Hit("skip", View.Width / 2f, View.Height / 2f, View.Width, View.Height, () => Skip());
            FinishBuild();
        }

        public void Begin(Kind kind, int count, PlayerSetup player) {
            Mode = kind; Count = count; finished = false;
            var m = View.Motif;
            FadeInTicks = kind == Kind.Win ? m.Win.FadeInTime : (kind == Kind.Survival ? m.Survival.FadeInTime : 20);
            who = player != null ? MotifAssets.Char(player.CharGroup, player.CharDef) : null;
            pal = player != null ? player.Palette : 1;
            if (who != null && who.Character != null) pose.Play(who.Character.Air, kind == Kind.GameOver ? 0 : 0, who);
            switch (kind) {
                case Kind.Win:
                    Text = Loc.Arabic ? Loc.T("fe.congrats") : m.Win.Text.Text; break;
                case Kind.Survival:
                    Text = Loc.Arabic ? string.Format(Loc.T("fe.survived"), count)
                                      : FightHud.ReplaceFirst(m.Survival.Text.Text, "%i", count.ToString()); break;
                case Kind.TimeAttack: {
                    // count = clear time in ticks; the best one is kept in PlayerPrefs
                    int best = PlayerPrefs.GetInt("ik.timeattack.best", 0);
                    bool record = best <= 0 || count < best;
                    if (record) { PlayerPrefs.SetInt("ik.timeattack.best", count); PlayerPrefs.Save(); best = count; }
                    Text = string.Format(Loc.T("fe.clearTime"), FormatTicks(count)) + "\n" +
                           (record ? Loc.T("fe.newRecord") : string.Format(Loc.T("fe.bestTime"), FormatTicks(best)));
                    break;
                }
                default:
                    Text = Loc.T("fe.gameOver"); break;
            }
            Draw();
        }

        /// <summary>60 ticks = 1 s → "m:ss.cc".</summary>
        public static string FormatTicks(int ticks) {
            int cs = (int)Math.Round(Math.Max(0, ticks) * 100.0 / 60.0);
            return string.Format("{0}:{1:00}.{2:00}", cs / 6000, cs / 100 % 60, cs % 100);
        }

        void Draw() {
            var m = View.Motif;
            var font = Mode == Kind.Win ? m.Win.Text : (Mode == Kind.Survival ? m.Survival.Text : m.Win.Text);
            bool ttf = Loc.Arabic || !MotifView.TextNode.BitmapCanDraw(MotifAssets.Font(font.FontIndex), Text, font.FontBank);
            text.Set(font, 0f, 0f, Text, Mode == Kind.GameOver ? new Color(1f, 0.35f, 0.3f) : (Color?)null, ttf);
            if (who != null && who.Character != null) {
                float k = View.Width / Mathf.Max(1f, who.LocalW) * 0.75f;
                int p = pal;
                pose.Draw((g, n) => { var s = who.Sprite(g, n, p, out var raw); return (s, raw); }, View.Width / 2f, View.Height - 60f, k, k);
            } else pose.Hide();
            hint.Set(FightText.Read(null, "", "", 2, -1), View.Width - 30f, View.Height - 20f, Ticks > 30 ? Loc.T("fe.tapToSkip") : "", new Color(1f, 1f, 1f, 0.7f), true);
        }

        protected override void OnTick() {
            pose.Tick();
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
