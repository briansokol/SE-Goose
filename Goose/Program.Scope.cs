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

        /// <summary>Projects live mechanical-connection and connector blocks via the shared enumerator, then refreshes <see cref="_scopeGrids"/> through <see cref="ScopeBuilder.BuildScope"/>.</summary>
        private void RebuildScope()
        {
            ScopeEdgeEnumerator.EnumerateLiveEdges(GridTerminalSystem, _scopeMechRaw, _scopeConnRaw, _scopeMechBuf, _scopeConnBuf);

            ScopeBuilder.BuildScope(Me.CubeGrid.EntityId, _scopeMechBuf, _scopeConnBuf, _config.EnableConnectorFederation, _scopeGrids);

            _scopeDriftHash = ScopeBuilder.ComputeScopeDriftHash(_scopeMechBuf, _scopeConnBuf);

            LogActionOnce("scope:size:" + _scopeGrids.Count, "Scope: " + _scopeGrids.Count + " grid(s)");
        }

        /// <summary>Rebuilds <see cref="_scopeGrids"/> when a rescan is pending, on first run, or once the rescan interval elapses; otherwise yields cheaply.</summary>
        private IEnumerator<YieldReason> StepRebuildScopeIfDue()
        {
            bool needs = _rescanRequested
                      || _scopeGrids.Count == 0
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
