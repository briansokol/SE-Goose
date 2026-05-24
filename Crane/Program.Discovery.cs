using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>EntityIds of grids currently in management scope.</summary>
        private readonly HashSet<long> _scopeGrids = new HashSet<long>();

        /// <summary>All blocks discovered within Crane's management scope that are eligible for management.</summary>
        private readonly List<IMyTerminalBlock> _allManagedBlocks = new List<IMyTerminalBlock>();

        /// <summary>Assemblers in the managed group (filtered to non-survival-kit assemblers via interface).</summary>
        private readonly List<IMyAssembler> _assemblers = new List<IMyAssembler>();

        /// <summary>Cargo containers in scope. Used by the feeder as both sources (when pulling
        /// ingots/components into assembler inventories) and sinks (when draining mismatched
        /// items out). Populated alongside <see cref="_assemblers"/> during rescan; not added
        /// to <see cref="_allManagedBlocks"/> because Crane doesn't otherwise manage them.</summary>
        private readonly List<IMyCargoContainer> _cargoContainers = new List<IMyCargoContainer>();

        /// <summary>LCDs tagged <c>[CCraft]</c> — host quota config (CustomData) and render the status surface.</summary>
        private readonly List<IMyTextSurface> _ccraftLcds = new List<IMyTextSurface>();

        /// <summary>LCDs tagged <c>[CError]</c> — render the warning log surface.</summary>
        private readonly List<IMyTextSurface> _cerrorLcds = new List<IMyTextSurface>();

        /// <summary>Reusable mechanical-edge buffer fed to <see cref="ScopeBuilder.BuildScope"/>.</summary>
        private readonly List<MechanicalEdge> _scopeMechBuf = new List<MechanicalEdge>();

        /// <summary>Reusable raw mechanical block buffer.</summary>
        private readonly List<IMyMechanicalConnectionBlock> _scopeMechRaw = new List<IMyMechanicalConnectionBlock>();

        /// <summary>Snapshot of mechanical edges from the most recent scope build.</summary>
        private readonly List<MechanicalEdge> _scopeMechCache = new List<MechanicalEdge>();

        /// <summary>Reusable raw connector block buffer (mirror of <see cref="_scopeMechRaw"/>).</summary>
        private readonly List<IMyShipConnector> _scopeConnRaw = new List<IMyShipConnector>();

        /// <summary>Connector edges fed to <see cref="ScopeBuilder.BuildScope"/> for [Federate]-tagged docking admission.</summary>
        private readonly List<ConnectorEdge> _scopeConnBuf = new List<ConnectorEdge>();

        /// <summary>Snapshot of connector edges from the most recent scope build (mirror of <see cref="_scopeMechCache"/>).</summary>
        private readonly List<ConnectorEdge> _scopeConnCache = new List<ConnectorEdge>();

        /// <summary>Rolling hash of the scope inputs.</summary>
        private ulong _scopeDriftHash;

        /// <summary>Ticks elapsed since the last rescan.</summary>
        private int _ticksSinceRescan = int.MaxValue;

        /// <summary>Set by the <c>rescan</c> command to trigger an immediate rescan on the next cycle.</summary>
        private bool _rescanRequested = false;

        /// <summary>Reusable scratch list for in-scope text-surface providers (LCD discovery).</summary>
        private readonly List<IMyTextSurfaceProvider> _surfaceProviderScratch = new List<IMyTextSurfaceProvider>();

        /// <summary>Rebuilds <see cref="_scopeGrids"/> when due, including [Federate]-tagged connector edges when enabled.</summary>
        private IEnumerator<YieldReason> StepRebuildScopeIfDue()
        {
            bool needs = _rescanRequested
                      || _scopeGrids.Count == 0
                      || _ticksSinceRescan >= _config.RescanIntervalTicks;

            if (!needs && _scopeGrids.Count > 0)
            {
                ulong currentHash = ComputeLiveScopeDriftHash();
                if (currentHash != _scopeDriftHash)
                {
                    _logger.LogAction("Scope drift detected");
                    _rescanRequested = true;
                    needs = true;
                }
            }

            if (!needs)
            {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            RebuildScope();
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Walks live mechanical-connection blocks, projects them into POCOs, then runs <see cref="ScopeBuilder.BuildScope"/>.</summary>
        private void RebuildScope()
        {
            _scopeMechRaw.Clear();
            GridTerminalSystem.GetBlocksOfType(_scopeMechRaw, m => !m.Closed);
            _scopeMechBuf.Clear();
            for (int i = 0; i < _scopeMechRaw.Count; i++)
            {
                IMyMechanicalConnectionBlock m = _scopeMechRaw[i];
                MechanicalEdge edge;
                edge.BaseGridId = m.CubeGrid != null ? m.CubeGrid.EntityId : 0;
                edge.TopGridId = (m.IsAttached && m.TopGrid != null) ? m.TopGrid.EntityId : 0;
                edge.Attached = m.IsAttached;
                edge.NoSubgridTag = BlockNameTags.NameHasTag(m.CustomName, BlockNameTags.NoSubgridTag);
                _scopeMechBuf.Add(edge);
            }

            _scopeConnRaw.Clear();
            GridTerminalSystem.GetBlocksOfType(_scopeConnRaw, c => !c.Closed);
            _scopeConnBuf.Clear();
            for (int i = 0; i < _scopeConnRaw.Count; i++)
            {
                IMyShipConnector c = _scopeConnRaw[i];
                ConnectorEdge edge;
                edge.OwnerGridId = c.CubeGrid != null ? c.CubeGrid.EntityId : 0;
                IMyShipConnector other = c.OtherConnector;
                edge.OtherGridId = (other != null && other.CubeGrid != null) ? other.CubeGrid.EntityId : 0;
                edge.Connected = c.Status == MyShipConnectorStatus.Connected;
                edge.FederateTag = BlockNameTags.NameHasTag(c.CustomName, BlockNameTags.FederateTag);
                _scopeConnBuf.Add(edge);
            }

            ScopeBuilder.BuildScope(Me.CubeGrid.EntityId, _scopeMechBuf, _scopeConnBuf, _config.EnableConnectorFederation, _scopeGrids);

            _scopeMechCache.Clear();
            _scopeMechCache.AddRange(_scopeMechBuf);
            _scopeConnCache.Clear();
            _scopeConnCache.AddRange(_scopeConnBuf);
            _scopeDriftHash = ScopeBuilder.ComputeScopeDriftHash(_scopeMechCache, _scopeConnCache);
            _logger.LogActionOnce("scope:size:" + _scopeGrids.Count, "Scope: " + _scopeGrids.Count + " grid(s)");
        }

        /// <summary>Computes the scope drift hash from the live raw block list.</summary>
        private ulong ComputeLiveScopeDriftHash()
        {
            _scopeMechRaw.Clear();
            GridTerminalSystem.GetBlocksOfType(_scopeMechRaw, m => !m.Closed);
            _scopeMechBuf.Clear();
            for (int i = 0; i < _scopeMechRaw.Count; i++)
            {
                IMyMechanicalConnectionBlock m = _scopeMechRaw[i];
                MechanicalEdge edge;
                edge.BaseGridId = m.CubeGrid != null ? m.CubeGrid.EntityId : 0;
                edge.TopGridId = (m.IsAttached && m.TopGrid != null) ? m.TopGrid.EntityId : 0;
                edge.Attached = m.IsAttached;
                edge.NoSubgridTag = BlockNameTags.NameHasTag(m.CustomName, BlockNameTags.NoSubgridTag);
                _scopeMechBuf.Add(edge);
            }

            _scopeConnRaw.Clear();
            GridTerminalSystem.GetBlocksOfType(_scopeConnRaw, c => !c.Closed);
            _scopeConnBuf.Clear();
            for (int i = 0; i < _scopeConnRaw.Count; i++)
            {
                IMyShipConnector c = _scopeConnRaw[i];
                ConnectorEdge edge;
                edge.OwnerGridId = c.CubeGrid != null ? c.CubeGrid.EntityId : 0;
                IMyShipConnector other = c.OtherConnector;
                edge.OtherGridId = (other != null && other.CubeGrid != null) ? other.CubeGrid.EntityId : 0;
                edge.Connected = c.Status == MyShipConnectorStatus.Connected;
                edge.FederateTag = BlockNameTags.NameHasTag(c.CustomName, BlockNameTags.FederateTag);
                _scopeConnBuf.Add(edge);
            }

            return ScopeBuilder.ComputeScopeDriftHash(_scopeMechBuf, _scopeConnBuf);
        }

        /// <summary>Rescans blocks in scope and classifies them (assembler / <c>[CCraft]</c> LCD / <c>[CError]</c> LCD).</summary>
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

            _allManagedBlocks.Clear();
            _assemblers.Clear();
            _cargoContainers.Clear();
            _ccraftLcds.Clear();
            _cerrorLcds.Clear();

            GridTerminalSystem.GetBlocksOfType<IMyAssembler>(_assemblers, asm =>
                asm != null
                && !asm.Closed
                && asm.CubeGrid != null
                && _scopeGrids.Contains(asm.CubeGrid.EntityId)
                && !BlockNameTags.HasIgnoreTag(asm.CustomName));
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded())
            {
                yield return YieldReason.BudgetHit;
            }

            for (int i = 0; i < _assemblers.Count; i++)
            {
                _allManagedBlocks.Add(_assemblers[i]);
            }

            GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(_cargoContainers, cc =>
                cc != null
                && !cc.Closed
                && cc.CubeGrid != null
                && _scopeGrids.Contains(cc.CubeGrid.EntityId)
                && !BlockNameTags.HasIgnoreTag(cc.CustomName));
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded())
            {
                yield return YieldReason.BudgetHit;
            }

            _surfaceProviderScratch.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyTextSurfaceProvider>(_surfaceProviderScratch, b =>
            {
                var tb = b as IMyTerminalBlock;
                if (tb == null || tb.Closed || tb.CubeGrid == null)
                { return false; }
                if (!_scopeGrids.Contains(tb.CubeGrid.EntityId))
                { return false; }
                if (tb == Me)
                { return false; }
                if (BlockNameTags.HasIgnoreTag(tb.CustomName))
                { return false; }
                return BlockNameTags.NameHasTag(tb.CustomName, "[CCraft]")
                    || BlockNameTags.NameHasTag(tb.CustomName, "[CError]");
            });
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded())
            {
                yield return YieldReason.BudgetHit;
            }

            for (int i = 0; i < _surfaceProviderScratch.Count; i++)
            {
                IMyTextSurfaceProvider sp = _surfaceProviderScratch[i];
                if (sp.SurfaceCount <= 0)
                { continue; }
                var tb = (IMyTerminalBlock)sp;
                if (BlockNameTags.NameHasTag(tb.CustomName, "[CCraft]"))
                {
                    _ccraftLcds.Add(sp.GetSurface(0));
                    _allManagedBlocks.Add(tb);
                }
                else if (BlockNameTags.NameHasTag(tb.CustomName, "[CError]"))
                {
                    _cerrorLcds.Add(sp.GetSurface(0));
                    _allManagedBlocks.Add(tb);
                }
            }

            if (_assemblers.Count == 0)
            {
                _logger.LogWarningOnce("scope:no-assemblers",
                    "[Crane] No assemblers found in scope. Add assemblers to this grid (or to a federated grid via a [Federate]-tagged connector).");
            }

            _logger.LogAction("Rescan: " + _assemblers.Count + " asm, "
                + _cargoContainers.Count + " cargo, "
                + _ccraftLcds.Count + " [CCraft], " + _cerrorLcds.Count + " [CError]");

            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>The first <c>[CCraft]</c>-tagged terminal block found this scan (host of the quota INI sections).</summary>
        private IMyTerminalBlock _ccraftConfigHost;

        /// <summary>Locates the <c>[CCraft]</c>-tagged terminal block currently hosting the quota config CustomData. Returns null when no <c>[CCraft]</c> LCD is present.</summary>
        private IMyTerminalBlock FindCCraftConfigHost()
        {
            if (_ccraftLcds.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < _allManagedBlocks.Count; i++)
            {
                IMyTerminalBlock block = _allManagedBlocks[i];
                if (block == null || block.Closed)
                {
                    continue;
                }

                if (BlockNameTags.NameHasTag(block.CustomName, "[CCraft]"))
                {
                    return block;
                }
            }
            return null;
        }
    }
}
