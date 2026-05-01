using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript {
    public partial class Program : MyGridProgram {
        /// <summary>Returns true when <paramref name="b"/>'s name carries the <c>[Stock]</c> tag.</summary>
        bool IsStockTagged(IMyTerminalBlock b) {
            return NameHasTag(b.CustomName, "[Stock]");
        }

        /// <summary>
        /// Resolves the inventory Goose may drain on a managed block. For production blocks
        /// this is the output inventory; only finished goods are sorted, never the input feed.
        /// </summary>
        IMyInventory GetSortableInventory(IMyTerminalBlock block) {
            IMyProductionBlock prod = block as IMyProductionBlock;
            if (prod != null) return prod.OutputInventory;
            return block.GetInventory(0);
        }

        /// <summary>Transfers up to <paramref name="maxTotal"/> units of <paramref name="type"/> from <paramref name="src"/> to <paramref name="dst"/>.</summary>
        /// <param name="src">Source inventory.</param>
        /// <param name="dst">Destination inventory.</param>
        /// <param name="type">Item type to move.</param>
        /// <param name="maxTotal">Upper bound on units moved across all matching item stacks.</param>
        /// <param name="label">Tag emitted to the action log when debug logging is on.</param>
        /// <returns>True when at least one unit was moved.</returns>
        bool MoveAllOfType(IMyInventory src, IMyInventory dst, MyItemType type, long maxTotal, string label) {
            if (src == null || dst == null) return false;
            if (src == dst) return false;
            if (!src.CanTransferItemTo(dst, type)) return false;
            long moved = 0;
            _itemBuffer.Clear();
            src.GetItems(_itemBuffer);
            for (int i = _itemBuffer.Count - 1; i >= 0 && moved < maxTotal; i--) {
                MyInventoryItem item = _itemBuffer[i];
                if (item.Type != type) continue;
                long remaining = maxTotal - moved;
                MyFixedPoint amount = item.Amount;
                if ((long)amount > remaining) amount = (MyFixedPoint)(double)remaining;
                if (!dst.CanItemsBeAdded(amount, type)) break;
                if (src.TransferItemTo(dst, i, null, true, amount)) {
                    moved += (long)amount;
                    if (_config.DebugLogging) {
                        LogAction(label + " " + amount + "x" + type.SubtypeId);
                    }
                }
            }
            return moved > 0;
        }

        /// <summary>Returns the total quantity of <paramref name="type"/> currently held in <paramref name="inv"/>.</summary>
        long GetCurrentAmount(IMyInventory inv, MyItemType type) {
            _itemBuffer.Clear();
            inv.GetItems(_itemBuffer);
            long total = 0;
            for (int i = 0; i < _itemBuffer.Count; i++) {
                if (_itemBuffer[i].Type == type) total += (long)_itemBuffer[i].Amount;
            }
            return total;
        }

        /// <summary>
        /// Wrapper around <see cref="MoveAllOfType"/> that returns the actual delta on the destination.
        /// Measuring the delta is necessary because partial transfers are common and the underlying
        /// boolean return is not a reliable indicator of how much moved.
        /// </summary>
        /// <returns>The number of units actually transferred (zero or positive).</returns>
        long TryMove(IMyInventory src, IMyInventory dst, MyItemType type, long maxAmount, string label) {
            if (src == null || dst == null || src == dst || maxAmount <= 0) return 0;
            long before = GetCurrentAmount(dst, type);
            MoveAllOfType(src, dst, type, maxAmount, label);
            long after = GetCurrentAmount(dst, type);
            return after - before;
        }

        /// <summary>
        /// Walks every <c>[Stock]</c> container and reconciles each item's actual quantity
        /// against its <see cref="StockQuota"/>, pulling in shortfalls and pushing out excess.
        /// </summary>
        IEnumerator<YieldReason> StepFulfillStockQuotas() {
            for (int s = 0; s < _stockContainers.Count; s++) {
                ContainerEntry dst = _stockContainers[s];
                if (!ValidateBlock(dst.Block) || dst.Inventory == null) continue;
                if (dst.Quotas == null) continue;

                foreach (var pair in dst.Quotas) {
                    MyItemType type = pair.Key;
                    StockQuota q = pair.Value;
                    long current = GetCurrentAmount(dst.Inventory, type);
                    long need = 0;
                    long excess = 0;
                    switch (q.Mode) {
                        case QuotaMode.Exact:
                            if (current < q.Amount) need = q.Amount - current;
                            else if (current > q.Amount) excess = current - q.Amount;
                            break;
                        case QuotaMode.Minimum:
                            if (current < q.Amount) need = q.Amount - current;
                            break;
                        case QuotaMode.Limiter:
                            if (current > q.Amount) excess = current - q.Amount;
                            break;
                        case QuotaMode.All:
                            // Effectively uncapped; CanItemsBeAdded enforces the real ceiling.
                            need = long.MaxValue;
                            break;
                    }

                    if (need > 0) PullItemFromSources(dst, type, need);
                    if (excess > 0) PushExcessToCategory(dst, type, excess);

                    if (q.Mode == QuotaMode.Exact || q.Mode == QuotaMode.Minimum) {
                        long after = GetCurrentAmount(dst.Inventory, type);
                        if (after < q.Amount) {
                            string typeShort = type.TypeId != null
                                ? type.TypeId.Replace("MyObjectBuilder_", "")
                                : "";
                            LogWarningOnce(
                                "quota:" + dst.Block.EntityId + ":" + typeShort + "/" + type.SubtypeId,
                                "[Goose] " + dst.Block.CustomName + " short on "
                                    + typeShort + "/" + type.SubtypeId
                                    + " (" + after + "/" + q.Amount + ")");
                        }
                    }

                    if (BudgetExceeded()) yield return YieldReason.BudgetHit;
                }
                yield return YieldReason.ChunkBoundary;
            }
        }

        /// <summary>
        /// Sources up to <paramref name="need"/> units of <paramref name="type"/> for <paramref name="dst"/>,
        /// preferring other stock containers' excess, then category-routed containers, then
        /// generic uncategorized inventories.
        /// </summary>
        void PullItemFromSources(ContainerEntry dst, MyItemType type, long need) {
            long remaining = need;

            for (int i = 0; i < _stockContainers.Count && remaining > 0; i++) {
                ContainerEntry src = _stockContainers[i];
                if (src == dst) continue;
                if (!ValidateBlock(src.Block) || src.Inventory == null) continue;
                if (src.Quotas == null) continue;
                StockQuota srcQ;
                if (!src.Quotas.TryGetValue(type, out srcQ)) continue;
                if (srcQ.Mode != QuotaMode.Limiter && srcQ.Mode != QuotaMode.Exact) continue;
                long srcExcess = GetCurrentAmount(src.Inventory, type) - srcQ.Amount;
                if (srcExcess <= 0) continue;
                if (!src.Inventory.CanTransferItemTo(dst.Inventory, type)) continue;
                remaining -= TryMove(src.Inventory, dst.Inventory, type, Math.Min(srcExcess, remaining), "stock<-stock");
                if (BudgetExceeded()) return;
            }

            ItemCategory cat = Classify(type);
            List<ContainerEntry> routes;
            if (remaining > 0 && _containersByCategory.TryGetValue(cat, out routes)) {
                for (int i = 0; i < routes.Count && remaining > 0; i++) {
                    ContainerEntry src = routes[i];
                    if (src == dst || src.IsStock) continue;
                    if (!ValidateBlock(src.Block) || src.Inventory == null) continue;
                    if (!src.Inventory.CanTransferItemTo(dst.Inventory, type)) continue;
                    remaining -= TryMove(src.Inventory, dst.Inventory, type, remaining, "stock<-cat");
                    if (BudgetExceeded()) return;
                }
            }

            for (int b = 0; b < _allInventoryBlocks.Count && remaining > 0; b++) {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (block == dst.Block) continue;
                if (!ValidateBlock(block)) continue;
                ContainerEntry srcEntry;
                if (_entryByBlock.TryGetValue(block, out srcEntry)) {
                    if (srcEntry.IsStock) continue;
                    // Already covered by the category-routes pass above.
                    if (srcEntry.Categories.Count > 0) continue;
                }
                IMyInventory srcInv = GetSortableInventory(block);
                if (srcInv == null) continue;
                if (!srcInv.CanTransferItemTo(dst.Inventory, type)) continue;
                remaining -= TryMove(srcInv, dst.Inventory, type, remaining, "stock<-gen");
                if (BudgetExceeded()) return;
            }
        }

        /// <summary>
        /// Pushes <paramref name="excess"/> units out of <paramref name="src"/> into a category-routed
        /// container; warns once per category when no route exists.
        /// </summary>
        void PushExcessToCategory(ContainerEntry src, MyItemType type, long excess) {
            ItemCategory cat = Classify(type);
            List<ContainerEntry> routes;
            if (!_containersByCategory.TryGetValue(cat, out routes)) {
                LogWarningOnce("noroute:" + cat, "[Goose] Excess " + type.SubtypeId + " has no [" + cat + "] route");
                return;
            }
            long remaining = excess;
            for (int i = 0; i < routes.Count && remaining > 0; i++) {
                ContainerEntry dst = routes[i];
                if (dst == src || dst.IsStock) continue;
                if (!ValidateBlock(dst.Block) || dst.Inventory == null) continue;
                if (!src.Inventory.CanTransferItemTo(dst.Inventory, type)) continue;
                remaining -= TryMove(src.Inventory, dst.Inventory, type, remaining, "stock->cat");
                if (BudgetExceeded()) return;
            }
        }

        /// <summary>
        /// Routes items from non-stock inventories into the first category-tagged container
        /// that can accept them; stock containers are handled by <see cref="StepFulfillStockQuotas"/>.
        /// </summary>
        IEnumerator<YieldReason> StepSortGenericCargo() {
            int counter = 0;
            for (int b = 0; b < _allInventoryBlocks.Count; b++) {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (!ValidateBlock(block)) continue;
                if (IsStockTagged(block)) continue;

                ContainerEntry srcEntry;
                _entryByBlock.TryGetValue(block, out srcEntry);
                if (srcEntry != null && srcEntry.ConsumerKind != ConsumerKind.None) continue;

                IMyInventory src = GetSortableInventory(block);
                if (src == null) continue;

                _itemBuffer.Clear();
                src.GetItems(_itemBuffer);

                for (int i = _itemBuffer.Count - 1; i >= 0; i--) {
                    MyInventoryItem item = _itemBuffer[i];
                    ItemCategory cat = Classify(item.Type);
                    List<ContainerEntry> routes;
                    if (!_containersByCategory.TryGetValue(cat, out routes) || routes.Count == 0) {
                        LogWarningOnce("nocat:" + cat, "[Goose] No container tagged for category " + cat);
                        continue;
                    }
                    bool atHome = false;
                    if (srcEntry != null) {
                        for (int c = 0; c < srcEntry.Categories.Count; c++) {
                            if (srcEntry.Categories[c] == cat) { atHome = true; break; }
                        }
                    }
                    if (atHome) continue;

                    for (int r = 0; r < routes.Count; r++) {
                        ContainerEntry dst = routes[r];
                        if (dst.Block == block) continue;
                        if (dst.IsStock) continue;
                        if (!ValidateBlock(dst.Block) || dst.Inventory == null) continue;
                        if (!src.CanTransferItemTo(dst.Inventory, item.Type)) continue;
                        MyFixedPoint amount = item.Amount;
                        // Skip when the route is full; SE has no direct "max addable" API, and
                        // partial-fit retries are deferred until they prove necessary.
                        if (!dst.Inventory.CanItemsBeAdded(amount, item.Type)) {
                            continue;
                        }
                        if (src.TransferItemTo(dst.Inventory, i, null, true, amount)) {
                            if (_config.DebugLogging) {
                                LogAction("sort " + amount + "x" + item.Type.SubtypeId + " ->" + dst.Block.CustomName);
                            }
                            break;
                        }
                    }
                    if (BudgetExceeded()) yield return YieldReason.BudgetHit;
                }
                counter++;
                if (counter % 10 == 0) yield return YieldReason.ChunkBoundary;
            }
        }
    }
}
