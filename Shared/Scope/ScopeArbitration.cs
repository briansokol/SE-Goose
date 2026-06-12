using System.Collections.Generic;

namespace IngameScript
{
    /// <summary>A Goose instance heard over IGC: its PB identity, construct signature, and the grids its construct covers.</summary>
    public class FederationPeer
    {
        /// <summary>EntityId of the peer's programmable block. Identifies the instance and filters self-echo.</summary>
        public long PbId;
        /// <summary>The peer's mechanical-only construct signature (see <see cref="ScopeBuilder.ComputeConstructSignature"/>).</summary>
        public ulong Signature;
        /// <summary>EntityIds of the grids in the peer's construct. Used to tell whether a docked grid is already managed by a peer Goose.</summary>
        public LongSet ConstructGrids;
    }

    /// <summary>Outcome of multi-Goose arbitration for the local instance this tick.</summary>
    public class ArbitrationResult
    {
        /// <summary>True when another Goose reports the identical construct signature (accidental same-grid duplicate). Both instances halt.</summary>
        public bool DuplicateScope;
        /// <summary>True when a directly federated peer Goose outranks this instance; the local instance enters full standby.</summary>
        public bool StandDown;
        /// <summary>Remote grids this instance is cleared to federate into scope this tick. Empty when standing down.</summary>
        public LongSet ApprovedFederateGrids = new LongSet();
    }

    /// <summary>Pure multi-Goose deference logic. Decides, from local connector state and the IGC peer table, whether the local instance halts (duplicate), stands down (outranked), and which peer grids it may federate.</summary>
    public static class ScopeArbitration
    {
        /// <summary>Runs arbitration for the local instance.</summary>
        /// <param name="rootGridId">EntityId of the local PB's grid.</param>
        /// <param name="myPbId">EntityId of the local PB (filters self-echo and identifies the instance).</param>
        /// <param name="mySignature">Local mechanical-only construct signature.</param>
        /// <param name="connEdges">All connector edges; only those owned by <paramref name="rootGridId"/> are considered.</param>
        /// <param name="peers">Live Goose peers heard over IGC.</param>
        /// <returns>The duplicate/standby decision and the approved federation grid set.</returns>
        public static ArbitrationResult Decide(
            long rootGridId,
            long myPbId,
            ulong mySignature,
            IList<ConnectorEdge> connEdges,
            IList<FederationPeer> peers)
        {
            var result = new ArbitrationResult();

            if (peers != null)
            {
                for (int i = 0; i < peers.Count; i++)
                {
                    FederationPeer p = peers[i];
                    if (p == null || p.PbId == myPbId)
                    {
                        continue;
                    }

                    if (p.Signature == mySignature)
                    {
                        result.DuplicateScope = true;
                        break;
                    }
                }
            }

            if (connEdges != null)
            {
                for (int i = 0; i < connEdges.Count; i++)
                {
                    ConnectorEdge c = connEdges[i];
                    if (c.OwnerGridId != rootGridId || !c.Connected || c.OtherGridId == 0)
                    {
                        continue;
                    }

                    if (!c.FederateTag || !c.OtherFederateTag)
                    {
                        continue;
                    }

                    if (!PeerCovers(peers, myPbId, c.OtherGridId))
                    {
                        result.ApprovedFederateGrids.Add(c.OtherGridId);
                        continue;
                    }

                    if (c.OtherPriority < c.LocalPriority)
                    {
                        result.StandDown = true;
                    }
                    else if (c.OtherPriority > c.LocalPriority)
                    {
                        result.ApprovedFederateGrids.Add(c.OtherGridId);
                    }
                }
            }

            if (result.StandDown)
            {
                result.ApprovedFederateGrids.Clear();
            }

            return result;
        }

        /// <summary>True when any peer Goose (other than the local instance) reports a construct containing <paramref name="gridId"/>.</summary>
        private static bool PeerCovers(IList<FederationPeer> peers, long myPbId, long gridId)
        {
            if (peers == null)
            {
                return false;
            }

            for (int i = 0; i < peers.Count; i++)
            {
                FederationPeer p = peers[i];
                if (p == null || p.PbId == myPbId || p.ConstructGrids == null)
                {
                    continue;
                }

                if (p.ConstructGrids.Contains(gridId))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
