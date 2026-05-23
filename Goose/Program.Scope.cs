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

        /// <summary>Reusable buffer for raw mechanical-connection block enumeration during <see cref="RebuildScope"/>.</summary>
        private readonly List<IMyMechanicalConnectionBlock> _scopeMechRaw = new List<IMyMechanicalConnectionBlock>();

        /// <summary>Reusable buffer for projected mechanical edges fed to <see cref="BuildScope"/>.</summary>
        private readonly List<MechanicalEdge> _scopeMechBuf = new List<MechanicalEdge>();

        /// <summary>Reusable buffer for projected connector edges fed to <see cref="BuildScope"/>.</summary>
        private readonly List<ConnectorEdge> _scopeConnBuf = new List<ConnectorEdge>();

        /// <summary>Reusable buffer for raw connector enumeration during <see cref="RebuildScope"/>. Holds every <see cref="IMyShipConnector"/> visible to GTS, not just in-scope ones — federation is what brings new connectors into scope.</summary>
        private readonly List<IMyShipConnector> _scopeConnRaw = new List<IMyShipConnector>();

        /// <summary>Snapshot of mechanical edges from the most recent <see cref="RebuildScope"/>; consulted by the drift check.</summary>
        private readonly List<MechanicalEdge> _scopeMechCache = new List<MechanicalEdge>();

        /// <summary>Snapshot of connector edges from the most recent <see cref="RebuildScope"/>; consulted by the drift check.</summary>
        private readonly List<ConnectorEdge> _scopeConnCache = new List<ConnectorEdge>();

        /// <summary>Rolling hash of (mechanical attach state + connector dock state + scope-affecting tags). Differs across ticks when scope inputs change; equal otherwise.</summary>
        private ulong _scopeDriftHash;

        /// <summary>Name-tag on a rotor/piston/hinge base that excludes its TopGrid (and everything past it) from scope.</summary>
        internal const string NoSubgridTag = "[NoSubgrid]";

        /// <summary>Name-tag on a PB-side connector that admits the currently-locked remote ship into scope.</summary>
        internal const string FederateTag = "[Federate]";

        /// <summary>POCO projection of <see cref="IMyMechanicalConnectionBlock"/> attachment state, used so the BFS core can be unit-tested without SE runtime.</summary>
        internal struct MechanicalEdge
        {
            /// <summary>EntityId of the grid hosting the base/stator side of this connection.</summary>
            public long BaseGridId;
            /// <summary>EntityId of the grid hosting the top side of this connection (0 when detached or not yet known).</summary>
            public long TopGridId;
            /// <summary>True when the base is currently attached to its top.</summary>
            public bool Attached;
            /// <summary>True when the base block carries the <c>[NoSubgrid]</c> opt-out tag. Honored starting with the no-subgrid-tag PR.</summary>
            public bool NoSubgridTag;
        }

        /// <summary>POCO projection of <see cref="IMyShipConnector"/> docking state, used so the BFS core can be unit-tested without SE runtime.</summary>
        internal struct ConnectorEdge
        {
            /// <summary>EntityId of the grid hosting the local side of this connector.</summary>
            public long OwnerGridId;
            /// <summary>EntityId of the grid hosting the remote (docked) connector (0 when undocked).</summary>
            public long OtherGridId;
            /// <summary>True when the connector pair is currently locked.</summary>
            public bool Connected;
            /// <summary>True when the local connector carries the <c>[Federate]</c> opt-in tag.</summary>
            public bool FederateTag;
        }

        /// <summary>Pure BFS that fills <paramref name="output"/> with every grid in scope. Admits the remote side of any [Federate]-tagged, locked connector on the root grid (single-hop), then walks mechanical edges from every admitted grid.</summary>
        /// <param name="rootGridId">EntityId of the seed grid (the PB's own grid in production).</param>
        /// <param name="mechEdges">All mechanical-connection edges in the visible grid system.</param>
        /// <param name="connEdges">All connector edges in the visible grid system. Only edges whose <see cref="ConnectorEdge.OwnerGridId"/> equals <paramref name="rootGridId"/> can extend scope (federation is non-transitive through further connectors).</param>
        /// <param name="enableFederation">Master kill-switch for connector federation. When false, all <see cref="ConnectorEdge"/> entries are ignored.</param>
        /// <param name="output">Set to populate. Cleared first.</param>
        internal static void BuildScope(
            long rootGridId,
            IList<MechanicalEdge> mechEdges,
            IList<ConnectorEdge> connEdges,
            bool enableFederation,
            HashSet<long> output)
        {
            output.Clear();
            output.Add(rootGridId);
            var frontier = new Queue<long>();
            frontier.Enqueue(rootGridId);

            if (enableFederation && connEdges != null)
            {
                for (int i = 0; i < connEdges.Count; i++)
                {
                    ConnectorEdge c = connEdges[i];
                    if (c.OwnerGridId != rootGridId)
                    {
                        continue;
                    }

                    if (!c.Connected)
                    {
                        continue;
                    }

                    if (!c.FederateTag)
                    {
                        continue;
                    }

                    if (c.OtherGridId == 0)
                    {
                        continue;
                    }

                    if (output.Add(c.OtherGridId))
                    {
                        frontier.Enqueue(c.OtherGridId);
                    }
                }
            }

            while (frontier.Count > 0)
            {
                long gridId = frontier.Dequeue();
                if (mechEdges != null)
                {
                    for (int i = 0; i < mechEdges.Count; i++)
                    {
                        MechanicalEdge e = mechEdges[i];
                        if (e.BaseGridId != gridId)
                        {
                            continue;
                        }

                        if (!e.Attached)
                        {
                            continue;
                        }

                        if (e.NoSubgridTag)
                        {
                            continue;
                        }

                        if (e.TopGridId == 0)
                        {
                            continue;
                        }

                        if (output.Add(e.TopGridId))
                        {
                            frontier.Enqueue(e.TopGridId);
                        }
                    }
                }
            }
        }

        /// <summary>Cheap rolling hash over mechanical and connector edges. Differs whenever any input that affects scope changes.</summary>
        /// <param name="mech">Mechanical edges to mix into the hash.</param>
        /// <param name="conn">Connector edges to mix into the hash.</param>
        internal static ulong ComputeScopeDriftHash(IList<MechanicalEdge> mech, IList<ConnectorEdge> conn)
        {
            ulong h = 1469598103934665603UL;
            if (mech != null)
            {
                for (int i = 0; i < mech.Count; i++)
                {
                    MechanicalEdge e = mech[i];
                    h ^= (ulong)e.BaseGridId;
                    h ^= ((ulong)e.TopGridId) << 1;
                    h ^= e.Attached ? 0x1UL : 0x0UL;
                    h ^= e.NoSubgridTag ? 0x2UL : 0x0UL;
                    h *= 1099511628211UL;
                }
            }
            if (conn != null)
            {
                for (int i = 0; i < conn.Count; i++)
                {
                    ConnectorEdge c = conn[i];
                    h ^= (ulong)c.OwnerGridId;
                    h ^= ((ulong)c.OtherGridId) << 1;
                    h ^= c.Connected ? 0x4UL : 0x0UL;
                    h ^= c.FederateTag ? 0x8UL : 0x0UL;
                    h *= 1099511628211UL;
                }
            }
            return h;
        }


        /// <summary>Computes the scope drift hash directly from the live raw block lists, avoiding a full edge-cache rebuild on stable ticks.</summary>
        /// <remarks>The bit composition must remain identical to <see cref="ComputeScopeDriftHash"/> so the two hashes are interchangeable.</remarks>
        private ulong ComputeScopeDriftHashFromRaw()
        {
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < _scopeMechRaw.Count; i++)
            {
                IMyMechanicalConnectionBlock m = _scopeMechRaw[i];
                if (m.Closed)
                {
                    continue;
                }

                long baseId = m.CubeGrid != null ? m.CubeGrid.EntityId : 0;
                long topId = (m.IsAttached && m.TopGrid != null) ? m.TopGrid.EntityId : 0;
                h ^= (ulong)baseId;
                h ^= ((ulong)topId) << 1;
                h ^= m.IsAttached ? 0x1UL : 0x0UL;
                h ^= NameHasTag(m.CustomName, NoSubgridTag) ? 0x2UL : 0x0UL;
                h *= 1099511628211UL;
            }
            for (int i = 0; i < _scopeConnRaw.Count; i++)
            {
                IMyShipConnector c = _scopeConnRaw[i];
                if (c.Closed)
                {
                    continue;
                }

                long ownerId = c.CubeGrid != null ? c.CubeGrid.EntityId : 0;
                IMyShipConnector other = c.OtherConnector;
                long otherId = (other != null && other.CubeGrid != null) ? other.CubeGrid.EntityId : 0;
                h ^= (ulong)ownerId;
                h ^= ((ulong)otherId) << 1;
                h ^= c.Status == MyShipConnectorStatus.Connected ? 0x4UL : 0x0UL;
                h ^= NameHasTag(c.CustomName, FederateTag) ? 0x8UL : 0x0UL;
                h *= 1099511628211UL;
            }
            return h;
        }

        /// <summary>Projects live mechanical-connection blocks into POCOs and runs <see cref="BuildScope"/> to refresh <see cref="_scopeGrids"/>.</summary>
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
                edge.NoSubgridTag = NameHasTag(m.CustomName, NoSubgridTag);
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
                edge.FederateTag = NameHasTag(c.CustomName, FederateTag);
                _scopeConnBuf.Add(edge);
            }

            BuildScope(Me.CubeGrid.EntityId, _scopeMechBuf, _scopeConnBuf, _config.EnableConnectorFederation, _scopeGrids);

            _scopeMechCache.Clear();
            _scopeMechCache.AddRange(_scopeMechBuf);
            _scopeConnCache.Clear();
            _scopeConnCache.AddRange(_scopeConnBuf);
            _scopeDriftHash = ComputeScopeDriftHash(_scopeMechCache, _scopeConnCache);

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
                ulong currentHash = ComputeScopeDriftHashFromRaw();
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
