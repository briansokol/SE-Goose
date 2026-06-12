using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>Builds per-item-type totals across a set of managed blocks. Iterator yields between blocks so the host dispatcher can stop pumping mid-walk when the per-tick instruction budget is exhausted.</summary>
    public static class ItemTotalsBuilder
    {
        /// <summary>Number of blocks scanned per <see cref="YieldReason.ChunkBoundary"/> yield. Matches the cadence used by Goose's inline scan.</summary>
        private const int BlocksPerChunk = 10;

        /// <summary>Accumulates totals across all inventories on <paramref name="blocks"/> (every <see cref="IMyTerminalBlock.GetInventory(int)"/> from index 0 to <see cref="IMyTerminalBlock.InventoryCount"/>-1) into <paramref name="totals"/>. Records each observed type in <paramref name="catalog"/> when non-null. Yields <see cref="YieldReason.ChunkBoundary"/> every <see cref="BlocksPerChunk"/> blocks and <see cref="YieldReason.BudgetHit"/> when <paramref name="budgetExceeded"/> returns true so the host dispatcher can stop pumping for this tick.</summary>
        /// <param name="blocks">Pre-filtered, in-scope blocks to scan. Null entries are skipped.</param>
        /// <param name="totals">Output map; cleared at the start of the walk.</param>
        /// <param name="catalog">Optional catalog to record each observed <see cref="MyItemType"/> in.</param>
        /// <param name="buffer">Scratch list reused for each inventory's items.</param>
        /// <param name="budgetExceeded">Optional callback queried after each block. May be null in tests, which still drain via foreach but ignore the yields.</param>
        /// <param name="onNewCatalogKey">Optional callback invoked once per genuinely new catalog key (<see cref="ItemCatalog.RecordItem"/> returned <c>true</c>). Used by the Goose-Crane bridge to announce local catalog growth to the peer.</param>
        public static IEnumerable<YieldReason> BuildItemTotals(
            IEnumerable<IMyTerminalBlock> blocks,
            LongByItemType totals,
            ItemCatalog catalog,
            InvItemList buffer,
            Func<bool> budgetExceeded,
            Action<MyItemType> onNewCatalogKey = null)
        {
            totals.Clear();
            int counter = 0;
            foreach (IMyTerminalBlock block in blocks)
            {
                if (block == null)
                {
                    continue;
                }

                for (int invIdx = 0; invIdx < block.InventoryCount; invIdx++)
                {
                    IMyInventory inv = block.GetInventory(invIdx);
                    if (inv == null)
                    {
                        continue;
                    }

                    buffer.Clear();
                    inv.GetItems(buffer);
                    for (int i = 0; i < buffer.Count; i++)
                    {
                        MyInventoryItem item = buffer[i];
                        long current;
                        totals.TryGetValue(item.Type, out current);
                        totals[item.Type] = current + (long)item.Amount;
                        if (catalog != null && catalog.RecordItem(item.Type) && onNewCatalogKey != null)
                        {
                            onNewCatalogKey(item.Type);
                        }
                    }
                }

                counter++;
                if (counter % BlocksPerChunk == 0)
                {
                    yield return YieldReason.ChunkBoundary;
                }

                if (budgetExceeded != null && budgetExceeded())
                {
                    yield return YieldReason.BudgetHit;
                }
            }
        }
    }
}
