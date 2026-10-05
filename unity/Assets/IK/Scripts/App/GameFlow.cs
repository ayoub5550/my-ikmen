using System;
using System.Collections.Generic;
using IK.Core;

namespace IK.App {
    /// <summary>Front-end screens the flow moves between.</summary>
    public enum FlowStep { Title, Select, Versus, Fight, Victory, Continue, GameOver, WinScreen, SurvivalResults, TimeAttackResults }

    /// <summary>
    /// The game-mode state machine of the front end, as plain C# so it is unit tested without a
    /// scene: which screens a mode visits, who the CPU opponents are, how the AI level ramps,
    /// and what happens after each <see cref="MatchResult"/>.
    ///
    /// Behaviour follows Ikemen GO's mode flow (the `start.lua` logic that v1.0.0 moved into
    /// motif.go/script.go): Arcade = ladder from `select.def` `arcade.maxmatches`, continue on a
    /// loss; Versus = P1 against a chosen CPU (on a phone there is no second pad), back to select
    /// after the result; Training = endless round against a dummy; Survival = one round per match,
    /// life carries over, until the first loss; Watch = CPU vs CPU.
    /// </summary>
    public class GameFlow {
        public GameMode Mode { get; private set; }
        public Roster Roster { get; private set; }
        public FlowStep Step { get; private set; } = FlowStep.Title;

        /// <summary>CPU difficulty 1..8 chosen by the player (Options.Difficulty by default).</summary>
        public int Difficulty = 5;
        public int RoundsToWin = 2;
        public int RoundTime = 99;

        public RosterChar P1Char { get; private set; }
        public RosterChar P2Char { get; private set; }
        public int P1Palette { get; private set; } = 1;
        public int P2Palette { get; private set; } = 1;
        /// <summary>Stage chosen on the select screen; null = automatic (char's stage or random).</summary>
        public string ChosenStage { get; private set; }

        /// <summary>Arcade opponents in order; survival grows it one opponent at a time.</summary>
        public readonly List<RosterChar> Ladder = new List<RosterChar>();
        /// <summary>0-based index of the current match in <see cref="Ladder"/>.</summary>
        public int MatchIndex { get; private set; }
        public int Wins { get; private set; }
        public int Continues { get; private set; }
        public int? P1Life { get; private set; }
        public MatchResult LastResult { get; private set; }
        public MatchSetup Current { get; private set; }

        /// <summary>dev.6: team game (title menu "Team Arcade" / "Team Versus").</summary>
        public bool TeamGame { get; private set; }
        public TeamMode Teams { get; private set; } = TeamMode.Single;
        public int TeamSize { get; private set; } = 1;
        /// <summary>Members after the leader picked on the select screen (P1, and P2 where the player picks it).</summary>
        public readonly List<RosterChar> P1Partners = new List<RosterChar>();
        public readonly List<RosterChar> P2Partners = new List<RosterChar>();
        /// <summary>Time attack: ticks of every won match so far (the clear time).</summary>
        public int TotalTicks { get; private set; }

        readonly Random rng;

        public GameFlow(int seed = 0) { rng = seed == 0 ? new Random() : new Random(seed); }

        // ------------------------------------------------------------------ mode rules

        /// <summary>The player picks the opponent (and the stage) in these modes.</summary>
        public static bool PicksOpponent(GameMode m) => m == GameMode.Versus || m == GameMode.Training || m == GameMode.Watch;
        public static bool PicksStage(GameMode m) => PicksOpponent(m);
        /// <summary>The CPU difficulty chooser is shown in these modes (arcade/survival use Options → difficulty).</summary>
        public static bool PicksDifficulty(GameMode m) => m == GameMode.Versus || m == GameMode.Watch;
        /// <summary>Modes that walk the arcade ladder.</summary>
        public static bool IsLadder(GameMode m) => m == GameMode.Arcade || m == GameMode.TimeAttack;
        /// <summary>Modes the title offers as a team game.</summary>
        public static bool AllowsTeams(GameMode m) => m == GameMode.Arcade || m == GameMode.Versus || m == GameMode.Watch;
        public static bool ShowsVersusScreen(GameMode m) => m != GameMode.Training;
        /// <summary>Motif `title.&lt;mode&gt;.text` key for the select screen title.</summary>
        public static string TitleKey(GameMode m) {
            switch (m) {
                case GameMode.Arcade: return "arcade";
                case GameMode.Versus: return "versus";
                case GameMode.Training: return "training";
                case GameMode.Survival: return "survival";
                case GameMode.TimeAttack: return "timeattack";
                default: return "watch";
            }
        }

        /// <summary>
        /// Arcade AI ramp: one below the chosen difficulty for the first opponent, up to two above
        /// it for the last, always inside MUGEN's 1..8.
        /// </summary>
        public static int ArcadeAiLevel(int difficulty, int matchIndex, int matchCount) {
            int lo = difficulty - 1, hi = difficulty + 2;
            int ai = matchCount <= 1 ? hi : lo + (int)Math.Round((hi - lo) * (double)matchIndex / (matchCount - 1));
            return Math.Max(1, Math.Min(8, ai));
        }

        /// <summary>Survival AI ramp: +1 level every 3 wins, capped at 8.</summary>
        public static int SurvivalAiLevel(int difficulty, int wins) => Math.Max(1, Math.Min(8, difficulty - 1 + wins / 3));

        // ------------------------------------------------------------------ driving

        /// <summary>Title menu picked a mode: go to the select screen.</summary>
        public FlowStep Start(GameMode mode, Roster roster, bool team = false) {
            Mode = mode;
            TeamGame = team && AllowsTeams(mode);
            Teams = TeamMode.Single; TeamSize = 1;
            P1Partners.Clear(); P2Partners.Clear();
            TotalTicks = 0;
            Roster = roster ?? throw new ArgumentNullException(nameof(roster));
            Ladder.Clear();
            MatchIndex = 0; Wins = 0; Continues = 0; P1Life = null;
            LastResult = null; Current = null;
            P1Char = P2Char = null; ChosenStage = null;
            return Step = FlowStep.Select;
        }

        /// <summary>
        /// The select screen is done. <paramref name="p2"/> and <paramref name="stage"/> are only
        /// used in the modes where the player picks them; random cells are resolved here.
        /// </summary>
        /// <summary>dev.6: the team menu of the select screen (call before <see cref="Selected"/>).
        /// <paramref name="p1Partners"/> / <paramref name="p2Partners"/> are the members after the leader.</summary>
        public void SetTeams(TeamMode teams, int size, IList<RosterChar> p1Partners, IList<RosterChar> p2Partners) {
            Teams = TeamGame ? teams : TeamMode.Single;
            TeamSize = Teams == TeamMode.Single ? 1 : Math.Max(2, Math.Min(4, size));
            P1Partners.Clear(); P2Partners.Clear();
            if (Teams == TeamMode.Single) return;
            if (p1Partners != null) foreach (var c in p1Partners) if (P1Partners.Count < TeamSize - 1) P1Partners.Add(c);
            if (p2Partners != null) foreach (var c in p2Partners) if (P2Partners.Count < TeamSize - 1) P2Partners.Add(c);
        }

        public FlowStep Selected(RosterChar p1, int p1Pal, RosterChar p2 = null, int p2Pal = 1, string stage = null) {
            P1Char = Resolve(p1);
            P1Palette = Math.Max(1, p1Pal);
            P2Palette = Math.Max(1, p2Pal);
            ChosenStage = PicksStage(Mode) ? stage : null;
            Ladder.Clear();
            MatchIndex = 0; Wins = 0; Continues = 0; P1Life = null;
            for (int k = 0; k < P1Partners.Count; k++) P1Partners[k] = Resolve(P1Partners[k]);
            for (int k = 0; k < P2Partners.Count; k++) P2Partners[k] = Resolve(P2Partners[k]);
            while (P1Partners.Count < TeamSize - 1) P1Partners.Add(Roster.RandomChar(rng));
            switch (Mode) {
                case GameMode.Arcade:
                case GameMode.TimeAttack:
                    Ladder.AddRange(Roster.ArcadeLadder(rng, P1Char));
                    if (Ladder.Count == 0 && Roster.Characters.Count > 0) Ladder.Add(Roster.RandomChar(rng));
                    break;
                case GameMode.Survival:
                    Ladder.Add(Roster.RandomChar(rng));
                    break;
                default:
                    Ladder.Add(Resolve(p2) ?? Roster.RandomChar(rng));
                    break;
            }
            P2Char = Ladder.Count > 0 ? Ladder[0] : null;
            BuildMatch();
            return Step = ShowsVersusScreen(Mode) ? FlowStep.Versus : FlowStep.Fight;
        }

        RosterChar Resolve(RosterChar c) {
            if (c == null) return null;
            return c.Random || c.Empty ? Roster.RandomChar(rng) : c;
        }

        /// <summary>The VS screen ended: fight.</summary>
        public FlowStep VersusDone() => Step = FlowStep.Fight;

        void BuildMatch() {
            P2Char = MatchIndex < Ladder.Count ? Ladder[MatchIndex] : P2Char;
            var m = new MatchSetup { Mode = Mode, MatchNo = MatchIndex + 1 };
            Fill(m.Players[0], P1Char, P1Palette);
            Fill(m.Players[1], P2Char, P2Palette);
            // the CPU opponent wears another colour when both pick the same character
            if (P2Char == P1Char && m.Players[1].Palette == m.Players[0].Palette && !PicksOpponent(Mode))
                m.Players[1].Palette = m.Players[0].Palette == 1 ? 2 : 1;
            m.RoundsToWin = RoundsToWin;
            m.RoundTime = RoundTime;
            if (Teams != TeamMode.Single) {
                m.Teams = Teams;
                foreach (var c in P1Partners) { var ps = new PlayerSetup(); Fill(ps, c, 1); m.Partners[0].Add(ps); }
                // the opponent team: the chosen members (versus / watch), else random CPUs
                bool chosen = PicksOpponent(Mode);
                for (int k = 0; k < TeamSize - 1; k++) {
                    var c = chosen && k < P2Partners.Count ? P2Partners[k] : Roster.RandomChar(rng);
                    var ps = new PlayerSetup(); Fill(ps, c, c == P2Char ? (m.Players[1].Palette == 1 ? 2 : 1) : 1);
                    m.Partners[1].Add(ps);
                }
            }
            switch (Mode) {
                case GameMode.TimeAttack:
                    m.Players[0].AiLevel = 0;
                    m.Players[1].AiLevel = ArcadeAiLevel(Difficulty, MatchIndex, Ladder.Count);
                    m.RoundsToWin = 1;          // Ikemen time attack: one round per opponent
                    break;
                case GameMode.Arcade:
                    m.Players[0].AiLevel = 0;
                    m.Players[1].AiLevel = ArcadeAiLevel(Difficulty, MatchIndex, Ladder.Count);
                    break;
                case GameMode.Versus:
                    m.Players[0].AiLevel = 0;
                    m.Players[1].AiLevel = Clamp(Difficulty);
                    break;
                case GameMode.Training:
                    m.Players[0].AiLevel = 0;
                    m.Players[1].AiLevel = 0;      // the engine's training dummy
                    m.RoundsToWin = 0;
                    m.RoundTime = -1;
                    break;
                case GameMode.Survival:
                    m.Players[0].AiLevel = 0;
                    m.Players[1].AiLevel = SurvivalAiLevel(Difficulty, Wins);
                    m.RoundsToWin = 1;
                    m.P1StartLife = P1Life;
                    break;
                case GameMode.Watch:
                    m.Players[0].AiLevel = Clamp(Difficulty);
                    m.Players[1].AiLevel = Clamp(Difficulty);
                    break;
            }
            // partners inherit the AI level of their side
            for (int side = 0; side < 2; side++)
                foreach (var ps in m.Partners[side]) ps.AiLevel = m.Players[side].AiLevel;
            m.StageDef = PickStage();
            Current = m;
        }

        static int Clamp(int ai) => Math.Max(1, Math.Min(8, ai));

        static void Fill(PlayerSetup p, RosterChar c, int pal) {
            if (c == null) return;
            p.CharGroup = c.CharGroup;
            p.CharDef = c.CharDef;
            p.Palette = pal;
        }

        /// <summary>Chosen stage, else the opponent's own stage (MUGEN arcade), else a random one.</summary>
        string PickStage() {
            if (!string.IsNullOrEmpty(ChosenStage)) return ChosenStage;
            if (P2Char != null && P2Char.Stages.Count > 0 && IsLadder(Mode)) return P2Char.Stages[0];
            if (Roster.Stages.Count > 0) return Roster.Stages[rng.Next(Roster.Stages.Count)].Def;
            return "kfm.def";
        }

        /// <summary>The fight screen reported the end of the match.</summary>
        public FlowStep MatchEnded(MatchResult r) {
            LastResult = r ?? new MatchResult { Aborted = true };
            if (LastResult.Aborted) {
                bool backToSelect = PicksOpponent(Mode);
                return Step = backToSelect ? FlowStep.Select : FlowStep.Title;
            }
            bool p1Won = LastResult.Winner == 0;
            switch (Mode) {
                case GameMode.Arcade:
                    if (!p1Won) return Step = FlowStep.Continue;
                    Wins++;
                    if (MatchIndex + 1 >= Ladder.Count) return Step = FlowStep.WinScreen;
                    return Step = FlowStep.Victory;
                case GameMode.TimeAttack:
                    // no continue: a loss ends the run (Ikemen time attack records only clears)
                    if (!p1Won) return Step = FlowStep.GameOver;
                    Wins++;
                    TotalTicks += Math.Max(0, LastResult.Ticks);
                    if (MatchIndex + 1 >= Ladder.Count) return Step = FlowStep.TimeAttackResults;
                    MatchIndex++;
                    BuildMatch();
                    return Step = FlowStep.Versus;
                case GameMode.Survival:
                    if (!p1Won) return Step = FlowStep.SurvivalResults;
                    Wins++;
                    P1Life = LastResult.P1LifeLeft;
                    int max = Roster.SurvivalMaxMatches[0];
                    if (max > 0 && Wins >= max) return Step = FlowStep.SurvivalResults;
                    MatchIndex++;
                    Ladder.Add(Roster.RandomChar(rng));
                    BuildMatch();
                    return Step = FlowStep.Versus;
                case GameMode.Training:
                    return Step = FlowStep.Select;
                default:                       // Versus, Watch: show who won, then pick again
                    return Step = FlowStep.Victory;
            }
        }

        /// <summary>The victory screen ended.</summary>
        public FlowStep VictoryDone() {
            if (Mode == GameMode.Arcade) {
                MatchIndex++;
                BuildMatch();
                return Step = FlowStep.Versus;
            }
            return Step = FlowStep.Select;
        }

        /// <summary>Continue screen answer: yes = the same opponent again, no = game over.</summary>
        public FlowStep ContinueAnswered(bool yes) {
            if (!yes) return Step = FlowStep.GameOver;
            Continues++;
            BuildMatch();
            return Step = FlowStep.Versus;
        }

        /// <summary>Game over / win screen / survival results end on the title.</summary>
        public FlowStep ResultsDone() => Step = FlowStep.Title;
    }
}
