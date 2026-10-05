using System;
using UnityEngine;
using IK.Core;
using IK.UI;

namespace IK.App {
    /// <summary>
    /// The one seam between the front end and the fight: starts a match on the
    /// <see cref="FightScreen"/> with
    /// <c>public void StartMatch(MatchSetup setup, System.Action&lt;MatchResult&gt; onEnd)</c>.
    /// Contract relied on: the fight loads both characters (any Resources char group) and the
    /// stage named in the setup, runs it with <see cref="CpuAI"/> for <c>AiLevel &gt; 0</c>
    /// players, and calls <c>onEnd</c> exactly once — after the winner's pose, or with
    /// <c>Aborted = true</c> when the player exits from the pause menu or the load fails
    /// (possibly synchronously, inside StartMatch).
    /// </summary>
    public static class FightLauncher {
        public static bool Start(FightScreen screen, MatchSetup setup, Action<MatchResult> onEnd) {
            if (screen == null || setup == null) return false;
            try {
                screen.StartMatch(setup, onEnd);
                return true;
            } catch (Exception e) {
                Debug.LogError("[IK] FightScreen.StartMatch failed: " + e);
                return false;
            }
        }
    }
}
