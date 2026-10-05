using System;
using System.Collections.Generic;

namespace IK.Core {
    /// <summary>
    /// dev.7: allocation-free lower-casing for names the engine compares every tick (trigger and
    /// controller names, AssertSpecial flags, const keys …). Mono / IL2CPP's
    /// <c>string.ToLowerInvariant</c> allocates a new string on every call even when the text is
    /// already lower case; with hundreds of trigger evaluations per tick that was ~45 KB of
    /// garbage per tick (state -1 alone) and a GC every second or two on the phone.
    /// The first call for a given string instance's text caches the result.
    /// </summary>
    public static class Lower {
        static readonly Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
        const int MaxEntries = 50000;

        public static string Of(string s) {
            if (s == null) return null;
            if (map.TryGetValue(s, out var l)) return l;
            l = s.ToLowerInvariant();
            if (map.Count < MaxEntries) map[s] = l;
            return l;
        }

        /// <summary>Cached <c>ToLowerInvariant</c>.</summary>
        public static string Lc(this string s) => Of(s);
    }
}
