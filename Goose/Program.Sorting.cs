using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>Returns true when <paramref name="b"/>'s name carries the <c>[Stock]</c> tag.</summary>
        private bool IsStockTagged(IMyTerminalBlock b)
        {
            return BlockNameTags.NameHasTag(b.CustomName, "[Stock]");
        }

        /// <summary>
        /// Resolves the inventory Goose may drain on a managed block. For production blocks
        /// this is the output inventory; only finished goods are sorted, never the input feed.
        /// </summary>
        /// <summary>Returns the inventory Goose should drain when sorting/balancing this block.
        /// For assemblers, follows the mode: <c>Assembly</c> drains the output side (finished
        /// components), <c>Disassembly</c> drains the input side (produced ingots). Other
        /// production blocks always drain output. Non-production blocks fall back to inventory 0.</summary>
        internal static IMyInventory GetSortableInventory(IMyTerminalBlock block)
        {
            var asm = block as IMyAssembler;
            if (asm != null)
            {
                return asm.Mode == MyAssemblerMode.Disassembly
                    ? asm.InputInventory
                    : asm.OutputInventory;
            }

            var prod = block as IMyProductionBlock;
            if (prod != null)
            {
                return prod.OutputInventory;
            }

            return block.GetInventory(0);
        }

        /// <summary>Transfers up to <paramref name="maxTotal"/> units of <paramref name="type"/> from <paramref name="src"/> to <paramref name="dst"/>.</summary>
        /// <param name="src">Source inventory.</param>
        /// <param name="dst">Destination inventory.</param>
        /// <param name="type">Item type to move.</param>
        /// <param name="maxTotal">Upper bound on units moved across all matching item stacks.</param>
        /// <param name="label">Tag emitted to the action log when debug logging is on.</param>
        /// <returns>True when at least one unit was moved.</returns>
        private bool MoveAllOfType(IMyInventory src, IMyInventory dst, MyItemType type, long maxTotal, string label)
        {
            if (src == null || dst == null)
            {
                return false;
            }

            if (src == dst)
            {
                return false;
            }

            if (!src.CanTransferItemTo(dst, type))
            {
                return false;
            }

            long moved = 0;
            _itemBuffer.Clear();
            src.GetItems(_itemBuffer);
            for (int i = _itemBuffer.Count - 1; i >= 0 && moved < maxTotal; i--)
            {
                MyInventoryItem item = _itemBuffer[i];
                if (item.Type != type)
                {
                    continue;
                }

                long remaining = maxTotal - moved;
                MyFixedPoint amount = item.Amount;
                if ((long)amount > remaining)
                {
                    amount = (MyFixedPoint)(double)remaining;
                }

                if (!dst.CanItemsBeAdded(amount, type))
                {
                    break;
                }

                if (src.TransferItemTo(dst, i, null, true, amount))
                {
                    moved += (long)amount;
                    if (_config.DebugLogging)
                    {
                        LogAction(label + " " + amount + "x" + type.SubtypeId);
                    }
                }
            }
            return moved > 0;
        }

        /// <summary>Returns the total quantity of <paramref name="type"/> currently held in <paramref name="inv"/>.</summary>
        private long GetCurrentAmount(IMyInventory inv, MyItemType type)
        {
            _itemBuffer.Clear();
            inv.GetItems(_itemBuffer);
            long total = 0;
            for (int i = 0; i < _itemBuffer.Count; i++)
            {
                if (_itemBuffer[i].Type == type)
                {
                    total += (long)_itemBuffer[i].Amount;
                }
            }
            return total;
        }

        /// <summary>Fills <see cref="_quotaSnapshot"/> with the per-type quantity totals of <paramref name="inv"/> in a single pass, so quota fulfillment can read current amounts without re-scanning the inventory for every quota.</summary>
        /// <param name="inv">Inventory to snapshot.</param>
        private void BuildQuotaSnapshot(IMyInventory inv)
        {
            _quotaSnapshot.Clear();
            _itemBuffer.Clear();
            inv.GetItems(_itemBuffer);
            for (int i = 0; i < _itemBuffer.Count; i++)
            {
                MyInventoryItem item = _itemBuffer[i];
                long current;
                _quotaSnapshot.TryGetValue(item.Type, out current);
                _quotaSnapshot[item.Type] = current + (long)item.Amount;
            }
        }

        /// <summary>
        /// Wrapper around <see cref="MoveAllOfType"/> that returns the actual delta on the destination.
        /// Measuring the delta is necessary because partial transfers are common and the underlying
        /// boolean return is not a reliable indicator of how much moved.
        /// </summary>
        /// <returns>The number of units actually transferred (zero or positive).</returns>
        private long TryMove(IMyInventory src, IMyInventory dst, MyItemType type, long maxAmount, string label)
        {
            if (src == null || dst == null || src == dst || maxAmount <= 0)
            {
                return 0;
            }

            long before = GetCurrentAmount(dst, type);
            MoveAllOfType(src, dst, type, maxAmount, label);
            long after = GetCurrentAmount(dst, type);
            return after - before;
        }

        /// <summary>Checks that a container entry's block is alive and it exposes an inventory.</summary>
        private bool ValidEntry(ContainerEntry e)
        {
            return e != null && ValidateBlock(e.Block) && e.Inventory != null;
        }

        /// <summary>Moves up to <paramref name="amount"/> of an item between <paramref name="self"/> and each route entry.</summary>
        /// <param name="pull">True to pull from routes into self; false to push from self into routes.</param>
        /// <returns>The amount left unmoved.</returns>
        private long MoveOverRoutes(ContainerEntry self, ContainerList routes, MyItemType type, long amount, string label, bool pull)
        {
            long remaining = amount;
            for (int i = 0; i < routes.Count && remaining > 0; i++)
            {
                ContainerEntry other = routes[i];
                if (other == self || other.IsStock)
                {
                    continue;
                }

                if (!ValidEntry(other))
                {
                    continue;
                }

                IMyInventory srcInv = pull ? other.Inventory : self.Inventory;
                IMyInventory dstInv = pull ? self.Inventory : other.Inventory;
                if (!srcInv.CanTransferItemTo(dstInv, type))
                {
                    continue;
                }

                remaining -= TryMove(srcInv, dstInv, type, remaining, label);
                if (BudgetExceeded())
                {
                    break;
                }
            }
            return remaining;
        }

        /// <summary>
        /// Walks every <c>[Stock]</c> container and reconciles each item's actual quantity
        /// against its <see cref="StockQuota"/>, pulling in shortfalls and pushing out excess.
        /// </summary>
        private IEnumerator<YieldReason> StepFulfillStockQuotas()
        {
            for (int s = 0; s < _stockContainers.Count; s++)
            {
                ContainerEntry dst = _stockContainers[s];
                if (!ValidEntry(dst))
                {
                    continue;
                }

                if (dst.Quotas == null)
                {
                    continue;
                }

                BuildQuotaSnapshot(dst.Inventory);

                foreach (KeyValuePair<MyItemType, StockQuota> pair in dst.Quotas)
                {
                    MyItemType type = pair.Key;
                    StockQuota q = pair.Value;
                    long current;
                    _quotaSnapshot.TryGetValue(type, out current);
                    long need = 0;
                    long excess = 0;
                    switch (q.Mode)
                    {
                        case QuotaMode.Exact:
                            if (current < q.Amount)
                            {
                                need = q.Amount - current;
                            }
                            else if (current > q.Amount)
                            {
                                excess = current - q.Amount;
                            }

                            break;
                        case QuotaMode.Minimum:
                            if (current < q.Amount)
                            {
                                need = q.Amount - current;
                            }

                            break;
                        case QuotaMode.Limiter:
                            if (current > q.Amount)
                            {
                                excess = current - q.Amount;
                            }

                            break;
                        case QuotaMode.All:
                            // Effectively uncapped; CanItemsBeAdded enforces the real ceiling.
                            need = long.MaxValue;
                            break;
                    }

                    long pulled = 0;
                    long pushed = 0;
                    if (need > 0)
                    {
                        pulled = PullItemFromSources(dst, type, need);
                    }

                    if (excess > 0)
                    {
                        pushed = PushExcessToCategory(dst, type, excess);
                    }

                    if (q.Mode == QuotaMode.Exact || q.Mode == QuotaMode.Minimum)
                    {
                        // Pull only adds this type, push only removes it, and need/excess are
                        // mutually exclusive per quota, so the snapshot value plus the moved
                        // deltas is the post-transfer amount without a second inventory scan.
                        long after = current + pulled - pushed;
                        if (after < q.Amount)
                        {
                            string typeShort = type.TypeId != null
                                ? type.TypeId.Replace("MyObjectBuilder_", "")
                                : "";
                            LogWarningOnce(
                                "quota:" + dst.Block.EntityId + ":" + typeShort + "/" + type.SubtypeId,
                                dst.Block.CustomName + " short on "
                                    + typeShort + "/" + type.SubtypeId
                                    + " (" + after + "/" + q.Amount + ")");
                        }
                    }

                    if (BudgetExceeded())
                    {
                        yield return YieldReason.BudgetHit;
                    }
                }
                yield return YieldReason.ChunkBoundary;
            }
        }

        /// <summary>
        /// Sources up to <paramref name="need"/> units of <paramref name="type"/> for <paramref name="dst"/>,
        /// preferring other stock containers' excess, then category-routed containers, then
        /// generic uncategorized inventories.
        /// </summary>
        /// <returns>The number of units actually moved into <paramref name="dst"/>.</returns>
        private long PullItemFromSources(ContainerEntry dst, MyItemType type, long need)
        {
            long remaining = need;

            for (int i = 0; i < _stockContainers.Count && remaining > 0; i++)
            {
                ContainerEntry src = _stockContainers[i];
                if (src == dst)
                {
                    continue;
                }

                if (!ValidEntry(src))
                {
                    continue;
                }

                if (src.Quotas == null)
                {
                    continue;
                }

                StockQuota srcQ;
                if (!src.Quotas.TryGetValue(type, out srcQ))
                {
                    continue;
                }

                if (srcQ.Mode != QuotaMode.Limiter && srcQ.Mode != QuotaMode.Exact)
                {
                    continue;
                }

                long srcExcess = GetCurrentAmount(src.Inventory, type) - srcQ.Amount;
                if (srcExcess <= 0)
                {
                    continue;
                }

                if (!src.Inventory.CanTransferItemTo(dst.Inventory, type))
                {
                    continue;
                }

                remaining -= TryMove(src.Inventory, dst.Inventory, type, Math.Min(srcExcess, remaining), "stock<-stock");
                if (BudgetExceeded())
                {
                    return need - remaining;
                }
            }

            ItemCategory cat = Classify(type);
            ContainerList routes;
            if (remaining > 0 && _containersByCategory.TryGetValue(cat, out routes))
            {
                remaining = MoveOverRoutes(dst, routes, type, remaining, "stock<-cat", true);
                if (BudgetExceeded())
                {
                    return need - remaining;
                }
            }

            for (int b = 0; b < _allInventoryBlocks.Count && remaining > 0; b++)
            {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (block == dst.Block)
                {
                    continue;
                }

                if (!ValidateBlock(block))
                {
                    continue;
                }

                ContainerEntry srcEntry;
                if (_entryByBlock.TryGetValue(block, out srcEntry))
                {
                    if (srcEntry.IsStock)
                    {
                        continue;
                    }
                    // Already covered by the category-routes pass above.
                    if (srcEntry.Categories.Count > 0)
                    {
                        continue;
                    }
                }
                IMyInventory srcInv = GetSortableInventory(block);
                if (srcInv == null)
                {
                    continue;
                }

                if (!srcInv.CanTransferItemTo(dst.Inventory, type))
                {
                    continue;
                }

                remaining -= TryMove(srcInv, dst.Inventory, type, remaining, "stock<-gen");
                if (BudgetExceeded())
                {
                    return need - remaining;
                }
            }

            return need - remaining;
        }

        /// <summary>
        /// Pushes <paramref name="excess"/> units out of <paramref name="src"/> into a category-routed
        /// container; warns once per category when no route exists.
        /// </summary>
        /// <returns>The number of units actually moved out of <paramref name="src"/>.</returns>
        private long PushExcessToCategory(ContainerEntry src, MyItemType type, long excess)
        {
            ItemCategory cat = Classify(type);
            ContainerList routes;
            if (!_containersByCategory.TryGetValue(cat, out routes))
            {
                LogWarningOnce("noroute:" + cat, "Excess " + type.SubtypeId + " has no " + CategoryName(cat) + " route");
                return 0;
            }
            long remaining = MoveOverRoutes(src, routes, type, excess, "stock->cat", false);
            return excess - remaining;
        }

        /// <summary>
        /// Routes items from non-stock inventories into the first category-tagged container
        /// that can accept them; stock containers are handled by <see cref="StepFulfillStockQuotas"/>.
        /// </summary>
        private IEnumerator<YieldReason> StepSortGenericCargo()
        {
            int counter = 0;
            for (int b = 0; b < _allInventoryBlocks.Count; b++)
            {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (!ValidateBlock(block))
                {
                    continue;
                }

                if (IsStockTagged(block))
                {
                    continue;
                }

                ContainerEntry srcEntry;
                _entryByBlock.TryGetValue(block, out srcEntry);
                if (srcEntry != null && srcEntry.ConsumerKind != ConsumerKind.None)
                {
                    continue;
                }

                IMyInventory src = GetSortableInventory(block);
                if (src == null)
                {
                    continue;
                }

                _itemBuffer.Clear();
                src.GetItems(_itemBuffer);

                for (int i = _itemBuffer.Count - 1; i >= 0; i--)
                {
                    MyInventoryItem item = _itemBuffer[i];
                    ItemCategory cat = Classify(item.Type);
                    ContainerList routes;
                    if (!_containersByCategory.TryGetValue(cat, out routes) || routes.Count == 0)
                    {
                        LogWarningOnce("nocat:" + cat, "No container tagged for category " + CategoryName(cat));
                        continue;
                    }
                    bool atHome = false;
                    if (srcEntry != null)
                    {
                        for (int c = 0; c < srcEntry.Categories.Count; c++)
                        {
                            if (srcEntry.Categories[c] == cat)
                            { atHome = true; break; }
                        }
                    }
                    if (atHome)
                    {
                        continue;
                    }

                    for (int r = 0; r < routes.Count; r++)
                    {
                        ContainerEntry dst = routes[r];
                        if (dst.Block == block)
                        {
                            continue;
                        }

                        if (dst.IsStock)
                        {
                            continue;
                        }

                        if (!ValidEntry(dst))
                        {
                            continue;
                        }

                        if (!src.CanTransferItemTo(dst.Inventory, item.Type))
                        {
                            continue;
                        }

                        MyFixedPoint amount = item.Amount;
                        // Skip when the route is full; SE has no direct "max addable" API, and
                        // partial-fit retries are deferred until they prove necessary.
                        if (!dst.Inventory.CanItemsBeAdded(amount, item.Type))
                        {
                            continue;
                        }
                        if (src.TransferItemTo(dst.Inventory, i, null, true, amount))
                        {
                            if (_config.DebugLogging)
                            {
                                LogAction("sort " + amount + "x" + item.Type.SubtypeId + " ->" + dst.Block.CustomName);
                            }
                            break;
                        }
                    }
                    if (BudgetExceeded())
                    {
                        yield return YieldReason.BudgetHit;
                    }
                }
                counter++;
                if (counter % 10 == 0)
                {
                    yield return YieldReason.ChunkBoundary;
                }
            }
        }
    }
}
