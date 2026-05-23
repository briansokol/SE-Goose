namespace IngameScript
{
    /// <summary>POCO projection of a ship connector's docking state. Pure data; lets the BFS core be unit-tested without the SE runtime.</summary>
    public struct ConnectorEdge
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
}
