#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using IK.App;
using IK.Core;
using IK.Input;
using IK.Settings;
using IK.UI;

namespace IK.EditorTools {
    /// <summary>
    /// dev.5 rendered checks for the front end: the title (logo pixels + menu labels in both
    /// languages, tap and D-pad navigation, menu sounds), the select screen (portraits really
    /// drawn in the cells and the big portrait windows, the whole selection done by taps), the
    /// VS screen (both portraits drawn), the hand-over to FightScreen.StartMatch and back, the
    /// victory / continue / results / credits screens, and one complete CPU-vs-CPU match through
    /// the flow. Pixel checks compare a capture with the same frame where the element under
    /// test is hidden, so a blank uGUI graphic cannot pass.
    /// </summary>
    public partial class IKUIRegressionRunner {
        IEnumerator FrontEndBoot(IKApp app) {
            Check(app.Current == Screen_.Title && app.Title.Visible && !app.Menu.Visible,
                  "Boots into the title screen");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!MotifAssets.Ready && sw.ElapsedMilliseconds < 60000) yield return null;
            Check(MotifAssets.Ready, "system.sff / system.snd decode in the background (" + sw.ElapsedMilliseconds + " ms after boot)");
            Check(MotifAssets.Motif.Sprites != null && MotifAssets.Motif.Sprites.Sprites.Count == 296,
                  "system.def + system.sff load from Resources" + (MotifAssets.LoadError != null ? ": " + MotifAssets.LoadError : ""));
            Check(MotifAssets.Motif.Sounds != null, "system.snd loads from Resources");
            var options = app.Title.Items.Find(i => i.Id == "options");
            options.Hit.onClick.Invoke();
            yield return null;
            Check(app.Current == Screen_.Settings && app.Settings.Visible, "Title → Options opens the settings");
            app.Settings.Root.Find("Developer").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Check(app.Current == Screen_.Main && app.Menu.Visible, "Options → Developer opens the dev.1-dev.4 menu");
        }

        IEnumerator FrontEndChecks(IKApp app) {
            var saved = SettingsStore.Current.language;

            // ---------------------------------------------------------------- title
            app.Menu.Root.Find("Back").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Check(app.Current == Screen_.Title, "Developer → Back returns to the title");
            var title = app.Title;
            foreach (var language in new[] { Language.English, Language.Arabic }) {
                SettingsStore.Current.language = language;
                Loc.Apply(language);
                title.Refresh();
                title.Step(40);                                   // past the fade-in
                Canvas.ForceUpdateCanvases();
                yield return null;
                string full = Dir + "title-" + language + ".png";
                CaptureFrame(full);
                if (language == Language.English) {
                    title.View.BgFront.gameObject.SetActive(false);   // layerno 1 = the logo
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    CaptureFrame(Dir + "title-nologo.png");
                    title.View.BgFront.gameObject.SetActive(true);
                    var lr = title.LogoRect;
                    int logo = DifferentPixelsIn(full, Dir + "title-nologo.png", title.View.PixelRect(lr.x, lr.y, lr.width, lr.height));
                    Check(logo > 2000, "Title logo is drawn from system.sff (logo pixels=" + logo + ")");
                    Check(title.Items[0].Label.UsedBitmap && title.Items[0].Label.Value == "ARCADE",
                          "English menu uses the motif's names and bitmap font (" + title.Items[0].Label.Value + ")");
                    Check(title.View.DrawnBackgroundElements >= 8, "Title background elements drawn: " + title.View.DrawnBackgroundElements);
                }
                foreach (var it in title.Items) it.Label.Rt.gameObject.SetActive(false);
                Canvas.ForceUpdateCanvases();
                yield return null;
                string bare = Dir + "title-nolabels-" + language + ".png";
                CaptureFrame(bare);
                foreach (var it in title.Items) it.Label.Rt.gameObject.SetActive(true);
                var t = title.View.Motif.Title;
                var menuRect = title.View.PixelRect(t.MenuPos[0] - 420f, t.MenuPos[1] - 50f, 420f, 420f);
                int labels = DifferentPixelsIn(full, bare, menuRect);
                Check(labels > 400, "Title menu labels render in " + language + " (pixels=" + labels + ")");
            }
            SettingsStore.Current.language = Language.English;
            Loc.Apply(Language.English);
            title.Refresh();

            // D-pad navigation + menu sounds
            int sounds = MotifAssets.SoundsPlayed;
            title.Select("arcade");
            title.Feed(new InputFrame());
            title.Feed(new InputFrame { D = true });
            Check(title.Cursor == 1, "D-pad down moves the title cursor (" + title.Cursor + ")");
            title.Feed(new InputFrame());
            title.Feed(new InputFrame { U = true });
            title.Feed(new InputFrame());
            Check(title.Cursor == 0, "D-pad up moves it back");
            Check(MotifAssets.SoundsPlayed >= sounds + 2 && MotifAssets.LastSound == "100,0",
                  "Cursor moves play system.snd 100,0 (" + MotifAssets.LastSound + ")");

            // ---------------------------------------------------------------- select (versus, by taps)
            title.Items.Find(i => i.Id == "versus").Hit.onClick.Invoke();
            yield return null;
            var sel = app.Select;
            Check(app.Current == Screen_.Select && sel.Visible && sel.Mode == GameMode.Versus, "Title → VS MODE opens the select screen");
            Check(sel.Roster != null && sel.Roster.Characters.Count == 4 && sel.Roster.Cells.Count == 5,
                  "Roster from select.def: kfm_zss, kfm720, kfm_zaxis, kfm + random (" + (sel.Roster != null ? sel.Roster.Cells.Count : 0) + " cells)");
            Check(sel.Roster != null && sel.Roster.Stages.Count == 6, "Six 2D stages in the stage list");
            sel.Step(30);
            Canvas.ForceUpdateCanvases();
            yield return null;
            string selFull = Dir + "select.png";
            CaptureFrame(selFull);
            var grid = sel.View.Top.Find("grid");
            grid.gameObject.SetActive(false);
            Canvas.ForceUpdateCanvases();
            yield return null;
            CaptureFrame(Dir + "select-nogrid.png");
            grid.gameObject.SetActive(true);
            for (int i = 0; i < 2; i++) {
                var r = sel.CellRect(i);
                int px = DifferentPixelsIn(selFull, Dir + "select-nogrid.png", sel.View.PixelRect(r.x, r.y, r.width, r.height));
                Check(px > 150, "Select cell " + i + " shows the portrait of " + sel.Roster.Cells[i].CharDef + " (pixels=" + px + ")");
            }
            var faceWin = sel.View.Top.Find("faceWindow1");
            faceWin.gameObject.SetActive(false);
            Canvas.ForceUpdateCanvases();
            yield return null;
            CaptureFrame(Dir + "select-noface.png");
            faceWin.gameObject.SetActive(true);
            var fr = sel.FaceRect(0);
            int facePx = DifferentPixelsIn(selFull, Dir + "select-noface.png", sel.View.PixelRect(fr.x, fr.y, fr.width, fr.height));
            Check(facePx > 1500, "P1 big portrait (the character's stand action) is drawn (pixels=" + facePx + ")");

            // the whole selection by taps: cell, cell again, palette, opponent, stage, level
            var cellHit = sel.View.Top.Find("grid").Find("cell0").GetComponent<Button>();
            cellHit.onClick.Invoke();
            Check(sel.Current == SelectScreen.Phase.P1Pal && sel.P1Char.CharDef == "kfm_zss.def", "Tapping the active cell confirms P1 (" + sel.Current + ")");
            sel.View.Top.Find("Right").GetComponent<Button>().onClick.Invoke();
            Check(sel.P1Pal == 2, "The > button picks palette 2");
            sel.Step(2);
            Canvas.ForceUpdateCanvases();
            yield return null;
            CaptureFrame(Dir + "select-pal2.png");
            Check(DifferentPixels(selFull, Dir + "select-pal2.png") > 300, "The palette change repaints the portrait");
            sel.View.Top.Find("OK").GetComponent<Button>().onClick.Invoke();
            Check(sel.Current == SelectScreen.Phase.P2Char, "OK moves on to the opponent (" + sel.Current + ")");
            sel.View.Top.Find("grid").Find("cell1").GetComponent<Button>().onClick.Invoke();
            Check(sel.Current == SelectScreen.Phase.P2Char && sel.P2Cell == 1, "First tap on another cell only moves the P2 cursor");
            sel.View.Top.Find("grid").Find("cell1").GetComponent<Button>().onClick.Invoke();
            Check(sel.Current == SelectScreen.Phase.P2Pal, "Second tap confirms P2 (" + sel.Current + ")");
            sel.Confirm();
            Check(sel.Current == SelectScreen.Phase.Stage, "Then the stage (" + sel.Current + ")");
            sel.Change(1);
            Check(sel.StageIndex == 0 && sel.ValueText().Contains("Mountainside Temple"), "Stage 0 is the KFM stage by its display name (" + sel.ValueText() + ")");
            sel.Step(2);
            Canvas.ForceUpdateCanvases();
            yield return null;
            CaptureFrame(Dir + "select-stage.png");
            sel.Confirm();
            Check(sel.Current == SelectScreen.Phase.Level, "Then the CPU level (" + sel.Current + ")");
            sel.Cancel();
            Check(sel.Current == SelectScreen.Phase.Stage, "Back steps back one phase");
            sel.Confirm();
            sel.Change(1);
            int level = sel.Level;
            sel.Confirm();
            yield return null;

            // ---------------------------------------------------------------- VS
            var m = app.Flow.Current;
            Check(app.Current == Screen_.Versus && m != null, "Selection done → VS screen (" + app.Current + ")");
            if (m != null) {
                Check(m.Mode == GameMode.Versus && m.Players[0].CharGroup == "chars/kfm_zss" && m.Players[0].Palette == 2 &&
                      m.Players[1].CharGroup == "chars/kfm720" && m.Players[1].AiLevel == level && m.Players[0].AiLevel == 0 &&
                      m.StageDef == "kfm.def",
                      "MatchSetup carries the choices (p1 " + m.Players[0].CharGroup + " pal " + m.Players[0].Palette + ", p2 " +
                      m.Players[1].CharGroup + " ai " + m.Players[1].AiLevel + ", stage " + m.StageDef + ")");
            }
            var vs = app.Versus;
            vs.Step(60);
            Canvas.ForceUpdateCanvases();
            yield return null;
            string vsFull = Dir + "vs.png";
            CaptureFrame(vsFull);
            for (int side = 0; side < 2; side++) {
                var w = vs.View.Top.Find("window" + (side + 1));
                w.gameObject.SetActive(false);
                Canvas.ForceUpdateCanvases();
                yield return null;
                string bare = Dir + "vs-noportrait" + (side + 1) + ".png";
                CaptureFrame(bare);
                w.gameObject.SetActive(true);
                var pr = vs.PortraitRect(side);
                int px = DifferentPixelsIn(vsFull, bare, vs.View.PixelRect(pr.x, pr.y, pr.width, pr.height));
                Check(px > 1500, "VS screen draws the P" + (side + 1) + " portrait (pixels=" + px + ")");
            }

            // ---------------------------------------------------------------- hand-over to the fight and back
            vs.Skip();
            yield return null; yield return null;
            Check(app.Current == Screen_.Fight && app.Fight.Setup == m && app.Fight.Engine != null,
                  "VS → FightScreen.StartMatch loads the chosen match" + (app.Fight.LoadError != null ? ": " + app.Fight.LoadError : ""));
            if (app.Current == Screen_.Fight) {
                for (int i = 0; i < 30; i++) app.Fight.Feed(new InputFrame());
                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "flow-fight.png");
                Button exit = null;
                foreach (var b in app.Fight.Root.GetComponentsInChildren<Button>(true)) if (b.name == "Exit") exit = b;
                Check(exit != null, "The fight's pause menu has an Exit button");
                if (exit != null) exit.onClick.Invoke();
                yield return null;
                Check(app.Current == Screen_.Select && app.Flow.LastResult != null && app.Flow.LastResult.Aborted,
                      "Exit from the pause menu returns to the versus select screen (" + app.Current + ")");
            }

            // ---------------------------------------------------------------- victory / continue / results / credits
            app.Victory.Begin(m, new MatchResult { Winner = 0, RoundsWon = new[] { 2, 0 } });
            app.Show(Screen_.Victory);
            app.Victory.Step(30);
            Canvas.ForceUpdateCanvases();
            yield return null;
            CaptureFrame(Dir + "victory.png");
            app.Victory.View.Top.Find("window1").gameObject.SetActive(false);
            Canvas.ForceUpdateCanvases();
            yield return null;
            CaptureFrame(Dir + "victory-nowinner.png");
            app.Victory.View.Top.Find("window1").gameObject.SetActive(true);
            Check(DifferentPixels(Dir + "victory.png", Dir + "victory-nowinner.png") > 1500, "Victory screen draws the winner's portrait");

            sounds = MotifAssets.SoundsPlayed;
            app.Show(Screen_.Continue);
            app.ContinueMenu.Step(300);
            Canvas.ForceUpdateCanvases();
            yield return null;
            CaptureFrame(Dir + "continue.png");
            Check(app.ContinueMenu.Digit == 8, "Continue counter shows 8 after counter.8.skiptime (" + app.ContinueMenu.Digit + ")");
            Check(MotifAssets.SoundsPlayed >= sounds + 2, "Continue counter voices play (900,9 then 900,8)");

            app.Results.Begin(ResultsScreen.Kind.Survival, 3, m != null ? m.Players[0] : null);
            app.Show(Screen_.Results);
            app.Results.Step(40);
            Canvas.ForceUpdateCanvases();
            yield return null;
            CaptureFrame(Dir + "survival-results.png");
            Check(app.Results.Text == "Rounds survived: 3", "Survival results text from the motif (" + app.Results.Text + ")");

            app.Show(Screen_.Credits);
            yield return null;
            Check(app.Credits.LicenceText.Contains("Ohmga Shironeko") && app.Credits.Body.text.Contains("Creative"),
                  "Credits show the screenpack's CC BY 3.0 attribution (packed LICENCE.txt)");
            Canvas.ForceUpdateCanvases();
            yield return null;
            CaptureFrame(Dir + "credits.png");
            app.Back();
            Check(app.Current == Screen_.Title, "Back from the credits returns to the title");

            // ---------------------------------------------------------------- one whole CPU match through the flow
            app.StartMode(GameMode.Watch);
            app.Flow.RoundTime = 10;                        // short rounds: the fixture runs every tick
            var roster = app.Flow.Roster;
            app.Go(app.Flow.Selected(roster.Cells[0], 1, roster.Cells[1], 1, "stage0.def"));
            Check(app.Current == Screen_.Versus, "Watch mode → VS");
            app.Versus.Step(40);
            app.Versus.Skip();
            yield return null;
            int ticks = 0;
            while (app.Current == Screen_.Fight && ticks < 20000) { app.Fight.Feed(new InputFrame()); ticks++; }
            Check(app.Current == Screen_.Victory && app.Flow.LastResult != null && !app.Flow.LastResult.Aborted,
                  "A CPU vs CPU match ends by itself and the flow shows the victory screen (" + app.Current + ", " + ticks + " ticks)");
            if (app.Flow.LastResult != null)
                Check(app.Flow.LastResult.Winner == -1 || app.Flow.LastResult.RoundsWon[app.Flow.LastResult.Winner] >= 1,
                      "The match result is consistent (winner " + app.Flow.LastResult.Winner + ", rounds " +
                      app.Flow.LastResult.RoundsWon[0] + "-" + app.Flow.LastResult.RoundsWon[1] + ")");
            app.Victory.Step(40);
            app.Victory.Skip();
            Check(app.Current == Screen_.Select, "After the victory screen, watch mode returns to select");

            // ---------------------------------------------------------------- dev.6: team versus (tag) by taps, then a whole team match
            app.Show(Screen_.Title);
            yield return null;
            var tv = app.Title.Items.Find(i => i.Id == "teamversus");
            Check(tv != null && app.Title.Items.Exists(i => i.Id == "teamarcade") && app.Title.Items.Exists(i => i.Id == "timeattack"),
                  "Title offers TEAM ARCADE, TEAM VERSUS and TIME ATTACK");
            if (tv != null) {
                app.Title.Select("teamversus");
                app.Title.Activate();
                yield return null;
                Check(app.Current == Screen_.Select && sel.TeamGame && sel.Current == SelectScreen.Phase.Team,
                      "Team Versus starts on the team menu (" + sel.Current + ")");
                sel.View.Top.Find("Right").GetComponent<Button>().onClick.Invoke();   // Turns x2 -> Turns x3
                sel.Change(1); sel.Change(1);                                          // -> Tag x2
                Check(sel.Teams == TeamMode.Tag && sel.TeamSize == 2, "The arrows choose Tag x 2 (" + sel.ValueText() + ")");
                sel.Step(2);
                Canvas.ForceUpdateCanvases();
                yield return null;
                CaptureFrame(Dir + "select-team.png");
                sel.Confirm();
                var g = sel.View.Top.Find("grid");
                g.Find("cell0").GetComponent<Button>().onClick.Invoke();               // leader: kfm_zss
                Check(sel.Current == SelectScreen.Phase.P1Char, "After the leader the grid asks for member 2");
                g.Find("cell3").GetComponent<Button>().onClick.Invoke();               // move to kfm
                g.Find("cell3").GetComponent<Button>().onClick.Invoke();               // confirm member 2
                Check(sel.Current == SelectScreen.Phase.P1Pal && sel.P1Partners.Count == 1 && sel.P1Partners[0].CharDef == "kfm.def",
                      "Member 2 picked (" + sel.Current + ", " + sel.P1Partners.Count + ")");
                sel.Confirm();                                                         // palette
                g.Find("cell1").GetComponent<Button>().onClick.Invoke(); g.Find("cell1").GetComponent<Button>().onClick.Invoke();
                g.Find("cell0").GetComponent<Button>().onClick.Invoke(); g.Find("cell0").GetComponent<Button>().onClick.Invoke();
                while (sel.Current != SelectScreen.Phase.Done && app.Current == Screen_.Select) { sel.Confirm(); yield return null; }
                var tm = app.Flow.Current;
                Check(tm != null && tm.Teams == TeamMode.Tag && tm.TeamSize(0) == 2 && tm.TeamSize(1) == 2 &&
                      tm.Partners[1].Count == 1 && tm.Partners[1][0].CharGroup == "chars/kfm_zss",
                      "MatchSetup is a 2-vs-2 tag match with the picked opponents");
                app.Versus.Step(40);
                app.Versus.Skip();
                yield return null; yield return null;
                var e6 = app.Fight.Engine;
                Check(app.Current == Screen_.Fight && e6 != null && e6.Teams == TeamMode.Tag && e6.Team[0].Count == 2 && e6.Team[1].Count == 2,
                      "The fight runs both teams" + (app.Fight.LoadError != null ? ": " + app.Fight.LoadError : ""));
                Button tagBtn = null;
                foreach (var b in app.Fight.Root.GetComponentsInChildren<Button>(true)) if (b.name == "Tag") tagBtn = b;
                Check(tagBtn != null && tagBtn.gameObject.activeInHierarchy, "The TAG button is shown in a tag match");
                if (e6 != null && tagBtn != null) {
                    int guard = 0;
                    while (!e6.CanTag(0) && guard++ < 600) app.Fight.Feed(new InputFrame());
                    var before = e6.P1;
                    tagBtn.onClick.Invoke();
                    app.Fight.Feed(new InputFrame());
                    Check(e6.P1 != before && e6.P1 == e6.Team[0][1], "Tapping TAG swaps in the partner (" + e6.P1.Character.Name + ")");
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    CaptureFrame(Dir + "fight-tag.png");
                    var line = app.Fight.Root.Find("teamLine");
                    Check(line != null && line.GetComponent<Text>().text.Length > 0, "The team line shows who is left");
                    Check(app.Music != null && app.Music.Current.StartsWith("fight"), "A fight track plays (" + (app.Music != null ? app.Music.Current : "") + ")");
                    // play it out CPU vs CPU with short rounds: the match must end by itself
                    app.Fight.Setup.Players[0].AiLevel = 0;
                    e6.TimerCount = 5;
                    int t6 = 0;
                    while (app.Current == Screen_.Fight && t6 < 20000) { app.Fight.Feed(new InputFrame()); t6++; }
                    Check(app.Current == Screen_.Victory, "The tag match ends and shows the victory screen (" + app.Current + ", " + t6 + " ticks)");
                }
            }

            SettingsStore.Current.language = saved;
            Loc.Apply(saved);
            app.Show(Screen_.Main);
            yield return null;
        }

        /// <summary>Pixels that differ between two captures inside <paramref name="r"/> (screen pixels).</summary>
        int DifferentPixelsIn(string a, string b, RectInt r) {
            var ia = new Texture2D(2, 2); ia.LoadImage(File.ReadAllBytes(a));
            var ib = new Texture2D(2, 2); ib.LoadImage(File.ReadAllBytes(b));
            int n = 0;
            int x0 = Mathf.Max(0, r.xMin), y0 = Mathf.Max(0, r.yMin);
            int x1 = Mathf.Min(Mathf.Min(ia.width, ib.width), r.xMax), y1 = Mathf.Min(Mathf.Min(ia.height, ib.height), r.yMax);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) {
                    var ca = ia.GetPixel(x, y);
                    var cb = ib.GetPixel(x, y);
                    if (Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g) + Mathf.Abs(ca.b - cb.b) > 0.1f) n++;
                }
            DestroyImmediate(ia); DestroyImmediate(ib);
            return n;
        }
    }
}
#endif
