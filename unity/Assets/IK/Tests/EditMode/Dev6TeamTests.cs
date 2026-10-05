using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IK.App;
using IK.Core;

namespace IK.Tests {
    /// <summary>
    /// dev.6 gate for teams (Core/FightEngineTeams.cs) and the new modes of the flow
    /// (Team Arcade / Team Versus / Time Attack in App/GameFlow.cs). Rules quoted from
    /// engine/ikemen-go/src/resources/defaultConfig.ini: Turns.Recovery.Base = 0,
    /// Turns.Recovery.Bonus = 27.5, Tag.LoseOnKO = 0, Team.PowerShare = 1.
    /// </summary>
    public class Dev6TeamTests {
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static string Chars => Path.Combine(Repo, "assets", "screenpack", "chars", "kfm");
        static MugenCharacter character;
        static CmdFile cmd;
        static CnsFile states;

        [OneTimeSetUp]
        public void Load() {
            character = MugenCharacter.Load(new FileSource(Chars), "kfm.def", loadSound: false);
            cmd = CmdFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.cmd")));
            states = CnsFile.Parse(File.ReadAllBytes(Path.Combine(Chars, "kfm.cns")));
            states.Merge(cmd.States);
        }

        static Fighter New() => new Fighter(character, states, cmd, () => 500f);

        static FightEngine Teams(TeamMode mode, int size, out List<Fighter> a, out List<Fighter> b) {
            a = new List<Fighter>(); b = new List<Fighter>();
            for (int k = 0; k < size; k++) { a.Add(New()); b.Add(New()); }
            var e = new FightEngine(a[0], b[0]);
            e.AnnounceTime = 0; e.OverTime = 2; e.WinPoseTime = 2;
            e.Teams = mode;
            e.SetTeam(0, a); e.SetTeam(1, b);
            e.StartRound(1);
            e.Tick(CmdKey.None, CmdKey.None);
            return e;
        }

        static void Until(FightEngine e, Func<bool> done, int max = 600) {
            for (int i = 0; i < max && !done(); i++) e.Tick(CmdKey.None, CmdKey.None);
        }

        [Test]
        public void Turns_LoserIsReplaced_WinnerKeepsLifePlusRecovery() {
            var e = Teams(TeamMode.Turns, 2, out var a, out var b);
            Assert.AreSame(a[0], e.P1); Assert.AreSame(b[0], e.P2);
            Assert.AreEqual(4, b[1].Id, "member ids: side + 1 + 2 * index (Ikemen player numbers)");
            e.P1.Life = 400;
            e.P2.Life = 0;                                  // P2's first member is KO
            Until(e, () => e.RoundNo == 2 && e.State == RoundState.Fighting);
            Assert.AreSame(a[0], e.P1, "the winner stays");
            Assert.AreSame(b[1], e.P2, "the loser's next member comes in");
            Assert.AreEqual(1000, e.P2.Life, "fresh member, full life");
            Assert.Greater(e.P1.Life, 400, "recovery: 27.5 % x time left");
            Assert.LessOrEqual(e.P1.Life, 400 + 275);
            Assert.AreEqual(1, e.Alive(1));
            Assert.IsFalse(e.MatchOver);
            e.P2.Life = 0;
            Until(e, () => e.MatchOver);
            Assert.IsTrue(e.MatchOver, "no member left on side 2");
            Assert.AreEqual(2, e.Wins[0]); Assert.AreEqual(0, e.Wins[1]);
            Assert.AreEqual(1, e.Chars.FindAll(c => c.PlayerNo == 1 && !c.IsHelper).Count, "one fighter per side on the field");
        }

        [Test]
        public void Tag_SwapOnW_PartnerTakesThePlace_RoundGoesOnAfterAKo() {
            var e = Teams(TeamMode.Tag, 2, out var a, out var b);
            Until(e, () => e.State == RoundState.Fighting);
            for (int i = 0; i < 40; i++) e.Tick(CmdKey.None, CmdKey.None);   // past the cooldown
            e.P1.Power = 1500;
            float x = e.P1.WorldX;
            Assert.IsTrue(e.CanTag(0), "standing idle with control and a living partner");
            e.Tick(CmdKey.w, CmdKey.None);
            Assert.AreSame(a[1], e.P1, "w (TagShiftFwd) brings the partner in");
            Assert.AreEqual(x, e.P1.WorldX, 3f, "where the leaving member stood");
            Assert.AreEqual(1500, e.P1.Power, "Team.PowerShare = 1");
            Assert.IsFalse(e.Chars.Contains(a[0]), "the leaving member is off the field");
            Assert.IsFalse(e.CanTag(0), "cooldown");

            // KO of the member on the field: the round goes on with the partner
            e.P1.Life = 0;
            Until(e, () => e.P1 == a[0], 200);
            Assert.AreSame(a[0], e.P1, "the partner replaces the KO'd member");
            Assert.AreEqual(RoundState.Fighting, e.State, "Tag.LoseOnKO = 0: the round is not over");
            e.P1.Life = 0;
            Until(e, () => e.State != RoundState.Fighting, 200);
            Assert.AreEqual(RoundState.Over, e.State, "every member KO ends the round");
            Assert.AreEqual(2, e.RoundWinner);
        }

        [Test]
        public void Tag_WaitingMemberRecovers() {
            var e = Teams(TeamMode.Tag, 2, out var a, out var b);
            Until(e, () => e.State == RoundState.Fighting);
            a[1].Life = 500;
            for (int i = 0; i < 120; i++) e.Tick(CmdKey.None, CmdKey.None);
            Assert.Greater(a[1].Life, 500);
        }

        static string Sp => Path.Combine(Repo, "assets", "screenpack");
        static Roster GameRoster() {
            var r = Roster.Parse(File.ReadAllBytes(Path.Combine(Repo, "assets", "ikmen", "select.def")));
            r.Filter(c => { var p = Path.Combine(Sp, c.CharGroup, c.CharDef); return File.Exists(p) ? Roster.CharDefProblem(MugenDef.Parse(File.ReadAllBytes(p))) : "missing"; },
                     s => { var p = Path.Combine(Sp, "stages", s.Def); return File.Exists(p) ? Roster.StageDefProblem(MugenDef.Parse(File.ReadAllBytes(p))) : "missing"; });
            return r;
        }

        [Test]
        public void TeamArcade_BuildsTeamsForBothSides() {
            var r = GameRoster();
            var f = new GameFlow(21) { Difficulty = 5 };
            f.Start(GameMode.Arcade, r, team: true);
            Assert.IsTrue(f.TeamGame);
            f.SetTeams(TeamMode.Tag, 3, new[] { r.Cells[1], r.Cells[2] }, null);
            Assert.AreEqual(FlowStep.Versus, f.Selected(r.Cells[0], 1));
            var m = f.Current;
            Assert.AreEqual(TeamMode.Tag, m.Teams);
            Assert.AreEqual(3, m.TeamSize(0));
            Assert.AreEqual(3, m.TeamSize(1), "the CPU team has as many members");
            foreach (var p in m.Partners[1]) Assert.AreEqual(m.Players[1].AiLevel, p.AiLevel);
            foreach (var p in m.Partners[0]) Assert.AreEqual(0, p.AiLevel, "the player's partners are human-controlled");
            Assert.IsFalse(m.Partners[0].Exists(p => string.IsNullOrEmpty(p.CharDef)), "random cell resolved");

            f.Start(GameMode.Arcade, r);                    // single arcade ignores team settings
            f.SetTeams(TeamMode.Turns, 2, new[] { r.Cells[1] }, null);
            f.Selected(r.Cells[0], 1);
            Assert.AreEqual(TeamMode.Single, f.Current.Teams);
            Assert.AreEqual(1, f.Current.TeamSize(1));
        }

        [Test]
        public void TeamVersus_UsesThePickedOpponents() {
            var r = GameRoster();
            var f = new GameFlow(22) { Difficulty = 3 };
            f.Start(GameMode.Versus, r, team: true);
            f.SetTeams(TeamMode.Turns, 2, new[] { r.Cells[0] }, new[] { r.Cells[1] });
            f.Selected(r.Cells[1], 1, r.Cells[0], 1, null);
            Assert.AreEqual(r.Cells[1].CharDef, f.Current.Partners[1][0].CharDef);
            Assert.AreEqual(3, f.Current.Partners[1][0].AiLevel);
        }

        [Test]
        public void TimeAttack_OneRoundEach_SumsTheTime_NoContinue() {
            var r = GameRoster();
            var f = new GameFlow(23) { Difficulty = 5 };
            f.Start(GameMode.TimeAttack, r);
            Assert.AreEqual(FlowStep.Versus, f.Selected(r.Cells[0], 1));
            Assert.AreEqual(1, f.Current.RoundsToWin);
            int n = f.Ladder.Count;
            Assert.Greater(n, 1);
            for (int i = 0; i < n - 1; i++) Assert.AreEqual(FlowStep.Versus, f.MatchEnded(new MatchResult { Winner = 0, Ticks = 600 }));
            Assert.AreEqual(FlowStep.TimeAttackResults, f.MatchEnded(new MatchResult { Winner = 0, Ticks = 600 }));
            Assert.AreEqual(600 * n, f.TotalTicks);
            Assert.AreEqual("0:10.00", IK.UI.ResultsScreen.FormatTicks(600));
            Assert.AreEqual("1:01.50", IK.UI.ResultsScreen.FormatTicks(3690));

            f.Start(GameMode.TimeAttack, r);
            f.Selected(r.Cells[0], 1);
            Assert.AreEqual(FlowStep.GameOver, f.MatchEnded(new MatchResult { Winner = 1 }), "a loss ends the run");
        }

        [Test]
        public void Music_EveryScreenTrackShips() {
            foreach (var name in new[] { "title", "select", "versus", "winner", "continue", "fight1", "fight2", "fight3" }) {
                var clip = Resources.Load<AudioClip>("music/" + name);
                Assert.IsNotNull(clip, "Resources/music/" + name + ".ogg (tools/music/compose.py)");
                Assert.AreEqual(2, clip.channels, name);
                Assert.Greater(clip.length, name.StartsWith("fight") || name == "title" || name == "select" ? 40f : 5f, name);
            }
            Assert.AreEqual(IK.App.MusicPlayer.ForStage("kfm.def"), IK.App.MusicPlayer.ForStage("KFM.def"), "stable per stage");
            CollectionAssert.Contains(IK.App.MusicPlayer.FightTracks, IK.App.MusicPlayer.ForStage("stage0.def"));
        }
    }
}
