using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript {
    /// <summary>Pure-static block-discovery helpers. Scope membership is passed in explicitly; no instance state.</summary>
    public static class BlockDiscovery {
        /// <summary>Predicate for managed blocks: in scope, has inventory, not a ship controller, not the PB itself, not opt-out tagged.</summary>
        /// <param name="block">Candidate block.</param>
        /// <param name="scopeGrids">Set of grid EntityIds currently in management scope.</param>
        /// <param name="pbBlock">The host PB block (excluded from management).</param>
        public static bool IsManaged(IMyTerminalBlock block, HashSet<long> scopeGrids, IMyTerminalBlock pbBlock) {
            if (block == null) return false;
            if (block.Closed) return false;
            if (block.CubeGrid == null) return false;
            if (scopeGrids == null || !scopeGrids.Contains(block.CubeGrid.EntityId)) return false;
            if (!block.HasInventory) return false;
            if (pbBlock != null && block == pbBlock) return false;
            if (block is IMyShipController) return false;
            if (BlockNameTags.HasIgnoreTag(block.CustomName)) return false;
            return true;
        }

        /// <summary>Returns true when a previously discovered block is still alive and in scope.</summary>
        public static bool ValidateBlock(IMyTerminalBlock block, HashSet<long> scopeGrids) {
            return block != null
                && !block.Closed
                && block.CubeGrid != null
                && scopeGrids != null
                && scopeGrids.Contains(block.CubeGrid.EntityId);
        }
    }
}
