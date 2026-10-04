using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>The 14 logical inputs of a MUGEN player, as a bit field.</summary>
    [Flags]
    public enum CmdKey {
        None = 0,
        U = 1 << 0, D = 1 << 1, B = 1 << 2, F = 1 << 3,
        a = 1 << 4, b = 1 << 5, c = 1 << 6,
        x = 1 << 7, y = 1 << 8, z = 1 << 9,
        s = 1 << 10, d = 1 << 11, w = 1 << 12, m = 1 << 13
    }

    /// <summary>One key inside a command step, with its MUGEN prefixes.</summary>
    public struct CmdStepKey {
        public CmdKey Key;
        public bool Release;      // ~   (key released)
        public bool Hold;         // /   (key must be held during this step)
        public bool Loose;        // $   (direction only has to be part of the current direction)
        public int ChargeTime;    // ~30D
        public override string ToString() =>
            (Release ? "~" : "") + (Hold ? "/" : "") + (Loose ? "$" : "") +
            (ChargeTime > 0 ? ChargeTime.ToString() : "") + Key;
    }

    /// <summary>One comma-separated step; keys inside it are simultaneous (`+`) or alternatives (`|`).</summary>
    public class CmdStep {
        public readonly List<CmdStepKey> Keys = new List<CmdStepKey>();
        public bool OrLogic;      // "x|y"
        public bool Strict;       // ">" : nothing else may be pressed in between
    }

    /// <summary>A command from the .cmd file plus its matching state.</summary>
    public class MugenCommand {
        public string Name = "";
        public string Source = "";
        public readonly List<CmdStep> Steps = new List<CmdStep>();
        public int Time = 15;         // ticks allowed for the whole command
        public int BufferTime = 1;    // ticks the command stays "true" after completing

        // runtime state
        internal int step;            // next step to satisfy
        internal int timer;           // ticks since the first step matched
        internal int buffer;          // ticks left while the command counts as pressed

        public bool IsActive => buffer > 0;

        public void Clear() { step = 0; timer = 0; buffer = 0; }
    }

    /// <summary>
    /// Reads a .cmd file: `[Defaults]`, every `[Command]` block, and the `[Statedef -1]`
    /// state controllers that turn commands into state changes (parsed by
    /// <see cref="CnsFile"/>, which understands the same text).
    /// </summary>
    public class CmdFile {
        public readonly List<MugenCommand> Commands = new List<MugenCommand>();
        public CnsFile States;            // the [Statedef -1] block (and any other state in the file)
        public int DefaultTime = 15, DefaultBufferTime = 1;

        public static CmdFile Parse(byte[] bytes) => Parse(MugenDef.DecodeText(bytes));

        public static CmdFile Parse(string text) {
            var cmd = new CmdFile { States = CnsFile.Parse(text) };
            string section = null;
            MugenCommand current = null;

            foreach (var raw in MugenDef.SplitLines(text)) {
                var line = MugenDef.StripComment(raw).Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[') {
                    int close = line.IndexOf(']');
                    section = (close > 0 ? line.Substring(1, close - 1) : line.Substring(1)).Trim().ToLowerInvariant();
                    if (section == "command") {
                        current = new MugenCommand { Time = cmd.DefaultTime, BufferTime = cmd.DefaultBufferTime };
                        cmd.Commands.Add(current);
                    } else current = null;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                if (section == "defaults") {
                    if (key == "command.time") cmd.DefaultTime = Math.Max(1, MugenDef.Atoi(value));
                    else if (key == "command.buffer.time") cmd.DefaultBufferTime = Math.Max(1, MugenDef.Atoi(value));
                    continue;
                }
                if (current == null) continue;
                switch (key) {
                    case "name": current.Name = MugenDef.Unquote(value); break;
                    case "command": current.Source = value; ParseSteps(current, value); break;
                    case "time": current.Time = Math.Max(1, MugenDef.Atoi(value)); break;
                    case "buffer.time": current.BufferTime = Math.Max(1, MugenDef.Atoi(value)); break;
                }
            }
            return cmd;
        }

        public MugenCommand Get(string name) {
            foreach (var c in Commands)
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>Compiles `~D, DF, F, x` style text into steps.</summary>
        public static void ParseSteps(MugenCommand cmd, string text) {
            cmd.Steps.Clear();
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var rawStep in text.Split(',')) {
                var stepText = rawStep.Trim();
                if (stepText.Length == 0) continue;
                var step = new CmdStep();
                if (stepText[0] == '>') { step.Strict = true; stepText = stepText.Substring(1).Trim(); }
                string[] parts;
                if (stepText.IndexOf('|') >= 0) { step.OrLogic = true; parts = stepText.Split('|'); }
                else parts = stepText.Split('+');
                foreach (var rawPart in parts) {
                    var part = rawPart.Trim();
                    if (part.Length == 0) continue;
                    var k = new CmdStepKey();
                    int i = 0;
                    while (i < part.Length) {
                        char c = part[i];
                        if (c == '~') { k.Release = true; i++; continue; }
                        if (c == '/') { k.Hold = true; i++; continue; }
                        if (c == '$') { k.Loose = true; i++; continue; }
                        if (c == '>') { step.Strict = true; i++; continue; }
                        if (c >= '0' && c <= '9') {
                            int n = 0;
                            while (i < part.Length && part[i] >= '0' && part[i] <= '9') { n = n * 10 + (part[i] - '0'); i++; }
                            k.ChargeTime = n;
                            continue;
                        }
                        break;
                    }
                    var rest = part.Substring(i).Trim();
                    if (rest.Length == 0) continue;
                    k.Key = KeyOf(rest);
                    if (k.Key != CmdKey.None) step.Keys.Add(k);
                }
                if (step.Keys.Count > 0) cmd.Steps.Add(step);
            }
        }

        static CmdKey KeyOf(string s) {
            switch (s) {
                case "U": return CmdKey.U;
                case "D": return CmdKey.D;
                case "B": return CmdKey.B;
                case "F": return CmdKey.F;
                case "DB": return CmdKey.D | CmdKey.B;
                case "DF": return CmdKey.D | CmdKey.F;
                case "UB": return CmdKey.U | CmdKey.B;
                case "UF": return CmdKey.U | CmdKey.F;
                case "a": return CmdKey.a;
                case "b": return CmdKey.b;
                case "c": return CmdKey.c;
                case "x": return CmdKey.x;
                case "y": return CmdKey.y;
                case "z": return CmdKey.z;
                case "s": return CmdKey.s;
                case "d": return CmdKey.d;
                case "w": return CmdKey.w;
                case "m": return CmdKey.m;
            }
            return CmdKey.None;
        }
    }

    /// <summary>
    /// Matches the commands of a <see cref="CmdFile"/> against the 60 Hz input stream.
    ///
    /// Each command walks its steps: a step is satisfied on the tick its keys are newly
    /// pressed (or released, for `~`), held keys only have to be down, `$` directions match
    /// any direction containing them, and `>` forbids any other new input between two steps.
    /// A command that does not complete within `time` ticks restarts. A completed command
    /// answers true for `buffer.time` ticks, which is what `command = "x"` reads in a trigger.
    /// This is the behaviour of Ikemen's `Command.Step`, without its netplay bookkeeping.
    /// </summary>
    public class CommandEngine {
        readonly List<MugenCommand> commands = new List<MugenCommand>();
        CmdKey previous, current;
        readonly Dictionary<CmdKey, int> heldSince = new Dictionary<CmdKey, int>();
        int tick;

        public IReadOnlyList<MugenCommand> Commands => commands;

        public CommandEngine(CmdFile file) {
            if (file != null) commands.AddRange(file.Commands);
        }

        public void Reset() {
            foreach (var c in commands) c.Clear();
            previous = current = CmdKey.None;
            heldSince.Clear();
            tick = 0;
        }

        /// <summary>True while the named command is buffered.</summary>
        public bool Active(string name) {
            foreach (var c in commands)
                if (c.IsActive && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Feeds one tick of input. B/F must already be facing-relative.</summary>
        public void Step(CmdKey keys) {
            previous = current;
            current = keys;
            tick++;

            foreach (CmdKey k in AllKeys) {
                if ((current & k) != 0) {
                    if (!heldSince.ContainsKey(k)) heldSince[k] = tick;
                } else heldSince.Remove(k);
            }

            bool newInput = (current & ~previous) != 0 || (previous & ~current) != 0;

            foreach (var c in commands) {
                if (c.buffer > 0) c.buffer--;
                if (c.Steps.Count == 0) continue;

                if (c.step > 0) {
                    c.timer++;
                    if (c.timer > c.Time) { c.step = 0; c.timer = 0; }
                }

                // a strict step is broken by any other new input
                if (c.step > 0 && c.step < c.Steps.Count && c.Steps[c.step].Strict && newInput &&
                    !StepSatisfied(c.Steps[c.step])) {
                    c.step = 0;
                    c.timer = 0;
                }

                while (c.step < c.Steps.Count && StepSatisfied(c.Steps[c.step])) {
                    c.step++;
                    if (c.step == 1) c.timer = 0;
                    if (c.step >= c.Steps.Count) {
                        c.buffer = c.BufferTime;
                        c.step = 0;
                        c.timer = 0;
                        break;
                    }
                    // several steps can only complete in one tick when the next one is a hold
                    if (!IsHoldOnly(c.Steps[c.step])) break;
                }
            }
        }

        static bool IsHoldOnly(CmdStep step) {
            foreach (var k in step.Keys) if (!k.Hold) return false;
            return true;
        }

        bool StepSatisfied(CmdStep step) {
            if (step.Keys.Count == 0) return false;
            if (step.OrLogic) {
                foreach (var k in step.Keys) if (KeySatisfied(k)) return true;
                return false;
            }
            foreach (var k in step.Keys) if (!KeySatisfied(k)) return false;
            return true;
        }

        bool KeySatisfied(CmdStepKey k) {
            bool isDirection = (k.Key & (CmdKey.U | CmdKey.D | CmdKey.B | CmdKey.F)) != 0;
            bool nowDown, wasDown;
            if (k.Loose && isDirection) {
                nowDown = (current & k.Key) == k.Key;
                wasDown = (previous & k.Key) == k.Key;
            } else if (isDirection) {
                // an exact direction: the pressed directions must be exactly these
                var mask = CmdKey.U | CmdKey.D | CmdKey.B | CmdKey.F;
                nowDown = (current & mask) == (k.Key & mask);
                wasDown = (previous & mask) == (k.Key & mask);
            } else {
                nowDown = (current & k.Key) == k.Key;
                wasDown = (previous & k.Key) == k.Key;
            }

            if (k.Release) {
                if (k.ChargeTime > 0) {
                    // "~30D": the key must have been held for 30 ticks before the release
                    return wasDown && !nowDown && releasedAfter >= k.ChargeTime;
                }
                return wasDown && !nowDown;
            }
            if (k.Hold) return nowDown;
            if (k.ChargeTime > 0) return nowDown && heldSince.TryGetValue(k.Key, out var since) && tick - since >= k.ChargeTime;
            return nowDown && !wasDown;
        }

        int releasedAfter {
            get {
                // how long the keys that were released this tick had been held
                int best = 0;
                foreach (CmdKey k in AllKeys) {
                    if ((previous & k) != 0 && (current & k) == 0 && lastHeldLength.TryGetValue(k, out var len))
                        best = Math.Max(best, len);
                }
                return best;
            }
        }

        readonly Dictionary<CmdKey, int> lastHeldLength = new Dictionary<CmdKey, int>();

        static readonly CmdKey[] AllKeys = {
            CmdKey.U, CmdKey.D, CmdKey.B, CmdKey.F, CmdKey.a, CmdKey.b, CmdKey.c,
            CmdKey.x, CmdKey.y, CmdKey.z, CmdKey.s, CmdKey.d, CmdKey.w, CmdKey.m
        };

        /// <summary>Call before <see cref="Step"/> to keep charge bookkeeping accurate.</summary>
        public void TrackCharges(CmdKey keys) {
            foreach (CmdKey k in AllKeys) {
                if ((keys & k) != 0) {
                    lastHeldLength.TryGetValue(k, out var len);
                    lastHeldLength[k] = len + 1;
                } else if ((current & k) != 0) {
                    // key just went up: keep the length until the release is consumed
                } else lastHeldLength[k] = 0;
            }
        }
    }
}
