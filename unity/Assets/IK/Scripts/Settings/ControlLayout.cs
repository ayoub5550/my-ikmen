using System;
using System.Collections.Generic;
using UnityEngine;

namespace IK.Settings {
    /// <summary>Which on-screen control a layout entry describes.</summary>
    public enum ControlId { Direction = 0, LP, MP, HP, LK, MK, HK, Start, Pause, D, W, MacroXY, MacroAB }

    /// <summary>
    /// One control: a centre position in reference units (1280x720) relative to an anchor
    /// corner, plus size and opacity. Storing an anchor + offset (instead of absolute
    /// pixels) is what makes a layout survive a different resolution, aspect or safe area.
    /// </summary>
    [Serializable]
    public class ControlPlacement {
        public ControlId id;
        public Vector2 anchor = new Vector2(1f, 0f);  // 0,0 = bottom-left … 1,1 = top-right
        public Vector2 position;                      // centre, reference units, from the anchor
        public float size = 130f;
        public float opacity = 1f;                    // multiplied by the global controls opacity
        public bool visible = true;

        public ControlPlacement Clone() => (ControlPlacement)MemberwiseClone();
    }

    [Serializable]
    public class ControlLayout {
        public const float RefWidth = 1280f;
        public const float RefHeight = 720f;
        public const float MinTarget = 100f;          // §2.5: minimum hit target

        public string name = "Default";
        public List<ControlPlacement> controls = new List<ControlPlacement>();

        public ControlPlacement Get(ControlId id) {
            for (int i = 0; i < controls.Count; i++) if (controls[i].id == id) return controls[i];
            return null;
        }

        public ControlLayout Clone() {
            var copy = new ControlLayout { name = name };
            foreach (var c in controls) copy.controls.Add(c.Clone());
            return copy;
        }

        /// <summary>Default arcade layout from docs/TOUCH_AND_SETTINGS.md §3.1/§3.2.</summary>
        public static ControlLayout Default() {
            var l = new ControlLayout { name = "Default" };
            l.controls.Add(new ControlPlacement { id = ControlId.Direction, anchor = new Vector2(0, 0), position = new Vector2(170, 170), size = 260 });
            l.controls.Add(new ControlPlacement { id = ControlId.LP, anchor = new Vector2(1, 0), position = new Vector2(-370, 250), size = 130 });
            l.controls.Add(new ControlPlacement { id = ControlId.MP, anchor = new Vector2(1, 0), position = new Vector2(-225, 280), size = 130 });
            l.controls.Add(new ControlPlacement { id = ControlId.HP, anchor = new Vector2(1, 0), position = new Vector2(-80, 310), size = 130 });
            l.controls.Add(new ControlPlacement { id = ControlId.LK, anchor = new Vector2(1, 0), position = new Vector2(-370, 105), size = 130 });
            l.controls.Add(new ControlPlacement { id = ControlId.MK, anchor = new Vector2(1, 0), position = new Vector2(-225, 135), size = 130 });
            l.controls.Add(new ControlPlacement { id = ControlId.HK, anchor = new Vector2(1, 0), position = new Vector2(-80, 165), size = 130 });
            // dev.7: START / pause below the motif lifebars (top centre covered the round timer)
            l.controls.Add(new ControlPlacement { id = ControlId.Start, anchor = new Vector2(1, 1), position = new Vector2(-250, -205), size = 100 });
            l.controls.Add(new ControlPlacement { id = ControlId.Pause, anchor = new Vector2(1, 1), position = new Vector2(-80, -205), size = 100 });
            l.controls.Add(new ControlPlacement { id = ControlId.D, anchor = new Vector2(1, 0), position = new Vector2(-510, 150), size = 110, visible = false });
            l.controls.Add(new ControlPlacement { id = ControlId.W, anchor = new Vector2(1, 0), position = new Vector2(-510, 285), size = 110, visible = false });
            l.controls.Add(new ControlPlacement { id = ControlId.MacroXY, anchor = new Vector2(1, 0), position = new Vector2(-150, 440), size = 110, visible = false });
            l.controls.Add(new ControlPlacement { id = ControlId.MacroAB, anchor = new Vector2(1, 0), position = new Vector2(-300, 420), size = 110, visible = false });
            return l;
        }

        /// <summary>Mirrored preset for left-handed players: attack buttons left, direction right.</summary>
        public static ControlLayout LeftHanded() {
            var l = Default().Clone();
            l.name = "LeftHanded";
            foreach (var c in l.controls) {
                if (c.id == ControlId.Start) continue;
                c.anchor = new Vector2(1f - c.anchor.x, c.anchor.y);
                c.position = new Vector2(-c.position.x, c.position.y);
            }
            return l;
        }

        /// <summary>Compact preset (tablets / 4:3): everything pulled towards the corners and smaller.</summary>
        public static ControlLayout Compact() {
            var l = Default().Clone();
            l.name = "Compact";
            foreach (var c in l.controls) {
                // START and PAUSE already sit on the screen edge; shrinking them further
                // would push them out of the safe area once the minimum target is applied.
                if (c.id == ControlId.Start || c.id == ControlId.Pause) continue;
                c.size = Mathf.Max(MinTarget, c.size * 0.85f);
                c.position = c.position * 0.85f;   // keep the spacing proportional to the smaller buttons
            }
            var dir = l.Get(ControlId.Direction);
            dir.size = 230f;
            return l;
        }

        public static ControlLayout Preset(string name) {
            switch (name) {
                case "LeftHanded": return LeftHanded();
                case "Compact": return Compact();
                default: return Default();
            }
        }

        public static readonly string[] PresetNames = { "Default", "LeftHanded", "Compact" };

        /// <summary>
        /// Axis-aligned rect of a control in reference units for a given screen rect
        /// (already reduced to the safe area), with the global size scale applied.
        /// </summary>
        public Rect RectOf(ControlPlacement c, Rect safeRef, float sizeScale) {
            var raw = RectOfRaw(c, safeRef, sizeScale);
            float size = raw.width;
            float cx = raw.center.x, cy = raw.center.y;
            // A control is never allowed to leave the safe area: a bigger button size or a
            // smaller safe area pulls it back in instead of rendering it half off-screen.
            if (size <= safeRef.width) cx = Mathf.Clamp(cx, safeRef.xMin + size * 0.5f, safeRef.xMax - size * 0.5f);
            if (size <= safeRef.height) cy = Mathf.Clamp(cy, safeRef.yMin + size * 0.5f, safeRef.yMax - size * 0.5f);
            return new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size);
        }

        /// <summary>
        /// The rect the author asked for, before the safe-area clamp of <see cref="RectOf"/>.
        /// <see cref="Validate"/> judges this one: the clamp is a runtime safety net, it must
        /// not silently turn an illegal layout into a legal-looking one.
        /// </summary>
        public Rect RectOfRaw(ControlPlacement c, Rect safeRef, float sizeScale) {
            float size = Mathf.Max(MinTarget, c.size * sizeScale);
            float cx = Mathf.Lerp(safeRef.xMin, safeRef.xMax, c.anchor.x) + c.position.x;
            float cy = Mathf.Lerp(safeRef.yMin, safeRef.yMax, c.anchor.y) + c.position.y;
            return new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size);
        }

        /// <summary>Snaps a position to the editor grid (8 reference units).</summary>
        public static Vector2 Snap(Vector2 position, float grid = 8f) =>
            new Vector2(Mathf.Round(position.x / grid) * grid, Mathf.Round(position.y / grid) * grid);

        /// <summary>
        /// Validation used by the layout editor and by test case §6.7: every visible control
        /// must stay inside the safe area, be at least <see cref="MinTarget"/> units and not
        /// overlap another control.
        /// </summary>
        public bool Validate(Rect safeRef, float sizeScale, out string error) {
            var rects = new List<KeyValuePair<ControlId, Rect>>();
            foreach (var c in controls) {
                if (!c.visible) continue;
                var r = RectOf(c, safeRef, sizeScale);
                var raw = RectOfRaw(c, safeRef, sizeScale);
                if (r.width < MinTarget - 0.01f || r.height < MinTarget - 0.01f) {
                    error = c.id + ": target smaller than " + MinTarget + " units";
                    return false;
                }
                if (raw.xMin < safeRef.xMin - 0.01f || raw.yMin < safeRef.yMin - 0.01f ||
                    raw.xMax > safeRef.xMax + 0.01f || raw.yMax > safeRef.yMax + 0.01f) {
                    error = c.id + ": outside the safe area";
                    return false;
                }
                foreach (var other in rects) {
                    if (other.Value.Overlaps(r)) {
                        error = c.id + " overlaps " + other.Key;
                        return false;
                    }
                }
                rects.Add(new KeyValuePair<ControlId, Rect>(c.id, r));
            }
            error = null;
            return true;
        }

        /// <summary>HUD no-go zone: the top 22 % of the safe area (lifebars, timer, combo text).</summary>
        public static Rect HudNoGo(Rect safeRef) {
            float h = safeRef.height * 0.22f;
            return new Rect(safeRef.xMin, safeRef.yMax - h, safeRef.width, h);
        }

        /// <summary>True when a control other than Start/Pause sits in the HUD zone.</summary>
        public bool HitsHud(Rect safeRef, float sizeScale, out ControlId offender) {
            var nogo = HudNoGo(safeRef);
            foreach (var c in controls) {
                if (!c.visible || c.id == ControlId.Start || c.id == ControlId.Pause) continue;
                if (RectOf(c, safeRef, sizeScale).Overlaps(nogo)) { offender = c.id; return true; }
            }
            offender = ControlId.Direction;
            return false;
        }
    }
}
