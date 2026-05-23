using System;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript {
    /// <summary>Pure-static parsers for stock-quota INI keys and values.</summary>
    public static class QuotaParsing {
        /// <summary>Splits a quota key shaped like <c>Type/Subtype</c> into its prefixed type id and subtype id.</summary>
        /// <param name="key">Quota key (e.g. <c>Component/SteelPlate</c> or <c>MyObjectBuilder_Ingot/Iron</c>).</param>
        /// <param name="typeIdWithPrefix">Full type id including the <c>MyObjectBuilder_</c> prefix.</param>
        /// <param name="subtypeId">Subtype portion of the key.</param>
        /// <returns><c>true</c> when the key has a valid <c>Type/Subtype</c> shape.</returns>
        public static bool TryParseQuotaKeyShape(string key, out string typeIdWithPrefix, out string subtypeId) {
            typeIdWithPrefix = null;
            subtypeId = null;
            if (string.IsNullOrEmpty(key)) return false;
            int slash = key.IndexOf('/');
            if (slash <= 0 || slash >= key.Length - 1) return false;
            string typeHalf = key.Substring(0, slash);
            string subHalf = key.Substring(slash + 1);
            typeIdWithPrefix = typeHalf.StartsWith("MyObjectBuilder_", StringComparison.Ordinal)
                ? typeHalf
                : "MyObjectBuilder_" + typeHalf;
            subtypeId = subHalf;
            return true;
        }

        /// <summary>Wraps <see cref="MyItemType.Parse"/> to return a nullable on failure instead of throwing.</summary>
        public static MyItemType? ResolveItemTypeViaParse(string fullyQualified) {
            try {
                return MyItemType.Parse(fullyQualified);
            } catch {
                return null;
            }
        }

        /// <summary>Parses a quota value into an amount and mode. Accepts a bare integer (Exact), integer + <c>M</c>/<c>m</c> (Minimum), integer + <c>L</c>/<c>l</c> (Limiter), or literal <c>All</c>/<c>all</c> (uncapped).</summary>
        /// <param name="raw">Raw value (e.g. <c>100</c>, <c>500M</c>, <c>250L</c>, <c>All</c>).</param>
        /// <param name="amount">Parsed amount (0 when <paramref name="mode"/> is <see cref="QuotaMode.All"/>).</param>
        /// <param name="mode">Resolved quota mode.</param>
        public static bool TryParseQuotaValue(string raw, out long amount, out QuotaMode mode) {
            amount = 0;
            mode = QuotaMode.Exact;
            if (string.IsNullOrEmpty(raw)) return false;
            if (raw.Equals("All", StringComparison.OrdinalIgnoreCase)) {
                mode = QuotaMode.All;
                return true;
            }
            char suffix = raw[raw.Length - 1];
            string numericPart = raw;
            if (suffix == 'M' || suffix == 'm') { mode = QuotaMode.Minimum; numericPart = raw.Substring(0, raw.Length - 1); }
            else if (suffix == 'L' || suffix == 'l') { mode = QuotaMode.Limiter; numericPart = raw.Substring(0, raw.Length - 1); }
            return long.TryParse(numericPart, out amount);
        }

        /// <summary>Clamps a percent value to the inclusive range 0-100.</summary>
        public static int ClampPercent(int raw) {
            if (raw < 0) return 0;
            if (raw > 100) return 100;
            return raw;
        }

        /// <summary>Returns true when <paramref name="s"/> is a non-empty C-style identifier.</summary>
        public static bool IsIdentifier(string s) {
            if (string.IsNullOrEmpty(s)) return false;
            char c = s[0];
            if (!(char.IsLetter(c) || c == '_')) return false;
            for (int i = 1; i < s.Length; i++) {
                c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            }
            return true;
        }

        /// <summary>Returns true when <paramref name="key"/> matches the <c>[MyObjectBuilder_]Type/Subtype</c> shape used by stock quota entries.</summary>
        public static bool IsValidQuotaKey(string key) {
            if (string.IsNullOrEmpty(key)) return false;
            int slash = key.IndexOf('/');
            if (slash <= 0 || slash >= key.Length - 1) return false;
            string typeHalf = key.Substring(0, slash);
            string subHalf = key.Substring(slash + 1);
            return IsIdentifier(typeHalf) && IsIdentifier(subHalf);
        }
    }
}
