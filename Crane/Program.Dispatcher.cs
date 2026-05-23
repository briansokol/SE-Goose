using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;

namespace IngameScript {
    public partial class Program : MyGridProgram {
        /// <summary>Step labels for the Crane pipeline; indices align with <see cref="StepFor"/>.</summary>
        static readonly string[] CraneStepLabels = {
            "ScopeRefresh", "BlockRescan", "ParseConfig", "ScanInventories",
            "LoadCCraftConfig", "QuotaEngine", "AssemblerPool",
            "DispatchAndReconcile", "RenderStatus", "PersistCatalog"
        };

        /// <summary>Returns the iterator for step <paramref name="i"/>.</summary>
        IEnumerator<YieldReason> StepFor(int i) {
            switch (i) {
                case 0: return StepRebuildScopeIfDue();
                case 1: return StepRescanIfDue();
                case 2: return StepParseConfigIfDirty();
                case 3: return StepScanInventories();
                case 4: return StepLoadCCraftConfig();
                case 5: return StepQuotaEngine();
                case 6: return StepAssemblerPool();
                case 7: return StepDispatchAndReconcile();
                case 8: return StepRenderStatus();
                case 9: return StepPersistCatalog();
                default: return NoOpStep();
            }
        }

        /// <summary>Defensive no-op iterator returned when step index drifts out of range.</summary>
        IEnumerator<YieldReason> NoOpStep() {
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Returns true when consumed instructions exceed the configured per-tick budget.</summary>
        bool BudgetExceeded() {
            return Runtime.CurrentInstructionCount >
                   Runtime.MaxInstructionCount * _config.BudgetFraction;
        }

        /// <summary>Per-item-type totals from the most recent inventory scan over Crane's scope.</summary>
        readonly Dictionary<VRage.Game.ModAPI.Ingame.MyItemType, long> _itemTotals =
            new Dictionary<VRage.Game.ModAPI.Ingame.MyItemType, long>();

        /// <summary>Reusable inventory item scratch buffer.</summary>
        readonly List<VRage.Game.ModAPI.Ingame.MyInventoryItem> _itemBuffer =
            new List<VRage.Game.ModAPI.Ingame.MyInventoryItem>();

        /// <summary>Builds <see cref="_itemTotals"/> via shared <see cref="ItemTotalsBuilder"/>. Records new types in <see cref="_catalog"/>.</summary>
        IEnumerator<YieldReason> StepScanInventories() {
            ItemTotalsBuilder.BuildItemTotals(_allManagedBlocks, _itemTotals, _catalog, _itemBuffer);
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Persists the catalog to Storage. No-op when the catalog is empty.</summary>
        IEnumerator<YieldReason> StepPersistCatalog() {
            string blob = _catalog.BuildStorageBlob();
            if (!string.Equals(blob, Storage, System.StringComparison.Ordinal)) {
                Storage = blob;
            }
            yield return YieldReason.ChunkBoundary;
        }
    }
}
