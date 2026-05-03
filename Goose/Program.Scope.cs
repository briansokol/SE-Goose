using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript {
    public partial class Program : MyGridProgram {
        /// <summary>EntityIds of grids currently in management scope. Recomputed by <see cref="StepRebuildScopeIfDue"/>; consulted by <see cref="IsManaged"/> and <see cref="ValidateBlock"/>.</summary>
        readonly HashSet<long> _scopeGrids = new HashSet<long>();

        /// <summary>Reusable buffer for raw mechanical-connection block enumeration during <see cref="RebuildScope"/>.</summary>
        readonly List<IMyMechanicalConnectionBlock> _scopeMechRaw = new List<IMyMechanicalConnectionBlock>();

        /// <summary>Reusable buffer for projected mechanical edges fed to <see cref="BuildScope"/>.</summary>
        readonly List<MechanicalEdge> _scopeMechBuf = new List<MechanicalEdge>();

        /// <summary>Reusable buffer for projected connector edges fed to <see cref="BuildScope"/>. Empty until connector federation lands in a later PR.</summary>
        readonly List<ConnectorEdge> _scopeConnBuf = new List<ConnectorEdge>();

        /// <summary>Name-tag on a rotor/piston/hinge base that excludes its TopGrid (and everything past it) from scope.</summary>
        internal const string NoSubgridTag = "[NoSubgrid]";

        /// <summary>POCO projection of <see cref="IMyMechanicalConnectionBlock"/> attachment state, used so the BFS core can be unit-tested without SE runtime.</summary>
        internal struct MechanicalEdge {
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
        internal struct ConnectorEdge {
            /// <summary>EntityId of the grid hosting the local side of this connector.</summary>
            public long OwnerGridId;
            /// <summary>EntityId of the grid hosting the remote (docked) connector (0 when undocked).</summary>
            public long OtherGridId;
            /// <summary>True when the connector pair is currently locked.</summary>
            public bool Connected;
            /// <summary>True when the local connector carries the <c>[Federate]</c> opt-in tag. Honored starting with the connector-federation PR.</summary>
            public bool FederateTag;
        }

        /// <summary>Pure BFS that fills <paramref name="output"/> with every grid in scope. Walks mechanical edges from any scoped grid; connector edges and tag opt-out land in later PRs.</summary>
        /// <param name="rootGridId">EntityId of the seed grid (the PB's own grid in production).</param>
        /// <param name="mechEdges">All mechanical-connection edges in the visible grid system.</param>
        /// <param name="connEdges">All connector edges in the visible grid system. Reserved for the connector-federation PR; ignored here.</param>
        /// <param name="enableFederation">Master kill-switch for connector federation. Reserved for the connector-federation PR.</param>
        /// <param name="output">Set to populate. Cleared first.</param>
        internal static void BuildScope(
            long rootGridId,
            IList<MechanicalEdge> mechEdges,
            IList<ConnectorEdge> connEdges,
            bool enableFederation,
            HashSet<long> output) {
            output.Clear();
            output.Add(rootGridId);
            Queue<long> frontier = new Queue<long>();
            frontier.Enqueue(rootGridId);
            while (frontier.Count > 0) {
                long gridId = frontier.Dequeue();
                if (mechEdges != null) {
                    for (int i = 0; i < mechEdges.Count; i++) {
                        MechanicalEdge e = mechEdges[i];
                        if (e.BaseGridId != gridId) continue;
                        if (!e.Attached) continue;
                        if (e.NoSubgridTag) continue;
                        if (e.TopGridId == 0) continue;
                        if (output.Add(e.TopGridId)) frontier.Enqueue(e.TopGridId);
                    }
                }
            }
        }

        /// <summary>Projects live mechanical-connection blocks into POCOs and runs <see cref="BuildScope"/> to refresh <see cref="_scopeGrids"/>.</summary>
        void RebuildScope() {
            _scopeMechRaw.Clear();
            GridTerminalSystem.GetBlocksOfType(_scopeMechRaw, m => !m.Closed);
            _scopeMechBuf.Clear();
            for (int i = 0; i < _scopeMechRaw.Count; i++) {
                IMyMechanicalConnectionBlock m = _scopeMechRaw[i];
                MechanicalEdge edge;
                edge.BaseGridId = m.CubeGrid != null ? m.CubeGrid.EntityId : 0;
                edge.TopGridId = (m.IsAttached && m.TopGrid != null) ? m.TopGrid.EntityId : 0;
                edge.Attached = m.IsAttached;
                edge.NoSubgridTag = NameHasTag(m.CustomName, NoSubgridTag);
                _scopeMechBuf.Add(edge);
            }
            _scopeConnBuf.Clear();
            BuildScope(Me.CubeGrid.EntityId, _scopeMechBuf, _scopeConnBuf, false, _scopeGrids);
        }

        /// <summary>Rebuilds <see cref="_scopeGrids"/> when a rescan is pending, on first run, or once the rescan interval elapses; otherwise yields cheaply.</summary>
        IEnumerator<YieldReason> StepRebuildScopeIfDue() {
            bool needs = _rescanRequested
                      || _scopeGrids.Count == 0
                      || _ticksSinceRescan >= _config.RescanIntervalTicks;
            if (!needs) {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            RebuildScope();
            yield return YieldReason.ChunkBoundary;
        }
    }
}
