using System.Collections.Generic;

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
                    h *= 1099511628211UL;
                }
            }
            return h;
        }
    }
}
