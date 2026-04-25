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
        bool IsStockTagged(IMyTerminalBlock b) {
            return NameHasTag(b.CustomName, "[Stock]");
        }

        IMyInventory GetSortableInventory(IMyTerminalBlock block) {
            // For production blocks, only the OUTPUT is drainable in v1.
            // GetInventory(1) is OutputInventory on refineries/assemblers.
            IMyProductionBlock prod = block as IMyProductionBlock;
            if (prod != null) return prod.OutputInventory;
            return block.GetInventory(0);
        }

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

        long GetCurrentAmount(IMyInventory inv, MyItemType type) {
            _itemBuffer.Clear();
            inv.GetItems(_itemBuffer);
            long total = 0;
            for (int i = 0; i < _itemBuffer.Count; i++) {
                if (_itemBuffer[i].Type == type) total += (long)_itemBuffer[i].Amount;
            }
            return total;
        }

        // NEW: shared helper introduced by the plan refactor.
        // Returns the amount actually moved (>= 0). Measures dst delta so we don't
        // trust MoveAllOfType's bool return — partial moves are common.
        long TryMove(IMyInventory src, IMyInventory dst, MyItemType type, long maxAmount, string label) {
            if (src == null || dst == null || src == dst || maxAmount <= 0) return 0;
            long before = GetCurrentAmount(dst, type);
            MoveAllOfType(src, dst, type, maxAmount, label);
            long after = GetCurrentAmount(dst, type);
            return after - before;
        }

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
                            need = long.MaxValue;        // capped by CanItemsBeAdded
                            break;
                    }

                    if (need > 0) PullItemFromSources(dst, type, need);
                    if (excess > 0) PushExcessToCategory(dst, type, excess);

                    if (BudgetExceeded()) yield return YieldReason.BudgetHit;
                }
                yield return YieldReason.ChunkBoundary;
            }
        }

        void PullItemFromSources(ContainerEntry dst, MyItemType type, long need) {
            long remaining = need;

            // Layer 1: other stock containers' Limiter/Exact excess of same type
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
                remaining -= TryMove(src.Inventory, dst.Inventory, type, Math.Min(srcExcess, remaining), "stock<-stock");
                if (BudgetExceeded()) return;
            }

            // Layer 2: category-route containers (non-stock)
            ItemCategory cat = Classify(type);
            List<ContainerEntry> routes;
            if (remaining > 0 && _containersByCategory.TryGetValue(cat, out routes)) {
                for (int i = 0; i < routes.Count && remaining > 0; i++) {
                    ContainerEntry src = routes[i];
                    if (src == dst || src.IsStock) continue;
                    if (!ValidateBlock(src.Block) || src.Inventory == null) continue;
                    remaining -= TryMove(src.Inventory, dst.Inventory, type, remaining, "stock<-cat");
                    if (BudgetExceeded()) return;
                }
            }

            // Layer 3: generic untagged inventories (production OUTPUTs included via GetSortableInventory)
            for (int b = 0; b < _allInventoryBlocks.Count && remaining > 0; b++) {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (block == dst.Block) continue;
                if (!ValidateBlock(block)) continue;
                ContainerEntry srcEntry;
                if (_entryByBlock.TryGetValue(block, out srcEntry)) {
                    if (srcEntry.IsStock) continue;
                    if (srcEntry.Categories.Count > 0) continue;     // already covered in layer 2
                }
                IMyInventory srcInv = GetSortableInventory(block);
                if (srcInv == null) continue;
                remaining -= TryMove(srcInv, dst.Inventory, type, remaining, "stock<-gen");
                if (BudgetExceeded()) return;
            }
        }

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
                remaining -= TryMove(src.Inventory, dst.Inventory, type, remaining, "stock->cat");
                if (BudgetExceeded()) return;
            }
        }

        IEnumerator<YieldReason> StepSortGenericCargo() {
            int counter = 0;
            for (int b = 0; b < _allInventoryBlocks.Count; b++) {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (!ValidateBlock(block)) continue;
                if (IsStockTagged(block)) continue;

                ContainerEntry srcEntry;
                _entryByBlock.TryGetValue(block, out srcEntry);

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
                    // Already home? (block is a route for this category)
                    bool atHome = false;
                    if (srcEntry != null) {
                        for (int c = 0; c < srcEntry.Categories.Count; c++) {
                            if (srcEntry.Categories[c] == cat) { atHome = true; break; }
                        }
                    }
                    if (atHome) continue;

                    // Find first route with capacity, transfer.
                    for (int r = 0; r < routes.Count; r++) {
                        ContainerEntry dst = routes[r];
                        if (dst.Block == block) continue;
                        if (dst.IsStock) continue;     // step 4 owns stock fulfillment
                        if (!ValidateBlock(dst.Block) || dst.Inventory == null) continue;
                        if (!src.CanTransferItemTo(dst.Inventory, item.Type)) continue;
                        MyFixedPoint amount = item.Amount;
                        if (!dst.Inventory.CanItemsBeAdded(amount, item.Type)) {
                            // Try partial: largest amount that fits.
                            // Simplest approach: skip when full. SE doesn't expose a direct "max addable" API
                            // without trial — implementer can iterate halving if needed; v1 just skips.
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
