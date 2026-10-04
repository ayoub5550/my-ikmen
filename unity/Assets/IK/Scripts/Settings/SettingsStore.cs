using System;
using System.IO;
using UnityEngine;

namespace IK.Settings {
    /// <summary>
    /// Loads/saves <see cref="GameSettings"/> as JSON in
    /// <c>Application.persistentDataPath/settings.json</c>. Writes are debounced 0.5 s
    /// (§5) and flushed on pause/quit, so a change is never lost but we do not hit the
    /// filesystem on every slider tick.
    /// </summary>
    public static class SettingsStore {
        public const string FileName = "settings.json";
        public static event Action<GameSettings> Changed;

        static GameSettings current;
        static float dirtySince = -1f;
        static string overridePath;

        public static string Path =>
            overridePath ?? System.IO.Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>Test hook: redirect the file (used by EditMode tests and the UI fixture).</summary>
        public static void UseFile(string path) { overridePath = path; current = null; }

        public static GameSettings Current {
            get {
                if (current == null) current = Load();
                return current;
            }
        }

        public static GameSettings Load() {
            try {
                if (File.Exists(Path)) {
                    string json = File.ReadAllText(Path);
                    int schema = ReadSchema(json);
                    var loaded = JsonUtility.FromJson<GameSettings>(json);
                    current = GameSettings.Migrate(loaded, schema);
                } else {
                    current = new GameSettings().Clamp();
                }
            } catch (Exception e) {
                Debug.LogWarning("[IK] settings load failed, using defaults: " + e.Message);
                current = new GameSettings().Clamp();
            }
            return current;
        }

        static int ReadSchema(string json) {
            try {
                var probe = JsonUtility.FromJson<SchemaProbe>(json);
                return probe != null && probe.schemaVersion > 0 ? probe.schemaVersion : 1;
            } catch { return 1; }
        }

        [Serializable] class SchemaProbe { public int schemaVersion; }

        /// <summary>Mark dirty and notify listeners; the file is written by <see cref="Tick"/>.</summary>
        public static void MarkChanged() {
            dirtySince = Time.unscaledTime;
            Changed?.Invoke(Current);
        }

        public static void Tick() {
            if (dirtySince >= 0f && Time.unscaledTime - dirtySince >= 0.5f) Flush();
        }

        public static void Flush() {
            if (current == null) return;
            dirtySince = -1f;
            try {
                current.Clamp();
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path, JsonUtility.ToJson(current, true));
            } catch (Exception e) {
                Debug.LogWarning("[IK] settings save failed: " + e.Message);
            }
        }

        public static void Reset() {
            current = new GameSettings().Clamp();
            MarkChanged();
            Flush();
        }

        /// <summary>Round-trip helper used by tests: serialise and read back without touching disk.</summary>
        public static GameSettings RoundTrip(GameSettings s) =>
            GameSettings.Migrate(JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(s)), s.schemaVersion);
    }
}
