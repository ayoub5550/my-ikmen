using System;
using System.Collections.Generic;
using System.Text;

namespace IK.Core {
    // Loader for the screenpack motif `system.def` (MUGEN 1.0 / Ikemen GO v1.0.0).
    // Reference: engine/ikemen-go/src/motif.go (the `Motif` struct, its `ini:` tags and the
    // per-screen property structs: TitleInfo, SelectInfo, VsScreen, VictoryScreen,
    // ContinueScreen, WinScreen, SurvivalResultsScreen) and the MUGEN 1.0 system.def docs.
    //
    // Loader only: no UnityEngine. The screens read these numbers and draw them; every
    // coordinate is in the motif's own `[Info] localcoord` space (1280x720 for ikemen1).
    // Text/anim elements reuse the fight-screen readers (FightText, FightAnimLayout) because
    // Ikemen GO reads both files with the same `Layout` / `AnimLayout` / `TextProperties` code.

    /// <summary>Tries several sources in order (Ikemen's motif search path: motif dir, data/, font/).</summary>
    public class SearchPathSource : IResourceSource {
        readonly IResourceSource[] sources;
        public SearchPathSource(params IResourceSource[] sources) { this.sources = sources; }

        public byte[] Read(string fileName) {
            if (string.IsNullOrEmpty(fileName)) return null;
            foreach (var s in sources) {
                if (s == null) continue;
                var b = s.Read(fileName);
                if (b != null) return b;
                // a motif path such as "ikemen1/fonts/Menu1.def" is also tried by its bare name
                var bare = BareName(fileName);
                if (bare != fileName) {
                    b = s.Read(bare);
                    if (b != null) return b;
                }
            }
            return null;
        }

        public static string BareName(string path) {
            var p = path.Replace('\\', '/');
            int i = p.LastIndexOf('/');
            return i >= 0 ? p.Substring(i + 1) : p;
        }
    }

    /// <summary>`[Title Info]`: the main menu.</summary>
    public class MotifTitle {
        public int FadeInTime, FadeOutTime;
        public float[] MenuPos = new float[2];
        public FightText ItemFont, ActiveFont;
        public float[] ItemSpacing = new float[2];
        public int VisibleItems = 6;
        public int[] WindowMarginsY = new int[2];
        public int[] CursorMoveSnd = { -1, 0 }, CursorDoneSnd = { -1, 0 }, CancelSnd = { -1, 0 };
        public FightText Loading;
        readonly Dictionary<string, string> itemNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>`menu.itemname.&lt;key&gt;` as written (e.g. "menuarcade" -> "ARCADE"), or the fallback.</summary>
        public string ItemName(string key, string fallback = "") =>
            itemNames.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;

        public int ItemNameCount => itemNames.Count;

        internal void Read(MugenDef.Section s) {
            if (s == null) { ItemFont = FightText.Read(null, "", "", 2, 0); ActiveFont = ItemFont; return; }
            FightIni.ReadInt(s, "fadein.time", ref FadeInTime);
            FightIni.ReadInt(s, "fadeout.time", ref FadeOutTime);
            FightIni.ReadFloats(s, "menu.pos", MenuPos);
            ItemFont = FightText.Read(s, "menu.item.", "", 2, 0);
            ActiveFont = FightText.Read(s, "menu.item.active.", "", 2, 0);
            if (ActiveFont.FontIndex < 0) ActiveFont = ItemFont;
            FightIni.ReadFloats(s, "menu.item.spacing", ItemSpacing);
            FightIni.ReadInt(s, "menu.window.visibleitems", ref VisibleItems);
            FightIni.ReadInts(s, "menu.window.margins.y", WindowMarginsY);
            FightIni.ReadInts(s, "cursor.move.snd", CursorMoveSnd);
            FightIni.ReadInts(s, "cursor.done.snd", CursorDoneSnd);
            FightIni.ReadInts(s, "cancel.snd", CancelSnd);
            Loading = FightText.Read(s, "loading.", "", 2, 0);
            const string pre = "menu.itemname.";
            foreach (var kv in s.Lines)
                if (kv.Key.StartsWith(pre, StringComparison.Ordinal))
                    itemNames[kv.Key.Substring(pre.Length)] = kv.Value;
        }
    }

    /// <summary>Per-player part of `[Select Info]` (`p1.` / `p2.`).</summary>
    public class MotifSelectPlayer {
        public int[] StartCell = new int[2];
        public FightAnimLayout CursorActive, CursorDone;
        public int[] CursorMoveSnd = { -1, 0 }, CursorDoneSnd = { -1, 0 }, RandomMoveSnd = { -1, 0 };
        /// <summary>Big portrait: `pN.face.anim` is a CHARACTER action (0 = stand) in ikemen1.</summary>
        public FightAnimLayout Face, Face2, FaceRandom, FaceDone;
        public FightText Name;
        public float[] NameSpacing = new float[2];
        public float[] PalMenuPos = new float[2];
        public FightText PalText, PalNumber;
        public int[] PalValueSnd = { -1, 0 }, PalDoneSnd = { -1, 0 }, PalCancelSnd = { -1, 0 };

        internal void Read(MugenDef.Section s, string p, AirFile table) {
            FightIni.ReadInts(s, p + "cursor.startcell", StartCell);
            CursorActive = FightAnimLayout.Read(s, p + "cursor.active.", table, 2);
            CursorDone = FightAnimLayout.Read(s, p + "cursor.done.", table, 2);
            FightIni.ReadInts(s, p + "cursor.move.snd", CursorMoveSnd);
            FightIni.ReadInts(s, p + "cursor.done.snd", CursorDoneSnd);
            FightIni.ReadInts(s, p + "random.move.snd", RandomMoveSnd);
            Face = FightAnimLayout.Read(s, p + "face.", null, 2);
            Face2 = FightAnimLayout.Read(s, p + "face2.", null, 2);
            FaceRandom = FightAnimLayout.Read(s, p + "face.random.", table, 2);
            FaceDone = FightAnimLayout.Read(s, p + "face.done.", null, 2);
            Name = FightText.Read(s, p + "name.", "", 2, 0);
            FightIni.ReadFloats(s, p + "name.spacing", NameSpacing);
            FightIni.ReadFloats(s, p + "palmenu.pos", PalMenuPos);
            PalText = FightText.Read(s, p + "palmenu.text.", "Color", 2, 0);
            PalNumber = FightText.Read(s, p + "palmenu.number.", "", 2, 0);
            FightIni.ReadInts(s, p + "palmenu.value.snd", PalValueSnd);
            FightIni.ReadInts(s, p + "palmenu.done.snd", PalDoneSnd);
            FightIni.ReadInts(s, p + "palmenu.cancel.snd", PalCancelSnd);
        }
    }

    /// <summary>`[Select Info]`: the character and stage select screen.</summary>
    public class MotifSelect {
        public int FadeInTime, FadeOutTime;
        public int Rows = 2, Columns = 5;
        public bool Wrapping, ShowEmptyBoxes, MoveOverEmptyBoxes = true;
        public float[] Pos = new float[2];
        public int[] CellSize = new int[2];
        public float[] CellSpacing = new float[2];
        public FightAnimLayout CellBg, CellRandom;
        public int RandomSwitchTime = 4;
        /// <summary>Small (cell) portrait: `portrait.spr` (9000,0) and its scale.</summary>
        public FightAnimLayout Portrait;
        public FightText Title;
        public readonly MotifSelectPlayer P1 = new MotifSelectPlayer(), P2 = new MotifSelectPlayer();
        public float[] StagePos = new float[2];
        public FightText StageFont, StageActiveFont, StageActive2Font, StageDoneFont;
        public int[] StageMoveSnd = { -1, 0 }, StageDoneSnd = { -1, 0 }, CancelSnd = { -1, 0 };
        public int PaletteSelect;
        readonly Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>`title.&lt;mode&gt;.text` (arcade, versus, training, survival, watch...).</summary>
        public string TitleText(string mode, string fallback = "") =>
            titles.TryGetValue(mode, out var v) && v.Length > 0 ? v : fallback;

        internal void Read(MugenDef.Section s, AirFile table) {
            if (s == null) return;
            FightIni.ReadInt(s, "fadein.time", ref FadeInTime);
            FightIni.ReadInt(s, "fadeout.time", ref FadeOutTime);
            FightIni.ReadInt(s, "rows", ref Rows);
            FightIni.ReadInt(s, "columns", ref Columns);
            FightIni.ReadBool(s, "wrapping", ref Wrapping);
            FightIni.ReadBool(s, "showemptyboxes", ref ShowEmptyBoxes);
            FightIni.ReadBool(s, "moveoveremptyboxes", ref MoveOverEmptyBoxes);
            FightIni.ReadFloats(s, "pos", Pos);
            FightIni.ReadInts(s, "cell.size", CellSize);
            // Go: a single `cell.spacing` value is used for both axes (motif.go fixups)
            var sp = s.Get("cell.spacing", "");
            if (sp.Length > 0) {
                var parts = MugenDef.SplitCsv(sp);
                CellSpacing[0] = MugenDef.Atof(parts[0]);
                CellSpacing[1] = parts.Length > 1 && parts[1].Length > 0 ? MugenDef.Atof(parts[1]) : CellSpacing[0];
            }
            CellBg = FightAnimLayout.Read(s, "cell.bg.", table, 2);
            CellRandom = FightAnimLayout.Read(s, "cell.random.", table, 2);
            FightIni.ReadInt(s, "cell.random.switchtime", ref RandomSwitchTime);
            Portrait = FightAnimLayout.Read(s, "portrait.", null, 2);
            Title = FightText.Read(s, "title.", "", 2, 0);
            P1.Read(s, "p1.", table);
            P2.Read(s, "p2.", table);
            FightIni.ReadFloats(s, "stage.pos", StagePos);
            StageFont = FightText.Read(s, "stage.", "", 2, 0);
            StageActiveFont = FightText.Read(s, "stage.active.", "", 2, 0);
            StageActive2Font = FightText.Read(s, "stage.active2.", "", 2, 0);
            StageDoneFont = FightText.Read(s, "stage.done.", "", 2, 0);
            FightIni.ReadInts(s, "stage.move.snd", StageMoveSnd);
            FightIni.ReadInts(s, "stage.done.snd", StageDoneSnd);
            FightIni.ReadInts(s, "cancel.snd", CancelSnd);
            FightIni.ReadInt(s, "paletteselect", ref PaletteSelect);
            foreach (var kv in s.Lines) {
                if (!kv.Key.StartsWith("title.", StringComparison.Ordinal) || !kv.Key.EndsWith(".text", StringComparison.Ordinal)) continue;
                var mid = kv.Key.Substring(6, kv.Key.Length - 6 - 5);
                if (mid.Length > 0 && !titles.ContainsKey(mid)) titles[mid] = kv.Value;
            }
        }

        /// <summary>
        /// Top-left of cell (col, row) in motif coordinates for a flat grid: `pos + i * (size +
        /// spacing)`. The ikemen1 per-row perspective overrides (`cell.*-N.*`) are not applied.
        /// </summary>
        public float[] CellPos(int col, int row, float scale = 1f) {
            return new[] {
                Pos[0] + col * (CellSize[0] + CellSpacing[0]) * scale,
                Pos[1] + row * (CellSize[1] + CellSpacing[1]) * scale
            };
        }
    }

    /// <summary>Per-player part of `[VS Screen]`.</summary>
    public class MotifVsPlayer {
        /// <summary>`pN.anim` (character action, 0 = stand) or `pN.spr`; offset/scale/facing/window.</summary>
        public FightAnimLayout Portrait;
        public FightText Name;
        internal void Read(MugenDef.Section s, string p) {
            Portrait = FightAnimLayout.Read(s, p, null, 2);
            Name = FightText.Read(s, p + "name.", "", 2, 0);
        }
    }

    /// <summary>`[VS Screen]`.</summary>
    public class MotifVersus {
        public int Time = 150, FadeInTime, FadeOutTime;
        public FightText Match;
        public readonly MotifVsPlayer P1 = new MotifVsPlayer(), P2 = new MotifVsPlayer();
        public float[] StagePos = new float[2];
        public FightText Stage;
        internal void Read(MugenDef.Section s) {
            if (s == null) { Match = FightText.Read(null, "", "", 2, 0); return; }
            FightIni.ReadInt(s, "time", ref Time);
            FightIni.ReadInt(s, "fadein.time", ref FadeInTime);
            FightIni.ReadInt(s, "fadeout.time", ref FadeOutTime);
            Match = FightText.Read(s, "match.", "", 2, 0);
            P1.Read(s, "p1.");
            P2.Read(s, "p2.");
            FightIni.ReadFloats(s, "stage.pos", StagePos);
            Stage = FightText.Read(s, "stage.", "", 2, 0);
        }
    }

    /// <summary>`[Victory Screen]`.</summary>
    public class MotifVictory {
        public bool Enabled;
        public int Time = 300, FadeInTime, FadeOutTime;
        public FightAnimLayout P1Portrait, P2Portrait;
        public FightText P1Name, WinQuote;
        public int P2LoseBrightness = 100;
        internal void Read(MugenDef.Section s) {
            WinQuote = FightText.Read(s, "winquote.", "Winner!", 2, 1);
            P1Name = FightText.Read(s, "p1.name.", "", 2, 1);
            P1Portrait = FightAnimLayout.Read(s, "p1.", null, 2);
            P2Portrait = FightAnimLayout.Read(s, "p2.", null, 1);
            if (s == null) return;
            FightIni.ReadBool(s, "enabled", ref Enabled);
            FightIni.ReadInt(s, "time", ref Time);
            FightIni.ReadInt(s, "fadein.time", ref FadeInTime);
            FightIni.ReadInt(s, "fadeout.time", ref FadeOutTime);
            FightIni.ReadInt(s, "p2.lose.brightness", ref P2LoseBrightness);
        }
    }

    /// <summary>`[Continue Screen]`.</summary>
    public class MotifContinue {
        public bool Enabled;
        public float[] Pos = new float[2];
        public FightText Continue, Yes, YesActive, No, NoActive, Credits;
        public int[] MoveSnd = { -1, 0 }, DoneSnd = { -1, 0 }, CancelSnd = { -1, 0 };
        public FightAnimLayout Counter;
        public int CounterEndTime = 1200, CounterSkipStart;
        /// <summary>`counter.N.skiptime` for N = 9..0, index = N.</summary>
        public readonly int[] CounterSkipTime = new int[10];
        public readonly int[][] CounterSnd = new int[10][];
        internal void Read(MugenDef.Section s, AirFile table) {
            Continue = FightText.Read(s, "continue.", "CONTINUE?", 2, 0);
            Yes = FightText.Read(s, "yes.", "YES", 2, 0);
            YesActive = FightText.Read(s, "yes.active.", "YES", 2, 0);
            No = FightText.Read(s, "no.", "NO", 2, 0);
            NoActive = FightText.Read(s, "no.active.", "NO", 2, 0);
            Credits = FightText.Read(s, "credits.", "", 2, 1);
            Counter = FightAnimLayout.Read(s, "counter.", table, 2);
            for (int i = 0; i < 10; i++) CounterSnd[i] = new[] { -1, 0 };
            if (s == null) return;
            FightIni.ReadBool(s, "enabled", ref Enabled);
            FightIni.ReadFloats(s, "pos", Pos);
            FightIni.ReadInts(s, "move.snd", MoveSnd);
            FightIni.ReadInts(s, "done.snd", DoneSnd);
            FightIni.ReadInts(s, "cancel.snd", CancelSnd);
            FightIni.ReadInt(s, "counter.endtime", ref CounterEndTime);
            FightIni.ReadInt(s, "counter.skipstart", ref CounterSkipStart);
            for (int i = 0; i < 10; i++) {
                FightIni.ReadInt(s, "counter." + i + ".skiptime", ref CounterSkipTime[i]);
                FightIni.ReadInts(s, "counter." + i + ".snd", CounterSnd[i]);
            }
        }

        /// <summary>
        /// The digit the continue counter shows at <paramref name="tick"/>: 9 until
        /// `counter.8.skiptime`, then 8, ... 0, and -1 once `counter.endtime` is reached.
        /// </summary>
        public int DigitAt(int tick) {
            if (tick >= CounterEndTime) return -1;
            int digit = 9;
            for (int d = 8; d >= 0; d--)
                if (CounterSkipTime[d] > 0 && tick >= CounterSkipTime[d]) digit = d;
            return digit;
        }
    }

    /// <summary>`[Win Screen]` (arcade cleared) and `[Survival Results Screen]` share this shape.</summary>
    public class MotifResults {
        public bool Enabled;
        public int FadeInTime, FadeOutTime, ShowTime = 300;
        public FightText Text;
        public int RoundsToWin;
        internal void Read(MugenDef.Section s, string textPrefix, string timeKey, string fallback) {
            Text = FightText.Read(s, textPrefix, fallback, 2, 0);
            if (s == null) return;
            FightIni.ReadBool(s, "enabled", ref Enabled);
            FightIni.ReadInt(s, "fadein.time", ref FadeInTime);
            FightIni.ReadInt(s, "fadeout.time", ref FadeOutTime);
            FightIni.ReadInt(s, timeKey, ref ShowTime);
            FightIni.ReadInt(s, "roundstowin", ref RoundsToWin);
        }
    }

    /// <summary>The whole motif.</summary>
    public class Motif {
        public MugenDef Def;
        public string Text = "";
        public string DefFile = "";
        public string Name = "", Author = "";
        public int[] LocalCoord = { 320, 240 };
        public string SprFile = "", SndFile = "", SelectFile = "", FightFile = "";
        /// <summary>`[Files] fontN` paths by N.</summary>
        public readonly Dictionary<int, string> Fonts = new Dictionary<int, string>();
        /// <summary>Every `[Begin Action n]` of system.def (cursors, VS logo, continue counter...).</summary>
        public AirFile Animations;
        public readonly Dictionary<string, string> Music = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public readonly MotifTitle Title = new MotifTitle();
        public readonly MotifSelect Select = new MotifSelect();
        public readonly MotifVersus Versus = new MotifVersus();
        public readonly MotifVictory Victory = new MotifVictory();
        public readonly MotifContinue Continue = new MotifContinue();
        public readonly MotifResults Win = new MotifResults();
        public readonly MotifResults Survival = new MotifResults();
        public bool GameOverEnabled;

        /// <summary>Loaded by <see cref="Load"/> when asked to; null for a pure parse.</summary>
        public SffFile Sprites;
        public SndFile Sounds;

        public static Motif Parse(byte[] bytes, string defFile = "") => Parse(MugenDef.DecodeText(bytes), defFile);

        public static Motif Parse(string text, string defFile = "") {
            var m = new Motif { Text = text ?? "", DefFile = defFile ?? "" };
            m.Def = MugenDef.Parse(m.Text);
            m.Animations = AirFile.Parse(m.Text);

            var info = m.Def["info"];
            if (info != null) {
                m.Name = MugenDef.Unquote(info.Get("name"));
                m.Author = MugenDef.Unquote(info.Get("author"));
                FightIni.ReadInts(info, "localcoord", m.LocalCoord);
            }
            var files = m.Def["files"];
            if (files != null) {
                m.SprFile = MugenDef.Unquote(files.Get("spr"));
                m.SndFile = MugenDef.Unquote(files.Get("snd"));
                m.SelectFile = MugenDef.Unquote(files.Get("select"));
                m.FightFile = MugenDef.Unquote(files.Get("fight"));
                foreach (var kv in files.Lines) {
                    if (!kv.Key.StartsWith("font", StringComparison.Ordinal)) continue;
                    var num = kv.Key.Substring(4);
                    if (num.Length == 0 || !MugenDef.IsNumeric(num) || num.Contains(".")) continue;
                    var v = MugenDef.Unquote(kv.Value);
                    if (v.Length > 0) m.Fonts[MugenDef.Atoi(num)] = v;
                }
            }
            var music = m.Def["music"];
            if (music != null) foreach (var kv in music.Lines) m.Music[kv.Key] = kv.Value;

            m.Title.Read(m.Def["title info"]);
            m.Select.Read(m.Def["select info"], m.Animations);
            m.Versus.Read(m.Def["vs screen"]);
            m.Victory.Read(m.Def["victory screen"]);
            m.Continue.Read(m.Def["continue screen"], m.Animations);
            m.Win.Read(m.Def["win screen"], "wintext.", "pose.time", "Congratulations!");
            m.Survival.Read(m.Def["survival results screen"], "winstext.", "show.time", "Rounds survived: %i");
            var go = m.Def["game over screen"];
            if (go != null) FightIni.ReadBool(go, "enabled", ref m.GameOverEnabled);
            return m;
        }

        /// <summary>Reads system.def and, when asked, its SFF and SND through <paramref name="source"/>.</summary>
        public static Motif Load(IResourceSource source, string defFile, bool loadSprites = true, bool loadSounds = true) {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var bytes = source.Read(defFile);
            if (bytes == null) throw new System.IO.FileNotFoundException("motif not found: " + defFile);
            var m = Parse(bytes, defFile);
            if (loadSprites && m.SprFile.Length > 0) {
                var sff = source.Read(m.SprFile);
                if (sff != null) m.Sprites = IK.Core.SffFile.Load(sff, false);
            }
            if (loadSounds && m.SndFile.Length > 0) {
                var snd = source.Read(m.SndFile);
                if (snd != null) {
                    try { m.Sounds = IK.Core.SndFile.Load(snd); } catch (Exception) { m.Sounds = null; }
                }
            }
            return m;
        }

        /// <summary>
        /// The `[&lt;prefix&gt;def]` + `[&lt;prefix&gt; name]` blocks rewritten as a stage-style
        /// `[BGdef]` + `[BG name]` text plus every `[Begin Action]`, so the stage BG loader
        /// (<see cref="StageDefinition.Parse(string,string,SffFile)"/>) reads motif backgrounds:
        /// MUGEN uses the same element format for both. Returns "" when the motif has no such block.
        /// </summary>
        public string BackgroundText(string prefix) {
            var sb = new StringBuilder();
            var actions = new StringBuilder();
            sb.Append("[Info]\nmugenversion = 1.0\n");
            bool found = false;
            bool copying = false, inAction = false;
            string defName = (prefix + "def").ToLowerInvariant();
            string elemPrefix = (prefix + " ").ToLowerInvariant();
            foreach (var raw in MugenDef.SplitLines(Text)) {
                var line = MugenDef.StripComment(raw).Trim();
                if (line.Length > 0 && line[0] == '[') {
                    int end = line.IndexOf(']');
                    string name = (end > 0 ? line.Substring(1, end - 1) : line.Substring(1)).Trim();
                    string low = name.ToLowerInvariant();
                    copying = false; inAction = false;
                    if (low == defName) {
                        sb.Append("[BGdef]\n"); copying = true; found = true;
                    } else if (low.StartsWith(elemPrefix, StringComparison.Ordinal)) {
                        sb.Append("[BG ").Append(name.Substring(elemPrefix.Length).Trim()).Append("]\n");
                        copying = true; found = true;
                    } else if (low.StartsWith("begin action", StringComparison.Ordinal)) {
                        actions.Append('[').Append(name).Append("]\n");
                        inAction = true;
                    }
                    continue;
                }
                if (copying) sb.Append(raw).Append('\n');
                else if (inAction) actions.Append(raw).Append('\n');
            }
            if (!found) return "";
            sb.Append(actions);
            return sb.ToString();
        }

        /// <summary>The background block <paramref name="prefix"/> ("TitleBG", "SelectBG"...) as a stage, with the motif sprites.</summary>
        public StageDefinition Background(string prefix) {
            var text = BackgroundText(prefix);
            if (text.Length == 0) return null;
            var stage = StageDefinition.Parse(text, DefFile + "#" + prefix, Sprites);
            stage.Sprites = Sprites;
            return stage;
        }

        /// <summary>Lines of a free-text section such as `[Infobox Text]` (comments stripped).</summary>
        public List<string> SectionLines(string section) {
            var r = new List<string>();
            bool inside = false;
            foreach (var raw in MugenDef.SplitLines(Text)) {
                var t = MugenDef.StripComment(raw).Trim();
                if (t.StartsWith("[")) {
                    int end = t.IndexOf(']');
                    inside = string.Equals((end > 0 ? t.Substring(1, end - 1) : t.Substring(1)).Trim(), section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (inside) { var l = MugenDef.StripComment(raw).TrimEnd(); if (l.Length > 0) r.Add(l); }
            }
            return r;
        }
    }
}
