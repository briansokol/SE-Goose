namespace IngameScript {
    /// <summary>How an item's stock-quota <c>Amount</c> should be interpreted.</summary>
    public enum QuotaMode {
        /// <summary>Pull up to, and push excess above, the target amount.</summary>
        Exact,
        /// <summary>Pull up to the target amount; never push.</summary>
        Minimum,
        /// <summary>Push excess above the target amount; never pull.</summary>
        Limiter,
        /// <summary>Pull all available stock without an upper bound.</summary>
        All
    }

    /// <summary>A single stock-quota rule parsed from a container's CustomData.</summary>
    public class StockQuota {
        /// <summary>Target item count (ignored when <see cref="Mode"/> is <see cref="QuotaMode.All"/>).</summary>
        public long Amount;

        /// <summary>How <see cref="Amount"/> is enforced.</summary>
        public QuotaMode Mode;
    }
}
