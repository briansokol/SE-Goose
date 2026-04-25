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
        List<IMyTerminalBlock> _allInventoryBlocks = new List<IMyTerminalBlock>();
        List<IMyCargoContainer> _cargoContainers = new List<IMyCargoContainer>();
        List<IMyShipConnector> _connectors = new List<IMyShipConnector>();
        List<IMyProductionBlock> _productionBlocks = new List<IMyProductionBlock>();

        int _ticksSinceRescan = int.MaxValue; // force first-cycle rescan
        bool _rescanRequested = false;

        bool IsManaged(IMyTerminalBlock block) {
            if (block == null) return false;
            if (block.Closed) return false;
            if (!block.IsSameConstructAs(Me)) return false;
            if (!block.HasInventory) return false;
            if (block == Me) return false;
            if (block is IMyShipController) return false;       // skip cockpits/seats
            if (HasIgnoreTag(block.CustomName)) return false;
            return true;
        }

        bool ValidateBlock(IMyTerminalBlock block) {
            return block != null && !block.Closed && block.IsSameConstructAs(Me);
        }

        bool HasIgnoreTag(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("[Ignore]", StringComparison.Ordinal) >= 0
                || name.IndexOf("[Locked]", StringComparison.Ordinal) >= 0;
        }

        IEnumerator<YieldReason> StepRescanIfDue() {
            if (!_rescanRequested && _ticksSinceRescan < _config.RescanIntervalTicks) {
                _ticksSinceRescan++;
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            _rescanRequested = false;
            _ticksSinceRescan = 0;
            _configDirty = true;

            _allInventoryBlocks.Clear();
            GridTerminalSystem.GetBlocksOfType(_allInventoryBlocks, IsManaged);
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded()) yield return YieldReason.BudgetHit;

            _cargoContainers.Clear();
            GridTerminalSystem.GetBlocksOfType(_cargoContainers, b => b.IsSameConstructAs(Me) && !b.Closed);
            yield return YieldReason.ChunkBoundary;

            _connectors.Clear();
            GridTerminalSystem.GetBlocksOfType(_connectors, b => b.IsSameConstructAs(Me) && !b.Closed);
            yield return YieldReason.ChunkBoundary;

            _productionBlocks.Clear();
            GridTerminalSystem.GetBlocksOfType(_productionBlocks, b => b.IsSameConstructAs(Me) && !b.Closed);
            yield return YieldReason.ChunkBoundary;

            LogAction("Rescan: " + _allInventoryBlocks.Count + " inv, "
                + _cargoContainers.Count + " cargo, "
                + _productionBlocks.Count + " prod");
        }
    }
}
