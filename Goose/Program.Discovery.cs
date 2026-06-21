using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>All inventory-bearing blocks Goose currently manages.</summary>
        private readonly BlockList _allInventoryBlocks = new BlockList();

        /// <summary>Cargo containers in scope, including unmanaged ones.</summary>
        private readonly CargoList _cargoContainers = new CargoList();

        /// <summary>Ship connectors in scope.</summary>
        private readonly ConnectorList _connectors = new ConnectorList();

        /// <summary>Refineries, assemblers, and other production blocks in scope.</summary>
        private readonly List<IMyProductionBlock> _productionBlocks = new List<IMyProductionBlock>();

        /// <summary>First surfaces of in-scope blocks tagged <c>[GError]</c>, redrawn each cycle.</summary>
        private readonly SurfaceList _gerrorLcds = new SurfaceList();

        /// <summary>First surfaces of in-scope blocks tagged <c>[GStatus]</c>, redrawn each cycle.</summary>
        private readonly SurfaceList _gstatusLcds = new SurfaceList();

        /// <summary>Scratch buffer reused by status-surface enumeration during rescans.</summary>
        private readonly List<IMyTextSurfaceProvider> _surfaceProviderScratch = new List<IMyTextSurfaceProvider>();

        /// <summary>Ticks elapsed since the last rescan; initialized high to force a first-cycle rescan.</summary>
        private int _ticksSinceRescan = int.MaxValue;

        /// <summary>Set by the <c>rescan</c> command to trigger an immediate rescan on the next cycle.</summary>
        private bool _rescanRequested = false;

        /// <summary>Predicate for blocks Goose should manage: in scope, has inventory, not ignored.</summary>
        private bool IsManaged(IMyTerminalBlock block)
        {
            return InventoryScan.IsScannableInventoryBlock(block, _scopeGrids, Me)
                && !HasIgnoreTag(block.CustomName);
        }

        /// <summary>Predicate for status-display surfaces: in scope, not ignored, tagged <c>[GError]</c> or <c>[GStatus]</c>.</summary>
        private bool IsStatusSurfaceProvider(IMyTextSurfaceProvider provider)
        {
            var tb = provider as IMyTerminalBlock;
            if (tb == null || tb.Closed || tb == Me)
            {
                return false;
            }
            if (!IsInScope(tb) || HasIgnoreTag(tb.CustomName))
            {
                return false;
            }

            return BlockNameTags.NameHasTag(tb.CustomName, "[GError]")
                || BlockNameTags.NameHasTag(tb.CustomName, "[GStatus]");
        }

        /// <summary>Returns true when a previously discovered block is still alive and in scope.</summary>
        private bool ValidateBlock(IMyTerminalBlock block)
        {
            return block != null
                && !block.Closed
                && IsInScope(block);
        }

        /// <summary>Returns true when a block name carries an opt-out tag (<c>[Ignore]</c> or <c>[Locked]</c>).</summary>
        private bool HasIgnoreTag(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf("[Ignore]", StringComparison.Ordinal) >= 0
                || name.IndexOf("[Locked]", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Re-discovers managed blocks when the rescan interval elapses or a rescan is requested,
        /// also marking the configuration dirty so any name-tag changes take effect.
        /// </summary>
        private IEnumerator<YieldReason> StepRescanIfDue()
        {
            if (!_rescanRequested && _ticksSinceRescan < _config.RescanIntervalTicks)
            {
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
            if (BudgetExceeded())
            {
                yield return YieldReason.BudgetHit;
            }

            _cargoContainers.Clear();
            GridTerminalSystem.GetBlocksOfType(_cargoContainers, b => !b.Closed && IsInScope(b));
            yield return YieldReason.ChunkBoundary;

            _connectors.Clear();
            GridTerminalSystem.GetBlocksOfType(_connectors, b => !b.Closed && IsInScope(b));
            yield return YieldReason.ChunkBoundary;

            _productionBlocks.Clear();
            GridTerminalSystem.GetBlocksOfType(_productionBlocks, b => !b.Closed && IsInScope(b));
            yield return YieldReason.ChunkBoundary;

            _refineries.Clear();
            GridTerminalSystem.GetBlocksOfType(_refineries, b => !b.Closed && IsInScope(b));
            yield return YieldReason.ChunkBoundary;

            _gerrorLcds.Clear();
            _gstatusLcds.Clear();
            _surfaceProviderScratch.Clear();
            GridTerminalSystem.GetBlocksOfType(_surfaceProviderScratch, IsStatusSurfaceProvider);
            for (int i = 0; i < _surfaceProviderScratch.Count; i++)
            {
                IMyTextSurfaceProvider provider = _surfaceProviderScratch[i];
                if (provider.SurfaceCount <= 0)
                {
                    continue;
                }

                var tb = provider as IMyTerminalBlock;
                if (BlockNameTags.NameHasTag(tb.CustomName, "[GError]"))
                {
                    _gerrorLcds.Add(provider.GetSurface(0));
                }
                if (BlockNameTags.NameHasTag(tb.CustomName, "[GStatus]"))
                {
                    _gstatusLcds.Add(provider.GetSurface(0));
                }
            }
            yield return YieldReason.ChunkBoundary;

            _lastRescanSummary = "Rescan: " + _allInventoryBlocks.Count + " inv, "
                + _cargoContainers.Count + " cargo, "
                + _productionBlocks.Count + " prod, "
                + _refineries.Count + " refn, "
                + _gstatusLcds.Count + " GStatus, "
                + _gerrorLcds.Count + " GError";
        }
    }
}
