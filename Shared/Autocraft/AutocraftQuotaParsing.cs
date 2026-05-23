using System;

namespace IngameScript {
    /// <summary>Autocraft quota suffix mode. Distinct from <see cref="QuotaMode"/> because autocraft only has two true modes: Minimum (assemble-only floor) and Exact (assemble below, disassemble above).</summary>
    public enum AutocraftMode {
        /// <summary>No suffix: keep at least N units, surplus allowed.</summary>
        Minimum,
        /// <summary>Suffix <c>E</c>: assemble below N, disassemble above N.</summary>
        Exact
    }

    /// <summary>Pure-static parser for Crane autocraft quota values: <c>x</c>/<c>X</c> sentinel, bare integer (Minimum), <c>E</c>/<c>e</c> (Exact), <c>L</c>/<c>l</c> silent-alias-to-Exact.</summary>
    public static class AutocraftQuotaParsing {
        /// <summary>Parses an autocraft quota value.</summary>
        /// <param name="raw">Raw value (e.g. <c>x</c>, <c>500</c>, <c>200E</c>, <c>100L</c>).</param>
        /// <param name="target">Parsed target amount (0 when <paramref name="ignore"/> is true).</param>
        /// <param name="mode">Resolved <see cref="AutocraftMode"/>.</param>
        /// <param name="ignore">True when value is the <c>x</c>/<c>X</c> sentinel (managed but inactive).</param>
        /// <returns><c>true</c> when the value parses cleanly.</returns>
        public static bool TryParseAutocraftQuotaValue(string raw, out long target, out AutocraftMode mode, out bool ignore) {
            target = 0;
            mode = AutocraftMode.Minimum;
            ignore = false;
            if (string.IsNullOrEmpty(raw)) return false;
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) return false;
            if (trimmed.Equals("x", StringComparison.OrdinalIgnoreCase)) {
                ignore = true;
                return true;
            }
            char suffix = trimmed[trimmed.Length - 1];
            string numericPart = trimmed;
            if (suffix == 'E' || suffix == 'e') {
                mode = AutocraftMode.Exact;
                numericPart = trimmed.Substring(0, trimmed.Length - 1);
            } else if (suffix == 'L' || suffix == 'l') {
                mode = AutocraftMode.Exact;
                numericPart = trimmed.Substring(0, trimmed.Length - 1);
            }
            return long.TryParse(numericPart, out target);
        }
    }
}
