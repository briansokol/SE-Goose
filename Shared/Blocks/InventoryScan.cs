using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>Shared predicate for discovering inventory-bearing blocks within a script's scope. Used by both Goose and Crane so their grid-wide item totals cover the same set of blocks.</summary>
    public static class InventoryScan
    {
        /// <summary>Returns true when <paramref name="block"/> is a live, in-scope, inventory-bearing block that should contribute to item totals. Excludes the PB itself and ship controllers (volatile pilot inventories). Callers apply their own name-tag opt-out filter on top.</summary>
        /// <param name="block">Candidate block.</param>
        /// <param name="scopeGrids">EntityIds of grids currently in management scope.</param>
        /// <param name="me">The running programmable block, excluded from the result.</param>
        public static bool IsScannableInventoryBlock(IMyTerminalBlock block, LongSet scopeGrids, IMyTerminalBlock me)
        {
            return block != null
                && !block.Closed
                && block.CubeGrid != null
                && scopeGrids.Contains(block.CubeGrid.EntityId)
                && block.HasInventory
                && block != me
                && !(block is IMyShipController);
        }
    }
}
