using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>EntityIds of grids currently in management scope. Recomputed by <see cref="StepRebuildScopeIfDue"/>; consulted by <see cref="IsManaged"/> and <see cref="ValidateBlock"/>.</summary>
        private readonly HashSet<long> _scopeGrids = new HashSet<long>();

        /// <summary>Reusable buffer for raw mechanical-connection block enumeration during scope rebuilds and drift checks.</summary>
        private readonly List<IMyMechanicalConnectionBlock> _scopeMechRaw = new List<IMyMechanicalConnectionBlock>();

        /// <summary>Reusable buffer for raw connector enumeration during scope rebuilds and drift checks. Holds every <see cref="IMyShipConnector"/> visible to GTS, not just in-scope ones; federation is what brings new connectors into scope.</summary>
        private readonly List<IMyShipConnector> _scopeConnRaw = new List<IMyShipConnector>();

        /// <summary>Reusable buffer for projected mechanical edges fed to <see cref="ScopeBuilder.BuildScope"/>.</summary>
        private readonly List<MechanicalEdge> _scopeMechBuf = new List<MechanicalEdge>();

        /// <summary>Reusable buffer for projected connector edges fed to <see cref="ScopeBuilder.BuildScope"/>.</summary>
        private readonly List<ConnectorEdge> _scopeConnBuf = new List<ConnectorEdge>();

        /// <summary>Rolling hash of (mechanical attach state + connector dock state + scope-affecting tags). Differs across ticks when scope inputs change; equal otherwise.</summary>
        private ulong _scopeDriftHash;

        /// <summary>EntityIds of the configured block group's member blocks. Empty unless <see cref="_groupModeActive"/> is true; populated during <see cref="RebuildScope"/>.</summary>
        private readonly HashSet<long> _groupBlockIds = new HashSet<long>();

        /// <summary>Reusable buffer for group-member enumeration during scope rebuilds.</summary>
        private readonly List<IMyTerminalBlock> _groupBlockScratch = new List<IMyTerminalBlock>();

        /// <summary>True when <see cref="GooseConfig.BlockGroup"/> is non-empty. When true, <see cref="_groupBlockIds"/> is the sole authority for membership (an empty set means nothing is managed).</summary>
        private bool _groupModeActive = false;

        /// <summary>Grid-based discovery gate: a block is in scope when it sits on a grid reachable from the PB. Independent of any configured block group; the group only limits managed targets via <see cref="IsInGroup"/>.</summary>
        /// <param name="block">Candidate block.</param>
        /// <returns>True when the block is in management scope.</returns>
        private bool IsInScope(IMyTerminalBlock block)
        {
            return block != null
                && block.CubeGrid != null
                && _scopeGrids.Contains(block.CubeGrid.EntityId);
        }

        /// <summary>True when a block may be used as a managed destination/target: every in-scope block when no group is configured, or only group members when one is.</summary>
        /// <param name="block">Candidate block.</param>
        /// <returns>True when the block is an eligible managed target.</returns>
        private bool IsInGroup(IMyTerminalBlock block)
        {
            return block != null
                && ScopeBuilder.IsManagedTarget(_groupModeActive, _groupBlockIds, block.EntityId);
        }

        /// <summary>Projects live mechanical-connection and connector blocks via the shared enumerator, then refreshes <see cref="_scopeGrids"/> through <see cref="ScopeBuilder.BuildScope"/>. When <see cref="GooseConfig.BlockGroup"/> is set, also resolves the named group into <see cref="_groupBlockIds"/>.</summary>
        private void RebuildScope()
        {
            ScopeEdgeEnumerator.EnumerateLiveEdges(GridTerminalSystem, _scopeMechRaw, _scopeConnRaw, _scopeMechBuf, _scopeConnBuf);

            if (_federationArbitrationActive)
            {
                ScopeBuilder.BuildScope(Me.CubeGrid.EntityId, _scopeMechBuf, _approvedFederateGrids, _scopeGrids);
            }
            else
            {
                ScopeBuilder.BuildScope(Me.CubeGrid.EntityId, _scopeMechBuf, _scopeConnBuf, _config.EnableConnectorFederation, _scopeGrids);
            }

            _groupBlockIds.Clear();
            _groupModeActive = false;
            string groupName = _config.BlockGroup;
            if (!string.IsNullOrEmpty(groupName))
            {
                _groupModeActive = true;
                IMyBlockGroup group = GridTerminalSystem.GetBlockGroupWithName(groupName);
                if (group == null)
                {
                    LogWarningOnce("scope:group-missing:" + groupName,
                        "[Goose] Block group '" + groupName + "' not found. Managing nothing until it exists. Check the blockGroup name in CustomData.");
                }
                else
                {
                    _groupBlockScratch.Clear();
                    group.GetBlocks(_groupBlockScratch);
                    for (int i = 0; i < _groupBlockScratch.Count; i++)
                    {
                        IMyTerminalBlock b = _groupBlockScratch[i];
                        if (b != null && !b.Closed)
                        {
                            _groupBlockIds.Add(b.EntityId);
                        }
                    }
                }
            }

            _scopeDriftHash = ScopeBuilder.ComputeScopeDriftHash(_scopeMechBuf, _scopeConnBuf);

            if (_groupModeActive)
            {
                LogActionOnce("scope:group:" + groupName + ":" + _groupBlockIds.Count,
                    "Scope: group '" + groupName + "', " + _groupBlockIds.Count + " block(s)");
            }
            else
            {
                LogActionOnce("scope:size:" + _scopeGrids.Count, "Scope: " + _scopeGrids.Count + " grid(s)");
            }
        }

        /// <summary>Rebuilds <see cref="_scopeGrids"/> when a rescan is pending, on first run, or once the rescan interval elapses; otherwise yields cheaply.</summary>
        private IEnumerator<YieldReason> StepRebuildScopeIfDue()
        {
            bool needs = _rescanRequested
                      || _scopeGrids.Count == 0
                      || (_groupModeActive && _groupBlockIds.Count == 0)
                      || _ticksSinceRescan >= _config.RescanIntervalTicks;

            if (!needs && _scopeGrids.Count > 0)
            {
                _scopeMechRaw.Clear();
                GridTerminalSystem.GetBlocksOfType(_scopeMechRaw, m => !m.Closed);
                _scopeConnRaw.Clear();
                GridTerminalSystem.GetBlocksOfType(_scopeConnRaw, c => !c.Closed);
                ulong currentHash = ScopeBuilder.ComputeScopeDriftHashFromRaw(_scopeMechRaw, _scopeConnRaw);
                if (currentHash != _scopeDriftHash)
                {
                    LogAction("Scope drift detected");
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
    }
}
