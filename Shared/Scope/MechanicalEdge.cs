namespace IngameScript
{
    /// <summary>POCO projection of a mechanical-connection block's attachment state. Pure data; lets the BFS core be unit-tested without the SE runtime.</summary>
    public struct MechanicalEdge
    {
        /// <summary>EntityId of the grid hosting the base/stator side of this connection.</summary>
        public long BaseGridId;
        /// <summary>EntityId of the grid hosting the top side of this connection (0 when detached or unknown).</summary>
        public long TopGridId;
        /// <summary>True when the base is currently attached to its top.</summary>
        public bool Attached;
        /// <summary>True when the base block carries the <c>[NoSubgrid]</c> opt-out tag.</summary>
        public bool NoSubgridTag;
    }
}
