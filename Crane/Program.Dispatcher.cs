using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>Step labels for the Crane pipeline; indices align with <see cref="StepFor"/>.</summary>
        private static readonly string[] CraneStepLabels = {
            "ScopeRefresh", "BlockRescan", "ParseConfig", "ScanInventories",
            "LoadCCraftConfig", "QuotaEngine", "AssemblerPool",
            "DispatchAndReconcile", "FeedAssemblers", "RenderStatus", "PersistCatalog"
        };

        /// <summary>Returns the iterator for step <paramref name="i"/>.</summary>
        private IEnumerator<YieldReason> StepFor(int i)
        {
            switch (i)
            {
                case 0:
                    return StepRebuildScopeIfDue();
                case 1:
                    return StepRescanIfDue();
                case 2:
                    return StepParseConfigIfDirty();
                case 3:
                    return StepScanInventories();
                case 4:
                    return StepLoadCCraftConfig();
                case 5:
                    return StepQuotaEngine();
                case 6:
                    return StepAssemblerPool();
                case 7:
                    return StepDispatchAndReconcile();
                case 8:
                    return StepFeedAssemblers();
                case 9:
                    return StepRenderStatus();
                case 10:
                    return StepPersistCatalog();
                default:
                    return NoOpStep();
            }
        }

        /// <summary>Defensive no-op iterator returned when step index drifts out of range.</summary>
        private IEnumerator<YieldReason> NoOpStep()
        {
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Returns true when consumed instructions exceed the configured per-tick budget.</summary>
        private bool BudgetExceeded()
        {
            return Runtime.CurrentInstructionCount >
                   Runtime.MaxInstructionCount * _config.BudgetFraction;
        }

        /// <summary>Per-item-type totals from the most recent inventory scan over Crane's scope.</summary>
        private readonly Dictionary<VRage.Game.ModAPI.Ingame.MyItemType, long> _itemTotals =
            new Dictionary<VRage.Game.ModAPI.Ingame.MyItemType, long>();

        /// <summary>Reusable inventory item scratch buffer.</summary>
        private readonly List<VRage.Game.ModAPI.Ingame.MyInventoryItem> _itemBuffer =
            new List<VRage.Game.ModAPI.Ingame.MyInventoryItem>();

        /// <summary>Builds <see cref="_itemTotals"/> via shared <see cref="ItemTotalsBuilder"/>. Records new types in <see cref="_catalog"/>.</summary>
        /// <summary>Builds <see cref="_itemTotals"/> via shared <see cref="ItemTotalsBuilder"/>, draining its coroutine so per-block <see cref="YieldReason.ChunkBoundary"/> / <see cref="YieldReason.BudgetHit"/> yields propagate up to the dispatcher pump. Records new types in <see cref="_catalog"/>.</summary>
        private IEnumerator<YieldReason> StepScanInventories()
        {
            foreach (YieldReason r in ItemTotalsBuilder.BuildItemTotals(
                _allManagedBlocks, _itemTotals, _catalog, _itemBuffer, BudgetExceeded))
            {
                yield return r;
            }
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Persists the catalog to Storage. No-op when the catalog is empty.</summary>
        private IEnumerator<YieldReason> StepPersistCatalog()
        {
            string blob = _catalog.BuildStorageBlob();
            if (!string.Equals(blob, Storage, System.StringComparison.Ordinal))
            {
                Storage = blob;
            }
            yield return YieldReason.ChunkBoundary;
        }
    }
}
