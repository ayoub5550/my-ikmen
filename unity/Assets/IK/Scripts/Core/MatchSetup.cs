using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>Game modes of the dev.5 front end (Ikemen GO `main.lua` modes we ship).</summary>
    public enum GameMode { Arcade, Versus, Training, Survival, Watch }

    /// <summary>
    /// Everything the fight screen needs to start one match. Built by the select screens and
    /// handed to <c>FightScreen.StartMatch</c>; the fight never decides who plays by itself.
    ///
    /// Characters are named by their Resources group and def file, exactly as
    /// <see cref="ResourcesSource"/> expects: group "chars/kfm", def "kfm.def".
    /// Stages are named by their def file inside the "stages" group ("kfm.def").
    /// </summary>
    public class MatchSetup {
        public GameMode Mode = GameMode.Versus;
        public readonly PlayerSetup[] Players = { new PlayerSetup(), new PlayerSetup() };
        public string StageDef = "kfm.def";
        /// <summary>Rounds needed to win (`roundstowin`). 0 = endless (training).</summary>
        public int RoundsToWin = 2;
        /// <summary>Round time in counts; -1 = no timer.</summary>
        public int RoundTime = 99;
        /// <summary>Match number inside an arcade/survival run (1-based, `matchno`).</summary>
        public int MatchNo = 1;
        /// <summary>Survival: P1 life carries over between matches (null = full life).</summary>
        public int? P1StartLife;
        /// <summary>Optional music file for this match (Resources group "sound"), "" = stage's own.</summary>
        public string Music = "";
    }

    public class PlayerSetup {
        public string CharGroup = "chars/kfm";
        public string CharDef = "kfm.def";
        /// <summary>Palette number 1..12 (`[Files] pal1..pal12`).</summary>
        public int Palette = 1;
        /// <summary>0 = human, 1..8 = CPU difficulty (MUGEN `AILevel`).</summary>
        public int AiLevel;
    }

    /// <summary>What the fight screen reports back when a match ends.</summary>
    public class MatchResult {
        /// <summary>0 = player 1 won, 1 = player 2 won, -1 = draw.</summary>
        public int Winner = -1;
        public int[] RoundsWon = { 0, 0 };
        public int P1LifeLeft;
        /// <summary>True when the player left the fight from the pause menu.</summary>
        public bool Aborted;
        public int Ticks;
    }
}
