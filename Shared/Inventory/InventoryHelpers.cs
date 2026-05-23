using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript {
    /// <summary>Pure-static inventory transfer helpers. Logger and buffer injected so callers can reuse their per-cycle allocations.</summary>
    public static class InventoryHelpers {
        /// <summary>Resolves the inventory a sorter may drain. For production blocks this is the output inventory; only finished goods are sorted, never the input feed.</summary>
        public static IMyInventory GetSortableInventory(IMyTerminalBlock block) {
            IMyProductionBlock prod = block as IMyProductionBlock;
            if (prod != null) return prod.OutputInventory;
            return block.GetInventory(0);
        }

        /// <summary>Transfers up to <paramref name="maxTotal"/> units of <paramref name="type"/> from <paramref name="src"/> to <paramref name="dst"/>.</summary>
        /// <param name="src">Source inventory.</param>
        /// <param name="dst">Destination inventory.</param>
        /// <param name="type">Item type to move.</param>
        /// <param name="maxTotal">Upper bound on units moved across all matching item stacks.</param>
        /// <param name="label">Tag emitted to the action log when <paramref name="debugLogging"/> is on.</param>
        /// <param name="buffer">Caller-owned scratch buffer (cleared on entry). Pass a fresh <c>List&lt;MyInventoryItem&gt;</c> when no reuse is needed.</param>
        /// <param name="debugLogging">When true, emits a transfer line to <paramref name="logAction"/>.</param>
        /// <param name="logAction">Action-log sink. May be null when <paramref name="debugLogging"/> is false.</param>
        public static bool MoveAllOfType(IMyInventory src, IMyInventory dst, MyItemType type, long maxTotal, string label, List<MyInventoryItem> buffer, bool debugLogging, Action<string> logAction) {
            if (src == null || dst == null) return false;
            if (src == dst) return false;
            if (!src.CanTransferItemTo(dst, type)) return false;
            long moved = 0;
            buffer.Clear();
            src.GetItems(buffer);
            for (int i = buffer.Count - 1; i >= 0 && moved < maxTotal; i--) {
                MyInventoryItem item = buffer[i];
                if (item.Type != type) continue;
                long remaining = maxTotal - moved;
                MyFixedPoint amount = item.Amount;
                if ((long)amount > remaining) amount = (MyFixedPoint)(double)remaining;
                if (!dst.CanItemsBeAdded(amount, type)) break;
                if (src.TransferItemTo(dst, i, null, true, amount)) {
                    moved += (long)amount;
                    if (debugLogging && logAction != null) {
                        logAction(label + " " + amount + "x" + type.SubtypeId);
                    }
                }
            }
            return moved > 0;
        }

        /// <summary>Returns the total quantity of <paramref name="type"/> currently held in <paramref name="inv"/>.</summary>
        public static long GetCurrentAmount(IMyInventory inv, MyItemType type, List<MyInventoryItem> buffer) {
            buffer.Clear();
            inv.GetItems(buffer);
            long total = 0;
            for (int i = 0; i < buffer.Count; i++) {
                if (buffer[i].Type == type) total += (long)buffer[i].Amount;
            }
            return total;
        }

        /// <summary>Wrapper around <see cref="MoveAllOfType"/> that returns the actual delta on the destination. Partial transfers are common and the underlying boolean return is not a reliable indicator of how much moved.</summary>
        public static long TryMove(IMyInventory src, IMyInventory dst, MyItemType type, long maxAmount, string label, List<MyInventoryItem> buffer, bool debugLogging, Action<string> logAction) {
            if (src == null || dst == null || src == dst || maxAmount <= 0) return 0;
            long before = GetCurrentAmount(dst, type, buffer);
            MoveAllOfType(src, dst, type, maxAmount, label, buffer, debugLogging, logAction);
            long after = GetCurrentAmount(dst, type, buffer);
            return after - before;
        }
    }
}
