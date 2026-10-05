using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using IK.Input;

namespace IK.UI {
    /// <summary>
    /// Training-style input display: the last 20 logic ticks as MUGEN notation plus the
    /// commands the recogniser accepted. This is the visible proof that the touch layer
    /// produces real command input at 60 Hz.
    /// </summary>
    public class InputDisplay : MonoBehaviour {
        public int maxEntries = 20;
        Text inputs, commands, tickLabel;
        readonly List<string> entries = new List<string>();
        readonly CommandRecognizer recognizer = CommandRecognizer.Kfm();
        InputFrame last;

        public CommandRecognizer Recognizer => recognizer;
        public IReadOnlyList<string> Entries => entries;

        public void Build(RectTransform parent) {
            var panel = UIKit.Panel(parent, "InputDisplay", new Vector2(0, 0), new Vector2(0, 0),
                                    Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.45f));
            // kept clear of the top HUD strip (START / PAUSE live there) and of the controls
            panel.anchorMin = new Vector2(0.32f, 0.17f);
            panel.anchorMax = new Vector2(0.70f, 0.78f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;

            UIKit.Text(panel, "title", new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(380, 40),
                       Loc.T("test.title"), 26, TextAnchor.MiddleCenter, Skin.Highlight);
            tickLabel = UIKit.Text(panel, "tick", new Vector2(0.5f, 1f), new Vector2(0, -58), new Vector2(380, 30),
                                   "tick 0", 18, TextAnchor.MiddleCenter, Skin.Text);
            inputs = UIKit.Text(panel, "inputs", new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(380, 260),
                                "", 22, TextAnchor.UpperCenter, Skin.Text);
            commands = UIKit.Text(panel, "commands", new Vector2(0.5f, 0f), new Vector2(0, 70), new Vector2(380, 130),
                                  "", 22, TextAnchor.LowerCenter, Skin.Accent);
        }

        public void Feed(InputFrame f, int tick) {
            recognizer.Push(f);
            if (f.ToBits() != last.ToBits()) {
                if (f.Any) {
                    entries.Add(f.ToString());
                    if (entries.Count > maxEntries) entries.RemoveAt(0);
                }
                last = f;
            }
            if (inputs != null) {
                var sb = new StringBuilder();
                for (int i = entries.Count - 1; i >= 0; i--) sb.AppendLine(entries[i]);
                inputs.text = sb.ToString();
            }
            if (commands != null) {
                var sb = new StringBuilder();
                var list = recognizer.Recognised;
                for (int i = list.Count - 1; i >= 0 && i > list.Count - 6; i--) sb.AppendLine(list[i]);
                commands.text = sb.Length > 0 ? sb.ToString() : Loc.Shape(Loc.T("test.hint"));
            }
            if (tickLabel != null) tickLabel.text = "tick " + tick;
        }
    }
}
