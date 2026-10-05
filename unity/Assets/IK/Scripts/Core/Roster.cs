using System;
using System.Collections.Generic;

namespace IK.Core {
    // `select.def` reader (MUGEN 1.0 select.def format as Ikemen GO v1.0.0 reads it:
    // engine/ikemen-go/src/script.go `selectDef` parsing + select_params.go): the
    // [Characters] cells, `randomselect`, per-character stage / order params, [ExtraStages]
    // and [Options] `<mode>.maxmatches`.
    //
    // Pure C#: names and paths only. Display names and portraits are filled in by the
    // front end from each character's own .def / .sff (see RosterChar.DisplayName).

    /// <summary>One cell of the select grid.</summary>
    public class RosterChar {
        /// <summary>The entry as written ("kfm", "kfm/kfm720.def", "randomselect").</summary>
        public string Entry = "";
        /// <summary>Resources group of the character ("chars/kfm720"), empty for random/empty cells.</summary>
        public string CharGroup = "";
        /// <summary>Def file inside the group ("kfm720.def").</summary>
        public string CharDef = "";
        public bool Random;
        public bool Empty;
        /// <summary>Arcade order 1..10 (`order = n`, MUGEN default 1). 0 or less = never an arcade opponent.</summary>
        public int Order = 1;
        /// <summary>Stage defs listed on the line, relative to the stages group ("kfm.def").</summary>
        public readonly List<string> Stages = new List<string>();
        public string Music = "";
        /// <summary>`includestage = 0` keeps the char's stage out of the stage select list.</summary>
        public bool IncludeStage = true;
        /// <summary>Filled by the front end from the char's .def ([Info] displayname / name).</summary>
        public string DisplayName = "";
        public string Author = "";
        public int LocalCoordWidth = 320;
        public bool Selectable => !Random && !Empty;

        public override string ToString() => Random ? "randomselect" : (CharGroup + "/" + CharDef);
    }

    public class RosterStage {
        /// <summary>The entry as written ("stages/stage0.def").</summary>
        public string Entry = "";
        /// <summary>Def file inside the "stages" Resources group ("stage0.def").</summary>
        public string Def = "";
        public string DisplayName = "";
        public override string ToString() => Def;
    }

    public class Roster {
        public readonly List<RosterChar> Cells = new List<RosterChar>();
        public readonly List<RosterStage> Stages = new List<RosterStage>();
        /// <summary>[Options] arcade.maxmatches: how many opponents of order 1, 2, ... 10.</summary>
        public int[] ArcadeMaxMatches = { 6, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
        /// <summary>[Options] survival.maxmatches; -1 in the first slot = endless.</summary>
        public int[] SurvivalMaxMatches = { -1, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        /// <summary>Entries dropped by <see cref="Filter"/> with the reason, for the report/UI.</summary>
        public readonly List<string> Skipped = new List<string>();

        /// <summary>Every playable character (no random / empty cells), in cell order.</summary>
        public List<RosterChar> Characters {
            get {
                var r = new List<RosterChar>();
                foreach (var c in Cells) if (c.Selectable) r.Add(c);
                return r;
            }
        }

        public static Roster Parse(byte[] bytes) => Parse(MugenDef.DecodeText(bytes));

        public static Roster Parse(string text) {
            var r = new Roster();
            string section = "";
            foreach (var raw in MugenDef.SplitLines(text ?? "")) {
                var line = MugenDef.StripComment(raw).Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[') {
                    int end = line.IndexOf(']');
                    section = (end > 0 ? line.Substring(1, end - 1) : line.Substring(1)).Trim().ToLowerInvariant();
                    continue;
                }
                switch (section) {
                    case "characters": r.AddCharLine(line); break;
                    case "extrastages": r.AddStage(line.Split(',')[0].Trim()); break;
                    case "options": r.ReadOption(line); break;
                }
            }
            // MUGEN: stages named on character lines join the stage list (unless includestage=0)
            foreach (var c in r.Cells)
                if (c.IncludeStage) foreach (var st in c.Stages) r.AddStage("stages/" + st);
            return r;
        }

        void AddCharLine(string line) {
            var parts = MugenDef.SplitCsv(line);
            var name = parts[0].Trim();
            if (name.Length == 0) return;
            var c = new RosterChar { Entry = name };
            string low = name.ToLowerInvariant();
            if (low == "randomselect") c.Random = true;
            else if (low == "emptyslot" || low == "blank") c.Empty = true;
            else ResolveCharPath(name, out c.CharGroup, out c.CharDef);
            for (int i = 1; i < parts.Length; i++) {
                var p = parts[i];
                if (p.Length == 0) continue;
                int eq = p.IndexOf('=');
                if (eq > 0) {
                    var key = p.Substring(0, eq).Trim().ToLowerInvariant();
                    var val = p.Substring(eq + 1).Trim();
                    if (key == "order") c.Order = MugenDef.Atoi(val);
                    else if (key == "music") c.Music = val;
                    else if (key == "includestage") c.IncludeStage = MugenDef.Atoi(val) != 0;
                } else if (p.EndsWith(".def", StringComparison.OrdinalIgnoreCase)) {
                    c.Stages.Add(StageDefName(p));
                }
            }
            Cells.Add(c);
        }

        /// <summary>
        /// MUGEN char entry -> Resources group + def: "kfm" = chars/kfm/kfm.def,
        /// "kfm/kfm720.def" = chars/kfm/kfm720.def, "kfm/" = chars/kfm/kfm.def.
        /// </summary>
        public static void ResolveCharPath(string entry, out string group, out string def) {
            var e = entry.Replace('\\', '/').Trim().Trim('"');
            if (e.StartsWith("chars/", StringComparison.OrdinalIgnoreCase)) e = e.Substring(6);
            if (e.EndsWith(".def", StringComparison.OrdinalIgnoreCase)) {
                int slash = e.LastIndexOf('/');
                if (slash < 0) {               // "kfm.def": MUGEN looks in chars/kfm/
                    def = e;
                    group = "chars/" + e.Substring(0, e.Length - 4);
                } else {
                    def = e.Substring(slash + 1);
                    group = "chars/" + e.Substring(0, slash);
                }
            } else {
                e = e.TrimEnd('/');
                group = "chars/" + e;
                int slash = e.LastIndexOf('/');
                def = (slash >= 0 ? e.Substring(slash + 1) : e) + ".def";
            }
        }

        /// <summary>"stages/kfm.def" -> "kfm.def" (the stages Resources group is flat).</summary>
        public static string StageDefName(string entry) {
            var e = entry.Replace('\\', '/').Trim().Trim('"');
            int slash = e.LastIndexOf('/');
            return slash >= 0 ? e.Substring(slash + 1) : e;
        }

        void AddStage(string entry) {
            if (string.IsNullOrEmpty(entry)) return;
            var def = StageDefName(entry);
            foreach (var s in Stages) if (string.Equals(s.Def, def, StringComparison.OrdinalIgnoreCase)) return;
            Stages.Add(new RosterStage { Entry = entry, Def = def });
        }

        void ReadOption(string line) {
            int eq = line.IndexOf('=');
            if (eq < 0) return;
            var key = line.Substring(0, eq).Trim().ToLowerInvariant();
            var val = line.Substring(eq + 1).Trim();
            if (key == "arcade.maxmatches") ArcadeMaxMatches = ReadTen(val);
            else if (key == "survival.maxmatches") SurvivalMaxMatches = ReadTen(val);
        }

        static int[] ReadTen(string val) {
            var r = new int[10];
            var p = MugenDef.SplitCsv(val);
            for (int i = 0; i < p.Length && i < 10; i++) r[i] = MugenDef.Atoi(p[i]);
            return r;
        }

        /// <summary>
        /// Drops what this port cannot run: characters whose state files are ZSS scripts (or
        /// whose def cannot be read) and stages that need a 3D model. The predicates come from
        /// the caller, which can read the files (Resources on device, disk in tests).
        /// Empty-handed rosters keep their random cell so the screen never shows nothing.
        /// </summary>
        public void Filter(Func<RosterChar, string> charProblem, Func<RosterStage, string> stageProblem) {
            for (int i = Cells.Count - 1; i >= 0; i--) {
                var c = Cells[i];
                if (!c.Selectable || charProblem == null) continue;
                var why = charProblem(c);
                if (why != null) { Skipped.Insert(0, c.Entry + ": " + why); Cells.RemoveAt(i); }
            }
            for (int i = Stages.Count - 1; i >= 0; i--) {
                var why = stageProblem != null ? stageProblem(Stages[i]) : null;
                if (why != null) { Skipped.Insert(0, Stages[i].Entry + ": " + why); Stages.RemoveAt(i); }
            }
            // a character's assigned stage that is gone falls back to the stage list
            var ok = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in Stages) ok.Add(s.Def);
            foreach (var c in Cells) c.Stages.RemoveAll(s => !ok.Contains(s));
        }

        /// <summary>
        /// Set to true by the engine once ZSS state files run; until then ZSS characters
        /// (kfm_zss, kfm_zaxis) are kept out of the roster.
        /// </summary>
        public static bool ZssSupported = true;

        /// <summary>
        /// Why a character def cannot run on this engine, or null when it can: no [Files], or
        /// `st*` / `cns` entries that are ZSS scripts (Ikemen's own language, not ported).
        /// </summary>
        public static string CharDefProblem(MugenDef def) {
            if (def == null) return "def not found";
            var files = def["files"];
            if (files == null) return "no [Files]";
            foreach (var kv in files.Lines) {
                var k = kv.Key;
                if (k == "cns" || k == "stcommon" || (k.StartsWith("st") && k != "stcommon")) {
                    var v = MugenDef.Unquote(kv.Value).ToLowerInvariant();
                    if (k == "stcommon") continue;          // the engine supplies the common states
                    if (v.EndsWith(".zss") && !ZssSupported) return "ZSS states (" + v + ") are not supported";
                }
            }
            if (MugenDef.Unquote(files.Get("sprite")).Length == 0) return "no sprite file";
            return null;
        }

        /// <summary>Why a stage def cannot be drawn, or null: a 3D `model` in [BGdef].</summary>
        public static string StageDefProblem(MugenDef def) {
            if (def == null) return "def not found";
            var bg = def["bgdef"];
            if (bg != null && MugenDef.Unquote(bg.Get("model")).Length > 0) return "3D stage model";
            return null;
        }

        /// <summary>
        /// Arcade ladder (MUGEN): for each order slot i, `maxmatches[i]` opponents of order i+1,
        /// picked at random without repeats while possible. Characters with order &lt;= 0 never
        /// appear. When an order has no characters its matches are skipped, like MUGEN.
        /// </summary>
        public List<RosterChar> ArcadeLadder(Random rng, RosterChar player = null) {
            var ladder = new List<RosterChar>();
            var chars = Characters;
            for (int order = 1; order <= 10; order++) {
                int count = ArcadeMaxMatches[order - 1];
                if (count <= 0) continue;
                var pool = chars.FindAll(c => c.Order == order);
                if (pool.Count == 0) continue;
                var bag = new List<RosterChar>();
                for (int i = 0; i < count; i++) {
                    if (bag.Count == 0) bag.AddRange(pool);
                    int k = rng.Next(bag.Count);
                    ladder.Add(bag[k]);
                    bag.RemoveAt(k);
                }
            }
            return ladder;
        }

        /// <summary>A random playable character (for the random cell and survival opponents).</summary>
        public RosterChar RandomChar(Random rng) {
            var chars = Characters;
            return chars.Count == 0 ? null : chars[rng.Next(chars.Count)];
        }
    }
}
