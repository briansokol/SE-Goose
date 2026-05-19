namespace IngameScript {
    /// <summary>Pure-static deficit-fanout helper for multi-assembler dispatch. Splits a per-item deficit across N capable assemblers in <c>ceil(D/N)</c> shares, last absorbs remainder.</summary>
    public static class DeficitSplit {
        /// <summary>Splits <paramref name="deficit"/> across <paramref name="capableCount"/> assemblers. If <paramref name="deficit"/> &lt; <paramref name="minBatch"/>, returns a single share. Otherwise returns ceil(D/N) per asm, last entry absorbs remainder.</summary>
        /// <param name="deficit">Total amount to split. Non-positive returns empty.</param>
        /// <param name="capableCount">Number of assemblers capable of producing the item.</param>
        /// <param name="minBatch">Threshold below which the entire deficit goes to a single assembler.</param>
        /// <returns>Per-assembler share array (length 0 when capableCount==0).</returns>
        public static long[] SplitDeficit(long deficit, int capableCount, int minBatch) {
            if (capableCount <= 0 || deficit <= 0) return new long[0];
            if (capableCount == 1) return new long[] { deficit };
            if (deficit < minBatch) return new long[] { deficit };
            long share = (deficit + capableCount - 1) / capableCount;
            long[] result = new long[capableCount];
            long assigned = 0;
            for (int i = 0; i < capableCount - 1; i++) {
                result[i] = share;
                assigned += share;
            }
            result[capableCount - 1] = deficit - assigned;
            if (result[capableCount - 1] < 0) result[capableCount - 1] = 0;
            return result;
        }
    }
}
