using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.App;
using IK.Core;

namespace IK.Tests {
    /// <summary>
    /// dev.5 gate for select.def (Core/Roster.cs) and the game-mode flow (App/GameFlow.cs).
    /// Roster expectations are quoted from assets/screenpack/data/select.def (the screenpack
    /// reference) and assets/ikmen/select.def (the roster the game ships).
    /// </summary>
    public class RosterTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Screenpack => Path.Combine(Repo, "assets", "screenpack");

        static Roster ScreenpackRoster() => Roster.Parse(File.ReadAllBytes(Path.Combine(Screenpack, "data", "select.def")));
        static Roster GameRoster() => Roster.Parse(File.ReadAllBytes(Path.Combine(Repo, "assets", "ikmen", "select.def")));

        static string CharProblem(RosterChar c) {
            var path = Path.Combine(Screenpack, c.CharGroup, c.CharDef);
            if (!File.Exists(path)) return "missing";
            return Roster.CharDefProblem(MugenDef.Parse(File.ReadAllBytes(path)));
        }

        static string StageProblem(RosterStage s) {
            var path = Path.Combine(Screenpack, "stages", s.Def);
            if (!File.Exists(path)) return "missing";
            return Roster.StageDefProblem(MugenDef.Parse(File.ReadAllBytes(path)));
        }

        // ---- select.def parsing ------------------------------------------------------

        [Test]
        public void Screenpack_select_def_is_read() {
            var r = ScreenpackRoster();
            // [Characters]: kfm_zss, kfm720, kfm_zaxis, randomselect
            Assert.AreEqual(4, r.Cells.Count);
            Assert.AreEqual("chars/kfm_zss", r.Cells[0].CharGroup);
            Assert.AreEqual("kfm_zss.def", r.Cells[0].CharDef);
            CollectionAssert.AreEqual(new[] { "kfm.def" }, r.Cells[0].Stages);
            Assert.AreEqual(1, r.Cells[0].Order);                // MUGEN default
            Assert.AreEqual(0, r.Cells[1].Order);                // kfm720, stages/kfm.def, order=0
            Assert.IsTrue(r.Cells[3].Random);
            Assert.AreEqual(3, r.Characters.Count);
            // [ExtraStages]: 8 entries (kfm.def also comes from the char lines, no duplicate)
            Assert.AreEqual(8, r.Stages.Count);
            Assert.AreEqual("stage1.def", r.Stages[0].Def);
            Assert.AreEqual("stage0-720.def", r.Stages[7].Def);
            CollectionAssert.AreEqual(new[] { 6, 1, 1, 0, 0, 0, 0, 0, 0, 0 }, r.ArcadeMaxMatches);
            Assert.AreEqual(-1, r.SurvivalMaxMatches[0]);
        }

        [Test]
        public void Unsupported_content_is_filtered_out() {
            var r = ScreenpackRoster();
            r.Filter(CharProblem, StageProblem);
            // dev.5: ZSS characters run (Core/ZssFile.cs); only the 3D stages (model = ...) are dropped
            Assert.AreEqual(3, r.Characters.Count);
            Assert.AreEqual("chars/kfm_zss", r.Characters[0].CharGroup);
            Assert.AreEqual("chars/kfm720", r.Characters[1].CharGroup);
            Assert.AreEqual("chars/kfm_zaxis", r.Characters[2].CharGroup);
            Assert.IsTrue(r.Cells[r.Cells.Count - 1].Random, "the random cell stays");
            Assert.AreEqual(6, r.Stages.Count);
            Assert.IsFalse(r.Stages.Exists(s => s.Def.StartsWith("stage3d")));
            Assert.AreEqual(2, r.Skipped.Count, string.Join("; ", r.Skipped));
            StringAssert.Contains("3D", r.Skipped.Find(x => x.StartsWith("stages/stage3d.def")));
        }

        [Test]
        public void Game_roster_ships_only_runnable_content() {
            var r = GameRoster();
            r.Filter(CharProblem, StageProblem);
            Assert.AreEqual(0, r.Skipped.Count, string.Join("; ", r.Skipped));
            Assert.AreEqual(5, r.Cells.Count);
            Assert.AreEqual("chars/kfm_zss", r.Cells[0].CharGroup);
            Assert.AreEqual("kfm_zss.def", r.Cells[0].CharDef);
            Assert.AreEqual("chars/kfm720", r.Cells[1].CharGroup);
            CollectionAssert.AreEqual(new[] { "stage0-720.def" }, r.Cells[1].Stages);
            Assert.AreEqual("chars/kfm_zaxis", r.Cells[2].CharGroup);
            Assert.AreEqual("chars/kfm", r.Cells[3].CharGroup);
            Assert.AreEqual("kfm.def", r.Cells[3].CharDef);
            Assert.IsTrue(r.Cells[4].Random);
            CollectionAssert.AreEqual(new[] { "kfm.def", "stage0.def", "stage0-720.def", "stage1.def", "stageZ.def", "interactivestage.def" },
                                      r.Stages.ConvertAll(s => s.Def));
        }

        [Test]
        public void Char_paths_resolve_like_mugen() {
            Roster.ResolveCharPath("kfm", out var g, out var d);
            Assert.AreEqual("chars/kfm", g); Assert.AreEqual("kfm.def", d);
            Roster.ResolveCharPath("kfm/kfm720.def", out g, out d);
            Assert.AreEqual("chars/kfm", g); Assert.AreEqual("kfm720.def", d);
            Roster.ResolveCharPath("kfm.def", out g, out d);
            Assert.AreEqual("chars/kfm", g); Assert.AreEqual("kfm.def", d);
            Roster.ResolveCharPath("chars/kfm720/", out g, out d);
            Assert.AreEqual("chars/kfm720", g); Assert.AreEqual("kfm720.def", d);
            Assert.AreEqual("stage0-720.def", Roster.StageDefName("stages/stage0-720.def"));
        }

        [Test]
        public void Char_line_params() {
            var r = Roster.Parse("[Characters]\nfoo, stages/a.def, stages/b.def, order=3, music=x.mp3, includestage=0\nemptyslot\n[ExtraStages]\nstages/c.def\n");
            Assert.AreEqual(2, r.Cells.Count);
            Assert.AreEqual(3, r.Cells[0].Order);
            Assert.AreEqual("x.mp3", r.Cells[0].Music);
            CollectionAssert.AreEqual(new[] { "a.def", "b.def" }, r.Cells[0].Stages);
            Assert.IsTrue(r.Cells[1].Empty);
            // includestage=0: a.def / b.def stay out of the stage list
            Assert.AreEqual(1, r.Stages.Count);
        }

        [Test]
        public void Arcade_ladder_follows_maxmatches() {
            var r = GameRoster();
            r.Filter(CharProblem, StageProblem);
            var ladder = r.ArcadeLadder(new System.Random(7));
            Assert.AreEqual(6, ladder.Count, "6 opponents of order 1; orders 2-3 have no characters");
            foreach (var c in ladder) Assert.IsTrue(c.Selectable);
            // no repeat until the pool is exhausted: each pair of picks covers both characters
            for (int i = 0; i + 1 < ladder.Count; i += 2) Assert.AreNotSame(ladder[i], ladder[i + 1]);

            var sp = ScreenpackRoster();
            sp.Filter(CharProblem, StageProblem);
            // kfm720 and kfm_zaxis are order=0 there: only kfm_zss is an arcade opponent
            var spLadder = sp.ArcadeLadder(new System.Random(1));
            Assert.AreEqual(6, spLadder.Count);
            foreach (var c in spLadder) Assert.AreEqual("chars/kfm_zss", c.CharGroup);
        }

        // ---- game flow ---------------------------------------------------------------

        static Roster Filtered() { var r = GameRoster(); r.Filter(CharProblem, StageProblem); return r; }

        [Test]
        public void Arcade_ai_ramps_inside_1_to_8() {
            int prev = 0;
            for (int i = 0; i < 6; i++) {
                int ai = GameFlow.ArcadeAiLevel(5, i, 6);
                Assert.GreaterOrEqual(ai, prev);
                Assert.That(ai, Is.InRange(1, 8));
                prev = ai;
            }
            Assert.AreEqual(4, GameFlow.ArcadeAiLevel(5, 0, 6));
            Assert.AreEqual(7, GameFlow.ArcadeAiLevel(5, 5, 6));
            Assert.AreEqual(1, GameFlow.ArcadeAiLevel(1, 0, 6));
            Assert.AreEqual(8, GameFlow.ArcadeAiLevel(8, 5, 6));
        }

        [Test]
        public void Arcade_flow_win_lose_continue() {
            var r = Filtered();
            var f = new GameFlow(3) { Difficulty = 5 };
            Assert.AreEqual(FlowStep.Select, f.Start(GameMode.Arcade, r));
            Assert.AreEqual(FlowStep.Versus, f.Selected(r.Cells[0], 2));
            Assert.AreEqual(6, f.Ladder.Count);
            var m = f.Current;
            Assert.AreEqual(GameMode.Arcade, m.Mode);
            Assert.AreEqual("chars/kfm_zss", m.Players[0].CharGroup);
            Assert.AreEqual(2, m.Players[0].Palette);
            Assert.AreEqual(0, m.Players[0].AiLevel);
            Assert.AreEqual(4, m.Players[1].AiLevel);
            Assert.AreEqual(1, m.MatchNo);
            Assert.IsTrue(r.Stages.Exists(s => s.Def == m.StageDef), "stage " + m.StageDef);
            Assert.AreEqual(FlowStep.Fight, f.VersusDone());

            // win match 1 -> victory -> VS of match 2
            Assert.AreEqual(FlowStep.Victory, f.MatchEnded(new MatchResult { Winner = 0 }));
            Assert.AreEqual(FlowStep.Versus, f.VictoryDone());
            Assert.AreEqual(2, f.Current.MatchNo);
            // lose match 2 -> continue -> yes: same match again
            Assert.AreEqual(FlowStep.Continue, f.MatchEnded(new MatchResult { Winner = 1 }));
            Assert.AreEqual(FlowStep.Versus, f.ContinueAnswered(true));
            Assert.AreEqual(2, f.Current.MatchNo);
            Assert.AreEqual(1, f.Continues);
            // win the rest -> win screen
            for (int i = 2; i <= 6; i++) {
                var step = f.MatchEnded(new MatchResult { Winner = 0 });
                if (i < 6) { Assert.AreEqual(FlowStep.Victory, step); f.VictoryDone(); }
                else Assert.AreEqual(FlowStep.WinScreen, step);
            }
            Assert.AreEqual(6, f.Wins);
            Assert.AreEqual(FlowStep.Title, f.ResultsDone());
        }

        [Test]
        public void Arcade_game_over_and_abort() {
            var r = Filtered();
            var f = new GameFlow(5);
            f.Start(GameMode.Arcade, r);
            f.Selected(r.Cells[1], 1);
            Assert.AreEqual(FlowStep.Continue, f.MatchEnded(new MatchResult { Winner = -1 }), "a draw is not a win");
            Assert.AreEqual(FlowStep.GameOver, f.ContinueAnswered(false));
            Assert.AreEqual(FlowStep.Title, f.ResultsDone());
            f.Start(GameMode.Arcade, r);
            f.Selected(r.Cells[0], 1);
            Assert.AreEqual(FlowStep.Title, f.MatchEnded(new MatchResult { Aborted = true }));
        }

        [Test]
        public void Versus_watch_training_setups() {
            var r = Filtered();
            var f = new GameFlow(9) { Difficulty = 6 };
            f.Start(GameMode.Versus, r);
            Assert.AreEqual(FlowStep.Versus, f.Selected(r.Cells[0], 1, r.Cells[1], 3, "stage1.def"));
            Assert.AreEqual("stage1.def", f.Current.StageDef);
            Assert.AreEqual(0, f.Current.Players[0].AiLevel);
            Assert.AreEqual(6, f.Current.Players[1].AiLevel);
            Assert.AreEqual("chars/kfm720", f.Current.Players[1].CharGroup);
            Assert.AreEqual(3, f.Current.Players[1].Palette);
            Assert.AreEqual(FlowStep.Victory, f.MatchEnded(new MatchResult { Winner = 1 }));
            Assert.AreEqual(FlowStep.Select, f.VictoryDone());

            f.Start(GameMode.Watch, r);
            f.Selected(r.Cells[1], 1, r.Cells[0], 1, null);
            Assert.AreEqual(6, f.Current.Players[0].AiLevel);
            Assert.AreEqual(6, f.Current.Players[1].AiLevel);
            Assert.IsTrue(r.Stages.Exists(s => s.Def == f.Current.StageDef), "random stage when none chosen");

            f.Start(GameMode.Training, r);
            Assert.AreEqual(FlowStep.Fight, f.Selected(r.Cells[0], 1, r.Cells[0], 2, "stage0.def"), "training skips the VS screen");
            Assert.AreEqual(GameMode.Training, f.Current.Mode);
            Assert.AreEqual(0, f.Current.RoundsToWin);
            Assert.AreEqual(-1, f.Current.RoundTime);
            Assert.AreEqual(0, f.Current.Players[1].AiLevel);
            Assert.AreEqual(FlowStep.Select, f.MatchEnded(new MatchResult { Aborted = true }));
        }

        [Test]
        public void Random_cell_resolves_to_a_character() {
            var r = Filtered();
            var f = new GameFlow(11);
            f.Start(GameMode.Versus, r);
            f.Selected(r.Cells[2], 1, r.Cells[2], 1, null);
            Assert.IsTrue(f.P1Char.Selectable);
            Assert.IsTrue(f.P2Char.Selectable);
        }

        [Test]
        public void Survival_carries_life_until_the_first_loss() {
            var r = Filtered();
            var f = new GameFlow(13) { Difficulty = 4 };
            f.Start(GameMode.Survival, r);
            Assert.AreEqual(FlowStep.Versus, f.Selected(r.Cells[0], 1));
            Assert.AreEqual(1, f.Current.RoundsToWin);
            Assert.IsNull(f.Current.P1StartLife);
            Assert.AreEqual(FlowStep.Versus, f.MatchEnded(new MatchResult { Winner = 0, P1LifeLeft = 640 }));
            Assert.AreEqual(640, f.Current.P1StartLife);
            Assert.AreEqual(2, f.Current.MatchNo);
            Assert.AreEqual(FlowStep.Versus, f.MatchEnded(new MatchResult { Winner = 0, P1LifeLeft = 300 }));
            Assert.AreEqual(300, f.Current.P1StartLife);
            Assert.AreEqual(FlowStep.SurvivalResults, f.MatchEnded(new MatchResult { Winner = 1 }));
            Assert.AreEqual(2, f.Wins);
            Assert.AreEqual(3, GameFlow.SurvivalAiLevel(4, 0));
            Assert.AreEqual(4, GameFlow.SurvivalAiLevel(4, 3));
            Assert.AreEqual(8, GameFlow.SurvivalAiLevel(8, 30));
        }

        [Test]
        public void Mode_rules() {
            Assert.IsFalse(GameFlow.PicksOpponent(GameMode.Arcade));
            Assert.IsFalse(GameFlow.PicksOpponent(GameMode.Survival));
            Assert.IsTrue(GameFlow.PicksOpponent(GameMode.Versus));
            Assert.IsTrue(GameFlow.PicksStage(GameMode.Watch));
            Assert.IsTrue(GameFlow.PicksDifficulty(GameMode.Versus));
            Assert.IsFalse(GameFlow.PicksDifficulty(GameMode.Training));
            Assert.IsFalse(GameFlow.ShowsVersusScreen(GameMode.Training));
            Assert.AreEqual("watch", GameFlow.TitleKey(GameMode.Watch));
        }
    }
}
