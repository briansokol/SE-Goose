using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>Pure BFS scope builder. Walks mechanical edges from a root grid; admits the remote side of any locked <c>[Federate]</c> connector on the root.</summary>
    public static class ScopeBuilder
    {
        /// <summary>Fills <paramref name="output"/> with every grid in scope. Single-hop federation: connectors only extend scope when their <see cref="ConnectorEdge.OwnerGridId"/> equals <paramref name="rootGridId"/>.</summary>
        /// <param name="rootGridId">EntityId of the seed grid.</param>
        /// <param name="mechEdges">All mechanical-connection edges in the visible grid system.</param>
        /// <param name="connEdges">All connector edges in the visible grid system.</param>
        /// <param name="enableFederation">Master kill-switch for connector federation.</param>
        /// <param name="output">Set to populate. Cleared first.</param>
        public static void BuildScope(
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
                    if (c.OwnerGridId != rootGridId || !c.Connected || !c.FederateTag || c.OtherGridId == 0)
                    {
                        continue;
                    }

                    if (output.Add(c.OtherGridId))
                    {
                        frontier.Enqueue(c.OtherGridId);
                    }
                }
            }

            TraverseMechFrontier(frontier, mechEdges, output);
        }

        /// <summary>Builds scope from a pre-resolved set of approved federation grids (the output of <see cref="ScopeArbitration"/>), rather than re-deriving federation from connector tags. Seeds the BFS with the root plus every approved grid, then walks mechanical edges so each approved grid's subgrids are included too.</summary>
        /// <param name="rootGridId">EntityId of the seed grid.</param>
        /// <param name="mechEdges">All mechanical-connection edges in the visible grid system.</param>
        /// <param name="approvedFederateGrids">Remote grids cleared to federate this tick; <c>null</c> or empty means no federation.</param>
        /// <param name="output">Set to populate. Cleared first.</param>
        public static void BuildScope(
            long rootGridId,
            IList<MechanicalEdge> mechEdges,
            HashSet<long> approvedFederateGrids,
            HashSet<long> output)
        {
            output.Clear();
            output.Add(rootGridId);
            var frontier = new Queue<long>();
            frontier.Enqueue(rootGridId);

            if (approvedFederateGrids != null)
            {
                foreach (long g in approvedFederateGrids)
                {
                    if (g != 0 && output.Add(g))
                    {
                        frontier.Enqueue(g);
                    }
                }
            }

            TraverseMechFrontier(frontier, mechEdges, output);
        }

        /// <summary>Breadth-first walk of mechanical edges from a seeded frontier, adding every reachable subgrid to the scope.</summary>
        /// <param name="frontier">Queue pre-seeded with the root (and any federated) grid ids.</param>
        /// <param name="mechEdges">Mechanical edges to traverse; null is treated as no edges.</param>
        /// <param name="output">Scope set that accumulates reachable grid ids.</param>
        private static void TraverseMechFrontier(Queue<long> frontier, IList<MechanicalEdge> mechEdges, HashSet<long> output)
        {
            while (frontier.Count > 0)
            {
                long gridId = frontier.Dequeue();
                if (mechEdges == null)
                {
                    continue;
                }

                for (int i = 0; i < mechEdges.Count; i++)
                {
                    MechanicalEdge e = mechEdges[i];
                    if (e.BaseGridId != gridId || !e.Attached || e.NoSubgridTag || e.TopGridId == 0)
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

        /// <summary>Pure membership decision shared by group and grid scope modes. In group mode a block is in scope only when it is a named group member; otherwise membership falls back to grid scope.</summary>
        /// <param name="groupModeActive">True when a non-empty group name resolved to an existing group.</param>
        /// <param name="groupBlockIds">EntityIds of the resolved group's member blocks; consulted only in group mode.</param>
        /// <param name="scopeGrids">EntityIds of grids in grid-based scope; consulted only outside group mode.</param>
        /// <param name="blockEntityId">EntityId of the candidate block.</param>
        /// <param name="blockGridId">EntityId of the candidate block's grid.</param>
        /// <returns>True when the block is in management scope under the active mode.</returns>
        public static bool IsBlockInScope(
            bool groupModeActive,
            HashSet<long> groupBlockIds,
            HashSet<long> scopeGrids,
            long blockEntityId,
            long blockGridId)
        {
            if (groupModeActive)
            {
                return groupBlockIds != null && groupBlockIds.Contains(blockEntityId);
            }

            return scopeGrids != null && scopeGrids.Contains(blockGridId);
        }

        /// <summary>True when a block is an eligible managed target: every block when no group is configured, or only group members when one is. Grid filtering is handled upstream by discovery.</summary>
        /// <param name="groupModeActive">True when a block group is configured.</param>
        /// <param name="groupBlockIds">EntityIds of the configured group's member blocks; consulted only in group mode.</param>
        /// <param name="blockEntityId">EntityId of the candidate block.</param>
        /// <returns>True when the block may be used as a managed destination/target.</returns>
        public static bool IsManagedTarget(bool groupModeActive, HashSet<long> groupBlockIds, long blockEntityId)
        {
            return !groupModeActive || (groupBlockIds != null && groupBlockIds.Contains(blockEntityId));
        }

        /// <summary>Order-independent FNV-1a hash over a construct's grid EntityIds. Two PBs on the same physical construct produce an identical signature; used for same-scope duplicate detection. Pass the mechanical-only grid set (no connector federation) so mutually federated grids never collide.</summary>
        /// <param name="gridIds">EntityIds of every grid in the construct.</param>
        /// <returns>A stable signature for the set, independent of enumeration order.</returns>
        public static ulong ComputeConstructSignature(IEnumerable<long> gridIds)
        {
            var ids = new List<long>();
            if (gridIds != null)
            {
                foreach (long id in gridIds)
                {
                    ids.Add(id);
                }
            }

            ids.Sort();
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < ids.Count; i++)
            {
                h ^= (ulong)ids[i];
                h *= 1099511628211UL;
            }
            return h;
        }

        /// <summary>Cheap rolling hash over mechanical and connector edges. Differs whenever any scope input changes.</summary>
        public static ulong ComputeScopeDriftHash(IList<MechanicalEdge> mech, IList<ConnectorEdge> conn)
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
                    h ^= c.OtherFederateTag ? 0x10UL : 0x0UL;
                    h ^= ((ulong)(uint)c.LocalPriority) << 16;
                    h ^= ((ulong)(uint)c.OtherPriority) << 24;
                    h *= 1099511628211UL;
                }
            }
            return h;
        }

        /// <summary>Drift-hash fast-path: hashes raw live blocks directly without projecting to POCO edges. Bit composition matches <see cref="ComputeScopeDriftHash(IList{MechanicalEdge}, IList{ConnectorEdge})"/> so both hashes are interchangeable on identical inputs.</summary>
        public static ulong ComputeScopeDriftHashFromRaw(
            IList<IMyMechanicalConnectionBlock> mech,
            IList<IMyShipConnector> conn)
        {
            ulong h = 1469598103934665603UL;
            if (mech != null)
            {
                for (int i = 0; i < mech.Count; i++)
                {
                    IMyMechanicalConnectionBlock m = mech[i];
                    if (m.Closed)
                    {
                        continue;
                    }

                    long baseId = m.CubeGrid != null ? m.CubeGrid.EntityId : 0;
                    long topId = (m.IsAttached && m.TopGrid != null) ? m.TopGrid.EntityId : 0;
                    h ^= (ulong)baseId;
                    h ^= ((ulong)topId) << 1;
                    h ^= m.IsAttached ? 0x1UL : 0x0UL;
                    h ^= BlockNameTags.NameHasTag(m.CustomName, BlockNameTags.NoSubgridTag) ? 0x2UL : 0x0UL;
                    h *= 1099511628211UL;
                }
            }
            if (conn != null)
            {
                for (int i = 0; i < conn.Count; i++)
                {
                    IMyShipConnector c = conn[i];
                    if (c.Closed)
                    {
                        continue;
                    }

                    long ownerId = c.CubeGrid != null ? c.CubeGrid.EntityId : 0;
                    IMyShipConnector other = c.OtherConnector;
                    long otherId = (other != null && other.CubeGrid != null) ? other.CubeGrid.EntityId : 0;
                    string otherName = other != null ? other.CustomName : null;
                    h ^= (ulong)ownerId;
                    h ^= ((ulong)otherId) << 1;
                    h ^= c.Status == MyShipConnectorStatus.Connected ? 0x4UL : 0x0UL;
                    h ^= BlockNameTags.HasFederateTag(c.CustomName) ? 0x8UL : 0x0UL;
                    h ^= BlockNameTags.HasFederateTag(otherName) ? 0x10UL : 0x0UL;
                    h ^= ((ulong)(uint)BlockNameTags.ParseFederatePriority(c.CustomName)) << 16;
                    h ^= ((ulong)(uint)BlockNameTags.ParseFederatePriority(otherName)) << 24;
                    h *= 1099511628211UL;
                }
            }
            return h;
        }
    }
}
