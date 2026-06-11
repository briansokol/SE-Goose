using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>Reusable item buffer for the per-tick inventory walks in the feeder.</summary>
        private readonly InvItemList _feederItemBuffer = new InvItemList();

        /// <summary>Reusable production-queue buffer for the per-tick queue walks in the feeder.</summary>
        private readonly List<MyProductionItem> _feederQueueBuffer = new List<MyProductionItem>();

        /// <summary>Reusable list of ingot item types, rebuilt each pass from <see cref="ItemCatalog.KnownItems"/>.</summary>
        private readonly ItemTypeList _feederIngotTypes = new ItemTypeList();

        /// <summary>Reusable scratch set for queued blueprint subtypes during disassembly staging.</summary>
        private readonly StringSet _feederQueuedSubtypes = new StringSet(StringComparer.Ordinal);

        /// <summary>
        /// Per-assembler material feeder. For each managed assembler:
        /// <list type="bullet">
        ///   <item><b>Empty queue:</b> drains everything from both Input and Output back to cargo.</item>
        ///   <item><b>Assembly mode:</b> drains non-ingots from Input; tops up every known ingot type
        ///   in Input to <see cref="CraneConfig.AssemblerIngotKeep"/>. Output is left alone so Goose
        ///   can drain finished components.</item>
        ///   <item><b>Disassembly mode:</b> drains Input (whatever falls out is for Goose to sort);
        ///   drains non-queued items from Output; stages queued components into Output up to the
        ///   queued amount.</item>
        /// </list>
        /// Yields <see cref="YieldReason.BudgetHit"/> and breaks when the per-tick instruction
        /// budget is hit; yields <see cref="YieldReason.ChunkBoundary"/> between assemblers.
        /// </summary>
        private IEnumerator<YieldReason> StepFeedAssemblers()
        {
            if (!_config.EnableAutocraft || _assemblers.Count == 0)
            {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }

            CollectIngotTypes(_catalog.KnownItems, _feederIngotTypes);
            long ingotKeep = Math.Max(1, (long)_config.AssemblerIngotKeep);
            Action<string> debugLog = _config.DebugLogging ? new Action<string>(_logger.LogAction) : null;

            for (int ai = 0; ai < _assemblers.Count; ai++)
            {
                IMyAssembler asm = _assemblers[ai];
                if (asm == null || asm.Closed)
                {
                    continue;
                }
                IMyInventory input = asm.InputInventory;
                IMyInventory output = asm.OutputInventory;
                if (input == null || output == null)
                {
                    continue;
                }

                if (asm.IsQueueEmpty)
                {
                    DrainInventory(input, false, null, _cargoContainers, _feederItemBuffer, BudgetExceeded, debugLog);
                    if (BudgetExceeded())
                    {
                        yield return YieldReason.BudgetHit;
                        yield break;
                    }
                    DrainInventory(output, false, null, _cargoContainers, _feederItemBuffer, BudgetExceeded, debugLog);
                    if (BudgetExceeded())
                    {
                        yield return YieldReason.BudgetHit;
                        yield break;
                    }
                }
                else if (asm.Mode == MyAssemblerMode.Assembly)
                {
                    DrainInventory(input, true, null, _cargoContainers, _feederItemBuffer, BudgetExceeded, debugLog);
                    if (BudgetExceeded())
                    {
                        yield return YieldReason.BudgetHit;
                        yield break;
                    }
                    for (int t = 0; t < _feederIngotTypes.Count; t++)
                    {
                        MyItemType type = _feederIngotTypes[t];
                        long current = GetCurrentAmount(input, type, _feederItemBuffer);
                        if (current >= ingotKeep)
                        {
                            continue;
                        }
                        PullItemIntoInventory(input, type, ingotKeep - current, _cargoContainers, _feederItemBuffer, BudgetExceeded, debugLog);
                        if (BudgetExceeded())
                        {
                            yield return YieldReason.BudgetHit;
                            yield break;
                        }
                    }
                }
                else
                {
                    DrainInventory(input, false, null, _cargoContainers, _feederItemBuffer, BudgetExceeded, debugLog);
                    if (BudgetExceeded())
                    {
                        yield return YieldReason.BudgetHit;
                        yield break;
                    }

                    _feederQueueBuffer.Clear();
                    try
                    { asm.GetQueue(_feederQueueBuffer); }
                    catch { _feederQueueBuffer.Clear(); }

                    _feederQueuedSubtypes.Clear();
                    for (int q = 0; q < _feederQueueBuffer.Count; q++)
                    {
                        string sub = _feederQueueBuffer[q].BlueprintId.SubtypeName ?? string.Empty;
                        if (sub.Length > 0)
                        {
                            _feederQueuedSubtypes.Add(sub);
                        }
                    }

                    DrainInventory(output, false, _feederQueuedSubtypes, _cargoContainers, _feederItemBuffer, BudgetExceeded, debugLog);
                    if (BudgetExceeded())
                    {
                        yield return YieldReason.BudgetHit;
                        yield break;
                    }

                    for (int q = 0; q < _feederQueueBuffer.Count; q++)
                    {
                        MyDefinitionId bp = _feederQueueBuffer[q].BlueprintId;
                        MyItemType type;
                        if (!TryFindItemTypeForBlueprint(bp, _blueprintMap, _catalog.KnownItems, out type))
                        {
                            continue;
                        }
                        long want = CeilHelpers.CeilToLong(_feederQueueBuffer[q].Amount);
                        long current = GetCurrentAmount(output, type, _feederItemBuffer);
                        if (current >= want)
                        {
                            continue;
                        }
                        PullItemIntoInventory(output, type, want - current, _cargoContainers, _feederItemBuffer, BudgetExceeded, debugLog);
                        if (BudgetExceeded())
                        {
                            yield return YieldReason.BudgetHit;
                            yield break;
                        }
                    }
                }

                yield return YieldReason.ChunkBoundary;
            }
        }

        /// <summary>Rebuilds <paramref name="output"/> with the ingot item types from the catalog.</summary>
        internal static void CollectIngotTypes(IReadOnlyDictionary<string, MyItemType> catalog, ItemTypeList output)
        {
            output.Clear();
            foreach (KeyValuePair<string, MyItemType> kv in catalog)
            {
                if (CatalogKeyHelpers.IsIngot(kv.Value))
                {
                    output.Add(kv.Value);
                }
            }
        }

        /// <summary>Drains items out of <paramref name="inv"/> into <paramref name="cargoContainers"/>,
        /// keeping ingots when <paramref name="keepIngots"/> is set and keeping anything whose
        /// subtype is in <paramref name="keepSubtypes"/>. Iterates in reverse so live index
        /// removal stays consistent. Returns early on budget hit; caller checks the same predicate
        /// after the call.</summary>
        internal static void DrainInventory(
            IMyInventory inv,
            bool keepIngots,
            StringSet keepSubtypes,
            IList<IMyCargoContainer> cargoContainers,
            InvItemList itemBuffer,
            Func<bool> budgetExceeded,
            Action<string> debugLog)
        {
            if (inv == null)
            {
                return;
            }
            itemBuffer.Clear();
            try
            { inv.GetItems(itemBuffer); }
            catch { return; }
            for (int i = itemBuffer.Count - 1; i >= 0; i--)
            {
                MyInventoryItem item = itemBuffer[i];
                bool keep = false;
                if (keepIngots && CatalogKeyHelpers.IsIngot(item.Type))
                {
                    keep = true;
                }
                if (!keep && keepSubtypes != null && keepSubtypes.Contains(item.Type.SubtypeId ?? string.Empty))
                {
                    keep = true;
                }
                if (keep)
                {
                    continue;
                }
                DrainSingleItem(inv, item.Type, item.Amount, i, cargoContainers, debugLog);
                if (budgetExceeded != null && budgetExceeded())
                {
                    return;
                }
            }
        }

        /// <summary>Transfers a single stack out of <paramref name="src"/> to the first cargo
        /// container that accepts it. No-op when no container is willing/able to receive.</summary>
        internal static void DrainSingleItem(
            IMyInventory src,
            MyItemType type,
            MyFixedPoint amount,
            int srcIdx,
            IList<IMyCargoContainer> cargoContainers,
            Action<string> debugLog)
        {
            for (int c = 0; c < cargoContainers.Count; c++)
            {
                IMyCargoContainer cc = cargoContainers[c];
                if (cc == null || cc.Closed)
                {
                    continue;
                }
                IMyInventory dst = cc.GetInventory(0);
                if (dst == null || dst == src)
                {
                    continue;
                }
                bool canTransfer;
                bool canAccept;
                try
                {
                    canTransfer = src.CanTransferItemTo(dst, type);
                    canAccept = dst.CanItemsBeAdded(amount, type);
                }
                catch { continue; }
                if (!canTransfer || !canAccept)
                {
                    continue;
                }
                try
                {
                    if (src.TransferItemTo(dst, srcIdx, null, true, amount))
                    {
                        if (debugLog != null)
                        {
                            debugLog("feeder drain " + amount + "x" + (type.SubtypeId ?? "") + " -> " + cc.CustomName);
                        }
                        return;
                    }
                }
                catch { }
            }
        }

        /// <summary>Pulls up to <paramref name="need"/> units of <paramref name="type"/> into
        /// <paramref name="dst"/> by walking <paramref name="cargoContainers"/> and transferring
        /// matching stacks. Returns early on budget hit; caller checks the same predicate after
        /// the call.</summary>
        internal static void PullItemIntoInventory(
            IMyInventory dst,
            MyItemType type,
            long need,
            IList<IMyCargoContainer> cargoContainers,
            InvItemList itemBuffer,
            Func<bool> budgetExceeded,
            Action<string> debugLog)
        {
            if (dst == null || need <= 0)
            {
                return;
            }
            long remaining = need;
            for (int c = 0; c < cargoContainers.Count && remaining > 0; c++)
            {
                IMyCargoContainer cc = cargoContainers[c];
                if (cc == null || cc.Closed)
                {
                    continue;
                }
                IMyInventory src = cc.GetInventory(0);
                if (src == null || src == dst)
                {
                    continue;
                }
                bool canTransfer;
                try
                { canTransfer = src.CanTransferItemTo(dst, type); }
                catch { continue; }
                if (!canTransfer)
                {
                    continue;
                }
                itemBuffer.Clear();
                try
                { src.GetItems(itemBuffer); }
                catch { continue; }
                for (int i = itemBuffer.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    MyInventoryItem item = itemBuffer[i];
                    if (item.Type != type)
                    {
                        continue;
                    }
                    long available = (long)item.Amount;
                    if (available <= 0)
                    {
                        continue;
                    }
                    long take = available < remaining ? available : remaining;
                    var amt = (MyFixedPoint)(double)take;
                    bool canAccept;
                    try
                    { canAccept = dst.CanItemsBeAdded(amt, type); }
                    catch { canAccept = false; }
                    if (!canAccept)
                    {
                        break;
                    }
                    try
                    {
                        if (src.TransferItemTo(dst, i, null, true, amt))
                        {
                            remaining -= take;
                            if (debugLog != null)
                            {
                                debugLog("feeder pull " + take + "x" + (type.SubtypeId ?? "") + " <- " + cc.CustomName);
                            }
                        }
                    }
                    catch { }
                    if (budgetExceeded != null && budgetExceeded())
                    {
                        return;
                    }
                }
                if (budgetExceeded != null && budgetExceeded())
                {
                    return;
                }
            }
        }

        /// <summary>Sums all stacks of <paramref name="type"/> in <paramref name="inv"/>.</summary>
        internal static long GetCurrentAmount(IMyInventory inv, MyItemType type, InvItemList itemBuffer)
        {
            if (inv == null)
            {
                return 0;
            }
            itemBuffer.Clear();
            try
            { inv.GetItems(itemBuffer); }
            catch { return 0; }
            long total = 0;
            for (int i = 0; i < itemBuffer.Count; i++)
            {
                MyInventoryItem item = itemBuffer[i];
                if (item.Type == type)
                {
                    total += (long)item.Amount;
                }
            }
            return total;
        }

        /// <summary>Reverse-lookup: given a queued blueprint, walks <paramref name="blueprintMap"/>
        /// for an entry whose stored subtype matches, then resolves the catalog key against
        /// <paramref name="catalog"/>. Returns <c>false</c> when no learned mapping exists yet.</summary>
        internal static bool TryFindItemTypeForBlueprint(
            MyDefinitionId bp,
            IReadOnlyDictionary<string, string> blueprintMap,
            IReadOnlyDictionary<string, MyItemType> catalog,
            out MyItemType type)
        {
            type = default(MyItemType);
            string bpSub = bp.SubtypeName ?? string.Empty;
            if (bpSub.Length == 0)
            {
                return false;
            }
            foreach (KeyValuePair<string, string> kv in blueprintMap)
            {
                if (kv.Value == bpSub)
                {
                    if (catalog.TryGetValue(kv.Key, out type))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
