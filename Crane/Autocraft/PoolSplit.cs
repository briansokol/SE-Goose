namespace IngameScript
{
    /// <summary>Pure-static helper for splitting the assembler pool between Assemble and Disassemble work, with optional reservation of one assembler for Disassembly when both sides have demand.</summary>
    public static class PoolSplit
    {
        /// <summary>Computes proportional A/(A+D) split. When both pending counts are positive and the pool has &gt; 1 assembler, reserves one for Disassemble so an Assemble surge cannot starve the Disassemble side.</summary>
        /// <param name="poolSize">Total assembler count.</param>
        /// <param name="assemblePending">Number of distinct keys needing Assembly.</param>
        /// <param name="disassemblePending">Number of distinct keys needing Disassembly.</param>
        /// <param name="reserveEnabled">Caller's pre-check (typically <c>poolSize &gt; 1 &amp;&amp; both sides have work</c>).</param>
        /// <param name="assembleCount">Out: assemblers assigned to Assemble work.</param>
        /// <param name="disassembleCount">Out: assemblers assigned to Disassemble work.</param>
        /// <param name="reservedActive">Out: true when an assembler was reserved for Disassemble.</param>
        public static void ComputePoolSplitWithReservation(
            int poolSize,
            int assemblePending,
            int disassemblePending,
            bool reserveEnabled,
            out int assembleCount,
            out int disassembleCount,
            out bool reservedActive)
        {
            assembleCount = 0;
            disassembleCount = 0;
            reservedActive = false;
            if (poolSize <= 0)
            {
                return;
            }

            if (assemblePending <= 0 && disassemblePending <= 0)
            {
                return;
            }

            if (assemblePending <= 0)
            {
                disassembleCount = poolSize;
                return;
            }
            if (disassemblePending <= 0)
            {
                assembleCount = poolSize;
                return;
            }

            if (reserveEnabled && poolSize > 1)
            {
                reservedActive = true;
                int remaining = poolSize - 1;
                int total = assemblePending + disassemblePending;
                int aShare = (int)System.Math.Round((double)remaining * assemblePending / total, System.MidpointRounding.AwayFromZero);
                if (aShare < 1)
                {
                    aShare = 1;
                }

                if (aShare > remaining)
                {
                    aShare = remaining;
                }

                int dShareRemaining = remaining - aShare;
                assembleCount = aShare;
                disassembleCount = dShareRemaining + 1;
                return;
            }

            int totalNoReserve = assemblePending + disassemblePending;
            int aOnly = (int)System.Math.Round((double)poolSize * assemblePending / totalNoReserve, System.MidpointRounding.AwayFromZero);
            if (aOnly < 1)
            {
                aOnly = 1;
            }

            if (aOnly > poolSize - 1)
            {
                aOnly = poolSize - 1;
            }

            if (poolSize == 1)
            {
                assembleCount = 1;
                disassembleCount = 0;
                return;
            }
            assembleCount = aOnly;
            disassembleCount = poolSize - aOnly;
        }
    }
}
