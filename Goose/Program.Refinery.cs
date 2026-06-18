using System;
using System.Collections.Generic;
using System.Text;

namespace IngameScript
{
    public partial class Program
    {
        /// <summary>Refineries in scope, refreshed each rescan.</summary>
        private readonly List<Sandbox.ModAPI.Ingame.IMyRefinery> _refineries = new List<Sandbox.ModAPI.Ingame.IMyRefinery>();

        /// <summary>Scratch list for a refinery's active ore order; reused per refinery to avoid GC.</summary>
        private readonly List<string> _refineryOrderScratch = new List<string>();

        /// <summary>Vanilla ores in default refinery feed priority (highest first).</summary>
        internal static readonly string[] DefaultRefineryOres =
        {
            "Stone", "Platinum", "Uranium", "Gold", "Silver",
            "Magnesium", "Cobalt", "Silicon", "Nickel", "Iron"
        };

        /// <summary>Object-builder type id that identifies an ore item.</summary>
        private const string OreTypeId = "MyObjectBuilder_Ore";

        /// <summary>Determines whether the given subtype is a known vanilla ore.</summary>
        /// <param name="subtype">Item subtype id to test.</param>
        /// <returns><c>true</c> if it is one of <see cref="DefaultRefineryOres"/>.</returns>
        private static bool IsKnownOre(string subtype)
        {
            for (int i = 0; i < DefaultRefineryOres.Length; i++)
            {
                if (string.Equals(DefaultRefineryOres[i], subtype, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Fills <paramref name="result"/> with the active (uncommented) ore subtypes in the
        /// block's <c>[Goose]</c> section, in file order (priority). Unknown tokens and duplicates are dropped.</summary>
        /// <param name="customData">The block's CustomData text.</param>
        /// <param name="result">List to populate with ordered ore subtypes.</param>
        internal static void ParseRefineryOrder(string customData, List<string> result)
        {
            result.Clear();
            if (string.IsNullOrEmpty(customData))
            {
                return;
            }
            string[] lines = customData.Split('\n');
            bool inGoose = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    inGoose = trimmed.Equals("[Goose]", StringComparison.Ordinal);
                    continue;
                }
                if (!inGoose || trimmed.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!IsKnownOre(trimmed) || result.Contains(trimmed))
                {
                    continue;
                }
                result.Add(trimmed);
            }
        }

        /// <summary>Indicates whether the block needs the default ore-priority template injected.</summary>
        /// <param name="customData">The block's CustomData text.</param>
        /// <returns><c>true</c> when no <c>[Goose]</c> section exists yet.</returns>
        internal static bool NeedsRefineryTemplate(string customData)
        {
            if (string.IsNullOrEmpty(customData))
            {
                return true;
            }
            return customData.IndexOf("[Goose]", StringComparison.Ordinal) < 0;
        }

        /// <summary>Returns <paramref name="existing"/> with a fully-commented default ore-priority
        /// <c>[Goose]</c> section appended.</summary>
        /// <param name="existing">Existing CustomData to preserve ahead of the new section.</param>
        /// <returns>The combined CustomData text.</returns>
        internal static string BuildRefineryTemplate(string existing)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(existing))
            {
                sb.Append(existing);
                if (!existing.EndsWith("\n", StringComparison.Ordinal))
                {
                    sb.Append('\n');
                }
                sb.Append('\n');
            }
            sb.Append("[Goose]\n");
            sb.Append("; Refinery ore priority (top = highest). Uncomment ores to feed this refinery.\n");
            sb.Append("; Highest available ore fills first; when it is gone grid-wide, the next fills in.\n");
            sb.Append("; Commented-out ores are returned to storage. Leave all commented to ignore this refinery.\n");
            for (int i = 0; i < DefaultRefineryOres.Length; i++)
            {
                sb.Append("; ");
                sb.Append(DefaultRefineryOres[i]);
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
