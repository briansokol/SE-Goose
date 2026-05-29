using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>Default rare-to-common ore feed priority, used when <c>[CRefine] order</c> is absent or empty.</summary>
        private const string DefaultRefineOrder =
            "Platinum,Uranium,Gold,Silver,Magnesium,Cobalt,Silicon,Nickel,Iron";

        /// <summary><c>MyObjectBuilder_</c> type id for ore item types.</summary>
        private const string OreTypeId = "MyObjectBuilder_Ore";

        /// <summary><c>MyObjectBuilder_</c> type id for ingot item types.</summary>
        private const string IngotTypeId = "MyObjectBuilder_Ingot";

        /// <summary>Ore item count pulled per feeder batch while topping up a refinery input.</summary>
        private const long RefineFeedBatch = 1000;

        /// <summary>Refineries in scope whose input Crane feeds. Populated during rescan.</summary>
        private readonly List<IMyRefinery> _refineries = new List<IMyRefinery>();

        /// <summary>Base ore feed priority (subtype names) parsed from <c>[CRefine] order</c>.</summary>
        private readonly List<string> _refineOrder = new List<string>();

        /// <summary>Per-ingot thresholds (keyed by ingot/ore subtype) parsed from the <c>[CRefine]</c> section.</summary>
        private readonly Dictionary<string, RefineThreshold> _refineThresholds =
            new Dictionary<string, RefineThreshold>(StringComparer.Ordinal);

        /// <summary>Grid-wide ingot totals by subtype, rebuilt each balancing cycle.</summary>
        private readonly Dictionary<string, long> _refineIngotTotals = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>Grid-wide ore totals (in cargo) by subtype, rebuilt each balancing cycle.</summary>
        private readonly Dictionary<string, long> _refineOreTotals = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>Reusable scratch buffer for refinery inventory scans.</summary>
        private readonly List<MyInventoryItem> _refineItemBuffer = new List<MyInventoryItem>();

        /// <summary>Reusable scratch buffer for <c>[CRefine]</c> key enumeration.</summary>
        private readonly List<MyIniKey> _refineKeyScratch = new List<MyIniKey>();

        /// <summary>Parses the <c>[CRefine]</c> section from the already-parsed PB CustomData into <see cref="_refineOrder"/> and <see cref="_refineThresholds"/>.</summary>
        private void LoadRefineConfig()
        {
            _refineOrder.Clear();
            _refineThresholds.Clear();

            string orderRaw = _ini.Get("CRefine", "order").ToString(string.Empty);
            List<string> parsed = RefineParsing.ParseOrderLine(orderRaw);
            if (parsed.Count == 0)
            {
                parsed = RefineParsing.ParseOrderLine(DefaultRefineOrder);
            }
            for (int i = 0; i < parsed.Count; i++)
            {
                _refineOrder.Add(parsed[i]);
            }

            _refineKeyScratch.Clear();
            _ini.GetKeys("CRefine", _refineKeyScratch);
            for (int i = 0; i < _refineKeyScratch.Count; i++)
            {
                MyIniKey key = _refineKeyScratch[i];
                if (string.Equals(key.Name, "order", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string val = _ini.Get(key.Section, key.Name).ToString(string.Empty);
                RefineThreshold threshold;
                if (RefineParsing.TryParseThreshold(val, out threshold))
                {
                    _refineThresholds[key.Name] = threshold;
                }
                else
                {
                    _logger.LogWarningOnce("crefine:bad-val:" + key.Name,
                        "[Crane] Bad [CRefine] threshold on '" + key.Name + "': '" + val + "'");
                }
            }
        }

        /// <summary>Feeds ore into managed refinery inputs in dynamic priority order, topping each up to the configured fill target.</summary>
        private IEnumerator<YieldReason> StepBalanceRefineries()
        {
            if (!_config.EnableRefineryBalancing || _refineries.Count == 0 || _refineOrder.Count == 0)
            {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }

            ClaimRefineryInputs(_refineries);
            BuildRefineryTotals();
            List<string> order = BuildDynamicRefineOrder(_refineOrder, _refineThresholds, _refineIngotTotals, _refineOreTotals);
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded())
            {
                yield return YieldReason.BudgetHit;
            }

            double target = _config.RefineryTargetFillPercent / 100.0;
            Action<string> debugLog = _config.DebugLogging ? (Action<string>)_logger.LogAction : null;
            for (int i = 0; i < _refineries.Count; i++)
            {
                IMyRefinery r = _refineries[i];
                if (r == null || r.Closed || !r.Enabled)
                {
                    continue;
                }
                TopUpRefinery(r, order, target, _refineOreTotals, _cargoContainers, _refineItemBuffer, BudgetExceeded, debugLog);
                yield return YieldReason.ChunkBoundary;
                if (BudgetExceeded())
                {
                    yield return YieldReason.BudgetHit;
                }
            }
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Turns off the conveyor system on every managed refinery so Crane owns the input feed.</summary>
        internal static void ClaimRefineryInputs(IList<IMyRefinery> refineries)
        {
            for (int i = 0; i < refineries.Count; i++)
            {
                IMyRefinery r = refineries[i];
                if (r == null || r.Closed)
                {
                    continue;
                }
                if (r.UseConveyorSystem)
                {
                    r.UseConveyorSystem = false;
                }
            }
        }

        /// <summary>Rebuilds grid-wide ingot totals (cargo + refinery outputs + assembler inventories) and ore totals (cargo).</summary>
        private void BuildRefineryTotals()
        {
            _refineIngotTotals.Clear();
            _refineOreTotals.Clear();

            for (int i = 0; i < _cargoContainers.Count; i++)
            {
                IMyCargoContainer cc = _cargoContainers[i];
                if (cc == null || cc.Closed)
                {
                    continue;
                }
                IMyInventory inv = cc.GetInventory(0);
                AccumulateBySubtype(inv, IngotTypeId, _refineIngotTotals, _refineItemBuffer);
                AccumulateBySubtype(inv, OreTypeId, _refineOreTotals, _refineItemBuffer);
            }

            for (int i = 0; i < _refineries.Count; i++)
            {
                IMyRefinery r = _refineries[i];
                if (r == null || r.Closed)
                {
                    continue;
                }
                AccumulateBySubtype(r.OutputInventory, IngotTypeId, _refineIngotTotals, _refineItemBuffer);
            }

            for (int i = 0; i < _assemblers.Count; i++)
            {
                IMyAssembler a = _assemblers[i];
                if (a == null || a.Closed)
                {
                    continue;
                }
                AccumulateBySubtype(a.GetInventory(0), IngotTypeId, _refineIngotTotals, _refineItemBuffer);
                AccumulateBySubtype(a.GetInventory(1), IngotTypeId, _refineIngotTotals, _refineItemBuffer);
            }
        }

        /// <summary>Adds every stack in <paramref name="inv"/> whose type id matches <paramref name="typeIdFilter"/> into <paramref name="totals"/>, keyed by subtype.</summary>
        private static void AccumulateBySubtype(IMyInventory inv, string typeIdFilter, Dictionary<string, long> totals, List<MyInventoryItem> buffer)
        {
            if (inv == null)
            {
                return;
            }
            buffer.Clear();
            try
            { inv.GetItems(buffer); }
            catch { return; }

            for (int i = 0; i < buffer.Count; i++)
            {
                MyInventoryItem item = buffer[i];
                if (!string.Equals(item.Type.TypeId, typeIdFilter, StringComparison.Ordinal))
                {
                    continue;
                }
                string sub = item.Type.SubtypeId;
                long cur;
                totals.TryGetValue(sub, out cur);
                totals[sub] = cur + (long)item.Amount;
            }
        }

        /// <summary>Tops a refinery's input toward <paramref name="targetFillFraction"/>, feeding ores in <paramref name="order"/> until the target is met or each ore is exhausted.</summary>
        internal static void TopUpRefinery(
            IMyRefinery r,
            IList<string> order,
            double targetFillFraction,
            IDictionary<string, long> oreTotals,
            IList<IMyCargoContainer> cargoContainers,
            List<MyInventoryItem> itemBuffer,
            Func<bool> budgetExceeded,
            Action<string> debugLog)
        {
            IMyInventory input = r.InputInventory;
            if (input == null)
            {
                return;
            }

            for (int i = 0; i < order.Count; i++)
            {
                if (input.VolumeFillFactor >= targetFillFraction)
                {
                    return;
                }
                string subtype = order[i];
                long oreAvail;
                if (!oreTotals.TryGetValue(subtype, out oreAvail) || oreAvail <= 0)
                {
                    continue;
                }

                var oreType = MyItemType.MakeOre(subtype);
                FeedOreUpToTarget(input, oreType, targetFillFraction, oreAvail, cargoContainers, itemBuffer, budgetExceeded, debugLog);
                if (budgetExceeded != null && budgetExceeded())
                {
                    return;
                }
            }
        }

        /// <summary>Pulls ore in batches into a refinery input until the fill target is reached, the grid supply is exhausted, or a batch moves nothing.</summary>
        private static void FeedOreUpToTarget(
            IMyInventory input,
            MyItemType oreType,
            double targetFillFraction,
            long maxItems,
            IList<IMyCargoContainer> cargoContainers,
            List<MyInventoryItem> itemBuffer,
            Func<bool> budgetExceeded,
            Action<string> debugLog)
        {
            long pulledTotal = 0;
            while (pulledTotal < maxItems)
            {
                if (input.VolumeFillFactor >= targetFillFraction)
                {
                    return;
                }
                long before = GetCurrentAmount(input, oreType, itemBuffer);
                long need = maxItems - pulledTotal;
                if (need > RefineFeedBatch)
                {
                    need = RefineFeedBatch;
                }
                PullItemIntoInventory(input, oreType, need, cargoContainers, itemBuffer, budgetExceeded, debugLog);
                long moved = GetCurrentAmount(input, oreType, itemBuffer) - before;
                if (moved <= 0)
                {
                    return;
                }
                pulledTotal += moved;
                if (budgetExceeded != null && budgetExceeded())
                {
                    return;
                }
            }
        }

        /// <summary>Builds the per-cycle ore feed order: ingots below their min (with ore available) are bumped to the front; ingots at/above their max are dropped.</summary>
        /// <param name="baseOrder">Configured base priority (ore subtype names, rare to common).</param>
        /// <param name="thresholds">Per-subtype min/max thresholds.</param>
        /// <param name="ingotTotals">Grid-wide ingot totals by subtype.</param>
        /// <param name="oreTotals">Grid-wide ore totals by subtype.</param>
        /// <returns>The ore subtypes to feed this cycle, in priority order.</returns>
        internal static List<string> BuildDynamicRefineOrder(
            IList<string> baseOrder,
            IDictionary<string, RefineThreshold> thresholds,
            IDictionary<string, long> ingotTotals,
            IDictionary<string, long> oreTotals)
        {
            var bumped = new List<string>();
            var normal = new List<string>();

            for (int i = 0; i < baseOrder.Count; i++)
            {
                string subtype = baseOrder[i];
                RefineThreshold threshold;
                thresholds.TryGetValue(subtype, out threshold);

                long ingot;
                ingotTotals.TryGetValue(subtype, out ingot);

                if (threshold != null && threshold.Max > 0 && ingot >= threshold.Max)
                {
                    continue;
                }

                long ore;
                oreTotals.TryGetValue(subtype, out ore);
                bool oreAvailable = ore > 0;

                if (threshold != null && threshold.Min > 0 && ingot < threshold.Min && oreAvailable)
                {
                    bumped.Add(subtype);
                }
                else
                {
                    normal.Add(subtype);
                }
            }

            var result = new List<string>(bumped.Count + normal.Count);
            result.AddRange(bumped);
            result.AddRange(normal);
            return result;
        }
    }
}
