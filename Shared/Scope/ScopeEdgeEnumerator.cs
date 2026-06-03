using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>Projects live mechanical-connection and ship-connector blocks into the POCO edges fed to <see cref="ScopeBuilder"/>. Allocation-free when callers reuse the four supplied lists.</summary>
    public static class ScopeEdgeEnumerator
    {
        /// <summary>Enumerates every non-closed mechanical and connector block visible to <paramref name="gts"/> and writes their POCO projections into <paramref name="mechOut"/> and <paramref name="connOut"/>. All four input lists are cleared before refill.</summary>
        /// <param name="gts">Grid terminal system to enumerate from (typically <see cref="MyGridProgram.GridTerminalSystem"/>).</param>
        /// <param name="mechRawScratch">Reusable raw-mechanical-block buffer. Cleared, then populated with non-closed blocks.</param>
        /// <param name="connRawScratch">Reusable raw-connector buffer. Cleared, then populated with non-closed blocks.</param>
        /// <param name="mechOut">Output edges, one per raw mechanical block. Cleared first.</param>
        /// <param name="connOut">Output edges, one per raw connector. Cleared first.</param>
        public static void EnumerateLiveEdges(
            IMyGridTerminalSystem gts,
            List<IMyMechanicalConnectionBlock> mechRawScratch,
            List<IMyShipConnector> connRawScratch,
            List<MechanicalEdge> mechOut,
            List<ConnectorEdge> connOut)
        {
            mechRawScratch.Clear();
            gts.GetBlocksOfType(mechRawScratch, m => !m.Closed);
            mechOut.Clear();
            for (int i = 0; i < mechRawScratch.Count; i++)
            {
                IMyMechanicalConnectionBlock m = mechRawScratch[i];
                MechanicalEdge edge;
                edge.BaseGridId = m.CubeGrid != null ? m.CubeGrid.EntityId : 0;
                edge.TopGridId = (m.IsAttached && m.TopGrid != null) ? m.TopGrid.EntityId : 0;
                edge.Attached = m.IsAttached;
                edge.NoSubgridTag = BlockNameTags.NameHasTag(m.CustomName, BlockNameTags.NoSubgridTag);
                mechOut.Add(edge);
            }

            connRawScratch.Clear();
            gts.GetBlocksOfType(connRawScratch, c => !c.Closed);
            connOut.Clear();
            for (int i = 0; i < connRawScratch.Count; i++)
            {
                IMyShipConnector c = connRawScratch[i];
                ConnectorEdge edge;
                edge.OwnerGridId = c.CubeGrid != null ? c.CubeGrid.EntityId : 0;
                IMyShipConnector other = c.OtherConnector;
                edge.OtherGridId = (other != null && other.CubeGrid != null) ? other.CubeGrid.EntityId : 0;
                string otherName = other != null ? other.CustomName : null;
                edge.Connected = c.Status == MyShipConnectorStatus.Connected;
                edge.FederateTag = BlockNameTags.HasFederateTag(c.CustomName);
                edge.OtherFederateTag = BlockNameTags.HasFederateTag(otherName);
                edge.LocalPriority = BlockNameTags.ParseFederatePriority(c.CustomName);
                edge.OtherPriority = BlockNameTags.ParseFederatePriority(otherName);
                connOut.Add(edge);
            }
        }
    }
}
