using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>Resolves the grid-wide "already have" count that the quota engine compares against a target.</summary>
    public static class AutocraftCounts
    {
        /// <summary>Returns Goose's authoritative grid-wide count for <paramref name="key"/> when the peer is linked and a value has been received; otherwise falls back to the local cargo+assembler scan keyed by <paramref name="type"/>.</summary>
        /// <param name="peerLinked">Whether the Goose peer is currently linked over the bridge.</param>
        /// <param name="gridCounts">Counts last reported by Goose, keyed by catalog key.</param>
        /// <param name="key">Catalog key (<c>Type/Subtype</c>) of the target item.</param>
        /// <param name="localTotals">Local item totals from Crane's own cargo+assembler scan.</param>
        /// <param name="type">Item type used to index <paramref name="localTotals"/>.</param>
        public static long EffectiveActual(bool peerLinked, Dictionary<string, long> gridCounts, string key,
            Dictionary<MyItemType, long> localTotals, MyItemType type)
        {
            if (peerLinked && gridCounts != null)
            {
                long gridCount;
                if (gridCounts.TryGetValue(key, out gridCount))
                {
                    return gridCount;
                }
            }

            long local;
            localTotals.TryGetValue(type, out local);
            return local;
        }
    }
}
