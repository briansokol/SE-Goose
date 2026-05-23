namespace IngameScript
{
    /// <summary>Why a pipeline step yielded back to the dispatcher.</summary>
    public enum YieldReason
    {
        /// <summary>Per-tick instruction budget was hit; resume next tick.</summary>
        BudgetHit,
        /// <summary>Logical chunk completed; resume next tick.</summary>
        ChunkBoundary,
        /// <summary>Step is waiting on external state (reserved for future use).</summary>
        ExternalWait
    }
}
