using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>Builds per-item-type totals across a set of managed blocks. Single-shot variant for tests; iterator variant for in-game use with budget yields.</summary>
    public static class ItemTotalsBuilder
    {
        /// <summary>Accumulates totals across all inventories on <paramref name="blocks"/> (every <see cref="IMyTerminalBlock.GetInventory(int)"/> from index 0 to <see cref="IMyTerminalBlock.InventoryCount"/>-1) into <paramref name="totals"/>. Records each observed type in <paramref name="catalog"/> when non-null.</summary>
        public static void BuildItemTotals(
            IEnumerable<IMyTerminalBlock> blocks,
            Dictionary<MyItemType, long> totals,
            ItemCatalog catalog,
            List<MyInventoryItem> buffer)
        {
            totals.Clear();
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
                        if (catalog != null)
                        {
                            catalog.RecordItem(item.Type);
                        }
                    }
                }
            }
        }
    }
}
