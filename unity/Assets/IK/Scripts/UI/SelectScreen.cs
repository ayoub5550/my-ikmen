using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IK.App;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// Character and stage select (`[Select Info]` + `[SelectBGdef]` of system.def, roster from
    /// select.def): the cell grid with each character's small portrait (`portrait.spr`, 9000,0)
    /// and the random cell, the animated P1/P2 cursors, the big portraits (`pN.face.anim`, the
    /// character's own action 0 in ikemen1, scaled by motif/char localcoord like Go), the names,
    /// the palette menu (`paletteselect`), then — depending on the mode — the opponent, the
    /// stage and the CPU difficulty. Everything is tappable: tap a cell to move there, tap it
    /// again (or the portrait, or OK) to confirm; the arrows change palette / stage / level.
    ///
    /// Approximations (documented in docs/DEV5.md): the ikemen1 grid is a perspective
    /// trapezoid (`cell.*-N.*` overrides with projection); here it is a flat grid, and for a
    /// small roster the cells are enlarged (up to 3x) so they are real touch targets. `face2`
    /// (the perspective side portrait) is not drawn.
    /// </summary>
    public class SelectScreen : FrontEndScreen {
        public enum Phase { P1Char, P1Pal, P2Char, P2Pal, Stage, Level, Done }

        public GameMode Mode { get; private set; }
        public Roster Roster { get; private set; }
        public Phase Current { get; private set; }
        public readonly List<Phase> Phases = new List<Phase>();
        public int P1Cell { get; private set; }
        public int P2Cell { get; private set; }
        public int P1Pal { get; private set; } = 1;
        public int P2Pal { get; private set; } = 1;
        /// <summary>-1 = random stage, else index in Roster.Stages.</summary>
        public int StageIndex { get; private set; } = -1;
        public int Level { get; private set; } = 5;
        public int Columns { get; private set; }
        public int Rows { get; private set; }
        public float CellScale { get; private set; } = 1f;

        /// <summary>p1 cell, p1 palette, p2 cell, p2 palette, stage def (null = auto), CPU level.</summary>
        public Action<RosterChar, int, RosterChar, int, string, int> onDone;
        public Action onBack;

        readonly List<MotifView.SpriteNode> cellBgs = new List<MotifView.SpriteNode>();
        readonly List<MotifView.SpriteNode> cellFaces = new List<MotifView.SpriteNode>();
        readonly List<Button> cellHits = new List<Button>();
        RectTransform gridRoot;
        MotifView.AnimNode cursor1, cursor2;
        MotifView.SpriteNode done1, done2;
        RectTransform[] faceWin = new RectTransform[2];
        readonly MotifView.AnimNode[] face = new MotifView.AnimNode[2];
        readonly MotifView.SpriteNode[] faceRandom = new MotifView.SpriteNode[2];
        readonly MotifView.TextNode[] names = new MotifView.TextNode[2];
        readonly MotifView.TextNode[] palText = new MotifView.TextNode[2];
        readonly MotifView.TextNode[] palNumber = new MotifView.TextNode[2];
        MotifView.TextNode title, hint, valueLine;
        Button okButton, backButton, leftButton, rightButton;
        readonly Button[] faceHits = new Button[2];
        int randomTick;

        MotifSelect Sel => View.Motif.Select;

        public override void Build(RectTransform parent) {
            CreateView(parent, "Select", "SelectBG");
            FadeInTicks = Sel.FadeInTime;
            for (int i = 0; i < 2; i++) {
                var pl = i == 0 ? Sel.P1 : Sel.P2;
                faceWin[i] = View.Window(View.Top, "faceWindow" + (i + 1), pl.Face.Layout);
                var origin = pl.Face.Layout.HasWindow ? new Vector2(pl.Face.Layout.Window[0], pl.Face.Layout.Window[1]) : Vector2.zero;
                face[i] = View.NewAnim("face" + (i + 1), faceWin[i], origin);
                faceRandom[i] = new MotifView.SpriteNode(faceWin[i], "faceRandom" + (i + 1), origin);
                names[i] = View.NewText("name" + (i + 1));
                palText[i] = View.NewText("palText" + (i + 1));
                palNumber[i] = View.NewText("palNumber" + (i + 1));
                int side = i;
                var c = pl.Face.Layout;
                faceHits[i] = View.Hit("faceHit" + (i + 1), c.OffsetX, c.OffsetY - 200f, 360f, 420f, () => Confirm());
            }
            gridRoot = UIKit.Panel(View.Top, "grid", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            cursor1 = View.NewAnim("cursor1");
            cursor2 = View.NewAnim("cursor2");
            done1 = View.NewSprite("done1");
            done2 = View.NewSprite("done2");
            title = View.NewText("title");
            hint = View.NewText("hint");
            valueLine = View.NewText("value");

            float w = View.Width, h = View.Height;
            backButton = UIKit.Button(View.Top, "Back", new Vector2(0f, 1f), MotifView.Ui(100f, 48f), new Vector2(170f, 64f), Loc.T("common.back"), () => Cancel(), 26);
            okButton = UIKit.Button(View.Top, "OK", new Vector2(0f, 1f), MotifView.Ui(w - 110f, h - 62f), new Vector2(190f, 80f), Loc.T("fe.ok"), () => Confirm(), 30);
            leftButton = UIKit.Button(View.Top, "Left", new Vector2(0f, 1f), MotifView.Ui(w / 2f - 330f, h - 62f), new Vector2(100f, 80f), "<", () => Change(-1), 36);
            rightButton = UIKit.Button(View.Top, "Right", new Vector2(0f, 1f), MotifView.Ui(w / 2f + 330f, h - 62f), new Vector2(100f, 80f), ">", () => Change(1), 36);
            FinishBuild();
        }

        /// <summary>Starts a selection for <paramref name="mode"/> (called before showing the screen).</summary>
        public void Begin(GameMode mode, Roster roster, int level) {
            Mode = mode;
            Roster = roster;
            Level = Mathf.Clamp(level, 1, 8);
            Phases.Clear();
            Phases.Add(Phase.P1Char);
            if (Sel.PaletteSelect > 0) Phases.Add(Phase.P1Pal);
            if (GameFlow.PicksOpponent(mode)) {
                Phases.Add(Phase.P2Char);
                if (Sel.PaletteSelect > 0) Phases.Add(Phase.P2Pal);
            }
            if (GameFlow.PicksStage(mode)) Phases.Add(Phase.Stage);
            if (GameFlow.PicksDifficulty(mode)) Phases.Add(Phase.Level);
            Current = Phases[0];
            P1Cell = CellIndex(Sel.P1.StartCell[1], Sel.P1.StartCell[0]);
            P2Cell = CellIndex(Sel.P2.StartCell[1], Sel.P2.StartCell[0]);
            P1Pal = P2Pal = 1;
            StageIndex = -1;
            BuildGrid();
            Refresh();
        }

        int CellIndex(int col, int row) {
            int n = Roster != null ? Roster.Cells.Count : 0;
            if (n == 0) return 0;
            int cols = Mathf.Max(1, Columns > 0 ? Columns : Mathf.Min(Sel.Columns, n));
            return Mathf.Clamp(row * cols + col, 0, n - 1);
        }

        /// <summary>Lays the cells out (flat grid, motif rows/columns, enlarged for small rosters).</summary>
        void BuildGrid() {
            foreach (Transform t in gridRoot) Destroy(t.gameObject);
            cellBgs.Clear(); cellFaces.Clear(); cellHits.Clear();
            int n = Roster.Cells.Count;
            Columns = Mathf.Clamp(Mathf.Min(Sel.Columns, n), 1, Mathf.Max(1, Sel.Columns));
            Rows = Mathf.Clamp(Mathf.CeilToInt(n / (float)Columns), 1, Mathf.Max(1, Sel.Rows));
            float pitch = Mathf.Max(1f, Sel.CellSize[0] + Sel.CellSpacing[0]);
            // the centre gap between the two portrait windows is ~320 units wide in ikemen1
            CellScale = Mathf.Clamp(300f / (Columns * pitch), 1f, 3f);
            for (int i = 0; i < Columns * Rows; i++) {
                int idx = i;
                var r = CellRect(i);
                cellBgs.Add(new MotifView.SpriteNode(gridRoot, "cellBg" + i, Vector2.zero));
                cellFaces.Add(new MotifView.SpriteNode(gridRoot, "cellFace" + i, Vector2.zero));
                cellHits.Add(View.Hit("cell" + i, r.center.x, r.center.y, r.width + 4f, r.height + 4f, () => TapCell(idx), gridRoot));
            }
            // cursors above the cells
            cursor1.Node.Rt.SetAsLastSibling(); cursor2.Node.Rt.SetAsLastSibling();
            done1.Rt.SetAsLastSibling(); done2.Rt.SetAsLastSibling();
            cursor1.Play(View.Motif.Animations, Sel.P1.CursorActive.AnimNo);
            cursor2.Play(View.Motif.Animations, Sel.P2.CursorActive.AnimNo);
        }

        /// <summary>Motif-space rect (left, top, w, h) of cell <paramref name="i"/>.</summary>
        public Rect CellRect(int i) {
            int col = i % Mathf.Max(1, Columns), row = i / Mathf.Max(1, Columns);
            float pitchX = (Sel.CellSize[0] + Sel.CellSpacing[0]) * CellScale;
            float pitchY = (Sel.CellSize[1] + Sel.CellSpacing[1]) * CellScale;
            float gridW = Columns * pitchX - Sel.CellSpacing[0] * CellScale;
            float x0 = View.Width / 2f - gridW / 2f;
            float y0 = Sel.Pos[1];
            return new Rect(x0 + col * pitchX, y0 + row * pitchY, Sel.CellSize[0] * CellScale, Sel.CellSize[1] * CellScale);
        }

        RosterChar CellChar(int i) => Roster != null && i >= 0 && i < Roster.Cells.Count ? Roster.Cells[i] : null;
        public RosterChar P1Char => CellChar(P1Cell);
        public RosterChar P2Char => CellChar(P2Cell);

        // ------------------------------------------------------------------ drawing

        void Refresh() {
            if (Roster == null) return;
            var m = View.Motif;
            title.Set(Sel.Title, Sel.Title.Layout.OffsetX == 0 ? View.Width / 2f : 0f, 0f,
                      Loc.Arabic ? Loc.T("fe." + GameFlow.TitleKey(Mode)) : Sel.TitleText(GameFlow.TitleKey(Mode), Mode.ToString().ToUpperInvariant()),
                      null, Loc.Arabic);
            hint.Set(FightText.Read(null, "", "", 2, 0), View.Width / 2f, 150f, HintText(), new Color(1f, 0.95f, 0.85f), true);

            // cells
            for (int i = 0; i < cellBgs.Count; i++) {
                var r = CellRect(i);
                var c = CellChar(i);
                var bg = MotifAssets.MotifSprite(Sel.CellBg.SprGroup, Sel.CellBg.SprNumber, out var bgRaw);
                bool show = c != null || Sel.ShowEmptyBoxes;
                if (show && bg != null) cellBgs[i].Set(bg, bgRaw, r.x, r.y, Sel.CellBg.Layout.ScaleX * CellScale, Sel.CellBg.Layout.ScaleY * CellScale);
                else cellBgs[i].Hide();
                if (c == null) { cellFaces[i].Hide(); continue; }
                if (c.Random) {
                    var rs = MotifAssets.MotifSprite(Sel.CellRandom.SprGroup, Sel.CellRandom.SprNumber, out var rraw);
                    cellFaces[i].Set(rs, rraw, r.x, r.y, Sel.CellRandom.Layout.ScaleX * CellScale, Sel.CellRandom.Layout.ScaleY * CellScale);
                    continue;
                }
                var info = MotifAssets.Char(c);
                if (info == null || info.Character == null) { cellFaces[i].Hide(); continue; }
                float k = PortraitScale(info);
                var spr = info.Sprite(Sel.Portrait.SprGroup >= 0 ? Sel.Portrait.SprGroup : 9000, Sel.Portrait.SprNumber, 1, out var raw);
                cellFaces[i].Set(spr, raw, r.x + Sel.Portrait.Layout.OffsetX, r.y + Sel.Portrait.Layout.OffsetY,
                                 Sel.Portrait.Layout.ScaleX * k * CellScale, Sel.Portrait.Layout.ScaleY * k * CellScale);
            }

            bool p2Phase = Current == Phase.P2Char || Current == Phase.P2Pal || IndexOf(Current) > IndexOf(Phase.P2Pal) && Phases.Contains(Phase.P2Char);
            DrawFace(0, P1Char, P1Pal, true);
            DrawFace(1, P2Char, P2Pal, Phases.Contains(Phase.P2Char) && IndexOf(Current) >= IndexOf(Phase.P2Char));
            DrawPalMenu(0, Current == Phase.P1Pal);
            DrawPalMenu(1, Current == Phase.P2Pal);
            DrawCursors();

            bool valueStep = Current == Phase.P1Pal || Current == Phase.P2Pal || Current == Phase.Stage || Current == Phase.Level;
            leftButton.gameObject.SetActive(valueStep);
            rightButton.gameObject.SetActive(valueStep);
            var stageFont = Current == Phase.Stage ? Sel.StageActiveFont : Sel.StageFont;
            string value = ValueText();
            if (value.Length > 0) valueLine.Set(stageFont, Sel.StagePos[0], Sel.StagePos[1], value, null, Loc.Arabic);
            else valueLine.Hide();
            UIKit.SetText(backButton.GetComponentInChildren<Text>(), Loc.T("common.back"));
            UIKit.SetText(okButton.GetComponentInChildren<Text>(), Loc.T("fe.ok"));
        }

        int IndexOf(Phase p) { int i = Phases.IndexOf(p); return i < 0 ? int.MaxValue : i; }

        string HintText() {
            bool watch = Mode == GameMode.Watch;
            switch (Current) {
                case Phase.P1Char: return Loc.T(watch ? "fe.pickP1Watch" : "fe.pickP1");
                case Phase.P2Char: return Loc.T(watch ? "fe.pickP2Watch" : "fe.pickP2");
                case Phase.P1Pal: case Phase.P2Pal: return Loc.T("fe.pickPal");
                case Phase.Stage: return Loc.T("fe.pickStage");
                case Phase.Level: return Loc.T("fe.pickLevel");
            }
            return "";
        }

        public string ValueText() {
            switch (Current) {
                case Phase.P1Pal: return string.Format(Loc.T("fe.color"), P1Pal);
                case Phase.P2Pal: return string.Format(Loc.T("fe.color"), P2Pal);
                case Phase.Stage:
                    return StageIndex < 0 ? Loc.T("fe.stageAuto")
                        : string.Format(Loc.T("fe.stage"), MotifAssets.StageName(Roster.Stages[StageIndex].Def));
                case Phase.Level: return string.Format(Loc.T("fe.cpuLevel"), Level);
            }
            if (IndexOf(Current) > IndexOf(Phase.Stage) && Phases.Contains(Phase.Stage))
                return StageIndex < 0 ? Loc.T("fe.stageAuto") : string.Format(Loc.T("fe.stage"), MotifAssets.StageName(Roster.Stages[StageIndex].Def));
            return "";
        }

        /// <summary>Go: portrait scale × motif localcoord width / character localcoord width.</summary>
        public float PortraitScale(MotifAssets.CharInfo info) => View.Width / Mathf.Max(1f, info != null ? info.LocalW : 320f);

        void DrawFace(int side, RosterChar c, int pal, bool on) {
            var pl = side == 0 ? Sel.P1 : Sel.P2;
            var lay = pl.Face.Layout;
            if (!on || c == null) { face[side].Hide(); faceRandom[side].Hide(); names[side].Hide(); faceHits[side].gameObject.SetActive(false); return; }
            faceHits[side].gameObject.SetActive(side == 0 ? Current == Phase.P1Char || Current == Phase.P1Pal : Current == Phase.P2Char || Current == Phase.P2Pal);
            if (c.Random) {
                face[side].Hide();
                var rl = pl.FaceRandom.Layout;
                var s = MotifAssets.MotifSprite(151, 2, out var raw);
                faceRandom[side].Set(s, raw, rl.OffsetX, rl.OffsetY, rl.ScaleX, rl.ScaleY, rl.Facing);
                names[side].Set(pl.Name, 0f, 0f, Loc.T("fe.random"), null, Loc.Arabic);
                return;
            }
            faceRandom[side].Hide();
            var info = MotifAssets.Char(c);
            if (info == null || info.Character == null) { face[side].Hide(); names[side].Hide(); return; }
            face[side].Play(info.Character.Air, pl.Face.AnimNo >= 0 ? pl.Face.AnimNo : 0, info);
            float k = PortraitScale(info);
            int p = pal;
            face[side].Draw((g, n) => { var spr = info.Sprite(g, n, p, out var raw); return (spr, raw); },
                            lay.OffsetX, lay.OffsetY, lay.ScaleX * k, lay.ScaleY * k, lay.Facing);
            names[side].Set(pl.Name, 0f, 0f, c.DisplayName, null, false);
        }

        void DrawPalMenu(int side, bool on) {
            var pl = side == 0 ? Sel.P1 : Sel.P2;
            if (!on) { palText[side].Hide(); palNumber[side].Hide(); return; }
            palText[side].Set(pl.PalText, pl.PalMenuPos[0], pl.PalMenuPos[1], Loc.Arabic ? Loc.T("fe.pickPal") : pl.PalText.Text, null, Loc.Arabic);
            palNumber[side].Set(pl.PalNumber, pl.PalMenuPos[0], pl.PalMenuPos[1], (side == 0 ? P1Pal : P2Pal).ToString());
        }

        void DrawCursors() {
            bool p1Active = Current == Phase.P1Char;
            bool p2Active = Current == Phase.P2Char;
            var r1 = CellRect(P1Cell);
            var r2 = CellRect(P2Cell);
            if (p1Active) {
                cursor1.Draw(MotifView.MotifLookup, r1.x, r1.y, Sel.P1.CursorActive.Layout.ScaleX * CellScale, Sel.P1.CursorActive.Layout.ScaleY * CellScale);
                done1.Hide();
            } else {
                cursor1.Hide();
                var s = MotifAssets.MotifSprite(Sel.P1.CursorDone.SprGroup, Sel.P1.CursorDone.SprNumber, out var raw);
                done1.Set(s, raw, r1.x, r1.y, Sel.P1.CursorDone.Layout.ScaleX * CellScale, Sel.P1.CursorDone.Layout.ScaleY * CellScale);
            }
            bool p2Shown = Phases.Contains(Phase.P2Char) && IndexOf(Current) >= IndexOf(Phase.P2Char);
            if (p2Active) {
                cursor2.Draw(MotifView.MotifLookup, r2.x, r2.y, Sel.P2.CursorActive.Layout.ScaleX * CellScale, Sel.P2.CursorActive.Layout.ScaleY * CellScale);
                done2.Hide();
            } else if (p2Shown) {
                cursor2.Hide();
                var s = MotifAssets.MotifSprite(Sel.P2.CursorDone.SprGroup, Sel.P2.CursorDone.SprNumber, out var raw);
                done2.Set(s, raw, r2.x, r2.y, Sel.P2.CursorDone.Layout.ScaleX * CellScale, Sel.P2.CursorDone.Layout.ScaleY * CellScale);
            } else { cursor2.Hide(); done2.Hide(); }
        }

        protected override void OnShow() { Refresh(); }

        protected override void OnTick() {
            cursor1.Tick(); cursor2.Tick();
            face[0].Tick(); face[1].Tick();
            randomTick++;
            Refresh();
        }

        // ------------------------------------------------------------------ input

        void TapCell(int i) {
            if (Roster == null || i >= Roster.Cells.Count) return;
            if (Current == Phase.P1Char) {
                if (P1Cell == i) { Confirm(); return; }
                P1Cell = i; MotifAssets.PlaySnd(Sel.P1.CursorMoveSnd);
            } else if (Current == Phase.P2Char) {
                if (P2Cell == i) { Confirm(); return; }
                P2Cell = i; MotifAssets.PlaySnd(Sel.P2.CursorMoveSnd);
            }
            Refresh();
        }

        public void MoveCursor(int dx, int dy) {
            if (Roster == null) return;
            bool p1 = Current == Phase.P1Char;
            int cell = p1 ? P1Cell : P2Cell;
            int n = Roster.Cells.Count;
            int col = cell % Columns, row = cell / Columns;
            col += dx; row += dy;
            if (Sel.Wrapping) {
                col = (col + Columns) % Columns;
                row = (row + Rows) % Rows;
            } else {
                col = Mathf.Clamp(col, 0, Columns - 1);
                row = Mathf.Clamp(row, 0, Rows - 1);
            }
            int next = row * Columns + col;
            if (next >= n) next = dx != 0 ? (dx > 0 ? row * Columns : n - 1) : (dy > 0 ? col : n - 1);
            next = Mathf.Clamp(next, 0, n - 1);
            if (p1) P1Cell = next; else P2Cell = next;
            MotifAssets.PlaySnd((p1 ? Sel.P1 : Sel.P2).CursorMoveSnd);
            Refresh();
        }

        /// <summary>Left/right on a value step (palette, stage, level).</summary>
        public void Change(int d) {
            switch (Current) {
                case Phase.P1Pal: P1Pal = Wrap(P1Pal + d, 1, PalCount(P1Char)); MotifAssets.PlaySnd(Sel.P1.PalValueSnd); break;
                case Phase.P2Pal: P2Pal = Wrap(P2Pal + d, 1, PalCount(P2Char)); MotifAssets.PlaySnd(Sel.P2.PalValueSnd); break;
                case Phase.Stage: StageIndex = Wrap(StageIndex + d, -1, Roster.Stages.Count - 1); MotifAssets.PlaySnd(Sel.StageMoveSnd); break;
                case Phase.Level: Level = Wrap(Level + d, 1, 8); MotifAssets.PlaySnd(Sel.StageMoveSnd); break;
                default: return;
            }
            Refresh();
        }

        static int Wrap(int v, int lo, int hi) {
            if (hi < lo) return lo;
            int span = hi - lo + 1;
            return lo + ((v - lo) % span + span) % span;
        }

        int PalCount(RosterChar c) {
            if (c == null || c.Random) return 1;
            var info = MotifAssets.Char(c);
            return info != null ? info.PaletteCount : 1;
        }

        public void Confirm() {
            switch (Current) {
                case Phase.P1Char: MotifAssets.PlaySnd(Sel.P1.CursorDoneSnd); break;
                case Phase.P2Char: MotifAssets.PlaySnd(Sel.P2.CursorDoneSnd); break;
                case Phase.P1Pal: MotifAssets.PlaySnd(Sel.P1.PalDoneSnd); break;
                case Phase.P2Pal: MotifAssets.PlaySnd(Sel.P2.PalDoneSnd); break;
                default: MotifAssets.PlaySnd(Sel.StageDoneSnd); break;
            }
            int i = Phases.IndexOf(Current);
            // a random cell has no palette to choose
            while (true) {
                i++;
                if (i >= Phases.Count) { Current = Phase.Done; Finish(); return; }
                var next = Phases[i];
                if (next == Phase.P1Pal && (P1Char == null || P1Char.Random)) continue;
                if (next == Phase.P2Pal && (P2Char == null || P2Char.Random)) continue;
                Current = next;
                break;
            }
            Refresh();
        }

        public void Cancel() {
            MotifAssets.PlaySnd(Sel.CancelSnd);
            int i = Phases.IndexOf(Current);
            if (Current == Phase.Done) i = Phases.Count;
            while (true) {
                i--;
                if (i < 0) { onBack?.Invoke(); return; }
                var prev = Phases[i];
                if (prev == Phase.P1Pal && (P1Char == null || P1Char.Random)) continue;
                if (prev == Phase.P2Pal && (P2Char == null || P2Char.Random)) continue;
                Current = prev;
                break;
            }
            Refresh();
        }

        void Finish() {
            var p2 = Phases.Contains(Phase.P2Char) ? P2Char : null;
            string stage = StageIndex >= 0 && StageIndex < Roster.Stages.Count ? Roster.Stages[StageIndex].Def : null;
            onDone?.Invoke(P1Char, P1Pal, p2, P2Pal, stage, Level);
        }

        public override void OnMenuKey(MenuKey key) {
            bool grid = Current == Phase.P1Char || Current == Phase.P2Char;
            switch (key) {
                case MenuKey.Up: if (grid) MoveCursor(0, -1); else Change(1); break;
                case MenuKey.Down: if (grid) MoveCursor(0, 1); else Change(-1); break;
                case MenuKey.Left: if (grid) MoveCursor(-1, 0); else Change(-1); break;
                case MenuKey.Right: if (grid) MoveCursor(1, 0); else Change(1); break;
                case MenuKey.Confirm: Confirm(); break;
                case MenuKey.Cancel: Cancel(); break;
            }
        }

        /// <summary>Motif-space rect of a big portrait window, for the rendered check.</summary>
        public Rect FaceRect(int side) {
            var l = (side == 0 ? Sel.P1 : Sel.P2).Face.Layout;
            if (l.HasWindow) return new Rect(l.Window[0], l.Window[1], l.Window[2], l.Window[3]);
            return new Rect(l.OffsetX - 200f, l.OffsetY - 400f, 400f, 400f);
        }
    }
}
