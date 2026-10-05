using System;
using System.Collections.Generic;
using UnityEngine;
using IK.Core;

namespace IK.UI {
    /// <summary>
    /// Runtime cache of everything the front-end screens read from the APK: the motif
    /// (`data/system.def` + `system.sff` + `system.snd` + its fonts), the roster
    /// (`data/select.def`), and per-character / per-stage display data (names, portraits,
    /// palettes). Everything comes out of <c>Resources</c> through <see cref="ResourcesSource"/>
    /// (AGENTS.md §7.x: Resources is the only readable store inside the APK).
    ///
    /// Each SFF gets its own <see cref="MugenAssetCache"/> (sprite keys collide across files).
    /// </summary>
    public static class MotifAssets {
        public const string DataGroup = "data";
        public const string MotifDef = "system.def";
        public const string SelectDef = "select.def";

        static Motif motif;
        static Roster roster;
        static MugenAssetCache motifCache;
        static readonly Dictionary<int, HudFont> fonts = new Dictionary<int, HudFont>();
        static readonly Dictionary<string, CharInfo> chars = new Dictionary<string, CharInfo>();
        static readonly Dictionary<string, string> stageNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static AudioSource audio;
        public static string LoadError { get; private set; }

        /// <summary>A roster character with what the select / VS / victory screens draw.</summary>
        public class CharInfo {
            public RosterChar Entry;
            public MugenCharacter Character;
            public readonly MugenAssetCache Cache = new MugenAssetCache();
            public string Error;
            public float LocalW => Character != null ? Character.LocalCoordWidth : 320f;

            /// <summary>Number of selectable palettes (pal1..pal12 present in the SFF), at least 1.</summary>
            public int PaletteCount {
                get {
                    var sff = Character != null ? Character.Sff : null;
                    if (sff == null) return 1;
                    int n = 0;
                    for (int i = 1; i <= 12; i++) if (sff.PaletteTable.ContainsKey(SffFile.Key(1, i))) n = i;
                    if (n == 0) n = Mathf.Clamp(sff.Palettes.Count, 1, 12);
                    return n;
                }
            }

            /// <summary>Index in the SFF palette list of `pal 1,1`, the palette sprite 0,0 and the moves share.</summary>
            public int SharedPaletteIndex {
                get {
                    var sff = Character != null ? Character.Sff : null;
                    if (sff != null && sff.PaletteTable.TryGetValue(SffFile.Key(1, 1), out var i)) return i;
                    return 0;
                }
            }

            /// <summary>Palette `palN` (1-based) for sprites that use the shared palette, or null.</summary>
            public uint[] Palette(int n) {
                var sff = Character != null ? Character.Sff : null;
                if (sff == null || sff.Palettes.Count == 0) return null;
                if (sff.PaletteTable.TryGetValue(SffFile.Key(1, n), out var idx) && idx >= 0 && idx < sff.Palettes.Count)
                    return sff.Palettes[idx];
                return sff.Palettes[Mathf.Clamp(n - 1, 0, sff.Palettes.Count - 1)];
            }

            /// <summary>Unity sprite for (group, number) in palette <paramref name="pal"/>; null when missing.</summary>
            public Sprite Sprite(int group, int number, int pal, out SffSprite raw) {
                raw = Character != null && Character.Sff != null ? Character.Sff.Get(group, number) : null;
                if (raw == null || raw.IsBlank) return null;
                // Ikemen `applypal`: the chosen palette replaces only the shared character palette
                // (pal 1,1); sprites with their own palette (the 9000,1 portraits) keep it
                uint[] p = null;
                if (!raw.Raw && raw.OwnPalette == null && raw.PaletteIndex == SharedPaletteIndex) p = Palette(pal);
                return Cache.SpriteFor(Character.Sff, raw, p);
            }
        }

        public static Motif Motif {
            get {
                if (motif != null) return motif;
                try {
                    motif = Motif.Load(Source, MotifDef);
                    if (motif.Sprites == null) LoadError = "system.sff missing";
                } catch (Exception e) {
                    LoadError = "motif: " + e.Message;
                    motif = Motif.Parse("", MotifDef);
                }
                return motif;
            }
        }

        public static IResourceSource Source => new ResourcesSource(DataGroup);

        public static MugenAssetCache MotifCache => motifCache ?? (motifCache = new MugenAssetCache());

        /// <summary>Unity sprite of the motif SFF, or null.</summary>
        public static Sprite MotifSprite(int group, int number, out SffSprite raw) {
            raw = Motif.Sprites != null ? Motif.Sprites.Get(group, number) : null;
            if (raw == null || raw.IsBlank) return null;
            return MotifCache.SpriteFor(Motif.Sprites, raw);
        }

        /// <summary>Motif font N (`[Files] fontN`), null when missing or not a bitmap font.</summary>
        public static HudFont Font(int index) {
            if (index < 0) return null;
            if (fonts.TryGetValue(index, out var f)) return f;
            f = null;
            if (Motif.Fonts.TryGetValue(index, out var path)) {
                try { f = HudFont.Load(Source, path); } catch (Exception) { f = null; }
                if (f != null && !f.Ready) f = null;
            }
            fonts[index] = f;
            return f;
        }

        /// <summary>The roster from `select.def`, with unsupported entries dropped (ZSS chars, 3D stages).</summary>
        public static Roster Roster {
            get {
                if (roster != null) return roster;
                var bytes = Source.Read(SelectDef);
                roster = bytes != null ? Roster.Parse(bytes) : Roster.Parse("[Characters]\nkfm, stages/kfm.def\n[ExtraStages]\nstages/kfm.def\n");
                roster.Filter(
                    c => {
                        var b = new ResourcesSource(c.CharGroup).Read(c.CharDef);
                        if (b == null) return "not packed";
                        return Roster.CharDefProblem(MugenDef.Parse(b));
                    },
                    s => {
                        var b = new ResourcesSource("stages").Read(s.Def);
                        if (b == null) return "not packed";
                        var def = MugenDef.Parse(b);
                        var why = Roster.StageDefProblem(def);
                        if (why == null) {
                            var info = def["info"];
                            s.DisplayName = info != null ? MugenDef.Unquote(info.Get("displayname", info.Get("name", s.Def))) : s.Def;
                            stageNames[s.Def] = s.DisplayName;
                        }
                        return why;
                    });
                foreach (var c in roster.Characters) {
                    var info = Char(c);
                    if (info.Character != null) {
                        c.DisplayName = info.Character.DisplayName;
                        c.Author = info.Character.Author;
                        c.LocalCoordWidth = Mathf.RoundToInt(info.Character.LocalCoordWidth);
                    }
                }
                foreach (var s in roster.Skipped) Debug.Log("[IK] roster: skipped " + s);
                return roster;
            }
        }

        /// <summary>Character data for a roster cell (loaded once: def + sff + air, no sounds).</summary>
        public static CharInfo Char(RosterChar c) {
            if (c == null || !c.Selectable) return null;
            string key = c.CharGroup + "/" + c.CharDef;
            if (chars.TryGetValue(key, out var info)) return info;
            info = new CharInfo { Entry = c };
            try { info.Character = MugenCharacter.Load(new ResourcesSource(c.CharGroup), c.CharDef, false); }
            catch (Exception e) { info.Error = e.Message; }
            chars[key] = info;
            return info;
        }

        /// <summary>Character data by Resources group + def (the MatchSetup naming).</summary>
        public static CharInfo Char(string group, string def) {
            foreach (var c in Roster.Cells) if (c.CharGroup == group && c.CharDef == def) return Char(c);
            return Char(new RosterChar { Entry = def, CharGroup = group, CharDef = def });
        }

        public static string StageName(string def) {
            if (string.IsNullOrEmpty(def)) return "";
            var _ = Roster;
            return stageNames.TryGetValue(def, out var n) && n.Length > 0 ? n : def;
        }

        // ------------------------------------------------------------------ sounds

        /// <summary>Plays `group, number` from system.snd (cursor move / done / cancel...).</summary>
        public static void PlaySnd(int[] gn) {
            if (gn == null || gn.Length < 2 || gn[0] < 0) return;
            var snd = Motif.Sounds;
            if (snd == null) return;
            var entry = snd.Get(gn[0], gn[1]);
            var clip = entry != null ? MotifCache.ClipFor(entry) : null;
            if (clip == null) return;
            if (audio == null) {
                var go = new GameObject("IKMenuAudio");
                UnityEngine.Object.DontDestroyOnLoad(go);
                audio = go.AddComponent<AudioSource>();
                audio.playOnAwake = false;
            }
            audio.volume = Mathf.Clamp01(Settings.SettingsStore.Current.sfxVolume / 100f);
            audio.PlayOneShot(clip);
            LastSound = gn[0] + "," + gn[1];
            SoundsPlayed++;
        }

        /// <summary>For tests: how many menu sounds were started, and the last one.</summary>
        public static int SoundsPlayed { get; private set; }
        public static string LastSound { get; private set; } = "";

        /// <summary>Drops every cached asset (tests, language reloads keep using the cache).</summary>
        public static void Clear() {
            motif = null; roster = null;
            if (motifCache != null) motifCache.Dispose();
            motifCache = null;
            foreach (var f in fonts.Values) if (f != null) f.Dispose();
            fonts.Clear();
            foreach (var c in chars.Values) c.Cache.Dispose();
            chars.Clear();
            stageNames.Clear();
        }
    }
}
