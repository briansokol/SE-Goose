using System.Collections.Generic;

namespace IngameScript
{
    /// <summary>Pure-static parsers for the <c>[CRefine]</c> ore-priority order and per-ingot thresholds.</summary>
    public static class RefineParsing
    {
        /// <summary>Splits a comma-separated ore-priority line into trimmed, non-empty subtype names, preserving order.</summary>
        /// <param name="raw">Raw value (e.g. <c>Platinum,Uranium,Gold</c>).</param>
        /// <returns>The ordered subtype names; empty when <paramref name="raw"/> is blank.</returns>
        public static StringList ParseOrderLine(string raw)
        {
            var order = new StringList();
            if (string.IsNullOrEmpty(raw))
            {
                return order;
            }

            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string entry = parts[i].Trim();
                if (entry.Length > 0)
                {
                    order.Add(entry);
                }
            }
            return order;
        }

        /// <summary>Parses a <c>min,max</c> threshold value into a <see cref="RefineThreshold"/>. Both parts must be non-negative integers.</summary>
        /// <param name="raw">Raw value (e.g. <c>5000,50000</c>).</param>
        /// <param name="threshold">Parsed threshold on success; <c>null</c> on failure.</param>
        /// <returns><c>true</c> when the value has a valid <c>min,max</c> shape.</returns>
        public static bool TryParseThreshold(string raw, out RefineThreshold threshold)
        {
            threshold = null;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            string[] parts = raw.Split(',');
            if (parts.Length != 2)
            {
                return false;
            }

            long min;
            long max;
            if (!long.TryParse(parts[0].Trim(), out min) || !long.TryParse(parts[1].Trim(), out max))
            {
                return false;
            }
            if (min < 0 || max < 0)
            {
                return false;
            }

            threshold = new RefineThreshold { Min = min, Max = max };
            return true;
        }
    }
}
