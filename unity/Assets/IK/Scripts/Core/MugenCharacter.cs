using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>Where a character's files come from (Resources on device, disk in tests).</summary>
    public interface IResourceSource {
        /// <summary>Bytes of a file named in the .def, or null when it does not exist.</summary>
        byte[] Read(string fileName);
    }

    /// <summary>
    /// A MUGEN character: its .def plus the sprite, animation and sound archives it names.
    /// dev.2 stops here — the .cmd / .cns state machine is dev.3.
    /// </summary>
    public class MugenCharacter {
        public MugenDef Def;
        public string Name = "", DisplayName = "", Author = "", VersionDate = "", MugenVersion = "";
        public float LocalCoordWidth = 320, LocalCoordHeight = 240;
        public SffFile Sff;
        public AirFile Air;
        public SndFile Snd;
        public string SpriteFile = "", AnimFile = "", SoundFile = "", CmdFile = "", CnsFile = "";
        public readonly List<string> Warnings = new List<string>();

        /// <summary>
        /// Reads the .def and then every archive it points at. Missing optional archives
        /// (sound above all) are recorded in <see cref="Warnings"/> instead of throwing, so a
        /// half-complete character still shows up in the viewer.
        /// </summary>
        public static MugenCharacter Load(IResourceSource source, string defFile, bool loadSound = true) {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var defBytes = source.Read(defFile);
            if (defBytes == null) throw new System.IO.FileNotFoundException("character def not found: " + defFile);

            var c = new MugenCharacter { Def = MugenDef.Parse(defBytes) };
            var info = c.Def["info"];
            if (info != null) {
                c.Name = MugenDef.Unquote(info.Get("name"));
                c.DisplayName = MugenDef.Unquote(info.Get("displayname", c.Name));
                c.Author = MugenDef.Unquote(info.Get("author"));
                c.VersionDate = MugenDef.Unquote(info.Get("versiondate"));
                c.MugenVersion = MugenDef.Unquote(info.Get("mugenversion"));
                var lc = MugenDef.SplitCsv(info.Get("localcoord", "320,240"));
                if (lc.Length >= 1 && MugenDef.IsNumeric(lc[0])) c.LocalCoordWidth = MugenDef.Atof(lc[0]);
                if (lc.Length >= 2 && MugenDef.IsNumeric(lc[1])) c.LocalCoordHeight = MugenDef.Atof(lc[1]);
            }

            var files = c.Def["files"];
            if (files == null) throw new System.IO.InvalidDataException("character def has no [Files] section");
            c.SpriteFile = MugenDef.Unquote(files.Get("sprite"));
            c.AnimFile = MugenDef.Unquote(files.Get("anim"));
            c.SoundFile = MugenDef.Unquote(files.Get("sound"));
            c.CmdFile = MugenDef.Unquote(files.Get("cmd"));
            c.CnsFile = MugenDef.Unquote(files.Get("cns"));

            var sffBytes = source.Read(c.SpriteFile);
            if (sffBytes == null) throw new System.IO.FileNotFoundException("sprite file not found: " + c.SpriteFile);
            c.Sff = SffFile.Load(sffBytes, isCharacter: true);

            var airBytes = source.Read(c.AnimFile);
            if (airBytes == null) c.Warnings.Add("anim file not found: " + c.AnimFile);
            else c.Air = AirFile.Parse(airBytes);

            if (loadSound && !string.IsNullOrEmpty(c.SoundFile)) {
                var sndBytes = source.Read(c.SoundFile);
                if (sndBytes == null) c.Warnings.Add("sound file not found: " + c.SoundFile);
                else {
                    try { c.Snd = SndFile.Load(sndBytes); } catch (Exception e) { c.Warnings.Add("sound: " + e.Message); }
                }
            }
            return c;
        }

        /// <summary>Action numbers in file order, so the viewer can walk them predictably.</summary>
        public IReadOnlyList<int> ActionNumbers => Air != null ? (IReadOnlyList<int>)Air.Order : Array.Empty<int>();

        /// <summary>The sprite an animation element points at, or null when it is missing.</summary>
        public SffSprite SpriteOf(AnimFrame frame) {
            if (frame == null || Sff == null || frame.Group < 0) return null;
            return Sff.Get(frame.Group, frame.Number);
        }

        /// <summary>
        /// Animations that reference at least one sprite that exists, in file order. Broken
        /// actions (every element missing) are skipped so the viewer never shows a blank.
        /// </summary>
        public List<int> PlayableActions() {
            var list = new List<int>();
            if (Air == null) return list;
            foreach (var no in Air.Order) {
                var a = Air.Get(no);
                if (a == null || a.Frames.Count == 0) continue;
                foreach (var f in a.Frames) {
                    if (SpriteOf(f) != null) { list.Add(no); break; }
                }
            }
            return list;
        }
    }
}
